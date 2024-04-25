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

namespace UI.Nodes.Rounds
{
    public class EventLapsTimesNode : EventPilotListNode<EventPilotTimeNode>
    {
        public EventLapsTimesNode(EventManager ev, Round round)
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
            if (Round.TimeSummary == null)
                return;

            Round startRound = Round;

            bool includeAllRounds = Round.TimeSummary.IncludeAllRounds;

            while (startRound != null)
            {
                Round prevRound = EventManager.RoundManager.PreviousRound(startRound);
                if (prevRound == null)
                {
                    break;
                }

                if (prevRound.TimeSummary != null && !includeAllRounds)
                {
                    break;
                }

                startRound = prevRound;

            }

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
                    lapsToCount = EventManager.Event.PBLaps;
                }
                else
                {
                    lapsToCount = EventManager.Event.Laps;
                }

                heading = lapsToCount + " Lap" + (lapsToCount > 1 ? "s" : "");
                SetHeading(heading);

                if (Round.TimeSummary.TimeSummaryType == TimeSummary.TimeSummaryTypes.RaceTime)
                {
                    SetHeading("Race Time");
                }
            }

            foreach (EventPilotTimeNode pilotNode in PilotNodes)
            {
                IEnumerable<Lap> laps;
                IEnumerable<Race> pilotInRaces = races.Where(r => r.HasPilot(pilotNode.Pilot));

                if (Round.TimeSummary.TimeSummaryType == TimeSummary.TimeSummaryTypes.RaceTime)
                {
                    laps = LapRecordManager.GetBestRaceTime(pilotInRaces, pilotNode.Pilot, lapsToCount);
                }
                else
                {
                    laps = LapRecordManager.GetBestLaps(pilotInRaces, pilotNode.Pilot, lapsToCount);
                }
                Brackets bracket = Brackets.None;
                var brackets = pilotInRaces.Select(r => r.Bracket).Distinct();
                if (brackets.Count() == 1)
                {
                    bracket = brackets.First();
                }

                pilotNode.SetLaps(laps, bracket);
            }
        }

        public override IEnumerable<EventPilotTimeNode> Order(IEnumerable<EventPilotTimeNode> nodes)
        {
            return nodes.OrderByDescending(t => t.Heading).ThenBy(t => t.Bracket).ThenBy(t => t.Time);
        }

        public override void EditSettings()
        {
            ObjectEditorNode<TimeSummary> editor = new ObjectEditorNode<TimeSummary>(Round.TimeSummary);
            editor.Scale(0.6f);
            GetLayer<PopupLayer>().Popup(editor);
            editor.OnOK += (a) =>
            {
                SaveRound();
                EventManager.ResultManager.Recalculate(Round);
                RequestLayout();
            };
        }

        public override string MakeCSV()
        {
            string csv = "Pilot, Time\n";
            foreach (EventPilotTimeNode pn in PilotNodes.OrderBy(pn => pn.Bounds.Y))
            {
                if (pn.Pilot != null)
                {
                    csv += Maths.MakeCSVLine(pn.Pilot.Name, pn.TimeNode.Text);
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

        public override void AddMenu(MouseInputEvent mouseInputEvent, MouseMenu mouseMenu)
        {
            if (Laps.Any())
            {
                Race race = Laps.First().Race;

                if (!eventManager.RaceManager.RaceRunning)
                {
                    mouseMenu.AddItem("Jump to Race", () =>
                    {
                        eventManager.RaceManager.SetRace(race);
                    });
                }
            }

            base.AddMenu(mouseInputEvent, mouseMenu);
        }

        public void SetLaps(IEnumerable<Lap> laps, Brackets bracket)
        {
            if (Heading)
                return;

            Bracket = bracket;

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
