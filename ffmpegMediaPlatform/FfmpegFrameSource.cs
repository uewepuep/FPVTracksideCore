using ImageServer;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace FfmpegMediaPlatform
{
    public abstract class FfmpegFrameSource : TextureFrameSource
    {
        private int width;
        private int height;

        public override int FrameWidth
        {
            get
            {
                return width > 0 ? width : 640;
            }
        }

        public override int FrameHeight
        {
            get
            {
                return height > 0 ? height : 480;
            }
        }

        public override SurfaceFormat FrameFormat
        {
            get
            {
                return SurfaceFormat;
            }
        }

        protected FfmpegMediaFramework ffmpegMediaFramework;

        protected Process process;

        protected byte[] buffer;

        private Thread thread;
        private bool run;
        private bool inited;

        public FfmpegFrameSource(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig)
            : base(videoConfig)
        {
            this.ffmpegMediaFramework = ffmpegMediaFramework;
            SurfaceFormat = SurfaceFormat.Color; // More widely supported than Bgr32
            
            // Initialize with default values from VideoConfig or hardcoded fallback
            width = VideoConfig.VideoMode?.Width ?? 640;
            height = VideoConfig.VideoMode?.Height ?? 480;
            
            if (width <= 0 || height <= 0)
            {
                width = 640;
                height = 480;
                Tools.Logger.VideoLog.LogCall(this, $"VideoConfig had invalid dimensions, using fallback 640x480");
            }
            
            buffer = new byte[width * height * 4];
            rawTextures = new XBuffer<RawTexture>(5, width, height);
            inited = true;
            
            Tools.Logger.VideoLog.LogCall(this, $"Pre-initialized with {width}x{height}");
        }

        public override void Dispose()
        {
            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Disposing frame source for '{VideoConfig.DeviceName}'");
            Stop();
            
            // Kill ALL ffmpeg processes that might be using this camera - aggressive cleanup (Windows only)
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                KillAllFfmpegProcessesForCamera();
            }
            
            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        Tools.Logger.VideoLog.LogCall(this, "FFMPEG Process still running, killing immediately");
                        process.Kill();
                        process.WaitForExit(1000); // Shorter wait in dispose
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "FFMPEG Process already exited");
                    }
                    process.Dispose();
                    Tools.Logger.VideoLog.LogCall(this, "FFMPEG Process disposed successfully");
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Error disposing process: {ex.Message}");
                }
                process = null;
            }
            
            base.Dispose();
        }

        private void KillAllFfmpegProcessesForCamera()
        {
            try
            {
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG (Windows) Killing ALL ffmpeg processes to ensure camera '{VideoConfig.DeviceName}' is freed");
                
                var ffmpegProcesses = System.Diagnostics.Process.GetProcessesByName("ffmpeg");
                int killedCount = 0;
                
                foreach (var proc in ffmpegProcesses)
                {
                    try
                    {
                        proc.Kill();
                        //if (!proc.HasExited)
                        //{
                        //    //Tools.Logger.VideoLog.LogCall(this, $"FFMPEG (Windows) Killing ffmpeg process {proc.Id}");
                        //    proc.Kill();
                        //    killedCount++;
                        //}
                    }
                    catch (Exception ex)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"FFMPEG (Windows) Error killing process {proc.Id}: {ex.Message}");
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
                
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG (Windows) Killed {killedCount} ffmpeg processes for camera cleanup");
                
                // Small delay to ensure processes are fully terminated
                if (killedCount > 0)
                {
                    System.Threading.Thread.Sleep(200);
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG (Windows) Error in aggressive cleanup: {ex.Message}");
            }
        }

        public override bool Start()
        {
            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Starting frame source for '{VideoConfig.DeviceName}' at {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height}@{VideoConfig.VideoMode.FrameRate}fps");
            
            // Ensure we're completely stopped before starting
            if (run)
            {
                Tools.Logger.VideoLog.LogCall(this, "FFMPEG Frame source already running, stopping first");
                Stop();
            }
            
            // Reset state for fresh start
            inited = false;
            width = VideoConfig.VideoMode?.Width ?? 640;
            height = VideoConfig.VideoMode?.Height ?? 480;
            buffer = new byte[width * height * 4];
            rawTextures = new XBuffer<RawTexture>(5, width, height);

            ProcessStartInfo processStartInfo = GetProcessStartInfo();

            process = new Process();
            process.StartInfo = processStartInfo;
            process.ErrorDataReceived += (s, e) =>
            {
                Logger.VideoLog.LogCall(this, e.Data);

                // Initialize immediately when we see the first frame progress update
                // This means ffmpeg is successfully reading from camera and outputting frames
                if (!inited && e.Data != null && (e.Data.Contains("frame=") && e.Data.Contains("fps=")))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Frame output detected - initializing frame processing");
                    
                    // Use the dimensions from VideoConfig since ffmpeg is successfully processing
                    width = VideoConfig.VideoMode?.Width ?? 640;
                    height = VideoConfig.VideoMode?.Height ?? 480;
                    
                    buffer = new byte[width * height * 4];  // RGBA = 4 bytes per pixel
                    rawTextures = new XBuffer<RawTexture>(5, width, height);

                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Initialized with {width}x{height}, buffer size: {buffer.Length} bytes");
                    inited = true;
                }
                
                // Also try to detect stream lines if they appear (fallback method)
                if (!inited && e.Data != null && e.Data.Contains("Stream") && e.Data.Contains("Video:"))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Found Stream line: {e.Data}");
                    
                    // Look for resolution pattern like "640x480" 
                    Regex resolutionRegex = new Regex(@"(\d{3,4})x(\d{3,4})");
                    Match resMatch = resolutionRegex.Match(e.Data);
                    
                    if (resMatch.Success)
                    {
                        if (int.TryParse(resMatch.Groups[1].Value, out int w) && int.TryParse(resMatch.Groups[2].Value, out int h) && w > 0 && h > 0)
                        {
                            width = w;
                            height = h;
                            
                            buffer = new byte[width * height * 4];
                            rawTextures = new XBuffer<RawTexture>(5, width, height);

                            Tools.Logger.VideoLog.LogCall(this, $"Stream parsing: Initialized with {width}x{height}, buffer size: {buffer.Length}");
                            inited = true;
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"Failed to parse resolution: '{resMatch.Groups[1].Value}' x '{resMatch.Groups[2].Value}'");
                        }
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "No resolution pattern found in Stream line");
                    }
                }
            };

            if (process.Start())
            {
                run = true;
                Connected = true;

                thread = new Thread(Run);
                thread.Name = "ffmpeg - " + VideoConfig.DeviceName;
                thread.Start();

                process.BeginErrorReadLine();
                return base.Start();
            }

            return false;
        }

        public override bool Stop()
        {
            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Stopping frame source for '{VideoConfig.DeviceName}'");
            run = false;
            
            // Wait for reading thread to finish
            if (thread != null && thread.IsAlive)
            {
                Tools.Logger.VideoLog.LogCall(this, "FFMPEG Waiting for reading thread to finish");
                if (!thread.Join(3000))
                {
                    Tools.Logger.VideoLog.LogCall(this, "FFMPEG Reading thread didn't finish in time, continuing with cleanup");
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, "FFMPEG Reading thread finished");
                }
                thread = null;
            }
            
            if (process != null && !process.HasExited)
            {
                Tools.Logger.VideoLog.LogCall(this, "FFMPEG Killing process immediately (no graceful shutdown needed for camera capture)");
                try
                {
                    // For camera capture, just kill immediately - no need for graceful shutdown
                    process.Kill();
                    if (!process.WaitForExit(3000))
                    {
                        Tools.Logger.VideoLog.LogCall(this, "FFMPEG Process didn't exit after kill - this is unusual");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "FFMPEG Process killed successfully");
                    }
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Error killing process: {ex.Message}");
                }

                try
                {
                    process.CancelOutputRead();
                }
                catch (InvalidOperationException)
                {
                    // No async read operation is in progress, ignore
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Error canceling output read: {ex.Message}");
                }
            }
            else if (process != null)
            {
                Tools.Logger.VideoLog.LogCall(this, "FFMPEG Process already exited");
            }
            else
            {
                Tools.Logger.VideoLog.LogCall(this, "FFMPEG No process to stop");
            }

            return base.Stop();
        }

        protected abstract ProcessStartInfo GetProcessStartInfo();

        private void Run()
        {
            Tools.Logger.VideoLog.LogCall(this, "Reading thread started");
            bool loggedInit = false;
            while(run)
            {
                if (!inited)
                {
                    System.Threading.Thread.Sleep(10); // Prevent busy waiting
                    continue;
                }
                if (!loggedInit)
                {
                    Tools.Logger.VideoLog.LogCall(this, "Reading thread initialized, starting to read frames");
                    loggedInit = true;
                }
                Stream stream = process.StandardOutput.BaseStream;
                if (stream != null)
                {
                    int totalBytesRead = 0;
                    int bytesToRead = buffer.Length;
                    
                    // Keep reading until we have a complete frame
                    while (totalBytesRead < bytesToRead)
                    {
                        int bytesRead = stream.Read(buffer, totalBytesRead, bytesToRead - totalBytesRead);
                        if (bytesRead == 0)
                            break; // End of stream
                        totalBytesRead += bytesRead;
                    }
                    
                    if (totalBytesRead == bytesToRead)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Complete frame read: {totalBytesRead} bytes, processing frame {FrameProcessNumber}");
                        ProcessImage();
                        NotifyReceivedFrame();
                    }
                    else if (totalBytesRead > 0)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Incomplete frame read: {totalBytesRead}/{bytesToRead} bytes");
                    }
                }
            }
        }

        protected override void ProcessImage()
        {
            var currentRawTextures = rawTextures;
            if (currentRawTextures != null)
            {
                RawTexture frame;
                if (currentRawTextures.GetWritable(out frame))
                {
                    FrameProcessNumber++;
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Processing frame {FrameProcessNumber}, buffer size: {buffer.Length} bytes");
                    // Convert byte[] to IntPtr for SetData call
                    System.Runtime.InteropServices.GCHandle handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try
                    {
                        IntPtr bufferPtr = handle.AddrOfPinnedObject();
                        frame.SetData(bufferPtr, SampleTime, FrameProcessNumber);
                    }
                    finally
                    {
                        handle.Free();
                    }
                    currentRawTextures.WriteOne(frame);
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Frame {FrameProcessNumber} written to rawTextures buffer");
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Could not get writable frame from rawTextures buffer");
                }
            }
            else
            {
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG rawTextures is null - cannot process frame");
            }

            base.ProcessImage();
        }
    }
}
