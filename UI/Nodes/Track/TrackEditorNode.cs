using Composition.Input;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
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
        public RaceTrackEditorNode TrackNode { get; private set; }

        public RaceLib.Track Track { get; private set; }


        public TrackEditorNode()
        {
            objectProperties.Remove();
            left.AddChild(objectProperties);
            TrackNode = new RaceTrackEditorNode();
            TrackNode.ClickedElement += TrackNode_ClickedElement;
            TrackNode.SelectedUpdated += TrackNode_SelectedUpdated;
            right.AddChild(TrackNode, 0);
            RelativeBounds = new RectangleF(0, 0, 1, 1);

            TextButtonNode add = new TextButtonNode("Add Element", ButtonBackground, ButtonHover, TextColor);
            add.OnClick += Add_OnClick;
            buttonContainer.AddChild(add, 0);

            TextButtonNode remove = new TextButtonNode("Remove Element", ButtonBackground, ButtonHover, TextColor);
            remove.OnClick += Remove_OnClick;
            buttonContainer.AddChild(remove, 0);

            TextButtonNode import = new TextButtonNode("Import..", ButtonBackground, ButtonHover, TextColor);
            import.OnClick += Import_OnClick;
            buttonContainer.AddChild(import, 0);

            AlignVisibleButtons();

            OnRefreshList += TrackEditorNode_OnRefreshList;
        }

        private void TrackNode_SelectedUpdated()
        {
            RefreshSelectedObjectProperties();
        }

        private void Import_OnClick(MouseInputEvent mie)
        {
            string filename = PlatformTools.OpenFileDialog("Import VDrone Track", "Track file|*.trk");

            RaceLib.Track track = ExternalData.VDFileManager.LoadTrk(filename);
            SetTrack(track);
        }

        private void TrackEditorNode_OnRefreshList(BaseObjectEditorNode<TrackElement> obj)
        {
            TrackElement[] elements = obj.Objects.ToArray();
            TrackNode.TrackEntity.TrackElements = elements;
        }

        private void TrackNode_ClickedElement(ThreeDee.Entities.TrackElement obj)
        {
            SetSelected(obj);
        }

        private void Remove_OnClick(Composition.Input.MouseInputEvent mie)
        {
            TrackElement[] elements = Objects.Where(r => r!= Selected).ToArray();
            TrackNode.TrackEntity.TrackElements = elements;
            SetObjects(elements);
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

            mouseMenu.Show(addButton.Bounds.Location);
        }

        private void Add(RaceLib.TrackElement.ElementTypes type)
        {
            TrackElement selected = Selected;

            TrackElement tr = TrackNode.AddTrackElement(type, Vector3.Zero);

            if (selected != null)
            {
                tr.Position = selected.Position;
                tr.Rotation = selected.Rotation;
                tr.Tilt = selected.Tilt;
            }

            SetObjects(TrackNode.TrackEntity.TrackElements);
            SetSelected(tr);
            TrackNode.TrackEntity.NeedUpdate = true;
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
            TrackNode.TrackEntity.NeedUpdate = true;
        }
    }

    public class RaceTrackEditorNode : RaceTrackNode
    {
        public EntityEditor EntityEditor { get; private set; }

        public event Action<TrackElement> ClickedElement;
        public event Action SelectedUpdated;
        private bool draggingView;
        
        
        private bool draggingTranslation;
        private bool draggingRotation;

        public TrackElement Selected { get; private set; }

        public RaceTrackEditorNode()
        {
        }

        public override void Load(RaceLib.Track track)
        {
            base.Load(track);

            EntityEditor = new EntityEditor(ContentManager);
        }


        public void Select(TrackElement trackElement)
        {
            if (EntityEditor.Parent != null)
            {
                EntityEditor.Parent.RemoveChild(EntityEditor);
            }
            if (trackElement == null)
            {
                return;
            }

            trackElement.AddChild(EntityEditor);

            Selected = trackElement;
            modeLookAt = trackElement.Position;
            Mode = Modes.Selected;
        }

        public override bool OnMouseInput(MouseInputEvent unTranslated)
        {
            MouseInputEvent mouseInputEvent = Translate(unTranslated);

            if (Mode == Modes.Selected)
            {
                if (mouseInputEvent.ButtonState == ButtonStates.Pressed && mouseInputEvent.Button == MouseButtons.Right)
                {
                    draggingView = true;
                }
                else if (mouseInputEvent.ButtonState == ButtonStates.Released && mouseInputEvent.Button == MouseButtons.Right)
                {
                    draggingView = false;
                }
                else if (draggingView)
                {
                    modeValue -= mouseInputEvent.PositionChange.X / 100.0f;
                }
            }

            if (mouseInputEvent.Button == MouseButtons.Left && mouseInputEvent.ButtonState == ButtonStates.Released)
            {
                draggingTranslation = false;
                draggingRotation = false;
                TrackEntity.NeedUpdate = true;
                modeLookAt = Selected.Position;
            }

            if (Selected != null && Camera != null)
            {
                Ray ray = Camera.ScreenToWorld(mouseInputEvent.Position);
                if (draggingTranslation || draggingRotation)
                {
                    float? distance = ray.Intersects(new Plane(Vector3.Zero, Vector3.Up));
                    if (distance.HasValue)
                    {
                        Vector3 landing = ray.Position + ray.Direction * distance.Value;

                        if (draggingTranslation)
                        {
                            Selected.Position = landing;
                            SelectedUpdated?.Invoke();
                        }

                        if (draggingRotation)
                        {
                            Vector3 direction = landing - Selected.Position;
                            direction.Y = 0;
                            direction.Normalize();

                            float addition = 0;
                            float dot;
                            if (direction.X > 0)
                            {
                                addition = 180;
                                dot = -Vector3.Dot(Vector3.Forward, direction);
                            }
                            else
                            {
                                dot = -Vector3.Dot(Vector3.Backward, direction);
                            }

                            Logger.UI.LogCall(this, direction);

                            Selected.RotationTopdown = MathHelper.ToDegrees((float)Math.Acos(dot)) + addition;

                            SelectedUpdated?.Invoke();
                        }
                    }
                }
                else if(mouseInputEvent.ButtonState == ButtonStates.Pressed && mouseInputEvent.Button == MouseButtons.Left)
                {
                    foreach (EntityDistance ed in EntityEditor.CastRay<Handle>(ray, EntityEditor.GetAbsoluteTransform()))
                    {
                        if (ed.Entity == EntityEditor.TranslationHandle)
                        {
                            draggingTranslation = true;
                        }

                        if (ed.Entity == EntityEditor.RotationHandle)
                        {
                            draggingRotation = true;
                        }
                        return true;
                    }
                }
            }


            if (mouseInputEvent.ButtonState == ButtonStates.Pressed && ClickedElement != null)
            {
                if (Camera != null)
                {
                    Ray ray = Camera.ScreenToWorld(mouseInputEvent.Position);

                    IEnumerable<EntityDistance> hitEntities = Root.CastRay<TrackElement>(ray, Matrix.Identity);

                    EntityDistance best = hitEntities.OrderBy(e => e.Distance).FirstOrDefault();
                    if (best.Entity != null)
                    {
                        ClickedElement((TrackElement)best.Entity);
                    }
                }
            }

            return base.OnMouseInput(mouseInputEvent);
        }
    }
}
