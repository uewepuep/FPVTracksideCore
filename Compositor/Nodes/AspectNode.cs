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
        public RectangleF BaseBoundsF { get; private set; }
        public Rectangle BaseBounds
        {
            get
            {
                return BaseBoundsF.ToRectangle();
            }
        }

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

        public override RectangleF CalculateRelativeBounds(RectangleF parentPosition)
        {
            BaseBoundsF = base.CalculateRelativeBounds(parentPosition);

            if (KeepAspectRatio)
            {
                return Maths.FitBoxMaintainAspectRatio(BaseBoundsF, AspectRatio, Alignment, FitType);
            }
            else
            {
                return BaseBoundsF;
            }
        }

        public override RectangleF ParentChainTargetBounds()
        {
            if (Parent == null)
            {
                return BoundsF;
            }

            RectangleF parent = Parent.ParentChainTargetBounds();

            RectangleF p = new RectangleF();
            p.X = parent.X + parent.Width * RelativeBounds.X;
            p.Y = parent.Y + parent.Height * RelativeBounds.Y;
            p.Width = parent.Width * RelativeBounds.Width;
            p.Height = parent.Height * RelativeBounds.Height;

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
