using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition.Nodes
{
    public class BlinkenNode : Node, IUpdateableNode
    {
        private ImageNode imageNode;

        public Color Color { get { return imageNode.Tint; } set { imageNode.Tint = value; } }
        public TimeSpan Timeout { get; set; }

        private DateTime expiry;

        public BlinkenNode(string filename)
        {
            Timeout = TimeSpan.FromMilliseconds(400);
            imageNode = new ImageNode(filename);
            AddChild(imageNode);
        }

        public void Blink()
        {
            expiry = DateTime.Now + Timeout;
            imageNode.Visible = true;
        }

        public void Update(GameTime gameTime)
        {
            if (imageNode.Visible && 
                DateTime.Now > expiry)
            {
                imageNode.Visible = false;
            }
        }

    }
}
