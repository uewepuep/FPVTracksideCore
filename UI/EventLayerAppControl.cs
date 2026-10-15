using Composition;
using Composition.Input;
using Composition.Nodes;
using ExternalData;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Tools;
using UI.Nodes;
using UI.Video;
using Webb;
using Webb.Api;

namespace UI
{
    // The window side of the REST API (Webb.IAppControl). Each member does what
    // the matching button, menu item or keyboard shortcut does; names, checks
    // and errors are handled in Webb/Api/AppApi.cs. Called on the UI thread.
    public class EventLayerAppControl : IAppControl
    {
        private readonly EventLayer layer;

        public EventLayerAppControl(EventLayer layer)
        {
            this.layer = layer;
        }

        // ───────────────────────── tabs and scenes ─────────────────────────

        public IEnumerable<AppTabInfo> Tabs
        {
            get
            {
                return layer.TabbedMultiNode.AllTabs.Select(kvp => new AppTabInfo { Name = kvp.Key.Text, Enabled = kvp.Key.Enabled, Visible = kvp.Key.Visible });
            }
        }

        public string CurrentTab
        {
            get
            {
                TracksideTabbedMultiNode tabs = layer.TabbedMultiNode;
                return tabs.AllTabs.Where(kvp => kvp.Value == tabs.Showing).Select(kvp => kvp.Key.Text).FirstOrDefault();
            }
        }

        public void ShowTab(string tab)
        {
            // The tab's own click handler, so it refreshes what it shows just as a click would.
            TextButtonNode button = layer.TabbedMultiNode.AllTabs.First(kvp => kvp.Key.Text == tab).Key;
            button.ButtonNode.Click(null);
        }

        public IEnumerable<string> Scenes { get { return Enum.GetNames(typeof(SceneManagerNode.Scenes)); } }

        public string CurrentScene { get { return layer.sceneManagerNode.Scene.ToString(); } }

        public void ShowLive()
        {
            layer.TabbedMultiNode.ShowLive();
        }

        public void ShowScene(string scene)
        {
            layer.TabbedMultiNode.ShowLive((SceneManagerNode.Scenes)Enum.Parse(typeof(SceneManagerNode.Scenes), scene));
        }

        public void ShowCommentators()
        {
            layer.TabbedMultiNode.ShowCommentators();
        }

        // ───────────────────────── view ─────────────────────────

        public AppView View
        {
            get
            {
                AutoRunner autoRunner = layer.AutoRunner;
                return new AppView
                {
                    PilotListVisible = layer.PilotListVisible,
                    WormVisible = layer.sceneManagerNode.WormVisible,
                    LapsVisible = layer.ChannelsGridNode.LapsVisible,
                    SubtitlesEnabled = layer.systemStatusNode?.Subtitles?.Value ?? false,
                    AutoRunnerPaused = autoRunner?.Paused ?? true,
                    AutoRunnerState = autoRunner?.State.ToString(),
                    AutoRunnerTimerSeconds = autoRunner != null && !autoRunner.Paused ? Math.Round(autoRunner.Timer.TotalSeconds, 1) : (double?)null,
                };
            }
        }

        public void SetView(AppViewChange change)
        {
            SystemStatusNode status = layer.systemStatusNode;
            AutoRunner autoRunner = layer.AutoRunner;
            ChannelsGridNode channels = layer.ChannelsGridNode;

            if (change.PilotListVisible.HasValue)
                layer.ShowPilotList(change.PilotListVisible.Value);
            if (change.WormVisible.HasValue)
                layer.sceneManagerNode.SetWorm(change.WormVisible.Value);
            if (change.LapsVisible.HasValue)
                channels.SetLapsVisiblity(change.LapsVisible.Value);
            if (change.SubtitlesEnabled.HasValue)
                status?.Subtitles?.SetMute(!change.SubtitlesEnabled.Value);
            if (change.MuteTts.HasValue)
                status?.MuteTTS?.SetMute(change.MuteTts.Value);
            if (change.MuteWav.HasValue)
                status?.MuteWAV?.SetMute(change.MuteWav.Value);
            if (change.AutoRunnerPaused.HasValue && autoRunner != null && autoRunner.Paused != change.AutoRunnerPaused.Value)
                autoRunner.TogglePause();
            if (change.AutoRunnerAddSeconds.HasValue && autoRunner != null)
                autoRunner.Timer += TimeSpan.FromSeconds(change.AutoRunnerAddSeconds.Value);
            if (change.ChannelVisibility.HasValue)
            {
                for (int i = 0; i < change.ChannelVisibility.Value; i++)
                    channels.IncreaseChannelVisiblity();
                for (int i = 0; i > change.ChannelVisibility.Value; i--)
                    channels.DecreaseChannelVisiblity();
            }
            if (change.ReorderChannels == true)
                channels.Reorder(true);
        }

