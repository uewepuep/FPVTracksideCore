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
        public bool CanMultiThread { get; set; }

        private AutoResetEvent autoresetevent;

        public TextureCache TextureCache { get; private set; }

        public BitmapFont BitmapFonts { get; private set; }

        public Point Offset { get; set; }

        private bool hasBegun;

        public Drawer(GraphicsDevice device)
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
            CanMultiThread = true;

            Offset = Point.Zero;

            autoresetevent = new AutoResetEvent(true);
        }

        public void Dispose()
        {
            IDisposable[] disposables;
            lock (cleanup)
            {
                disposables = cleanup.ToArray();
                cleanup.Clear();
            }

            foreach (IDisposable toClean in disposables)
            {
                toClean.Dispose();
            }

            autoresetevent?.WaitOne(5000);
            autoresetevent?.Dispose();
            autoresetevent = null;

            SpriteBatch?.Dispose();
            SpriteBatch = null;
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
            dest.X += Offset.X;
            dest.Y += Offset.Y;

            SpriteBatch?.Draw(texture, dest, src, Color.FromNonPremultiplied(new Vector4(tint.ToVector3(), alpha)));
        }

        public void Draw(Texture2D texture, Rectangle src, RectangleF dest, Color tint, float alpha)
        {
            dest.X += Offset.X;
            dest.Y += Offset.Y;

            Vector2 scale = new Vector2(dest.Width / src.Width, dest.Height / src.Height);
            SpriteBatch?.Draw(texture, dest.Position, src, Color.FromNonPremultiplied(new Vector4(tint.ToVector3(), alpha)), 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        }

        public void PushClipRectangle(Rectangle clip)
        {
            if (SpriteBatch == null)
                return;

            SpriteBatch.End();

            clipRectangles.Push(clip);

            RasterizerState rasterizerState = new RasterizerState();
            rasterizerState.ScissorTestEnable = true;

            SpriteBatch.GraphicsDevice.ScissorRectangle = clip;

            SpriteBatch.Begin(SpriteSortMode.Deferred, blendState, SamplerState.AnisotropicClamp, null, rasterizerState, null, null);
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

        public void DoPreProcess()
        {
            DebugTimer.DebugStartTime("PreProcess " + Thread.CurrentThread.Name);

            if (preProcessForced.Count > 0)
            {
                lock (preProcessForced)
                {
                    for (int i = 0; i < preProcessForced.Count; i++)
                    {
                        try
                        {
                            IPreProcessable todo = preProcessForced.Dequeue();

                            if (todo != null && !todo.Disposed)
                            {
                                todo.PreProcess(this);
                            }
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

            DebugTimer.DebugEndTime("PreProcess " + Thread.CurrentThread.Name);
        }

        public void Begin()
        {
            if (autoresetevent == null || hasBegun)
                return;

            autoresetevent.Reset();

            FrameCount++;

            clipRectangles.Clear();

            SpriteBatch sb = SpriteBatch;
            if (sb != null) 
            {
                sb.Begin(SpriteSortMode.Deferred, blendState, SamplerState.AnisotropicClamp, null, null, null, null);
                hasBegun = true;
            }
        }

        public void End()
        {
            SpriteBatch sb = SpriteBatch;
            if (autoresetevent == null || sb == null || !hasBegun)
                return;

            sb.End();
            hasBegun = false;

            autoresetevent.Set();

            if (cleanup.Any())
            {
                IDisposable[] disposables;
                lock (cleanup)
                {
                    disposables = cleanup.ToArray();
                    cleanup.Clear();
                }

                foreach (IDisposable toClean in disposables)
                {
                    toClean.Dispose();
                }
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

            if (!CanMultiThread)
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
    }

    public interface IPreProcessable
    {
        void PreProcess(Drawer id);
        bool Disposed { get; }
    }
}
