using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class InsideOutBorderRelativeNode : Node
    {
        private BorderNode borderNode;

        public int Offset { get; set; }

        public InsideOutBorderRelativeNode(Color color) 
        {
            borderNode = new BorderNode(color);
            AddChild(borderNode);
            Offset = -2;
        }


        public override void Layout(RectangleF parentBounds)
        {
            base.Layout(parentBounds);

            Node child = Children.FirstOrDefault();
            if (child != null)
            {
                Rectangle childBounds = child.Bounds;

                borderNode.Width = Offset;

                borderNode.BoundsF = new RectangleF(
                    childBounds.X - Offset,
                    childBounds.Y - Offset,
                    childBounds.Width + (Offset * 2),
                    childBounds.Height + (Offset * 2));
            }
        }


        public override void Draw(Drawer id, float parentAlpha)
        {
            NeedsDraw = false;

            Node[] t = Children;
            foreach (Node n in t)
            {
                if (n == borderNode)
                    continue;

                if (n.Drawable)
                {
                    n.Draw(id, parentAlpha * Alpha);
                }
            }

            if (t.Any() && Visible) 
            { 
                borderNode.Draw(id, parentAlpha);
            }
        }
    }
}
