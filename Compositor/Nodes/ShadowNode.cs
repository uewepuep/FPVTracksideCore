using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class ShadowNode : Node
    {
        private ImageNode right;
        private ImageNode bottom;
        private ImageNode corner;

        public int Width { get; set; }

        public ShadowNode()
        {
            Width = 20;

            right = new ImageNode(@"img/shadow_right.png");
            bottom = new ImageNode(@"img/shadow_bottom.png");
            corner = new ImageNode(@"img/shadow_corner.png");

            right.KeepAspectRatio = false;
            bottom.KeepAspectRatio = false;
            corner.KeepAspectRatio = false;

            AddChild(right);
            AddChild(bottom);
            AddChild(corner);
        }
        protected override void LayoutChildren(RectangleF bounds)
        {
            float rx = (float)Math.Floor(bounds.Right);
            float by = (float)Math.Floor(bounds.Bottom);

            right.BoundsF = new RectangleF(rx, bounds.Top, Width, by - bounds.Top);
            bottom.BoundsF = new RectangleF(bounds.Left, by, rx - bounds.Left, Width);
            corner.BoundsF = new RectangleF(rx, by, Width, Width);
        }

#if DEBUG
        protected override void SetParent(Node node)
        {
            base.SetParent(node);
            System.Diagnostics.Debug.Assert(!ParentChain.Any(p => p is RenderTargetNode && p != this.Parent));
        }
#endif

    }
}
