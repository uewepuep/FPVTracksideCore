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
                return width;
            }
        }

        public override int FrameHeight
        {
            get
            {
                return height;
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

        protected char[] buffer;

        private Thread thread;
        private bool run;
        private bool inited;

        public FfmpegFrameSource(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig)
            : base(videoConfig)
        {
            this.ffmpegMediaFramework = ffmpegMediaFramework;
            SurfaceFormat = SurfaceFormat.Bgr32;
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

                if (!inited && e.Data.Contains("Stream"))
                {
                    Regex reg = new Regex("([0-9]*)x([0-9]*), ([0-9]*) fps");
                    Match m = reg.Match(e.Data);
                    if (m.Success) 
                    {
                        if (int.TryParse(m.Groups[1].Value, out int w) && int.TryParse(m.Groups[2].Value, out int h)) 
                        {
                            width = w;
                            height = h;
                        }

                        buffer = new char[width * height * 4];
                        rawTextures = new XBuffer<RawTexture>(5, width, height);

                        inited = true;
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
            if (process != null)
            {
                process.WaitForExit();
                process.CancelOutputRead();
            }

            return base.Stop();
        }

        protected abstract ProcessStartInfo GetProcessStartInfo();

        private void Run()
        {
            while(run)
            {
                if (!inited)
                {
                    continue;
                }
                StreamReader reader = process.StandardOutput;
                if (reader != null)
                {
                    if (reader.Read(buffer, 0, buffer.Length) == buffer.Length)
                    {
                        ProcessImage();
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
                    frame.SetData(buffer, SampleTime, FrameProcessNumber);
                    currentRawTextures.WriteOne(frame);
                }
            }

            base.ProcessImage();
        }
    }
}
