using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Drawing;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Tools;

namespace ImageServer
{
    public abstract class FrameSource : IDisposable
    {
        public delegate void FrameDelegate(long sampleTime, long processNumber);

        public event FrameDelegate OnFrameEvent;

        public enum States
        {
            Stopped,
            Running,
            Paused
        }

        public States State { get; private set; }

        public abstract bool UpdateTexture(GraphicsDevice graphicsDevice, int drawFrameId, ref Texture2D texture);
        public abstract int FrameWidth { get; }
        public abstract int FrameHeight { get; }
        public abstract SurfaceFormat FrameFormat { get; }

        public abstract IEnumerable<Mode> GetModes();

        public int References { get; set; }

        public long FrameProcessNumber { get; set; }
        public long SampleTime { get; set; }

        public VideoConfig VideoConfig { get; private set; }

        public virtual bool Connected { get; protected set; }

        public bool IsDisposed { get; private set; }

        public bool IsVisible { get; set; }

        public bool DrawnThisGraphicsFrame { get; set; }
        public bool Recording { get; protected set; }
        public bool RebootRequired { get; protected set; }

        public enum Directions
        {
            TopDown,
            BottomUp
        }

        public Directions Direction { get; protected set; }

        public FrameSource(VideoConfig videoConfig)
        {
            VideoConfig = videoConfig;
            SampleTime = 0;
            OnFrameEvent = null;
            Direction = Directions.TopDown;
            IsVisible = true;
        }

        public virtual void CleanUp()
        {
            if (State != States.Stopped)
            {
                Stop();
            }
        }

        public virtual void Dispose()
        {
            IsDisposed = true;
            CleanUp();
        }

        public virtual bool Start()
        {
            State = States.Running;
            return true;
        }

        public virtual bool Stop()
        {
            State = States.Stopped;
            return true;
        }

        public virtual bool Pause()
        {
            State = States.Paused;
            return true;
        }

        public virtual bool Unpause()
        {
            State = States.Running;
            return true;
        }

        public void OnFrame(long sampleTime, long processNumber)
        {
            // Log only every 120 frames to reduce spam
            if (processNumber % 120 == 0)
            {
                Tools.Logger.VideoLog.LogCall(this, $"OnFrame: processNumber={processNumber}, OnFrameEvent subscribers={(OnFrameEvent?.GetInvocationList()?.Length ?? 0)}");
            }
            OnFrameEvent?.Invoke(sampleTime, processNumber);
        }
    }

    public interface ICaptureFrameSource
    {
        bool IsVisible { get; }
        FrameTime[] FrameTimes { get; }
        void StartRecording(string filename);
        void StopRecording();
        string Filename { get; }
        bool Recording { get; }
        VideoConfig VideoConfig { get; }

        bool RecordNextFrameTime { set; }
        bool ManualRecording { get; set; }
        bool Finalising { get; }
    }


    public enum PlaybackSpeed
    {
        Normal,
        Slow,
        FastAsPossible
    }
    public interface IPlaybackFrameSource
    {
        FrameTime[] FrameTimes { get; }
        DateTime StartTime { get; }
        DateTime CurrentTime { get; }
        double FrameRate { get; }
        PlaybackSpeed PlaybackSpeed { get; set; }
        TimeSpan MediaTime { get; }
        TimeSpan Length { get; }
        bool Repeat { get; set; }
        bool IsAtEnd { get; }

        void SetPosition(DateTime seekTime);
        void SetPosition(TimeSpan seekTime);
        void PrevFrame();
        void NextFrame();

        void Play();
        bool Pause();
        bool Start();
        void Mute(bool mute = true);
    }
}
