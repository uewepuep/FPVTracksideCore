using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Composition.Nodes
{
    public class TextButtonNode : Node
    {
        public ColorNode BackgroundNode { get; private set; }
        public HoverNode HoverNode { get; private set; }
        public TextNode TextNode { get; private set; }
        public ButtonNode ButtonNode { get; private set; }

        public event MouseInputDelegate OnClick;
        public event MouseInputDelegate OnHover;

        public Color Background { get { return BackgroundNode.Color; } set { BackgroundNode.Color = value; } }

        public Color Hover { get { return HoverNode.Color; } set { HoverNode.Color = value; } }

        public string Text { get { return TextNode.Text; } set { TextNode.Text = value; } }


        private bool enabled;
        public bool Enabled
        {
            get
            {
                return enabled;
            }
            set
            {
                enabled = value;

                if (enabled)
                {
                    TextNode.Alpha = 1.0f;
                    HoverNode.Visible = true;
                }
                else
                {
                    TextNode.Alpha = 0.25f;
                    HoverNode.Visible = false;
                }
            }
        }

        public TextButtonNode(string text, Microsoft.Xna.Framework.Color background, Microsoft.Xna.Framework.Color hover, Microsoft.Xna.Framework.Color textColor)
        {
            BackgroundNode = new ColorNode(background);
            AddChild(BackgroundNode);

            Init(text, hover, textColor);
        }

        public TextButtonNode(string text, ToolTexture background, Microsoft.Xna.Framework.Color hover, Microsoft.Xna.Framework.Color textColor)
        {
            BackgroundNode = new ColorNode(background);
            AddChild(BackgroundNode);

            Init(text, hover, textColor);
        }

        private void Init(string text, Microsoft.Xna.Framework.Color hover, Microsoft.Xna.Framework.Color textColor)
        {
            enabled = true;

            text = Translator.Get("Button." + text, text);

            TextNode = new TextNode(text, textColor);
            TextNode.RelativeBounds = new RectangleF(0.05f, 0.17f, 0.9f, 0.73f);
            AddChild(TextNode);

            ButtonNode = new ButtonNode();
            ButtonNode.OnClick += ButtonNode_OnClick;
            AddChild(ButtonNode);

            HoverNode = new HoverNode(hover);
            HoverNode.OnHover += HoverNode_OnHover;
            AddChild(HoverNode);
        }

        private void HoverNode_OnHover(Input.MouseInputEvent mie)
        {
            if (enabled)
            {
                OnHover?.Invoke(mie);
            }
        }

        private void ButtonNode_OnClick(Input.MouseInputEvent mie)
        {
            if (enabled)
            {
                OnClick?.Invoke(mie);
            }
        }

        protected override string GetNodeName()
        {
            return base.GetNodeName() + "(" + Text + ")";
        }
    }
}
