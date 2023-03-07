using Composition.Input;
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
    public class LapRecordsSummaryNode : PilotSummaryTable
    {
        public LapRecordsSummaryNode(EventManager eventManager) 
            : base(eventManager, "Lap Records")
        {
            eventManager.LapRecordManager.OnNewPersonalBest += RecordManager_OnNewBest;
            eventManager.OnEventChange += Refresh;
            eventManager.OnPilotRefresh += Refresh;
            columnToOrderBy = 3;
        }
        public override void Dispose()
        {
            base.Dispose();
            eventManager.LapRecordManager.OnNewPersonalBest -= RecordManager_OnNewBest;
            eventManager.OnEventChange -= Refresh;
            eventManager.OnPilotRefresh -= Refresh;
        }

        private void RecordManager_OnNewBest(Pilot p, int lapCount, Lap[] laps)
        {
            Refresh();
        }

        public override void CreateHeadings(Node container, out Round[] rounds, out int column)
        {
            rounds = null;
            column = 0;
            foreach (int c in eventManager.LapRecordManager.ConsecutiveLapsToTrack)
            {
                column++;
                TextButtonNode headingText = new TextButtonNode(c.ToString() + " Lap", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
                if (c > 1)
                    headingText.Text += "s";
                if (c == 0)
                    headingText.Text = "Holeshot";

                headingText.TextNode.Alignment = RectangleAlignment.TopCenter;
                container.AddChild(headingText);
                int ca = column;
                headingText.OnClick += (mie) => { columnToOrderBy = ca; Refresh(); };
            }
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
                        return value;
                    return int.MaxValue;
                });
            }
        }

        protected override void SetResult(PilotResultNode pilotResNode, Pilot pilot, Round[] rounds)
        {
            List<Node> nodes = new List<Node>();

            foreach (int consecutive in eventManager.LapRecordManager.ConsecutiveLapsToTrack)
            {
                Lap[] bestLaps;
                bool overalBest;
                eventManager.LapRecordManager.GetBestLaps(pilot, consecutive, out bestLaps, out overalBest);

                LapTimesTextNode napTimesTextNode = new LapTimesTextNode(eventManager);
                napTimesTextNode.SetLapTimes(consecutive, bestLaps, overalBest);
                nodes.Add(napTimesTextNode);
            }

            pilotResNode.Set(pilot, nodes);
        }

        public static void AddContextMenus(MouseMenu mm, EventManager eventManager, IEnumerable<Lap> laps)
        {
            Lap shortestLap = laps.Shortest();
            if (shortestLap != null)
            {
                if (laps.Count() == 1)
                {
                    mm.AddItem("Disqualify Lap", () =>
                    {
                        eventManager.RaceManager.DisqualifyLap(shortestLap);
                    });
                }
                else
                {
                    mm.AddItem("Disqualify shortest Lap in this PB", () =>
                    {
                        eventManager.RaceManager.DisqualifyLap(shortestLap);
                    });
                }

                if (!eventManager.RaceManager.RaceRunning)
                {
                    mm.AddItem("Jump to Race", () =>
                    {
                        eventManager.RaceManager.SetRace(shortestLap.Race);
                    });
                }
            }
        }

        private class LapTimesTextNode : TextNode
        {
            private Lap[] laps;

            private EventManager eventManager;

            public TimeSpan LapTime { get { return laps.TotalTime(); } }

            public LapTimesTextNode(EventManager eventManager)
                : base("", Theme.Current.InfoPanel.Text.XNA)
            {
                this.eventManager = eventManager;
            }

            public void SetLapTimes(int consecutive, Lap[] bestLaps, bool overalBest)
            {
                this.laps = bestLaps;

                TimeSpan time = bestLaps.TotalTime();
                if (bestLaps.Any())
                {
                    Text = time.ToStringRaceTime();
                }
                else
                {
                    Text = "-";
                }
                if (overalBest)
                {
                    Tint = Theme.Current.OverallBestTime.XNA;
                }
                else
                {
                    Tint = Color.White;
                }
            }

            public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
            {
                if (mouseInputEvent.ButtonState == ButtonStates.Released && mouseInputEvent.Button == MouseButtons.Right)
                {
                    MouseMenu mm = new MouseMenu(this);

                    AddContextMenus(mm, eventManager, laps);

                    mm.Show(mouseInputEvent);
                }

                return base.OnMouseInput(mouseInputEvent);
            }
        }
    }
}
