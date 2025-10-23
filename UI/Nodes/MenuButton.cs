using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using RaceLib;
using Sound;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timing;
using Webb;
using Tools;
using UI.Video;
using Composition;
using ExternalData;
using UI.Nodes.Rounds;
using RaceLib.Game;

namespace UI.Nodes
{
    public class MenuButton : ImageButtonNode
    {
        private EventManager eventManager;
        private VideoManager videoManager;
        private SoundManager soundManager;
        private EventWebServer eventWebServer;
        private TimingSystemManager timingSystemManager;
        private KeyboardShortcuts keyMapper;

        private TracksideTabbedMultiNode tabbedMultiNode;

        public event Action BackToEventSelector;
        public event Action EventEditor;
        public event Action<Event> Restart;
        public event Action ChannelsChanged;
        public event Action<bool> VideoSettingsExited;
        public event Action TimingChanged;
        public event Action DataDeleted;
        public event Action BugReport;

        public event Action OBSRemoteConfigSaved;
        public event Action AutoRunnerConfigsSaved;
        public event Action GeneralSettingsSaved;
        public event Action ProfileSettingsSaved;

        public event Action OpenFPVTracksideSite;
        public event Action OpenMultiGPSite;


        private Event evennt;
        private bool hasEvent;

        public Profile Profile { get; set; }

        public MenuButton(Profile profile, EventManager eventManager, VideoManager videoManager, SoundManager soundManager, EventWebServer eventWebServer, TracksideTabbedMultiNode tabbedMultiNode, KeyboardShortcuts keyMapper, Color hover, Color tint) 
            : base(@"img\settings.png", Color.Transparent, hover, tint)
        {
            Profile = profile;

            this.eventManager = eventManager;
            this.videoManager = videoManager;
            this.soundManager = soundManager;
            this.eventWebServer = eventWebServer;
            this.tabbedMultiNode = tabbedMultiNode;
            this.keyMapper = keyMapper;

            if (eventManager != null)
            {
                timingSystemManager = eventManager.RaceManager.TimingSystemManager;
                Profile = eventManager.Profile;
            }

            OnClick += SettingsButton_OnClick;
            hasEvent = true;
            ImageNode.CanScale = false;
        }

        public MenuButton(Profile profile, Color hover, Color tint) 
            : this(profile, null, null, null, null, null, null, hover, tint)
        {
            hasEvent = false;
        }

        public override void Dispose()
        {
            base.Dispose();

            if (!hasEvent)
            {
                if (videoManager != null)
                    videoManager.Dispose();

                if (soundManager != null)
                    soundManager.Dispose();

                if (timingSystemManager != null)
                    timingSystemManager.Dispose();
            }
        }

