using Composition;
using Composition.Input;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

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

        public override void Dispose()
        {
            base.Dispose();

            // We need to dispose the source we created.
            Source.Dispose();
            Source = null;
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

    public class ChromaKeyFileFrameNode : FileFrameNode
    {
        private Texture2D replacementTexture;

        private Color[] data;

        public bool Enabled { get; set; }


        public ChromaKeyFileFrameNode(string filename) 
            : base(filename)
        {
            Enabled = true;
        }

        public override void PreProcess(Drawer id)
        {
            base.PreProcess(id);

            if (!Enabled)
                return;

            if (texture == replacementTexture)
                return;

            Maths.ChromaKey(texture, ref data, ref replacementTexture);

            texture = replacementTexture;
        }
    }
}
