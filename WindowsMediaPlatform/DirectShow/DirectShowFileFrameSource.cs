using DirectShowLib;
using ImageServer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsMediaPlatform.DirectShow
{
    public class DirectShowFileFrameSource : DirectShowFrameSource, IPlaybackFrameSource
    {
        public FileInfo Path { get; private set; }

        protected IMediaPosition MediaPosition { get; private set; }
        protected IBasicAudio BasicAudio { get; private set; }
        protected IBasicVideo BasicVideo { get; private set; }

        private IBaseFilter sourceFilter;

        public FrameTime[] FrameTimes { get; private set; }

        public TimeSpan MediaTime { get; private set; }
        public TimeSpan Length
        {
            get
            {
                return GetLength();
            }
        }


        public bool IsAtEnd
        {
            get
            {
                return MediaTime >= Length;
            }
        }
        public DateTime StartTime
        {
            get
            {
                return FrameTimes.FirstFrame();
            }
        }

        public DateTime CurrentTime
        {
            get
            {
                return FrameTimes.FirstFrame() + TimeSpan.FromSeconds(CurrentPosition());
            }
        }

        public double FrameRate { get; set; }

        public override bool Connected
        {
            get
            {
                return sourceFilter != null;
            }
            protected set
            {
                base.Connected = value;
            }
        }

        public bool Repeat { get; set; }

        public PlaybackSpeed PlaybackSpeed { get; set; }

        public DirectShowFileFrameSource(VideoConfig videoSource)
            :base(videoSource)
        {
            Tools.Logger.VideoLog.LogCall(this, videoSource.FilePath);
            Path = new FileInfo(videoSource.FilePath);
            FrameTimes = videoSource.FrameTimes;

            FrameRate = videoSource.VideoMode.FrameRate;

            PlaybackSpeed = PlaybackSpeed.Normal;
        }

        public override void CleanUp()
        {
            base.CleanUp();

            if (sourceFilter != null)
            {
                Marshal.ReleaseComObject(sourceFilter);
                sourceFilter = null;
            }
        }

        public override bool Start()
        {
            if (!Path.Exists)
                return false;

            return base.Start();
        }

        public override bool Pause()
        {
            if (MediaControl != null)
            {
                MediaControl.Pause();
            }
            return true;
        }

        public void Play()
        {
            if (MediaControl != null)
            {
                MediaControl.Run();
            }
        }

        public TimeSpan GetLength()
        {
            double dur = 0;
            if (MediaPosition != null)
            {
                int hr = MediaPosition.get_Duration(out dur);
                DsError.ThrowExceptionForHR(hr);
            }
            return TimeSpan.FromSeconds(dur);
        }

        public double CurrentPosition()
        {
            double now = 0;
            if (MediaPosition != null)
            {
                int hr = MediaPosition.get_CurrentPosition(out now);
                DsError.ThrowExceptionForHR(hr);
            }
            return now;
        }

        public void SetPosition(double position)
        {
            if (MediaPosition != null)
            {
                int hr = MediaPosition.put_CurrentPosition(position);
                DsError.ThrowExceptionForHR(hr);
            }
        }

        public void Mute(bool mute = true)
        {
            if (BasicAudio != null)
            {
                BasicAudio.put_Volume(mute ? -10000 : 0);
            }
        }

        protected override int SetupSource()
        {
            int hr;

            hr = filterGraph.AddSourceFilter(Path.FullName, Path.FullName, out sourceFilter);
            DsError.ThrowExceptionForHR(hr);

            IPin fileVideoOut = DsFindPin.ByDirection(sourceFilter, PinDirection.Output, 1);

            if (fileVideoOut == null)
            {
                fileVideoOut = DsFindPin.ByDirection(sourceFilter, PinDirection.Output, 0);
            }

            IPin smartTeeIn = DsFindPin.ByDirection(smartTee, PinDirection.Input, 0);

            // Connect the graph.  Many other filters automatically get added here
            hr = filterGraph.Connect(fileVideoOut, smartTeeIn);
            DsError.ThrowExceptionForHR(hr);

            MediaPosition = (IMediaPosition)filterGraph;
            BasicAudio = (IBasicAudio)filterGraph;
            BasicVideo = (IBasicVideo)filterGraph;

            double duration;
            MediaPosition.get_Duration(out duration);

            return hr;
        }

        public override bool Setup()
        {
            if (base.Setup())
            {
                // Create a renderer that we'll ignore..

                int hr; 
                IBaseFilter nullRender = (IBaseFilter)new NullRenderer();
                hr = filterGraph.AddFilter(nullRender, "NullRenderer");
                DsError.ThrowExceptionForHR(hr);

                IBaseFilter pAudioRenderer = (IBaseFilter)new DSoundRender();
                hr = filterGraph.AddFilter(pAudioRenderer, "Audio");
                DsError.ThrowExceptionForHR(hr);

                IPin wmvVideoOut = DsFindPin.ByDirection(grabber, PinDirection.Output, 0);
                IPin wmvAudioOut = DsFindPin.ByDirection(sourceFilter, PinDirection.Output, 0);

                IPin vmr9In = DsFindPin.ByDirection(nullRender, PinDirection.Input, 0);
                IPin audioRendererIn = DsFindPin.ByDirection(pAudioRenderer, PinDirection.Input, 0);

                // Connect the graph.  Many other filters automatically get added here
                hr = filterGraph.Connect(wmvVideoOut, vmr9In);
                DsError.ThrowExceptionForHR(hr);

                hr = filterGraph.Connect(wmvAudioOut, audioRendererIn);
                DsError.ThrowExceptionForHR(hr);
                return true;
            }

            return false;
        }

        public void SetPosition(DateTime time)
        {
            TimeSpan difference = time - FrameTimes.FirstFrame();
            SetPosition(difference.TotalSeconds);
        }

        public override int BufferCB(double sampleTime, IntPtr buffer, int bufferLen)
        {
            MediaTime = TimeSpan.FromSeconds(sampleTime);

            if (PlaybackSpeed == PlaybackSpeed.Slow)
            {
                Thread.Sleep(150);
            }
            return base.BufferCB(sampleTime, buffer, bufferLen);
        }

        public void SetPosition(TimeSpan seekTo)
        {
            SetPosition(seekTo.TotalSeconds);
        }

        public override IEnumerable<Mode> GetModes()
        {
            yield break;
        }

        public void PrevFrame()
        {
            throw new NotImplementedException();
        }

        public void NextFrame()
        {
            throw new NotImplementedException();
        }
    }
}
