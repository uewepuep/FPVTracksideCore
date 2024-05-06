using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using RaceLib.Format;
using Tools;

namespace UI.Nodes.Rounds
{
    public abstract class EventPilotListNode<T> : EventXNode where T : EventPilotNode
    {
        public IEnumerable<T> PilotNodes { get { return contentContainer.Children.OfType<T>(); } }
        public IEnumerable<TextNode> BracketNodes { get { return contentContainer.Children.OfType<TextNode>(); } }

        public ImageButtonNode MenuButton { get; private set; }

        public int Columns { get; private set; }

        public EventPilotListNode(EventManager ev, Round round)
            : base(ev, round)
        {

            EventManager.OnPilotRefresh += Refresh;
            EventManager.OnEventChange += Refresh;
            EventManager.RaceManager.OnRaceEnd += OnRaceEnd;
            EventManager.RaceManager.OnLapDisqualified += OnLapDisqualified;

            MenuButton = new ImageButtonNode(@"img\settings.png", Color.Transparent, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            MenuButton.OnClick += MenuButton_OnClick;
            buttonContainer.AddChild(MenuButton, 0);
            FormatData();
            UpdateButtons();
        }

        private void OnLapDisqualified(Lap lap)
        {
            // Don't refresh mid-race.
            if (!EventManager.RaceManager.RaceRunning)
            {
                Refresh();
            }
        }

        protected void OnRaceEnd(Race race)
        {
            Refresh();
        }

        public override void Dispose()
        {
            EventManager.OnPilotRefresh -= Refresh;
            EventManager.OnEventChange -= Refresh;
            EventManager.RaceManager.OnRaceEnd -= OnRaceEnd;
            EventManager.RaceManager.OnLapDisqualified -= OnLapDisqualified;

            base.Dispose();
        }

        public void SetSubHeadingRounds(IEnumerable<Race> races)
        {
            IEnumerable<Round> rounds = races.Select(r => r.Round).Distinct().OrderBy(r => r.RoundNumber);
            SetSubHeading(RaceLib.Ext.ToString(rounds));
        }

        protected override void UpdateButtons()
        {
            base.UpdateButtons();
            canAddFinal = Round.RoundType != Round.RoundTypes.Final;

            if (MenuButton != null)
                MenuButton.Scale(0.6f);
        }

        private void MenuButton_OnClick(MouseInputEvent mie)
        {
            MouseMenu mm = new MouseMenu(this);

            MakeMenu(mm);

            Point position = new Point(MenuButton.Bounds.X, MenuButton.Bounds.Bottom);
            mm.Show(position - mie.Translation);
        }

        public void MakeMenu(MouseMenu mm)
        {
            mm.AddItem("Edit Settings", EditSettings);
            mm.AddItem("Copy to Clipboard", CopyToClipboard);
            mm.AddItem("Export CSV", ExportCSV);
            mm.AddItemConfirm("Re-calculate", Recalculate);

            //if (EventManager.Event.SyncWith == SyncWith.MultiGP)
            //{
            //    mm.AddItem("MultiGP - Upload Overall Results", UploadMultiGPResults);
            //}
        }

        //public virtual void UploadMultiGPResults()
        //{
        //    EventLayer eventLayer = CompositorLayer as EventLayer; 
        //    if (eventLayer != null)
        //    {
        //        if (eventLayer.SyncManager.ISync is MultiGP.MultiGPSync)
        //        {
        //            Pilot[] orderedPilots = GetOrderedPilots().ToArray(); 
        //            eventLayer.SyncManager.MultiGPCaptureOverallRaceResults(orderedPilots);
        //        }
        //    }
        //}

        public override IEnumerable<Pilot> GetOrderedPilots()
        {
            return PilotNodes.OrderBy(r => r.Position).Select(p => p.Pilot).Where(p => p != null);
        }

        protected virtual void Recalculate()
        {
            Refresh();
        }

        public virtual void EditSettings()
        {

        }

        private void CopyToClipboard()
        {
            string csv = MakeCSV();
            if (!string.IsNullOrEmpty(csv))
            {
                PlatformTools.Clipboard.SetText(csv);
            }
        }

        private void ExportCSV()
        {
            PlatformTools.ExportCSV("Save CSV", MakeCSV(), GetLayer<PopupLayer>());
        }

        public abstract string MakeCSV();

        private bool needsRefresh;

        public void Refresh()
        {
            needsRefresh = true;
            RequestLayout();
        }

        public override void Layout(RectangleF parentBounds)
        {
            if (needsRefresh)
            {
                FormatData();
            }

            base.Layout(parentBounds);
        }

        private void FormatData()
        {
            UpdateNodes();

            T[] ordered = Order(PilotNodes).ToArray();

            UpdateColumns(ordered);
            UpdatePositions(ordered);
            needsRefresh = false;
            RequestFormatLayout();
        }

        public virtual void UpdateNodes()
        {
        }

        public virtual void UpdatePositions(IEnumerable<T> nodes)
        {
            int i = 0;
            foreach (T t in nodes)
            {
                t.Position = i;
                i++;
            }
        }

        public virtual IEnumerable<T> Order(IEnumerable<T> nodes)
        {
            return PilotNodes;
        }

        protected virtual void UpdateColumns(IEnumerable<T> ordered)
        {
            int pilotsPerColumn = 26;
            int nodeCount = PilotNodes.Count();

            nodeCount += ordered.Select(t => t.Bracket).Distinct().Count();

            Columns = (int)Math.Ceiling(nodeCount / (float)pilotsPerColumn);

            float width = 1.0f / Columns;

            int column = 0;

            foreach (TextNode tn in BracketNodes)
            {
                tn.Dispose();
            }

            Brackets lastBracket = Brackets.None;
            List<Node> list = new List<Node>();

            bool showBrackets = ordered.Where(p => p.Pilot != null).Select(r => r.Bracket).Distinct().Count() > 1;

            foreach (T ppn in ordered)
            {
                if (lastBracket != ppn.Bracket && showBrackets)
                {
                    lastBracket = ppn.Bracket;

                    string text = lastBracket.ToString();
                    TextNode bracketNode = new TextNode(text, Theme.Current.Rounds.Text.XNA);
                    contentContainer.AddChild(bracketNode);
                    list.Add(bracketNode);
                }

                list.Add(ppn);
                if (list.Count >= pilotsPerColumn)
                {
                    float leftAlign = column * width;
                    MakeColumns(list, pilotsPerColumn, leftAlign, width);
                    column++;
                    list.Clear();
                }
            }

            if (list.Any())
            {
                float leftAlign = column * width;
                MakeColumns(list, pilotsPerColumn, leftAlign, width);
            }
        }

        public override void CalculateAspectRatio(float height)
        {
            // Default safe value.
            float ap = 300 / 800.0f;

            if (PilotNodes.Any() && height > 0)
            {
                float maxWidth = PilotNodes.Select(p => p.GetRequiredWidth()).First();

                ap = maxWidth / height;
            }

            float aspectRatio = ap * Columns;
            if (AspectRatio != aspectRatio)
            {
                AspectRatio = aspectRatio;
            }

            base.CalculateAspectRatio(height);
        }


        protected override void AddButtonRoundMenu(MouseMenu addRound)
        {
            base.AddButtonRoundMenu(addRound);

            int perRace = EventManager.Channels.GetChannelGroups().Count();

            if (perRace == 0)
                return;

            Pilot[] pilots = Order(PilotNodes).Select(p => p.Pilot).Where(p => p != null).ToArray();

            int races = (int)Math.Ceiling(pilots.Length / (float)perRace);
            int spots = races * perRace;

            addRound.AddItem("Top " + perRace, () =>
            {
                EventManager.RoundManager.GenerateTopX(Round, pilots.Take(perRace));
                Refresh();
            });

            for (int i = perRace * 2; i <= spots; i += perRace)
            {
                int c = i;
                addRound.AddItem("Ordered Top " + c, () =>
                {
                    EventManager.RoundManager.GenerateTopX(Round, pilots.Take(c));
                    Refresh();
                });
            }


            for (int i = perRace * 2; i <= spots; i += perRace)
            {
                int c = i;
                addRound.AddItem("Seeded Top " + c, () =>
                {
                    EventManager.RoundManager.GenerateSeededX(Round, pilots.Take(c));
                    Refresh();
                });
            }
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            MouseInputEvent translated = Translate(mouseInputEvent);

            if (translated.EventType == MouseInputEvent.EventTypes.Button && translated.Button == MouseButtons.Right && translated.ButtonState == ButtonStates.Released)
            {
                MouseMenu mouseMenu = new MouseMenu(this);
                mouseMenu.AddItem("Copy Pilots", CopyToClipboard);

                foreach (EventPilotNode pilotResultNode in PilotNodes)
                {
                    if (pilotResultNode.Contains(translated.Position))
                    {
                        pilotResultNode.AddMenu(translated, mouseMenu);
                    }
                }


                mouseMenu.Show(mouseInputEvent.Position);
                return true;
            }

            return base.OnMouseInput(mouseInputEvent);
        }
    }

