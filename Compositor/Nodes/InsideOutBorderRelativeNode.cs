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
        public float Offset { get; set; }

        public InsideOutBorderRelativeNode(Color color) 
            : base(color)
        {
            Offset = 0.04f;
        }

        public override void Layout(Rectangle parentBounds)
        {
            base.Layout(parentBounds);

            Node child = Children.FirstOrDefault();
            if (child != null)
            {
                Rectangle childBounds = child.Bounds;

                int offsetX = (int)(childBounds.Width * Offset);
                int offsetY = (int)(childBounds.Height * Offset);

                int offset = Math.Max(offsetX, offsetY);

                Bounds = new Rectangle(
                    childBounds.X - offset,
                    childBounds.Y - offset,
                    childBounds.Width + (offset * 2),
                    childBounds.Height + (offset * 2));
            }
        }


    }
}
