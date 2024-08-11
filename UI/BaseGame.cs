using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RaceLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Timing;
using Timing.ImmersionRC;
using Tools;
using UI;
using UI.Nodes;
using UI.Nodes.Rounds;
using UI.Sponsor;
using UI.Video;
using Webb;

namespace UI
{
    public class BaseGame : LayerStackGameBackgroundThread
    {
        protected EventLayer eventLayer;

        protected LoadingLayer loadingLayer;

        private Mutex mutex;

        private bool hasEverShownEventSelector;

        public Texture2D Banner { get; private set; }

        public WorkQueue Background { get; private set; }


        public DirectoryInfo Log { get; private set; }
        public DirectoryInfo Data { get; private set; }
        public DirectoryInfo Themes { get; private set; }
        public DirectoryInfo Video { get; private set; }
        public DirectoryInfo Sponsors { get; private set; }
        public DirectoryInfo Pilots { get; private set; }
        public DirectoryInfo Patreons { get; private set; }
        public DirectoryInfo HTTPFiles { get; private set; }

        public Tools.Profile Profile { get; private set; }

        public BaseGame(PlatformTools platformTools)
            :base(platformTools)
        {
            IOTools.WorkingDirectory = platformTools.WorkingDirectory;
            
            Background = new WorkQueue("Game Background");

            hasEverShownEventSelector = false;
            mutex = new Mutex(false, "FPVTrackside - uewepuep");

            Log = CreateDirectory(platformTools.WorkingDirectory, "log");
            Data = CreateDirectory(platformTools.WorkingDirectory, "data");
            Themes = CreateDirectory(platformTools.WorkingDirectory, "themes");
            Video = CreateDirectory(platformTools.WorkingDirectory, "video");
            Sponsors = CreateDirectory(platformTools.WorkingDirectory, "sponsors");
            Pilots = CreateDirectory(platformTools.WorkingDirectory, "pilots");
            Patreons = CreateDirectory(platformTools.WorkingDirectory, "patreons");
            HTTPFiles = CreateDirectory(platformTools.WorkingDirectory, "httpfiles");

            Content.RootDirectory = "Content";
            IsFixedTimeStep = false;
            this.Window.Title = Assembly.GetEntryAssembly().GetName().Name + " - " + Assembly.GetEntryAssembly().GetName().Version;
            
            Logger.Init(Log);


            FileInfo[] oldConfigs = Data.GetFiles("*.xml").Where(r => !r.Name.EndsWith("GeneralSettings.xml")).ToArray();
            if (oldConfigs.Any())
            {
                if (Data.GetDirectories().Any(d => d.Name == "default"))
                {
                    foreach (FileInfo xml in oldConfigs) 
                    { 
                        string newName = Path.Combine(Data.FullName, "default",xml.Name);

                        if (!File.Exists(newName))
                        {
                            xml.MoveTo(newName);
                        }
                        else
                        {
                            xml.Delete();
                        }
                    }
                }
            }
        }

        private DirectoryInfo CreateDirectory(DirectoryInfo working, string directory)
        {
            string fullPath = Path.Combine(working.FullName, directory);
            DirectoryInfo di = new DirectoryInfo(fullPath);
            if (!di.Exists)
            {
                di.Create();
            }
            return di;
        }


        protected override void Dispose(bool disposing)
        {
            Logger.UI.Log(this, this.ToString(), "Dispose");

            Background.Dispose();
            
            base.Dispose(disposing);
            Tools.Logger.CleanUp();
        }

        protected override void Initialize()
        {
            GeneralSettings.Initialise();

            Profile = new Profile(GeneralSettings.Instance.Profile);

            ApplicationProfileSettings.Initialize(Profile);

            if (!ApplicationProfileSettings.Instance.UseDirectX9)
            {
                GraphicsDeviceManager.GraphicsProfile = GraphicsProfile.HiDef;
                GraphicsDeviceManager.ApplyChanges();
            }
            base.Initialize();
        }

