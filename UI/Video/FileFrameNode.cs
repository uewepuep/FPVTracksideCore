using Composition.Input;
using ImageServer;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Video
{
    public class FileFrameNode : FrameNode
    {

        private IPlaybackFrameSource playbackFrameSource
        {
            get
            {
                return Source as IPlaybackFrameSource;
            }
        }

        public bool Repeat
        {
            get
            {
                return playbackFrameSource.Repeat;
            }
            set
            {
                playbackFrameSource.Repeat = value;
            }
        }

        public FileFrameNode(string filename) 
            : base(VideoFrameworks.GetFramework(FrameWork.MediaFoundation).CreateFrameSource(filename))
        {
            Start();
        }

        public void Play()
        {
            playbackFrameSource?.Play();
        }

        void SetPosition(DateTime seekTime)
        {
            playbackFrameSource?.SetPosition(seekTime);
        }

        bool Pause()
        {
            return playbackFrameSource.Pause();
        }

        bool Start()
        {
            return playbackFrameSource.Start();
        }

        void Mute(bool mute = true)
        {
            playbackFrameSource?.Mute(mute);
        }


        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.Button == MouseButtons.Left)
            {
                playbackFrameSource.SetPosition(TimeSpan.Zero);
                playbackFrameSource.Play();
            }

            return base.OnMouseInput(mouseInputEvent);
        }
    }
}
