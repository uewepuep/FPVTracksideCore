using Composition.Nodes;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes.Track
{
    public class TrackEditorNode : BaseObjectEditorNode<RaceLib.TrackElement>
    {
        private RaceTrackNode trackNode;

        private Node switcheroo;

        public TrackEditorNode()
            :base(Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA, Theme.Current.ScrollBar.XNA)
        {
            trackNode = new RaceTrackNode();
            AddChild(trackNode, 0);

            left.AddChild(new ColorNode(Theme.Current.Editor.Foreground.XNA), 0);

            switcheroo = new ColorNode(Theme.Current.Editor.Foreground.XNA);
            objectProperties.Remove();
            switcheroo.AddChild(objectProperties);
            AddChild(switcheroo);
        }

        public void SetTrack(RaceLib.Track track)
        {
            SetObjects(track.TrackElements);
            trackNode.Load(track);
        }

        public override void SetObjects(IEnumerable<TrackElement> toEdit, bool addRemove = false, bool cancelButton = true)
        {
            base.SetObjects(toEdit, addRemove, cancelButton);
            left.RelativeBounds = new RectangleF(0.0f, 0.0f, 0.1f, 1);
            switcheroo.RelativeBounds = new RectangleF(left.RelativeBounds.Right, 0.7f, 0.2f, 0.3f);
            objectProperties.RelativeBounds = new RectangleF(0, 0, 1, 1);
            trackNode.RelativeBounds = new RectangleF(left.RelativeBounds.Right, 0.0f, 1 - left.RelativeBounds.Right, 1);
            RequestLayout();
        }

        protected override void SetSelected(TrackElement obj)
        {
            trackNode.Select(obj);
            base.SetSelected(obj);
        }
    }
}
