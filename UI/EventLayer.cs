using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using ExternalData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RaceLib;
using Sound;
using System;
using System.Linq;
using System.Threading;
using Tools;
using UI.Nodes;
using UI.Video;
using Webb;
using UI.Sponsor;

namespace UI
{
    public class EventLayer : CompositorLayer, IRaceControl
    {
        public EventManager EventManager { get; private set; }

        public ControlButtonsNode ControlButtons { get; private set; }

        private VideoManager videoManager;

        private PilotListNode pilotList;
        public MenuButton MenuButton { get; private set; }

        public SoundManager SoundManager { get; private set; }

        private Node mainContainer;
        private AnimatedRelativeNode leftContainer;
        private AnimatedRelativeNode centreContainer;

        private TextNode hideLeftNode;

        private EventWebServer eventWebServer;

        public ChannelsGridNode ChannelsGridNode { get; private set; }
        public RoundsNode RoundsNode { get; private set; }

        public TracksideTabbedMultiNode TabbedMultiNode { get; private set; }

        private SceneManagerNode sceneManagerNode;

        private TopBarNode topBar;
        private AspectNode centralAspectNode;
        private AnimatedNode rightBar;

        private float rightBarWidth = 0.035f;
        private float leftContainerWidth = 0.15f;

        private bool showPilotList;

        private ExternalData.RemoteNotifier RemoteNotifier;
        private WorkQueue workQueueStartRace;

        private SystemStatusNode systemStatusNode;
        public KeyboardShortcuts KeyMapper { get; private set; }

        public OBSRemoteControlManager OBSRemoteControlManager { get; private set; }

        public AutoRunner AutoRunner { get; private set; }

        public Profile Profile
        {
            get
            {
                return EventManager.Profile;
            }
        }

