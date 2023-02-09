using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using Tools;

namespace UI.Nodes
{
    public class EventLapCountsNode : EventPilotListNode<PilotLapCountsNode>
    {
        public EventLapCountsNode(EventManager ev, Round round)
            : base(ev, round)
        {
            SetHeading("Lap Count");
            Refresh();
            EventManager.ResultManager.RaceResultsChanged += PointsManager_RaceResultsChanged;
        }

        protected override void UpdateButtons()
        {
            canAddTimes = true;
            base.UpdateButtons();
        }


        public override void Dispose()
        {
            EventManager.ResultManager.RaceResultsChanged -= PointsManager_RaceResultsChanged;
            base.Dispose();
        }

        private void PointsManager_RaceResultsChanged(Race obj)
        {
            Refresh();
        }

        protected override void Recalculate()
        {
            EventManager.ResultManager.Recalculate(Round);
            RequestLayout();
        }

        public override string MakeCSV()
        {
            string csv = "";
            foreach (PilotLapCountsNode pn in PilotNodes.OrderBy(pn => pn.Bounds.Y))
            {
                string line = ",";
                if (pn.Pilot != null)
                {
                    line = pn.Pilot.Name + ",";
                    foreach (TextNode tn in pn.ResultNodes)
                    {
                        line += tn.Text + ",";
                    }
                    line += pn.TotalLaps.ToString();

                    csv += line + "\n";
                }
            }

            return csv;
        }

        public override void UpdateNodes()
        {
            IEnumerable<Race> races = EventManager.ResultManager.GetRoundPointRaces(Round);
            IEnumerable<Pilot> pilots = races.SelectMany(r => r.Pilots).Where(r => !r.PracticePilot).Distinct();

            SetSubHeadingRounds(races);

            if (!PilotNodes.Any(pcn => pcn.Heading))
            {
                PilotLapCountsNode headingNode = new PilotLapCountsNode(EventManager, null);
                contentContainer.AddChild(headingNode);
            }

            foreach (Pilot pilot in pilots)
            {
                PilotLapCountsNode pn = PilotNodes.FirstOrDefault(pan => pan.Pilot == pilot);
                if (pn == null)
                {
                    pn = new PilotLapCountsNode(EventManager, pilot);
                    contentContainer.AddChild(pn);
                }
            }

            foreach (PilotLapCountsNode pcn in PilotNodes.ToArray())
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

            Round[] rounds = races.Select(r => r.Round).Distinct().ToArray();


            foreach (PilotLapCountsNode sn in PilotNodes)
            {
                if (sn.Heading)
                {
                    sn.MakeHeadings(rounds);
                }
                else
                {
                    Pilot p = sn.Pilot;

                    IEnumerable<Race> pilotRaces = races.Where(r => r.HasPilot(p));
                    if (races.Any())
                    {
                        Race.Brackets bracket = pilotRaces.First().Bracket;
                        sn.UpdateScoreText(rounds, pilotRaces, bracket);
                    }
                }
            }
        }

        public override IEnumerable<PilotLapCountsNode> Order(IEnumerable<PilotLapCountsNode> nodes)
        {
            return nodes.OrderByDescending(d => d.Heading)
                        .ThenBy(d => d.Bracket)
                        .ThenByDescending(d => d.TotalLaps)
                        .ThenBy(d => EventManager.LapRecordManager.GetPBTimePosition(d.Pilot));
        }

        public override void UpdatePositions(IEnumerable<PilotLapCountsNode> nodes)
        {
            int position = 0;
            int lastScore = 0;
            int inARow = 0;
            Race.Brackets lastBracket = Race.Brackets.None;

            foreach (PilotLapCountsNode ppn in nodes)
            {
                if (lastBracket != ppn.Bracket)
                {
                    lastBracket = ppn.Bracket;
                    position = 0;
                    inARow = 0;
                }

                if (!ppn.Heading)
                {
                    if (lastScore != ppn.TotalLaps)
                    {
                        position++;
                        position += inARow;
                        lastScore = ppn.TotalLaps;
                        inARow = 0;
                    }
                    else
                    {
                        inARow++;
                    }
                }

                ppn.Position = position;
            }
        }
    }

    public class PilotLapCountsNode : EventPilotNode
    {
        public int TotalLaps { get; private set; }

        private TextNode totalLaps;
        public IEnumerable<TextNode> ResultNodes { get { return roundScoreContainer.Children.OfType<TextNode>(); } }

        public PilotLapCountsNode(EventManager eventManager, Pilot pilot)
            : base(eventManager, pilot)
        {
            totalLaps = new TextNode("0", Theme.Current.Rounds.Text.XNA);
            totalLaps.Alignment = RectangleAlignment.BottomRight;
            totalLaps.Style.Bold = true;
            AddChild(totalLaps);
        }

        public void MakeHeadings(IEnumerable<Round> rounds)
        {
            totalLaps.Remove();
            roundScoreContainer.ClearDisposeChildren();

            foreach (Round round in rounds)
            {
                string text = round.ToStringShort();
                TextNode pointNode = new TextNode(text, Theme.Current.Rounds.Text.XNA);
                pointNode.Alignment = RectangleAlignment.CenterRight;
                roundScoreContainer.AddChild(pointNode);
            }

            roundScoreContainer.AddChild(totalLaps);

            totalLaps.Text = "Total";
            positionNode.Text = "Pos.";
        }

        public void UpdateScoreText(IEnumerable<Round> rounds, IEnumerable<Race> races, Race.Brackets bracket)
        {
            totalLaps.Remove();
            roundScoreContainer.ClearDisposeChildren();
            Bracket = bracket;

            HasRaced = false;

            TotalLaps = 0;

            foreach (Round round in rounds)
            {
                TextNode pointNode = new TextNode("-", Theme.Current.Rounds.Text.XNA);
                roundScoreContainer.AddChild(pointNode);

                Race race = races.FirstOrDefault(r => r.Round == round && r.HasPilot(Pilot));
                if (race != null)
                {
                    if (race.Started)
                    {
                        int laps = race.GetValidLapsCount(Pilot, false);
                        pointNode.Text = laps.ToString();
                        TotalLaps += laps;
                    }
                }
            }
            roundScoreContainer.AddChild(totalLaps);
            totalLaps.Text = TotalLaps.ToString();
        }

        protected override int GetItemWidth(Node node)
        {
            if (node != totalLaps && node != positionNode)
            {
                return 20;
            }

            return base.GetItemWidth(node);
        }
    }
}
