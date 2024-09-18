using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ImageServer
{
    public class StaticFrameSource : FrameSource
    {
        public TimeSpan Interval { get; set; }

        private int width;
        private int height;
        private SurfaceFormat surfaceFormat;

        public override int FrameWidth { get { return width; } }
        public override int FrameHeight { get { return height; } }
        public override SurfaceFormat FrameFormat { get { return surfaceFormat; } }

        private Thread spawnThread;

        private Dictionary<GraphicsDevice, Texture2D> textures;

        public StaticFrameSource(VideoConfig videoSource)
            :base(videoSource)
        {
            textures = new Dictionary<GraphicsDevice, Texture2D>();
        }

        public override void Dispose()
        {
            Stop();
            base.Dispose();
        }

        public override bool Start()
        {
            if (base.Start())
            {
                spawnThread = new Thread(Spawn);
                spawnThread.Name = "Static image source thread";
                spawnThread.Start();
            }
            return false;
        }

        public override bool Stop()
        {
            if (base.Stop())
            {
                if (spawnThread != null)
                {
                    spawnThread.Join();
                }
            }
            return false;
        }

        private void Spawn()
        {
            while (State == States.Running)
            {
                OnFrame(0, 0);
                Thread.Sleep(Interval);
            }
        }

        public override bool UpdateTexture(GraphicsDevice graphicsDevice, int id, ref Texture2D texture)
        {
            if (!textures.TryGetValue(graphicsDevice, out texture))
            {
                using (System.IO.FileStream fs = new System.IO.FileStream(VideoConfig.FilePath, System.IO.FileMode.Open))
                {
                    texture = Texture2D.FromStream(graphicsDevice, fs);
                    Connected = true;
                }

                width = texture.Width;
                height = texture.Height;
                surfaceFormat = texture.Format;

                textures.Add(graphicsDevice, texture);
            }

            return true;
        }

        public override IEnumerable<Mode> GetModes()
        {
            yield break;
        }
    }
}
