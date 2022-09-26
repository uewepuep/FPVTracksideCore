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
        public delegate void FrameDelegate(int number);

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

        public int FrameCount { get; set; }

        public VideoConfig VideoConfig { get; private set; }

        public virtual bool Connected { get; protected set; }

        public bool IsDisposed { get; private set; }

        public bool IsVisible { get; set; }

        public bool Flipped { get; set; }

        public bool DrawnThisGraphicsFrame { get; set; }
        public bool Recording { get; protected set; }
        public bool RebootRequired { get; protected set; }

        public FrameSource(VideoConfig videoConfig)
        {
            VideoConfig = videoConfig;
            Flipped = videoConfig.Flipped;
            FrameCount = 0;
            OnFrameEvent = null;
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

        public void OnFrame(int id)
        {
            if (State == States.Running)
            {
                OnFrameEvent?.Invoke(id);
                FrameCount++;
            }
        }


    }

    public interface ICaptureFrameSource
    {
        bool IsVisible { get; }
        FrameTime[] FrameTimes { get; }
        void StartRecording(string filename);
        void StopRecording();
        string Filename { get; }
        bool Flipped { get; }
        bool Recording { get; }
        VideoConfig VideoConfig { get; }

        bool RecordNextFrameTime { set; }
    }

    public interface IPlaybackFrameSource
    {
        FrameTime[] FrameTimes { get; }
        DateTime StartTime { get; }
        DateTime CurrentTime { get; }
        double FrameRate { get; }
        bool SlowMotion { get; set; }
        TimeSpan MediaTime { get; }
        TimeSpan Length { get; }

        void SetPosition(DateTime seekTime);
        void Play();
        bool Pause();
        bool Start();
        void Mute(bool mute = true);
    }
}