        public EventLayer(BaseGame game, GraphicsDevice graphicsDevice, EventManager eventManager)
            : base(graphicsDevice)
        {

            workQueueStartRace = new WorkQueue("Event Layer - Start Race");

            showPilotList = true;

            EventManager = eventManager;
            EventManager.SetChannelColors(Theme.Current.ChannelColors.XNA());

            RaceStringFormatter.Instance.Practice = ProfileSettings.Instance.Practice;
            RaceStringFormatter.Instance.TimeTrial = ProfileSettings.Instance.TimeTrial;
            RaceStringFormatter.Instance.Race = ProfileSettings.Instance.Race;
            RaceStringFormatter.Instance.Freestyle = ProfileSettings.Instance.Freestyle;
            RaceStringFormatter.Instance.Endurance = ProfileSettings.Instance.Endurance;
            RaceStringFormatter.Instance.CasualPractice = ProfileSettings.Instance.CasualPractice;

            EventManager.RaceManager.RemainingTimesToAnnounce = ProfileSettings.Instance.RemainingSecondsToAnnounce;

            videoManager = new VideoManager(GeneralSettings.Instance.VideoStorageLocation, eventManager.Profile);

            SoundManager = new SoundManager(EventManager, eventManager.Profile);
            SoundManager.MuteTTS = !ProfileSettings.Instance.TextToSpeech;

            EventManager.RaceManager.TimingSystemManager.OnConnected += (int count) =>
            {
                SoundManager.TimingSystemsConnected(count);
            };

            EventManager.RaceManager.TimingSystemManager.OnDisconnected += () =>
            {
                SoundManager.TimingSystemDisconnected();
            };

            EventManager.RaceManager.OnRaceTimeRemaining += (r, t) =>
            {
                SoundManager.TimeRemaining(r, t);
            };

            EventManager.RaceManager.OnRaceTimesUp += (r) =>
            {
                SoundManager.TimesUp(r);
                OBSRemoteControlManager.Trigger(OBSRemoteControlManager.Triggers.TimesUp);
            };

            EventManager.RaceManager.OnRaceChanged += (r) =>
            {
                if (r != null)
                {
                    TabbedMultiNode.ShowLive();
                }

                ShowPilotList(r == null || r.PilotCount == 0);
            };


            topBar = new TopBarNode();
            topBar.RelativeBounds = new RectangleF(0, 0, 1, 0.1f); 
            Root.AddChild(topBar);

            rightBar = new AnimatedNode();
            rightBar.AnimationTime = TimeSpan.FromSeconds(ProfileSettings.Instance.ReOrderAnimationSeconds);

            centralAspectNode = new AspectNode();
            centralAspectNode.SetAspectRatio(16, 9);
            centralAspectNode.Alignment = RectangleAlignment.TopLeft;
            centralAspectNode.RelativeBounds = new RectangleF(0, 0, 1 - rightBarWidth, 1);
            Root.AddChild(centralAspectNode);
            Root.AddChild(rightBar);

            ColorNode rightSideColor = new ColorNode(Theme.Current.RightControls.Background);
            rightBar.AddChild(rightSideColor);
                        
            mainContainer = new Node();
            mainContainer.RelativeBounds = new RectangleF(0, topBar.RelativeBounds.Bottom, 1, 1 - topBar.RelativeBounds.Bottom);
            centralAspectNode.AddChild(mainContainer);


            leftContainer = new AnimatedRelativeNode();
            ColorNode leftBg = new ColorNode(Theme.Current.LeftPilotList.Background);
            leftContainer.AddChild(leftBg);

            pilotList = new PilotListNode(EventManager);
            leftContainer.AddChild(pilotList);

            pilotList.OnPilotClick += (mouseInputEvent, pilot) =>
            {
                var dl = LayerStack.GetLayer<DragLayer>();
                if (dl != null && dl.IsDragging)
                {
                    return;
                }

                if (mouseInputEvent.Button == MouseButtons.Left && mouseInputEvent.ButtonState == ButtonStates.Released)
                {
                    TogglePilot(pilot);
                    pilotList.UpdatePilotChannel(pilot);
                }
            };
            pilotList.OnPilotChannelClick += (mie, p) =>
            {
                MouseMenu mm = new MouseMenu(LayerStack.GetLayer<MenuLayer>());

                foreach (var cg in EventManager.Channels.GetChannelGroups())
                {
                    foreach (Channel c in cg)
                    {
                        mm.AddItem(c.ToString(), () => { EventManager.SetPilotChannel(p, c); });
                    }
                    mm.AddBlank();
                }

                mm.Show(mie);

                RequestLayout();
            };


            centreContainer = new AnimatedRelativeNode();
            mainContainer.AddChild(centreContainer);
            mainContainer.AddChild(leftContainer);

            Node.SplitHorizontally(leftContainer, centreContainer, leftContainerWidth);

            float btnSize = 0.03f;
            AnimatedNode hideButtonNode = new AnimatedNode();
            hideButtonNode.RelativeBounds = new RectangleF(0, 1 - btnSize, btnSize, btnSize);
            centreContainer.AddChild(hideButtonNode);

            hideLeftNode = new TextNode("<", Color.Gray);
            hideButtonNode.AddChild(hideLeftNode);
            ButtonNode hideButton = new ButtonNode();
            hideButton.OnClick += (mie) =>
            {
                ShowPilotList(!showPilotList);
            };
            hideLeftNode.AddChild(hideButton);

            AutoRunner = new AutoRunner(this);

            ChannelsGridNode = new ChannelsGridNode(EventManager, videoManager);
            sceneManagerNode = new SceneManagerNode(EventManager, videoManager, ChannelsGridNode, topBar, AutoRunner);
            sceneManagerNode.OnSceneChange += SceneManagerNode_OnSceneChange;
            sceneManagerNode.OnVideoSettingsChange += LoadVideo;

            RoundsNode = new RoundsNode(EventManager);

            TabbedMultiNode = new TracksideTabbedMultiNode(eventManager, videoManager, RoundsNode, sceneManagerNode);
            TabbedMultiNode.RelativeBounds = new RectangleF(0, 0, 1, 0.99f);
            TabbedMultiNode.OnTabChange += OnTabChange;
            centreContainer.AddChild(TabbedMultiNode);

            topBar.Init(EventManager, TabbedMultiNode.ReplayNode);

            ControlButtons = new ControlButtonsNode(EventManager, ChannelsGridNode, TabbedMultiNode, AutoRunner);
            ControlButtons.RelativeBounds = new RectangleF(0, 0.0f, 1, 1);
            rightBar.AddChild(ControlButtons);

            ControlButtons.StartButton.OnClick += (mie) => { StartRace(); };
            ControlButtons.StopButton.OnClick += (mie) => { StopRace(); };
            ControlButtons.ClearButton.OnClick += (mie) => { Clear(); };
            ControlButtons.NextButton.OnClick += NextButton_OnClick;
            ControlButtons.ResumeButton.OnClick += (mie) => { ResumeRace(); };
            ControlButtons.ResetButton.OnClick += (mie) => 
            {
                LayerStack.GetLayer<PopupLayer>().PopupConfirmation("Confirm Reset Race", EventManager.RaceManager.ResetRace);
            };

            ControlButtons.PasteClipboard.OnClick += (mie) =>
            {
                var lines = PlatformTools.Clipboard.GetLines();
                EventManager.AddPilotsFromLines(lines);
            };

            ControlButtons.CopyResultsClipboard.OnClick += (mie) =>
            {
                PlatformTools.Clipboard.SetText(EventManager.GetResultsText());
            };

            ControlButtons.PilotList.OnClick += (mie) =>
            {
                ShowPilotList(!showPilotList);
            };

            IRaceControl raceControl = null;
            if (GeneralSettings.Instance.HTTPServerRaceControl)
            {
                raceControl = this;
            }

            eventWebServer = new EventWebServer(EventManager, SoundManager, raceControl, new IWebbTable[] { TabbedMultiNode.LapRecordsSummaryNode, TabbedMultiNode.LapCountSummaryNode, TabbedMultiNode.PointsSummaryNode });

            if (GeneralSettings.Instance.HTTPServer)
            {
                eventWebServer.Start();
            }

            MenuButton = new MenuButton(Profile, EventManager, videoManager, SoundManager, eventWebServer, TabbedMultiNode, Theme.Current.Hover.XNA, Theme.Current.RightControls.Text.XNA);
            MenuButton.RelativeBounds = new RectangleF(0.7f, 0, 0.3f, 0.03f);
            MenuButton.ImageNode.Tint = Theme.Current.RightControls.Text.XNA;
            rightBar.AddChild(MenuButton);

            MenuButton.BackToEventSelector += BackToEventSelector;
            MenuButton.ChannelsChanged += () =>
            {
                pilotList.RebuildList();

                LoadVideo();
                RoundsNode.ChannelsChanged();
            };

            MenuButton.TimingChanged += () =>
            {
                systemStatusNode.SetupStatuses(EventManager.RaceManager.TimingSystemManager, videoManager, SoundManager, OBSRemoteControlManager);
            };

            MenuButton.VideoSettingsExited += (bool changed) =>
            {
                LoadVideo();

                if (EventManager.RaceManager.CurrentRace != null)
                {
                    Race race = EventManager.RaceManager.CurrentRace;
                    EventManager.RaceManager.ClearRace();
                    EventManager.RaceManager.SetRace(race);
                }
                sceneManagerNode.SetScene(sceneManagerNode.Scene, true);
            };
            MenuButton.EventChanged += () =>
            {
                topBar.UpdateDetails();
                pilotList.RebuildList();
            };

            MenuButton.DataDeleted += () =>
            {
                pilotList.RebuildList();
                RoundsNode.Refresh();
            };

            MenuButton.Restart += (e) =>
            {
                if (LayerStack.Game is UI.BaseGame)
                {
                    ((UI.BaseGame)LayerStack.Game).Restart(e);
                }
            };

            MenuButton.OBSRemoteConfigSaved += ReloadOBSRemoteControl;
            MenuButton.AutoRunnerConfigsSaved += ReloadAutoRunnerConfig;

            float width = 0.9f;

            systemStatusNode = new SystemStatusNode();
            systemStatusNode.SetupStatuses(EventManager.RaceManager.TimingSystemManager, videoManager, SoundManager, OBSRemoteControlManager);
            systemStatusNode.RelativeBounds = new RectangleF((1 - width) / 2, MenuButton.RelativeBounds.Bottom + 0.01f, 0.9f, 1);
            rightSideColor.AddChild(systemStatusNode);

            ChannelsGridNode.OnChannelNodeCloseClick += (ChannelNodeBase cn) =>
            {
                if (cn.Pilot != null)
                {
                    TogglePilot(cn.Pilot);
                }
                else
                {
                    cn.Visible = false;
                }
            };

            RequestRedraw();

            ControlButtons.UpdateControlButtons();

            if (ProfileSettings.Instance.NotificationEnabled)
            {
                RemoteNotifier = new RemoteNotifier(EventManager, ProfileSettings.Instance.NotificationURL, ProfileSettings.Instance.NotificationSerialPort);
            }

            KeyMapper = KeyboardShortcuts.Read(Profile);

            ReloadOBSRemoteControl();
        }

