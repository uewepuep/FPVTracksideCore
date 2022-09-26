using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class AspectNode : Node
    {
        public bool KeepAspectRatio { get; set; }

        public float AspectRatio { get; set; }

        public RectangleAlignment Alignment { get; set; }

        // This guy is what the bounds would be if aspect ratio wasn't done.
        public Rectangle BaseBounds { get; private set; }

        public FitType FitType { get; set; }

        public AspectNode()
        {
            Alignment = RectangleAlignment.Center;
            KeepAspectRatio = true;
            AspectRatio = 1;

            FitType = FitType.FitBoth;
        }

        public AspectNode(float aspectRatio)
            :this()
        {
            AspectRatio = aspectRatio;
        }

        public void SetAspectRatio(Size size)
        {
            AspectRatio = size.AspectRatio;
        }

        public void SetAspectRatio(float width, float height)
        {
            if (width == 0 || height == 0)
            {
                AspectRatio = 1;
                return;
            }

            AspectRatio = width / height;
        }

        public override Rectangle CalculateRelativeBounds(Rectangle parentPosition)
        {
            BaseBounds = base.CalculateRelativeBounds(parentPosition);

            if (KeepAspectRatio)
            {
                return Maths.FitBoxMaintainAspectRatio(BaseBounds, AspectRatio, Alignment, FitType);
            }
            else
            {
                return BaseBounds;
            }
        }

        public override Rectangle ParentChainTargetBounds()
        {
            if (Parent == null)
            {
                return Bounds;
            }

            Rectangle parent = Parent.ParentChainTargetBounds();

            Rectangle p = new Rectangle();
            p.X = parent.X + (int)Math.Round(parent.Width * RelativeBounds.X);
            p.Y = parent.Y + (int)Math.Round(parent.Height * RelativeBounds.Y);
            p.Width = (int)Math.Round(parent.Width * RelativeBounds.Width);
            p.Height = (int)Math.Round(parent.Height * RelativeBounds.Height);

            if (KeepAspectRatio)
            {
                return Maths.FitBoxMaintainAspectRatio(p, AspectRatio, Alignment, FitType);
            }
            else
            {
                return p;
            }
        }
    }

}
