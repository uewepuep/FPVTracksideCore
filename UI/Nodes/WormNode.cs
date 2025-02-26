using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using RaceLib.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using UI.Video;

namespace UI.Nodes
{
    public class WormNode : AlphaAnimatedNode
    {
        public EventManager EventManager { get; private set; }


        private bool needsNewPilots;
        private bool needsWormUpdate;

        private Node wormContainer;
        private Node captureContainer;

        public int PilotCount { get { return wormContainer.ChildCount; } }

        public event Action RebuiltList;

        private bool hasDrawnSinceUpdate;

        public WormNode(EventManager eventManager)
        {
            ColorNode backGround = new ColorNode(Theme.Current.Panel.XNA);
            backGround.Alpha = 0.5f;
            AddChild(backGround);

            wormContainer = new Node();
            wormContainer.RelativeBounds = RectangleF.Centered(0.99f, 0.8f);
            AddChild(wormContainer);

            captureContainer = new Node();
            AddChild(captureContainer);

            EventManager = eventManager;

            EventManager.OnPilotChangedChannels += NeedsNewPilots;
            EventManager.RaceManager.OnPilotAdded += NeedsNewPilots;
            EventManager.RaceManager.OnPilotRemoved += NeedsNewPilots;
            EventManager.RaceManager.OnRaceChanged += NeedsNewPilots;
            EventManager.RaceManager.OnRaceClear += NeedsNewPilots;
            EventManager.RaceManager.OnRaceReset += NeedsNewPilots;

            EventManager.RaceManager.OnLapDetected += NeedsWormUpdate;
            EventManager.RaceManager.OnLapDisqualified += NeedsWormUpdate;
            EventManager.RaceManager.OnLapsRecalculated += NeedsWormUpdate;
            EventManager.RaceManager.OnSplitDetection += NeedsWormUpdate;
            eventManager.GameManager.OnCapture += GameManager_OnCapture;
        }

        private void GameManager_OnCapture(Pilot[] pilots, RaceLib.Game.Captured captured)
        {
            IEnumerable<CapturePointNode> capturePointNodes = captureContainer.Children.OfType<CapturePointNode>();
            foreach (CapturePointNode capturePointNode in capturePointNodes)
            {
                if (capturePointNode.TimingSystemIndex == captured.TimingSystemIndex)
                {
                    capturePointNode.SetPilots(pilots, captured);
                }
            }
        }

        public override void Dispose()
        {
            EventManager.OnPilotChangedChannels -= NeedsNewPilots;
            EventManager.RaceManager.OnPilotAdded -= NeedsNewPilots;
            EventManager.RaceManager.OnPilotRemoved -= NeedsNewPilots;
            EventManager.RaceManager.OnRaceChanged -= NeedsNewPilots;
            EventManager.RaceManager.OnRaceClear -= NeedsNewPilots;
            EventManager.RaceManager.OnRaceReset -= NeedsNewPilots;
            EventManager.RaceManager.OnLapDetected -= NeedsWormUpdate;
            EventManager.RaceManager.OnLapDisqualified -= NeedsWormUpdate;
            EventManager.RaceManager.OnLapsRecalculated -= NeedsWormUpdate;
            EventManager.RaceManager.OnSplitDetection -= NeedsWormUpdate;

            base.Dispose();
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            hasDrawnSinceUpdate = true;

            base.Draw(id, parentAlpha);
        }

        public override void Update(GameTime gametime)
        {
            base.Update(gametime);

            if (!hasDrawnSinceUpdate)
                return;

            if (needsNewPilots)
            {
                needsNewPilots = false;
                UpdatePilots();
            }

            if (needsWormUpdate)
            {
                needsWormUpdate = false;
                UpdateWorms();
            }

            hasDrawnSinceUpdate = false;
        }

