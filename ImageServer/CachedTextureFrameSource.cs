using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Tools;

namespace ImageServer
{
    public class CachedTextureFrameSource : FrameSource, IPlaybackFrameSource
    {
        public override int FrameWidth { get { return frameWidth; } }

        public override int FrameHeight { get { return frameHeight; } }

        public override SurfaceFormat FrameFormat { get { return frameFormat; } }

        public FrameTime[] FrameTimes { get { return frameTimes; } } 

        public DateTime StartTime { get { return startTime; } }

        public DateTime CurrentTime
        {
            get
            {
                return StartTime + MediaTime;
            }
        }

        public double FrameRate { get { return frameRate; } }

        public PlaybackSpeed PlaybackSpeed { get; set; }

        public TimeSpan MediaTime
        {
            get
            {
                return TimeSpan.FromTicks(currentIndex * TicksPerFrame);
            }
        }

        public TimeSpan Length { get { return length; } }

        public bool IsAtEnd
        {
            get
            {
                return MediaTime >= Length;
            }
        }

        public bool Repeat { get; set; }
        public bool BounceRepeat { get; set; }

        public bool Reversed { get; set; }

        public long TicksPerFrame
        {
            get
            {
                return (long)(10000000 / FrameRate);
            }
        }


        private int currentIndex;
        private List<FrameTextureSample> samples;

        private Thread playbackThread;

        private int frameWidth;
        private int frameHeight;
        private SurfaceFormat frameFormat;
        private FrameTime[] frameTimes;
        private DateTime startTime;
        private double frameRate;
        private TimeSpan length;

        private FrameSource frameSource;
        private GraphicsDevice graphicsDevice;

        private IPlaybackFrameSource playbackFrameSource { get { return frameSource as IPlaybackFrameSource; } }

        public Texture2D Mask { get; set; }

        public CachedTextureFrameSource(GraphicsDevice graphicsDevice, VideoFrameWork videoFrameWork, string filename)
            :base(new VideoConfig())
        {
            samples = new List<FrameTextureSample>();
            frameSource = videoFrameWork.CreateFrameSource(filename);
            this.graphicsDevice = graphicsDevice;
        }

        public override void Dispose()
        {
            frameSource?.Dispose();
            frameSource = null;

            base.Dispose();

            Stop();

            for (int i = 0; i < samples.Count; i++)
            {
                FrameTextureSample fs = samples[i];
                fs.Dispose();
            }
            samples.Clear();
        }

        private void CopyFrameSource(FrameSource frameSource)
        {
            int count = 0;

            playbackFrameSource.PlaybackSpeed = PlaybackSpeed.Normal;
            frameSource.OnFrameEvent += (long sampleTime, long processNumber) =>
            {
                if (count == 0)
                {
                    Direction = frameSource.Direction;
                    frameWidth = frameSource.FrameWidth;
                    frameHeight = frameSource.FrameHeight;
                    frameFormat = frameSource.FrameFormat;
                    frameTimes = playbackFrameSource.FrameTimes;
                    startTime = playbackFrameSource.StartTime;
                    frameRate = playbackFrameSource.FrameRate;
                    length = playbackFrameSource.Length;
                }

                count++;
                FrameTextureSample texture = CreateSample(graphicsDevice, frameWidth, frameHeight, frameFormat);

                Texture2D texture2D = texture as Texture2D;
                //if (Mask != null)
                //{
                //    texture2D.Mask(Mask);
                //}

                if (frameSource.UpdateTexture(graphicsDevice, count, ref texture2D))
                {
                    samples.Add(texture);
                }
            };
            frameSource.Start();

            while (count == 0)
            {
                Thread.Sleep(1);

                if (frameSource == null || frameSource.IsDisposed)
                    break;
            }
        }

        protected virtual FrameTextureSample CreateSample(GraphicsDevice graphicsDevice, int frameWidth, int frameHeight, SurfaceFormat frameFormat)
        {
            return new FrameTextureSample(graphicsDevice, frameWidth, frameHeight, frameFormat);
        }

        private void PlayBackThread()
        {
            if (frameSource != null && samples.Count == 0)
            {
                CopyFrameSource(frameSource);
            }

            int count = 0;
            while (State == States.Running)
            {
                if (Reversed)
                {
                    if (currentIndex >= 1)
                    {
                        currentIndex--;
                    }
                    else if (BounceRepeat)
                    {
                        Reversed = false;
                    }
                }
                else
                {
                    if (currentIndex < samples.Count - 1)
                    {
                        currentIndex++;
                    }
                    else if (Repeat)
                    {
                        if (BounceRepeat)
                        {
                            Reversed = true;
                        }
                        else
                        {
                            currentIndex = 0;
                        }
                    }

                    if (playbackFrameSource != null && playbackFrameSource.IsAtEnd)
                    {
                        frameSource.Dispose();
                        frameSource = null;
                    }
                }

                count++;

                if (TicksPerFrame > 0)
                {
                    OnFrame(TicksPerFrame * currentIndex, count);

                    Thread.Sleep(TimeSpan.FromTicks(TicksPerFrame));
                }
            }
        }

        public override IEnumerable<Mode> GetModes()
        {
            yield break;
        }

        public override bool UpdateTexture(GraphicsDevice graphicsDevice, int drawFrameId, ref Texture2D texture)
        {
            if (currentIndex < samples.Count)
            {
                texture = samples[currentIndex];
                return true;
            }

            return false;
        }

        public void Play()
        {
            Start();
        }

        public override bool Start()
        {
            if (playbackThread == null)
            {
                playbackThread = new Thread(PlayBackThread);
                playbackThread.Name = "CachedTextureFrameSource";
                playbackThread.Start();

                return base.Start();
            }
            return false;
        }

        public override bool Stop()
        {
            if (playbackThread != null)
            {
                base.Stop();

                playbackThread.Join();
                playbackThread = null;

                return true;
            }

            return false;
        }

        public void SetPosition(DateTime seekTime)
        {
            SetPosition(seekTime - StartTime);
        }

        public void SetPosition(TimeSpan seekTime)
        {
            int index = (int)(seekTime.Ticks / TicksPerFrame);

            currentIndex = Math.Min(samples.Count, Math.Max(0, index));
        }

        public void PrevFrame()
        {
            if (currentIndex >= 1)
            {
                currentIndex--;
            }
        }

        public void NextFrame()
        {
            if (currentIndex < samples.Count - 1)
            {
                currentIndex++;
            }
        }

        public void Mute(bool mute = true)
        {

        }
    }
}