        private void ReloadAutoRunnerConfig()
        {
            AutoRunner.LoadConfig(Profile);
        }

        private void NextButton_OnClick(MouseInputEvent mie)
        {
            if (EventManager.RaceManager.CurrentRace == null)
            {
                Race lastRace = EventManager.RaceManager.LastFinishedRace();
                if (lastRace != null) 
                {
                    EventManager.RaceManager.SetRace(lastRace);
                    return;
                }
            }

            // If we didn't find the last finished race, just do next race.
            NextRace(true);
        }

        public override void Dispose()
        {
            SponsorLayer sponsor = LayerStack.GetLayer<SponsorLayer>();
            if (sponsor != null)
            {
                sponsor.SoundManager = null;
            }

            EventManager.Dispose();
            workQueueStartRace.Dispose();
            SoundManager.Dispose();
            videoManager.Dispose();
            sceneManagerNode.Dispose();

            eventWebServer?.Stop();
            RemoteNotifier?.Dispose();

            OBSRemoteControlManager?.Dispose();

            eventWebServer?.Dispose();

            base.Dispose();
        }

        public void FinalSetup()
        {
            bool recoveredRace = false;
            Race toRecover = EventManager.RaceManager.GetRaceToRecover();
            if (toRecover != null)
            {
                LayerStack.GetLayer<PopupLayer>().PopupConfirmation("Recover race?", () =>
                {
                    recoveredRace = RecoverRace(toRecover);
                });
            }

            if (!recoveredRace)
            {
                Clear();
                TabbedMultiNode.Snap();
            }
        }
        private void ReloadOBSRemoteControl()
        {
            OBSRemoteControlManager?.Dispose();
            OBSRemoteControlManager = new OBSRemoteControlManager(sceneManagerNode, TabbedMultiNode, EventManager);

            systemStatusNode.SetupStatuses(EventManager.RaceManager.TimingSystemManager, videoManager, SoundManager, OBSRemoteControlManager);
        }

