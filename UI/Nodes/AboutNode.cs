using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class AboutNode : BorderPanelShadowNode
    {
        public ImageNode ImageNode { get; private set; }

        public AboutNode()
        {
            RelativeBounds = new RectangleF(0.15f, 0.1f, 0.7f, 0.8f);

            TextNode thanks = new TextNode("Thank you everyone for your support!!", Theme.Current.TextMain.XNA);
            thanks.RelativeBounds = new RectangleF(0.1f, 0.95f, 0.8f, 0.05f);
            thanks.Alignment = RectangleAlignment.TopCenter;
            Inner.AddChild(thanks);

            ImageNode = new ImageNode();
            ImageNode.KeepAspectRatio = true;
            ImageNode.RelativeBounds = new RectangleF(0, 0, 1, thanks.RelativeBounds.Y);
            Inner.AddChild(ImageNode);

            CloseNode closeNode = new CloseNode();
            Inner.AddChild(closeNode);
            closeNode.OnClick += CloseNode_OnClick;
        }

        private void CloseNode_OnClick(Composition.Input.MouseInputEvent mie)
        {
            Dispose();
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (ImageNode.Texture == null)
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                ImageNode.Texture = TextureHelper.GetEmbeddedTexture(id.GraphicsDevice, assembly, @"UI.img.heros.jpg");
                ImageNode.UpdateAspectRatioFromTexture();
            }
            base.Draw(id, parentAlpha);
        }
    }
}
