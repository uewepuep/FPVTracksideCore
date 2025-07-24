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
            Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - BEGIN");
            Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Current thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            
            bool lockAcquired = false;
            try
            {
                Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Attempting to acquire renderTargetLock with 2 second timeout");
                if (System.Threading.Monitor.TryEnter(renderTargetLock, 2000))
                {
                    lockAcquired = true;
                    Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Acquired renderTargetLock successfully");
                    
                    try
                    {
                        // Dispose renderTarget with timeout protection
                        if (renderTarget != null)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Disposing renderTarget with timeout protection");
                            var renderTargetDisposeTask = System.Threading.Tasks.Task.Run(() => 
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - RenderTarget dispose task started on thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                                renderTarget.Dispose();
                                Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - RenderTarget dispose task completed successfully");
                            });
                            
                            if (renderTargetDisposeTask.Wait(100))
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - RenderTarget disposed successfully");
                            }
                            else
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - WARNING: RenderTarget dispose timed out after 1 second, continuing anyway");
                            }
                            renderTarget = null;
                        }

                        // Dispose drawer with timeout protection
                        if (drawer != null)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Disposing drawer with timeout protection");
                            var drawerDisposeTask = System.Threading.Tasks.Task.Run(() => 
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Drawer dispose task started on thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                                drawer.Dispose();
                                Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Drawer dispose task completed successfully");
                            });
                            
                            if (drawerDisposeTask.Wait(100))
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Drawer disposed successfully");
                            }
                            else
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - WARNING: Drawer dispose timed out after 1 second, continuing anyway");
                            }
                            drawer = null;
                        }

                        colorData = null;
                        Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Graphics resources disposed successfully");
                    }
                    catch (System.Exception ex)
                    {
                        Tools.Logger.VideoLog.LogException(this, ex);
                        Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Exception during graphics disposal, continuing");
                    }
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - WARNING: Could not acquire renderTargetLock within 2 seconds, skipping graphics disposal to prevent deadlock");
                }
            }
            finally
            {
                if (lockAcquired)
                {
                    System.Threading.Monitor.Exit(renderTargetLock);
                    Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Released renderTargetLock");
                }
            }

            Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Calling base.Dispose()");
            base.Dispose();
            Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - SUCCESS");
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
                    drawer = new Drawer(CompositorLayer.GraphicsDevice);
                    drawer.CanMultiThread = false;
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
            Texture2D texture2d = Texture;
            if (texture2d == null || drawer == null || renderTarget == null)
                return;

            Rectangle sourceBounds = new Rectangle();
            sourceBounds.X = (int)(texture2d.Width * RelativeSourceBounds.X);
            sourceBounds.Y = (int)(texture2d.Height * RelativeSourceBounds.Y);
            sourceBounds.Width = (int)(texture2d.Width * RelativeSourceBounds.Width);
            sourceBounds.Height = (int)(texture2d.Height * RelativeSourceBounds.Height);

            sourceBounds = Flip(sourceBounds);

            try
            {
                // Set the render target
                drawer.GraphicsDevice.SetRenderTarget(renderTarget);
                drawer.GraphicsDevice.Clear(Color.Transparent);

                drawer.Begin();
                drawer.Draw(texture2d, sourceBounds, new Rectangle(0, 0, Size.Width, Size.Height), Color.White, 1);
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
