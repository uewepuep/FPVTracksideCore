using Composition;
using Composition.Input;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using OfficeOpenXml.Style;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Tools;
using UI.Video;
using static UI.Nodes.ChannelNodeBase;

namespace UI.Nodes
{
    public class ChannelsGridNode : GridNode, IUpdateableNode
    {
        public IEnumerable<Pilot> Pilots
        {
            get
            {
                return ChannelNodes.Select(lpn => lpn.Pilot);
            }
        }

        public IEnumerable<Channel> Channels
        {
            get
            {
                return ChannelNodes.Select(lpn => lpn.Channel);
            }
        }

        public IEnumerable<ChannelNodeBase> ChannelNodes
        {
            get
            {
                return Children.OfType<ChannelNodeBase>();
            }
        }

        public IEnumerable<CamGridNode> CamNodes
        {
            get
            {
                return Children.OfType<CamGridNode>();
            }
        }

        public event ChannelNodeBase.ChannelNodeDelegate OnChannelNodeClick;
        public event ChannelNodeBase.ChannelNodeDelegate OnChannelNodeCloseClick;

        private Size withLaps;
        private Size withOutLaps;

        public EventManager EventManager { get; private set; }
        public VideoManager VideoManager { get; private set; }

        public TimeSpan CurrentAnimationTime { get; private set; }

        public AutoCrashOut AutoCrashOut { get; private set; }

        private VideoTimingManager videoTimingManager;

        private List<ChannelVideoInfo> channelInfos;

        private bool manualOverride;

        private GridStatsNode gridStatsNode;

        private object channelCreationLock;

        public bool SingleRow { get; set; }

        public bool Replay { get; private set; }

        public bool ForceReOrder { get; private set; }
        public ReOrderTypes ReOrderType { get; private set; }
        private DateTime reOrderRequest;

        private bool extrasVisible;

        public event Action<Node> OnFullScreen;

        public enum ReOrderTypes
        {
            None, 
            ChannelOrder,
            PositionOrder
        }

        public override IEnumerable<GridTypes> AllowedGridTypes
        {
            get
            {
                if (ApplicationProfileSettings.Instance.ChannelGrid1)
                    yield return GridTypes.One;
                if (ApplicationProfileSettings.Instance.ChannelGrid2)
                    yield return GridTypes.Two;
                if (ApplicationProfileSettings.Instance.ChannelGrid3)
                    yield return GridTypes.Three;
                if (ApplicationProfileSettings.Instance.ChannelGrid4)
                    yield return GridTypes.Four;
                if (ApplicationProfileSettings.Instance.ChannelGrid6)
                    yield return GridTypes.Six;
                if (ApplicationProfileSettings.Instance.ChannelGrid8)
                    yield return GridTypes.Eight;
                if (ApplicationProfileSettings.Instance.ChannelGrid10)
                    yield return GridTypes.Ten;
                if (ApplicationProfileSettings.Instance.ChannelGrid12)
                    yield return GridTypes.Twelve;
                if (ApplicationProfileSettings.Instance.ChannelGrid12)
                    yield return GridTypes.Fifteen;
                if (ApplicationProfileSettings.Instance.ChannelGrid16)
                    yield return GridTypes.Sixteen;

                yield return GridTypes.SingleRow;
            }
        }

        public bool ShowingPilotPhotos
        {
            get
            {
                return ChannelNodes.Any(c => c.ShowingPilotPhoto);
            }
        }

        public ChannelsGridNode(EventManager eventManager, VideoManager videoManager)
        {
            SingleRow = false;

            channelCreationLock = new object();
            channelInfos = new List<ChannelVideoInfo>();

            videoTimingManager = new VideoTimingManager(eventManager.RaceManager.TimingSystemManager, this);

            EventManager = eventManager;
            VideoManager = videoManager;

            ForceReOrder = false;

            withLaps = new Size(400, 300 + 24);
            withOutLaps = new Size(400, 300);
            SingleSize = withLaps;

            Alignment = ApplicationProfileSettings.Instance.AlignChannels;

            RequestLayout();

            EventManager.RaceManager.OnPilotAdded += AddPilotNR;
            EventManager.RaceManager.OnPilotRemoved += RemovePilot;
            EventManager.RaceManager.OnRaceClear += RaceManager_OnRaceClear;
            EventManager.RaceManager.OnRaceStart += RaceManager_OnRaceStart;
            EventManager.RaceManager.OnRaceEnd += RaceManager_OnRaceEnd;
            EventManager.RaceManager.OnRaceChanged += RaceManager_OnRaceChanged;
            EventManager.OnPilotRefresh += Refresh;

            gridStatsNode = new GridStatsNode(EventManager);
            gridStatsNode.Visible = false;
            AddChild(gridStatsNode);
        }

