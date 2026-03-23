using ImageServer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace FfmpegMediaPlatform
{
    public class FfmpegRtmpFrameSource : FfmpegFrameSource, ICaptureFrameSource
    {
        private readonly bool listenMode;
        private byte[] idleFrame;
        private byte[] lastFrame;
        private DateTime lastFrameTime = DateTime.MinValue;
        private static readonly TimeSpan FrameHoldDuration = TimeSpan.FromSeconds(5);

        public FfmpegRtmpFrameSource(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig)
            : base(ffmpegMediaFramework, videoConfig)
        {
            listenMode = Uri.TryCreate(videoConfig.URL ?? "", UriKind.Absolute, out Uri parsed)
                && (parsed.Host == "0.0.0.0" || parsed.Host == "127.0.0.1" || parsed.Host == "localhost");
        }

        public override IEnumerable<Mode> GetModes()
        {
            return new[]
            {
                new Mode { FrameWork = FrameWork.FFmpeg, Width = 3840, Height = 2160, FrameRate = 60, Format = "rtmp", Index = 0 },
                new Mode { FrameWork = FrameWork.FFmpeg, Width = 3840, Height = 2160, FrameRate = 30, Format = "rtmp", Index = 1 },
                new Mode { FrameWork = FrameWork.FFmpeg, Width = 1920, Height = 1080, FrameRate = 60, Format = "rtmp", Index = 2 },
                new Mode { FrameWork = FrameWork.FFmpeg, Width = 1920, Height = 1080, FrameRate = 30, Format = "rtmp", Index = 3 },
                new Mode { FrameWork = FrameWork.FFmpeg, Width = 1280, Height = 720, FrameRate = 60, Format = "rtmp", Index = 4 },
                new Mode { FrameWork = FrameWork.FFmpeg, Width = 1280, Height = 720, FrameRate = 30, Format = "rtmp", Index = 5 },
                new Mode { FrameWork = FrameWork.FFmpeg, Width = 854, Height = 480, FrameRate = 30, Format = "rtmp", Index = 6 },
            };
        }

        protected override void Run()
        {
            byte[] readBuffer = null;
            int totalBytesRead = 0;
            System.Threading.Tasks.Task<int> pendingRead = null;

            while (run)
            {
                try
                {
                    if (!inited)
                    {
                        System.Threading.Thread.Sleep(10);
                        continue;
                    }

                    if (process == null || process.HasExited)
                    {
                        // Client disconnected or ffmpeg exited — restart to listen again
                        Connected = false;
                        break;
                    }

                    int bytesToRead = buffer.Length;

                    // Allocate/reallocate read buffer if needed (separate from display buffer)
                    if (readBuffer == null || readBuffer.Length != bytesToRead)
                    {
                        readBuffer = new byte[bytesToRead];
                        totalBytesRead = 0;
                        pendingRead = null;
                    }

                    int frameIntervalMs = VideoConfig.VideoMode?.FrameRate > 0
                        ? (int)(1000f / VideoConfig.VideoMode.FrameRate)
                        : 33;

                    // Only start a new read if there isn't one already in flight
                    if (pendingRead == null)
                        pendingRead = process.StandardOutput.BaseStream.ReadAsync(readBuffer, totalBytesRead, bytesToRead - totalBytesRead);

                    if (!pendingRead.Wait(frameIntervalMs))
                    {
                        // Still waiting — show fallback but keep the same read in flight
                        ShowFallback();
                        ProcessCameraFrame();
                        continue;
                    }

                    int bytesRead = pendingRead.Result;
                    pendingRead = null;

                    if (bytesRead == 0)
                    {
                        // Stream closed
                        Connected = false;
                        break;
                    }

                    totalBytesRead += bytesRead;

                    if (totalBytesRead < bytesToRead)
                        continue; // Still mid-frame — keep accumulating, no display yet

                    // Complete frame — push to display buffer and save
                    System.Array.Copy(readBuffer, buffer, bytesToRead);
                    if (lastFrame == null || lastFrame.Length != bytesToRead)
                        lastFrame = new byte[bytesToRead];
                    System.Array.Copy(readBuffer, lastFrame, bytesToRead);
                    lastFrameTime = DateTime.UtcNow;
                    totalBytesRead = 0;

                    ProcessCameraFrame();
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    totalBytesRead = 0;
                    pendingRead = null;
                    System.Threading.Thread.Sleep(100);
                }
            }
        }

        private void ShowFallback()
        {
            if (DateTime.UtcNow - lastFrameTime < FrameHoldDuration && lastFrame != null && lastFrame.Length == buffer.Length)
                System.Array.Copy(lastFrame, buffer, buffer.Length);
            else if (idleFrame != null && idleFrame.Length == buffer.Length)
                System.Array.Copy(idleFrame, buffer, buffer.Length);
            else
                System.Array.Clear(buffer, 0, buffer.Length);
        }

        public override bool Start()
        {
            idleFrame = LoadTestPattern(VideoConfig.VideoMode?.Width ?? 1280, VideoConfig.VideoMode?.Height ?? 720);
            return base.Start();
        }

        private byte[] LoadTestPattern(int width, int height)
        {
            string path = System.IO.Path.Combine("img", "testpattern.png");
            if (!System.IO.File.Exists(path))
                return null;

            try
            {
                string args = $"-i \"{path}\" -vf \"scale={width}:{height},format=rgba\" -vframes 1 -f rawvideo pipe:1";
                ProcessStartInfo psi = ffmpegMediaFramework.GetProcessStartInfo(args);
                psi.RedirectStandardOutput = true;

                using (Process p = new Process { StartInfo = psi })
                {
                    p.Start();
                    using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                    {
                        p.StandardOutput.BaseStream.CopyTo(ms);
                        p.WaitForExit(5000);
                        byte[] bytes = ms.ToArray();
                        if (bytes.Length == width * height * 4)
                            return bytes;
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }

            return null;
        }

        protected override ProcessStartInfo GetProcessStartInfo()
        {
            string url = VideoConfig.URL;

            int width = VideoConfig.VideoMode?.Width ?? 1280;
            int height = VideoConfig.VideoMode?.Height ?? 720;

            List<string> filters = new List<string>();
            filters.Add($"scale={width}:{height}");
            if (VideoConfig.Flipped)
                filters.Add("vflip");
            if (VideoConfig.Mirrored)
                filters.Add("hflip");
            filters.Add("format=rgba");
            string filterChain = string.Join(",", filters);

            string listenArg = listenMode ? "-listen 1 " : "";

            string ffmpegArgs = $"{listenArg}" +
                                $"-i \"{url}\" " +
                                $"-fflags nobuffer " +
                                $"-flags low_delay " +
                                $"-max_delay 0 " +
                                $"-strict experimental " +
                                $"-an " +
                                $"-vf \"{filterChain}\" " +
                                $"-f rawvideo pipe:1";

            Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG RTMP {(listenMode ? "Listen" : "Connect")} Mode: {ffmpegArgs}");

            return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
        }
    }
}
