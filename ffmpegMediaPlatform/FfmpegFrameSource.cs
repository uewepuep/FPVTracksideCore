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
            base.Dispose();
        }

        public override bool Start()
        {
            if (run)
                return base.Start();

            inited = false;

            ProcessStartInfo processStartInfo = GetProcessStartInfo();

            process = new Process();
            process.StartInfo = processStartInfo;
            process.ErrorDataReceived += (s, e) =>
            {
                Logger.VideoLog.LogCall(this, e.Data);

                //Stream #0:0: Video: rawvideo (YUY2 / 0x32595559), yuyv422(tv, bt470bg/bt709/unknown), 640x480, 60 fps, 60 tbr, 10000k tbn

                if (!inited && e.Data != null && e.Data.Contains("Stream") && e.Data.Contains("Video:"))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Found Stream line: {e.Data}");
                    
                    // Look for resolution pattern like "640x480" but be more specific to avoid matching hex codes
                    Regex resolutionRegex = new Regex(@",\s*(\d{3,4})x(\d{3,4}),");
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
            run = false;
            if (process != null && !process.HasExited)
            {
                try
                {
                    // Try to terminate gracefully first
                    process.StandardInput.Close();
                }
                catch { }

                // Wait for a short time for graceful exit
                if (!process.WaitForExit(2000))
                {
                    try
                    {
                        // Force kill the process if it doesn't exit gracefully
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch { }
                }

                try
                {
                    process.CancelOutputRead();
                }
                catch (InvalidOperationException)
                {
                    // No async read operation is in progress, ignore
                }
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
                }
            }

            base.ProcessImage();
        }
    }
}
