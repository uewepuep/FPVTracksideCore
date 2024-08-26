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
using System.IO;
using UI.Nodes.Rounds;
using Composition;

namespace UI
{
    public class EventLayer : CompositorLayer, IRaceControl
    {
        public EventManager EventManager { get; private set; }

        public ControlButtonsNode ControlButtons { get; private set; }

        protected VideoManager videoManager;

        protected PilotListNode pilotList;
        public MenuButton MenuButton { get; private set; }

        public SoundManager SoundManager { get; private set; }

        private Node mainContainer;
        private AnimatedRelativeNode leftContainer;
        private AnimatedRelativeNode centreContainer;

        private EventWebServer eventWebServer;

        public ChannelsGridNode ChannelsGridNode { get; private set; }
        public RoundsNode RoundsNode { get; private set; }

        public TracksideTabbedMultiNode TabbedMultiNode { get; private set; }

        protected SceneManagerNode sceneManagerNode;

        private TopBarNode topBar;
        private AspectNode centralAspectNode;
        private AnimatedNode rightBar;
        protected TabButtonsNode tabButtonsNode;
        private TextButtonNode pilotListButton;

        private float rightBarWidth = 0.035f;
        private float leftContainerWidth = 0.15f;

        private bool showPilotList;

        private ExternalData.RemoteNotifier RemoteNotifier;
        private WorkQueue workQueueStartRace;

        private SystemStatusNode systemStatusNode;
        
        private DateTime videoCheckEnd;
        private bool hasReportedNoVideo;

        public KeyboardShortcuts KeyMapper { get; private set; }

        public OBSRemoteControlManager OBSRemoteControlManager { get; private set; }

        public AutoRunner AutoRunner { get; private set; }

        public AutoCrashOut AutoCrashOut { get { return ChannelsGridNode.AutoCrashOut; } }


        public Profile Profile
        {
            get
            {
                return EventManager.Profile;
            }
        }

        public GlobalInterceptKeys GlobalInterceptKeys { get; private set; }

