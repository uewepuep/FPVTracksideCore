using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timing;
using Tools;

namespace UI.Nodes
{
    public class LapEditorNode : AspectNode
    {
        public Lap[] Laps { get; private set; }
        public Race Race { get; private set; }
        public Pilot Pilot { get; private set; }

        private Node raceNode;
        private Node lapsNode;
        private TextButtonNode okButton;
        private TextButtonNode cancelButton;
        private TextButtonNode addLapButton;
        private TextButtonNode copyLapsButton;
        private TextButtonNode pasteLapsButton;

        public event Action<Lap[]> OnOK;
        public event Action<Lap[]> OnCancel;

        public Color ChannelColor { get; private set; }

        private List<LapEditorContainer> lapContainers;

        private IEnumerable<LapEditorContainer> orderedLapContainers { get { return lapContainers.OrderBy(l => l.End); } }

        public RaceManager RaceManager { get; private set; }

        public const float DisabledAlpha = 0.3f;

        public LapEditorNode(RaceManager raceManager, Race race, Pilot pilot, IEnumerable<Lap> laps, Color channel)
        {
            RaceManager = raceManager;
            lapContainers = new List<LapEditorContainer>();

            ChannelColor = channel;

            AspectRatio = 3;
            Race = race;
            Pilot = pilot;

            Laps = laps.Where(l => l.Race == race && l.Pilot == pilot).OrderBy(l => l.End).ToArray();

            BorderPanelShadowNode background = new BorderPanelShadowNode(Theme.Current.Editor.Background, Theme.Current.Editor.Border.XNA);
            AddChild(background);

            TextNode title = new TextNode("Lap Editor - " + Pilot.Name, Theme.Current.Editor.Text.XNA);
            title.RelativeBounds = new RectangleF(0, 0, 0.9f, 0.08f);
            background.Inner.AddChild(title);

            raceNode = new ColorNode(Theme.Current.Editor.Foreground.XNA);
            raceNode.RelativeBounds = new RectangleF(0.01f, 0.09f, 0.98f, 0.1f);
            background.Inner.AddChild(raceNode);

            lapsNode = new ColorNode(Theme.Current.Editor.Foreground.XNA);
            lapsNode.RelativeBounds = new RectangleF(0.01f, 0.22f, 0.98f, 0.65f);
            background.Inner.AddChild(lapsNode);

            float topOfButtonContainer = 1 - 0.10f;

            Node buttonContainer = new Node();
            buttonContainer.RelativeBounds = new RectangleF(0, topOfButtonContainer, 1, 1 - topOfButtonContainer);
            background.Inner.AddChild(buttonContainer);

            okButton = new TextButtonNode("Ok", Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);
            cancelButton = new TextButtonNode("Cancel", Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);

            addLapButton = new TextButtonNode("Add Lap", Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);
            copyLapsButton = new TextButtonNode("Copy Laps", Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);
            pasteLapsButton = new TextButtonNode("Paste Laps", Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);

            okButton.OnClick += OkButton_OnClick;
            cancelButton.OnClick += CancelButton_OnClick;
            addLapButton.OnClick += AddLapButton_OnClick;

            copyLapsButton.OnClick += CopyLapsButton_OnClick;
            pasteLapsButton.OnClick += PasteLapsButton_OnClick;  

            buttonContainer.AddChild(cancelButton, addLapButton, copyLapsButton, pasteLapsButton, okButton);
            AlignHorizontally(0.05f, cancelButton, null, addLapButton, copyLapsButton, pasteLapsButton, null, okButton);

            Scale(0.5f, 0.9f);

            foreach (Lap lap in Laps)
            {
                LapEditorContainer lc = new LapEditorContainer(lap, ChannelColor);
                lc.OnValidityChanged += UpdateNumbersEtc;
                lc.OnSplitLap += OnSplitLap;
                lc.OnTimeChanged += () => { UpdateNumbersEtc(); };
                lapContainers.Add(lc);
            }

            Layout();

            UpdateRaceNode();
        }

