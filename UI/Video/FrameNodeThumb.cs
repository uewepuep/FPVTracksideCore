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
    public class FrameNodeThumb : FrameNode
    {
        private RenderTarget2D renderTarget;
        private Color[] colorData;
        private Drawer drawer;
        private object renderTargetLock;

        public Size Size { get; set; }

        public bool ThumbnailEnabled { get; set; }

        public TextNode Info { get; set; }

        public bool DrawThumbnail { get; set; }

        public FrameNodeThumb(FrameSource s)
            : base(s)
        {
            renderTargetLock = new object();

            DrawThumbnail = false;

            Size = new Size(8, 8);
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
                renderTarget?.Dispose();
                renderTarget = null;

                drawer?.Dispose();
                drawer = null;

                colorData = null;
            }

            base.Dispose();
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            base.Draw(id, parentAlpha);

            lock (renderTargetLock)
            {
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
        }

        public override void PreProcess(Drawer id)
        {
            base.PreProcess(id);

            lock (renderTargetLock)
            {
                if (renderTarget == null)
                {
                    renderTarget = new RenderTarget2D(CompositorLayer.GraphicsDevice, Size.Width, Size.Height);
                    colorData = new Color[renderTarget.Width * renderTarget.Height];
                }

                if (drawer == null)
                {
                    drawer = new Drawer(CompositorLayer.GraphicsDevice, true);
                    drawer.CanPreProcess = false;
                }

                if (renderTarget != null && drawer != null && texture != null)
                {
                    DrawToTexture();

                    if (colorData != null)
                        renderTarget.GetData(colorData);
                }
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

            try
            {
                // Set the render target
                drawer.GraphicsDevice.SetRenderTarget(renderTarget);
                drawer.GraphicsDevice.Clear(Color.Transparent);

                drawer.Begin();
                drawer.Draw(texture, sourceBounds, new Rectangle(0, 0, Size.Width, Size.Height), Color.White, 1);
                drawer.End();
            }
            finally
            {
                // Drop the render target
                drawer.GraphicsDevice.SetRenderTarget(null);
            }
        }

        public Color[] GetColorData()
        {
            return colorData;
        }
    }
}