        protected override void LoadContent()
        {
            Logger.UI.Log(this, this.Window.Title, "LoadContent");

            base.LoadContent();

            float inversScale = ApplicationProfileSettings.Instance.InverseResolutionScalePercent / 100.0f;
            if (inversScale > 0)
            {
                LayerStack.Scale = 1 / inversScale;
            }

            ClearColor = Theme.Current.Background.XNA;

            Banner = TextureHelper.GetEmbeddedTexture(GraphicsDevice, System.Reflection.Assembly.GetAssembly(typeof(BaseGame)), @"UI.img.banner.png");

            BackgroundLayer backgroundLayer = new BackgroundLayer(GraphicsDevice, Theme.Current.Background);
            backgroundLayer.BackgroundNode.Alignment = RectangleAlignment.TopLeft;
            LayerStack.Add(backgroundLayer);

            loadingLayer = new LoadingLayer(GraphicsDevice);
            loadingLayer.BlockOnLoading = true;

            PopupLayer popupLayer = new PopupLayer(GraphicsDevice);
            MenuLayer menuLayer = new MenuLayer(GraphicsDevice, Theme.Current.MenuBackground.XNA, Theme.Current.Hover.XNA, Theme.Current.MenuText.XNA, Theme.Current.MenuTextInactive.XNA, Theme.Current.ScrollBar.XNA);
            DragLayer dragLayer = new DragLayer(GraphicsDevice);

            LayerStack.Add(popupLayer);

            LayerStack.Add(menuLayer);
            LayerStack.Add(dragLayer);
            LayerStack.Add(loadingLayer);

            LayerStack.Add(new SponsorLayer(GraphicsDevice));
#if DEBUG
            LayerStack.Add(new TestLayer(GraphicsDevice, popupLayer));
#endif
            bool waitingOnMutex;
            try
            {
                waitingOnMutex = !mutex.WaitOne(TimeSpan.Zero);
            }
            catch
            {
                waitingOnMutex = true;
            }

            if (waitingOnMutex)
            {
                AlreadyRunningLayer alreadyRunning = new AlreadyRunningLayer(GraphicsDevice, mutex);
                LayerStack.Add(alreadyRunning);
            }

            int frameRate = Math.Min(1000, Math.Max(1, ApplicationProfileSettings.Instance.FrameRateLimit));
            TargetElapsedTime = TimeSpan.FromSeconds(1f / frameRate);
            IsFixedTimeStep = true;
            GraphicsDeviceManager.SynchronizeWithVerticalRetrace = ApplicationProfileSettings.Instance.VSync;
            GraphicsDeviceManager.ApplyChanges();


            loadingLayer.WorkQueue.Enqueue("Database Upgrade", DatabaseUpgrade);

            loadingLayer.WorkQueue.Enqueue("Startup", Startup);
        }

        private void DatabaseUpgrade()
        {
            using (IDatabase db = DatabaseFactory.Open(Guid.Empty))
            {
                Logger.AllLog.LogCall(this, db.Version);
            }
        }

        public virtual void Startup()
        {
            if (ApplicationProfileSettings.Instance.ShowWelcomeScreen && !hasEverShownEventSelector)
            {
                ShowWelcomeSetup();
            }
            else
            {
                ShowEventSelector();
            }
        }

        protected override void UnloadContent()
        {
            if (LayerStack.GetLayer<AlreadyRunningLayer>() == null)
            {
                mutex.ReleaseMutex();
            }
            base.UnloadContent();
        }

        public virtual void ShowEventSelector()
        {
            EventSelectorLayer eventSelector = LayerStack.GetLayer<EventSelectorLayer>();
            if (eventSelector != null)
            {
                eventSelector.Dispose();
            }

            Logger.UI.LogCall(this);
            hasEverShownEventSelector = true;
            eventSelector = new EventSelectorLayer(GraphicsDevice, CreateEventSelectorEditor());

            LayerStack.AddAbove<BackgroundLayer>(eventSelector);
            eventSelector.OnOK += EventSelected;
        }

        protected virtual EventSelectorEditor CreateEventSelectorEditor()
        {
            return new EventSelectorEditor(Banner, Profile);
        }

        public void ShowWelcomeSetup()
        {
            Logger.UI.LogCall(this);

            Profile profile = new Profile(GeneralSettings.Instance.Profile);

            CompositorLayer welcomeLayer = new CompositorLayer(GraphicsDevice);
            WelcomeSetupNode welcomeSetupNode = new WelcomeSetupNode(Banner, profile);
            welcomeSetupNode.OnOK += () =>
            {
                if (welcomeLayer != null)
                {
                    LayerStack.Remove(welcomeLayer);
                    welcomeLayer.Dispose();
                    welcomeLayer = null;
                }
                ShowEventSelector();
            };
            welcomeSetupNode.Restart += Restart;
            welcomeLayer.Root.AddChild(welcomeSetupNode);

            LayerStack.AddAbove<BackgroundLayer>(welcomeLayer);
        }

        public void EventSelected(BaseObjectEditorNode<Event> ed)
        {
            EventSelectorEditor editor = ed as EventSelectorEditor;

            Logger.UI.LogCall(this, editor.Selected);

            Event selected = editor.Selected;

            if (selected == null)
                selected = editor.Objects.First();

            StartEvent(selected, editor.Profile);
        }

        public void Restart(Event evvent)
        {
            Restart();

            if (evvent != null)
            {
                PlatformTools.Invoke(() =>
                {
                    if (loadingLayer != null)
                    {
                        loadingLayer.WorkQueue.Enqueue("Setting Event", () =>
                        {
                            EventSelectorLayer eventSelectorLayer = LayerStack.GetLayer<EventSelectorLayer>();
                            if (eventSelectorLayer != null)
                            {
                                eventSelectorLayer.Dispose();
                                StartEvent(evvent, eventSelectorLayer.Editor.Profile);
                            }
                        });
                    }
                });
            }
        }

