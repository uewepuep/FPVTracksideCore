using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using ExternalData;
using Microsoft.Xna.Framework;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Engineering;
using RaceLib;
using RaceLib.Format;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes.Rounds
{
    public class RoundsNode : Node
    {
        public IEnumerable<Race> Races { get { return EventManager.RaceManager.Races; } }

        public IEnumerable<EventRoundNode> RoundNodes { get { return Children.OfType<EventRoundNode>(); } }
        public IEnumerable<EventXNode> EventXNodes { get { return Children.OfType<EventXNode>(); } }
        public IEnumerable<EventPointsNode> EventPointsNodes { get { return Children.OfType<EventPointsNode>(); } }
        public IEnumerable<EventLapsTimesNode> EventTimesNodes { get { return Children.OfType<EventLapsTimesNode>(); } }
        public IEnumerable<EventLapCountsNode> EventLapCountsNodes { get { return Children.OfType<EventLapCountsNode>(); } }
        public IEnumerable<EventPackCountNode> EventPackCountNodes { get { return Children.OfType<EventPackCountNode>(); } }
        public IEnumerable<EventStageNode> EventStageNodes { get { return Children.OfType<EventStageNode>(); } }

        public EventManager EventManager { get; private set; }
        public RoundManager RoundManager { get { return EventManager.RoundManager; } }
        public ISync Syncer { get; private set; }

        private bool needsRefresh;

        public ScrollerNode Scroller { get; private set; }

        private TimeSpan scrollTime;

        private RoundControl roundControl;

        public int RacesPerColumn { get; private set; }

        public RoundsNode(EventManager eventManager)
        {
            roundControl = new RoundControl();
            roundControl.RelativeBounds = new RectangleF(0, 0.96f, 1, 0.03f);
            AddChild(roundControl);

            roundControl.Next += RoundControl_Next;
            roundControl.Prev += RoundControl_Prev;

            scrollTime = TimeSpan.FromSeconds(2);

            Scroller = new ScrollerNode(this, ScrollerNode.Types.Horizontal, Theme.Current.ScrollBar.XNA);
            Scroller.OnSelfLayout += RequestLayout;

            EventManager = eventManager;
            ChannelsChanged();

            RoundManager.OnRoundAdded += Em_OnRoundAdded;
            RoundManager.OnRoundRemoved += Refresh;
            RoundManager.OnStageChanged += Refresh;
            EventManager.RaceManager.OnPilotAdded += RaceManager_OnPilotAdded;
            EventManager.RaceManager.OnPilotRemoved += OnPilotRemoved;
            EventManager.RaceManager.OnRaceEnd += RaceManager_OnRaceEnd; ;
            EventManager.RaceManager.OnRaceClear += UpdateRace;
            EventManager.RaceManager.OnRaceChanged += UpdateRace;
            EventManager.RaceManager.OnRaceReset += UpdateRace;
            EventManager.RaceManager.OnLapsRecalculated += UpdateRace;
            EventManager.RaceManager.OnRaceRemoved += Refresh;
            EventManager.RaceManager.OnRaceCreated += Refresh;
            EventManager.ResultManager.RaceResultsChanged += Refresh;
            EventManager.OnPilotRefresh += Refresh;
        }



        public override void Dispose()
        {
            RoundManager.OnRoundAdded -= Em_OnRoundAdded;
            RoundManager.OnRoundRemoved -= Refresh;
            RoundManager.OnStageChanged -= Refresh;
            EventManager.RaceManager.OnPilotAdded -= RaceManager_OnPilotAdded;
            EventManager.RaceManager.OnPilotRemoved -= OnPilotRemoved;
            EventManager.RaceManager.OnRaceEnd -= RaceManager_OnRaceEnd;
            EventManager.RaceManager.OnRaceClear -= UpdateRace;
            EventManager.RaceManager.OnRaceChanged -= UpdateRace;
            EventManager.RaceManager.OnRaceReset -= UpdateRace;
            EventManager.RaceManager.OnLapsRecalculated -= UpdateRace;
            EventManager.RaceManager.OnRaceRemoved -= Refresh;
            EventManager.RaceManager.OnRaceCreated -= Refresh;
            EventManager.OnPilotRefresh -= Refresh;

            if (Syncer != null)
            {
                Syncer.RaceSyncEvent -= Refresh;
            }

            Scroller.Dispose();
            base.Dispose();
        }

        public void SetSyncer(ISync syncer)
        {
            Syncer = syncer;
            if (syncer != null)
            {
                syncer.RaceSyncEvent += Syncer_RaceSyncEvent;
            }
        }

        private void Syncer_RaceSyncEvent(SyncType arg1, bool arg2)
        {
            Refresh();
        }

        public void ChannelsChanged()
        {
            if (EventManager.Channels.GetChannelGroups().Count() > 8)
            {
                RacesPerColumn = 2;
            }
            else
            {
                RacesPerColumn = 3;
            }
            Refresh();
        }

        private void RoundControl_Prev()
        {
            int scroll = Bounds.Width / 2;
            Scroller.ScrollAdd(-scroll);
        }

        private void RoundControl_Next()
        {
            int scroll = Bounds.Width / 2;
            Scroller.ScrollAdd(scroll);
        }


        private void RaceManager_OnRaceEnd(Race race)
        {
            UpdateRace(race);
            Refresh();
        }

        private void Em_OnRoundAdded()
        {
            Refresh();
            Scroller.ScrollToEnd(scrollTime);
        }

        private void RaceManager_OnPilotAdded(PilotChannel pc)
        {
            if (EventManager.RaceManager.CurrentRace != null)
            {
                UpdateRace(EventManager.RaceManager.CurrentRace);
            }
        }


        private void OnPilotRemoved(PilotChannel pc)
        {
            if (EventManager.RaceManager.CurrentRace == null)
            {
                Refresh();
            }
        }

        private void UpdateRace(Race race)
        {
            EventRaceNode ern = RoundNodes.SelectMany(s => s.RaceNodes).FirstOrDefault(r => r.Race == race);
            if (ern != null)
            {
                ern.NeedsInit = true;
            }
        }

        private void Refresh(SyncType st, bool o)
        {
            Refresh();
        }

        private void Refresh(object o)
        {
            Refresh();
        }

        public void Refresh()
        {
            needsRefresh = true;
            RequestLayout();
        }

        private void HookUp(EventXNode eventXNode)
        {
            eventXNode.AddRound += GenerateRoundKeepChannels;
            eventXNode.CustomRound += GenerateCustomRound;
            eventXNode.ChangeChannels += GenerateChangeChannels;
            eventXNode.Finals += GenerateFinal;
            eventXNode.DoubleElim += GenerateDoubleElim;
            eventXNode.SumPoints += ToggleSumPoints;
            eventXNode.Times += ToggleTimePoints;
            eventXNode.LapCounts += ToggleLapCount;
            eventXNode.PackCount += TogglePackCount;
            eventXNode.Clone += CloneRound;
            eventXNode.AddEmptyRound += AddEmptyRound;
            eventXNode.NeedsFormatLayout += RequestLayout;
        }

        private void DoRefresh()
        {
            needsRefresh = false;

            Round[] rounds = EventManager.RoundManager.Rounds.OrderBy(e => e.Order).ToArray();
            foreach (Round round in rounds)
            {
                IEnumerable<Race> roundRaces = EventManager.RaceManager.GetRaces(round).OrderBy(r => r.RaceNumber);
                EventRoundNode ern = RoundNodes.FirstOrDefault(d => d.Round == round);
                if (ern == null)
                {
                    ern = new EventRoundNode(this, round);

                    ern.PastePilot += Paste;
                    ern.MatchChannels += MatchChannels;
                    ern.SetRaceTypes += SetRoundType;
                    ern.NeedFullRefresh += Refresh;
                    ern.RemoveRound += RoundManager.RemoveRound;
                    ern.FillRound += FillRound;
                    HookUp(ern);
                    AddChild(ern);
                }
                ern.SetRaces(roundRaces);

                Stage stage = round.Stage;
                if (stage != null)
                {
                    Round lastOrDefault = EventManager.RoundManager.GetLastStageRound(stage);
                    if (lastOrDefault != round)
                        continue;

                    if (stage.PointSummary != null)
                    {
                        EventPointsNode esn = EventPointsNodes.FirstOrDefault(d => d.Round == round);
                        if (esn == null && ern != null)
                        {
                            esn = new EventPointsNode(this, EventManager, round);
                            esn.RemoveRound += ToggleSumPoints;
                            HookUp(esn);
                            AddChild(esn);
                        }
                        else
                        {
                            esn.Refresh();
                            RequestLayout();
                        }
                    }

                    if (stage.TimeSummary != null)
                    {
                        EventLapsTimesNode esn = EventTimesNodes.FirstOrDefault(d => d.Round == round);
                        if (esn == null && ern != null)
                        {
                            esn = new EventLapsTimesNode(this, EventManager, round);
                            esn.RemoveRound += ToggleTimePoints;
                            HookUp(esn);
                            AddChild(esn);
                        }
                        else
                        {
                            esn.Refresh();
                            RequestLayout();
                        }
                    }

                    if (stage.PackCountAfterRound)
                    {
                        EventPackCountNode esn = EventPackCountNodes.FirstOrDefault(d => d.Round == round);
                        if (esn == null && ern != null)
                        {
                            esn = new EventPackCountNode(this, EventManager, round);
                            esn.RemoveRound += TogglePackCount;
                            HookUp(esn);
                            AddChild(esn);
                        }
                        else
                        {
                            esn.Refresh();
                            RequestLayout();
                        }
                    }

                    if (stage.LapCountAfterRound)
                    {
                        EventLapCountsNode esn = EventLapCountsNodes.FirstOrDefault(d => d.Round == round);
                        if (esn == null && ern != null)
                        {
                            esn = new EventLapCountsNode(this, EventManager, round);
                            esn.RemoveRound += ToggleLapCount;
                            HookUp(esn);
                            AddChild(esn);
                        }
                        else
                        {
                            esn.Refresh();
                            RequestLayout();
                        }
                    }

                }
            }

            foreach (var ern in RoundNodes.ToArray())
            {
                if (!rounds.Contains(ern.Round))
                {
                    ern.Dispose();
                }
            }

            foreach (EventStageNode eventStageNode in EventStageNodes.ToArray())
            {
                if (!eventStageNode.HasResult())
                {
                    eventStageNode.Dispose();
                }
            }

            SetOrder<EventXNode, long>((a) =>
            {
                return a.Order;
            });
        }

        private void AddEmptyRound(Round callingRound)
        {
            RoundManager.CreateEmptyRound(callingRound.EventType);
            Refresh();
        }

        private void SetRoundType(EventTypes type, Round round)
        {
            RoundManager.SetRoundType(type, round);
            Refresh();
        }

        private void CloneRound(Round callingRound)
        {
            RoundManager.CloneRound(callingRound);
            Refresh();
            Scroller.ScrollToEnd(scrollTime);
        }

        private void ToggleSumPoints(Round callingRound)
        {
            EventManager.RoundManager.ToggleSumPoints(callingRound);
            Refresh();

            Scroller.ScrollToEnd(scrollTime);
        }
        private void ToggleTimePoints(Round callingRound)
        {
            ToggleTimePoints(callingRound, TimeSummary.TimeSummaryTypes.PB);
        }

        private void ToggleTimePoints(Round callingRound, TimeSummary.TimeSummaryTypes type)
        {
            EventManager.RoundManager.ToggleTimePoints(callingRound, type);
            Refresh();

            Scroller.ScrollToEnd(scrollTime);
        }

        public void TogglePackCount(Round callingRound)
        {
            EventManager.RoundManager.TogglePackCount(callingRound);
            Refresh();
        }

        private void ToggleLapCount(Round callingRound)
        {
            EventManager.RoundManager.ToggleLapCount(callingRound);
            Refresh();

            Scroller.ScrollToEnd(scrollTime);
        }

        private void GenerateDoubleElim(Round callingRound)
        {
            RoundManager.GenerateDoubleElimination(callingRound);
            Refresh();
        }

        private void GenerateChaseTheAce(Round callingRound)
        {
            RoundManager.GenerateChaseTheAce(callingRound);
            Refresh();
        }

        private void GenerateCustomRound(Round callingRound)
        {
            CustomRoundEditor editor = new CustomRoundEditor(EventManager, callingRound);
            GetLayer<PopupLayer>().Popup(editor);

            editor.OnOK += (e) =>
            {
                RoundManager.GenerateRound(editor.Selected);
            };
        }

        private void GenerateRoundKeepChannels(Round callingRound)
        {
            GenerateRound(callingRound, RoundPlan.ChannelChangeEnum.KeepFromPreviousRound);
        }

        private void GenerateChangeChannels(Round callingRound)
        {
            GenerateRound(callingRound, RoundPlan.ChannelChangeEnum.Change);
        }

        private void GenerateRound(Round callingRound, RoundPlan.ChannelChangeEnum changeChannel)
        {
            RoundPlan roundPlan = new RoundPlan(EventManager, callingRound);
            roundPlan.ChannelChange = changeChannel;

            RoundManager.GenerateRound(roundPlan);
            Refresh();
        }

        private void GenerateFinal(Round callingRound)
        {
            RoundManager.GenerateFinal(callingRound);
            Refresh();
        }

        private void MatchChannels(IEnumerable<Race> races)
        {
            foreach (Race race in races)
            {
                EventManager.SetPilotChannels(race);
            }
        }

        private void Paste(Round round)
        {
            var lines = PlatformTools.Clipboard.GetLines();
            IEnumerable<Tuple<Pilot, Channel>> pilotChannels = EventManager.GetPilotsFromLines(lines, true);
            if (pilotChannels.Any())
            {
                RoundManager.SetRoundPilots(round, pilotChannels);
            }
            Refresh();
        }

        private void FillRound(Round round)
        {
            if (!RoundManager.DoesRoundHaveAllPilots(round))
            {
                AutoFormat format = new AutoFormat(EventManager);
                RoundManager.Generate(format, round, new RoundPlan(EventManager, null));
            }
        }

        public override void Layout(RectangleF parentBounds)
        {
            BoundsF = CalculateRelativeBounds(parentBounds);

            if (needsRefresh)
            {
                DoRefresh();
            }
            
            if (parentBounds.Width == 0)
                return;

            int paddingX = 50;

            float height = (int)(BoundsF.Height * 0.95f);
            float y = (int)(BoundsF.Height * 0.01f) + BoundsF.Y;
            float x = paddingX + BoundsF.X;

            foreach (EventXNode ern in EventXNodes)
            {
                ern.Alignment = RectangleAlignment.CenterLeft;
                ern.RelativeBounds = new RectangleF(0, 0, 1, 1);
                ern.CalculateAspectRatio(height);

                int width = (int)(height * ern.AspectRatio);
                RectangleF bounds = new RectangleF(x - (int)Scroller.CurrentScrollPixels, y, width, height);
                ern.SetBounds(bounds);
                x += width;
                x += paddingX;
            }

            Scroller.ViewSizePixels = Bounds.Width;

            Scroller.ContentSizePixels = 100;
            if (EventXNodes.Any())
            {
                Scroller.ContentSizePixels += EventXNodes.Select(e => e.Bounds.Right).Max() - EventXNodes.Select(e => e.Bounds.X).Min();
            }

            Scroller.Layout(BoundsF);

            base.Layout(parentBounds);
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            Scroller.Draw(id, parentAlpha);

            id.PushClipRectangle(Bounds);
            base.Draw(id, parentAlpha);
            id.PopClipRectangle();
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            Scroller.OnMouseInput(mouseInputEvent);

            return base.OnMouseInput(mouseInputEvent);
        }

        public override Rectangle? CanDrop(MouseInputEvent finalInputEvent, Node node)
        {
            EventRoundNode dropped = node as EventRoundNode;
            if (dropped == null)
                return base.CanDrop(finalInputEvent, node);


            Node found = FindDragTarget(finalInputEvent.Position.X);
            Point location;
            if (found == null)
            {
                found = RoundNodes.OrderBy(r => r.Bounds.X).LastOrDefault();
                if (found != null)
                {
                    location = found.Bounds.Location;
                    location.Y += found.Bounds.Height + 10;
                }
                else
                {
                    location = Bounds.Location;
                }
            }
            else
            {
                location = found.Bounds.Location;
            }

            Rectangle output = new Rectangle(location, dropped.Bounds.Size);
            output.Width = 2;
            output.X -= 5;

            return output;
        }

        public override bool OnDrop(MouseInputEvent finalInputEvent, Node node)
        {
            IEnumerable<StageNode> stageNodes = EventStageNodes.Select(e => e.StageNode).Distinct();
            foreach (StageNode stageNode in stageNodes)
            {
                if (stageNode.OnDrop(finalInputEvent, node))
                {
                    return true;
                }
            }

            EventRoundNode dropped = node as EventRoundNode;
            if (dropped != null)
            {
                OrderByDrop(dropped, finalInputEvent.Position.X);
                RoundManager.SetStage(dropped.Round, null);

                using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                {
                    db.Update(dropped.Round);
                }
            }
            return base.OnDrop(finalInputEvent, node);
        }

        public Node FindDragTarget(float positionX)
        {
            foreach (EventRoundNode ern in RoundNodes.OrderBy(rn => rn.Bounds.X))
            {
                if (ern.Bounds.Center.X > positionX)
                {
                    return ern;
                }
            }
            return null;
        }

        public void OrderByDrop(EventRoundNode dropped, float positionX)
        {
            Node found = FindDragTarget(positionX);

            const int inc = 100;
            int order = inc;
            foreach (EventRoundNode ern in RoundNodes.OrderBy(rn => rn.Round.Order))
            {
                if (ern == dropped)
                    continue;

                if (ern == found)
                {
                    dropped.Round.Order = order;
                    order += inc;
                }

                ern.Round.Order = order;
                order += inc;
            }

            if (found == null)
            {
                dropped.Round.Order = order;
            }

            Round[] rounds = RoundNodes.Select(r => r.Round).ToArray();
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                db.Update(rounds);
            }

            Refresh();
        }

        public void ScrollToRace(Race race)
        {
            foreach (EventRoundNode ern in RoundNodes)
            {
                if (ern.Races.Contains(race))
                {
                    ScrollToRound(ern);
                }
            }
        }

        public void ScrollToRound(EventRoundNode round)
        {
            int minX = RoundNodes.Select(e => e.Bounds.X).Min();

            Scroller.ScrollTo(round.Bounds.X - minX);
        }
    }

    public class RoundControl : AspectNode
    {
        private ImageButtonNode prev;
        private ImageButtonNode next;


        public event Action Prev;
        public event Action Next;

        public RoundControl()
        {
            SetAspectRatio(10, 1);

            prev = new ImageButtonNode(@"img\start.png", Color.Transparent, Theme.Current.Hover.XNA);
            prev.ImageNode.RelativeSourceBounds = new RectangleF(1, 0, -1, 1);
            prev.RelativeBounds = new RectangleF(0, 0, 0.1f, 1);
            prev.OnClick += Prev_OnClick;

            next = new ImageButtonNode(@"img\start.png", Color.Transparent, Theme.Current.Hover.XNA);
            next.RelativeBounds = new RectangleF(1 - prev.RelativeBounds.Width, 0, prev.RelativeBounds.Width, 1);
            next.OnClick += Next_OnClick;

            AddChild(prev);
            AddChild(next);
        }

        private void Next_OnClick(MouseInputEvent mie)
        {
            Next?.Invoke();
        }

        private void Prev_OnClick(MouseInputEvent mie)
        {
            Prev?.Invoke();
        }
    }
}