        private void UpdateWorms()
        {
            int maxLaps;

            switch (EventManager.RaceManager.RaceType)
            {
                case EventTypes.Endurance:
                    maxLaps = EventManager.RaceManager.LeadLap + 1;
                    break;

                default:
                    Race race = EventManager.RaceManager.CurrentRace;
                    if (race != null)
                    {
                        maxLaps = race.TargetLaps;
                    }
                    else
                    {
                        maxLaps = EventManager.Event.Laps;
                    }

                    break;
            }

            foreach (PilotWormNode pwm in wormContainer.Children)
            {
                pwm.UpdateWorm(maxLaps);
            }
        }

        private void UpdatePilots()
        {
            wormContainer.ClearDisposeChildren();

            Race race = EventManager.RaceManager.CurrentRace;
            if (race != null)
            {
                foreach (PilotChannel pc in race.PilotChannelsSafe)
                {
                    Color color = EventManager.GetChannelColor(pc.Channel);

                    PilotWormNode pilotWormNode = new PilotWormNode(race, pc.Pilot, color, EventManager.RaceManager.TimingSystemManager.TimingSystemCount, EventManager.Event.Laps, EventManager.Event.PrimaryTimingSystemLocation);
                    wormContainer.AddChild(pilotWormNode);
                }

                wormContainer.Visible = true;
                captureContainer.Visible = false;

                AlignVertically(0.05f, wormContainer.Children.ToArray());
            }

            if (EventManager.GameManager.GameType != null && 
                EventManager.GameManager.GameType.TimingSystemPointMode == TimingSystemPointMode.CaptureTheTimer)
            {
                wormContainer.Visible = false;
                captureContainer.Visible = true;
                //wormContainer.RelativeBounds = new RectangleF(0.5f, 0.1f, 0.45f, 0.8f);
                //captureContainer.RelativeBounds = new RectangleF(0.05f, 0.1f, 0.45f, 0.8f);

                captureContainer.ClearDisposeChildren();

                int index = 0;
                foreach (Timing.ITimingSystem ts in EventManager.RaceManager.TimingSystemManager.TimingSystems)
                {
                    CapturePointNode capturePointNode = new CapturePointNode(EventManager, index);
                    captureContainer.AddChild(capturePointNode);
                    index++;
                }

                AlignHorizontally(0.05f, captureContainer.Children);
            }

            RebuiltList?.Invoke();
        }


        private void NeedsWormUpdate(object o)
        {
            needsWormUpdate = true;
        }

        private void NeedsNewPilots(object o)
        {
            needsNewPilots = true;
        }
    }

    public class PilotWormNode : Node
    {
        public int MaxLaps { get; private set; }
        public int Sectors { get; private set; }

        private TextNode pilotName;

        private AnimatedNode worm;

        private Node segmentContainer;

        public Pilot Pilot { get; private set; }
        public Race Race { get; private set; }

        private PrimaryTimingSystemLocation primaryTimingSystemLocation;
            
        private const float lineWidth = 0.001f;

        public PilotWormNode(Race race, Pilot pilot, Color color, int sectors, int maxLaps, PrimaryTimingSystemLocation primaryTimingSystemLocation)
        {
            this.primaryTimingSystemLocation = primaryTimingSystemLocation;

            pilotName = new TextNode(pilot.Name, Theme.Current.TextMain.XNA);
            pilotName.RelativeBounds = new RectangleF(0, 0, 0.1f, 1);
            AddChild(pilotName);

            segmentContainer = new Node();
            segmentContainer.RelativeBounds = new RectangleF(pilotName.RelativeBounds.Right, 0, 1 - pilotName.RelativeBounds.Right, 1f);
            AddChild(segmentContainer);

            Node wormContainer = new Node();
            wormContainer.RelativeBounds = new RectangleF(pilotName.RelativeBounds.Right, 0, 1 - pilotName.RelativeBounds.Right, 1f);
            AddChild(wormContainer);

            Race = race;
            Pilot = pilot;

            Sectors = sectors;
            
            worm = new AnimatedNode();
            wormContainer.AddChild(worm);

            ColorNode cn = new ColorNode(color);
            worm.AddChild(cn);

            ColorNode lapNode = new ColorNode(Theme.Current.TextMain.XNA);
            lapNode.RelativeBounds = new RectangleF(0, 0, lineWidth, 1);
            segmentContainer.AddChild(lapNode);

            UpdateSegments();

            UpdateWorm(maxLaps);
        }