        public void ResumeRace()
        {
            Race race = EventManager.RaceManager.CurrentRace;
            if (race != null && race.Ended)
            {
                TimeSpan timeSinceEnd = DateTime.Now - race.End;
                if (timeSinceEnd.TotalSeconds > 5)
                {
                    workQueueStartRace.Enqueue(() =>
                    {
                        SoundManager.StartRaceIn(EventManager.Event.MaxStartDelay, () =>
                        {
                            if (RecoverRace(race))
                            {
                                SoundManager.Start();
                            }
                        });
                    });
                }
                else
                {
                    RecoverRace(race);
                }
            }
        }

        public void NextRace(bool unfinishedOnly)
        {
            SponsorLayer sponsorLayer = LayerStack.GetLayer<SponsorLayer>();
            if (sponsorLayer != null && GeneralSettings.Instance.SponsoredByMessages)
            {
                sponsorLayer.TriggerMaybe(() => 
                {
                    EventManager.RaceManager.NextRace(unfinishedOnly);
                });       
            }
            else
            {
                EventManager.RaceManager.NextRace(unfinishedOnly);
            }
        }

        private bool RecoverRace(Race toRecover)
        {
            bool recoveredRace = EventManager.RaceManager.ResumeRace(toRecover);
            if (recoveredRace && ProfileSettings.Instance.AutoHideShowPilotList)
            {
                ShowPilotList(false);
                TabbedMultiNode.ShowLive();
                TabbedMultiNode.Snap();
                sceneManagerNode.SetScene(SceneManagerNode.Scenes.Race);
                sceneManagerNode.Snap();
                RequestLayout();
                ControlButtons.UpdateControlButtons();
                return true;
            }
            return false;
        }

        public void LoadVideo()
        {
            using (AutoResetEvent waiter = new AutoResetEvent(false))
            {
                Race current = EventManager.RaceManager.CurrentRace;
                EventManager.RaceManager.ClearRace();

                videoManager.LoadCreateDevices((fs) =>
                {
                    ChannelsGridNode.FillChannelNodes();
                    sceneManagerNode.SetupCams();
                    systemStatusNode.SetupStatuses(EventManager.RaceManager.TimingSystemManager, videoManager, SoundManager, OBSRemoteControlManager);

                    if (current != null)
                    {
                        EventManager.RaceManager.SetRace(current);
                    }
                    waiter.Set();
                });

                waiter.WaitOne();
            }
        }

