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

namespace UI.Nodes.Rounds
{
    public class EventPackCountNode : EventPilotListNode<PilotPackCountNode>
    {
        public EventPackCountNode(RoundsNode roundsNode, EventManager ev, Round round)
            : base(roundsNode, ev, round)
        {
            SetHeading("Pack Count");
            Refresh();
        }

        protected override void UpdateButtons()
        {
            canAddTimes = true;
            base.UpdateButtons();
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

        public override string[][] MakeTable()
        {
            List<string[]> output = new List<string[]>();

            foreach (PilotPackCountNode pn in PilotNodes.OrderBy(pn => pn.Bounds.Y))
            {
                List<string> line = new List<string>();
                if (pn.Pilot != null)
                {
                    line.Add(pn.Pilot.Name);
                    line.Add(pn.Packs.ToString());
                    output.Add(line.ToArray());
                }
            }

            return output.ToArray();
        }

        public override void UpdateNodes()
        {
            IEnumerable<Race> races = EventManager.ResultManager.GetRoundRaces(Round);
            IEnumerable<Pilot> pilots = races.SelectMany(r => r.Pilots).Where(r => !r.PracticePilot).Distinct();

            SetSubHeadingRounds(races);

            if (!PilotNodes.Any(pcn => pcn.Heading))
            {
                PilotPackCountNode headingNode = new PilotPackCountNode(EventManager, null);
                contentContainer.AddChild(headingNode);
            }

            foreach (Pilot pilot in pilots)
            {
                PilotPackCountNode pn = PilotNodes.FirstOrDefault(pan => pan.Pilot == pilot);
                if (pn == null)
                {
                    pn = new PilotPackCountNode(EventManager, pilot);
                    contentContainer.AddChild(pn);
                }
            }

            foreach (PilotPackCountNode pcn in PilotNodes.ToArray())
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


            foreach (PilotPackCountNode sn in PilotNodes)
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
                        Brackets bracket = pilotRaces.First().Bracket;
                        sn.UpdateScoreText(rounds, pilotRaces, bracket);
                    }
                }
            }
        }

        public override IEnumerable<PilotPackCountNode> Order(IEnumerable<PilotPackCountNode> nodes)
        {
            return nodes.OrderByDescending(d => d.Heading)
                        .ThenBy(d => d.Bracket)
                        .ThenByDescending(d => d.Packs)
                        .ThenBy(d => EventManager.LapRecordManager.GetPBTimePosition(d.Pilot));
        }

        public override void UpdatePositions(IEnumerable<PilotPackCountNode> nodes)
        {
            int position = 0;
            int lastScore = 0;
            int inARow = 0;
            Brackets lastBracket = Brackets.None;

            foreach (PilotPackCountNode ppn in nodes)
            {
                if (lastBracket != ppn.Bracket)
                {
                    lastBracket = ppn.Bracket;
                    position = 0;
                    inARow = 0;
                }

                if (!ppn.Heading)
                {
                    if (lastScore != ppn.Packs)
                    {
                        position++;
                        position += inARow;
                        lastScore = ppn.Packs;
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

        public override bool HasResult()
        {
            if (Stage == null)
                return false;

            return Stage.PackCountAfterRound;
        }
    }

    public class PilotPackCountNode : EventPilotNode
    {
        public int Packs { get; private set; }

        private TextNode packsTextNode;

        public PilotPackCountNode(EventManager eventManager, Pilot pilot)
            : base(eventManager, pilot)
        {
            packsTextNode = new TextNode("0", Theme.Current.Rounds.Text.XNA);
            packsTextNode.Alignment = RectangleAlignment.BottomRight;
            packsTextNode.Style.Bold = true;

            positionNode.Visible = false;

            roundScoreContainer.AddChild(packsTextNode);
        }

        public void MakeHeadings(IEnumerable<Round> rounds)
        {
            packsTextNode.Remove();
            roundScoreContainer.ClearDisposeChildren();
            roundScoreContainer.AddChild(packsTextNode);
            packsTextNode.Text = "Packs";
            positionNode.Text = "";
        }

        public void UpdateScoreText(IEnumerable<Round> rounds, IEnumerable<Race> races, Brackets bracket)
        {
            Bracket = bracket;
            HasRaced = false;
            Packs = 0;

            foreach (Round round in rounds)
            {
                Race race = races.FirstOrDefault(r => r.Round == round && r.HasPilot(Pilot));
                if (race != null)
                {
                    if (race.Started)
                    {
                        Packs++;
                    }
                }
            }
            packsTextNode.Text = Packs.ToString();
        }

        protected override int GetItemWidth(Node node)
        {
            if (node != packsTextNode && node != positionNode)
            {
                return 20;
            }

            return base.GetItemWidth(node);
        }
    }
}
