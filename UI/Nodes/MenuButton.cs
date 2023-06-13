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

namespace UI.Nodes
{
    public class MenuButton : ImageButtonNode
    {
        private EventManager eventManager;
        private VideoManager videoManager;
        private SoundManager soundManager;
        private EventWebServer eventWebServer;
        private TimingSystemManager timingSystemManager;

        private TracksideTabbedMultiNode tabbedMultiNode;

        public event System.Action BackToEventSelector;
        public event Action<Event> Restart;
        public event System.Action ChannelsChanged;
        public event System.Action<bool> VideoSettingsExited;
        public event System.Action TimingChanged;
        public event System.Action EventChanged;
        public event System.Action DataDeleted;
        public event System.Action BugReport;

        public event System.Action OBSRemoteSettingsSaved;
        public event System.Action GeneralSettingsSaved;

        private Event evennt;
        private bool hasEvent;

        public MenuButton(EventManager eventManager, VideoManager videoManager, SoundManager soundManager, EventWebServer eventWebServer, TracksideTabbedMultiNode tabbedMultiNode, Color hover, Color tint) 
            : base(@"img\settings.png", Color.Transparent, hover, tint)
        {
            this.eventManager = eventManager;
            this.videoManager = videoManager;
            this.soundManager = soundManager;
            this.eventWebServer = eventWebServer;
            this.tabbedMultiNode = tabbedMultiNode;

            if (eventManager != null)
            {
                this.timingSystemManager = eventManager.RaceManager.TimingSystemManager;
            }

            OnClick += SettingsButton_OnClick;
            hasEvent = true;
            ImageNode.CanScale = false;
        }

        public MenuButton(Color hover, Color tint) 
            : this(null, null, null, null, null, hover, tint)
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
                foreach (var kvp in tabbedMultiNode.Tabs)
                {
                    TextButtonNode tbn = kvp.Key;
                    Node n = kvp.Value;
                    root.AddItem(tbn.Text, () =>
                    {
                        tbn.ButtonNode.Click(null);
                    });
                }
            }
            MouseMenu openWindow = root.AddSubmenu("Open New Window");

            root.AddBlank();

            root.AddItem("General Settings", () =>
            {
                ShowGeneralSettings();
            });

            if (hasEvent)
            {
                root.AddItem("Event Settings", () =>
                {
                    ShowEventSettings();
                });
            }

            root.AddItem("Channel Settings", () =>
            {
                ShowChannelSettings();
            });

            root.AddItem("Theme Settings", () =>
            {
                ShowThemeSettings();
            }, isNotRunningRace);

            root.AddItem("Keyboard Shortcuts", () =>
            {
                ShowKeyboardShortcuts();
            });

            root.AddItem("Points Settings", () =>
            {
                ShowPointsSettings();

            }, isNotRunningRace);

            root.AddItem("Sound Editor", () =>
            {
                ShowSoundsSettings();
            });

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

            root.AddItem("Export Column Settings", () =>
            {
                ShowExportSettings();
            });

            root.AddItem("OBS Remote Control Settings", () =>
            {
                ShowOBSRemoteControlSettings();
            });

            openWindow.AddItem("Log", () =>
            {
                BaseGame baseGame = CompositorLayer.Game as BaseGame;
                baseGame.QuickLaunchWindow<LogNode>(eventManager);
            });

