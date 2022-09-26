using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Tools;

namespace UI.Nodes
{
    public class PanelNode : ColorNode
    {
        public PanelNode() : base(Theme.Current.Panel.XNA)
        {
        }

    }
}
