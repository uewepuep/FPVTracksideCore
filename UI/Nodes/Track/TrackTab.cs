using Composition.Nodes;
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

        public ColorNode Panel { get; private set; }

        public TrackTab() 
        {
            Panel = new ColorNode(Theme.Current.Panel.XNA);
            AddChild(Panel);

            RaceTrackNode = new RaceTrackNode();
            RaceTrackNode.ClickedElement += RaceTrackNode_ClickedElement;
            AddChild(RaceTrackNode);

            TextButtonNode flyThrough = new TextButtonNode("Fly Through", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
            flyThrough.RelativeBounds = new Tools.RectangleF(0.9f, 0.9f, 0.1f, 0.1f);
            AddChild(flyThrough);

            flyThrough.OnClick += FlyThrough_OnClick;
        }

        private void RaceTrackNode_ClickedElement(ThreeDee.Entities.TrackElement obj)
        {
            Panel.ClearDisposeChildren();

            TrackElementEditable trackElementEditable = new TrackElementEditable(obj);

            BaseObjectEditorNode<TrackElementEditable> editor = new BaseObjectEditorNode<TrackElementEditable>(Theme.Current.InfoPanel.Background.XNA, Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.InfoPanel.Text.XNA, Theme.Current.ScrollBar.XNA);
            editor.Clip = false;
            Panel.AddChild(editor);
            editor.OnCancel += (e) =>
            {
                CloseSide();
            };

            editor.OnOK += Editor_OnOK;

            editor.SetObject(trackElementEditable);
            editor.RefreshList();
            editor.RequestLayout();

            OpenSide();
        }

        private void Editor_OnOK(BaseObjectEditorNode<TrackElementEditable> obj)
        {
            CloseSide();
        }

        public void OpenSide()
        {
            Panel.RelativeBounds = new RectangleF(0, 0, 0.15f, 1);
            RaceTrackNode.RelativeBounds = new RectangleF(Panel.RelativeBounds.Right, 0, 1 - Panel.RelativeBounds.Right, 1);
            RequestLayout();
        }

        public void CloseSide() 
        {
            RaceTrackNode.RelativeBounds = new RectangleF(0, 0, 1, 1);
            Panel.RelativeBounds = new RectangleF(0, 0, 0, 0);
            RequestLayout();

        }


        private void FlyThrough_OnClick(Composition.Input.MouseInputEvent mie)
        {
            RaceTrackNode.FlyThrough();
        }
    }
}
