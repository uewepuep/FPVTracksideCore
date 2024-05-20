using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ThreeDee.Entities;
using ThreeDee.Nodes;
using Tools;

namespace UI.Nodes.Track
{
    public class TrackTab : Node
    {
        public RaceTrackNode RaceTrackNode { get; private set; }

        private RaceLib.Track track;

        public bool Loaded { get { return track != null; } }

        private RaceLib.EventManager eventManager;

        public TrackTab(RaceLib.EventManager eventManager) 
        {
            this.eventManager = eventManager;

            RaceTrackNode = new RaceTrackNode();
            AddChild(RaceTrackNode);

            TextButtonNode flyThrough = new TextButtonNode("View", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
            flyThrough.RelativeBounds = new Tools.RectangleF(0.94f, 0.94f, 0.05f, 0.05f);
            flyThrough.OnClick += ModeClick;
            AddChild(flyThrough);

            TextButtonNode edit = new TextButtonNode("Edit", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
            edit.RelativeBounds = new Tools.RectangleF(0.94f, 0.01f, 0.05f, 0.05f);
            edit.OnClick += EditClick;
            AddChild(edit);
        }

        public void Load(RaceLib.Track track)
        {
            if (track == null)
            {
                track = new RaceLib.Track();
                track.Name = "New Track";
                track.TrackElements = new RaceLib.TrackElement[] { new RaceLib.TrackElement() };
            }

            RaceTrackNode.Load(track);
            this.track = track;
        }

        private void TrackEditorNode_OnOK(BaseObjectEditorNode<TrackElement> obj)
        {
            TrackEditorNode trackEditorNode = obj as TrackEditorNode;
            if (trackEditorNode != null) 
            {
                using (RaceLib.IDatabase db = RaceLib.DatabaseFactory.Open(Guid.Empty))
                {
                    eventManager.Event.Track = trackEditorNode.Track;
                    track.TrackElements = trackEditorNode.TrackNode.GetTrackElements().ToArray();

                    db.Upsert(track);
                    db.Upsert(eventManager.Event);
                }

                Load(track);
            }
        }


        private void EditClick(Composition.Input.MouseInputEvent mie)
        {
            PopupLayer popupLayer = CompositorLayer.LayerStack.GetLayer<PopupLayer>();
            if (popupLayer != null)
            {
                TrackEditorNode TrackEditorNode = new TrackEditorNode();
                TrackEditorNode.OnOK += TrackEditorNode_OnOK;
                popupLayer.Popup(TrackEditorNode);
                TrackEditorNode.SetTrack(track);
            }
        }

        private void ModeClick(Composition.Input.MouseInputEvent mie)
        {
            RaceTrackNode.ToggleMode();
        }
    }
}
