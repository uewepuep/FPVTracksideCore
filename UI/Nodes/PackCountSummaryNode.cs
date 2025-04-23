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
    public class PackCountSummaryNode : PilotSummaryTable
    {
        public ResultManager PointsManager { get { return eventManager.ResultManager; } }

        public PackCountSummaryNode(EventManager eventManager)
            :base(eventManager, "Pack Count")
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

            int c = column + 1;
            TextButtonNode total = new TextButtonNode("Packs Total", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
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
                Race race = eventManager.RaceManager.GetRaces(r => r.Round == round && r.HasPilot(pilot)).FirstOrDefault();

                if (race != null)
                {
                    int laps = race.GetValidLapsCount(pilot, false);
                    if (laps > 0)
                    {
                        total += 1;
                    }
                }
            }

            TextNode t = new TextNode(total.ToString(), Theme.Current.Rounds.Text.XNA);
            t.Alignment = RectangleAlignment.TopRight;
            nodes.Add(t);

            pilotResNode.Set(pilot, nodes);
        }
    }
}