        public override void Dispose()
        {
            EventManager.RaceManager.OnPilotAdded -= AddPilotNR;
            EventManager.RaceManager.OnPilotRemoved -= RemovePilot;
            EventManager.RaceManager.OnRaceClear -= RaceManager_OnRaceClear;
            EventManager.RaceManager.OnRaceStart -= RaceManager_OnRaceStart;
            EventManager.RaceManager.OnRaceEnd -= RaceManager_OnRaceEnd;
            EventManager.RaceManager.OnRaceChanged -= RaceManager_OnRaceChanged;
            EventManager.OnPilotRefresh -= Refresh;

            videoTimingManager?.Dispose();
            AutoCrashOut?.Dispose();
            base.Dispose();
        }


        private void RaceManager_OnRaceChanged(Race race)
        {
            manualOverride = false;
            ClearPilots();
            Reorder();
        }

        private void RaceManager_OnRaceEnd(Race race)
        {
            SetAssignedVisible(true);
            manualOverride = false;

            Reorder();
        }

        private void RaceManager_OnRaceStart(Race race)
        {
            bool isRace = EventManager.RaceManager.EventType != EventTypes.Freestyle;
            manualOverride = false;
            SetLapsVisiblity(isRace);
        }

        private void RaceManager_OnRaceClear(Race race)
        {
            ClearPilots();
        }
       
        public void Refresh()
        {
            manualOverride = false;
            ClearPilots();
            Reorder();

            Race r = EventManager.RaceManager.CurrentRace;
            if (r != null)
            {
                foreach (var pc in r.PilotChannelsSafe)
                {
                    AddPilotNR(pc);
                }
            }
        }

        protected override GridTypes DecideLayout(int count)
        {
            if (SingleRow)
            {
                gridStatsNode.SetAnimatedVisibility(false);
                return GridTypes.SingleRow;
            }

            return base.DecideLayout(count);
        }

        public void Reorder()
        {
            Reorder(false);
        }

        public void Reorder(bool forceReorder)
        {
            if (forceReorder)
            {
                ForceReOrder = forceReorder;
            }
            ForceUpdate = true;
            RequestLayout();
        }

        protected override int VisibleChildCount()
        {
            int count = base.VisibleChildCount();
            if (gridStatsNode.Visible && gridStatsNode.Alpha == 1)
            {
                count--;
            }
            return count;
        }

        public override void UpdateVisibility(IEnumerable<Node> input)
        {
            if (reOrderRequest < DateTime.Now || ForceReOrder)
            {
                int crashed = input.OfType<ChannelNodeBase>().Count(c => c.CrashedOut && c.Pilot != null);
                int all = input.OfType<ChannelNodeBase>().Count(c => c.Pilot != null);
                bool raceFinished = EventManager.RaceManager.RaceFinished;

                foreach (ChannelNodeBase cbn in input.OfType<ChannelNodeBase>())
                {
                    if (cbn.Pilot != null && !Replay)
                    {
                        bool visible = !cbn.CrashedOut || raceFinished;
                        if (all == crashed)
                        {
                            visible = true;
                        }
                        cbn.SetAnimatedVisibility(visible);
                    }
                }

                foreach (CamGridNode camNode in CamNodes)
                {
                    camNode.SetAnimatedVisibility(camNode.VideoBounds.ShowInGrid && extrasVisible);
                }

                CheckGridStatsVisiblilty();
            }

            base.UpdateVisibility(input);
        }

        private void CheckGridStatsVisiblilty()
        {
            int visibleCount = VisibleChildCount();
            GridTypes gridTypeDecided = DecideLayout(visibleCount);
            int gridItemCount = GridTypeItemCount(gridTypeDecided);

            if (gridItemCount > visibleCount && !Replay && !LockGridType && gridTypeDecided != GridTypes.SingleRow)
            {
                gridStatsNode.SetAnimatedVisibility(true);
            }
            else
            {
                gridStatsNode.SetAnimatedVisibility(false);
            }
        }

