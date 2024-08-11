using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Tools;

namespace Composition.Nodes
{
    public class ListNode<T> : Node, IScrollableNode where T : Node
    {
        public enum ListStyles
        {
            TopDown,
            BottomUp
        };

        public int ItemHeight { get; set; }
        public int ItemPadding { get; set; }

        public int ItemHeightFull 
        {
            get
            {
                return ItemHeight + ItemPadding;
            }
        }

        public ListStyles ListStyle { get; set; }

        public IEnumerable<T> ChildrenOfType { get { return Children.OfType<T>(); } }

        public bool LayoutInvisibleItems { get; set; }

        public Color[] BackgroundColors { get; set; }

        public bool ShrinkContentsForScrollers { get; set; }

        public ScrollerNode Scroller { get; private set; }

        public Point ScrollOffset
        {
            get
            {
                return Point.Zero;
            }
        }

        private Size size;
        public Size Size
        {
            get => size;
            set
            {
                if (size.Width != value.Width || size.Height != value.Height)
                {
                    size = value;
                    RequestRedraw();
                }
            }
        }

        public bool Clip { get; set; }

        public ListNode(Microsoft.Xna.Framework.Color scrollColor)
        {
            Scroller = new ScrollerNode(this, ScrollerNode.Types.VerticalRight);
            Scroller.Color = scrollColor;

            ItemHeight = 40;
            ItemPadding = 2;
            ListStyle = ListStyles.TopDown;
            LayoutInvisibleItems = true;
            BackgroundColors = null;
            ShrinkContentsForScrollers = true;
            Clip = true;
        }

        public override void Dispose()
        {
            Scroller?.Dispose();
            Scroller = null;    
            base.Dispose();
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (BackgroundColors != null)
            {
                DebugTimer.DebugStartTime(this);

                int index = 0;

                Texture2D[] textures = BackgroundColors.Select(c => id.TextureCache.GetTextureFromColor(c)).ToArray();

                foreach (T child in Children)
                {
                    id.Draw(textures[index], child.Bounds, Color.White, 1);
                    index++;

                    if (index >= textures.Length)
                        index = 0;
                }

                DebugTimer.DebugEndTime(this);
            }

            //Not drawing base because we want to do manaual draw childrne..
            //base.Draw(id, parentAlpha);

            NeedsDraw = false;

            Point offset = id.Offset;

            Rectangle bounds = Bounds;

            if (Clip)
            {
                id.PushClipRectangle(bounds);
            }

            if (Scroller != null)
            {
                id.Offset = new Point(0, -(int)Scroller.CurrentScrollPixels);
            }

            Node[] t = Children;
            foreach (Node n in t)
            {
                if (n.Drawable)
                {
                    if (n.Bounds.Bottom + id.Offset.Y >= Bounds.Y && n.Bounds.Y + id.Offset.Y <= Bounds.Bottom)
                    {
                        n.Draw(id, parentAlpha * Alpha);
                    }
                }
            }

            if (Clip)
            {
                id.PopClipRectangle();
            }

            id.Offset = offset;

            Scroller?.Draw(id, parentAlpha);
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (Scroller.OnMouseInput(mouseInputEvent))
            {
                return true;
            }

            MouseInputEvent translated = Translate(mouseInputEvent);

            return base.OnMouseInput(translated);
        }

        public override void Layout(RectangleF parentBounds)
        {
            Node[] items = LayoutInvisibleItems ? Children.ToArray() : VisibleChildren.ToArray();
            int contentSize = (items.Length * (ItemHeight + ItemPadding)) + ItemPadding;

            BoundsF = CalculateRelativeBounds(parentBounds);

            Size = new Size(Bounds.Width, contentSize);

            base.Layout(parentBounds);

            if (Scroller  != null)
            {
                Scroller.ContentSizePixels = contentSize;
                Scroller.ViewSizePixels = Bounds.Height;
                Scroller.Layout(BoundsF);
            }
        }

        protected override void LayoutChildren(RectangleF bounds)
        {
            Node[] items = LayoutInvisibleItems ? Children.ToArray() : VisibleChildren.ToArray();
            int contentSize = (items.Length * (ItemHeight + ItemPadding)) + ItemPadding;

            float left = bounds.Left + ItemPadding;
            float width = bounds.Width - (ItemPadding * 2);

            if (Scroller.Needed && Scroller.Visible && ShrinkContentsForScrollers)
            {
                switch (Scroller.ScrollType)
                {
                    case ScrollerNode.Types.VerticalLeft:
                        left += Scroller.Width;
                        width -= Scroller.Width;
                        break;
                    case ScrollerNode.Types.VerticalRight:
                        width -= Scroller.Width;
                        break;
                }
            }

            if (ListStyle == ListStyles.TopDown)
            {
                float prevBottom = bounds.Top;
                foreach (Node n in items)
                {
                    n.RelativeBounds = new RectangleF(0, 0, 1, 1);
                    n.Layout(new RectangleF(left,
                                           prevBottom + ItemPadding,
                                           width,
                                           ItemHeight));

                    prevBottom = n.Bounds.Bottom;
                }
            }
            else
            {
                float prevTop = contentSize + bounds.Top;
                foreach (Node n in items)
                {
                    n.RelativeBounds = new RectangleF(0, 0, 1, 1);
                    n.Layout(new RectangleF(left,
                            prevTop - (ItemPadding + ItemHeight),
                            width,
                            ItemHeight));
                    prevTop = n.Bounds.Y;
                }
            }
        }

        public MouseInputEvent Translate(MouseInputEvent input)
        {
            if (Scroller == null)
            {
                return input;
            }

            return Scroller.Translate(input);
        }
    }
}
