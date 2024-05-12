using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ThreeDee.Nodes;

namespace UI.Nodes.Track
{
    public class TrackTab : Node
    {
        public RaceTrackNode RaceTrackNode { get; private set; }

        public TrackTab() 
        {
            RaceTrackNode = new RaceTrackNode();
            AddChild(RaceTrackNode);

            TextButtonNode flyThrough = new TextButtonNode("Fly Through", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
            flyThrough.RelativeBounds = new Tools.RectangleF(0.9f, 0.9f, 0.1f, 0.1f);
            AddChild(flyThrough);

            flyThrough.OnClick += FlyThrough_OnClick;
        }

        private void FlyThrough_OnClick(Composition.Input.MouseInputEvent mie)
        {
            RaceTrackNode.FlyThrough();
        }
    }
}
