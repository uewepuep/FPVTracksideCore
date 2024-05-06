using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class WormNode : AlphaAnimatedNode
    {
        public EventManager EventManager { get; private set; }

        private List<PilotWormNode> pilotWormList;

        private bool needsNewPilots;
        private bool needsWormUpdate;

        private Node container;

        public int PilotCount { get { return pilotWormList.Count; } }

        public event Action RebuiltList;

        public WormNode(EventManager eventManager)
        {
            ColorNode backGround = new ColorNode(Theme.Current.Panel.XNA);
            backGround.Alpha = 0.5f;
            AddChild(backGround);

            container = new Node();
            container.RelativeBounds = RectangleF.Centered(0.99f, 0.8f);
            AddChild(container);

            pilotWormList = new List<PilotWormNode>();

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
            if (needsNewPilots)
            {
                UpdatePilots();
                needsNewPilots = false;
            }

            if (needsWormUpdate)
            {
                UpdateWorms();
                needsWormUpdate = false;
            }

            base.Draw(id, parentAlpha);
        }

        private void UpdateWorms()
        {
            foreach (PilotWormNode pwm in pilotWormList)
            {
                pwm.UpdateWorm();
            }
        }

        private void UpdatePilots()
        {
            foreach (PilotWormNode pwm in pilotWormList)
            {
                pwm.Dispose();
            }

            pilotWormList.Clear();

            Race race = EventManager.RaceManager.CurrentRace;
            if (race != null)
            {
                foreach (PilotChannel pc in race.PilotChannelsSafe)
                {
                    Color color = EventManager.GetChannelColor(pc.Channel);

                    PilotWormNode pilotWormNode = new PilotWormNode(race, pc.Pilot, color, EventManager.RaceManager.TimingSystemManager.TimingSystemCount, EventManager.Event.Laps, EventManager.Event.PrimaryTimingSystemLocation);
                    pilotWormList.Add(pilotWormNode);
                    container.AddChild(pilotWormNode);
                }

                AlignVertically(0.05f, pilotWormList.ToArray());
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
        public float Laps { get; private set; }
        public float Sectors { get; private set; }

        private TextNode pilotName;

        private AnimatedNode worm;

        public Pilot Pilot { get; private set; }
        public Race Race { get; private set; }

        private PrimaryTimingSystemLocation primaryTimingSystemLocation;

        public PilotWormNode(Race race, Pilot pilot, Color color, int sectors, int laps, PrimaryTimingSystemLocation primaryTimingSystemLocation)
        {
            this.primaryTimingSystemLocation = primaryTimingSystemLocation;

            pilotName = new TextNode(pilot.Name, Theme.Current.TextMain.XNA);
            pilotName.RelativeBounds = new RectangleF(0, 0, 0.1f, 1);
            AddChild(pilotName);

            Node wormContainer = new Node();
            wormContainer.RelativeBounds = new RectangleF(pilotName.RelativeBounds.Right, 0, 1 - pilotName.RelativeBounds.Right, 1f);
            AddChild(wormContainer);

            Race = race;
            Pilot = pilot;

            Sectors = sectors;
            Laps = laps;
            
            float line = 0.001f;

            ColorNode lapNode = new ColorNode(Theme.Current.TextMain.XNA);
            lapNode.RelativeBounds = new RectangleF(0, 0, line, 1);
            wormContainer.AddChild(lapNode);

            for (int l = 0; l <= Laps; l++)
            {
                float lapPos = GetCompletionPercent(l, 0);

                lapNode = new ColorNode(Theme.Current.TextMain.XNA);
                lapNode.RelativeBounds = new RectangleF(lapPos, 0, line, 1);
                wormContainer.AddChild(lapNode);

                for (int s = 1; s < Sectors && l < Laps; s++)
                {
                    float sectPos = GetCompletionPercent(l, s);

                    ColorNode sectNode = new ColorNode(Theme.Current.TextAlt.XNA);
                    sectNode.RelativeBounds = new RectangleF(sectPos, 0, line, 1);
                    wormContainer.AddChild(sectNode);
                }
            }

            worm = new AnimatedNode();
            wormContainer.AddChild(worm);

            ColorNode cn = new ColorNode(color);
            worm.AddChild(cn);

            UpdateWorm();
        }

        public void UpdateWorm()
        {
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
            float holeShotWidth;
            float remainder;

            float lapWidth = 1 / Laps;
            float sectorWidth = (1 / Sectors) * lapWidth;


            if (primaryTimingSystemLocation == PrimaryTimingSystemLocation.Holeshot)
            {
                holeShotWidth = (1 / Laps) * 0.1f;
            }
            else
            {
                holeShotWidth = 0;
            }

            remainder = 1 - holeShotWidth;
            

            float output =  holeShotWidth + ((lapWidth * lap) + (sector * sectorWidth)) * remainder;

            output = Math.Clamp(output, 0, 1);


            Logger.TimingLog.LogCall(this, lap, sector, output);

            return output;
        }
    }
}
