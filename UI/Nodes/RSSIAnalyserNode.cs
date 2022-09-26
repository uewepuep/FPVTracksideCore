using Composition;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class RSSIAnalyserNode : BorderPanelNode
    {
        private RSSINode rSSINode;

        public RSSIAnalyserNode(EventManager ev)
        {
            Scale(0.6f);

            HeadingNode heading = new HeadingNode(Theme.Current.InfoPanel, "RSSI Analyser");
            Inner.AddChild(heading);

            rSSINode = new RSSINode(ev);
            rSSINode.RelativeBounds = new RectangleF(0, heading.RelativeBounds.Bottom, 1, 1 - heading.RelativeBounds.Bottom);
            Inner.AddChild(rSSINode);
        }
    }
}
