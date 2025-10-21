using Composition.Input;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class DockNode : Node
    {
        private DockChildNode top;
        private DockChildNode bottom;


        private DockChildNode left;
        private DockChildNode right;

        public DockChildNode Top
        {
            get
            {
                if (top == null)
                {
                    top = new DockChildNode(100);
                    AddChild(top);
                }

                return top;
            }
        }

        public DockChildNode Bottom
        {
            get
            {
                if (bottom == null)
                {
                    bottom = new DockChildNode(100);
                    AddChild(bottom);
                }

                return bottom;
            }
        }

        public DockChildNode Left
        {
            get
            {
                if (left == null)
                {
                    left = new DockChildNode(100);
                    AddChild(left);
                }

                return left;
            }
        }

        public DockChildNode Right
        {
            get
            {
                if (right == null)
                {
                    right = new DockChildNode(100);
                    AddChild(right);
                }

                return right;
            }
        }

        public Node Center { get; private set; }
        
        public DockNode(int top, int? bottom = null, int? left = null, int? right = null)
            :this()
        {
            Top.SetFixedSize(top);

            if (bottom != null)
                Bottom.SetFixedSize(bottom.Value);

            if (left != null)
                Left.SetFixedSize(left.Value);

            if (right != null)
                Right.SetFixedSize(right.Value);
        }

        public DockNode()
        {
            Center = new Node();
            AddChild(Center);
        }

        public override void Layout(RectangleF parentBounds)
        {
#if DEBUG
            top?.SetColor(Color.Red);
            bottom?.SetColor(Color.Green);
            left?.SetColor(Color.Blue);
            right?.SetColor(Color.Yellow);
#endif

            NeedsLayout = false;
            BoundsF = CalculateRelativeBounds(parentBounds);
            RectangleF bounds = BoundsF;

            if (top != null && top.Visible)
            {
                RectangleF topBounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, top.FixedSize);
                top.Layout(topBounds);

                bounds.Y += top.FixedSize;
                bounds.Height -= top.FixedSize;
            }

            if (bottom != null && bottom.Visible)
            {
                RectangleF bottomBounds = new RectangleF(bounds.X, bounds.Bottom - bottom.FixedSize, bounds.Width, bottom.FixedSize);
                bottom.Layout(bottomBounds);

                bounds.Height -= bottom.FixedSize;
            }

            if (left != null && left.Visible)
            {
                RectangleF leftBounds = new RectangleF(bounds.X, bounds.Y, left.FixedSize, bounds.Height);
                left.Layout(leftBounds);

                bounds.X += left.FixedSize;
                bounds.Width -= left.FixedSize;
            }

            if (right != null && right.Visible)
            {
                RectangleF rightBounds = new RectangleF(bounds.Right - right.FixedSize, bounds.Y, right.FixedSize, bounds.Height);
                right.Layout(rightBounds);

                bounds.Width -= right.FixedSize;
            }

            Center.Layout(bounds);

        }
    }

    public class DockChildNode : Node
    {
        public int FixedSize { get; private set; }

        public DockChildNode(int fixedSize) 
        {
            FixedSize = fixedSize;
        }

        public void SetFixedSize(int size)
        {
            FixedSize = size;
            RequestLayout();
        }

#if DEBUG
        private Color color;

        private bool hover;

        public void SetColor(Color color) { this.color = color; }

        public override void Draw(Drawer id, float parentAlpha)
        {
            Rectangle singleLine = Bounds;
            singleLine.Height = 1;

            if (hover)
            {
                id.QuickDraw(singleLine, Color.Magenta);
            }
            id.QuickDraw(singleLine, color);

            base.Draw(id, parentAlpha);
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            hover = Contains(mouseInputEvent.Position);
            return base.OnMouseInput(mouseInputEvent);
        }
#endif
    }
}
