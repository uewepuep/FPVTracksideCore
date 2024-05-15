using Composition.Input;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ThreeDee.Entities;
using Tools;
using UI.Video;

namespace UI.Nodes.Track
{
    public class TrackEditorNode : ObjectEditorNode<TrackElement>
    {
        public RaceTrackNode TrackNode { get; private set; }

        public RaceLib.Track Track { get; private set; }

        public TrackEditorNode()
        {
            objectProperties.Remove();
            left.AddChild(objectProperties);
            TrackNode = new RaceTrackNode();
            TrackNode.ClickedElement += TrackNode_ClickedElement;
            right.AddChild(TrackNode, 0);
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
            MouseMenu mouseMenu = new MouseMenu(this);
            mouseMenu.TopToBottom = false;

            foreach (RaceLib.TrackElement.ElementTypes type in Enum.GetValues(typeof(RaceLib.TrackElement.ElementTypes)))
            {
                var t2 = type;
                mouseMenu.AddItem(type.ToString(), () => { Add(t2); });
            }

            mouseMenu.Show(addButton);
        }

        private void Add(RaceLib.TrackElement.ElementTypes type)
        {
            TrackNode.AddTrackElement(type, Vector3.Zero);
            SetObjects(TrackNode.TrackEntity.TrackElements);
        }

        public void SetTrack(RaceLib.Track track)
        {
            RaceLib.Track clone = (RaceLib.Track)track.Clone();

            TrackNode.Load(track);

            SetObjects(TrackNode.TrackEntity.TrackElements);
            Track = track;
        }

        public override void SetObjects(IEnumerable<TrackElement> toEdit, bool addRemove = false, bool cancelButton = true)
        {
            base.SetObjects(toEdit, addRemove, cancelButton);

            SplitHorizontally(left, right, 0.2f);

            objectProperties.RelativeBounds = new RectangleF(0, 0.8f, 1, 0.2f);
            multiItemBox.RelativeBounds = new RectangleF(0, 0.0f, 1, objectProperties.RelativeBounds.Top);
            TrackNode.RelativeBounds = new RectangleF(0, 0.0f, 1, 1);
            TrackNode.RequestLayout();
            RequestLayout();
        }

        protected override void SetSelected(TrackElement obj)
        {
            TrackNode.Select(obj);
            base.SetSelected(obj);
        }

        protected override void ChildValueChanged(Change newChange)
        {
            base.ChildValueChanged(newChange);
            TrackNode.TrackEntity.UpdateFlightPath();
        }
    }
}
