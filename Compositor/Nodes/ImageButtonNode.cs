using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition.Input;
using Microsoft.Xna.Framework;
using Tools;

namespace Composition.Nodes
{
    public class ImageButtonNode : Node, IButtonNode
    {
        public ColorNode BackgroundNode { get; private set; }
        public HoverNode HoverNode { get; private set; }
        public ImageNode ImageNode { get; private set; }
        public ButtonNode ButtonNode { get; private set; }

        public event MouseInputDelegate OnClick;

        public Color Background { get { return BackgroundNode.Color; } set { BackgroundNode.Color = value; } }
        public Color Hover { get { return HoverNode.Color; } set { HoverNode.Color = value; } }

        public ImageButtonNode(string filename, Color background, Color hover)
            : this(filename, background, hover, Color.White)
        {

        }
        public ImageButtonNode(string filename, Color background, Color hover, Color tint)
            : this(filename, new ToolTexture(background), hover, tint)
        {

        }


        public ImageButtonNode(string filename, ToolTexture background, Color hover)
            :this(filename, background, hover, Color.White)
        {

        }
        public ImageButtonNode(string filename, ToolTexture background, Color hover, Color tint)
        {
            BackgroundNode = new ColorNode(background);
            AddChild(BackgroundNode);

            ImageNode = new ImageNode(filename);
            ImageNode.Tint = tint;

            AddChild(ImageNode);

            ButtonNode = new ButtonNode();
            ButtonNode.OnClick += (mie) => { OnClick?.Invoke(mie); };
            AddChild(ButtonNode);

            HoverNode = new HoverNode(hover);
            AddChild(HoverNode);
        }

        protected override string GetNodeName()
        {
            if (ImageNode.FileName != null)
            {
                return base.GetNodeName() + "(" + ImageNode.FileName + ")";
            }
            return base.GetNodeName();
        }

    }
}