        private void SettingsButton_OnClick(Composition.Input.MouseInputEvent mouseInputEvent)
        {
            bool isEmpty = true;
            bool isNotRunningRace = true;
            evennt = null;
            if (hasEvent)
            {
                evennt = eventManager.Event;
            }

            if (hasEvent)
            {
                isEmpty = eventManager.RaceManager.PilotCount == 0;
                isNotRunningRace = !eventManager.RaceManager.RaceRunning;
            }

            MouseMenu root = new MouseMenu(this);
            root.LeftToRight = false;

            if (BackToEventSelector != null)
            {
                root.AddItem("Back to Event Selection", () =>
                {
                    BackToEventSelector?.Invoke();
                });

                root.AddBlank();
            }

            root.AddItem("Online Manual", () =>
            {
                ShowOnlineManual();
            });

            if (BugReport != null)
            root.AddItem("Bug Report", () =>
            {
                BugReport();
            });

            
            if (tabbedMultiNode != null)
            {
                root.AddBlank();
                MouseMenu tabs = root.AddSubmenu("Open Tab");

                foreach (var kvp in tabbedMultiNode.Tabs)
                {
                    TextButtonNode tbn = kvp.Key;
                    Node n = kvp.Value;
                    tabs.AddItem(tbn.Text, () =>
                    {
                        tbn.ButtonNode.Click(null);
                    });
                }
            }

            bool hasLocalWebServer = eventWebServer != null;
            if (hasLocalWebServer || OpenFPVTracksideSite != null || OpenMultiGPSite != null)
            {
                MouseMenu webMenu = root.AddSubmenu("Open Web");
                if (eventWebServer != null)
                    webMenu.AddItem("Open Local Web page", OpenWebServer);
                if (OpenFPVTracksideSite != null)
                    webMenu.AddItem("Open FPVTrackside.com event page", () => { OpenFPVTracksideSite(); });
                if (OpenMultiGPSite != null)
                    webMenu.AddItem("Open MultiGP.com event page", () => { OpenMultiGPSite(); });
            }

            MouseMenu openWindow = root.AddSubmenu("Open New Window");
            root.AddBlank();

            root.AddItem("Application Settings", () =>
            {
                ShowSettingsEditor();
            });

            root.AddItem("Auto Runner Settings", () =>
            {
                ShowAutoRunnerSettings();
            });

            root.AddItem("Channel Settings", () =>
            {
                ShowChannelSettings();
            });

            root.AddItem("Export Column Settings", () =>
            {
                ShowExportSettings();
            });

            if (hasEvent)
            {
                root.AddItem("Event Settings", () =>
                {
                    EventEditor?.Invoke();
                });
            }
            root.AddItem("Game Settings", () =>
            {
                ShowGameTypeEditor();
            });

            root.AddItem("Keyboard Shortcuts", () =>
            {
                ShowKeyboardShortcuts();
            });


            root.AddItem("OBS Remote Control Settings", () =>
            {
                ShowOBSRemoteControlSettings();
            });

            root.AddItem("Points Settings", () =>
            {
                ShowPointsSettings();

            }, isNotRunningRace);

           
            root.AddItem("Sound Editor", () =>
            {
                ShowSoundsSettings();
            });

            root.AddItem("Theme Settings", () =>
            {
                ShowThemeSettings();
            }, isNotRunningRace);


            root.AddItem("Timing Settings", () =>
            {
                ShowTimingSettings();

            }, isNotRunningRace);

            if (PlatformTools.HasFeature(PlatformFeature.Video))
            {
                root.AddItem("Video Input Settings", () =>
                {
                    ShowVideoSettings();
                });
            }

            openWindow.AddItem("Log", () =>
            {
                BaseGame baseGame = CompositorLayer.Game as BaseGame;
                baseGame.QuickLaunchWindow<LogNode>(eventManager, keyMapper);
            });

            if (hasEvent)
            {
                root.AddBlank();

                root.AddItem("YouTube Chapters", () =>
                {
                    ShowChapterMarkerDialog();
                });

                MouseMenu export = root.AddSubmenu("Export");

                FileTools.ExportMenu(export, "Export PBs", PlatformTools, "Save Top Consecutive Laps", eventManager.LapRecordManager.ExportPBs(), GetLayer<PopupLayer>());
                FileTools.ExportMenu(export, "Export Raw Laps", PlatformTools, "Save Top Consecutive Laps", eventManager.RaceManager.GetRawLaps(), GetLayer<PopupLayer>());
                FileTools.ExportMenu(export, "Export Race Results", PlatformTools, "Save Race Results", eventManager.RaceManager.GetRaceResultsText(ApplicationProfileSettings.Instance.Units), GetLayer<PopupLayer>());

                MouseMenu delete = root.AddSubmenu("Delete from event");

                delete.AddItemConfirm("Delete all rounds & races", () =>
                {
                    DeleteAllRaceData();
                });

                delete.AddItemConfirm("Delete all pilots", () =>
                {
                    RemoveAllPilots();
                });

                root.AddItem("Refresh all race data", () =>
                {
                    LoadingLayer ll = GetLayer<LoadingLayer>();
                    if (ll == null)
                        return;

                    WorkSet workSet = new WorkSet();
                    eventManager.UnloadRaces(workSet, ll.WorkQueue);
                    eventManager.LoadRaces(workSet, ll.WorkQueue);
                });
            }
            root.AddBlank();

            MouseMenu openDirectory = root.AddSubmenu("Open Directory");

            if (hasEvent)
            {
                openDirectory.AddItem("Open Event Data Directory", () =>
                {
                    // On macOS, use WorkingDirectory (Application Support). On Windows, use EventStorageLocation as-is.
                    string eventPath = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)
                        ? Path.Combine(PlatformTools.WorkingDirectory.FullName, ApplicationProfileSettings.Instance.EventStorageLocation, eventManager.EventId.ToString())
                        : Path.Combine(ApplicationProfileSettings.Instance.EventStorageLocation, eventManager.EventId.ToString());
                    PlatformTools.OpenFileManager(eventPath);
                });
            }

            openDirectory.AddItem("Open Events Directory", () =>
            {
                // On macOS, use WorkingDirectory (Application Support). On Windows, use EventStorageLocation as-is.
                string eventsPath = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)
                    ? Path.Combine(PlatformTools.WorkingDirectory.FullName, ApplicationProfileSettings.Instance.EventStorageLocation)
                    : ApplicationProfileSettings.Instance.EventStorageLocation;
                PlatformTools.OpenFileManager(eventsPath);
            });

