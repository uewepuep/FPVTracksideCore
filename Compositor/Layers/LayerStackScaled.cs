using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Composition.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Composition.Layers
{
    public class LayerStackScaled : LayerStack
    {
        private RenderTarget2D renderTarget;
        private Drawer drawer;

        public float Scale { get; set; }

        public LayerStackScaled(GraphicsDevice graphicsDevice, LayerStackGame game, PlatformTools textRenderer)
            : base(graphicsDevice, game, textRenderer)
        {
            Scale = 1;
            drawer = new Drawer(GraphicsDevice, false);
        }

        public override void Dispose()
        {
            drawer?.Dispose();
            renderTarget?.Dispose();

            base.Dispose();
        }

        public override Rectangle GetBounds()
        {
            if (Scale == 1)
            {
                return base.GetBounds();
            }
            else
            {
                return new Rectangle(0, 0, (int)(Window.ClientBounds.Width * Scale), (int)(Window.ClientBounds.Height * Scale));
            }
        }

        public override void Draw()
        {
            InputEventFactory.ResolutionScale = Scale;

            if (Scale == 1)
            {
                base.Draw();
                return;
            }
            else
            {

                Rectangle client = Window.ClientBounds;
                client.X = 0;
                client.Y = 0;

                Rectangle scaled = new Rectangle(0, 0, (int)(Window.ClientBounds.Width * Scale), (int)(Window.ClientBounds.Height * Scale));
                if (scaled.Width == 0 || scaled.Height == 0)
                {
                    return;
                }

                try
                {
                    if (renderTarget == null)
                    {
                        renderTarget = new RenderTarget2D(GraphicsDevice, scaled.Width, scaled.Height);
                    }
                    else if (renderTarget.Width != scaled.Width || renderTarget.Height != scaled.Height)
                    {
                        renderTarget.Dispose();
                        renderTarget = new RenderTarget2D(GraphicsDevice, scaled.Width, scaled.Height);
                    }

                    GraphicsDevice.SetRenderTarget(renderTarget);
                    base.Draw();
                }
                finally
                {
                    GraphicsDevice.SetRenderTarget(null);

                    drawer.Begin();
                    drawer.Draw(renderTarget, scaled, client, Color.White, 1);
                    drawer.End();
                }
            }
        }
    }
}