        public override void SetLayerStack(LayerStack layerStack)
        {
            base.SetLayerStack(layerStack);
            UpdateCrop(ProfileSettings.Instance.CropContent16by9);

            SponsorLayer sponsor = LayerStack.GetLayer<SponsorLayer>();
            if (sponsor != null)
            {
                sponsor.SoundManager = SoundManager;
            }

            if (SoundManager != null)
            {
                SoundManager.SetupSpeaker(PlatformTools, ProfileSettings.Instance.Voice, ProfileSettings.Instance.TextToSpeechVolume);
            }
        }

        public void UpdateCrop(bool crop)
        {
            centralAspectNode.KeepAspectRatio = crop;

            //Move stuff around for streamer mode.
            BackgroundLayer backgroundLayer = LayerStack.GetLayer<BackgroundLayer>();
            if (backgroundLayer != null)
            {
                if (crop)
                {
                    backgroundLayer.Crop(16, 9);
                }
                else
                {
                    backgroundLayer.Uncrop();
                }
            }

            if (crop)
            {
                topBar.Remove();
                centralAspectNode.AddChild(topBar);

                rightBar.RelativeBounds = new RectangleF(1 - rightBarWidth, 0, rightBarWidth, 1);
            }
            else
            {
                topBar.Remove();
                Root.AddChild(topBar);

                rightBar.RelativeBounds = new RectangleF(1 - rightBarWidth, topBar.RelativeBounds.Bottom, rightBarWidth, 1 - topBar.RelativeBounds.Bottom);
            }
        }

        private void BackToEventSelector()
        {
            if (LayerStack.Game is UI.BaseGame)
            {
                ((UI.BaseGame)LayerStack.Game).Restart(null);
            }
        }

        private void ShowPilotList(bool show)
        {
            if (show)
            {
                leftContainer.RelativeBounds = new RectangleF(0, 0, leftContainerWidth, 1);
                centreContainer.RelativeBounds = new RectangleF(leftContainerWidth, 0, 1 - leftContainerWidth, 1);
                hideLeftNode.Text = "<";
            }
            else
            {
                hideLeftNode.Text = ">";
                leftContainer.RelativeBounds = new RectangleF(-leftContainerWidth, 0, leftContainerWidth, 1);
                centreContainer.RelativeBounds = new RectangleF(0, 0, 1, 1);
            }

            showPilotList = show;

            hideLeftNode.RequestLayout();
        }

        protected override void OnUpdate(GameTime gameTime)
        {
            base.OnUpdate(gameTime);
            EventManager?.Update(gameTime);
            AutoRunner?.Update();
        }

        protected override void OnDraw()
        {
            if (videoManager != null)
            {
                videoManager.UpdateAutoPause();
            }
            base.OnDraw();
        }


        public void TogglePilot(Pilot p)
        {
            ChannelsGridNode.TogglePilotVisible(p);

            if (EventManager.RaceManager.HasPilot(p) && !EventManager.RaceManager.RaceFinished)
            {
                EventManager.RaceManager.RemovePilotFromCurrentRace(p);
            }
            else
            {
                Channel c = EventManager.GetChannel(p);

                if (InputEventFactory.AreControlKeysDown())
                {
                    Channel found = EventManager.RaceManager.GetFreeChannel(c);
                    if (found != null)
                    {
                        c = found;
                        EventManager.SetPilotChannel(p, c);
                    }
                }

                if (c != null && !EventManager.RaceManager.HasPilot(p))
                {
                    if (EventManager.RaceManager.AddPilot(c, p))
                    {
                        ChannelsGridNode.SetPilotVisible(p, true);
                        TabbedMultiNode.ShowLive();
                    }
                }
            }
        }


        public bool StartRace()
        {
            return StartRace(false);
        }

