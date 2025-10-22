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
        private volatile bool isDisposing;

        public Size Size { get; set; }

        public bool ThumbnailEnabled { get; set; }

        public TextNode Info { get; set; }

        public bool DrawThumbnail { get; set; }

        public FrameNodeThumb(FrameSource s)
            : base(s)
        {
            renderTargetLock = new object();
            isDisposing = false;

            DrawThumbnail = false;

            Size = new Size(8, 8);
            ThumbnailEnabled = false;

            Info = new TextNode("", Color.White);
            Info.Style.Border = true;
            Info.RelativeBounds = new RectangleF(0, 0, 1, 0.05f);
            AddChild(Info);
            
            // Subscribe to our own ImageArrived handler AFTER the base class has subscribed
            // This way we can control the processing during disposal
            Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb subscribing to additional OnFrameEvent for disposal control");
            if (Source != null)
            {
                Source.OnFrameEvent += ImageArrivedSafe;
            }
        }

        public void SetVideoTimingInfo(int current, int max)
        {
            Info.Text = "Video Timing Info " + " Current " + current + ", Max " + max;
        }

        public override void Dispose()
        {
            Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - BEGIN");
            Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Current thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            
            // Signal that disposal is starting to prevent any new graphics operations
            // This MUST be set first before any other disposal logic
            isDisposing = true;
            
            // Immediately unsubscribe from frame events to prevent new ImageArrived calls
            if (Source != null)
            {
                Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Unsubscribing from frame events");
                // Unsubscribe our safe handler early to prevent race conditions
                try
                {
                    Source.OnFrameEvent -= ImageArrivedSafe;
                }
                catch (System.Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                }
            }
            
            // Small delay to allow any in-flight operations to complete
            System.Threading.Thread.Sleep(100);
            
            bool lockAcquired = false;
            try
            {
                Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Attempting to acquire renderTargetLock with minimal timeout");
                if (System.Threading.Monitor.TryEnter(renderTargetLock, 50))
                {
                    lockAcquired = true;
                    Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Acquired renderTargetLock successfully");
                    
                    // Immediately null references to prevent further use
                    var renderTargetToDispose = renderTarget;
                    var drawerToDispose = drawer;
                    renderTarget = null;
                    drawer = null;
                    colorData = null;
                    
                    // Dispose graphics resources asynchronously without blocking UI
                    if (renderTargetToDispose != null || drawerToDispose != null)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Scheduling async graphics disposal");
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            try
                            {
                                // Additional delay before disposal to ensure all texture operations are complete
                                System.Threading.Thread.Sleep(50);
                                
                                if (renderTargetToDispose != null)
                                {
                                    Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Async disposing RenderTarget");
                                    renderTargetToDispose.Dispose();
                                }
                                
                                if (drawerToDispose != null)
                                {
                                    Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Async disposing Drawer");
                                    drawerToDispose.Dispose();
                                }
                                
                                Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Async graphics disposal completed");
                            }
                            catch (System.Exception ex)
                            {
                                Tools.Logger.VideoLog.LogException(this, ex);
                                Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Exception during async graphics disposal");
                            }
                        });
                    }
                    
                    Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Graphics resources scheduled for async disposal");
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FrameNodeThumb.Dispose() - Could not acquire renderTargetLock quickly, skipping graphics disposal to prevent UI blocking");
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

        // Safe ImageArrived handler that prevents processing during disposal
        private void ImageArrivedSafe(long sampleTime, long processNumber)
        {
            if (isDisposing)
            {
                // Log only every 30 frames to avoid spam during disposal
                if (processNumber % 30 == 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"ImageArrivedSafe: Ignoring frame {processNumber} - disposal in progress");
                }
                return;
            }

            // All processing is now safe because we check isDisposing
            // The base class ImageArrived will still be called, but our PreProcess method
            // will bail out early if isDisposing is true
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (isDisposing) return;
            
            base.Draw(id, parentAlpha);

            lock (renderTargetLock)
            {
                if (!isDisposing && renderTarget != null && !renderTarget.IsDisposed && DrawThumbnail)
                {
                    try
                    {
                        DebugTimer.DebugStartTime(this);

                        Rectangle sourceBounds = new Rectangle(0, 0, Size.Width, Size.Height);

                        Rectangle rectangle = Bounds;
                        rectangle.Width = 48;
                        rectangle.Height = 48;

                        id.Draw(renderTarget, sourceBounds, rectangle, Tint, 1);
                        DebugTimer.DebugEndTime(this);
                    }
                    catch (System.ObjectDisposedException)
                    {
                        // RenderTarget was disposed during async cleanup - ignore silently
                    }
                    catch (System.Exception ex)
                    {
                        Tools.Logger.VideoLog.LogException(this, ex);
                    }
                }
            }
        }

        public override void PreProcess(Drawer id)
        {
            if (isDisposing) return;
            
            base.PreProcess(id);

            lock (renderTargetLock)
            {
                if (isDisposing) return;
                
                try
                {
                    if (renderTarget == null || renderTarget.IsDisposed)
                    {
                        renderTarget = new RenderTarget2D(CompositorLayer.GraphicsDevice, Size.Width, Size.Height);
                        colorData = new Color[renderTarget.Width * renderTarget.Height];
                    }

                    if (drawer == null)
                    {
                        drawer = new Drawer(CompositorLayer.GraphicsDevice);
                        drawer.CanMultiThread = false;
                    }

                    if (!isDisposing && renderTarget != null && !renderTarget.IsDisposed && drawer != null && texture != null)
                    {
                        DrawToTexture();

                        if (!isDisposing && colorData != null)
                            renderTarget.GetData(colorData);
                    }
                }
                catch (System.ObjectDisposedException)
                {
                    // Graphics resources were disposed during async cleanup - skip this frame
                }
                catch (System.Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                }
            }
        }

        protected void DrawToTexture()
        {
            if (isDisposing) return;
            
            Texture2D texture2d = Texture;
            if (isDisposing || texture2d == null || drawer == null || renderTarget == null || renderTarget.IsDisposed)
                return;

            Rectangle sourceBounds = new Rectangle();
            sourceBounds.X = (int)(texture2d.Width * RelativeSourceBounds.X);
            sourceBounds.Y = (int)(texture2d.Height * RelativeSourceBounds.Y);
            sourceBounds.Width = (int)(texture2d.Width * RelativeSourceBounds.Width);
            sourceBounds.Height = (int)(texture2d.Height * RelativeSourceBounds.Height);

            sourceBounds = Flip(sourceBounds);

            try
            {
                if (isDisposing) return;
                
                // Set the render target
                drawer.GraphicsDevice.SetRenderTarget(renderTarget);
                drawer.GraphicsDevice.Clear(Color.Transparent);

                drawer.Begin();
                drawer.Draw(texture2d, sourceBounds, new Rectangle(0, 0, Size.Width, Size.Height), Color.White, 1);
                drawer.End();
            }
            catch (System.ObjectDisposedException)
            {
                // Resources were disposed during async cleanup - ignore silently
            }
            finally
            {
                try
                {
                    // Drop the render target
                    if (!isDisposing && drawer != null)
                        drawer.GraphicsDevice.SetRenderTarget(null);
                }
                catch (System.ObjectDisposedException)
                {
                    // GraphicsDevice was disposed - ignore silently
                }
            }
        }

        public Color[] GetColorData()
        {
            return colorData;
        }
    }
}
