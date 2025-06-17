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
    public class LapCountSummaryNode : PilotSummaryTable
    {
        public ResultManager PointsManager { get { return eventManager.ResultManager; } }

        public LapCountSummaryNode(EventManager eventManager)
            :base(eventManager, "Lap Count")
        {
            eventManager.OnPilotRefresh += Refresh;
            OrderByLast();
        }


        public override void Dispose()
        {
            eventManager.OnPilotRefresh -= Refresh;
            base.Dispose();
        }

        public override void CreateHeadings(Node container, out Round[] rounds, out int column)
        {
            rounds = eventManager.Event.Rounds.OrderBy(r => r.Order).ThenBy(r => r.RoundNumber).ToArray();

            column = 0;
            foreach (Round r in rounds)
            {
                column++;
                int ca = column;

                TextButtonNode headingText = new TextButtonNode(r.ToStringShort(), Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
                headingText.TextNode.Alignment = RectangleAlignment.TopRight;
                headingText.OnClick += (mie) => { columnToOrderBy = ca; Refresh(); };
                container.AddChild(headingText);
            }

            int c = column + 1;
            TextButtonNode total = new TextButtonNode("Total", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
            total.TextNode.Alignment = RectangleAlignment.TopRight;
            total.OnClick += (mie) => { columnToOrderBy = c; Refresh(); };
            container.AddChild(total);
        }

        public override void SetOrder()
        {
            // order them
            if (columnToOrderBy == 0)
            {
                rows.SetOrder<PilotResultNode, string>(pa => pa.Pilot.Name);
            }
            else
            {
                rows.SetOrder<PilotResultNode, double>(pa =>
                {
                    double value;
                    if (pa.GetValue(columnToOrderBy, out value))
                        return -value;
                    return 0;
                });
            }
        }

        protected override void SetResult(PilotResultNode pilotResNode, Pilot pilot, Round[] rounds)
        {
            List<Node> nodes = new List<Node>();

            int total = 0;

            foreach (Round round in rounds)
            {
                IEnumerable<Race> races = eventManager.RaceManager.GetRaces(r => r.Round == round && r.HasPilot(pilot));
                int laps = 0;
                foreach(Race race in races)
                {
                    if (race != null)
                    {
                        int raceLaps = race.GetValidLapsCount(pilot, false);
                        laps += raceLaps;

                        total += raceLaps;
                    }
                }

                TextNode rn = new TextNode("", Theme.Current.Rounds.Text.XNA);
                rn.Alignment = RectangleAlignment.TopRight;
                rn.Text = laps.ToString();
                nodes.Add(rn);

                
            }

            TextNode t = new TextNode(total.ToString(), Theme.Current.Rounds.Text.XNA);
            t.Alignment = RectangleAlignment.TopRight;
            nodes.Add(t);

            pilotResNode.Set(pilot, nodes);
        }
    }
}
