using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Tools;

namespace UI.Nodes
{
    public class BorderPanelNode : Node
    {
        public Node Inner { get; private set; }

        public BorderNode BorderNode { get; private set; }

        public BorderPanelNode()
           : this(Theme.Current.Panel.XNA, Theme.Current.PanelAlt.XNA)
        {
        }

        public BorderPanelNode(Color panel, Color border)
             : base()
        {
            ColorNode n = new ColorNode(panel);
            Setup(n, border);
        }

        public BorderPanelNode(Tools.ToolTexture panel, Color border)
             : base()
        {
            ColorNode n = new ColorNode(panel);
            Setup(n, border);
        }

        protected virtual void Setup(ColorNode panel, Color borderColor)
        {
            BorderNode = new BorderNode(borderColor);
            AddChild(BorderNode);

            PaddingNode pn = new PaddingNode(BorderNode.Width, BorderNode.Width);
            AddChild(pn);

            pn.AddChild(panel);

            Inner = new PaddingNode(5, 5);
            panel.AddChild(Inner);
        }
    }

    public class BorderPanelShadowNode : BorderPanelNode
    {
        public ShadowNode ShadowNode { get; private set; }

        public BorderPanelShadowNode()
          : this(Theme.Current.Panel.XNA, Theme.Current.PanelAlt.XNA)
        {
        }

        public BorderPanelShadowNode(Color panel, Color border)
             : base()
        {
            ColorNode n = new ColorNode(panel);
            Setup(n, border);
        }

        public BorderPanelShadowNode(Tools.ToolTexture panel, Color border)
             : base()
        {
            ColorNode n = new ColorNode(panel);
            Setup(n, border);
        }

        protected override void Setup(ColorNode panel, Color borderColor)
        {
            ShadowNode = new ShadowNode();
            AddChild(ShadowNode);

            base.Setup(panel, borderColor);
        }
    }

}
