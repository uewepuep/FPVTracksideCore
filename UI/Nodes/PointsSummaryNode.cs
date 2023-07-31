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
    public class PointsSummaryNode : PilotSummaryTable
    {
        public ResultManager PointsManager { get { return eventManager.ResultManager; } }

        public PointsSummaryNode(EventManager eventManager)
            :base(eventManager, "Points")
        {
        }

        public override void CreateHeadings(Node container, out Round[] rounds, out int column)
        {
            rounds = eventManager.Event.Rounds.Where(r => r.EventType.HasPoints()).OrderBy(r => r.Order).ThenBy(r => r.RoundNumber).ToArray();

            bool rollOver = rounds.Where(r => r.PointSummary != null).Any(r => r.PointSummary.RoundPositionRollover);

            column = 0;
            bool prevTotal = false;
            foreach (Round r in rounds)
            {
                column++;
                int ca = column;

                if (prevTotal && rollOver)
                {
                    int ca3 = column;

                    TextButtonNode rro = new TextButtonNode("RRO", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
                    rro.TextNode.Alignment = RectangleAlignment.TopRight;
                    rro.OnClick += (mie) => { columnToOrderBy = ca3; Refresh(); };
                    container.AddChild(rro);

                    prevTotal = false;
                    column++;
                }

                TextButtonNode headingText = new TextButtonNode(r.ToStringShort(), Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
                headingText.TextNode.Alignment = RectangleAlignment.TopRight;
                headingText.OnClick += (mie) => { columnToOrderBy = ca; Refresh(); };
                container.AddChild(headingText);

                if (r.PointSummary != null)
                {
                    column++;
                    int ca2 = column;

                    TextButtonNode sumText = new TextButtonNode("Total", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
                    sumText.TextNode.Alignment = RectangleAlignment.TopRight;
                    sumText.OnClick += (mie) => { columnToOrderBy = ca2; Refresh(); };
                    container.AddChild(sumText);
                    prevTotal = true;
                }
            }
        }

        public override IEnumerable<IEnumerable<string>> GetTable()
        {
            OrderByLast();
            return base.GetTable();
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
            bool prevTotal = false;

            List<Node> nodes = new List<Node>();

            bool rollOver = rounds.Where(r => r.PointSummary != null).Any(r => r.PointSummary.RoundPositionRollover);

            foreach (Round round in rounds)
            {
                if (prevTotal && rollOver)
                {
                    Round rolloverRound = eventManager.ResultManager.GetRollOverRound(round);
                    if (rolloverRound != null)
                    {
                        Result res = eventManager.ResultManager.GetRollOver(pilot, rolloverRound, false);

                        ResultNode tn = new ResultNode(res);
                        tn.Alignment = RectangleAlignment.CenterRight;
                        nodes.Add(tn);
                        prevTotal = false;
                    }
                }

                Result r = PointsManager.GetResult(round, pilot);
                ResultNode rn = new ResultNode(r);
                nodes.Add(rn);

                if (round.PointSummary != null)
                {
                    prevTotal = true;
                    int points = PointsManager.GetPointsTotal(round, pilot);
                    TextNode tn = new TextNode(points.ToString(), Theme.Current.Rounds.Text.XNA);
                    tn.Alignment = RectangleAlignment.CenterRight;
                    tn.Style.Bold = true;
                    nodes.Add(tn);
                }
            }

            pilotResNode.Set(pilot, nodes);
        }
    }
}