            if (hasEvent)
            {
                openWindow.AddItem("Rounds", () =>
                {
                    BaseGame baseGame = CompositorLayer.Game as BaseGame;
                    baseGame.QuickLaunchWindow<RoundsNode>(eventManager);
                });

                openWindow.AddItem("Lap Records", () =>
                {
                    BaseGame baseGame = CompositorLayer.Game as BaseGame;
                    baseGame.QuickLaunchWindow<LapRecordsSummaryNode>(eventManager);
                });

                openWindow.AddItem("Points", () =>
                {
                    BaseGame baseGame = CompositorLayer.Game as BaseGame;
                    baseGame.QuickLaunchWindow<PointsSummaryNode>(eventManager);
                });

                openWindow.AddItem("Lap Count", () =>
                {
                    BaseGame baseGame = CompositorLayer.Game as BaseGame;
                    baseGame.QuickLaunchWindow<LapCountSummaryNode>(eventManager);
                });

                openWindow.AddItem("Channel List", () =>
                {
                    BaseGame baseGame = CompositorLayer.Game as BaseGame;
                    baseGame.QuickLaunchWindow<PilotChanelList>(eventManager);
                });

                openWindow.AddItem("Event Status", () =>
                {
                    BaseGame baseGame = CompositorLayer.Game as BaseGame;
                    baseGame.QuickLaunchWindow<EventStatusNodeTopBar>(eventManager);
                });

                root.AddBlank();

                MouseMenu export = root.AddSubmenu("Export");
                export.AddItem("Export PBs CSV", () =>
                {
                    ExportPBsCSV();
                });

                export.AddItem("Export Raw Laps CSV", () =>
                {
                    ExportLapsCSV();
                });

                export.AddItem("Export Race Results CSV", () =>
                {
                    ExportRacesCSV();
                });


                MouseMenu delete = root.AddSubmenu("Delete from event");

                delete.AddItemConfirm("Delete all races", () =>
                {
                    DeleteAllRaceData();
                });

                delete.AddItemConfirm("Delete all pilots", () =>
                {
                    RemoveAllPilots();
                });

                if (eventWebServer != null)
                {
                    root.AddItem("Open Local Webserver", () =>
                    {
                        OpenWebServer();
                    });
                }
            }
            root.AddBlank();

            root.AddItem("Open FPVTrackside Directory", () =>
            {
                OpenDirectory();
            });

            root.AddBlank();

            root.AddItem("About", () =>
            {
                About();
            });

