using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Composition.Layers
{
    public class LayerStackDoubleBuffered : LayerStack
    {
        private Tools.DoubleBuffer<RenderTarget2D> doubleBuffer;
        private Drawer drawer;

        private Rectangle bounds;

        public int FrameRate { get; set; }

        private Thread drawThread;
        private bool draw;

        public LayerStackDoubleBuffered(GraphicsDevice graphicsDevice, LayerStackGame game, PlatformTools textRenderer)
            : base(graphicsDevice, game, textRenderer)
        {
            UpdateSize();
            drawer = new Drawer(GraphicsDevice);
            FrameRate = 60;
            draw = true;

            drawThread = new Thread(DrawContent);
            drawThread.Name = "LayerStackDrawThread";
            drawThread.Start();
        }

        public override void Dispose()
        {
            drawer?.Dispose();

            draw = false;
            drawThread.Join();

            base.Dispose();
        }

        private void UpdateSize()
        {
            RenderTarget2D a = new RenderTarget2D(GraphicsDevice, Window.ClientBounds.Width, Window.ClientBounds.Height);
            RenderTarget2D b = new RenderTarget2D(GraphicsDevice, Window.ClientBounds.Width, Window.ClientBounds.Height);
            doubleBuffer = new Tools.DoubleBuffer<RenderTarget2D>(a, b);

            bounds = new Rectangle(0, 0, Window.ClientBounds.Width, Window.ClientBounds.Height);
        }

        protected void DrawContent()
        {
            while (draw)
            {
                lock (doubleBuffer)
                {
                    try
                    {
                        RenderTarget2D write = doubleBuffer.Writable();
                        GraphicsDevice.SetRenderTarget(write);
                        base.Draw();
                        GraphicsDevice.SetRenderTarget(null);
                    }
                    finally
                    {
                        // Drop the render target
                        GraphicsDevice.SetRenderTarget(null);
                    }
                }
            }
        }

        public override void Draw()
        {
            RenderTarget2D read = doubleBuffer.Readable();
            lock (doubleBuffer)
            {
                drawer.Begin();
                drawer.Draw(read, bounds, bounds, Color.White, 1);
                drawer.End();

                doubleBuffer.Swap();
            }
        }
    }
}
