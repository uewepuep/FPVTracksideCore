using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ThreeDee.Entities;
using Tools;

namespace UI.Nodes.Track
{
    public class TrackEditorNode : ObjectEditorNode<TrackElement>
    {
        private RaceTrackNode trackNode;

        public TrackEditorNode()
        {
            objectProperties.Remove();
            left.AddChild(objectProperties);
            trackNode = new RaceTrackNode();
            trackNode.ClickedElement += TrackNode_ClickedElement;
            right.AddChild(trackNode, 0);
            RelativeBounds = new RectangleF(0, 0, 1, 1);

            TextButtonNode add = new TextButtonNode("Add Element", ButtonBackground, ButtonHover, TextColor);
            add.OnClick += Add_OnClick;
            buttonContainer.AddChild(add, 0);

            TextButtonNode remove = new TextButtonNode("Remove Element", ButtonBackground, ButtonHover, TextColor);
            remove.OnClick += Remove_OnClick;
            buttonContainer.AddChild(remove, 0);

            AlignVisibleButtons();
        }

        private void TrackNode_ClickedElement(ThreeDee.Entities.TrackElement obj)
        {
        }

        private void Remove_OnClick(Composition.Input.MouseInputEvent mie)
        {
        }

        private void Add_OnClick(Composition.Input.MouseInputEvent mie)
        {
        }

        public void SetTrack(RaceLib.Track track)
        {
            trackNode.Load(track);

            SetObjects(trackNode.TrackEntity.TrackElements);
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
