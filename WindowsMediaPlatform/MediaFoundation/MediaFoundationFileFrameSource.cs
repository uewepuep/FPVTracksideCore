using ImageServer;
using MediaFoundation;
using MediaFoundation.Misc;
using MediaFoundation.ReadWrite;
using MediaFoundation.Transform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools;

namespace WindowsMediaPlatform.MediaFoundation
{
    public class MediaFoundationFileFrameSource : MediaFoundationFrameSource, IPlaybackFrameSource
    {
        private IMFMediaSource source;
        private IMFPresentationDescriptor presentationDescriptor;

        public FileInfo Path { get; private set; }
        public FrameTime[] FrameTimes { get; private set; }

        public PlaybackSpeed PlaybackSpeed { get; set; }

        public TimeSpan MediaTime
        {
            get
            {
                if (seek.HasValue)
                {
                    return TimeSpan.FromSeconds(seek.Value / FrameRate);
                }

                return sampleTime;
            }
        }

        public DateTime StartTime
        {
            get
            {
                return FrameTimes.GetRealTime(TimeSpan.Zero, Latency);
            }
        }

        public DateTime CurrentTime
        {
            get
            {
                return FrameTimes.GetRealTime(MediaTime, Latency);
            }
        }

        public double FrameRate { get; set; }

        public override bool Connected 
        { 
            get
            {
                return source != null;
            }
            protected set
            {
                base.Connected = value;
            }
        }

        private TimeSpan sampleTime;
        private long sampleFrame;

        public TimeSpan Length { get; private set; }

        private Queue<Action<IMFSourceReader>> queue;

        private long? seek;
        private DateTime epoch;

        public TimeSpan Latency { get; private set; }
        public bool Repeat { get; set; }

        public bool IsAtEnd
        {
            get
            {
                return MediaTime >= Length;
            }
        }

        public TimeSpan FrameTime
        {
            get
            {
                return TimeSpan.FromMilliseconds(1000 / FrameRate);
            }
        }

        public MediaFoundationFileFrameSource(VideoConfig videoConfig) 
            : base(videoConfig)
        {
            ASync = false;
            AutomaticVideoConversion = true;

            //Tools.Logger.VideoLog.LogCall(this, videoConfig.FilePath);
            Path = new FileInfo(videoConfig.FilePath);
            FrameTimes = videoConfig.FrameTimes;
            PlaybackSpeed = PlaybackSpeed.Normal;


            Latency = TimeSpan.Zero;
            if (VideoConfig.DeviceLatency != 0)
            {
                Latency = TimeSpan.FromSeconds(VideoConfig.DeviceLatency);
            }
        }

        protected override void ProcessImage()
        {
            if (State == States.Paused && seek == null)
            {
                Thread.Sleep(10);
            }
            else
            {
                IMFSample sample;
                HResult hr = Read(out sample);
                if (MFHelper.Succeeded(hr) && sample != null)
                {
                    if (PlaybackSpeed == PlaybackSpeed.Slow && FrameRate > 0)
                    {
                        int rate = 4;
                        epoch += FrameTime * rate;
                    }

                    DateTime due = epoch + sample.GetSampleTime();

                    sampleTime = sample.GetSampleTime();
                    sampleFrame = GetFrameTime(sampleTime);

                    //Logger.VideoLog.Log(this, sampleFrame + ", tim " + sample.GetSampleTime() + ", dur " + sample.GetSampleDuration());
                    CurrentlySeeking();

                    if (PlaybackSpeed != PlaybackSpeed.FastAsPossible && State == States.Running && (seek == null || seek.Value < sampleFrame))
                    {
                        TimeSpan diff = due - DateTime.Now;

                        if (diff > TimeSpan.Zero)
                        {
                            Thread.Sleep(diff);
                        }
                    }

                    if (AutomaticVideoConversion)
                    {
                        ProcessRGBSample(sample);
                    }
                    else
                    {
                        ProcessRaw(sample);
                    }

                    Connected = true;
                    base.ProcessImage();
                    MFHelper.SafeRelease(sample);
                }
                else
                {
                    if (sampleFrame > 0 && State == States.Running)
                    {
                        if (Repeat)
                        {
                            SetPosition(TimeSpan.Zero);
                        }
                    }
                }
            }

            lock (queue)
            {
                while (queue.Count > 0)
                {
                    var action = queue.Dequeue();
                    action?.Invoke(reader);
                }
            }
        }

        protected override HResult ProcessRGBSample(IMFSample sample)
        {
            if (!CurrentlySeeking())
            {
                return base.ProcessRGBSample(sample);
            }
            NotifyReceivedFrame();
            return HResult.S_OK;
        }