    public class EventPilotNode : Node, IPilot
    {
        public Pilot Pilot { get; private set; }
        public Channel Channel { get { return null; } }
        public bool Heading { get { return Pilot == null; } }

        private ColorNode colorNode;
        protected TextNode pilotNameNode;
        protected TextNode positionNode;
        protected Node roundScoreContainer;

        public bool HasRaced { get; protected set; }
        public Brackets Bracket { get; protected set; }

        public event Action<EventPilotNode, MouseInputEvent> OnClick;

        private int position;
        public int Position
        {
            get
            {
                return position;
            }
            set
            {
                if (Heading)
                    return;

                position = value;

                if (value == 0)
                {
                    positionNode.Text = "";
                }
                else
                {
                    positionNode.Text = value.ToStringPosition();
                }
            }
        }

        protected EventManager eventManager;

        private int pilotNameWidth = 150;
        private int horizontalPadding = 10;
        private int verticalPadding = 2;

        public EventPilotNode(EventManager eventManager, Pilot pilot)
        {
            this.eventManager = eventManager;
            Pilot = pilot;

            ToolTexture color = Heading ? Theme.Current.Rounds.Background : Theme.Current.Rounds.Foreground;

            colorNode = new ColorNode(color);
            colorNode.Scale(0.95f, 0.9f);
            AddChild(colorNode);

            string pilotName = "Pilot";
            if (Pilot != null)
            {
                pilotName = Pilot.Name;
            }

            pilotNameNode = new TextNode(pilotName, Theme.Current.Rounds.Text.XNA);
            pilotNameNode.Alignment = RectangleAlignment.BottomLeft;
            AddChild(pilotNameNode);

            positionNode = new TextNode("0", Theme.Current.Rounds.Text.XNA);
            positionNode.Alignment = RectangleAlignment.BottomRight;
            positionNode.Style.Bold = true;
            AddChild(positionNode);

            roundScoreContainer = new Node();
            AddChild(roundScoreContainer);
        }

