using Composition;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using Composition.Input;
using UI.Video;

namespace UI.Nodes
{
    public class SceneManagerNode : Node
    {
        private AnimatedNode launchCamsNode;
        private AnimatedNode commentatorsAndSummary;
        private AnimatedNode finishLineNode;

        private VideoManager videoManager;
        private EventManager eventManager;

        public ChannelsGridNode ChannelsGridNode { get; private set; }
        private TopBarNode topBarNode;

        private NamedRaceNode resultsRaceNode;
        private NamedRaceNode nextRaceNode;

        private WormNode wormNode;

        private AnimatedNode eventStatusNodeContainer;

        private Scenes preFullScreenScene;
        private FullScreenAspectClosable fullScreenContainer;
        private Node fullscreenNode;
        private Node fullscreenNodeParent;

        public enum Scenes
        {
            Clear,
            PreRace,
            Race,
            FinishLine,
            RaceResults,

            EventStatus,
            Fullscreen,
        }

        public Scenes Scene { get; private set; }

        public event Action<Scenes> OnSceneChange;
        public event Action OnVideoSettingsChange;

        public TimeSpan AfterRaceStart { get; private set; }

        public bool PreRaceScene { get { return ProfileSettings.Instance.PreRaceScene; } }
        public bool PostRaceScene { get { return ProfileSettings.Instance.PostRaceScene; } }

        public TimeSpan SetupAnimationTime { get { return TimeSpan.FromSeconds(0.5f); } }
        public TimeSpan MidRaceAnimationTime { get { return TimeSpan.FromSeconds(ProfileSettings.Instance.ReOrderAnimationSeconds); } }

        private AutoRunnerTimerNode autoRunnerTimerNode;

        public SceneManagerNode(EventManager eventManager, VideoManager videoManager, ChannelsGridNode channelsGridNode, TopBarNode topBarNode, AutoRunner autoRunner)
        {
            AfterRaceStart = TimeSpan.FromSeconds(2);

            this.eventManager = eventManager;
            this.videoManager = videoManager;
            ChannelsGridNode = channelsGridNode;
            this.topBarNode = topBarNode;

            channelsGridNode.OnFullScreen += OnChannelsGridNodeFullScreen;

            eventStatusNodeContainer = new AnimatedNode();
            eventStatusNodeContainer.Visible = false;
            eventStatusNodeContainer.RelativeBounds = new RectangleF(0, 0.05f, 1, 0.9f);
            AddChild(eventStatusNodeContainer);

            EventStatusNode eventStatusNode = new EventStatusNode(eventManager);
            eventStatusNodeContainer.AddChild(eventStatusNode);

            launchCamsNode = new AnimatedNode();
            launchCamsNode.SetAnimatedVisibility(false);

            finishLineNode = new AnimatedNode();
            finishLineNode.SetAnimatedVisibility(false);

            commentatorsAndSummary = new AnimatedNode();
            commentatorsAndSummary.SetAnimatedVisibility(false);

            resultsRaceNode = new NamedRaceNode("Results", eventManager);

            nextRaceNode = new NamedRaceNode("Next Race", eventManager);

            resultsRaceNode.RelativeBounds = new RectangleF(0, 0, 0.01f, 0.01f);
            nextRaceNode.RelativeBounds = new RectangleF(0, 0, 0.01f, 0.01f);

            wormNode = new WormNode(eventManager);
            wormNode.RelativeBounds = new RectangleF(0.0f, 1f, 1, 0.0f);


            AddChild(resultsRaceNode);
            AddChild(nextRaceNode);
            AddChild(wormNode);

            AddChild(launchCamsNode);
            AddChild(commentatorsAndSummary);
            AddChild(channelsGridNode);
            AddChild(finishLineNode);

            eventManager.RaceManager.OnRaceStart += RaceManager_OnRaceStart;
            eventManager.RaceManager.OnRaceEnd += RaceManager_OnRaceEnd;
            eventManager.RaceManager.OnRaceClear += RaceManager_OnRaceClearReset;
            eventManager.RaceManager.OnRaceReset += RaceManager_OnRaceClearReset;
            eventManager.RaceManager.OnRaceChanged += SetRace;
            eventManager.RaceManager.OnLapsRecalculated += OnLapsRecalculated;
            eventManager.RaceManager.OnRacePreStart += RaceManager_OnRacePreStart;
            eventManager.RoundManager.OnRoundAdded += UpdateNextRaceNode;
            eventManager.OnPilotRefresh += EventManager_OnPilotRefresh;

            SetScene(Scenes.Clear);

            autoRunner.SetSceneManager(this);

            autoRunnerTimerNode = new AutoRunnerTimerNode(autoRunner);
            autoRunnerTimerNode.RelativeBounds = new RectangleF(0, 0.96f, 0.97f, 0.04f);
            autoRunnerTimerNode.Alignment = RectangleAlignment.BottomRight;
            AddChild(autoRunnerTimerNode);

            fullScreenContainer = new FullScreenAspectClosable();
            fullScreenContainer.Close += UnFullScreen;
            AddChild(fullScreenContainer);
        }