        public void SetReorderType(ReOrderTypes reOrderType)
        {
            ReOrderType = reOrderType;
        }

        public override IEnumerable<Node> OrderedChildren(IEnumerable<Node> input)
        {
            ReOrderTypes reOrderType = ReOrderType;

            if (!EventManager.RaceManager.RaceType.HasResult())
            {
                reOrderType = ReOrderTypes.ChannelOrder;
            }

            if (ApplicationProfileSettings.Instance.ReOrderDelaySeconds != 0f && !ForceReOrder)
            {
                if (reOrderRequest == DateTime.MaxValue)
                {
                    reOrderRequest = DateTime.Now.AddSeconds(ApplicationProfileSettings.Instance.ReOrderDelaySeconds);
                    reOrderType = ReOrderTypes.None;
                }
                else if (reOrderRequest < DateTime.Now)
                {
                    reOrderRequest = DateTime.MaxValue;
                }
                else
                {
                    reOrderType = ReOrderTypes.None;
                }
            }

            IEnumerable<Node> output;
            switch (reOrderType)
            {
                case ReOrderTypes.None:
                default:
                    output = input;
                    break;
                case ReOrderTypes.ChannelOrder:
                    // Order by channel
                    output = input.OfType<ChannelNodeBase>().OrderBy(p => p.Channel.Frequency);
                    reOrderRequest = DateTime.MaxValue;
                    ForceReOrder = false;
                    break;
                case ReOrderTypes.PositionOrder:
                    // order by position
                    output = input.OfType<ChannelNodeBase>().OrderBy(p => p.Position).ThenBy(p => p.PBTime).ThenBy(p => p.Channel.Frequency);
                    reOrderRequest = DateTime.MaxValue;
                    ForceReOrder = false;
                    break;
            }


            // Add in the grid stats node
            if (gridStatsNode.Visible)
            {
                output = output.Union(new Node[] { gridStatsNode });
            }

            // And the cam nodes
            if (CamNodes.Any(c => c.Visible))
            {
                output = output.Union(CamNodes.Where(c => c.Visible).OrderBy(c => c.FrameNode.Source.VideoConfig.DeviceName));
            }

            return output;
        }

        public void AddVideo(ChannelVideoInfo ci)
        {
            channelInfos.Add(ci);

            // Just create them now if we can. Helps get the video systems up and running.
            ChannelNodeBase cn = GetCreateChannelNode(ci.Channel);
            if (cn != null)
            {
                cn.Visible = false;
                cn.Snap();
            }
        }

        public void ClearVideo()
        {
            AutoCrashOut?.Dispose();

            channelInfos.Clear();

            foreach (ChannelNodeBase n in ChannelNodes.ToArray())
            {
                n.Dispose();
            }

            foreach (CamGridNode n in CamNodes.ToArray())
            {
                n.Dispose();
            }
        }


        public void AddPilotNR(PilotChannel pilotChannel)
        {
            AddPilot(pilotChannel);
        }

        public ChannelNodeBase AddPilot(PilotChannel pilotChannel)
        {
            ChannelNodeBase channelNode = GetCreateChannelNode(pilotChannel.Channel);
            if (channelNode == null)
                return null;

            channelNode.SetPilot(pilotChannel.Pilot);
            channelNode.SetAnimationTime(CurrentAnimationTime);
            channelNode.SetAnimatedVisibility(true);
            channelNode.SetCrashedOutType(CrashOutType.None);

            bool isRace = EventManager.RaceManager.EventType != EventTypes.Freestyle;
            channelNode.SetLapsVisible(isRace);

            // Make them all update their position...
            ChannelNodeBase[] nodes = ChannelNodes.ToArray();
            foreach (ChannelNodeBase n in nodes)
            {
                n.UpdatePosition(null);
            }

            Reorder(true);
            return channelNode;
        }

        public void MakeExtrasVisible(bool visible)
        {
            extrasVisible = visible;
            foreach (CamGridNode camNode in CamNodes)
            {
                camNode.SetAnimatedVisibility(camNode.VideoBounds.ShowInGrid && extrasVisible);
            }

            CheckGridStatsVisiblilty();
        }