        private void PasteLapsButton_OnClick(MouseInputEvent mie)
        {
            string lapLine = PlatformTools.Clipboard.GetText();
            AddManualLaps(lapLine.TSVToDouble());
        }

        private void CopyLapsButton_OnClick(MouseInputEvent mie)
        {
            PlatformTools.Clipboard.SetText(orderedLapContainers.Where(lc => lc.Valid).Select(lc => lc.Length).ToTSV());
        }

        private void AddLapButton_OnClick(MouseInputEvent mie)
        {
            GetLayer<PopupLayer>().Popup(new AddLapTimeNode(Pilot, AddManualLap));
        }

        public void AddManualLaps(IEnumerable<double> laps)
        {
            DateTime from = Race.Start;
            foreach (double seconds in laps)
            {
                TimeSpan time = TimeSpan.FromSeconds(seconds);

                AddManualLap(time);
                from += time;
            }
        }

        private void AddManualLap(TimeSpan timeSinceLast)
        {
            AddManualLap(Pilot, timeSinceLast);
        }

        private void AddManualLap(Pilot pilot, TimeSpan timeSinceLast)
        {
            DateTime start = Race.Start;

            LapEditorContainer last = lapContainers.Where(r => r.Valid).LastOrDefault();
            if (last != null)
            {
                start = last.End;
            }

            LapEditorContainer newLC = new LapEditorContainer(start, start + timeSinceLast, ChannelColor);
            newLC.OnValidityChanged += UpdateNumbersEtc;
            newLC.OnSplitLap += OnSplitLap;
            newLC.OnTimeChanged += () => { UpdateNumbersEtc(); };
            lapContainers.Add(newLC);

            Layout();
            UpdateNumbersEtc();
        }

        private void Layout()
        {
            foreach (LapEditorContainer lc in lapContainers)
            {
                lc.Remove();
            }

            lapsNode.ClearDisposeChildren();

            int perRow = 8;
            int rowCount = 8;
            List<Node> rows = new List<Node>();

            foreach (LapEditorContainer lc in orderedLapContainers)
            {
                Node row = rows.LastOrDefault();

                if (row == null || row.ChildCount >= perRow)
                {
                    row = new Node();
                    rows.Add(row);
                }
                row.AddChild(lc);
            }

            foreach (Node row in rows)
            {
                AlignHorizontally(0.01f, perRow, row.Children.ToArray());
                lapsNode.AddChild(row);
            }

            AlignVertically(0.01f, Math.Max(rowCount, rows.Count) , rows.ToArray());
        }

        private void OnSplitLap(LapEditorContainer original, int splits)
        {
            TimeSpan newLength = TimeSpan.FromSeconds(original.Length.TotalSeconds / splits);

            DateTime lapStart = original.Start;
            DateTime lapEnd = lapStart;

            List<LapEditorContainer> newLapConts = new List<LapEditorContainer>();
            for (int i = 0; i < (splits - 1); i++)
            {
                lapEnd = lapStart + newLength;

                LapEditorContainer newLC = new LapEditorContainer(lapStart, lapEnd, ChannelColor);
                newLC.OnValidityChanged += UpdateNumbersEtc;
                newLC.OnSplitLap += OnSplitLap;
                newLC.OnTimeChanged += () => { UpdateNumbersEtc(); };
                lapContainers.Add(newLC);

                lapStart = lapEnd;

                newLapConts.Add(newLC);
            }

            original.Splits = newLapConts;

            Layout();
            UpdateNumbersEtc();
        }

        private void UpdateNumbersEtc()
        {
            DateTime prev = Race.Start;
            int number = Race.Event.PrimaryTimingSystemLocation == PrimaryTimingSystemLocation.Holeshot ? 0 : 1;

            foreach (LapEditorContainer lve in orderedLapContainers)
            {
                if (lve.Valid)
                {
                    lve.Number = number;
                    lve.Start = prev;
                    prev = lve.End;
                    lve.Refresh();

                    number++;
                }
            }

            UpdateRaceNode();
        }