        // ───────────────────────── race-director buttons ─────────────────────────

        public IEnumerable<string> Buttons
        {
            get
            {
                ControlButtonsNode cb = layer.ControlButtons;
                if (cb == null)
                    yield break;

                Tuple<Node, string>[] buttons =
                {
                    Tuple.Create<Node, string>(cb.StartButton, "start"),
                    Tuple.Create<Node, string>(cb.StopButton, "stop"),
                    Tuple.Create<Node, string>(cb.ClearButton, "clear"),
                    Tuple.Create<Node, string>(cb.NextButton, "next"),
                    Tuple.Create<Node, string>(cb.ResumeButton, "resume"),
                    Tuple.Create<Node, string>(cb.ResetButton, "reset"),
                };
                foreach (Tuple<Node, string> b in buttons)
                {
                    if (b.Item1 != null && b.Item1.Visible)
                        yield return b.Item2;
                }
            }
        }

        public void StartRaceWithVideoCheck() { layer.StartRaceWithVideoCheck(); }
        public void StopRace() { layer.StopRace(); }
        public void EmergencyStop() { layer.EmergencyStop(); }
        public void ClearRace() { layer.Clear(); }
        public void ResumeRace() { layer.ResumeRace(); }
        public void StartStopNext() { layer.StartStopNext(); }
        public void NextRace() { layer.NextRace(false); }
        public void NextButton() { layer.NextButton(); }
        public void VideoCheck() { layer.VideoCheck(); }

        public bool HasReplay(Race race)
        {
            return layer.videoManager != null && layer.videoManager.HasReplay(race);
        }

        public void ShowReplay(Race race)
        {
            layer.TabbedMultiNode.ShowReplay(race);
        }

        public void ToggleCrashedOut(int channelGroup)
        {
            layer.ChannelsGridNode.ToggleCrashedOut(layer.EventManager.GetChannelGroup(channelGroup).ToArray());
        }

        public DateTime? NextRaceStartTime { get { return layer.AutoRunner?.NextRaceStartTime; } }

        // ───────────────────────── OBS ─────────────────────────

        private OBSRemoteControlManager Obs { get { return layer.OBSRemoteControlManager; } }

        public bool ObsEnabled { get { return Obs != null && Obs.Enabled; } }
        public bool ObsConnected { get { return Obs != null && Obs.Connected; } }
        public bool ObsAvailable { get { return Obs?.RemoteControl != null; } }

        public bool ObsActive
        {
            get { return Obs != null && Obs.Active; }
            set
            {
                if (Obs != null)
                    Obs.Active = value;
            }
        }

        public IEnumerable<string> ObsTriggers { get { return Enum.GetNames(typeof(OBSRemoteControlManager.Triggers)); } }

        public void ObsReconnect()
        {
            layer.ReloadOBSRemoteControl();
        }

        public Task<string[]> ObsList(string what)
        {
            OBSRemoteControl rc = Obs.RemoteControl;
            TaskCompletionSource<string[]> tcs = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<string[]> done = items => tcs.TrySetResult(items);
            switch (what)
            {
                case "scenes": rc.GetScenes(done); break;
                case "sources": rc.GetSources(done); break;
                case "filters": rc.GetFilters(done); break;
                case "hotkeys": rc.GetHotKeys(done); break;
            }
            return tcs.Task;
        }

        public void ObsSetScene(string scene) { Obs.RemoteControl.SetScene(scene); }
        public void ObsHotkey(string name) { Obs.RemoteControl.TriggerHotKeyAction(name); }
        public void ObsSourceFilter(string source, string filter, bool enabled) { Obs.RemoteControl.SetSourceFilterEnabled(source, filter, enabled); }

        public void ObsTrigger(string trigger)
        {
            Obs.Trigger((OBSRemoteControlManager.Triggers)Enum.Parse(typeof(OBSRemoteControlManager.Triggers), trigger), true);
        }

