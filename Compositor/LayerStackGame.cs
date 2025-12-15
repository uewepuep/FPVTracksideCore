using Composition.Layers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace Composition
{
    public class LayerStackGame : Microsoft.Xna.Framework.Game
    {
        public LayerStackScaled LayerStack { get; private set; }
        public GraphicsDeviceManager GraphicsDeviceManager { get; private set; }

        public Color ClearColor { get; set; }

        private Size lastSize;

        public PlatformTools PlatformTools { get; protected set; }

        public LayerStackGame(PlatformTools platformTools)
        {
            Window.AllowUserResizing = true;
            GraphicsDeviceManager = new GraphicsDeviceManager(this);
            GraphicsDeviceManager.PreferredBackBufferHeight = 1000;
            GraphicsDeviceManager.PreferredBackBufferWidth = 1842;

            lastSize = new Size(0, 0);

            ClearColor = Color.CornflowerBlue;
            PlatformTools = platformTools;

            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            try
            {
                LayerStack = new LayerStackScaled(GraphicsDevice, this, PlatformTools);
            }
            catch(Exception e)
            {
                Tools.Logger.UI.LogException(this, e);
            }
        }

        protected override void Dispose(bool disposing)
        {
            LayerStack?.Dispose();
            LayerStack = null;

            base.Dispose(disposing);
        }

        public virtual void Restart()
        {
            PlatformTools.Invoke(() =>
            {
                UnloadContent();

                LayerStack?.Dispose();
                LayerStack = null;

                Initialize();
            });
        }

        protected override void Update(GameTime gameTime)
        {
            try
            {
                Size size = new Size(Window.ClientBounds.Width, Window.ClientBounds.Height);
                if (size.Width != lastSize.Width || size.Height != lastSize.Height)
                {
                    lastSize = size;

                    GraphicsDeviceManager.PreferredBackBufferWidth = size.Width;
                    GraphicsDeviceManager.PreferredBackBufferHeight = size.Height;
                    GraphicsDeviceManager.ApplyChanges();
                }

                if (LayerStack != null)
                {
                    LayerStack.Update(gameTime);
                }

                base.Update(gameTime);
            }
            catch (Exception e)
            {
                Tools.Logger.UI.LogException(this, e);
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            try
            {
                DoBackground();

                GraphicsDeviceManager.GraphicsDevice.Clear(ClearColor);
                if (LayerStack != null)
                {
                    LayerStack.Draw();
                }
                base.Draw(gameTime);
            }
            catch (Exception e)
            {
                Tools.Logger.UI.LogException(this, e);
            }
        }

        protected virtual void DoBackground()
        {
            LayerStack?.DoBackground();
        }
    }

    public class LayerStackGameBackgroundThread : LayerStackGame
    {

        private Thread background;
        private bool runBackground;

        private AutoResetEvent backgroundSet;
        private AutoResetEvent drawSet;

        public Viewport ViewPort { get; private set; }

        public LayerStackGameBackgroundThread(PlatformTools platformTools)
            :base(platformTools) 
        {
            backgroundSet = new AutoResetEvent(true);
            drawSet = new AutoResetEvent(false);

            if (platformTools.ThreadedDrawing)
            {
                background = new Thread(Background);
                background.Name = "LayerStackGame Background Draw";
                background.Start();
            }
            runBackground = platformTools.ThreadedDrawing;
        }

        protected override void Dispose(bool disposing)
        {
            if (background != null)
            {
                runBackground = false;
                drawSet.Set();
                background.Join();
            }

            base.Dispose(disposing);
        }

        private void Background()
        {
            try
            {
                while (runBackground)
                {
                    drawSet.WaitOne();

                    if (!runBackground)
                    {
                        break;
                    }

                    base.DoBackground();

                    backgroundSet.Set();
                }

                backgroundSet.Set();
            }
            catch (Exception ex) 
            {
                Tools.Logger.CrashLogger.Log(ex);
                throw ex;
            }
        }

        protected override bool BeginDraw()
        {
            backgroundSet.WaitOne();
            return base.BeginDraw();
        }

        protected override void EndDraw()
        {
            base.EndDraw();
            drawSet.Set();
        }

        protected override void DoBackground()
        {
            // done on the thread so this do nothing.
            if (!runBackground)
            {
                base.DoBackground();
                backgroundSet.Set();
            }
        }
    }
}