            openDirectory.AddItem("Open Pilot Profile Image Directory", () =>
            {
                // On macOS: uses Application Support, or custom absolute path if EventStorageLocation is absolute
                // On Windows: uses current directory
                string pilotsPath = Path.Combine(IOTools.GetBaseDirectory().FullName, "pilots");
                PlatformTools.OpenFileManager(pilotsPath);
            });

            openDirectory.AddItem("Open Tracks Directory", () =>
            {
                // On macOS: uses Application Support, or custom absolute path if EventStorageLocation is absolute
                // On Windows: uses current directory
                string tracksPath = Path.Combine(IOTools.GetBaseDirectory().FullName, "Tracks");
                PlatformTools.OpenFileManager(tracksPath);
            });

            openDirectory.AddItem("Open FPVTrackside Directory", () =>
            {
                // Opens the base directory (Application Support on macOS, or custom location if set)
                PlatformTools.OpenFileManager(IOTools.GetBaseDirectory().FullName);
            });

            AddMenus(root);

            root.Show(this);
        }

        protected virtual void AddMenus(MouseMenu root)
        {

        }

        public void ShowOnlineManual()
        {
            try
            {
                DataTools.StartBrowser(@"https://docs.google.com/document/d/1ysdQD3JdPvdTsNZR1Q_jh6voXrC2lJlIUAcL1TOb670");
            }
            catch
            {
            }
        }

        public void ShowChannelSettings()
        {
            Channel[] channels;
            if (eventManager == null)
            {
                channels = Channel.Read(Profile);
            }
            else
            {
                channels = eventManager.Channels;
            }

            ChannelEditor editor = new ChannelEditor(channels, eventManager != null);
            GetLayer<PopupLayer>().Popup(editor);

            editor.OnOK += (e) =>
            {
                channels = editor.Objects.OrderBy(r => r.Frequency).ThenBy(r => r.Band).ToArray();

                if (editor.SaveDefault)
                {
                    Channel.Write(Profile, channels);
                }

                if (eventManager != null)
                {
                    using (IDatabase db = DatabaseFactory.Open(eventManager.EventId))
                    {
                        eventManager.Event.Channels = channels;
                        db.Update(eventManager.Event);
                    }
                    eventManager.SetChannelColors(Theme.Current.ChannelColors.XNA());
                }

                ChannelsChanged?.Invoke();
            };
        }

        public void ShowVideoSettings()
        {
            videoManager?.StopDevices();


            VideoSourceEditor editor = VideoSourceEditor.GetVideoSourceEditor(eventManager, Profile);
            GetLayer<PopupLayer>().Popup(editor);

            editor.OnOK += (e) =>
            {
                List<VideoConfig> sources = editor.Objects.ToList();
                VideoManager.WriteDeviceConfig(Profile, sources);

                VideoSettingsExited?.Invoke(true);
            };

            editor.OnCancel += (e) =>
            {
                VideoSettingsExited?.Invoke(false);
            };
        }

        public void ShowPointsSettings()
        {
            PointsSettings settings = null;

            if (eventManager != null)
            {
                settings = eventManager.ResultManager.PointsSettings;
            }
            else
            {
                settings = PointsSettings.Read(Profile);
            }

            ObjectEditorNode<PointsSettings> editor = new ObjectEditorNode<PointsSettings>(settings, false, true, false);
            editor.OnOK += (a) =>
            {
                settings = a.Objects.FirstOrDefault();
                PointsSettings.Write(Profile, settings);
                if (eventManager != null)
                {
                    eventManager.ResultManager.PointsSettings = settings;
                }
            };
            GetLayer<PopupLayer>().Popup(editor);
        }

        public void ShowTimingSettings()
        {
            if (timingSystemManager == null)
            {
                timingSystemManager = new TimingSystemManager(Profile);
            }

            TimingSystemSettings[] timingSystemSettings = TimingSystemSettings.Read(Profile);

            TimingSystemEditor editor = new TimingSystemEditor(timingSystemSettings);
            editor.OnOK += (a) =>
            {
                timingSystemManager.TimingSystemsSettings = a.Objects.ToArray();
                TimingSystemSettings.Write(Profile, timingSystemManager.TimingSystemsSettings);
                timingSystemManager.InitialiseTimingSystems(Profile);

                TimingChanged?.Invoke();
            };
            GetLayer<PopupLayer>().Popup(editor);
        }

        public void ShowSoundsSettings()
        {
            if (soundManager == null)
            {
                ApplicationProfileSettings profileSettings = ApplicationProfileSettings.Read(Profile);

                soundManager = new SoundManager(null, Profile);
                soundManager.SetupSpeaker(PlatformTools, profileSettings.Voice, profileSettings.TextToSpeechVolume);
                soundManager.WaitForInit();
            }

            SoundEditor soundEditor = new SoundEditor(soundManager);
            soundEditor.OnOK += (a) =>
            {
                if (soundManager != null)
                {
                    soundManager.WriteSettings();
                }
            };

            GetLayer<PopupLayer>().Popup(soundEditor);
        }

        public void ShowExportSettings()
        {
            GetLayer<PopupLayer>().Popup(new ExportColumnEditor(eventManager, Profile));
        }

        public void ShowOBSRemoteControlSettings()
        {
            OBSRemoteControlManager.OBSRemoteControlConfig config = OBSRemoteControlManager.OBSRemoteControlConfig.Load(Profile);

            var editor = new OBSRemoteControlEditor(config);
            editor.OnOK += (e) =>
            {
                config.RemoteControlEvents = editor.Objects.ToList();
                OBSRemoteControlManager.OBSRemoteControlConfig.Write(Profile, config);

                OBSRemoteConfigSaved?.Invoke();
            };

            GetLayer<PopupLayer>().Popup(editor);
        }
        public void ShowAutoRunnerSettings()
        {
            AutoRunnerConfig config = AutoRunnerConfig.Load(Profile);

            AutoRunnerConfigEditor editor = new AutoRunnerConfigEditor(config);
            editor.OnOK += (e) =>
            {
                AutoRunnerConfig.Write(Profile, config);
                AutoRunnerConfigsSaved?.Invoke();
            };

            GetLayer<PopupLayer>().Popup(editor);
        }

        public void ShowSettingsEditor()
        {
            ApplicationProfileSettings profileSettings = ApplicationProfileSettings.Read(Profile);

            SettingsEditor editor = new SettingsEditor(profileSettings);
            GetLayer<PopupLayer>().Popup(editor);

            editor.OnOK += (e) =>
            {
                ApplicationProfileSettings.Write(Profile, profileSettings);
                if (hasEvent && Restart != null && editor.NeedsRestart)
                {
                    GetLayer<PopupLayer>().PopupConfirmation("Changes require restart to take effect. Restart now?", () => { Restart(evennt); });
                }

                ApplicationProfileSettings.Initialize(Profile);

                ProfileSettingsSaved?.Invoke();
            };
        }

        public void ShowThemeSettings()
        {
            ThemeEditor editor = new ThemeEditor(Profile, Theme.Themes);
            editor.Selected = Theme.Current;
            GetLayer<PopupLayer>().Popup(editor);

            editor.OnOK += (e) =>
            {
                Restart?.Invoke(evennt);
            };
        }

        public void ShowKeyboardShortcuts()
        {
            var k = KeyboardShortcuts.Read(Profile);

            KeyboardShortcutsEditor editor = new KeyboardShortcutsEditor(k);
            editor.Selected = k;
            GetLayer<PopupLayer>().Popup(editor);

            editor.OnOK += (e) =>
            {
                KeyboardShortcuts.Write(Profile, k);
                Restart?.Invoke(evennt);
            };
        }

        public void ShowGameTypeEditor()
        {
            GameType[] settings = GameType.Read(Profile);

            ObjectEditorNode<GameType> editor = new ObjectEditorNode<GameType>(settings, true, true, false);
            editor.OnOK += (a) =>
            {
                GameType.Write(Profile, editor.Objects.ToArray());
                GetLayer<PopupLayer>().PopupConfirmation("Changes require restart to take effect. Restart now?", () => { Restart(evennt); });
            };
            GetLayer<PopupLayer>().Popup(editor);
        }

        public void OpenCurrentDirectory(string addition = "")
        {
            string path = Directory.GetCurrentDirectory();
            if (!string.IsNullOrEmpty(addition))
            {
                path = Path.Combine(path, addition);
            }

            PlatformTools.OpenFileManager(path);
        }

        public void OpenWebServer()
        {
            if (eventWebServer != null)
            {
                if (ApplicationProfileSettings.Instance.HTTPServer == false)
                {
                    ApplicationProfileSettings.Instance.HTTPServer = true;
                }

                if (!eventWebServer.Running)
                {
                    eventWebServer.Start();
                }

                DataTools.StartBrowser(eventWebServer.Url);
            }
        }

        public void DeleteAllRaceData()
        {
            eventManager.RaceManager.DeleteRaces();
            eventManager.RoundManager.DeleteRounds();
            eventManager.LapRecordManager.Clear();

            DataDeleted?.Invoke();
        }

        public void RemoveAllPilots()
        {
            eventManager.RemovePilots();

            DataDeleted?.Invoke();
        }

        public void ShowChapterMarkerDialog()
        {
            GetLayer<PopupLayer>().Popup(new ChapterMarkerDialog(eventManager, PlatformTools));
        }

    }
}