        public AppObsConfig GetObsConfig()
        {
            OBSRemoteControlManager.OBSRemoteControlConfig c = OBSRemoteControlManager.OBSRemoteControlConfig.Load(layer.Profile);
            return new AppObsConfig
            {
                Enabled = c.Enabled,
                Host = c.Host,
                Port = c.Port,
                Password = string.IsNullOrEmpty(c.Password) ? "" : AppSettings.MaskedSecret,
                Rules = (c.RemoteControlEvents ?? new List<OBSRemoteControlManager.OBSRemoteControlEvent>()).Select(ToRule).ToList(),
            };
        }

        public void SetObsConfig(AppObsConfig config)
        {
            OBSRemoteControlManager.OBSRemoteControlConfig c = OBSRemoteControlManager.OBSRemoteControlConfig.Load(layer.Profile);
            List<OBSRemoteControlManager.OBSRemoteControlEvent> rules = (config.Rules ?? new List<AppObsRule>()).Select(FromRule).ToList();

            c.Enabled = config.Enabled;
            if (!string.IsNullOrWhiteSpace(config.Host))
                c.Host = config.Host.Trim();
            if (config.Port > 0)
                c.Port = config.Port;
            if (config.Password != null && config.Password != AppSettings.MaskedSecret)
                c.Password = config.Password;
            c.RemoteControlEvents = rules;

            OBSRemoteControlManager.OBSRemoteControlConfig.Write(layer.Profile, c);
            layer.ReloadOBSRemoteControl();
        }

        private static AppObsRule ToRule(OBSRemoteControlManager.OBSRemoteControlEvent e)
        {
            AppObsRule r = new AppObsRule { Trigger = e.Trigger.ToString() };
            switch (e)
            {
                case OBSRemoteControlManager.OBSRemoteControlSetSceneEvent s:
                    r.Action = "scene"; r.Scene = s.SceneName; break;
                case OBSRemoteControlManager.OBSRemoteControlSourceFilterToggleEvent f:
                    r.Action = "filter"; r.Source = f.SourceName; r.Filter = f.FilterName; r.Enable = f.Enable; break;
                case OBSRemoteControlManager.OBSRemoteControlActionEvent a:
                    r.Action = "hotkeyName"; r.Hotkey = a.ActionName; break;
                case OBSRemoteControlManager.OBSRemoteControlHotKeyEvent h:
                    r.Action = "hotkeySequence"; r.Hotkey = h.HotKey.ToString(); r.Modifiers = h.Modifiers.ToString(); break;
            }
            return r;
        }

        private static OBSRemoteControlManager.OBSRemoteControlEvent FromRule(AppObsRule r)
        {
            OBSRemoteControlManager.Triggers trigger;
            if (r == null || !Enum.TryParse(r.Trigger, true, out trigger))
                throw new ArgumentException("each rule needs a valid 'trigger' (see GET /obs)");

            switch ((r.Action ?? "").ToLowerInvariant())
            {
                case "scene":
                    if (string.IsNullOrWhiteSpace(r.Scene))
                        throw new ArgumentException("a scene rule needs 'scene'");
                    return new OBSRemoteControlManager.OBSRemoteControlSetSceneEvent { Trigger = trigger, SceneName = r.Scene };
                case "filter":
                    if (string.IsNullOrWhiteSpace(r.Source) || string.IsNullOrWhiteSpace(r.Filter))
                        throw new ArgumentException("a filter rule needs 'source' and 'filter'");
                    return new OBSRemoteControlManager.OBSRemoteControlSourceFilterToggleEvent { Trigger = trigger, SourceName = r.Source, FilterName = r.Filter, Enable = r.Enable ?? true };
                case "hotkeyname":
                    if (string.IsNullOrWhiteSpace(r.Hotkey))
                        throw new ArgumentException("a hotkeyName rule needs 'hotkey'");
                    return new OBSRemoteControlManager.OBSRemoteControlActionEvent { Trigger = trigger, ActionName = r.Hotkey };
                case "hotkeysequence":
                    OBSWebsocketDotNet.Types.OBSHotkey key;
                    if (!Enum.TryParse(r.Hotkey, true, out key))
                        throw new ArgumentException("unknown OBS key '" + r.Hotkey + "'");
                    OBSRemoteControlManager.OBSRemoteControlHotKeyEvent h = new OBSRemoteControlManager.OBSRemoteControlHotKeyEvent { Trigger = trigger, HotKey = key };
                    OBSWebsocketDotNet.Types.KeyModifier[] mods = ParseModifiers(r.Modifiers);
                    if (mods.Length > 0) h.Modifier1 = mods[0];
                    if (mods.Length > 1) h.Modifier2 = mods[1];
                    if (mods.Length > 2) h.Modifier3 = mods[2];
                    return h;
                default:
                    throw new ArgumentException("a rule's 'action' is scene, filter, hotkeyName or hotkeySequence");
            }
        }