        public bool CurrentlySeeking()
        {
            if (seek.HasValue)
            {
                if (sampleFrame >= seek.Value)
                {
                    //Logger.VideoLog.LogCall(this, sampleFrame, (int)(seek.Value / FrameRate), seek.Value % FrameRate);
                    seek = null;
                    return false;
                }

                return true;
            }
            return false;
        }

        private void CreateMediaSource(string sURL)
        {
            IMFSourceResolver sourceResolver;
            object tempSource;

            // Create the source resolver.
            HResult hr = MFExtern.MFCreateSourceResolver(out sourceResolver);
            MFError.ThrowExceptionForHR(hr);

            try
            {
                // Use the source resolver to create the media source.
                MFObjectType ObjectType = MFObjectType.Invalid;

                hr = sourceResolver.CreateObjectFromURL(
                        sURL,                       // URL of the source.
                        MFResolution.MediaSource,   // Create a source object.
                        null,                       // Optional property store.
                        out ObjectType,             // Receives the created object type.
                        out tempSource                 // Receives a pointer to the media source.
                    );
                MFError.ThrowExceptionForHR(hr);

                // Get the IMFMediaSource interface from the media source.
                source = (IMFMediaSource)tempSource;

                hr = source.CreatePresentationDescriptor(out presentationDescriptor);
                MFError.ThrowExceptionForHR(hr);

                long ticks;
                hr = presentationDescriptor.GetUINT64(MFAttributesClsid.MF_PD_DURATION, out ticks);
                MFError.ThrowExceptionForHR(hr);

                Length = TimeSpan.FromTicks(ticks);

                CreateReader(source);
            }
            finally
            {
                // Clean up
                MFHelper.SafeRelease(sourceResolver);
            }
        }

        protected override HResult SetupTransforms(out IMFMediaType sourceMediaType, out IMFMediaType outputMediaType)
        {
            HResult hr = base.SetupTransforms(out sourceMediaType, out outputMediaType);

            int numerator;
            int denominator;

            MFExtern.MFGetAttribute2UINT32asUINT64(outputMediaType, MFAttributesClsid.MF_MT_FRAME_RATE, out numerator, out denominator);

            FrameRate = numerator / denominator;

            return hr;
        }

        public override void CleanUp()
        {
            base.CleanUp();

            if (source != null)
            {
                MFHelper.SafeRelease(source);
                source = null;
            }
        }

        public override bool Start()
        {
            if (!Path.Exists)
                return false;

            if (source == null)
            {
                CreateMediaSource(Path.FullName);

                queue = new Queue<Action<IMFSourceReader>>();

                NotifyReceivedFrame();
            }

            epoch = DateTime.Now - MediaTime;

            return base.Start();
        }

        public override bool Pause()
        {
            return base.Pause();
        }

        public void Play()
        {
            Unpause();
            epoch = DateTime.Now - MediaTime;
        }

        public void SetPosition(DateTime seekTime)
        {
            TimeSpan mediaTime = FrameTimes.GetMediaTime(seekTime, Latency);
            if (queue == null)
                return;

            SetPosition(mediaTime);
        }
        public void SetPosition(TimeSpan mediaTime)
        {
            if (mediaTime < TimeSpan.Zero)
                mediaTime = TimeSpan.Zero;
            if (mediaTime > Length)
                mediaTime = Length;

            SetPosition(GetFrameTime(mediaTime));
        }

        public void SetPosition(long frame)
        {
            if (frame < 0)
                frame = 0;


            lock (queue)
            {
                queue.Enqueue((reader) =>
                {
                    TimeSpan mediaTime = GetFrameTime(frame);

                    reader.Flush(0);
                    using (PropVariant value = new PropVariant(mediaTime.Ticks))
                    {
                        reader.SetCurrentPosition(Guid.Empty, value);
                        seek = frame;
                        epoch = DateTime.Now - mediaTime;

                        //Logger.VideoLog.LogCall(this, mediaTime, (int)(frame / FrameRate), frame % FrameRate);
                    }
                });
            }
        }

        public void Mute(bool mute = true)
        {
        }

        public override IEnumerable<Mode> GetModes()
        {
            yield break;
        }

        protected long GetFrameTime(TimeSpan mediaTime)
        {
            return (long)Math.Round(mediaTime.TotalSeconds * FrameRate);
        }

        protected TimeSpan GetFrameTime(long frameNumber)
        {
            return frameNumber * FrameTime;
        }

        public void PrevFrame()
        {
            SetPosition(sampleFrame - 1);
        }
        public void NextFrame()
        {
            SetPosition(sampleFrame + 1);
        }

    }
}