        public void SetBiggerInfo(bool biggerChannel, bool biggerResults)
        {
            ChannelNodeBase[] nodes = ChannelNodes.ToArray();
            foreach (ChannelNodeBase n in nodes)
            {
                n.SetBiggerInfo(biggerChannel, biggerResults);
            }
        }

        private ChannelNodeBase GetCreateChannelNode(Channel c)
        {
            lock (channelCreationLock)
            {
                ChannelNodeBase channelNodeBase = ChannelNodes.FirstOrDefault(cia => cia.Channel == c);
                if (channelNodeBase != null)
                {
                    return channelNodeBase;
                }

                channelNodeBase = CreateChannelNode(c);
                AddChild(channelNodeBase);
                return channelNodeBase;
            }
        }

        private ChannelNodeBase CreateChannelNode(Channel c)
        {
            lock (channelCreationLock)
            {
                ChannelNodeBase channelNodeBase = null;

                ChannelVideoInfo ci = channelInfos.FirstOrDefault(cia => cia.Channel == c);
                Color color = EventManager.GetChannelColor(c);

                if (ci != null)
                {
                    ChannelVideoNode channelNode = new ChannelVideoNode(EventManager, c, ci.FrameSource, color);
                    channelNode.Init();
                    channelNode.FrameNode.RelativeSourceBounds = ci.ScaledRelativeSourceBounds;
                    channelNode.FrameNode.SetAspectRatio(withLaps);
                    AutoCrashOut?.AddChannelNode(channelNode);

                    channelNodeBase = channelNode;
                }
                else
                {
                    channelNodeBase = new ChannelNodeBase(EventManager, c, color);
                    channelNodeBase.Init();
                }

                channelNodeBase.RelativeBounds = new RectangleF(0.45f, 0.45f, 0.1f, 0.1f);
                channelNodeBase.OnClick += (mie) =>
                {
                    OnChannelNodeClick?.Invoke(channelNodeBase);
                };

                channelNodeBase.OnCloseClick += () =>
                {
                    OnChannelNodeCloseClick?.Invoke(channelNodeBase);
                    Reorder();
                };

                channelNodeBase.OnCrashedOutClick += () =>
                {
                    Reorder();
                };

                channelNodeBase.OnFullscreen += () =>
                {
                    FullScreen(channelNodeBase);
                };

                channelNodeBase.OnShowAll += () =>
                {
                    IncreaseChannelVisiblity();
                };

                channelNodeBase.OnPBChange += Reorder;
                channelNodeBase.RequestReorder += Reorder;

                channelNodeBase.RelativeBounds = new RectangleF(0.45f, 0.45f, 0.1f, 0.1f);
                channelNodeBase.Layout(BoundsF);
                channelNodeBase.Snap();

                return channelNodeBase;
            }
        }

        public void RemovePilot(PilotChannel pc)
        {
            RemovePilot(pc.Pilot);
        }

        public void RemovePilot(Pilot p)
        {
            ChannelNodeBase channelNode = ChannelNodes.FirstOrDefault(lpn => lpn.Pilot == p);
            if (channelNode != null)
            {
                channelNode.SetPilot(null);
                channelNode.SetAnimatedVisibility(false);

                Reorder(true);
            }
        }

        public void SetLapsVisiblity(bool visible)
        {
            if (visible)
            {
                SingleSize = withLaps;
            }
            else
            {
                SingleSize = withOutLaps;
            }

            foreach (ChannelNodeBase channelNode in ChannelNodes)
            {
                channelNode.SetLapsVisible(visible);
            }

            RequestLayout();
        }

        public void SetProfileVisible(PilotProfileOptions options)
        {
            foreach (ChannelNodeBase channelNode in ChannelNodes)
            {
                channelNode.SetProfileVisible(options);
            }
        }

        public ChannelNodeBase GetChannelNode(Pilot p)
        {
            return ChannelNodes.FirstOrDefault(cn => cn.Pilot == p);
        }

        public void ClearPilots()
        {
            foreach (ChannelNodeBase cn in ChannelNodes)
            {
                cn.LapsNode.ClearLaps();
                cn.SetPilot(null);
                cn.SetAnimatedVisibility(false);
            }
            Reorder(true);
        }