        private static OBSWebsocketDotNet.Types.KeyModifier[] ParseModifiers(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return new OBSWebsocketDotNet.Types.KeyModifier[0];
            List<OBSWebsocketDotNet.Types.KeyModifier> mods = new List<OBSWebsocketDotNet.Types.KeyModifier>();
            foreach (string part in s.Split(new[] { ',', '+', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                OBSWebsocketDotNet.Types.KeyModifier m;
                if (!Enum.TryParse(part, true, out m))
                    throw new ArgumentException("unknown modifier '" + part + "'");
                if (m != OBSWebsocketDotNet.Types.KeyModifier.None)
                    mods.Add(m);
            }
            return mods.Take(3).ToArray();
        }

        // ───────────────────────── settings ─────────────────────────

        private static bool NeedsRestart(PropertyInfo p)
        {
            return p.GetCustomAttribute<NeedsRestartAttribute>() != null;
        }

        public IEnumerable<AppSettingsGroup> SettingsGroups
        {
            get
            {
                Profile profile = layer.Profile;
                yield return new AppSettingsGroup
                {
                    Name = "application",
                    Summary = "Application settings (menu → Application Settings): display, sound, web server, video, race behaviour.",
                    Type = typeof(ApplicationProfileSettings),
                    Load = () => ApplicationProfileSettings.Read(profile),
                    Save = o =>
                    {
                        ApplicationProfileSettings.Write(profile, (ApplicationProfileSettings)o);
                        ApplicationProfileSettings.Initialize(profile);
                    },
                    NeedsRestart = NeedsRestart,
                };
                yield return new AppSettingsGroup
                {
                    Name = "auto-runner",
                    Summary = "Auto runner settings (menu → Auto Runner Settings).",
                    Type = typeof(AutoRunnerConfig),
                    Load = () => AutoRunnerConfig.Load(profile),
                    Save = o =>
                    {
                        AutoRunnerConfig.Write(profile, (AutoRunnerConfig)o);
                        layer.ReloadAutoRunnerConfig();
                    },
                    NeedsRestart = NeedsRestart,
                };
            }
        }

        // ───────────────────────── commands ─────────────────────────

        // Commands only the window can run. Race, tab, scene, view and sound
        // commands come from the API itself.
        public IEnumerable<AppCommand> Commands
        {
            get
            {
                MenuButton menu = layer.MenuButton;
                PlatformTools platformTools = layer.PlatformTools;
                EventManager eventManager = layer.EventManager;
                List<AppCommand> list = new List<AppCommand>();

                // Settings dialogs (shown on the FPVTrackside screen)
                list.Add(new AppCommand("open-application-settings", "Dialogs", "Application Settings", () => menu.ShowSettingsEditor()));
                list.Add(new AppCommand("open-auto-runner-settings", "Dialogs", "Auto Runner Settings", () => menu.ShowAutoRunnerSettings()));
                list.Add(new AppCommand("open-channel-settings", "Dialogs", "Channel Settings", () => menu.ShowChannelSettings()));
                list.Add(new AppCommand("open-export-settings", "Dialogs", "Export Column Settings", () => menu.ShowExportSettings()));
                list.Add(new AppCommand("open-event-settings", "Dialogs", "Event Settings", layer.EventEditor));
                list.Add(new AppCommand("open-game-settings", "Dialogs", "Game Settings", () => menu.ShowGameTypeEditor()));
                list.Add(new AppCommand("open-keyboard-shortcuts", "Dialogs", "Keyboard Shortcuts", () => menu.ShowKeyboardShortcuts()));
                list.Add(new AppCommand("open-obs-settings", "Dialogs", "OBS Remote Control Settings", () => menu.ShowOBSRemoteControlSettings()));
                list.Add(new AppCommand("open-points-settings", "Dialogs", "Points Settings", () => menu.ShowPointsSettings(), true));
                list.Add(new AppCommand("open-sound-editor", "Dialogs", "Sound Editor", () => menu.ShowSoundsSettings()));
                list.Add(new AppCommand("open-theme-settings", "Dialogs", "Theme Settings", () => menu.ShowThemeSettings(), true));
                list.Add(new AppCommand("open-timing-settings", "Dialogs", "Timing Settings", () => menu.ShowTimingSettings(), true));
                if (platformTools.HasFeature(PlatformFeature.Video))
                    list.Add(new AppCommand("open-video-settings", "Dialogs", "Video Input Settings", () => menu.ShowVideoSettings()));
                list.Add(new AppCommand("open-youtube-chapters", "Dialogs", "YouTube Chapters", () => menu.ShowChapterMarkerDialog()));

                // Web pages, opened in the browser on the FPVTrackside machine
                list.Add(new AppCommand("open-web-page", "Web", "Open the local web page", () => menu.OpenWebServer()));
                list.Add(new AppCommand("open-stages", "Web", "Open the stages web app", () => menu.OpenWebServer("stages/")));
                list.Add(new AppCommand("open-api-console", "Web", "Open the API console", () => menu.OpenWebServer("api/v1/docs")));
                list.Add(new AppCommand("open-web-users", "Web", "Open the web users page", () => menu.OpenWebServer("stages/users")));

                // New windows
                if (platformTools.HasFeature(PlatformFeature.SecondaryWindow) && layer.LayerStack.Game is BaseGame game)
                {
                    KeyboardShortcuts keys = layer.KeyMapper;
                    list.Add(new AppCommand("window-log", "Windows", "Open the log in a new window", () => game.QuickLaunchWindow<LogNode>(eventManager, keys)));
                    list.Add(new AppCommand("window-event-status", "Windows", "Open Event Status in a new window", () => game.QuickLaunchWindow<EventStatusNodeTopBar>(eventManager, keys)));
                    list.Add(new AppCommand("window-lap-count", "Windows", "Open Lap Count in a new window", () => game.QuickLaunchWindow<LapCountSummaryNode>(eventManager, keys)));
                    list.Add(new AppCommand("window-lap-records", "Windows", "Open Lap Records in a new window", () => game.QuickLaunchWindow<LapRecordsSummaryNode>(eventManager, keys)));
                    list.Add(new AppCommand("window-channel-list", "Windows", "Open the pilot channel list in a new window", () => game.QuickLaunchWindow<PilotChanelList>(eventManager, keys)));
                    list.Add(new AppCommand("window-points", "Windows", "Open Points in a new window", () => game.QuickLaunchWindow<PointsSummaryNode>(eventManager, keys)));
                    list.Add(new AppCommand("window-replay", "Windows", "Open Replay in a new window", () => game.QuickLaunchWindow<ReplayNode>(eventManager, keys)));
                    list.Add(new AppCommand("window-rounds", "Windows", "Open Rounds in a new window", () => game.QuickLaunchWindow<UI.Nodes.Rounds.RoundsNode>(eventManager, keys)));
                }

                // Data
                list.Add(new AppCommand("delete-all-races", "Data", "Delete every round and race in the event (menu → Delete from event; no confirmation)", () => menu.DeleteAllRaceData(), true));
                list.Add(new AppCommand("delete-all-pilots", "Data", "Take every pilot off the event (menu → Delete from event; no confirmation)", () => menu.RemoveAllPilots(), true));
                list.Add(new AppCommand("copy-results", "Data", "Copy the results to the FPVTrackside machine's clipboard", () => platformTools.Clipboard.SetText(eventManager.GetResultsText(ApplicationProfileSettings.Instance.Units, ApplicationProfileSettings.Instance.ExportDecimalPlaces).ToTSV())));
                list.Add(new AppCommand("paste-pilots", "Data", "Add pilots from the FPVTrackside machine's clipboard", () => eventManager.AddPilotsFromLines(platformTools.Clipboard.GetLines())));

                return list;
            }
        }

        // ───────────────────────── leaving the event ─────────────────────────

        public void OpenEvent(Guid eventId)
        {
            if (layer.LayerStack.Game is BaseGame game)
                game.Restart(new Event { ID = eventId });
        }

        public void BackToEventSelector()
        {
            layer.BackToEventSelector();
        }

        public void Restart()
        {
            if (layer.LayerStack.Game is BaseGame game)
                game.Restart(layer.EventManager.Event);
        }
    }
}
