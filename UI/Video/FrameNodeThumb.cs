using Composition;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Video
{
    public class FrameNodeThumb : FrameNode, IUpdateableNode
    {
        public Texture2D Thumbnail { get { return renderTarget; } }

        private RenderTarget2D renderTarget;
        private object renderTargetLock;
        private Drawer drawer;

        public Size Size { get; set; }

        public bool ThumbnailEnabled { get; set; }

        public TextNode Info { get; set; }

        public bool DrawThumbnail { get; set; }

        public FrameNodeThumb(FrameSource s)
            : base(s)
        {
            DrawThumbnail = false;

            Size = new Size(8, 8);
            renderTargetLock = new object();
            ThumbnailEnabled = false;

            Info = new TextNode("", Color.White);
            Info.Style.Border = true;
            Info.RelativeBounds = new RectangleF(0, 0, 1, 0.05f);
            AddChild(Info);
        }

        public void SetVideoTimingInfo(int current, int max)
        {
            Info.Text = "Video Timing Info " + " Current " + current + ", Max " + max;
        }

        public override void Dispose()
        {
            lock (renderTargetLock)
            {
                if (renderTarget != null)
                {
                    renderTarget.Dispose();
                    renderTarget = null;
                }
            }

            drawer?.Dispose();
            drawer = null;
            base.Dispose();
        }

        public void Update(GameTime gameTime)
        {
            if (ThumbnailEnabled)
            {
                lock (renderTargetLock)
                {
                    if (renderTarget == null)
                    {
                        renderTarget = new RenderTarget2D(CompositorLayer.GraphicsDevice, Size.Width, Size.Height);
                    }

                    if (drawer == null)
                    {
                        drawer = new Drawer(CompositorLayer.GraphicsDevice, true);
                    }

                    if (renderTarget != null && drawer != null && texture != null)
                    {
                        DrawToTexture();
                    }
                }
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            base.Draw(id, parentAlpha);

            if (renderTarget != null && DrawThumbnail)
            {
                DebugTimer.DebugStartTime(this);

                Rectangle sourceBounds = new Rectangle(0, 0, Size.Width, Size.Height);

                Rectangle rectangle = Bounds;
                rectangle.Width = 48;
                rectangle.Height = 48;

                id.Draw(renderTarget, sourceBounds, rectangle, Tint, 1);
                DebugTimer.DebugEndTime(this);
            }
        }

        protected void DrawToTexture()
        {
            Rectangle sourceBounds = new Rectangle();
            sourceBounds.X = (int)(Texture.Width * RelativeSourceBounds.X);
            sourceBounds.Y = (int)(Texture.Height * RelativeSourceBounds.Y);
            sourceBounds.Width = (int)(Texture.Width * RelativeSourceBounds.Width);
            sourceBounds.Height = (int)(Texture.Height * RelativeSourceBounds.Height);

            sourceBounds = Flip(sourceBounds);

            lock (renderTargetLock)
            {
                try
                {
                    // Set the render target
                    CompositorLayer.GraphicsDevice.SetRenderTarget(renderTarget);
                    CompositorLayer.GraphicsDevice.Clear(Color.Transparent);

                    drawer.Begin();
                    drawer.Draw(texture, sourceBounds, new Rectangle(0, 0, Size.Width, Size.Height), Color.White, 1);
                    drawer.End();
                }
                finally
                {
                    // Drop the render target
                    CompositorLayer.GraphicsDevice.SetRenderTarget(null);
                }
            }
        }

        public Color[] GetColorData()
        {
            lock (renderTargetLock)
            {
                if (renderTarget != null && !renderTarget.IsDisposed)
                {
                    Color[] color = new Color[renderTarget.Width * renderTarget.Height];
                    renderTarget.GetData(color);
                    return color;
                }
            }
            return new Color[0];
        }
    }
}
