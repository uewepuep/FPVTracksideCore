using Composition.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
using Tools;

namespace Composition.Nodes
{
    public interface IScrollableNode
    {
        public ScrollerNode Scroller { get; }
        public Point ScrollOffset { get; }
    }


    public class ScrollerNode : ColorNode
    {
        public enum Types
        {
            Horizontal,
            VerticalLeft,
            VerticalRight
        }

        public Types ScrollType { get; set; }

        public delegate void ScrollAmount(float scrollPX);

        public float ContentSizePixels { get; set; }
        public float ViewSizePixels { get; set; }

        public float CurrentScrollRelative
        {
            get
            {
                if (ViewSizePixels == 0) return 0;

                return CurrentScrollPixels / ViewSizePixels;
            }
        }

        public Node Scrollable { get; private set; }

        public int Width { get; set; }

        public bool Needed { get; private set; }
        public float MaxScroll { get { return ContentSizePixels - ViewSizePixels; }  }

        public bool Dragging { get; private set; }

        private bool sendToEnd;

        private InterpolatedFloat currentScrollPixels;

        public float CurrentScrollPixels { get { return currentScrollPixels.Output; } }

        public bool Enabled { get; set; }

        public Point grabOffset;

        public event Action OnSelfLayout;

        private bool needsSnap;

        public ScrollerNode(Node scrollable, Types direction)
            :this(scrollable, direction, new Color(236, 236, 236, 128))
        {
        }

        public ScrollerNode(Node scrollable, Types scrollDirection, Color color)
            : base (color)
        {
            currentScrollPixels = new InterpolatedFloat(0, 0, TimeSpan.FromSeconds(0.2f));

            Width = 10;

            Scrollable = scrollable;
            ScrollType = scrollDirection;

            Enabled = true;
        }

        public void SelfLayout()
        {
            if (Scrollable != null)
            {
                Layout(Scrollable.BoundsF);
                OnSelfLayout?.Invoke();
            }
        }

        public override void Layout(RectangleF parentBounds)
        {
            float lengthFactor = ViewSizePixels / ContentSizePixels;

            bool wasNeeded = Needed;
            Needed = lengthFactor < 1;

            if (!Needed && wasNeeded)
            {
                needsSnap = true;
            }

            if (sendToEnd)
            {
                ScrollTo(MaxScroll);
                sendToEnd = false;
            }

            if (Needed && Enabled)
            {
                int length = (int)(lengthFactor * ViewSizePixels);

                float positionFactor = currentScrollPixels.Target / MaxScroll;
                int position = (int)(positionFactor * (ViewSizePixels - length));

                switch (ScrollType)
                {
                    case Types.Horizontal:
                        BoundsF = new RectangleF(position + parentBounds.X, parentBounds.Bottom - Width, length, Width);
                        break;

                    case Types.VerticalLeft:
                        BoundsF = new RectangleF(parentBounds.Left, position + parentBounds.Y, Width, length);
                        break;

                    case Types.VerticalRight:
                        BoundsF = new RectangleF(parentBounds.Right - Width, position + parentBounds.Y, Width, length);
                        break;
                }
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (needsSnap)
            {
                ScrollTo(0);
                SnapScroll();
                SelfLayout();
                needsSnap = false;
            }

            if (Needed && Visible && Enabled)
            {
                if (Dragging)
                {
                    MouseState mouseState = Mouse.GetState(Scrollable.CompositorLayer.LayerStack.Window);
                    if (mouseState.LeftButton == ButtonState.Released)
                    {
                        Dragging = false;
                    }

                    Point position = Mouse.GetState().Position + grabOffset;

                    Rectangle sb = Scrollable.Bounds;
                    switch (ScrollType)
                    {
                        case Types.Horizontal:
                            if (sb.Width != 0)
                                ScrollFactor((position.X - sb.X) / (float)sb.Width);
                            break;

                        case Types.VerticalLeft:
                        case Types.VerticalRight:
                            if (sb.Height != 0)
                                ScrollFactor((position.Y - sb.Y) / (float)sb.Height);
                            break;
                    }

                    SelfLayout();
                }

                if (!currentScrollPixels.Finished)
                {
                    SelfLayout();
                }

                base.Draw(id, parentAlpha);
            }
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (!Visible || !Needed || !Enabled)
            {
                return false;
            }

            if (BoundsF.Contains(mouseInputEvent.Position))
            {
                if (mouseInputEvent.ButtonState == ButtonStates.Pressed)
                {
                    Dragging = true;
                    grabOffset = Bounds.Location - mouseInputEvent.Position;
                }

                return true;
            }

            if (mouseInputEvent.WheelChange != 0)
            {
                ScrollAdd(-mouseInputEvent.WheelChange);
                return true;
            }

            return false;
        }

        public void ScrollTo(float point, TimeSpan time)
        {
            point = Limit(point);
            currentScrollPixels.SetTarget(point, time);
        }

        public void SnapScroll()
        {
            currentScrollPixels.Snap();
        }

        public void ScrollFactor(float factor)
        {
            float point = factor * ContentSizePixels;
            ScrollTo(point);
        }

        public void ScrollTo(float point)
        {
            ScrollTo(point, TimeSpan.FromSeconds(0.1f));
        }

        public void ScrollAdd(float addition)
        {
            float newValue = currentScrollPixels.Target + addition;
            ScrollTo(newValue, TimeSpan.FromSeconds(0.1f));
        }

        public void ScrollToStart(TimeSpan time)
        {
            ScrollTo(MaxScroll);
        }

        public void ScrollToEnd(TimeSpan time)
        {
            if (CurrentScrollPixels < MaxScroll)
            {
                ScrollTo(MaxScroll);
                sendToEnd = true;
            }
        }

        private float Limit(float input)
        {
            int maxScroll = (int)MaxScroll;
            if (maxScroll < 0) maxScroll = 0;

            if (input < 0) input = 0;
            if (input > maxScroll) input = maxScroll;
            return input;
        }

        protected override string GetNodeName()
        {
            return "ScrollBar " + ScrollType;
        }

        public Point Translate(Point point)
        {
            // Scroller
            switch (ScrollType)
            {
                case Types.Horizontal:
                    point.X += (int)CurrentScrollPixels;
                    break;
                case Types.VerticalLeft:
                case Types.VerticalRight:
                    point.Y += (int)CurrentScrollPixels;
                    break;
            }
            return point;
        }

        public MouseInputEvent Translate(MouseInputEvent input)
        {
            Point translation = Translate(Point.Zero);

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
    }
}