        public EventLayer(BaseGame game, GraphicsDevice graphicsDevice, EventManager eventManager, PlatformTools platformTools)
            : base(graphicsDevice)
        {

            DirectoryInfo eventDirectory = new DirectoryInfo(Path.Combine(ApplicationProfileSettings.Instance.EventStorageLocation, eventManager.Event.ID.ToString()));


            workQueueStartRace = new WorkQueue("Event Layer - Start Race");

            showPilotList = true;

            EventManager = eventManager;
            EventManager.SetChannelColors(Theme.Current.ChannelColors.XNA());

            RaceStringFormatter.Instance.Practice = ApplicationProfileSettings.Instance.Practice;
            RaceStringFormatter.Instance.TimeTrial = ApplicationProfileSettings.Instance.TimeTrial;
            RaceStringFormatter.Instance.Race = ApplicationProfileSettings.Instance.Race;
            RaceStringFormatter.Instance.Freestyle = ApplicationProfileSettings.Instance.Freestyle;
            RaceStringFormatter.Instance.Endurance = ApplicationProfileSettings.Instance.Endurance;
            RaceStringFormatter.Instance.CasualPractice = ApplicationProfileSettings.Instance.CasualPractice;

            EventManager.RaceManager.RemainingTimesToAnnounce = ApplicationProfileSettings.Instance.RemainingSecondsToAnnounce;

            // Init the videos into the video directories.
            VideoManagerFactory.Init(eventDirectory.FullName, eventManager.Profile);

            videoManager = VideoManagerFactory.CreateVideoManager();
            videoManager.AutoPause = true;

            SoundManager = new SoundManager(EventManager, eventManager.Profile);
            SoundManager.MuteTTS = !ApplicationProfileSettings.Instance.TextToSpeech;
            SoundManager.SillyNameChance = ApplicationProfileSettings.Instance.SillyNameChance;
            SoundManager.Units = ApplicationProfileSettings.Instance.Units;
            SoundManager.PilotAnnounceTime = TimeSpan.FromSeconds(ApplicationProfileSettings.Instance.PilotProfileHoldLengthSeconds);

            KeyMapper = KeyboardShortcuts.Read(Profile);

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

            EventManager.RaceManager.OnRacePreStart += (r) =>
            {
                // Update the control buttons to to show cancel etc.
                ControlButtons.UpdateControlButtons();
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
            };

            EventManager.OnPilotRefresh += OnPilotRefresh;

            topBar = new TopBarNode();
            topBar.RelativeBounds = new RectangleF(0, 0, 1, 0.13f); 
            Root.AddChild(topBar);

            rightBar = new AnimatedNode();
            rightBar.SetAnimationTime(TimeSpan.FromSeconds(ApplicationProfileSettings.Instance.ReOrderAnimationSeconds));

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

            pilotList.OnPilotClick += PilotList_OnPilotClick;
            pilotList.OnPilotChannelClick += PilotList_OnPilotChannelClick;
            pilotList.OnTakePhoto += TakePhoto;

            centreContainer = new AnimatedRelativeNode();
            mainContainer.AddChild(centreContainer);
            mainContainer.AddChild(leftContainer);

            Node.SplitHorizontally(leftContainer, centreContainer, leftContainerWidth);

            AutoRunner = new AutoRunner(this);

            ChannelsGridNode = new ChannelsGridNode(EventManager, videoManager);
            sceneManagerNode = new SceneManagerNode(EventManager, videoManager, ChannelsGridNode, topBar, AutoRunner);
            sceneManagerNode.OnSceneChange += SceneManagerNode_OnSceneChange;
            sceneManagerNode.OnVideoSettingsChange += LoadVideo;

            RoundsNode = new RoundsNode(EventManager);

            tabButtonsNode = new TabButtonsNode(Theme.Current.Panel.XNA, Theme.Current.PanelAlt.XNA, Theme.Current.Hover.XNA, Theme.Current.TextMain.XNA);
            pilotListButton = tabButtonsNode.AddTab("Pilots");
            pilotListButton.OnClick += TogglePilotList;
            OnPilotRefresh();

            TabbedMultiNode = CreateTabNode();
            TabbedMultiNode.RelativeBounds = new RectangleF(0, 0, 1, 0.99f);
            TabbedMultiNode.OnTabChange += OnTabChange;
            centreContainer.AddChild(TabbedMultiNode);

            TabbedMultiNode.PhotoBooth.OnNewPhoto += (p) =>
            {
                pilotList.RebuildList();
            };

            topBar.Init(EventManager, TabbedMultiNode.ReplayNode);
            topBar.TabContainer.AddChild(tabButtonsNode);
            TabbedMultiNode.Init(platformTools);

            ControlButtons = new ControlButtonsNode(EventManager, ChannelsGridNode, TabbedMultiNode, AutoRunner);
            ControlButtons.RelativeBounds = new RectangleF(0, 0.0f, 1, 1);
            rightBar.AddChild(ControlButtons);

            ControlButtons.StartButton.OnClick += (mie) => { StartRaceWithVideoCheck(); };
            ControlButtons.StopButton.OnClick += (mie) => { StopRace(); };
            ControlButtons.ClearButton.OnClick += (mie) => { Clear(); };
            ControlButtons.NextButton.OnClick += NextButton_OnClick;
            ControlButtons.ResumeButton.OnClick += (mie) => { ResumeRace(); };
            ControlButtons.ResetButton.OnClick += (mie) => 
            {
                LayerStack.GetLayer<PopupLayer>().PopupConfirmation("Confirm Reset Race", EventManager.RaceManager.ResetRace);
            };
            ControlButtons.WormButton.OnClick += (mie) =>
            {
                sceneManagerNode.ToggleWorm();
            };

            ControlButtons.PasteClipboard.OnClick += (mie) =>
            {
                var lines = PlatformTools.Clipboard.GetLines();
                EventManager.AddPilotsFromLines(lines);
            };

            ControlButtons.CopyResultsClipboard.OnClick += (mie) =>
            {
                PlatformTools.Clipboard.SetText(EventManager.GetResultsText(ApplicationProfileSettings.Instance.Units));
            };

            IRaceControl raceControl = null;
            if (ApplicationProfileSettings.Instance.HTTPServerRaceControl)
            {
                raceControl = this;
            }

            eventWebServer = new EventWebServer(EventManager, SoundManager, raceControl, Theme.Current.ChannelColors);

            if (ApplicationProfileSettings.Instance.HTTPServer)
            {
                eventWebServer.Start();
            }

            MenuButton = new MenuButton(Profile, EventManager, videoManager, SoundManager, eventWebServer, TabbedMultiNode, KeyMapper, Theme.Current.Hover.XNA, Theme.Current.RightControls.Text.XNA);
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
            MenuButton.EventEditor += EventEditor;
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

            if (ApplicationProfileSettings.Instance.NotificationEnabled)
            {
                RemoteNotifier = new RemoteNotifier(EventManager, ApplicationProfileSettings.Instance.NotificationURL, ApplicationProfileSettings.Instance.NotificationSerialPort);
            }


            ReloadOBSRemoteControl();

            SoundManager.OnHighlightPilot += sceneManagerNode.FullScreen;

            GlobalInterceptKeys = GlobalInterceptKeys.Instance;

            ShortcutKey[] globals = new ShortcutKey[] { KeyMapper.GlobalCopyResults, KeyMapper.GlobalStartStopRace, KeyMapper.GlobalNextRace, KeyMapper.GlobalPrevRace };
            GlobalInterceptKeys.AddListen(globals.GetKeys().Distinct());

            GlobalInterceptKeys.OnKeyPress += GlobalInterceptKeys_OnChange;
        }

