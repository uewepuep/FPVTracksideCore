using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using RaceLib;
using Tools;

namespace UI.Nodes
{
    public class EventLapsNode : EventPilotListNode<EventPilotTimeNode>
    {
        public EventLapsNode(EventManager ev, Round round) 
            : base(ev, round)
        {
        }

        protected override void UpdateButtons()
        {
            canSum = true;
            canAddLapCount = true;
            base.UpdateButtons();
        }

        public override void UpdateNodes()
        {
            Round startRound = EventManager.RoundManager.GetFirstRound(Round.EventType, Round.RoundType);

            if (startRound == null)
            {
                return;
            }

            IEnumerable<Race> races = EventManager.RaceManager.GetRaces(startRound, Round);
            IEnumerable<Pilot> pilots = races.SelectMany(r => r.Pilots).Distinct();

            SetSubHeadingRounds(races);

            // Remove all casual practice pilots if we're not in casual practice.
            if (EventManager.Event.EventType != EventTypes.CasualPractice)
            {
                pilots = pilots.Where(p => !p.PracticePilot);
            }

            if (!PilotNodes.Any(pcn => pcn.Heading))
            {
                EventPilotTimeNode headingNode = new EventPilotTimeNode(EventManager, null);
                contentContainer.AddChild(headingNode);
            }

            foreach (Pilot pilot in pilots)
            {
                EventPilotNode pn = PilotNodes.FirstOrDefault(pan => pan.Pilot == pilot);
                if (pn == null)
                {
                    pn = new EventPilotTimeNode(EventManager, pilot);
                    contentContainer.AddChild(pn);
                }
            }

            foreach (EventPilotNode pcn in PilotNodes.ToArray())
            {
                if (pcn.Heading)
                {
                    continue;
                }

                if (!pilots.Contains(pcn.Pilot))
                {
                    pcn.Dispose();
                }
            }

            int lapsToCount = EventManager.Event.PBLaps;
            if (Round.TimeSummary != null)
            {
                string heading = "";

                if (Round.TimeSummary.TimeSummaryType == TimeSummary.TimeSummaryTypes.PB)
                {
                    heading = "PB Records";
                    lapsToCount = EventManager.Event.PBLaps;
                }
                else
                {
                    heading = "Lap Records";
                    lapsToCount = EventManager.Event.Laps;
                }

                SetHeading(heading);
            }

            foreach (EventPilotTimeNode pilotNode in PilotNodes)
            {
                IEnumerable<Lap> laps = EventManager.LapRecordManager.GetBestLaps(races, pilotNode.Pilot, lapsToCount);
                pilotNode.SetLaps(laps);
            }
        }

        public override IEnumerable<EventPilotTimeNode> Order(IEnumerable<EventPilotTimeNode> nodes)
        {
            return nodes.OrderByDescending(t => t.Heading).ThenBy(t => t.Time);
        }

        public override void EditSettings()
        {
            ObjectEditorNode<TimeSummary> editor = new ObjectEditorNode<TimeSummary>(Round.TimeSummary);
            GetLayer<PopupLayer>().Popup(editor);
            editor.OnOK += (a) =>
            {
                SaveRound();
                Recalculate();
            };
        }

        public override string MakeCSV()
        {
            string csv = "Pilot, Time\n";
            foreach (EventPilotTimeNode pn in PilotNodes.OrderBy(pn => pn.Bounds.Y))
            {
                if (pn.Pilot != null)
                {
                    csv += pn.Pilot.Name + "," + pn.TimeNode.Text + "\n";
                }
            }
            return csv;
        }
    }

    public class EventPilotTimeNode : EventPilotNode
    {
        public TimeSpan Time { get; private set; }
        public Lap[] Laps { get; private set; }

        public TextNode TimeNode { get; private set; }

        public EventPilotTimeNode(EventManager eventManager, Pilot pilot)
            : base(eventManager, pilot)
        {
            TimeNode = new TextNode("", Theme.Current.Rounds.Text.XNA);
            roundScoreContainer.AddChild(TimeNode);

            if (Heading)
            {
                TimeNode.Text = "Time";
                positionNode.Text = "Pos.";
            }
            Laps = new Lap[0];
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.Button == MouseButtons.Right && Laps.Any())
            {
                Race race = Laps.First().Race;

                MouseMenu mm = new MouseMenu(this);
                if (!eventManager.RaceManager.RaceRunning)
                {
                    mm.AddItem("Jump to Race", () =>
                    {
                        eventManager.RaceManager.SetRace(race);
                    });
                }

                mm.Show(mouseInputEvent);
            }

            return base.OnMouseInput(mouseInputEvent);
        }

        public void SetLaps(IEnumerable<Lap> laps)
        {
            if (Heading)
                return;

            Laps = laps.ToArray();
            Time = Laps.TotalTime();

            if (Time != TimeSpan.MaxValue && Time != TimeSpan.Zero)
            {
                TimeNode.Text = Time.ToStringRaceTime();
            }
            else
            {
                TimeNode.Text = "";
            }
        }
    }
}