        private void UpdateSegments()
        {
            if (MaxLaps == 0)
                return;

            int count = segmentContainer.ChildCount - 1;
            for (int l = count; l < MaxLaps * Sectors; l++)
            {
                ColorNode lapNode = new ColorNode(Theme.Current.TextMain.XNA);
                segmentContainer.AddChild(lapNode);

                for (int s = 1; s < Sectors; s++)
                {
                    ColorNode sectNode = new ColorNode(Theme.Current.TextAlt.XNA);
                    segmentContainer.AddChild(sectNode);
                }
            }

            for (int i = 0; i < segmentContainer.ChildCount; i ++)
            {
                ColorNode node = segmentContainer.GetChild<ColorNode>(i);

                int lap = i / Sectors;
                int sector = i % Sectors;

                if (sector == 0)
                {
                    float lapPos = GetCompletionPercent(lap, 0);

                    node.Color = Theme.Current.TextMain.XNA;
                    node.RelativeBounds = new RectangleF(lapPos, 0, lineWidth, 1);
                }
                else
                {
                    float sectPos = GetCompletionPercent(lap, sector);
                    node.RelativeBounds = new RectangleF(sectPos, 0, lineWidth, 1);
                    node.Color = Theme.Current.TextAlt.XNA;
                }
            }
        }

        public void UpdateWorm(int maxLaps)
        {
            MaxLaps = maxLaps;
            UpdateSegments();

            int lapNumber = -1;
            int sector = 0;

            Detection detection = Race.GetLastValidDetection(Pilot);
            if (detection != null)
            {
                sector = detection.SectorNumber;
                lapNumber = detection.LapNumber;
            }

            float completionPercent = GetCompletionPercent(lapNumber, sector);

            worm.RelativeBounds = new RectangleF(0, 0.3f, completionPercent, 0.4f);
            worm.RequestLayout();
        }

        public float GetCompletionPercent(int lap, int sector)
        {
            float maxLaps = MaxLaps;
            float sectors = Sectors;

            float holeShotWidth;
            float remainder;

            float lapWidth = 1 / maxLaps;
            float sectorWidth = (1 / sectors) * lapWidth;


            if (primaryTimingSystemLocation == PrimaryTimingSystemLocation.Holeshot)
            {
                holeShotWidth = (1 / maxLaps) * 0.1f;
            }
            else
            {
                holeShotWidth = 0;
            }

            remainder = 1 - holeShotWidth;
            

            float output =  holeShotWidth + ((lapWidth * lap) + (sector * sectorWidth)) * remainder;

            output = Math.Clamp(output, 0, 1);

            return output;
        }
    }

    public class CapturePointNode : AspectNode
    {
        private ColorNode colorNode;
        private TextNode pilotsNode;
        private EventManager eventManager;

        public GameManager GameManager
        {
            get
            {
                return eventManager.GameManager;
            }
        }

        public int TimingSystemIndex { get; private set; }

        public CapturePointNode(EventManager eve, int timingSystemIndex)
        {
            KeepAspectRatio = true;
            AspectRatio = 1;

            TimingSystemIndex = timingSystemIndex;
            eventManager = eve;

            TextNode textNode = new TextNode("Capture Point " + (timingSystemIndex + 1), Theme.Current.TextMain.XNA);
            textNode.RelativeBounds = new RectangleF(0, 0.8f, 1, 0.2f);
            AddChild(textNode);

            colorNode = new ColorNode(Color.Gray);
            colorNode.RelativeBounds = new RectangleF(0, 0.0f, 1, 0.8f);
            AddChild(colorNode);

            pilotsNode = new TextNode("", Theme.Current.TextMain.XNA);
            pilotsNode.Scale(0.9f);
            colorNode.AddChild(pilotsNode);
        }

        public void SetPilots(Pilot[] pilots, Captured captured)
        {
            Color color = GameManager.GetTeamColor(captured.Channel);

            colorNode.Color = color;
            pilotsNode.Text = string.Join("\r\n", pilots.Select(p => p.Name));
        }
    }
}
