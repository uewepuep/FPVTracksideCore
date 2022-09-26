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
    public class WormNode : AnimatedNode
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
            container.RelativeBounds = new RectangleF(0.99f, 0.8f);
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

                    PilotWormNode pilotWormNode = new PilotWormNode(race, pc.Pilot, color, EventManager.RaceManager.TimingSystemManager.TimingSystemCount, EventManager.Event.Laps);
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

        private void NeedsNewPilots(object o, object p)
        {
            needsNewPilots = true;
        }

    }

    public class PilotWormNode : Node
    {
        public int Laps { get; private set; }
        public int Sectors { get; private set; }

        private TextNode pilotName;

        private AnimatedNode worm;

        public Pilot Pilot { get; private set; }
        public Race Race { get; private set; }

        public PilotWormNode(Race race, Pilot pilot, Color color, int sectors, int laps)
        {
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
            for (int l = 0; l <= Laps; l++)
            {
                float lapPos = l / (float)Laps;

                ColorNode lapNode = new ColorNode(Theme.Current.TextMain.XNA);
                lapNode.RelativeBounds = new RectangleF(lapPos, 0, line, 1);
                wormContainer.AddChild(lapNode);

                for (int s = 1; s <= Sectors; s++)
                {
                    float sectPos = (s / (float)Sectors) / (float)Laps;

                    ColorNode sectNode = new ColorNode(Theme.Current.TextAlt.XNA);
                    sectNode.RelativeBounds = new RectangleF(lapNode.RelativeBounds.X + sectPos, 0, line, 1);
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
            float completionPercent = 0;
            int lapNumber = 1;

            Lap lap = Race.GetLastValidLap(Pilot);

            if (lap != null)
            {
                completionPercent = lap.Detection.LapNumber / (float)Laps;
                lapNumber = lap.Number + 1;
            }

            Detection detection = Race.GetLastValidDetection(Pilot, lapNumber);
            if (detection != null)
            {
                completionPercent += ((detection.TimingSystemIndex + 1) / (float)Sectors) / (float)Laps;
            }

            completionPercent = Math.Min(1, completionPercent);

            worm.RelativeBounds = new RectangleF(0, 0.3f, completionPercent, 0.4f);
        }
    }
}