        public bool StartRace(bool ignoreMax)
        {
            if (!EventManager.RaceManager.CanRunRace)
                return false;
            
            Race race = EventManager.RaceManager.CurrentRace;
            if (race == null)
                return false;

            if (EventManager.RaceManager.TimingSystemManager.MaxPilots < race.PilotCount && !ignoreMax)
            {
                string message;
                if (EventManager.RaceManager.TimingSystemManager.MaxPilots == 0)
                {
                    message = "No timing systems have been setup";
                }
                else
                {
                    message = "There are " + race.PilotCount + " pilots in the race. \nBut the Primary Timing System supports only " + EventManager.RaceManager.TimingSystemManager.MaxPilots;
                }

                LayerStack.GetLayer<PopupLayer>().PopupConfirmation(message,
                    () => { StartRace(true); });
                return false;
            }

            if (workQueueStartRace.QueueLength > 0)
                return false;

            if (ProfileSettings.Instance.AutoHideShowPilotList)
            {
                ShowPilotList(false);
                TabbedMultiNode.ShowLive();
            }

            SoundManager.StopSound();

            videoManager.StartRecording(race);

            bool staggeredStart = ProfileSettings.Instance.TimeTrialStaggeredStart && EventManager.RaceManager.RaceType == EventTypes.TimeTrial;

            bool delayedStart = (EventManager.Event.MinStartDelay + EventManager.Event.MaxStartDelay).TotalSeconds > 0 && EventManager.RaceManager.RaceType.HasDelayedStart();

            if (staggeredStart)
            {
                TimeSpan staggeredTime = TimeSpan.FromSeconds(ProfileSettings.Instance.StaggeredStartDelaySeconds);
                EventManager.RaceManager.PreRaceStart();

                AutoResetEvent wait = new AutoResetEvent(false);
                SoundManager.StaggeredStart(() =>
                {
                    wait.Set();
                });

                workQueueStartRace.Enqueue(() =>
                {
                    if (!wait.WaitOne(TimeSpan.FromSeconds(20)))
                    {
                        EventManager.RaceManager.CancelRaceStart(true);
                    }

                    if (!EventManager.RaceManager.StartStaggered(staggeredTime, SoundManager.StaggeredPilot))
                    {
                        EventManager.RaceManager.CancelRaceStart(true);
                    }
                    ControlButtons.UpdateControlButtons();
                });
            }
            else if (delayedStart)
            {
                EventManager.RaceManager.PreRaceStart();

                workQueueStartRace.Enqueue(() =>
                {
                    SoundManager.StartRaceIn(EventManager.Event.MaxStartDelay, () =>
                    {
                        if (!EventManager.RaceManager.StartRace())
                        {
                            EventManager.RaceManager.CancelRaceStart(true);
                        }
                        ControlButtons.UpdateControlButtons();
                    });
                });
            }
            else
            {
                workQueueStartRace.Enqueue(() =>
                {
                    EventManager.RaceManager.PreRaceStart();
                    EventManager.RaceManager.StartRaceInLessThan(TimeSpan.Zero, TimeSpan.Zero);
                });
            }

            ControlButtons.UpdateControlButtons();
            return true;
        }

        public void StopRace()
        {
            workQueueStartRace.Clear();

            if (EventManager.RaceManager.PreRaceStartDelay)
            {
                EventManager.RaceManager.CancelRaceStart(false);
                ControlButtons.UpdateControlButtons();
            }
            else
            {
                EventManager.RaceManager.EndRace();
            }

            videoManager.StopRecording();
        }

        public void Clear()
        {
            if (ProfileSettings.Instance.AutoHideShowPilotList)
            {
                ShowPilotList(true);
            }

            // clear the pilots in the current race..
            if ((EventManager.RaceManager.RaceFinished || EventManager.RaceManager.CanRunRace || EventManager.RaceManager.CurrentRace == null) && !EventManager.RaceManager.PreRaceStartDelay)
            {
                EventManager.RaceManager.ClearRace();
            }

            sceneManagerNode.SetScene(SceneManagerNode.Scenes.Clear);
            ControlButtons.UpdateControlButtons();
        }

        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            if (base.OnKeyboardInput(inputEvent))
                return true;