            root.Show(this);
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
                channels = Channel.Read();
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
                    Channel.Write(channels);
                }

                if (eventManager != null)
                {
                    using (Database db = new Database())
                    {
                        eventManager.Event.Channels = channels;
                        db.Events.Update(eventManager.Event);
                    }
                    eventManager.SetChannelColors(Theme.Current.ChannelColors.XNA());
                }

                ChannelsChanged?.Invoke();
            };
        }

        public void ShowVideoSettings()
        {
            videoManager?.StopDevices();

            VideoSourceEditor editor = VideoSourceEditor.GetVideoSourceEditor(CompositorLayer.GraphicsDevice, eventManager);
            GetLayer<PopupLayer>().Popup(editor);

            editor.OnOK += (e) =>
            {
                List<VideoConfig> sources = editor.Objects.ToList();
                VideoManager.WriteDeviceConfig(sources);

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
                settings = PointsSettings.Read();
            }

            ObjectEditorNode<PointsSettings> editor = new ObjectEditorNode<PointsSettings>(settings, false, true, false);
            editor.OnOK += (a) =>
            {
                settings = a.Objects.FirstOrDefault();
                PointsSettings.Write(settings);
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
                timingSystemManager = new TimingSystemManager();
            }

            TimingSystemSettings[] timingSystemSettings = TimingSystemSettings.Read();

            TimingSystemEditor editor = new TimingSystemEditor(timingSystemSettings);
            editor.OnOK += (a) =>
            {
                timingSystemManager.TimingSystemsSettings = a.Objects.ToArray();
                TimingSystemSettings.Write(timingSystemManager.TimingSystemsSettings);
                timingSystemManager.InitialiseTimingSystems();

                TimingChanged?.Invoke();
            };
            GetLayer<PopupLayer>().Popup(editor);
        }

        public void ShowSoundsSettings()
        {
            if (soundManager == null)
            {
                soundManager = new SoundManager(null);
                soundManager.SetupSpeaker(PlatformTools, GeneralSettings.Instance.Voice, GeneralSettings.Instance.TextToSpeechVolume);
                soundManager.WaitForInit();
            }
            GetLayer<PopupLayer>().Popup(new SoundEditor(soundManager));
        }

        public void ShowExportSettings()
        {
            GetLayer<PopupLayer>().Popup(new ExportColumnEditor(eventManager));
        }

        public void ShowOBSRemoteControlSettings()
        {
            OBSRemoteControlManager.OBSRemoteControlConfig config = OBSRemoteControlManager.OBSRemoteControlConfig.Load();

            var editor = new OBSRemoteControlEditor(config);
            editor.OnOK += (e) =>
            {
                config.RemoteControlEvents = editor.Objects.ToList();
                OBSRemoteControlManager.OBSRemoteControlConfig.Write(config);

                OBSRemoteSettingsSaved?.Invoke();
            };

            GetLayer<PopupLayer>().Popup(editor);
        }

        public void ShowEventSettings()
        {
            ObjectEditorNode<Event> editor = new ObjectEditorNode<Event>(eventManager.Event, false, false);
            GetLayer<PopupLayer>().Popup(editor);

            editor.OnOK += (e) =>
            {
                eventManager.Event = editor.Objects.FirstOrDefault();
                using (Database db = new Database())
                {
                    db.Events.Update(eventManager.Event);
                }

                EventChanged?.Invoke();
            };
        }

        public void ShowGeneralSettings()
        {
            GeneralSettingsEditor editor = new GeneralSettingsEditor(GeneralSettings.Instance);
            GetLayer<PopupLayer>().Popup(editor);

            editor.OnOK += (e) =>
            {
                GeneralSettings.Write();
                if (hasEvent && Restart != null && editor.NeedsRestart)
                {
                    GetLayer<PopupLayer>().PopupConfirmation("Changes require restart to take effect. Restart now?", () => { Restart(evennt); });
                }

                GeneralSettingsSaved?.Invoke();
            };
        }

        public void ShowThemeSettings()
        {
            ThemeSettingsEditor editor = new ThemeSettingsEditor(Theme.Themes);
            editor.Selected = Theme.Current;
            GetLayer<PopupLayer>().Popup(editor);

            editor.OnOK += (e) =>
            {
                Restart?.Invoke(evennt);
            };
        }

        public void ShowKeyboardShortcuts()
        {
            var k = KeyboardShortcuts.Read();

            KeyboardShortcutsEditor editor = new KeyboardShortcutsEditor(k);
            editor.Selected = k;
            GetLayer<PopupLayer>().Popup(editor);

            editor.OnOK += (e) =>
            {
                KeyboardShortcuts.Write(k);
                Restart?.Invoke(evennt);
            };
        }

        public void About()
        {
            AboutNode aboutNode = new AboutNode();
            GetLayer<PopupLayer>().Popup(aboutNode);
            RequestLayout();
        }

        public void OpenDirectory()
        {
            PlatformTools.OpenFileManager(System.IO.Directory.GetCurrentDirectory());
        }

        public void OpenWebServer()
        {
            if (eventWebServer != null)
            {
                DataTools.StartBrowser(eventWebServer.Url);
            }
        }

        public void ExportPBsCSV()
        {
            PlatformTools.ExportCSV("Save Top Consecutive Laps CSV", eventManager.LapRecordManager.ExportPBsCSV(), GetLayer<PopupLayer>());
        }

        public void ExportRacesCSV()
        {
            PlatformTools.ExportCSV("Save Race Results CSV", eventManager.RaceManager.GetRaceResultsText(","), GetLayer<PopupLayer>());
        }

        public void ExportLapsCSV()
        {
            PlatformTools.ExportCSV("Save Top Consecutive Laps CSV", eventManager.RaceManager.GetRawLaps(), GetLayer<PopupLayer>());
        }

        public void DeleteAllRaceData()
        {
            eventManager.RaceManager.DeleteRaces();
            eventManager.LapRecordManager.Clear();

            DataDeleted?.Invoke();
        }

        public void RemoveAllPilots()
        {
            eventManager.RemovePilots();

            DataDeleted?.Invoke();
        }

    }
}