        private void StartEvent(Event selected, Profile profile)
        {
            loadingLayer.BlockOnLoading = true;

            WorkSet startEventWorkSet = new WorkSet();
            startEventWorkSet.OnError += ErrorLoadingEvent;
            EventManager eventManager = new EventManager(profile);

            BackgroundLayer backgroundLayer = LayerStack.GetLayer<BackgroundLayer>();

            eventManager.LoadEvent(startEventWorkSet, loadingLayer.WorkQueue, selected);
            
            // Load races BEFORE sync. Sync systems need to assume this..
            eventManager.LoadRaces(startEventWorkSet, loadingLayer.WorkQueue, selected);

            OnStartEvent(eventManager, selected);

            loadingLayer.WorkQueue.Enqueue(startEventWorkSet, "Reloading Settings", () =>
            {
                // Re-init the following settings so settings windows can reload event to reload settings.
                ApplicationProfileSettings.Initialize(Profile);
                Theme.Initialise(PlatformTools.WorkingDirectory, "Dark");

                if (backgroundLayer != null)
                {
                    backgroundLayer.SetBackground(Theme.Current.Background);
                }
            });

            loadingLayer.WorkQueue.Enqueue(startEventWorkSet, "Initializing UI", () =>
            {
                CreateEventLayer(eventManager);
            });

            loadingLayer.WorkQueue.Enqueue(startEventWorkSet, "Initializing Video Inputs", () =>
            {
                try
                {
                    eventLayer?.LoadVideo();
                }
                catch (Exception ex)
                {
                    Logger.VideoLog.LogException(this, ex);
                    LayerStack.GetLayer<PopupLayer>().PopupMessage("Error loading Video: " + ex.Message);
                }
            });

            loadingLayer.WorkQueue.Enqueue(startEventWorkSet, "Setting Scene", () =>
            {
                if (eventLayer != null)
                {
                    eventLayer.FinalSetup();

                    while (eventLayer.Root.Alpha < 1)
                    {
                        Thread.Sleep(10);
                        eventLayer.Root.Alpha += 0.05f;
                    }

                    eventLayer.Root.Alpha = 1;
                }
            });
        }

        protected virtual void CreateEventLayer(EventManager eventManager)
        {
            eventLayer = new EventLayer(this, GraphicsDevice, eventManager, PlatformTools);

            eventLayer.Root.Alpha = 0;
            LayerStack.AddAbove<BackgroundLayer>(eventLayer);

            TestLayer testLayer = LayerStack.GetLayer<TestLayer>();
            if (testLayer != null)
            {
                testLayer.EventLayer = eventLayer;
            }
        }

        protected virtual void OnStartEvent(EventManager eventManager, Event selected)
        {

        }

        public void ErrorLoadingEvent(WorkItem arg1, Exception arg2)
        {
            if (eventLayer != null)
            {
                eventLayer.Dispose();
                eventLayer = null;
            }

            Logger.UI.LogException(this, arg2);

            LayerStack.GetLayer<PopupLayer>().PopupMessage("Error loading event. Please check log files.", () =>
            {
                if (LayerStack.Game is UI.BaseGame)
                {
                    ((UI.BaseGame)LayerStack.Game).Restart(null);
                }
            });
        }

        protected override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (LayerStack != null && LayerStack.InputEventFactory != null)
            {
                TimeSpan sinceMouseMove = DateTime.Now - LayerStack.InputEventFactory.LastMouseUpdateTime;
                IsMouseVisible = sinceMouseMove.TotalSeconds < 5;
            }
        }

        public void ShowNewWindow(Node node)
        {
            PlatformTools.ShowNewWindow(node);
        }

        public void QuickLaunchWindow<T>(EventManager eventManager, KeyboardShortcuts keyMapper)
        {
            if (typeof(T) == typeof(LapRecordsSummaryNode))
            {
                var n = new LapRecordsSummaryNode(eventManager);
                ShowNewWindow(n);
            }

            if (typeof(T) == typeof(PointsSummaryNode))
            {
                var n = new PointsSummaryNode(eventManager);
                n.Refresh();
                ShowNewWindow(n);
            }

            if (typeof(T) == typeof(LapCountSummaryNode))
            {
                var n = new LapCountSummaryNode(eventManager);
                n.Refresh();
                ShowNewWindow(n);
            }

            if (typeof(T) == typeof(PilotChanelList))
            {
                var n = new PilotChanelList(eventManager);
                ShowNewWindow(n);
            }

            if (typeof(T) == typeof(RoundsNode))
            {
                var n = new RoundsNode(eventManager);
                ShowNewWindow(n);
            }

            if (typeof(T) == typeof(ReplayNode))
            {
                var n = new ReplayNode(eventManager, keyMapper);
                n.ReplayRace(eventManager.RaceManager.CurrentRace);
                ShowNewWindow(n);
            }

            if (typeof(T) == typeof(LogNode))
            {
                var n = new LogNode(Logger.AllLog);
                ShowNewWindow(n);
            }

            if (typeof(T) == typeof(EventStatusNodeTopBar))
            {
                var n = new EventStatusNodeTopBar(eventManager);
                ShowNewWindow(n);
            }
        }
    }
}