        public void SetPilotVisible(Pilot p, bool visible)
        {
            if (EventManager.RaceManager.RaceRunning)
                manualOverride = true;

            ChannelNodeBase cn = GetChannelNode(p);
            if (cn != null)
            {
                cn.SetAnimatedVisibility(visible);
            }
        }

        public void TogglePilotVisible(Pilot p)
        {
            if (EventManager.RaceManager.RaceRunning)
                manualOverride = true;

            ChannelNodeBase cn = GetChannelNode(p);
            if (cn != null)
            {
                cn.SetAnimatedVisibility(!cn.Visible);
            }
        }

        public void SetAssignedVisible(bool visible)
        {
            foreach (ChannelNodeBase cn in ChannelNodes)
            {
                if (cn.Pilot != null)
                {
                    cn.SetAnimatedVisibility(visible);
                }
            }
        }

        public void SetUnassignedVisible(bool visible)
        {
            foreach (ChannelNodeBase cn in ChannelNodes)
            {
                if (cn.Pilot == null)
                {
                    cn.SetAnimatedVisibility(visible);
                }
            }
        }

        private void FullScreen(ChannelNodeBase fullScreen)
        {
            if (EventManager.RaceManager.RaceRunning || Replay)
            {
                IEnumerable<ChannelNodeBase> pilotNodes = ChannelNodes.Where(cn => cn.Pilot != null && cn != fullScreen);
                if (pilotNodes.Any())
                {
                    foreach (ChannelNodeBase cn in pilotNodes)
                    {
                        cn.SetAnimatedVisibility(false);
                        cn.SetCrashedOutType(CrashOutType.FullScreen);
                    }
                }

                fullScreen.SetAnimatedVisibility(true);
                fullScreen.SetCrashedOutType(CrashOutType.None);

                RequestLayout();
            }
            else
            {
                OnFullScreen?.Invoke(fullScreen);
            }
        }

        public void FullScreen(CamGridNode fullScreen)
        {
            if (EventManager.RaceManager.RaceRunning || Replay)
            {
                foreach (ChannelNodeBase cn in ChannelNodes)
                {
                    cn.SetAnimatedVisibility(false);
                }

                fullScreen.SetAnimatedVisibility(true);
                RequestLayout();
            }
            else
            {
                OnFullScreen?.Invoke(fullScreen);
            }
        }


        public void IncreaseChannelVisiblity()
        {
            ForceReOrder = true;

            if (EventManager.RaceManager.RaceRunning)
                manualOverride = true;

            IEnumerable<ChannelNodeBase> pilotNodes = ChannelNodes.Where(cn => cn.Pilot != null);
            if (pilotNodes.Any())
            {
                foreach (ChannelNodeBase cn in pilotNodes)
                {
                    cn.SetAnimatedVisibility(true);
                    cn.SetCrashedOutType(CrashOutType.None);
                }

                RequestLayout();
            }
            else
            {
                AllVisible(true);
            }
        }

        public void DecreaseChannelVisiblity()
        {
            ForceReOrder = true;

            if (manualOverride)
            {
                manualOverride = false;
                return;
            }

            IEnumerable<ChannelNodeBase> emptyNodes = ChannelNodes.Where(cn => cn.Visible && cn.Pilot == null);
            if (emptyNodes.Any())
            {
                foreach (ChannelNodeBase cn in emptyNodes)
                {
                    cn.SetAnimatedVisibility(false);
                }

                RequestLayout();
            }
            else
            {
                AllVisible(false);
            }
        }

        public void AutomaticSetCrashed(ChannelNodeBase cn, bool crashed)
        {
            if (!manualOverride)
            {
                if (cn != null)
                {
                    if (!cn.Finished && cn.CrashedOutType != ChannelNodeBase.CrashOutType.Manual && cn.CrashedOutType != ChannelNodeBase.CrashOutType.FullScreen)
                    {
                        cn.SetCrashedOutType(crashed ? ChannelNodeBase.CrashOutType.Auto : ChannelNodeBase.CrashOutType.None);
                    }
                }
                Reorder();
            }
        }