            if (inputEvent.ButtonState == ButtonStates.Pressed)
            {
                if (KeyMapper.HidePilotList.Match(inputEvent))
                {
                    ShowPilotList(false);
                    return true;
                }

                if (KeyMapper.ShowPilotList.Match(inputEvent))
                {
                    ShowPilotList(true);
                    return true;
                }

                if (KeyMapper.ShowMoreChannels.Match(inputEvent))
                {
                    ChannelsGridNode.IncreaseChannelVisiblity();
                    return true;
                }

                if (KeyMapper.ShowLessChannels.Match(inputEvent))
                {
                    ChannelsGridNode.DecreaseChannelVisiblity();
                    return true;
                }

                if (KeyMapper.ShowLaps.Match(inputEvent))
                {
                    ChannelsGridNode.SetLapsVisiblity(true);
                    return true;
                }

                if (KeyMapper.HideLaps.Match(inputEvent))
                {
                    ChannelsGridNode.SetLapsVisiblity(false);
                    return true;
                }

                if (KeyMapper.ReOrderChannelsNow.Match(inputEvent))
                {
                    ChannelsGridNode.Reorder(true);
                    return true;
                }

                if (KeyMapper.StartStopRace.Match(inputEvent))
                {
                    if (TabbedMultiNode.IsOnLive)
                    {
                        if (ControlButtons.StopButton.Visible)
                        {
                            StopRace();
                        }
                        else if (ControlButtons.StartButton.Visible)
                        {
                            StartRace();
                        }
                        else if (sceneManagerNode.Scene == SceneManagerNode.Scenes.RaceResults)
                        {
                            NextRace(false);
                        }
                    }

                    return true;
                }

                if (KeyMapper.ResumeRace.Match(inputEvent))
                {
                    ResumeRace();
                }


                if (KeyMapper.NextRace.Match(inputEvent))
                {
                    NextRace(false);
                    return true;
                }

                if (KeyMapper.PrevRace.Match(inputEvent))
                {
                    EventManager.RaceManager.PrevRace();
                    return true;
                }


                if (KeyMapper.ShowWorm.Match(inputEvent))
                {
                    sceneManagerNode.ToggleWorm();
                    return true;
                }

                if (KeyMapper.ScenePreRace.Match(inputEvent))
                {
                    TabbedMultiNode.ShowLive(SceneManagerNode.Scenes.PreRace);
                    return true;
                }

                if (KeyMapper.SceneRace.Match(inputEvent))
                {
                    TabbedMultiNode.ShowLive(SceneManagerNode.Scenes.Race);
                    return true;
                }

                if (KeyMapper.ScenePostRace.Match(inputEvent))
                {
                    TabbedMultiNode.ShowLive(SceneManagerNode.Scenes.RaceResults);
                    return true;
                }

                if (KeyMapper.SceneCommentators.Match(inputEvent))
                {
                    TabbedMultiNode.ShowLive(SceneManagerNode.Scenes.Commentators);
                    return true;
                }

                if (KeyMapper.SceneFinishLine.Match(inputEvent)) 
                {
                    TabbedMultiNode.ShowLive(SceneManagerNode.Scenes.FinishLine);
                    return true;
                }

                if (KeyMapper.SceneEventStatus.Match(inputEvent))
                {
                    TabbedMultiNode.ShowLive(SceneManagerNode.Scenes.EventStatus);
                    return true;
                }


                Race race = EventManager.RaceManager.CurrentRace;
                if (KeyMapper.AnnounceRace.Match(inputEvent)) 
                {
                    SoundManager.AnnounceRace(race);
                }

                if (KeyMapper.AnnounceRaceResults.Match(inputEvent))
                {
                    SoundManager.AnnounceResults(race);
                }

                if (KeyMapper.HurryUpEveryone.Match(inputEvent))
                {
                    SoundManager.HurryUpEveryone();
                }

                if (KeyMapper.UntilRaceStart.Match(inputEvent))
                {
                    SoundManager.PlayTimeUntilNextRace(AutoRunner.NextRaceStartTime - DateTime.Now);
                }

                if (KeyMapper.TimeRemaining.Match(inputEvent))
                {
                    SoundManager.TimeRemaining(race, EventManager.RaceManager.RemainingTime);
                }

                if (KeyMapper.RaceOver.Match(inputEvent))
                {
                    SoundManager.RaceOver();
                }

                if (KeyMapper.Custom1.Match(inputEvent))
                    SoundManager.PlaySound(SoundKey.Custom1);
                
                if (KeyMapper.Custom2.Match(inputEvent))
                    SoundManager.PlaySound(SoundKey.Custom2);

                if (KeyMapper.Custom3.Match(inputEvent))
                    SoundManager.PlaySound(SoundKey.Custom3);

                if (KeyMapper.Custom4.Match(inputEvent))
                    SoundManager.PlaySound(SoundKey.Custom4);

                if (KeyMapper.Custom5.Match(inputEvent))
                    SoundManager.PlaySound(SoundKey.Custom5);

                if (race != null)
                {
                    int channelGroupId = 0;
                    foreach (ShortcutKey shortcut in KeyMapper.AddLapChannelGroup)
                    {
                        if (shortcut.Match(inputEvent))
                        {
                            Channel[][] channelGroups = EventManager.Channels.GetChannelGroups().ToArray();
                            if (channelGroups.Length > channelGroupId)
                            {
                                Channel[] group = channelGroups[channelGroupId];

                                PilotChannel pc = race.PilotChannelsSafe.FirstOrDefault(p => group.Contains(p.Channel));
                                if (pc != null)
                                {
                                    EventManager.RaceManager.AddManualLap(pc.Pilot, DateTime.Now);
                                }
                            }
                        }
                        channelGroupId++;
                    }

                    channelGroupId = 0;
                    foreach (ShortcutKey shortcut in KeyMapper.RemoveLapChannelGroup)
                    {
                        if (shortcut.Match(inputEvent))
                        {
                            Channel[][] channelGroups = EventManager.Channels.GetChannelGroups().ToArray();
                            if (channelGroups.Length > channelGroupId)
                            {
                                Channel[] group = channelGroups[channelGroupId];

                                PilotChannel pc = race.PilotChannelsSafe.FirstOrDefault(p => group.Contains(p.Channel));
                                if (pc != null)
                                {
                                    Lap[] laps = race.GetValidLaps(pc.Pilot, true);
                                    if (laps.Any())
                                    {
                                        Lap last = laps.LastOrDefault();
                                        EventManager.RaceManager.DisqualifyLap(last);
                                    }
                                }
                            }
                        }
                        channelGroupId++;
                    }
                }


                switch (inputEvent.Key)
                {
                    case Keys.V:
                        if (InputEventFactory.AreControlKeysDown())
                        {
                            if (ControlButtons.PasteClipboard.Visible)
                            {
                                var lines = PlatformTools.Clipboard.GetLines();
                                EventManager.AddPilotsFromLines(lines);
                            }
                        }
                        return true;

                    case Keys.C:
                        if (InputEventFactory.AreControlKeysDown())
                        {
                            if (ControlButtons.CopyResultsClipboard.Visible)
                            {
                                PlatformTools.Clipboard.SetText(EventManager.GetResultsText());
                            }
                        }
                        else
                        {
                            if (ControlButtons.ClearButton.Visible)
                            {
                                Clear();
                            }
                        }
                        return true;
                }
            }
            return false;
        }

