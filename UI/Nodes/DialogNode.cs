using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Tools;

namespace UI.Nodes
{
    public class DialogNode : AbsoluteSizeNode
    {
        protected Node back;

        public DialogNode() : base(800, 800)
        {
            RelativeBounds = new RectangleF(0.1f, 0.1f, 0.8f, 0.8f);

            // Add black background
            ColorNode background = new ColorNode(Color.Black);
            background.RelativeBounds = new RectangleF(0, 0, 1, 1);
            AddChild(background);
            SetBack(background);
        }

        protected void SetBack(Node node)
        {
            back = node;
        }
    }
} 