        public void SyncFinished()
        {
            if (Scene != Scenes.Race)
            {
                UpdateNextRaceNode();
            }
        }

        private void EventManager_OnPilotRefresh()
        {
            UpdateNextRaceNode();
            resultsRaceNode.SetRace(eventManager.RaceManager.CurrentRace);
        }

        private void RaceManager_OnRacePreStart(Race race)
        {
            ChannelsGridNode.SetProfileVisible(false, false);
        }

        public void UpdateNextRaceNode()
        {
            Race nextRace = eventManager.RaceManager.GetNextRace(true);
            if (nextRace == null)
            {
                nextRaceNode.SetRace(null);
                if (eventManager.RaceManager.CurrentRace != null)
                {
                    nextRaceNode.ShowNextRoundOptions(eventManager.RaceManager.CurrentRace.Round);
                }
            }
            else
            {
                nextRaceNode.SetRace(nextRace);
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            eventManager.RaceManager.OnRaceStart -= RaceManager_OnRaceStart;
            eventManager.RaceManager.OnRaceEnd -= RaceManager_OnRaceEnd;
            eventManager.RaceManager.OnRaceClear -= RaceManager_OnRaceClearReset;
            eventManager.RaceManager.OnRaceReset -= RaceManager_OnRaceClearReset;
            eventManager.RaceManager.OnRaceChanged -= SetRace;
            eventManager.RaceManager.OnLapsRecalculated -= OnLapsRecalculated;
            eventManager.RoundManager.OnRoundAdded -= UpdateNextRaceNode;
            eventManager.RaceManager.OnRacePreStart -= RaceManager_OnRacePreStart;
            eventManager.OnPilotRefresh -= EventManager_OnPilotRefresh;
        }

        public override bool OnDrop(MouseInputEvent finalInputEvent, Node node)
        {
            if (!base.OnDrop(finalInputEvent, node))
            {
                IPilot pilotNode = node as IPilot;
                if (pilotNode != null)
                {
                    eventManager.RaceManager.AddPilot(pilotNode.Pilot);
                    return true;
                }
                return false;
            }
            return true;
        }

        private void RaceManager_OnRaceStart(Race race)
        {
            eventManager.TimedActionManager.Enqueue(DateTime.Now + AfterRaceStart,
                () =>
                {
                    if (race.Running)
                    {
                        SetScene(Scenes.Race);
                    }
                });
        }

        private void RaceManager_OnRaceClearReset(Race race)
        {
            SetScene(Scenes.PreRace);
        }

        private void RaceManager_OnRaceEnd(Race race)
        {
            SetScene(Scenes.RaceResults);
        }

        private void OnLapsRecalculated(Race race)
        {
            if (Scene == Scenes.RaceResults && resultsRaceNode.Race == race)
            {
                // Update the results.
                resultsRaceNode.SetRace(race);
            }
        }

        public void SetRace(Race race)
        {
            if (race == null)
            {
                SetScene(Scenes.PreRace);
                return;
            }

            if (race.Ended)
            {
                SetScene(Scenes.RaceResults);
            }
            else if (race.Running)
            {
                SetScene(Scenes.Race);
            }
            else
            {
                SetScene(Scenes.PreRace);
            }
        }

        public void SetScene(Scenes s, bool force = false)
        {
            if (Scene == s && !force)
                return;

            Scene = s;

            if (Scene != Scenes.Fullscreen)
            {
                UnFullScreen();
            }

            SetChannelGridReordering(s);

            // If those scenes are disabled, ignore that scene and do race.
            if (s == Scenes.PreRace && !PreRaceScene || s == Scenes.RaceResults && !PostRaceScene)
            {
                SceneLayout(Scenes.Race);
            }
            else // Do the requested layout
            {
                SceneLayout(Scene);
            }

            // Need to make this visible again :)
            ChannelsGridNode.Visible = true;

            RequestLayout();

            OnSceneChange?.Invoke(s);
        }

        public void SetAnimationTime(TimeSpan time)
        {
            launchCamsNode.AnimationTime = time;
            commentatorsAndSummary.AnimationTime = time;
            resultsRaceNode.AnimationTime = time;
            nextRaceNode.AnimationTime = time;
            ChannelsGridNode.SetAnimationTime(time);
            topBarNode.SetAnimationTime(time);
        }

        private void SetChannelGridReordering(Scenes scene)
        {
            bool hasPilots = eventManager.RaceManager.HasPilots;
            switch (scene)
            {
                case Scenes.PreRace:
                    if (ProfileSettings.Instance.PilotOrderPreRace == GeneralSettings.OrderTypes.PositionAndPB)
                    {
                        ChannelsGridNode.SetReorderType(ChannelsGridNode.ReOrderTypes.PositionOrder);
                    }
                    else
                    {
                        ChannelsGridNode.SetReorderType(ChannelsGridNode.ReOrderTypes.ChannelOrder);
                    }
                    break;
                case Scenes.Race:
                    if (ProfileSettings.Instance.PilotOrderMidRace == GeneralSettings.OrderTypes.PositionAndPB)
                    {
                        ChannelsGridNode.SetReorderType(ChannelsGridNode.ReOrderTypes.PositionOrder);
                    }
                    else
                    {
                        ChannelsGridNode.SetReorderType(ChannelsGridNode.ReOrderTypes.ChannelOrder);
                    }
                    break;
                case Scenes.RaceResults:
                    if (ProfileSettings.Instance.PilotOrderPostRace == GeneralSettings.OrderTypes.PositionAndPB)
                    {
                        ChannelsGridNode.SetReorderType(ChannelsGridNode.ReOrderTypes.PositionOrder);
                    }
                    else
                    {
                        ChannelsGridNode.SetReorderType(ChannelsGridNode.ReOrderTypes.ChannelOrder);
                    }
                    break;
            }
        }

        private void SceneLayout(Scenes scene)
        {
            float channelGridHeight = 0.3f;
            float nonChannelGridHeight = 1 - channelGridHeight;

            switch (scene)
            {
                case Scenes.PreRace:
                    float launchWidth = 0.7f;
                    SetAnimationTime(SetupAnimationTime);

                    IEnumerable<Node> launchCams = launchCamsNode.VisibleChildren;
                    IEnumerable<Node> commentatorCams = commentatorsAndSummary.VisibleChildren;

                    if (!launchCams.Any() && !commentatorCams.Any())
                    {
                        channelGridHeight = 1;
                        nonChannelGridHeight = 0;

                        ChannelsGridNode.SingleRow = false;
                        ChannelsGridNode.MakeExtrasVisible(true);
                    }
                    else
                    {
                        ChannelsGridNode.SingleRow = true;

                        if (!launchCams.Any())
                        {
                            launchWidth = 0;
                        }

                        if (!commentatorCams.Any())
                        {
                            launchWidth = 1;
                        }
                        ChannelsGridNode.MakeExtrasVisible(false);
                    }

                    ChannelsGridNode.SetBiggerInfo(true, false);
                    ChannelsGridNode.SetProfileVisible(true, true);

                    launchCamsNode.SetAnimatedVisibility(true);
                    launchCamsNode.RelativeBounds = new RectangleF(0, 0, launchWidth, nonChannelGridHeight);

                    Node.AlignHorizontally(0, launchCams.ToArray());

                    commentatorsAndSummary.SetAnimatedVisibility(true);
                    commentatorsAndSummary.RelativeBounds = new RectangleF(launchWidth, 0, 1 - launchWidth, nonChannelGridHeight);

                    if (launchCams.Any())
                    {
                        Node.AlignVertically(0, commentatorsAndSummary.VisibleChildren.ToArray());
                    }
                    else
                    {
                        Node.AlignHorizontally(0, commentatorsAndSummary.VisibleChildren.ToArray());
                    }

                    ChannelsGridNode.RelativeBounds = new RectangleF(0, nonChannelGridHeight, 1, channelGridHeight);

                    resultsRaceNode.SetAnimatedVisibility(false);
                    nextRaceNode.SetAnimatedVisibility(false);

                    eventStatusNodeContainer.SetAnimatedVisibility(false);
                    finishLineNode.SetAnimatedVisibility(false);

                    break;

                case Scenes.Race:
                    ChannelsGridNode.SetProfileVisible(false, true);

                    SetAnimationTime(MidRaceAnimationTime);
                    
                    ChannelsGridNode.SingleRow = false;
                    ChannelsGridNode.RelativeBounds = new RectangleF(0, 0.0f, 1, 1);

                    commentatorsAndSummary.SetAnimatedVisibility(false);
                    launchCamsNode.SetAnimatedVisibility(false);
                    finishLineNode.SetAnimatedVisibility(false);

                    resultsRaceNode.SetAnimatedVisibility(false);
                    nextRaceNode.SetAnimatedVisibility(false);
                    ChannelsGridNode.SetBiggerInfo(!eventManager.RaceManager.RaceRunning, false);
                    ChannelsGridNode.MakeExtrasVisible(true);
                    eventStatusNodeContainer.SetAnimatedVisibility(false);
                    break;

                case Scenes.Clear:
                    SetAnimationTime(SetupAnimationTime);

                    ChannelsGridNode.AllVisible(false);
                    commentatorsAndSummary.SetAnimatedVisibility(false);
                    launchCamsNode.SetAnimatedVisibility(false);
                    finishLineNode.SetAnimatedVisibility(false);

                    resultsRaceNode.SetAnimatedVisibility(false);
                    nextRaceNode.SetAnimatedVisibility(false);

                    ChannelsGridNode.SetBiggerInfo(false, false);
                    ChannelsGridNode.MakeExtrasVisible(false);
                    ChannelsGridNode.SetProfileVisible(false, true);
                    eventStatusNodeContainer.SetAnimatedVisibility(false);
                    break;

                case Scenes.RaceResults:
                    SetAnimationTime(SetupAnimationTime);

                    ChannelsGridNode.SetProfileVisible(false, true);

                    launchCamsNode.SetAnimatedVisibility(false);
                    finishLineNode.SetAnimatedVisibility(false);

                    ChannelsGridNode.SingleRow = true;
                    ChannelsGridNode.RelativeBounds = new RectangleF(0, 0.0f, 1, channelGridHeight);

                    if (commentatorsAndSummary.ChildCount > 0)
                    {
                        float commentatorsize = 0.48f;
                        float halfRemainder = (1 - commentatorsize) / 2.0f;

                        commentatorsAndSummary.SetAnimatedVisibility(true);
                        commentatorsAndSummary.RelativeBounds = new RectangleF(halfRemainder, channelGridHeight, commentatorsize, nonChannelGridHeight);
                        AlignVertically(0, commentatorsAndSummary.Children);

                        resultsRaceNode.RelativeBounds = new RectangleF(0, channelGridHeight, halfRemainder, nonChannelGridHeight);
                        resultsRaceNode.Scale(0.95f, 0.8f);

                        nextRaceNode.RelativeBounds = new RectangleF(halfRemainder + commentatorsize, channelGridHeight, halfRemainder, nonChannelGridHeight);
                        nextRaceNode.Scale(0.95f, 0.8f);
                    }
                    else
                    {
                        commentatorsAndSummary.SetAnimatedVisibility(false);

                        resultsRaceNode.RelativeBounds = new RectangleF(0.125f, channelGridHeight, 0.33f, nonChannelGridHeight);
                        resultsRaceNode.Scale(0.8f);

                        nextRaceNode.RelativeBounds = new RectangleF(0.535f, channelGridHeight, 0.33f, nonChannelGridHeight);
                        nextRaceNode.Scale(0.8f);
                    }

                    nextRaceNode.SetAnimatedVisibility(true);

                    if (eventManager.RaceManager.RaceFinished)
                    {
                        resultsRaceNode.SetAnimatedVisibility(true);
                        resultsRaceNode.SetRace(eventManager.RaceManager.CurrentRace);
                    }
                    else
                    {
                        resultsRaceNode.SetAnimatedVisibility(false);
                    }

                    UpdateNextRaceNode();

                    ChannelsGridNode.SetBiggerInfo(true, true);
                    ChannelsGridNode.MakeExtrasVisible(false);
                    eventStatusNodeContainer.SetAnimatedVisibility(false);
                    break;

                case Scenes.Fullscreen:
                    Node fsNode = fullscreenNode;

                    if (fsNode == null)
                    {
                        break;
                    }

                    if (fsNode is AnimatedNode)
                    {
                        AnimatedNode an = fsNode as AnimatedNode;
                        an.SetAnimatedVisibility(true);
                    }

                    fsNode.RelativeBounds = new RectangleF(0, 0, 1, 1);
                    break;

                case Scenes.EventStatus:
                    launchCamsNode.SetAnimatedVisibility(false);
                    commentatorsAndSummary.SetAnimatedVisibility(false);
                    ChannelsGridNode.RelativeBounds = new RectangleF(0, 0.0f, 0, 0);

                    nextRaceNode.SetAnimatedVisibility(false);
                    resultsRaceNode.SetAnimatedVisibility(false);
                    eventStatusNodeContainer.SetAnimatedVisibility(true);
                    break;

                case Scenes.FinishLine:
                    SetAnimationTime(SetupAnimationTime);

                    IEnumerable<Node> finishCam = finishLineNode.VisibleChildren;

                    if (finishCam.Any())
                    {
                        ChannelsGridNode.SingleRow = true;
                        ChannelsGridNode.MakeExtrasVisible(false);
                    }
                    else
                    {
                        channelGridHeight = 1;
                        nonChannelGridHeight = 0;

                        ChannelsGridNode.SingleRow = false;
                        ChannelsGridNode.MakeExtrasVisible(true);
                    }

                    ChannelsGridNode.SetBiggerInfo(true, false);
                    ChannelsGridNode.SetProfileVisible(true, true);

                    finishLineNode.SetAnimatedVisibility(true);
                    finishLineNode.RelativeBounds = new RectangleF(0, 0, 1, nonChannelGridHeight);

                    ChannelsGridNode.RelativeBounds = new RectangleF(0, nonChannelGridHeight, 1, channelGridHeight);

                    resultsRaceNode.SetAnimatedVisibility(false);
                    nextRaceNode.SetAnimatedVisibility(false);

                    eventStatusNodeContainer.SetAnimatedVisibility(false);
                    break;
            }
        }

        public void Hide()
        {
            launchCamsNode.SetAnimatedVisibility(false);
            commentatorsAndSummary.SetAnimatedVisibility(false);
            resultsRaceNode.SetAnimatedVisibility(false);
            nextRaceNode.SetAnimatedVisibility(false);

            ChannelsGridNode.Visible = false;
        }

        public void Show()
        {
            SetScene(Scene);
        }

        public void SetupCams()
        {
            launchCamsNode.ClearDisposeChildren();
            commentatorsAndSummary.ClearDisposeChildren();

            foreach (FrameSource source in videoManager.GetFrameSources())
            {
                foreach (VideoBounds videoBounds in source.VideoConfig.VideoBounds)
                {
                    CamNode node = null;
                    switch (videoBounds.SourceType)
                    {
                        case SourceTypes.Commentators:
                            node = new CamNode(source, videoBounds);
                            node.OnFullScreenRequest += FullScreen;
                            commentatorsAndSummary.AddChild(node);
                            break;

                        case SourceTypes.Launch:
                            node = new CamNode(source, videoBounds);
                            node.OnFullScreenRequest += FullScreen;
                            node.FrameNode.KeepAspectRatio = false;
                            node.FrameNode.CropToFit = true;
                            node.FrameNode.Alignment = RectangleAlignment.Center;
                            launchCamsNode.AddChild(node);
                            break;

                        case SourceTypes.FinishLine:
                            node = new CamNode(source, videoBounds);
                            node.OnFullScreenRequest += FullScreen;
                            node.FrameNode.KeepAspectRatio = false;
                            node.FrameNode.CropToFit = true;
                            node.FrameNode.Alignment = RectangleAlignment.Center;
                            finishLineNode.AddChild(node);
                            break;

                    }
                    if (node != null)
                    {
                        node.OnVideoBoundsChange += Node_OnVideoBoundsChange;
                    }
                }
            }
        }

        private void Node_OnVideoBoundsChange(VideoBounds obj)
        {
            videoManager.WriteCurrentDeviceConfig();
            OnVideoSettingsChange?.Invoke();
        }

        public void UnFullScreen()
        {
            if (fullscreenNode != null && fullscreenNodeParent != null)
            {
                fullscreenNode.Remove();
                fullscreenNodeParent.AddChild(fullscreenNode);

                fullscreenNode = null;
                fullscreenNodeParent = null;
                SetScene(preFullScreenScene);
            }
        }

        private void OnChannelsGridNodeFullScreen(Node node)
        {
            FullScreen(node);
            fullScreenContainer.KeepAspectRatio = true;
        }

        public void FullScreen(Node node)
        {
            if (Scene != Scenes.Fullscreen)
            {
                preFullScreenScene = Scene;
            }

            UnFullScreen();

            fullscreenNode = node;
            fullscreenNodeParent = node.Parent;

            fullscreenNode.Remove();
            fullScreenContainer.AddChild(fullscreenNode, 0);
            fullScreenContainer.KeepAspectRatio = false;

            SetScene(Scenes.Fullscreen);
        }

        public void FullScreen(Pilot pilot)
        {
            ChannelNodeBase channelNodeBase = ChannelsGridNode.GetChannelNode(pilot);
            if (channelNodeBase != null && pilot != null)
            {
                FullScreen(channelNodeBase);
                fullScreenContainer.KeepAspectRatio = true;
            }
            else
            {
                UnFullScreen();
            }
        }

        public void ShowCommentators()
        {
            FullScreen(commentatorsAndSummary);
        }

        public void ToggleWorm()
        {
            if (wormNode.RelativeBounds.Height > 0)
            {
                wormNode.RelativeBounds = new RectangleF(0.0f, 1f, 1, 0.0f);
                ChannelsGridNode.RelativeBounds = new RectangleF(0, 0, 1, 1);
            }
            else
            { 
                // 0.2 and 6 are the numbers it was designed for...
                float height = 0.2f * (eventManager.RaceManager.PilotCount / 6.0f);

                wormNode.RelativeBounds = new RectangleF(0.0f, 1 - height, 1, height);
                ChannelsGridNode.RelativeBounds = new RectangleF(0, 0, 1, 1 - height);
            }
            RequestLayout();
        }

        private class FullScreenAspectClosable : AspectNode
        {
            private CloseNode closeNode;

            public event Action Close;

            public FullScreenAspectClosable()
                :base(4 / 3.0f)
            {
                closeNode = new CloseNode();
                closeNode.OnClick += CloseNode_OnClick;
                AddChild(closeNode);    
            }

            private void CloseNode_OnClick(MouseInputEvent mie)
            {
                Close?.Invoke();
            }

            public override void Draw(Drawer id, float parentAlpha)
            {
                if (Children.Length == 2)
                {
                    base.Draw(id, parentAlpha);
                }
            }

            public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
            {
                if (Children.Length == 2)
                {
                    return base.OnMouseInput(mouseInputEvent);
                }
                return false;
            }
        }
    }
}
