using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class ProgressBarNode : Node
    {
        public float Progress
        {
            get
            {
                return progressNode.RelativeBounds.Width;
            }
            set
            {
                RectangleF rectangleF = progressNode.RelativeBounds;
                rectangleF.Width = Math.Min(1, Math.Max(0, value));
                progressNode.RelativeBounds = rectangleF;
                RequestLayout();
            }
        }

        protected Node progressNode;
        protected BorderNode borderNode;

        public ProgressBarNode(Color color)
        {
            borderNode = new BorderNode(color);
            AddChild(borderNode);

            progressNode = new Node();
            AddChild(progressNode);

            ColorNode colorNode = new ColorNode(color);
            progressNode.AddChild(colorNode);

            Progress = 0;
        }

    }

    public class AnimatedProgressBarNode : ProgressBarNode
    {
        private AnimatedImageNode animatedImageNode;

        public AnimatedProgressBarNode(Color color, string animatedImageFilename)
            : base(color)
        {
            animatedImageNode = new AnimatedImageNode(animatedImageFilename);
            animatedImageNode.Offset = new Point(-4, 0);
            animatedImageNode.Tint = color;
            AddChild(animatedImageNode);
        }
    }
}
