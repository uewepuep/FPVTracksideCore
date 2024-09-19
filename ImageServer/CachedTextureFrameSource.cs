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

        private long currentTimeTicks;
        public DateTime CurrentTime
        {
            get
            {
                return StartTime.AddTicks(currentTimeTicks);
            }
        }

        public double FrameRate { get { return frameRate; } }

        public PlaybackSpeed PlaybackSpeed { get; set; }

        public TimeSpan MediaTime
        {
            get
            {
                return TimeSpan.FromTicks(currentTimeTicks);
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

        public CachedTextureFrameSource()
            :base(new VideoConfig())
        {
            samples = new List<FrameTextureSample>();
        }

        public override void Dispose()
        {
            base.Dispose();

            Stop();

            foreach (FrameTextureSample frame in samples)
            {
                frame.Dispose();
            }
            samples.Clear();
        }

        public void CopyFrameSource(GraphicsDevice graphicsDevice, FrameSource frameSource)
        {
            IPlaybackFrameSource playbackFrameSource = frameSource as IPlaybackFrameSource;
            int count = 0;

            playbackFrameSource.PlaybackSpeed = PlaybackSpeed.FastAsPossible;
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
                FrameTextureSample texture = new FrameTextureSample(graphicsDevice, FrameWidth, FrameHeight, FrameFormat);

                Texture2D texture2D = texture as Texture2D;

                if (frameSource.UpdateTexture(graphicsDevice, count, ref texture2D))
                {
                    samples.Add(texture);
                }
            };
            frameSource.Start();

            while (!playbackFrameSource.IsAtEnd)
            {
                Thread.Sleep(1);
            }
        }

        private void PlayBack()
        {
            int count = 0;
            while (State == States.Running)
            {
                currentTimeTicks += TicksPerFrame;

                count++;
                OnFrame(currentTimeTicks, count);

                Thread.Sleep(TimeSpan.FromTicks(TicksPerFrame));
            }
        }

        public override IEnumerable<Mode> GetModes()
        {
            yield break;
        }

        public override bool UpdateTexture(GraphicsDevice graphicsDevice, int drawFrameId, ref Texture2D texture)
        {
            while (currentIndex + 1 < samples.Count && samples[currentIndex].FrameSampleTime < currentTimeTicks)
            {
                currentIndex++;
            }

            if (currentIndex + 1 == samples.Count && Repeat)
            {
                currentIndex = 0;
                currentTimeTicks = 0;
            }

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
                playbackThread = new Thread(PlayBack);
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
            currentTimeTicks = seekTime.Ticks;
        }

        public void PrevFrame()
        {
            currentTimeTicks -= TicksPerFrame;
        }

        public void NextFrame()
        {
            currentTimeTicks += TicksPerFrame;
        }

        public void Mute(bool mute = true)
        {

        }

        public void DoChromaKey(ChromaKeyColor chromaKeyColor, byte chromaKeyLimit)
        {
            Color[] data = null;
            for (int i = 0; i < samples.Count; i++)
            {
                FrameTextureSample oldTexture = samples[i];

                if (data == null)
                {
                    data = new Color[oldTexture.Width * oldTexture.Height];
                }
                FrameTextureSample newTexture = new FrameTextureSample(oldTexture.GraphicsDevice, oldTexture.Width, oldTexture.Height, oldTexture.Format);

                Texture2D replacementTexture = newTexture as Texture2D;

                TextureHelper.ChromaKey(oldTexture, ref data, ref replacementTexture, chromaKeyColor, chromaKeyLimit);

                newTexture.FrameSampleTime = oldTexture.FrameSampleTime;
                newTexture.FrameProcessCount = oldTexture.FrameProcessCount;

                samples[i] = newTexture;

                oldTexture.Dispose();
            }
        }
    }
}