        protected virtual void EventEditor()
        {
            EventEditor editor = new EventEditor(EventManager.Event);
            ShowEventEditor(editor);
        }

        protected void ShowEventEditor(EventEditor editor)
        {
            LayerStack.GetLayer<PopupLayer>().Popup(editor);

            editor.OnOK += (e) =>
            {
                EventManager.Event = editor.Objects.FirstOrDefault();
                using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                {
                    db.Update(EventManager.Event);
                }

                topBar.UpdateDetails();
                pilotList.RebuildList();
                ControlButtons.UpdateControlButtons();
            };
        }

        public virtual TracksideTabbedMultiNode CreateTabNode()
        {
            return new TracksideTabbedMultiNode(EventManager, videoManager, SoundManager, RoundsNode, sceneManagerNode, tabButtonsNode, KeyMapper);
        }

        private void TakePhoto(MouseInputEvent mie, Pilot p)
        {
            TabbedMultiNode.PhotoBooth.SetPilot(p);
            TabbedMultiNode.ShowPhotoBooth(mie);
        }

        private void PilotList_OnPilotChannelClick(MouseInputEvent mie, Pilot p)
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
        }

        private void PilotList_OnPilotClick(MouseInputEvent mouseInputEvent, Pilot pilot)
        {
            if (TabbedMultiNode.IsOnPhotoBooth)
            {
                TabbedMultiNode.PhotoBooth.SetPilot(pilot);
                return;
            }

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
        }

