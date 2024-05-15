using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes.Track
{
    public class TrackEditorNode : ObjectEditorNode<RaceLib.TrackElement>
    {
        private RaceTrackNode trackNode;

        public TrackEditorNode()
        {
            objectProperties.Remove();
            left.AddChild(objectProperties);
            trackNode = new RaceTrackNode();
            right.AddChild(trackNode, 0);
            RelativeBounds = new RectangleF(0, 0, 1, 1);
        }

        public void SetTrack(RaceLib.Track track)
        {
            SetObjects(track.TrackElements);
            trackNode.Load(track);
        }

        public override void SetObjects(IEnumerable<TrackElement> toEdit, bool addRemove = false, bool cancelButton = true)
        {
            base.SetObjects(toEdit, addRemove, cancelButton);

            SplitHorizontally(left, right, 0.2f);

            objectProperties.RelativeBounds = new RectangleF(0, 0.7f, 1, 0.3f);
            multiItemBox.RelativeBounds = new RectangleF(0, 0.0f, 1, objectProperties.RelativeBounds.Top);
            trackNode.RelativeBounds = new RectangleF(0, 0.0f, 1, 1);
            trackNode.RequestLayout();
            RequestLayout();
        }

        protected override void SetSelected(TrackElement obj)
        {
            trackNode.Select(obj);
            base.SetSelected(obj);
        }
    }
}
