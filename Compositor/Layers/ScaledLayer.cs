using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Composition.Layers
{
    public class ScaledLayer : Layer
    {
        private RenderTarget2D renderTargetFull;
        private RenderTarget2D renderTargetSmall;
        private SpriteBatch spriteBatch;

        private GraphicsDevice graphicsDevice;

        public float Scale { get; set; }

        public ScaledLayer(GraphicsDevice graphicsDevice)
        {
            this.graphicsDevice = graphicsDevice;
            Scale = 1f;
            spriteBatch = new SpriteBatch(graphicsDevice);
        }

        public override void Dispose()
        {
            spriteBatch?.Dispose();
            renderTargetFull?.Dispose();
            renderTargetSmall?.Dispose();
            base.Dispose();
        }

        public override void Draw()
        {
            if (!Visible)
                return;

            //if (Scale == 1)
            //{
            //    OnDraw();
            //    return;
            //}

            if (LayerStack == null)
            {
                OnDraw();
                return;
            }

            Rectangle client = LayerStack.GetBounds();
            client.X = 0;
            client.Y = 0;

            Rectangle scaled = new Rectangle(0, 0, (int)(client.Width * Scale), (int)(client.Height * Scale));
            if (scaled.Width == 0 || scaled.Height == 0)
                return;

            if (renderTargetFull == null || renderTargetFull.Width != client.Width || renderTargetFull.Height != client.Height)
            {
                renderTargetFull?.Dispose();
                renderTargetFull = new RenderTarget2D(graphicsDevice, client.Width, client.Height);
            }

            if (renderTargetSmall == null || renderTargetSmall.Width != scaled.Width || renderTargetSmall.Height != scaled.Height)
            {
                renderTargetSmall?.Dispose();
                renderTargetSmall = new RenderTarget2D(graphicsDevice, scaled.Width, scaled.Height);
            }

            // Step 1: render layer at full resolution into renderTargetFull
            graphicsDevice.SetRenderTarget(renderTargetFull);
            OnDraw();

            // Step 2: downscale into renderTargetSmall
            graphicsDevice.SetRenderTarget(renderTargetSmall);
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp);
            spriteBatch.Draw(renderTargetFull, scaled, Color.White);
            spriteBatch.End();

            // Step 3: upscale back to screen (bilinear upscale = blur)
            graphicsDevice.SetRenderTarget(null);
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
            spriteBatch.Draw(renderTargetSmall, client, Color.White);
            spriteBatch.End();
        }
    }
}