        private void UpdateRaceNode()
        {
            float textHeight = 0.8f;

            raceNode.ClearDisposeChildren();

            TextNode start = new TextNode("0.00", Theme.Current.Editor.Text.XNA);
            start.RelativeBounds = new RectangleF(0, 0 - textHeight, 1, textHeight);
            start.Alignment = RectangleAlignment.TopLeft;
            raceNode.AddChild(start);

            TextNode end = new TextNode(Race.Length.TotalSeconds.ToString("0.00"), Theme.Current.Editor.Text.XNA);
            end.RelativeBounds = new RectangleF(0, 0 - textHeight, 1, textHeight);
            end.Alignment = RectangleAlignment.TopRight;
            raceNode.AddChild(end);

            DateTime last = Race.Start;
            var validOrderedLapContainers = orderedLapContainers.Where(r => r.Valid);
            
            if (validOrderedLapContainers.Any())
            {
                last = validOrderedLapContainers.Select(l => l.End).Max();
            }
            TimeSpan length = last - Race.Start;

            double maxSeconds = Math.Min(Race.Length.TotalSeconds, length.TotalSeconds);

            float width = 0.0025f;

            DateTime raceStart = Race.Start;

            float prevFactor = 0;
            foreach (LapEditorContainer lve in orderedLapContainers)
            {
                TimeSpan sinceRaceStart = lve.End - raceStart;

                float factor = (float)(sinceRaceStart.TotalSeconds / maxSeconds);

                ColorNode colorNode = new ColorNode(ChannelColor);
                colorNode.Alpha = lve.Valid ? 1 : DisabledAlpha;
                colorNode.RelativeBounds = new RectangleF(factor - (width / 2), 0.1f, width, 0.8f);
                raceNode.AddChild(colorNode);

                if (lve.Valid)
                {
                    TextNode lapTime = new TextNode(Lap.LapNumberToString(lve.Number), Theme.Current.Editor.Text.XNA);
                    lapTime.RelativeBounds = new RectangleF(prevFactor, (1 - textHeight) / 2, factor - prevFactor, textHeight);
                    raceNode.AddChild(lapTime);

                    prevFactor = factor;
                }
            }
            raceNode.RequestLayout();
        }

        private void CancelButton_OnClick(Composition.Input.MouseInputEvent mie)
        {
            OnCancel?.Invoke(Laps);
            Dispose();
        }

        private void OkButton_OnClick(Composition.Input.MouseInputEvent mie)
        {
            LapEditorContainer focused = lapContainers.FirstOrDefault(lc => lc.TextEditorFocused);
            if (focused != null)
            {
                focused.HasFocus = false;
            }

            foreach (var lc in lapContainers.Where(lc => lc.Lap == null))
            {
                lc.CreateLap(RaceManager, Pilot);
            }

            // Do any splits we gotta do.
            foreach (var lc in lapContainers.Where(lc => lc.Splits != null && lc.Lap != null))
            {
                lc.DoSplit(RaceManager);
            }

            using (IDatabase db = DatabaseFactory.Open(Race.Event.ID))
            {
                foreach (var lc in lapContainers)
                {
                    lc.SaveChanges(db);
                }
            }

            RaceManager.RecalcuateLaps(Pilot, Race);

            OnOK?.Invoke(Laps);
            Dispose();
        }
    }

    public class LapEditorContainer : Node
    {
        public Lap Lap { get; private set; }

        public bool Valid { get; set; }
        public int Number { get; set; }
        public TimeSpan Length { get { return End - Start; } }

        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        private TextEditNode lapTime;
        private TextNode lapNumber;

        public event System.Action OnValidityChanged;
        public event Action<LapEditorContainer, int> OnSplitLap;
        public event System.Action OnTimeChanged;

        public List<LapEditorContainer> Splits { get; set; }

        public bool TextEditorFocused { get { return lapTime.HasFocus; } }


        public LapEditorContainer(Lap lap, Color channelColor)
            :this(lap.Start, lap.End, channelColor)
        {
            Lap = lap;
            Valid = lap.Detection.Valid;
            Number = lap.Number;

            Refresh();
        }

