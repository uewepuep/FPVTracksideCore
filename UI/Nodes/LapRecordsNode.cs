using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using OfficeOpenXml.Style;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class LapRecordsNode : Node
    {
        private EventManager eventManager;
        private LapRecordManager recordManager;

        private ListNode<PilotLapsNode> rows;

        private Node headings;

        private int[] tracking;

        private int columnToOrderBy;

        public Pilot[] FilterPilots { get; private set; }

        public int ItemHeight { get { return rows.ItemHeight; } set { rows.ItemHeight = value; } }

        private bool positions;

        public LapRecordsNode(EventManager em, bool positions)
        {
            this.positions= positions;

            eventManager = em;
            recordManager = em.LapRecordManager;

            PanelNode panelNode = new PanelNode();
            AddChild(panelNode);
            
            HeadingNode heading = new HeadingNode(Theme.Current.InfoPanel, "Lap Records");
            panelNode.AddChild(heading);

            headings = new Node();
            headings.RelativeBounds = new Tools.RectangleF(0, heading.RelativeBounds.Bottom, 1, 0.05f);
            panelNode.AddChild(headings);
                     
            rows = new ListNode<PilotLapsNode>(Theme.Current.ScrollBar.XNA);
            rows.RelativeBounds = new Tools.RectangleF(0, headings.RelativeBounds.Bottom, 1, 1 - headings.RelativeBounds.Bottom);
            rows.ItemHeight = 40;
            rows.ItemPadding = 0;
            rows.Scale(0.99f);

            rows.BackgroundColors = new Color[]
            {
                new Color(Theme.Current.PanelAlt.XNA, 0.5f),
                new Color(Theme.Current.Panel.XNA, 0.5f)
            };

            panelNode.AddChild(rows);

            // Try to order by the PB
            var list = recordManager.ConsecutiveLapsToTrack.ToList();
            if (list.Contains(1))
            {
                columnToOrderBy = list.IndexOf(1);
            }
            else
            {
                columnToOrderBy = 0;
            }

            rebuildHeadings(list);

            recordManager.OnNewPersonalBest += RecordManager_OnNewBest;
            eventManager.OnEventChange += Refresh;
            eventManager.OnPilotRefresh += Refresh;

            FilterPilots = new Pilot[0];

            Refresh();
        }

        public override void Dispose()
        {
            recordManager.OnNewPersonalBest -= RecordManager_OnNewBest;
            eventManager.OnEventChange -= Refresh;
            eventManager.OnPilotRefresh -= Refresh;

            base.Dispose();
        }

        private void RecordManager_OnNewBest(Pilot p, int lapCount, Lap[] laps)
        {
            SetPilots(null);
        }

        private void rebuildHeadings(IEnumerable<int> toTrack)
        {
            tracking = toTrack.ToArray(); ;

            rows.ClearDisposeChildren();
            headings.ClearDisposeChildren();

            if (positions)
            {
                Node headingNode = new Node();
                headings.AddChild(headingNode);

                TextNode position = new TextNode("Position", Theme.Current.InfoPanel.Text.XNA);
                position.Alignment = RectangleAlignment.BottomCenter;
                position.Scale(1, 0.7f);
                headingNode.AddChild(position);
            }

            // Add pilot names list..
            {
                Node headingNode = new Node();
                headings.AddChild(headingNode);

                TextButtonNode headingText = new TextButtonNode("Pilots", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
                headingText.TextNode.Alignment = RectangleAlignment.TopCenter;
                headingNode.AddChild(headingText);
                headingText.OnClick += (mie) => { columnToOrderBy = 0; SetPilots(null); };
            }

            int column = 0;
            foreach (int c in tracking)
            {
                column++;
                Node headingNode = new Node();
                headings.AddChild(headingNode);

                TextButtonNode headingText = new TextButtonNode(c.ToString() + " Lap", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
                if (c > 1)
                    headingText.Text += "s";
                if (c == 0)
                    headingText.Text = "Holeshot";

                headingText.TextNode.Alignment = RectangleAlignment.TopCenter;
                headingNode.AddChild(headingText);
                int ca = column;
                headingText.OnClick += (mie) => { columnToOrderBy = ca; SetPilots(null); };
            }

            AlignHorizontally(0.02f, headings.Children.ToArray());
        }

        public void SetFilterPilots(IEnumerable<Pilot> pilots)
        {
            FilterPilots = pilots.ToArray();
            Refresh();
        }

        public void Refresh()
        {
            SetPilots(null);
        }

        private void SetPilots(Pilot pilotWithChange)
        {
            if (tracking.Length != recordManager.ConsecutiveLapsToTrack.Length || tracking.Intersect(recordManager.ConsecutiveLapsToTrack).Count() != recordManager.ConsecutiveLapsToTrack.Length)
            {
                rebuildHeadings(recordManager.ConsecutiveLapsToTrack);
            }

            IEnumerable<Pilot> pilots = eventManager.Event.Pilots.Distinct();

            if (FilterPilots.Any())
            {
                pilots = pilots.Intersect(FilterPilots);
            }
            else
            {
                // Remove all casual practice pilots if we're not in casual practice.
                if (eventManager.Event.EventType != EventTypes.CasualPractice)
                {
                    pilots = pilots.Where(p => !p.PracticePilot);
                }
            }

            int index = 0;    
            foreach (Pilot p in pilots)
            {
                if (p == null)
                    continue;

                PilotLapsNode t = GetCreateNode(index);
                if (t.LapTimeCount != recordManager.ConsecutiveLapsToTrack.Length)
                {
                    t = GetCreateNode(index);
                }

                bool updateTimes = false;

                if (t.Pilot != p)
                {
                    t.SetPilot(p);
                    updateTimes = true;
                }

                if (pilotWithChange == t.Pilot || pilotWithChange == null)
                {
                    updateTimes = true;
                }

                if (updateTimes)
                {
                    for (int i = 0; i < recordManager.ConsecutiveLapsToTrack.Length; i++)
                    {
                        int consecutive = recordManager.ConsecutiveLapsToTrack[i];

                        Lap[] bestLaps;
                        bool overalBest;
                        recordManager.GetBestLaps(p, consecutive, out bestLaps, out overalBest);

                        t.SetLapTimes(consecutive, bestLaps, overalBest);
                    }
                }
                index++;
            }

            for (; index < rows.ChildCount; index++)
            {
                PilotLapsNode t = GetCreateNode(index);
                t.Dispose();
            }

            // order them
            if (columnToOrderBy == 0)
            {
                rows.SetOrder<PilotLapsNode, string>(pa => pa.Pilot.Name);
            }
            else
            {
                int i = columnToOrderBy - 1;
                int consecutive = recordManager.ConsecutiveLapsToTrack[i];

                rows.SetOrder<PilotLapsNode, double>(pa => 
                {
                    return pa.GetLapTime(consecutive).TotalSeconds;
                });
            }

            for (int i = 0; i < rows.ChildCount; i++) 
            {
                PilotLapsNode pilotLapsNode = rows.GetChild<PilotLapsNode>(i);
                if (pilotLapsNode != null) 
                {
                    pilotLapsNode.Position = i + 1;
                }
            }

            rows.RequestLayout();
            RequestLayout();
        }

        private PilotLapsNode GetCreateNode(int index)
        {
            while (index >= rows.ChildCount)
            {
                PilotLapsNode tn = new PilotLapsNode(positions, eventManager, recordManager.ConsecutiveLapsToTrack);
                rows.AddChild(tn);
            }
            return (PilotLapsNode)rows.GetChild(index);
        }

        public static void AddDisqualifyOptions(MouseMenu mm, EventManager eventManager, IEnumerable<Lap> laps)
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

        private class PilotLapsNode : Node
        {
            public Pilot Pilot { get; private set;  }
            private TextNode pilotName;
            private LapTimesTextNode[] lapTimeNodes;

            private List<int> consecutives;
            private EventManager eventManager;

            public int LapTimeCount { get { return lapTimeNodes.Length; } }

            private int position;
            public int Position 
            { 
                get 
                { 
                    return position; 
                }
                set
                {
                    position = value;

                    if (positionNode != null)
                    {
                        positionNode.Text = value.ToStringPosition();
                    }
                }
            } 

            private TextNode positionNode;

            public PilotLapsNode(bool positions, EventManager eventManager, IEnumerable<int> consecutives)
            {
                this.eventManager = eventManager;
                this.consecutives = consecutives.ToList();

                Node n = new Node();
                n.Scale(1, 0.75f);
                AddChild(n);

                if (positions)
                {
                    positionNode = new TextNode("", Theme.Current.InfoPanel.Text.XNA);
                    n.AddChild(positionNode);
                }

                pilotName = new TextNode("", Theme.Current.InfoPanel.Text.XNA);
                n.AddChild(pilotName);

                lapTimeNodes = new LapTimesTextNode[consecutives.Count()];

                for (int i = 0; i < consecutives.Count(); i++)
                {
                    lapTimeNodes[i] = new LapTimesTextNode(eventManager);
                    n.AddChild(lapTimeNodes[i]);
                }

                AlignHorizontally(0.02f, n.Children.ToArray());
            }

            public TimeSpan GetLapTime(int consecutive)
            {
                int index = consecutives.IndexOf(consecutive);
                if (index >= 0 && index < lapTimeNodes.Length)
                {
                    LapTimesTextNode c = lapTimeNodes[index];
                    return c.LapTime;
                }
                return TimeSpan.MaxValue;
            }

            public void SetLapTimes(int consecutive, Lap[] bestLaps, bool overalBest)
            {

                int index = consecutives.IndexOf(consecutive);
                if (index >= 0 && index < lapTimeNodes.Length)
                {
                    LapTimesTextNode c = lapTimeNodes[index];
                    c.SetLapTimes(consecutive, bestLaps, overalBest);
                }
            }

            public void SetPilot(Pilot p)
            {
                pilotName.Text = p.Name;
                Pilot = p;
            }

            public void Clear()
            {
                pilotName.Text = "";

                foreach (TextNode n in lapTimeNodes)
                {
                    n.Text = "";
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

                    AddDisqualifyOptions(mm, eventManager, laps);

                    mm.Show(mouseInputEvent);
                }

                return base.OnMouseInput(mouseInputEvent);
            }


        }
    }

}
