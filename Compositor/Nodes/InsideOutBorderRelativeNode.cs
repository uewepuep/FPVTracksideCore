using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class InsideOutBorderRelativeNode : ColorNode
    {
        private BorderNode borderNode;

        public int Offset { get; set; }

        public InsideOutBorderRelativeNode(Color color) 
            : base(color)
        {
            borderNode = new BorderNode(color);
            Offset = -1;
        }

        public override void Layout(Rectangle parentBounds)
        {
            base.Layout(parentBounds);

            Node child = Children.FirstOrDefault();
            if (child != null)
            {
                Rectangle childBounds = child.Bounds;

                borderNode.Width = Offset;

                borderNode.Bounds = new Rectangle(
                    childBounds.X - Offset,
                    childBounds.Y - Offset,
                    childBounds.Width + (Offset * 2),
                    childBounds.Height + (Offset * 2));
            }
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            base.Draw(id, parentAlpha);
            borderNode.Draw(id, parentAlpha);
        }
    }
}
