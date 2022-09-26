using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class GaugeNode : Node
    {
        public bool KeepAspectRatio
        {
            get
            {
                return foregroundNode.KeepAspectRatio;
            }
            set
            {
                foregroundNode.KeepAspectRatio = value;
                backgroundNode.KeepAspectRatio = value;
            }
        }

        private ImageNode backgroundNode;
        private ImageNode foregroundNode;

        public GaugeNode(string filename, Color forground, Color background)
        {
            backgroundNode = new ImageNode(filename, background);
            backgroundNode.KeepAspectRatio = false;
            AddChild(backgroundNode);

            foregroundNode = new ImageNode(filename, forground);
            foregroundNode.KeepAspectRatio = false;
            AddChild(foregroundNode);
        }

        public void SetValue(float fraction)
        {
            RectangleF bounds = new RectangleF(0, 0, fraction, 1);
            foregroundNode.RelativeBounds = bounds;
            foregroundNode.RelativeSourceBounds = bounds;
        }
    }
}
