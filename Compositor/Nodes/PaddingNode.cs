using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition.Nodes
{
    public class PaddingNode : Node
    {
        public int PaddingX { get; set; }
        public int PaddingY { get; set; }

        public PaddingNode(int x, int y)
        {
            PaddingX = x;
            PaddingY = y;
        }

        public PaddingNode()
            : this(2,2)
        {
        }

        public override void Layout(Rectangle parentBounds)
        {
            Bounds = new Rectangle(
                parentBounds.X + PaddingX,
                parentBounds.Y + PaddingY,
                parentBounds.Width - (2 * PaddingX),
                parentBounds.Height - (2 * PaddingY)
                );

            LayoutChildren(Bounds);
        }
    }
}
