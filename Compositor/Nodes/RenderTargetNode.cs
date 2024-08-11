using Composition.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class RenderTargetNode : ImageNode, IUpdateableNode, IPreProcessable, IScrollableNode
    {
        protected RenderTarget2D renderTarget
        {
            get
            {
                return (RenderTarget2D)texture;
            }
            set
            {
                texture = value;
            }
        }

        private object renderTargetLock;
        protected Drawer drawer;

        private Size size;
        public Size Size
        {
            get => size;
            set
            {
                if (size.Width != value.Width || size.Height != value.Height)
                {
                    size = value;
                    SetAspectRatio(size);
                    RequestRedraw();
                }
            }
        }

        public bool LayoutDefinesSize { get; set; }

        private bool hasLayedOut;
        private Rectangle lastLayoutBounds;

        public ScrollerNode Scroller { get; private set; }
        public Point ScrollOffset
        {
            get
            {
                return new Point((int)BoundsF.X, (int)BoundsF.Y);
            }
        }
        public bool CanScroll
        {
            get
            {
                return Scroller.Visible;
            }
            set
            {
                if (Scroller != null)
                {
                    Scroller.Visible = value;
                }
            }
        }

        private bool disposed;

        public HoverNode HoverNode { get; set; }
        public Color ClearColor { get; set; }

        protected int lastDrawFrame;

        public RenderTargetNode()
            : this(128, 128)
        {
            LayoutDefinesSize = true;
            hasLayedOut = false;
        }

        public RenderTargetNode(int width, int height)
        {
            Size = new Size(width, height);
            renderTargetLock = new object();
            Scroller = new ScrollerNode(this, ScrollerNode.Types.VerticalRight);
            HoverNode = null;
            ClearColor = Color.Transparent;
        }

        public override void Dispose()
        {
            disposed = true;

            if (renderTarget != null)
            {
                CompositorLayer.CleanUp(renderTarget);
                renderTarget = null;
            }

            if (drawer != null)
            {
                CompositorLayer.CleanUp(drawer);
                drawer = null;
            }

            Scroller.Dispose();
            base.Dispose();
        }

        // Need to use basebounds as ImageNode over-rides the Bounds size based on image size. But our image size is dynamic..
        public override bool Contains(Point point)
        {
            return BaseBoundsF.Contains(point);
        }

        public override void Layout(RectangleF parentBounds)
        {
            BoundsF = CalculateRelativeBounds(parentBounds);

            bool isAnimatingSize = IsAnimatingSize();

            if (lastLayoutBounds.Width != BaseBounds.Width || lastLayoutBounds.Height != BaseBounds.Height || NeedsLayout)
            {
                if (LayoutDefinesSize)
                {
                    if (!isAnimatingSize)
                    {
                        Size = new Size(BaseBounds.Width, BaseBounds.Height);
                        hasLayedOut = true;
                        NeedsLayout = false;

                        LayoutChildren(new RectangleF(0, 0, Size.Width, Size.Height));
                        NeedsDraw = true;
                        lastLayoutBounds = BaseBounds;
                    }
                }
                else
                {
                    hasLayedOut = true;
                    NeedsLayout = false;
                    LayoutChildren(new RectangleF(0, 0, Size.Width, Size.Height));
                    NeedsDraw = true;
                    lastLayoutBounds = BaseBounds;
                }

            }

            if (renderTarget != null && !isAnimatingSize)
            {
                RectangleF actualBounds = new RectangleF(Bounds.X, Bounds.Y, Size.Width, Size.Height);
                Scroller.Layout(actualBounds);

                switch (Scroller.ScrollType)
                {
                    case ScrollerNode.Types.Horizontal:
                        Scroller.ViewSizePixels = Bounds.Width;
                        Scroller.ContentSizePixels = Size.Width;
                        break;
                    case ScrollerNode.Types.VerticalLeft:
                    case ScrollerNode.Types.VerticalRight:
                        Scroller.ViewSizePixels = Bounds.Height;
                        Scroller.ContentSizePixels = Size.Height;
                        break;
                }
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            lastDrawFrame = id.FrameCount;

            DebugTimer.DebugStartTime(this);
            if (!CompositorLayer.InView(Bounds))
                return;

            float alpha = parentAlpha * Alpha;
            if (Tint.A != 255)
            {
                alpha *= Tint.A / 255.0f;
            }
            lock (renderTargetLock)
            {
                if (renderTarget != null)
                {
                    Rectangle sourceBounds = new Rectangle(0, 0, renderTarget.Width, renderTarget.Height);
                    Rectangle destBounds = Bounds;

                    if (!CanScale)
                    {
                        destBounds.Width = renderTarget.Width;
                        destBounds.Height = renderTarget.Height;

                        if (Scroller.Visible)
                        {
                            switch (Scroller.ScrollType)
                            {
                                case ScrollerNode.Types.Horizontal:
                                    sourceBounds.X += (int)Scroller.CurrentScrollPixels;

                                    if (sourceBounds.Width > Scroller.ViewSizePixels)
                                    {
                                        sourceBounds.Width = (int)Scroller.ViewSizePixels;
                                        destBounds.Width = (int)Scroller.ViewSizePixels;
                                    }
                                    break;
                                case ScrollerNode.Types.VerticalLeft:
                                case ScrollerNode.Types.VerticalRight:
                                    sourceBounds.Y += (int)Scroller.CurrentScrollPixels;

                                    if (sourceBounds.Height > Scroller.ViewSizePixels)
                                    {
                                        sourceBounds.Height = (int)Scroller.ViewSizePixels;
                                        destBounds.Height = (int)Scroller.ViewSizePixels;
                                    }
                                    break;
                            }
                        }
                    }

                    try
                    {
                        id.Draw(renderTarget, sourceBounds, destBounds, Tint, alpha);

                        HoverNode hoverNode = HoverNode;
                        if (hoverNode != null)
                        {
                            // Draw a hovernode over the top of everything
                            hoverNode.RenderTargetDraw(id, parentAlpha);
                        }
                    }
                    catch
                    {
                        if (renderTarget != null)
                        {
                            renderTarget.Dispose();
                            renderTarget = null;
                        }

                        if (drawer != null)
                        {
                            drawer.Dispose();
                            drawer = null;
                        }
                    }
                }
            }

            Scroller.Draw(id, parentAlpha);
            DebugTimer.DebugEndTime(this);

            if (NeedsDraw) 
            { 
                NeedsDraw = false;
                id.PreProcess(this);
            }
        }

        public virtual void Update(GameTime gameTime)
        {
            DebugTimer.DebugStartTime(this);
            if (NeedsLayout && Parent != null)
            {
                Layout(Parent.BoundsF);
                NeedsLayout = false;
                NeedsDraw = true;
            }

            bool canRender = !LayoutDefinesSize || (LayoutDefinesSize && hasLayedOut);
            if (canRender && !disposed)
            {
                if (lastDrawFrame == CompositorLayer.FrameNumber)
                {
                    lock (renderTargetLock)
                    {
                        if (drawer == null)
                        {
                            drawer = new Drawer(CompositorLayer.GraphicsDevice);
                            drawer.CanMultiThread = false;
                        }

                        Size maxSize = Size;

                        maxSize.Width = Math.Min(4096, maxSize.Width);
                        maxSize.Height = Math.Min(4096, maxSize.Height);

                        bool isAnimating = false;
                        if (Parent != null && Parent.IsAnimating())
                        {
                            isAnimating = true;
                        }

                        if (renderTarget != null && !isAnimating && (maxSize.Width != renderTarget.Width || maxSize.Height != renderTarget.Height))
                        {
                            renderTarget.Dispose();
                            renderTarget = null;
                        }

                        if (renderTarget == null && maxSize.Width > 0 && maxSize.Height > 0)
                        {
                            renderTarget = CreateRenderTarget(maxSize);
                            NeedsDraw = true;
                        }
                    }
                }
            }
            DebugTimer.DebugEndTime(this);
        }

        protected virtual RenderTarget2D CreateRenderTarget(Size maxSize)
        {
            return new RenderTarget2D(drawer.GraphicsDevice, maxSize.Width, maxSize.Height);
        }

        public virtual void PreProcess(Drawer id)
        {
            if (drawer != null)
            {
                if (drawer.CanMultiThread)
                {
                    drawer.DoPreProcess();
                }
            }

            if (renderTarget != null)
            {
                DrawToTexture(drawer);
            }
            else
            {
                NeedsDraw = true;
            }
        }

        protected void DrawToTexture(Drawer id)
        {
            lock (renderTargetLock)
            {
                try
                {
                    if (id != null)
                    {
                        // Set the render target
                        id.GraphicsDevice.SetRenderTarget(renderTarget);
                        id.GraphicsDevice.Clear(ClearColor);
                        //#if DEBUG
                        //                    Random r = new Random(lastDrawFrame);
                        //                    id.GraphicsDevice.Clear(new Color((float)r.NextDouble(), (float)r.NextDouble(), (float)r.NextDouble()));
                        //#endif

                        DrawContent(id);
                    }
                }
                catch
                {
                    if (renderTarget != null)
                    {
                        renderTarget.Dispose();
                        renderTarget = null;
                    }

                    if (drawer != null)
                    {
                        drawer.Dispose();
                        drawer = null;
                    }
                }
                finally
                {
                    // Drop the render target
                    id.GraphicsDevice.SetRenderTarget(null);
                }
            }
            if (drawer != null && drawer.CanMultiThread)
            {
                Parent?.RequestRedraw();
            }
        }

        protected virtual void DrawContent(Drawer id)
        {
            id.Begin();
            DrawChildren(id, 1);
            id.End();
        }

        public override void RequestLayout()
        {
            NeedsLayout = true;

            // Layouts still need to go up
            base.RequestLayout();
        }

        public override void RequestRedraw()
        {
            NeedsDraw = true;

            // Layouts still need to go up
            base.RequestRedraw();
        }

        // Mouse events all need to be translated.
        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
#if DEBUG
            if (mouseInputEvent.Button == MouseButtons.Right && mouseInputEvent.ButtonState == ButtonStates.Pressed)
            {
                if (texture != null && Keyboard.GetState().IsKeyDown(Keys.LeftAlt))
                {
                    string filename = Address;
                    System.Text.RegularExpressions.Regex rgx = new System.Text.RegularExpressions.Regex("[^a-zA-Z0-9 -]");
                    filename = rgx.Replace(filename, "");

                    texture.SaveAs(filename + ".png");
                }
            }
#endif

            if (Scroller.OnMouseInput(mouseInputEvent))
            {
                return true;
            }

            if (BaseBoundsF.Contains(mouseInputEvent.Position) || mouseInputEvent is MouseInputLeaveEvent)
            {
                MouseInputEvent translated = Translate(mouseInputEvent);
                return base.OnMouseInput(translated);
            }
            return false;
        }

        public MouseInputEvent Translate(MouseInputEvent input)
        {
            Point translation = new Point(-Bounds.X, -Bounds.Y);

            if (Scroller != null)
            {
                translation = Scroller.Translate(translation);
            }

            MouseInputEvent output = new MouseInputEvent(input, translation);

            if (input is MouseInputEnterEvent)
            {
                output = new MouseInputEnterEvent(output);
            }
            else if (input is MouseInputEnterEvent)
            {
                output = new MouseInputEnterEvent(output);
            }
            return output;
        }

        public override bool OnDrop(MouseInputEvent mouseInputEvent, Node node)
        {
            MouseInputEvent translated = Translate(mouseInputEvent);
            return base.OnDrop(translated, node);
        }

        // These should all just return false, as the render target breaks the animation knowledge chain.
        public override bool IsAnimating()
        {
            return false;
        }

        public override bool IsAnimatingInvisiblity()
        {
            return false;
        }

        public override bool IsAnimatingSize()
        {
            return false;
        }
        public override IEnumerable<Node> GetRecursiveChildren()
        {
            yield return this;
        }
    }

}