        private void OnPilotRefresh()
        {
            if (pilotListButton != null)
            {
                pilotListButton.Text = EventManager.Event.PilotCount + " Pilots";
            }
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
            GlobalInterceptKeys.Reset();

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
                RecoverRace(race);
            }
        }

        public void NextRace(bool unfinishedOnly)
        {
            SponsorLayer sponsorLayer = LayerStack.GetLayer<SponsorLayer>();
            if (sponsorLayer != null && ApplicationProfileSettings.Instance.SponsoredByMessages)
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
            if (recoveredRace && ApplicationProfileSettings.Instance.AutoHideShowPilotList)
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

            if (TabbedMultiNode.IsOnPhotoBooth)
            {
                TabbedMultiNode.PhotoBooth.Clean();
                TabbedMultiNode.ShowPhotoBooth(null);
            }
        }

        public override void SetLayerStack(LayerStack layerStack)
        {
            base.SetLayerStack(layerStack);
            UpdateCrop(ApplicationProfileSettings.Instance.CropContent16by9);

            SponsorLayer sponsor = LayerStack.GetLayer<SponsorLayer>();
            if (sponsor != null)
            {
                sponsor.SoundManager = SoundManager;
            }

            if (SoundManager != null)
            {
                SoundManager.SetupSpeaker(PlatformTools, ApplicationProfileSettings.Instance.Voice, ApplicationProfileSettings.Instance.TextToSpeechVolume);
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

        private void TogglePilotList(object mie = null)
        {
            ShowPilotList(!showPilotList);
        }

        private void ShowPilotList(bool show)
        {
            if (show)
            {
                leftContainer.RelativeBounds = new RectangleF(0, 0, leftContainerWidth, 1);
                centreContainer.RelativeBounds = new RectangleF(leftContainerWidth, 0, 1 - leftContainerWidth, 1);
            }
            else
            {
                leftContainer.RelativeBounds = new RectangleF(-leftContainerWidth, 0, leftContainerWidth, 1);
                centreContainer.RelativeBounds = new RectangleF(0, 0, 1, 1);
            }
            showPilotList = show;
        }

        protected override void OnUpdate(GameTime gameTime)
        {
            base.OnUpdate(gameTime);
            EventManager?.Update(gameTime);
            AutoRunner?.Update();

            if (ApplicationProfileSettings.Instance.AutoRaceStartVideoCheck)
            {
                UpdateAutoVideoCheck();
            }
        }

        protected void UpdateAutoVideoCheck()
        {
            if (sceneManagerNode != null) 
            {
                if (sceneManagerNode.Scene == SceneManagerNode.Scenes.VideoCheck)
                {
                    Race race = EventManager.RaceManager.CurrentRace;
                    if (race != null) 
                    {
                        bool allFine = true;
                        Channel badChannel = Channel.None;

                        foreach (Channel channel in race.Channels)
                        {
                            if (!AutoCrashOut.HasMotion(channel))
                            {
                                badChannel = channel;
                                allFine = false;
                            }
                        }

                        if (allFine) 
                        {
                            sceneManagerNode.SetScene(SceneManagerNode.Scenes.PreRace);

                            if (hasReportedNoVideo)
                            {
                                SoundManager.PlayVideoOk(() => { StartRace(); });
                            }
                            else
                            {
                                StartRace();
                            }
                        }
                        else
                        {
                            if (DateTime.Now > videoCheckEnd)
                            {
                                Pilot p = race.GetPilot(badChannel);
                                if (p != null)
                                {
                                    SoundManager.PlayVideoIssuesDelayRace(p);
                                }
                                videoCheckEnd = DateTime.Now + TimeSpan.FromSeconds(ApplicationProfileSettings.Instance.AutoRaceStartVideoCheckAnnouncementSeconds);
                                hasReportedNoVideo = true;
                            }
                        }
                    }
                }
            } 
        }

        private void GlobalInterceptKeys_OnChange()
        {
            if (GlobalInterceptKeys.Match(KeyMapper.GlobalStartStopRace))
            {
                StartStopNext();
            }

            if (GlobalInterceptKeys.Match(KeyMapper.GlobalNextRace))
            {
                NextRace(false);
            }

            if (GlobalInterceptKeys.Match(KeyMapper.GlobalPrevRace))
            {
                EventManager.RaceManager.PrevRace();
            }

            if (GlobalInterceptKeys.Match(KeyMapper.GlobalCopyResults))
            {
                PlatformTools.Clipboard.SetText(EventManager.GetResultsText(ApplicationProfileSettings.Instance.Units));
            }
        }

        protected override void OnDraw()
        {
            // Only draw the pilot list if its visible
            leftContainer.Visible = leftContainer.HasAnimation || leftContainer.RelativeBounds.X >= 0;

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

            if (LowDiskSpace())
            {
                LayerStack.GetLayer<PopupLayer>().PopupMessage("Race Start Cancelled. Low on disk space (< 1gb)");
                return false;
            }

            if (ApplicationProfileSettings.Instance.AutoHideShowPilotList)
            {
                ShowPilotList(false);
                TabbedMultiNode.ShowLive();
            }

            SoundManager.StopSound();

            videoManager.StartRecording(race);

            bool staggeredStart = ApplicationProfileSettings.Instance.TimeTrialStaggeredStart && EventManager.RaceManager.RaceType == EventTypes.TimeTrial;

            bool delayedStart = (EventManager.Event.MinStartDelay + EventManager.Event.MaxStartDelay).TotalSeconds > 0 && EventManager.RaceManager.RaceType.HasDelayedStart();

            if (staggeredStart)
            {
                TimeSpan staggeredTime = TimeSpan.FromSeconds(ApplicationProfileSettings.Instance.StaggeredStartDelaySeconds);
                if (!EventManager.RaceManager.PreRaceStart())
                {
                    EventManager.RaceManager.CancelRaceStart(true);
                }

                AutoResetEvent wait = new AutoResetEvent(false);
                SoundManager.StaggeredStart(() =>
                {
                    wait.Set();
                });

                if (workQueueStartRace.QueueLength > 0)
                    return false;

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
                if (workQueueStartRace.QueueLength > 0)
                    return false;

                workQueueStartRace.Enqueue(() =>
                {
                    bool failed = false;

                    // Trigger the sound. The actual race start will happen after it ends
                    SoundManager.StartRaceIn(EventManager.Event.MaxStartDelay, () =>
                    {
                        if (failed)
                            return;

                        if (!EventManager.RaceManager.StartRace())
                        {
                            EventManager.RaceManager.CancelRaceStart(true);
                        }
                        ControlButtons.UpdateControlButtons();
                    });

                    // Pre race start. Happens first because the sound is playing.
                    if (!EventManager.RaceManager.PreRaceStart())
                    {
                        failed = true;
                        EventManager.RaceManager.CancelRaceStart(true);

                    }
                    ControlButtons.UpdateControlButtons();
                });
            }
            else
            {
                if (workQueueStartRace.QueueLength > 0)
                    return false;

                workQueueStartRace.Enqueue(() =>
                {
                    EventManager.RaceManager.PreRaceStart();
                    EventManager.RaceManager.StartRaceInLessThan(TimeSpan.Zero, TimeSpan.Zero);
                    ControlButtons.UpdateControlButtons();
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
            SoundManager.StopSound();   
            if (ApplicationProfileSettings.Instance.AutoHideShowPilotList)
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
                if (!TabbedMultiNode.IsOnReplay)
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
                    StartStopNext();
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
                    TabbedMultiNode.ShowCommentators();
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
                    SoundManager.StopSound();
                    SoundManager.AnnounceRace(race, true);
                }

                if (KeyMapper.AnnounceRaceResults.Match(inputEvent))
                {
                    SoundManager.StopSound();
                    SoundManager.AnnounceResults(race);
                }

                if (KeyMapper.HurryUpEveryone.Match(inputEvent))
                {
                    SoundManager.StopSound();
                    SoundManager.HurryUpEveryone();
                }

                if (KeyMapper.UntilRaceStart.Match(inputEvent))
                {
                    SoundManager.StopSound();
                    SoundManager.PlayTimeUntilNextRace(AutoRunner.NextRaceStartTime - DateTime.Now);
                }

                if (KeyMapper.TimeRemaining.Match(inputEvent))
                {
                    SoundManager.StopSound();
                    SoundManager.TimeRemaining(race, EventManager.RaceManager.RemainingTime);
                }

                if (KeyMapper.RaceOver.Match(inputEvent))
                {
                    SoundManager.StopSound();
                    SoundManager.RaceOver();
                }

                if (KeyMapper.Custom1.Match(inputEvent))
                {
                    SoundManager.StopSound();
                    SoundManager.PlaySound(SoundKey.Custom1);
                }

                if (KeyMapper.Custom2.Match(inputEvent))
                {
                    SoundManager.StopSound();
                    SoundManager.PlaySound(SoundKey.Custom2);
                }

                if (KeyMapper.Custom3.Match(inputEvent))
                {
                    SoundManager.StopSound();
                    SoundManager.PlaySound(SoundKey.Custom3);
                }

                if (KeyMapper.Custom4.Match(inputEvent))
                {
                    SoundManager.StopSound();
                    SoundManager.PlaySound(SoundKey.Custom4);
                }

                if (KeyMapper.Custom5.Match(inputEvent))
                {
                    SoundManager.StopSound();
                    SoundManager.PlaySound(SoundKey.Custom5);
                }

                if (KeyMapper.EnableTTSAudio.Match(inputEvent))
                {
                    systemStatusNode.MuteTTS.SetMute(false);
                    return true;
                }

                if (KeyMapper.DisableTTSAudio.Match(inputEvent))
                {
                    systemStatusNode.MuteTTS.SetMute(true);
                    return true;
                }

                if (KeyMapper.EnableWAVAudio.Match(inputEvent))
                {
                    systemStatusNode.MuteWAV.SetMute(false);
                    return true;
                }

                if (KeyMapper.DisableWAVAudio.Match(inputEvent))
                {
                    systemStatusNode.MuteWAV.SetMute(true);
                    return true;
                }

                if (KeyMapper.StopSound.Match(inputEvent))
                {
                    SoundManager.StopSound();
                }

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
                                PlatformTools.Clipboard.SetText(EventManager.GetResultsText(ApplicationProfileSettings.Instance.Units));
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

                int i = 0;
                foreach (ShortcutKey shortcut in KeyMapper.ToggleView)
                {
                    if (shortcut.Match(inputEvent))
                    {
                        Channel[] channel = EventManager.GetChannelGroup(i).ToArray();
                        ChannelsGridNode.ToggleCrashedOut(channel);
                    }
                    i++;
                }

            }
            return false;
        }

        private void StartStopNext()
        {
            if (TabbedMultiNode.IsOnLive)
            {
                if (ControlButtons.StopButton.Visible)
                {
                    StopRace();
                }
                else if (ControlButtons.StartButton.Visible)
                {
                    if (sceneManagerNode.Scene == SceneManagerNode.Scenes.PreRace && ApplicationProfileSettings.Instance.AutoRaceStartVideoCheck)
                    {
                        VideoCheck();
                    }
                    else
                    { 
                        StartRace();
                    }
                }
                else if (sceneManagerNode.Scene == SceneManagerNode.Scenes.RaceResults)
                {
                    NextRace(false);
                }
            }
        }


        private void StartRaceWithVideoCheck()
        {
            if (sceneManagerNode.Scene == SceneManagerNode.Scenes.PreRace && ApplicationProfileSettings.Instance.AutoRaceStartVideoCheck)
            {
                VideoCheck();
            }
            else
            {
                StartRace();
            }
        }


        private void VideoCheck()
        {
            if (sceneManagerNode == null)
                return;

            videoCheckEnd = DateTime.Now + TimeSpan.FromSeconds(ApplicationProfileSettings.Instance.AutoRaceStartVideoCheckAnnouncementSeconds);
            hasReportedNoVideo = false;
            sceneManagerNode.SetScene(SceneManagerNode.Scenes.VideoCheck);
            SoundManager.PlayEnableVideo();
        }

        private void OnTabChange(string tab, Node s)
        {
            ControlButtons.UpdateControlButtons(); 
            if (ApplicationProfileSettings.Instance.AutoHideShowPilotList)
            {
                if (TabbedMultiNode.IsOnLive)
                {
                    ShowPilotList(!EventManager.RaceManager.HasPilots);
                }
                else if (TabbedMultiNode.IsOnPhotoBooth || TabbedMultiNode.IsOnRounds)
                {
                    ShowPilotList(true);
                }
                else
                { 
                    ShowPilotList(false);
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
            // for fullscreen scene, just keep previous layout.
            if (sceneManagerNode.Scene == SceneManagerNode.Scenes.Fullscreen)
                return;

            // ignoring tabs 
            float topBarBottom = 0.1f;

            if (TabbedMultiNode != null)
            {
                float topBarHeight = topBar.RelativeBounds.Height;

                if (TabbedMultiNode.Showing == sceneManagerNode && sceneManagerNode.Scene == SceneManagerNode.Scenes.Race)
                {
                    // shrunken
                    topBarBottom = topBarBottom / 2;

                    topBar.LogoOnBottomLine(true);
                    topBar.RelativeBounds = new RectangleF(0, -topBarBottom, 1, topBarHeight);
                }
                else
                {
                    // including tabs
                    topBarBottom = topBarHeight;
                    topBar.LogoOnBottomLine(false);
                    topBar.RelativeBounds = new RectangleF(0, 0, 1, topBarHeight);
                }
            }

            bool crop = centralAspectNode.KeepAspectRatio;
            if (!crop)
            {
                rightBar.RelativeBounds = new RectangleF(1 - rightBarWidth, topBarBottom, rightBarWidth, 1 - topBarBottom);
            }

            mainContainer.RelativeBounds = new RectangleF(0, topBarBottom, 1, 1 - topBarBottom);
        }

        private bool LowDiskSpace()
        {
            long lowSpace = 1024 * 1024 * 1024; //1gb

            DriveInfo drive = new DriveInfo(Directory.GetCurrentDirectory());
            try
            {
                if (drive.AvailableFreeSpace < lowSpace)
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
            return false;
        }
    }
}