        public void FillChannelNodes()
        {
            ClearVideo();
            AutoCrashOut = new AutoCrashOut(EventManager, this);
       
            Channel[] channels = EventManager.Channels;

            try
            {
                foreach (ChannelVideoInfo channelVideoInfo in VideoManager.CreateChannelVideoInfos())
                {
                    if (channels.Contains(channelVideoInfo.Channel))
                    {
                        AddVideo(channelVideoInfo);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.UI.LogException(this, e);
            }

            foreach (VideoConfig vs in VideoManager.VideoConfigs)
            {
                foreach (VideoBounds videoBounds in vs.VideoBounds.Where(vb => vb.SourceType != SourceTypes.FPVFeed && vb.ShowInGrid))
                {
                    try
                    {
                        FrameSource source = VideoManager.GetFrameSource(vs);
                        if (source != null)
                        {
                            CamGridNode camNode = new CamGridNode(source, videoBounds);
                            camNode.OnCloseClick += () =>
                            {
                                MakeExtrasVisible(false);
                            };

                            camNode.OnFullscreen += () =>
                            {
                                FullScreen(camNode);
                            };

                            camNode.OnShowAll += () =>
                            {
                                IncreaseChannelVisiblity();
                            };

                            AddChild(camNode);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.UI.LogException(this, e);
                    }
                }
            }
        }

        public void SetAnimationTime(TimeSpan timeSpan)
        {
            CurrentAnimationTime = timeSpan;

            foreach (AnimatedNode cn in Children.OfType<AnimatedNode>())
            {
                cn.SetAnimationTime(timeSpan);
            }

            foreach (AnimatedRelativeNode cn in Children.OfType<AnimatedRelativeNode>())
            {
                cn.SetAnimationTime(timeSpan);
            }

            if (gridStatsNode != null)
            {
                gridStatsNode.SetAnimationTime(timeSpan);
            }
        }

        public void Update(GameTime gameTime)
        {
            if (reOrderRequest != DateTime.MinValue && DateTime.Now > reOrderRequest)
            {
                Reorder(true);
            }
        }

        public override bool OnDrop(MouseInputEvent finalInputEvent, Node node)
        {
            if (!base.OnDrop(finalInputEvent, node))
            {
                IPilot pl = node as IPilot;
                if (pl != null)
                {
                    if (EventManager.RaceManager.AddPilot(pl.Pilot))
                    {
                        return true;
                    }
                }
                return false;
            }

            return true;
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            Node[] t = Children;

            // Draw the crashed out channel nodes and the non-channel nodes.
            for (int i = t.Length - 1; i >= 0; i--)
            {
                if (t[i].Drawable)
                {
                    if (t[i] is ChannelNodeBase)
                    {
                        ChannelNodeBase c = (ChannelNodeBase)t[i];
                        if (c.CrashedOut)
                        {
                            c.Draw(id, parentAlpha * Alpha);
                        }
                    }
                    else
                    {
                        t[i].Draw(id, parentAlpha * Alpha);
                    }
                }
            }

            // Draw the active channel nodes
            for (int i = t.Length - 1; i >= 0; i--)
            {
                if (t[i].Drawable)
                {
                    if (t[i] is ChannelNodeBase)
                    {
                        ChannelNodeBase c = (ChannelNodeBase)t[i];
                        if (!c.CrashedOut)
                        {
                            c.Draw(id, parentAlpha * Alpha);
                        }
                    }
                }
            }

            NeedsDraw = false;
        }

        public void SetPlaybackTime(DateTime time)
        {
            Replay = true;
            foreach (ChannelNodeBase nodeBase in ChannelNodes)
            {
                nodeBase.SetPlaybackTime(time);
            }
        }

        public void ToggleCrashedOut(IEnumerable<Channel> channels)
        {
            foreach (ChannelNodeBase channelNode in ChannelNodes)
            {
                if (channels.Contains(channelNode.Channel))
                {
                    if (channelNode.Visible)
                    {
                        channelNode.Close();
                    }
                    else
                    {
                        channelNode.SetCrashedOutType(CrashOutType.None);
                    }
                }
            }
            Reorder(true);
            RequestLayout();
        }

        public void ReloadPilotProfileImages()
        {
            foreach (ChannelNodeBase channelNode in ChannelNodes)
            {
                channelNode.PilotProfile.Reload();
            }
        }
    }
}