        public override void Layout(RectangleF parentBounds)
        {
            BoundsF = CalculateRelativeBounds(parentBounds);

            colorNode.Layout(BoundsF);
            RectangleF workBounds = colorNode.BoundsF;

            roundScoreContainer.BoundsF = roundScoreContainer.CalculateRelativeBounds(workBounds);

            workBounds.X += horizontalPadding;

            workBounds.Y += verticalPadding;
            workBounds.Height -= verticalPadding * 2;

            RectangleF pilotNameParentBounds = workBounds;
            pilotNameParentBounds.Width = pilotNameWidth;
            pilotNameNode.Layout(pilotNameParentBounds);

            float x = pilotNameParentBounds.Right + horizontalPadding;
            foreach (Node node in roundScoreContainer.Children)
            {
                RectangleF bounds = workBounds;
                bounds.X = x;
                bounds.Width = GetItemWidth(node);
                node.Layout(bounds);

                x = bounds.Right + horizontalPadding;
            }

            RectangleF posBounds = workBounds;
            posBounds.X = x;
            posBounds.Width = GetItemWidth(positionNode);
            positionNode.Layout(posBounds);

            workBounds.Width = posBounds.Right - workBounds.X;
            colorNode.BoundsF = workBounds;
        }

        protected virtual int GetItemWidth(Node node)
        {
            return 50;
        }

        public int GetRequiredWidth()
        {
            int value = pilotNameWidth;

            foreach (Node c in roundScoreContainer.Children)
            {
                value += GetItemWidth(c) + horizontalPadding;
            }

            value += 100;

            return value;
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.ButtonState == ButtonStates.Released)
            {
                OnClick?.Invoke(this, mouseInputEvent);
                return true;
            }

            if (mouseInputEvent.Button == MouseButtons.Left && mouseInputEvent.ButtonState == ButtonStates.Pressed)
            {
                GetLayer<DragLayer>()?.RegisterDrag(this, mouseInputEvent);
            }
            return base.OnMouseInput(mouseInputEvent);
        }

        public virtual void AddMenu(MouseInputEvent mouseInputEvent, MouseMenu mouseMenu)
        {
        }
    }
}
