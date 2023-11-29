using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;

namespace UI.Nodes
{
    public class LapsNode : Node, IUpdateableNode
    {
        private LapNode[] lapNodes;

        public Pilot Pilot { get; private set; }

        public EventManager EventManager { get; private set; }

        private int lapsPerRow;

        private ColorNode backgroundNode;

        private Color channelColor;
        public Color ChannelColor { get => channelColor; set { channelColor = value; UpdateLapCount(); } }

        public bool BackgroundVisible { get { return backgroundNode.Visible; } set { backgroundNode.Visible = value; } }

        private object locker;

        public int LapLines { get; set; }

        private TableNode table;

        private DateTime? playbackTime;

        public LapsNode(EventManager em)
        {
            locker = new object();
            table = new TableNode();
            EventManager = em;

            backgroundNode = new ColorNode(Theme.Current.PilotViewTheme.LapBackground);
            AddChild(backgroundNode);

            AddChild(table);
            LapLines = 1;

            UpdateLapCount();

            em.RaceManager.OnLapDisqualified += RaceManager_OnLapDisqualified;
        }

        public override void Dispose()
        {
            EventManager.RaceManager.OnLapDisqualified -= RaceManager_OnLapDisqualified;
            base.Dispose();
        }

        private void RaceManager_OnLapDisqualified(Lap lap)
        {
            if (lap.Pilot == Pilot)
            {
                RefreshData();
            }
        }

        private void UpdateLapCount()
        {
            lock (locker)
            {
                if (lapNodes != null)
                {
                    foreach (var n in lapNodes)
                    {
                        n.Dispose();
                    }
                }

                lapsPerRow = GetLapsPerRowCount();
                table.SetSize(LapLines, lapsPerRow);

                lapNodes = new LapNode[table.CellCount];

                for (int i = 0; i < lapNodes.Length; i++)
                {
                    LapNode tn = new LapNode(EventManager, ChannelColor);
                    tn.OnRightClick += Menu;
                    tn.IncludeLapNumber = true;
                    tn.Visible = false;

                    Node container = table.GetCell(i);
                    if (container != null)
                    {
                        container.AddChild(tn);
                        lapNodes[i] = tn;
                    }
                }
            }
        }

        private int GetLapsPerRowCount()
        {
            if (!EventManager.RaceManager.RaceType.HasLapCount())
                return 5;

            Race currentRace = EventManager.RaceManager.CurrentRace;
            if (currentRace != null)
            {
                int laps = currentRace.TargetLaps;

                if (EventManager.Event.PrimaryTimingSystemLocation == PrimaryTimingSystemLocation.Holeshot)
                    laps++;

                // Get the laps the pilot has done..
                if (Pilot != null)
                {
                    int pilotLaps = currentRace.GetValidLapsCount(Pilot, true);
                    laps = Math.Max(laps, pilotLaps);
                }

                return Math.Min(5, Math.Max(2, laps));
            }

            return 5;
        }

        public void SetPilot(Pilot pilot)
        {
            if (Pilot != pilot)
            {
                Pilot = pilot;
            }
            RefreshData();
        }

        public void RefreshData()
        {
            if (Pilot == null)
                return;

            if (lapsPerRow != GetLapsPerRowCount() || LapLines != table.Rows)
            {
                UpdateLapCount();
            }

            Race r = EventManager.RaceManager.CurrentRace;

            if (r == null)
                return;

            if (!r.HasPilot(Pilot))
                return;

            Lap[] laps = r.GetValidLaps(Pilot, true).OrderBy(l => l.End).ToArray();

            if (playbackTime.HasValue)
            {
                laps = laps.Where(lap => lap.Detection.Time < playbackTime.Value).ToArray();
            }

            RefreshData(laps);
        }

        public void RefreshData(Lap[] laps)
        { 
            lock (locker)
            {
                if (lapNodes == null)
                    return;

                int lapStart = Math.Max(0, laps.Length - lapNodes.Length);
                for (int i = 0; i < lapNodes.Length; i++)
                {
                    LapNode lapNode = lapNodes[i];
                    if (lapNode == null)
                        continue;

                    int lapIndex = lapStart + i;
                    if (lapIndex < laps.Length)
                    {
                        Lap lap = laps[lapIndex];
                        if (lap == null)
                            continue;
                        
                        lapNode.SetLap(lap);

                        bool overalBest;
                        if (EventManager.LapRecordManager.IsRecordLap(lap, out overalBest))
                        {
                            if (overalBest)
                            {
                                lapNode.Tint = Theme.Current.OverallBestTime.XNA;
                            }
                            else
                            {
                                lapNode.Tint = Theme.Current.NewPersonalBest.XNA;
                            }
                        }
                        else
                        {
                            lapNode.Tint = Color.White;
                        }
                    }
                    else
                    {
                        lapNodes[i].Clear();
                    }
                }
            }

            RequestLayout();
        }

        public void AddLap(Lap lap)
        {
            RefreshData();
        }

        public void ClearLaps()
        {
            lock (locker)
            {
                for (int i = 0; i < lapNodes.Length; i++)
                {
                    lapNodes[i].Clear();
                }
                RequestLayout();
            }
        }

        public void Update(GameTime gameTime)
        {
            lock (locker)
            {
                for (int i = 0; i < lapNodes.Length && i < lapNodes.Length; i++)
                {
                    bool hasLap = lapNodes[i].Lap != null;
                    lapNodes[i].Visible = hasLap;
                }
            }
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (!base.OnMouseInput(mouseInputEvent))
            {
                if (mouseInputEvent.Button == MouseButtons.Right && mouseInputEvent.ButtonState == ButtonStates.Released)
                {
                    Menu(mouseInputEvent, null);
                    return true;
                }
                return false;
            }

            return true;
        }