        public LapEditorContainer(DateTime start, DateTime end, Color channelColor)
        {
            Start = start;
            End = end;
            Valid = true;

            ImageNode imageNode = new ImageNode(@"img/lapbg.png");
            imageNode.KeepAspectRatio = false;
            imageNode.Tint = channelColor;
            AddChild(imageNode);

            lapNumber = new TextNode("", Theme.Current.Editor.Text.XNA);
            lapNumber.Alignment = RectangleAlignment.CenterLeft;
            AddChild(lapNumber);

            lapTime = new TextEditNode("", Theme.Current.Editor.Text.XNA);
            lapTime.TextChanged += LapTime_TextChanged;
            AddChild(lapTime);

            lapNumber.RelativeBounds = new RectangleF(0.05f, 0, 0.3f, 1);

            float starts = lapNumber.RelativeBounds.Right + 0.05f;

            lapTime.RelativeBounds = new RectangleF(starts, 0, 1 - starts, 1);

            Refresh();
        }

        private void LapTime_TextChanged(string obj)
        {
            try
            {
                float seconds;
                if (float.TryParse(obj, out seconds))
                {
                    if (seconds < 0)
                    {
                        seconds = 0;
                    }
                    if (seconds > 3600)
                    {
                        seconds = 3600;
                    }

                    End = Start + TimeSpan.FromSeconds(seconds);
                }
            }
            catch
            {
                End = DateTime.MaxValue;
            }

            Refresh();
            OnTimeChanged?.Invoke();
        }

        public void Refresh()
        {
            Alpha = Valid ? 1 : LapEditorNode.DisabledAlpha;

            if (Valid)
            {
                lapNumber.Text = Lap.LapNumberToString(Number);
            }
            else
            {
                lapNumber.Text = "";
            }
            if (!lapTime.HasFocus)
            {
                lapTime.Text = Length.ToStringRaceTime();
            }
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.Button == MouseButtons.Left)
            {
                if (CompositorLayer.InputEventFactory.AreControlKeysDown() && mouseInputEvent.ButtonState == ButtonStates.Released)
                {
                    lapTime.HasFocus = false;
                    ToggleValidity();
                    return true;
                }
            }
            else if (mouseInputEvent.Button == MouseButtons.Right)
            {
                lapTime.HasFocus = false;
                if (mouseInputEvent.ButtonState == ButtonStates.Released)
                {
                    MouseMenu mm = new MouseMenu(this);

                    string valid = "Re-qualify Lap";
                    if (Valid)
                        valid = "Disqualify Lap";

                    mm.AddItem(valid, () =>
                    {
                        ToggleValidity();
                    });

                    mm.AddSubmenu("Split Lap into...", (i) =>
                    {
                        OnSplitLap?.Invoke(this, i);
                    }, new int[] { 2, 3, 4, 5, 6, 7 });

                    mm.Show(mouseInputEvent);
                }
            }

            return base.OnMouseInput(mouseInputEvent);
        }

        private void ToggleValidity()
        {
            Valid = !Valid;

            Refresh();
            OnValidityChanged?.Invoke();
        }

        public void CreateLap(RaceManager raceManager, Pilot pilot)
        {
            if (Lap == null && Valid)
            {
                raceManager.AddManualLap(pilot, End);
            }
        }

        public void SaveChanges(IDatabase db)
        {
            if (Lap != null)
            {
                Lap.Detection.Valid = Valid;
                Lap.End = End;

                db.Update(Lap.Detection);
                db.Update(Lap);
            }
        }

        public void DoSplit(RaceManager raceManager)
        {
            Lap[] newLaps = raceManager.SplitLap(Lap, Splits.Count + 1).ToArray();

            for (int i = 0; i < newLaps.Length && i < Splits.Count; i++)
            {
                Splits[i].Lap = newLaps[i];

                if (Splits[i].Splits != null)
                {
                    Splits[i].DoSplit(raceManager);
                }
            }
        }
    }
}
