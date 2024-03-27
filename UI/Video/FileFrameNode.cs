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

        public byte Limit { get; set; }

        public ChromaKeyFileFrameNode(string filename) 
            : base(filename)
        {
            Enabled = true;
            Limit = 10;
        }

        public override void PreProcess(Drawer id)
        {
            base.PreProcess(id);

            if (!Enabled)
                return;

            if (texture == replacementTexture)
                return;

            if (replacementTexture == null || texture.Width != replacementTexture.Width || texture.Height != replacementTexture.Height)
            {
                replacementTexture?.Dispose();
                replacementTexture = new Texture2D(id.GraphicsDevice, texture.Width, texture.Height, false, SurfaceFormat.Bgra32);
                data = new Color[texture.Width * texture.Height];
            }

            texture.GetData(data);
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i].G > data[i].B + Limit &&
                    data[i].G > data[i].R + Limit)
                {
                    data[i] = Color.Transparent;
                }
            }
            replacementTexture.SetData(data);
            texture = replacementTexture;
        }
    }
}