        private void Menu(MouseInputEvent mouseInputEvent, LapNode lapNode)
        {
            MouseMenu mm = new MouseMenu(this);

            Lap lap = null;
            if (lapNode != null)
            {
                lap = lapNode.Lap;
            }

            if (EventManager.RaceManager.RaceRunning)
            {
                mm.AddItem("Add Lap Now", () =>
                {
                    EventManager.RaceManager.AddManualLap(Pilot, DateTime.Now);
                });
            }  

            if (EventManager.RaceManager.RaceFinished)
            {
                // if we're in video playback do some adjustments..
                if (playbackTime.HasValue)
                {
                    mm.AddItem("Add Lap Now", () =>
                    {
                        EventManager.RaceManager.AddManualLap(Pilot, playbackTime.Value);
                    });
                }
            }
            if (EventManager.RaceManager.RaceStarted)
            {
                mm.AddItem("Add Lap time...", () =>
                {
                    GetLayer<PopupLayer>().Popup(new AddLapTimeNode(EventManager.RaceManager, Pilot));
                });

                mm.AddItem("Edit Laps", () =>
                {
                    Race currentRace = EventManager.RaceManager.CurrentRace;
                    if (currentRace == null)
                        return;

                    Channel channel = currentRace.GetChannel(Pilot);
                    if (channel == null)
                        return;

                    Color c = EventManager.GetChannelColor(channel);

                    Lap[] editLaps = currentRace.GetLaps(Pilot).ToArray();

                    if (editLaps.Any())
                    {
                        //LapEditorNode editor = new LapEditorNode(editLaps, c);
                        LapEditorNode editor = new LapEditorNode(EventManager.RaceManager, editLaps, c);
                        GetLayer<PopupLayer>().Popup(editor);
                        editor.OnOK += (v) =>
                        {
                            using (IDatabase db = DatabaseFactory.Open())
                            {
                                db.Update(editLaps);
                                currentRace.ReCalculateLaps(db, Pilot);
                            }

                            EventManager.LapRecordManager.ClearPilot(Pilot);
                            EventManager.LapRecordManager.UpdatePilot(Pilot);
                            EventManager.SpeedRecordManager.UpdatePilot(Pilot);
                            RefreshData();
                        };
                    }
                });
            }

            if (lap != null)
            {
                mm.AddItem("Disqualify Lap", () =>
                {
                    EventManager.RaceManager.DisqualifyLap(lap);
                });

                mm.AddSubmenu("Split Lap into...", (i) =>
                {
                    EventManager.RaceManager.SplitLap(lap, i);
                }, new int[] { 2, 3, 4, 5, 6, 7 });
            }

            mm.Show(mouseInputEvent);
        }

        public void SetPlaybackTime(DateTime time)
        {
            playbackTime = time;
            if (playbackTime.HasValue)
            {
                Race r = EventManager.RaceManager.CurrentRace;

                if (r == null)
                    return;

                if (!r.HasPilot(Pilot))
                    return;

                Lap[] laps = r.GetValidLaps(Pilot, true).OrderBy(l => l.End).Where(lap => lap.Detection.Time <= playbackTime.Value).ToArray();
             
                if (laps.Length != lapNodes.Where(ln => ln.Visible).Count() ||
                    laps.FirstOrDefault() != lapNodes.First().Lap)
                {
                    RefreshData(laps);
                }
            }
        }
    }

    public class LapNode : Node
    {
        public delegate void OnLapNode(MouseInputEvent mie, LapNode lapNode);
        public event OnLapNode OnRightClick;

        public Lap Lap { get; private set; }

        private const string empty = "";

        public bool IncludeRace { get; set; }
        public bool IncludeLapNumber { get; set; }

        private EventManager em;

        public string Title { get; set; }

        private TextNode textNode;
        private ImageNode imageNode;

        public Color Tint { get { return textNode.Tint; } set { textNode.Tint = value; } }

        public LapNode(EventManager em, Color channelColor) 
        {
            imageNode = new ImageNode(@"img/lapbg.png");
            imageNode.KeepAspectRatio = false;
            imageNode.Tint = channelColor;
            AddChild(imageNode);

            textNode = new TextNode("", Theme.Current.TextMain.XNA);
            textNode.Alignment = RectangleAlignment.Center;
            textNode.RelativeBounds = new RectangleF(0.1f, 0, 0.8f, 0.9f);

            imageNode.AddChild(textNode);

            this.em = em;
            IncludeRace = false;
            IncludeLapNumber = false;
        }

        public void Clear()
        {
            textNode.Text = Title + empty;
            Lap = null;
        }

        public void SetLap(Lap lap)
        {
            if (lap == null)
            {
                Clear();
                Lap = null;
                return;
            }
                
            string newText = Title;
            if (IncludeRace)
            {
                newText += "R" + lap.Race.RaceNumber + " ";
            }
            if (IncludeLapNumber)
            {
                newText += Lap.LapNumberToString(lap.Number) + " ";
            }

            newText += lap.Length.ToStringRaceTime();

            textNode.Text = newText;
            Lap = lap;
        }


        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (Lap != null)
            {
                if (mouseInputEvent.Button == MouseButtons.Right && mouseInputEvent.ButtonState == ButtonStates.Released)
                {
                    OnRightClick?.Invoke(mouseInputEvent, this);
                    return true;
                }

                if (mouseInputEvent.ButtonState == ButtonStates.Released && CompositorLayer.InputEventFactory.AreControlKeysDown())
                {
                    em.RaceManager.DisqualifyLap(Lap);
                    return true;
                }
            }
            
            return base.OnMouseInput(mouseInputEvent);
        }
    }
}
