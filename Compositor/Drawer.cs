 using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Tools;
using System.Threading;
using Composition.Text;

namespace Composition
{
    public class Drawer : IDisposable
    {
        public GraphicsDevice GraphicsDevice { get; private set; }

        public SpriteBatch SpriteBatch { get; private set; }

        public int FrameCount { get; private set; }

        private Stack<Rectangle> clipRectangles;

        private BlendState blendState;

        private List<IDisposable> cleanup;
        private Queue<IPreProcessable> preProcessForced;
        private Queue<IPreProcessable> preProcessOptional;

        public TimeSpan PreProcessLimit { get; set; }
        public bool CanPreProcess { get; set; }

        private WorkQueue background;

        public TextureCache TextureCache { get; private set; }

        private AutoResetEvent autoresetevent; 

        public BitmapFont BitmapFonts { get; private set; }

        public Drawer(GraphicsDevice device, bool renderTarget)
        {
            TextureCache = new TextureCache(device);
            GraphicsDevice = device;
            SpriteBatch = new SpriteBatch(GraphicsDevice);
            clipRectangles = new Stack<Rectangle>();
            blendState = BlendState.AlphaBlend;
            cleanup = new List<IDisposable>();
            preProcessForced = new Queue<IPreProcessable>();
            preProcessOptional = new Queue<IPreProcessable>();
            PreProcessLimit = TimeSpan.FromMilliseconds(1000 / 120.0);
            CanPreProcess = true;

            autoresetevent = new AutoResetEvent(true);

            if (!renderTarget)
            {
                background = new WorkQueue("Drawer");
                background.Priority = ThreadPriority.BelowNormal;
            }
        }

        public void Dispose()
        {
            autoresetevent?.WaitOne(5000);
            autoresetevent?.Dispose();
            autoresetevent = null;

            SpriteBatch?.Dispose();
            SpriteBatch = null;

            background?.Dispose();
            background = null;
        }

        public void EnqueueBackgroundWork(Action action)
        {
            if (background != null)
            {
                background.Enqueue(action);
            }
            else
            {
                action();
            }
        }

        public void QuickDraw(Rectangle dest)
        {
            Draw(TextureCache.GetTextureFromColor(Color.Green), dest, Color.White, 0.5f);
        }

        public void Draw(Texture2D texture, Rectangle dest, Color tint, float alpha)
        {
            Draw(texture, new Rectangle(0, 0, texture.Width, texture.Height), dest, tint, alpha);
        }

        public void Draw(Texture2D texture, Rectangle src, Rectangle dest, Color tint, float alpha)
        {
            SpriteBatch?.Draw(texture, dest, src, Color.FromNonPremultiplied(new Vector4(tint.ToVector3(), alpha)));
        }

        public void PushClipRectangle(Rectangle clip)
        {
            SpriteBatch.End();

            clipRectangles.Push(clip);

            RasterizerState rasterizerState = new RasterizerState();
            rasterizerState.ScissorTestEnable = true;

            SpriteBatch.GraphicsDevice.ScissorRectangle = clip;

            SpriteBatch.Begin(SpriteSortMode.Immediate, blendState, SamplerState.AnisotropicClamp, null, rasterizerState, null, null);
        }

        public void PopClipRectangle()
        {
            if (clipRectangles.Any())
            {
                clipRectangles.Pop();

                if (clipRectangles.Any())
                {
                    SpriteBatch.GraphicsDevice.ScissorRectangle = clipRectangles.Peek();
                }
                else
                {
                    SpriteBatch.End();
                    SpriteBatch.Begin(SpriteSortMode.Deferred, blendState, SamplerState.AnisotropicClamp, null, null, null, null);
                }
            }
        }

        public void Begin()
        {
            if (autoresetevent == null)
                return;

            autoresetevent.Reset();

            FrameCount++;

            if (preProcessForced.Count > 0)
            {
                lock (preProcessForced)
                {
                    for (int i = 0; i < preProcessForced.Count; i++)
                    {
                        try
                        {
                            IPreProcessable todo = preProcessForced.Dequeue();
                            todo.PreProcess(this);
                        }
                        catch (Exception e)
                        {
                            Logger.AllLog.LogException(this, e);
                        }
                    }
                }
            }


            if (preProcessOptional.Count > 0)
            {
                DateTime last = DateTime.Now + PreProcessLimit;
                lock (preProcessOptional)
                {
                    for (int i = 0; i < preProcessOptional.Count && DateTime.Now < last; i++)
                    {
                        try
                        {
                            IPreProcessable todo = preProcessOptional.Dequeue();
                            todo.PreProcess(this);
                        }
                        catch (Exception e)
                        {
                            Logger.AllLog.LogException(this, e);
                        }
                    }
                }
            }

            clipRectangles.Clear();
            SpriteBatch.Begin(SpriteSortMode.Deferred, blendState, SamplerState.AnisotropicClamp, null, null, null, null);
        }

        public void End()
        {
            if (autoresetevent == null)
                return;

            SpriteBatch.End();

            autoresetevent.Set();

            lock (cleanup)
            {
                foreach (IDisposable toClean in cleanup)
                {
                    toClean.Dispose();
                }
                cleanup.Clear();
            }
        }

        public void CleanUp(IDisposable toClean)
        {
            if (toClean == null)
                return;

            lock (cleanup)
            {
                cleanup.Add(toClean);
            }
        }

        public void PreProcess(IPreProcessable toPreProcess, bool forced = false)
        {
            if (preProcessForced == null)
                return;

            if (!CanPreProcess)
            {
                toPreProcess.PreProcess(this);
                return;
            }

            if (forced)
            {
                lock (preProcessForced)
                {
                    if (!preProcessForced.Contains(toPreProcess))
                    {
                        preProcessForced.Enqueue(toPreProcess);
                    }
                }
            }
            else
            { 
                lock (preProcessOptional)
                {
                    if (!preProcessOptional.Contains(toPreProcess))
                    {
                        preProcessOptional.Enqueue(toPreProcess);
                    }
                }
            }
        }

        public bool InView(Rectangle rectangle)
        {
            Rectangle zeroedView = GraphicsDevice.Viewport.Bounds;
            zeroedView.X = 0;
            zeroedView.Y = 0;

            return zeroedView.Intersects(rectangle);
        }
    }

    public interface IPreProcessable
    {
        void PreProcess(Drawer id);
    }
}
