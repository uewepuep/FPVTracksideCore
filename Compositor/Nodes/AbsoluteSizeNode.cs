using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Tools;

namespace Composition.Nodes
{
    public class AbsoluteSizeNode : Node
    {
        public Size Size { get; set; }
        public RectangleAlignment Alignment { get; set; }

        public AbsoluteSizeNode(int width, int height)
            :this(new Size(width, height))
        {
        }

        public AbsoluteSizeNode(Size size)
        {
            Alignment = RectangleAlignment.Center;
            Size = size;
        }

        public override void Layout(RectangleF parentBounds)
        {
            RectangleF bounds = new RectangleF(0, 0, Size.Width, Size.Height);

            BoundsF = Maths.FitBoxMaintainAspectRatio(parentBounds, bounds, 1, Alignment);

            LayoutChildren(BoundsF);
        }
    }

    public class AbsoluteHeightNode : Node
    {
        public int Height { get; set; }
        public RectangleAlignment Alignment { get; set; }

        public AbsoluteHeightNode(int height)
        {
            Alignment = RectangleAlignment.Center;
            Height = height;
        }

        public override void Layout(RectangleF parentBounds)
        {
            RectangleF bounds = CalculateRelativeBounds(parentBounds);
            bounds.Height = Height;

            BoundsF = Maths.FitBoxMaintainAspectRatio(parentBounds, bounds, 1, Alignment);
            LayoutChildren(BoundsF);
        }
    }
}
