using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using ExternalData;
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

        private Node nameContainer;

        private TextEditNode trackName;

        private TextNode length;

        private TextButtonNode menuButton;
        private ITrackProvider trackProvider;

        public TrackEditorNode(ITrackProvider trackProvider)
        {
            this.trackProvider = trackProvider;

            objectProperties.Remove();
            left.AddChild(objectProperties);
            TrackNode = new RaceTrackEditorNode();
            TrackNode.SetMode(RaceTrackNode.Modes.Selected);
            TrackNode.ClickedElement += TrackNode_ClickedElement;
            TrackNode.SelectedUpdated += TrackNode_SelectedUpdated;
            right.AddChild(TrackNode, 0);
            RelativeBounds = new RectangleF(0, 0, 1, 1);

            menuButton = new TextButtonNode("Menu", ButtonBackground, ButtonHover, TextColor);
            menuButton.OnClick += MenuButton_OnClick;
            buttonContainer.AddChild(menuButton, 0);

            okButton.Visible = false;
            cancelButton.Visible = false;

            AlignHorizontally(0.05f, addButton, removeButton, null, null, null, null, menuButton);

            OnRefreshList += TrackEditorNode_OnRefreshList;
            addButton.Text = "Add Element";
            removeButton.Text = "Remove Element";

            nameContainer = new ColorNode(Theme.Current.Editor.Background);
            nameContainer.RelativeBounds = buttonContainer.RelativeBounds;
            nameContainer.Scale(0.25f, 1);
            right.AddChild(nameContainer);

            TextNode trackNameName = new TextNode("Track Name: ", TextColor);
            nameContainer.AddChild(trackNameName);

            trackName = new TextEditNode(" ", TextColor);
            nameContainer.AddChild(trackName);

            trackNameName.RelativeBounds = new RectangleF(0.01f, 0.25f, 0.3f, 0.5f);
            trackName.RelativeBounds = new RectangleF(0.35f, 0.25f, 0.63f, 0.5f);

            OnOK += TrackEditorNode_OnOK;

            length = new TextNode("", Theme.Current.InfoPanel.Text.XNA);
            length.RelativeBounds = new RectangleF(0.79f, 0.01f, 0.2f, 0.02f);
            length.Alignment = RectangleAlignment.CenterRight;
            AddChild(length);
        }

        private void MenuButton_OnClick(MouseInputEvent mie)
        {
            MouseMenu mouseMenu = new MouseMenu(this);
            mouseMenu.TopToBottom = false;

            mouseMenu.AddItem("New Track", () => { NewTrack_OnClick(mie); });
            mouseMenu.AddItem("Open", () => { OpenTrack(); });

            mouseMenu.AddItem("Import", () => { Import_OnClick(mie); });
            mouseMenu.AddBlank();
            mouseMenu.AddItem("Save & Exit", () => { OkButton_OnClick(mie); });
            mouseMenu.AddItem("Exit", () => { CancelButton_OnClick(mie); });

            mouseMenu.Show(menuButton.Bounds.Location);
        }

        private void TrackEditorNode_OnOK(BaseObjectEditorNode<TrackElement> obj)
        {
            Track.Name = trackName.Text;
        }

        private void OpenTrack()
        {
            TrackSelector trackSelector = new TrackSelector(trackProvider);
            PopupLayer py = CompositorLayer.LayerStack.GetLayer<PopupLayer>();
            py.Popup(trackSelector);

            trackSelector.OnTrackSelected += TrackSelector_OnTrackSelected;
        }

        private void TrackSelector_OnTrackSelected(RaceLib.Track obj)
        {
            SetTrack(obj);
        }

        private void NewTrack_OnClick(MouseInputEvent mie)
        {
            SetTrack(null);
        }

        private void TrackNode_SelectedUpdated()
        {
            RefreshSelectedObjectProperties();
        }

        private void Import_OnClick(MouseInputEvent mie)
        {
            string filename = PlatformTools.OpenFileDialog("Import VDrone Track", "Track file|*.trk");
            if (string.IsNullOrEmpty(filename))
            {
                return;
            }
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

        protected override void AddOnClick(MouseInputEvent mie)
        {
            MouseMenu mouseMenu = new MouseMenu(this);
            mouseMenu.TopToBottom = false;

            foreach (RaceLib.TrackElement.ElementTypes type in Enum.GetValues(typeof(RaceLib.TrackElement.ElementTypes)))
            {
                if (type == RaceLib.TrackElement.ElementTypes.Invalid)
                    continue;

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
                tr.RotationTopdown = selected.RotationTopdown;
                tr.Tilt = selected.Tilt;
            }

            SetObjects(TrackNode.TrackEntity.TrackElements);
            SetSelected(tr);
            TrackNode.TrackEntity.NeedUpdate = true;
        }

        public void SetTrack(RaceLib.Track track)
        {
            if (track == null)
            {
                track = new RaceLib.Track();
                track.ID = Guid.NewGuid();
                track.Name = "Track  " + track.ID.ToString();
                track.TrackElements = new RaceLib.TrackElement[] { new RaceLib.TrackElement() { ElementType = RaceLib.TrackElement.ElementTypes.Gate } };
            }

            RaceLib.Track clone = (RaceLib.Track)track.Clone();

            TrackNode.Load(track);

            SetObjects(TrackNode.TrackEntity.TrackElements);
            Track = track;

            trackName.Text = Track.Name;
            TrackNode.TrackEntity.OnFlightPathUpdate += TrackEntity_OnFlightPathUpdate;
        }

        public override void SetObjects(IEnumerable<TrackElement> toEdit, bool addRemove = true, bool cancelButton = true)
        {
            base.SetObjects(toEdit, addRemove, cancelButton);

            SplitHorizontally(left, right, 0.2f);

            objectProperties.RelativeBounds = new RectangleF(0, 0.8f, 1, 0.2f);
            multiItemBox.RelativeBounds = new RectangleF(0, 0.0f, 1, objectProperties.RelativeBounds.Top);
            TrackNode.RelativeBounds = new RectangleF(0, 0.0f, 1, 1);
            TrackNode.RequestLayout();
            RequestLayout();

            heading.Text = "Track Editor";
            itemName.Visible = false;
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

        private void TrackEntity_OnFlightPathUpdate()
        {
            if (TrackNode.TrackEntity.FlightPath != null)
            {
                int l = (int)TrackNode.TrackEntity.FlightPath.Length;

                if (GeneralSettings.Instance.Units == RaceLib.Units.Imperial)
                {
                    l = (int)(l * 3.28084f);

                    length.Text = l.ToString() + "ft";
                }
                else
                {
                    length.Text = l.ToString() + "m";
                }
                Track.Length = l;
            }
            else
            {
                length.Text = "";
            }
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
            if (trackElement == null)
            {
                return;
            }
            Selected = trackElement;
            modeLookAt = trackElement.Position;
            SetMode(Modes.Selected);
            EntityEditor.Target = Selected;
        }


        protected override void DrawContent(Drawer id)
        {
            base.DrawContent(id);

            if (Selected != null)
            {
                EntityEditor.Draw(Renderer, Camera, Matrix.Identity);
            }
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
                            landing.X = (float)Math.Round(landing.X, 1);
                            landing.Y = 0;
                            landing.Z = (float)Math.Round(landing.Z, 1);

                            
                            // Try to put it on another element.
                            IEnumerable<EntityDistance> hitEntities = Root.CastRay<TrackElement>(ray, Matrix.Identity);
                            EntityDistance best = hitEntities.OrderBy(e => e.Distance).FirstOrDefault();
                            if (best.Entity != null && best.Entity != Selected)
                            {
                                landing.Y = best.Entity.Position.Y + best.Entity.BoundingBox.Max.Y;
                            }

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
                                dot = -Vector3.Dot(Vector3.Forward, direction);
                            }
                            else
                            {
                                addition = 180;
                                dot = -Vector3.Dot(Vector3.Backward, direction);
                            }

                            Logger.UI.LogCall(this, direction);

                            float degrees = MathHelper.ToDegrees((float)Math.Acos(dot)) + addition;

                            float increment = 5;

                            float temp = (float)Math.Round(degrees / increment);

                            degrees = temp * increment;

                            Selected.RotationTopdown = degrees;

                            SelectedUpdated?.Invoke();
                        }
                    }
                }
                else if(mouseInputEvent.ButtonState == ButtonStates.Pressed && mouseInputEvent.Button == MouseButtons.Left)
                {
                    foreach (EntityDistance ed in EntityEditor.CastRay<Handle>(ray, Matrix.Identity))
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


            if (mouseInputEvent.ButtonState == ButtonStates.Pressed && mouseInputEvent.Button == MouseButtons.Left && ClickedElement != null)
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
