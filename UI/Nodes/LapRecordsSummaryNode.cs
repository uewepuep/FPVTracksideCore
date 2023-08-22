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
            column++;

            if (eventManager.Event.EventType == EventTypes.Race)
            {
                TextButtonNode racetimeHeading = new TextButtonNode("Race Time", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
                racetimeHeading.TextNode.Alignment = RectangleAlignment.TopCenter;
                container.AddChild(racetimeHeading);
                int ca2 = column;
                racetimeHeading.OnClick += (mie) => { columnToOrderBy = ca2; Refresh(); };
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

        public override void CalculatePositions()
        {
            int index = columnToOrderBy - 1;
            if (index >= 0 && index < eventManager.LapRecordManager.ConsecutiveLapsToTrack.Length)
            {
                int orderLap = eventManager.LapRecordManager.ConsecutiveLapsToTrack[index];

                for (int i = 0; i < rows.ChildCount; i++)
                {
                    PilotResultNode pilotLapsNode = rows.GetChild<PilotResultNode>(i);
                    if (pilotLapsNode != null)
                    {
                        int? old = eventManager.LapRecordManager.GetPastPosition(pilotLapsNode.Pilot, orderLap);

                        pilotLapsNode.SetPosition(i + 1, old);
                    }
                }
            }
            else
            {
                base.CalculatePositions();
            }
        }

        protected override void SetResult(PilotResultNode pilotResNode, Pilot pilot, Round[] rounds)
        {
            List<Node> nodes = new List<Node>();
            Lap[] bestLaps;
            bool overalBest;

            foreach (int consecutive in eventManager.LapRecordManager.ConsecutiveLapsToTrack)
            {
                eventManager.LapRecordManager.GetBestLaps(pilot, consecutive, out bestLaps, out overalBest);

                LapTimesTextNode napTimesTextNode = new LapTimesTextNode(eventManager);
                napTimesTextNode.SetLapTimes(bestLaps, overalBest);
                nodes.Add(napTimesTextNode);
            }

            if (eventManager.Event.EventType == EventTypes.Race)
            {
                eventManager.LapRecordManager.GetBestRaceTime(pilot, out bestLaps, out overalBest);

                LapTimesTextNode lapTimesTextNode = new LapTimesTextNode(eventManager);
                lapTimesTextNode.SetLapTimes(bestLaps, overalBest);
                nodes.Add(lapTimesTextNode);
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

        public class LapTimesTextNode : TextNode
        {
            private Lap[] laps;

            private EventManager eventManager;

            public TimeSpan LapTime { get { return laps.TotalTime(); } }

            public LapTimesTextNode(EventManager eventManager)
                : base("", Theme.Current.InfoPanel.Text.XNA)
            {
                this.eventManager = eventManager;
            }

            public void SetLapTimes(Lap[] bestLaps, bool overalBest)
            {
                if (bestLaps == null)
                {
                    bestLaps = new Lap[0];
                }

                this.laps = bestLaps;

                TimeSpan time = bestLaps.TotalTime();
                if (bestLaps.Any())
                {
                    Text = time.ToStringRaceTime();

                    Race r = bestLaps.First().Race;

                    if (r != null) 
                    {
                        Text += " (R" + r.RoundNumber + ")";
                    }
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
