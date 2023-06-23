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
    public class ListNode<T> : RenderTargetNode where T : Node
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

        public ListNode(Microsoft.Xna.Framework.Color scrollColor)
        {
            Scroller.ScrollType = ScrollerNode.Types.VerticalRight;
            Scroller.Color = scrollColor;

            ItemHeight = 40;
            ItemPadding = 2;
            ListStyle = ListStyles.TopDown;
            LayoutInvisibleItems = true;
            LayoutDefinesSize = false;
            KeepAspectRatio = false;
            CanScale = false;
            BackgroundColors = null;
            ShrinkContentsForScrollers = true;
        }

        protected override void DrawContent(Drawer id)
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
            base.DrawContent(id);
        }

        public override void Layout(Rectangle parentBounds)
        {
            Node[] items = LayoutInvisibleItems ? Children.ToArray() : VisibleChildren.ToArray();
            int contentSize = (items.Length * (ItemHeight + ItemPadding)) + ItemPadding;

            Bounds = CalculateRelativeBounds(parentBounds);

            Size = new Size(BaseBounds.Width, contentSize);

            base.Layout(parentBounds);

            Scroller.ContentSizePixels = contentSize;
            Scroller.ViewSizePixels = BaseBounds.Height;
            Scroller.Layout(Bounds);
        }

        protected override void LayoutChildren(Rectangle bounds)
        {
            Node[] items = LayoutInvisibleItems ? Children.ToArray() : VisibleChildren.ToArray();
            int contentSize = (items.Length * (ItemHeight + ItemPadding)) + ItemPadding;

            int left = ItemPadding;
            int width = bounds.Width - (ItemPadding * 2);

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
                Alignment = RectangleAlignment.TopLeft;
                int prevBottom = 0;
                foreach (Node n in items)
                {
                    n.RelativeBounds = new RectangleF(0, 0, 1, 1);
                    n.Layout(new Rectangle(left,
                                           prevBottom + ItemPadding,
                                           width,
                                           ItemHeight));

                    prevBottom = n.Bounds.Bottom;
                }
            }
            else
            {
                Alignment = RectangleAlignment.BottomLeft;
                int prevTop = contentSize;
                foreach (Node n in items)
                {
                    n.RelativeBounds = new RectangleF(0, 0, 1, 1);
                    n.Layout(new Rectangle(left,
                            prevTop - (ItemPadding + ItemHeight),
                            width,
                            ItemHeight));
                    prevTop = n.Bounds.Y;
                }
            }
        }
    }

}
