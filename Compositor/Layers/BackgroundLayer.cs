using Composition.Nodes;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Layers
{
    public class BackgroundLayer : CompositorLayer
    {
        public ColorNode BackgroundNode { get; private set; }

        public BackgroundLayer(GraphicsDevice device, string filename) 
            : base(device)
        {
            BackgroundNode = new ColorNode(filename, Microsoft.Xna.Framework.Color.Black);
            BackgroundNode.Alignment = RectangleAlignment.Center;
            BackgroundNode.KeepAspectRatio = false;
            Root.AddChild(BackgroundNode);
        }

        public BackgroundLayer(GraphicsDevice device, ToolTexture texture)
           : base(device)
        {
            BackgroundNode = new ColorNode(texture);
            BackgroundNode.Alignment = RectangleAlignment.Center;
            BackgroundNode.KeepAspectRatio = false;
            Root.AddChild(BackgroundNode);
        }

        public void Crop(int width, int height)
        {
            BackgroundNode.KeepAspectRatio = true;
            BackgroundNode.SetAspectRatio(16, 9);
            RequestLayout();
        }

        public void Uncrop()
        {
            BackgroundNode.KeepAspectRatio = false;
            RequestLayout();
        }
    }
}
