using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class IconButtonNode : Node
    {
        public ColorNode BackgroundNode { get; private set; }
        public HoverNode HoverNode { get; private set; }
        public TextNode TextNode { get; private set; }
        public ButtonNode ButtonNode { get; private set; }
        public ImageNode ImageNode { get; private set; }

        public event MouseInputDelegate OnClick;

        public Color Background { get { return BackgroundNode.Color; } set { BackgroundNode.Color = value; } }
        public Color Hover { get { return HoverNode.Color; } set { HoverNode.Color = value; } }

        public string Text { get { return TextNode.Text; } set { TextNode.Text = value; } }

        public IconButtonNode(string filename, string text, ToolTexture background, Color hover, Color textColor, float verticalTextRatio = 0.3f)
        {
            BackgroundNode = new ColorNode(background);
            AddChild(BackgroundNode);

            ImageNode = new ImageNode(filename);
            ImageNode.RelativeBounds = new RectangleF(0, 0, 1, 1 - verticalTextRatio);
            ImageNode.Tint = textColor;
            AddChild(ImageNode);

            text = Translator.Get("Button." + text, text);

            TextNode = new TextNode(text, textColor);
            TextNode.Alignment = Tools.RectangleAlignment.Center; 
            TextNode.RelativeBounds = new RectangleF(0.05f, 1 - verticalTextRatio, 0.9f, verticalTextRatio);
            AddChild(TextNode);

            ButtonNode = new ButtonNode();
            ButtonNode.OnClick += (mie) => { OnClick?.Invoke(mie); };
            AddChild(ButtonNode);

            HoverNode = new HoverNode(hover);
            AddChild(HoverNode);
        }

        protected override string GetNodeName()
        {
            return base.GetNodeName() + "(" + Text + ")";
        }
    }
}