        private void OnTabChange(string tab, Node s)
        {
            ControlButtons.UpdateControlButtons(); 
            if (ProfileSettings.Instance.AutoHideShowPilotList)
            {
                if (TabbedMultiNode.IsOnLive)
                {
                    ShowPilotList(!EventManager.RaceManager.HasPilots);
                }

                if (TabbedMultiNode.IsOnRounds)
                {
                    ShowPilotList(true);
                }
            }

            UpdateTopBar();
            ControlButtons.UpdateControlButtons();
        }

        private void SceneManagerNode_OnSceneChange(SceneManagerNode.Scenes scene)
        {
            UpdateTopBar();
        }

        private void UpdateTopBar()
        {
            if (TabbedMultiNode != null)
            {
                if (TabbedMultiNode.Showing == sceneManagerNode && sceneManagerNode.Scene == SceneManagerNode.Scenes.Race)
                {
                    topBar.LogoOnBottomLine(true);
                    TabbedMultiNode.SetTabsVisible(false);
                    topBar.RelativeBounds = new RectangleF(0, -0.05f, 1, 0.1f);
                }
                else
                {
                    TabbedMultiNode.SetTabsVisible(true);
                    topBar.LogoOnBottomLine(false);
                    topBar.RelativeBounds = new RectangleF(0, 0, 1, 0.1f);
                }
            }

            bool crop = centralAspectNode.KeepAspectRatio;
            if (!crop)
            {
                rightBar.RelativeBounds = new RectangleF(1 - rightBarWidth, topBar.RelativeBounds.Bottom, rightBarWidth, 1 - topBar.RelativeBounds.Bottom);
            }

            mainContainer.RelativeBounds = new RectangleF(0, topBar.RelativeBounds.Bottom, 1, 1 - topBar.RelativeBounds.Bottom);
        }
    }
}
