using Composition.Nodes;
using ExternalData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RaceLib;
using Sound;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Tools;
using UI.Nodes;
using UI.Video;
using Composition;
using Webb;

namespace UI
{
    // The REST API's way into the window (Webb.IAppControl). Every member runs
    // on the UI thread and does what the matching button, menu item or
    // keyboard shortcut does, so the screen behaves exactly as if the race
    // director had done it.
    public partial class EventLayer : IAppControl
    {
        // ───────────────────────── state ─────────────────────────

        public AppState GetState()
        {
            TracksideTabbedMultiNode tabs = TabbedMultiNode;
            string tab = null;
            List<AppTabInfo> tabInfos = new List<AppTabInfo>();
            foreach (var kvp in tabs.AllTabs)
            {
                tabInfos.Add(new AppTabInfo { Name = kvp.Key.Text, Enabled = kvp.Key.Enabled, Visible = kvp.Key.Visible });
                if (kvp.Value == tabs.Showing)
                    tab = kvp.Key.Text;
            }

            List<string> buttons = new List<string>();
            if (ControlButtons != null)
            {
                void Add(Node n, string name) { if (n != null && n.Visible) buttons.Add(name); }
                Add(ControlButtons.StartButton, "start");
                Add(ControlButtons.StopButton, "stop");
                Add(ControlButtons.ClearButton, "clear");
                Add(ControlButtons.NextButton, "next");
                Add(ControlButtons.ResumeButton, "resume");
                Add(ControlButtons.ResetButton, "reset");
            }

            Race current = EventManager.RaceManager.CurrentRace;

            return new AppState
            {
                Tab = tab,
                Tabs = tabInfos,
                Scene = sceneManagerNode.Scene.ToString(),
                Scenes = SceneNames().ToList(),
                PilotListVisible = showPilotList,
                WormVisible = sceneManagerNode.WormVisible,
                LapsVisible = ChannelsGridNode.LapsVisible,
                SubtitlesEnabled = systemStatusNode?.Subtitles?.Value ?? false,
                MuteTts = SoundManager.MuteTTS,
                MuteWav = SoundManager.MuteWAV,
                AutoRunnerPaused = AutoRunner?.Paused ?? true,
                AutoRunnerState = AutoRunner?.State.ToString(),
                AutoRunnerTimerSeconds = AutoRunner != null && !AutoRunner.Paused ? Math.Round(AutoRunner.Timer.TotalSeconds, 1) : (double?)null,
                ReplayAvailable = current != null && current.Ended && videoManager != null && videoManager.HasReplay(current),
                RaceRunning = EventManager.RaceManager.RaceRunning,
                Buttons = buttons,
                TimingSystems = EventManager.RaceManager.TimingSystemManager.AllSystems
                    .Select(t => new AppTimingSystem { Type = t.Type.ToString(), Name = t.ToString(), Connected = t.Connected })
                    .ToList(),
                Obs = GetObs(),
            };
        }

        private static IEnumerable<string> SceneNames()
        {
            yield return "Auto";
            foreach (string s in Enum.GetNames(typeof(SceneManagerNode.Scenes)))
                yield return s;
            yield return "Commentators";
        }

        private static string Slug(string s)
        {
            return new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        // ───────────────────────── tabs and scenes ─────────────────────────

        public void ShowTab(string tab)
        {
            string want = Slug(tab);
            foreach (var kvp in TabbedMultiNode.AllTabs)
            {
                TextButtonNode button = kvp.Key;
                if (Slug(button.Text) != want)
                    continue;
                if (!button.Visible)
                    throw AppControlException.Conflict("the " + button.Text + " tab isn't available here");
                if (!button.Enabled)
                    throw AppControlException.Conflict("the " + button.Text + " tab is disabled right now" + (kvp.Value == TabbedMultiNode.ReplayNode ? " (no recording of the selected race)" : ""));

                // The tab's own click handler, so it refreshes what it shows
                // just as a click would.
                button.ButtonNode.Click(null);
                return;
            }
            throw AppControlException.NotFound("tab '" + tab + "'");
        }

        public void ShowScene(string scene)
        {
            string want = Slug(scene);
            if (want == "auto" || want == "live")
            {
                TabbedMultiNode.ShowLive();
                return;
            }
            if (want == "commentators")
            {
                TabbedMultiNode.ShowCommentators();
                return;
            }
            foreach (SceneManagerNode.Scenes s in Enum.GetValues(typeof(SceneManagerNode.Scenes)))
            {
                if (Slug(s.ToString()) == want)
                {
                    TabbedMultiNode.ShowLive(s);
                    return;
                }
            }
            throw AppControlException.NotFound("scene '" + scene + "'");
        }

        public void SetView(AppViewChange change)
        {
            if (change == null)
                return;

            if (change.PilotListVisible.HasValue)
                ShowPilotList(change.PilotListVisible.Value);
            if (change.WormVisible.HasValue)
                sceneManagerNode.SetWorm(change.WormVisible.Value);
            if (change.LapsVisible.HasValue)
                ChannelsGridNode.SetLapsVisiblity(change.LapsVisible.Value);
            if (change.SubtitlesEnabled.HasValue && systemStatusNode?.Subtitles != null)
                systemStatusNode.Subtitles.SetMute(!change.SubtitlesEnabled.Value);
            if (change.MuteTts.HasValue)
                systemStatusNode.MuteTTS.SetMute(change.MuteTts.Value);
            if (change.MuteWav.HasValue)
                systemStatusNode.MuteWAV.SetMute(change.MuteWav.Value);
            if (change.AutoRunnerPaused.HasValue && AutoRunner != null && AutoRunner.Paused != change.AutoRunnerPaused.Value)
                AutoRunner.TogglePause();
            if (change.AutoRunnerAddSeconds.HasValue && AutoRunner != null)
                AutoRunner.Timer += TimeSpan.FromSeconds(change.AutoRunnerAddSeconds.Value);
            if (change.ChannelVisibility.HasValue)
            {
                for (int i = 0; i < change.ChannelVisibility.Value; i++)
                    ChannelsGridNode.IncreaseChannelVisiblity();
                for (int i = 0; i > change.ChannelVisibility.Value; i--)
                    ChannelsGridNode.DecreaseChannelVisiblity();
            }
            if (change.ReorderChannels == true)
                ChannelsGridNode.Reorder(true);
        }

        // ───────────────────────── race-director buttons ─────────────────────────

        void IAppControl.StartRace(bool videoCheck)
        {
            if (videoCheck)
                StartRaceWithVideoCheck();
            else if (!StartRace())
                throw AppControlException.Conflict("FPVTrackside refused to start the race");
        }

        // The Stop button, run straight away rather than queued so the caller
        // sees the race ended in the response.
        public void StopRace(bool emergency)
        {
            if (emergency)
                SoundManager.EmergencyStop();

            workQueueStartStopRace.Clear();
            if (EventManager.RaceManager.PreRaceStartDelay)
            {
                EventManager.RaceManager.CancelRaceStart(false);
            }
            else
            {
                EventManager.RaceManager.EndRace();
            }
            videoManager?.StopRecording();
            ControlButtons.UpdateControlButtons();
        }

        public void ClearRace()
        {
            Clear();
        }

        void IAppControl.StartStopNext()
        {
            if (!TabbedMultiNode.IsOnLive)
                TabbedMultiNode.ShowLive();
            StartStopNext();
        }

        public void ShowReplay(Guid raceId)
        {
            Race race = EventManager.RaceManager.GetRaceByRaceId(raceId);
            if (race == null)
                throw AppControlException.NotFound("race");
            if (EventManager.RaceManager.RaceRunning)
                throw AppControlException.Conflict("a race is running");
            if (!race.Ended || videoManager == null || !videoManager.HasReplay(race))
                throw AppControlException.Conflict("there is no recording of that race");
            TabbedMultiNode.ShowReplay(race);
        }

        public void ToggleCrashedOut(int channelGroup)
        {
            Channel[] channels = EventManager.GetChannelGroup(channelGroup).ToArray();
            if (channels.Length == 0)
                throw AppControlException.NotFound("channel group " + (channelGroup + 1));
            ChannelsGridNode.ToggleCrashedOut(channels);
        }

        // ───────────────────────── sound ─────────────────────────

        public void PlaySound(string sound, string text)
        {
            Race race = EventManager.RaceManager.CurrentRace;
            string key = Slug(sound);

            if (key == "stop")
            {
                SoundManager.StopSound();
                return;
            }

            SoundManager.StopSound();
            switch (key)
            {
                case "announcerace": SoundManager.AnnounceRace(race, true); return;
                case "announceresults": SoundManager.AnnounceResults(race); return;
                case "hurryup": SoundManager.HurryUpEveryone(); return;
                case "untilracestart": SoundManager.PlayTimeUntilNextRace(AutoRunner.NextRaceStartTime - DateTime.Now); return;
                case "timeremaining": SoundManager.TimeRemaining(race, EventManager.RaceManager.RemainingTime); return;
                case "raceover": SoundManager.RaceOver(); return;
                case "flag":
                    EventManager.AddFlag();
                    SoundManager.PlaySound(SoundKey.Flag);
                    return;
                case "say":
                    if (string.IsNullOrWhiteSpace(text))
                        throw AppControlException.BadRequest("'text' is required for say");
                    SoundManager.SponsorRead(text, TimeSpan.FromSeconds(60));
                    return;
            }

            foreach (SoundKey sk in Enum.GetValues(typeof(SoundKey)))
            {
                if (sk != SoundKey.None && Slug(sk.ToString()) == key)
                {
                    SoundManager.PlaySound(sk);
                    return;
                }
            }
            throw AppControlException.NotFound("sound '" + sound + "'");
        }

        // ───────────────────────── OBS ─────────────────────────

        public AppObsState GetObs()
        {
            OBSRemoteControlManager m = OBSRemoteControlManager;
            return new AppObsState
            {
                Enabled = m != null && m.Enabled,
                Connected = m != null && m.Connected,
                Active = m != null && m.Active,
                Triggers = Enum.GetNames(typeof(OBSRemoteControlManager.Triggers)).ToList(),
            };
        }

        public void SetObs(AppObsChange change)
        {
            if (change == null)
                return;
            if (change.Reconnect == true)
                ReloadOBSRemoteControl();
            if (change.Active.HasValue && OBSRemoteControlManager != null)
                OBSRemoteControlManager.Active = change.Active.Value;
        }

        private OBSRemoteControl Obs()
        {
            OBSRemoteControl rc = OBSRemoteControlManager?.RemoteControl;
            if (rc == null)
                throw AppControlException.Conflict("OBS remote control is switched off (OBS Remote Control Settings, or PUT /obs/config)");
            return rc;
        }

        public Task<string[]> ObsList(string what)
        {
            OBSRemoteControl rc = Obs();
            TaskCompletionSource<string[]> tcs = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<string[]> done = items => tcs.TrySetResult(items);
            switch (what)
            {
                case "scenes": rc.GetScenes(done); break;
                case "sources": rc.GetSources(done); break;
                case "filters": rc.GetFilters(done); break;
                case "hotkeys": rc.GetHotKeys(done); break;
                default: throw AppControlException.NotFound("OBS list '" + what + "'");
            }

            // The OBS client only calls back on success; a failure just goes quiet.
            Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ =>
                tcs.TrySetException(new AppControlException(502, "obs_unavailable", "OBS didn't answer — check it is running with obs-websocket enabled, and the host, port and password")));
            return tcs.Task;
        }

        public void ObsSetScene(string scene)
        {
            Obs().SetScene(scene);
        }

        public void ObsTrigger(string trigger)
        {
            OBSRemoteControlManager.Triggers t;
            if (!Enum.TryParse(trigger, true, out t))
                throw AppControlException.NotFound("OBS trigger '" + trigger + "'");
            Obs();
            if (!OBSRemoteControlManager.Active)
                throw AppControlException.Conflict("OBS remote control is paused; PATCH /obs {\"active\": true} first");
            OBSRemoteControlManager.Trigger(t, true);
        }

        public void ObsHotkey(string name)
        {
            Obs().TriggerHotKeyAction(name);
        }

        public void ObsSourceFilter(string source, string filter, bool enabled)
        {
            Obs().SetSourceFilterEnabled(source, filter, enabled);
        }

        private const string MaskedSecret = "********";

        public AppObsConfig GetObsConfig()
        {
            OBSRemoteControlManager.OBSRemoteControlConfig c = OBSRemoteControlManager.OBSRemoteControlConfig.Load(Profile);
            return new AppObsConfig
            {
                Enabled = c.Enabled,
                Host = c.Host,
                Port = c.Port,
                Password = string.IsNullOrEmpty(c.Password) ? "" : MaskedSecret,
                Rules = (c.RemoteControlEvents ?? new List<OBSRemoteControlManager.OBSRemoteControlEvent>()).Select(ToRule).ToList(),
            };
        }

        public void SetObsConfig(AppObsConfig config)
        {
            if (config == null)
                throw AppControlException.BadRequest("a config is required");

            OBSRemoteControlManager.OBSRemoteControlConfig c = OBSRemoteControlManager.OBSRemoteControlConfig.Load(Profile);
            List<OBSRemoteControlManager.OBSRemoteControlEvent> rules = (config.Rules ?? new List<AppObsRule>()).Select(FromRule).ToList();

            c.Enabled = config.Enabled;
            if (!string.IsNullOrWhiteSpace(config.Host))
                c.Host = config.Host.Trim();
            if (config.Port > 0)
                c.Port = config.Port;
            if (config.Password != null && config.Password != MaskedSecret)
                c.Password = config.Password;
            c.RemoteControlEvents = rules;

            OBSRemoteControlManager.OBSRemoteControlConfig.Write(Profile, c);
            ReloadOBSRemoteControl();
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
                throw AppControlException.BadRequest("each rule needs a valid 'trigger' (see GET /obs)");

            switch (Slug(r.Action))
            {
                case "scene":
                    if (string.IsNullOrWhiteSpace(r.Scene))
                        throw AppControlException.BadRequest("a scene rule needs 'scene'");
                    return new OBSRemoteControlManager.OBSRemoteControlSetSceneEvent { Trigger = trigger, SceneName = r.Scene };
                case "filter":
                    if (string.IsNullOrWhiteSpace(r.Source) || string.IsNullOrWhiteSpace(r.Filter))
                        throw AppControlException.BadRequest("a filter rule needs 'source' and 'filter'");
                    return new OBSRemoteControlManager.OBSRemoteControlSourceFilterToggleEvent { Trigger = trigger, SourceName = r.Source, FilterName = r.Filter, Enable = r.Enable ?? true };
                case "hotkeyname":
                    if (string.IsNullOrWhiteSpace(r.Hotkey))
                        throw AppControlException.BadRequest("a hotkeyName rule needs 'hotkey'");
                    return new OBSRemoteControlManager.OBSRemoteControlActionEvent { Trigger = trigger, ActionName = r.Hotkey };
                case "hotkeysequence":
                    OBSWebsocketDotNet.Types.OBSHotkey key;
                    if (!Enum.TryParse(r.Hotkey, true, out key))
                        throw AppControlException.BadRequest("unknown OBS key '" + r.Hotkey + "'");
                    OBSRemoteControlManager.OBSRemoteControlHotKeyEvent h = new OBSRemoteControlManager.OBSRemoteControlHotKeyEvent { Trigger = trigger, HotKey = key };
                    OBSWebsocketDotNet.Types.KeyModifier[] mods = ParseModifiers(r.Modifiers);
                    if (mods.Length > 0) h.Modifier1 = mods[0];
                    if (mods.Length > 1) h.Modifier2 = mods[1];
                    if (mods.Length > 2) h.Modifier3 = mods[2];
                    return h;
                default:
                    throw AppControlException.BadRequest("a rule's 'action' is scene, filter, hotkeyName or hotkeySequence");
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
                    throw AppControlException.BadRequest("unknown modifier '" + part + "'");
                if (m != OBSWebsocketDotNet.Types.KeyModifier.None)
                    mods.Add(m);
            }
            return mods.Take(3).ToArray();
        }

        // ───────────────────────── settings ─────────────────────────

        private class SettingsGroup
        {
            public string Name;
            public string Summary;
            public Type Type;
            public Func<object> Load;
            public Action<object> Save;
        }

        private IEnumerable<SettingsGroup> Groups()
        {
            yield return new SettingsGroup
            {
                Name = "application",
                Summary = "Application settings (menu → Application Settings): display, sound, web server, video, race behaviour.",
                Type = typeof(ApplicationProfileSettings),
                Load = () => ApplicationProfileSettings.Read(Profile),
                Save = o =>
                {
                    ApplicationProfileSettings.Write(Profile, (ApplicationProfileSettings)o);
                    ApplicationProfileSettings.Initialize(Profile);
                },
            };
            yield return new SettingsGroup
            {
                Name = "auto-runner",
                Summary = "Auto runner settings (menu → Auto Runner Settings).",
                Type = typeof(AutoRunnerConfig),
                Load = () => AutoRunnerConfig.Load(Profile),
                Save = o =>
                {
                    AutoRunnerConfig.Write(Profile, (AutoRunnerConfig)o);
                    ReloadAutoRunnerConfig();
                },
            };
        }

        private SettingsGroup Group(string name)
        {
            SettingsGroup g = Groups().FirstOrDefault(x => Slug(x.Name) == Slug(name));
            if (g == null)
                throw AppControlException.NotFound("settings group '" + name + "' (see GET /settings)");
            return g;
        }

        private static IEnumerable<PropertyInfo> Editable(Type t)
        {
            return t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                .Where(p => p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
                .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null);
        }

        private static bool IsSecret(PropertyInfo p)
        {
            string n = p.Name.ToLowerInvariant();
            return p.PropertyType == typeof(string) && (n.Contains("password") || n.Contains("secret") || n.EndsWith("token") || n.EndsWith("apikey"));
        }

        private static string Camel(string s)
        {
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        public IEnumerable<AppSettingsInfo> SettingsGroups()
        {
            foreach (SettingsGroup g in Groups())
            {
                yield return new AppSettingsInfo
                {
                    Name = g.Name,
                    Summary = g.Summary,
                    Fields = Editable(g.Type).Select(p =>
                    {
                        Type t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                        return new AppSettingField
                        {
                            Name = Camel(p.Name),
                            Label = p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? p.Name.CamelCaseToHuman(),
                            Category = p.GetCustomAttribute<CategoryAttribute>()?.Category,
                            Type = t.IsEnum ? "enum" : t.IsArray ? "array" : t.Name.ToLowerInvariant(),
                            Values = t.IsEnum ? Enum.GetNames(t).ToList() : null,
                            NeedsRestart = p.GetCustomAttribute<NeedsRestartAttribute>() != null,
                        };
                    }).ToList(),
                };
            }
        }

        public object GetSettings(string group)
        {
            SettingsGroup g = Group(group);
            return ToJson(g.Type, g.Load());
        }

        private static JObject ToJson(Type t, object o)
        {
            JsonSerializer ser = Webb.Api.ApiJson.Serializer;
            JObject j = new JObject();
            foreach (PropertyInfo p in Editable(t))
            {
                object v = p.GetValue(o);
                if (IsSecret(p))
                    j[Camel(p.Name)] = string.IsNullOrEmpty(v as string) ? "" : MaskedSecret;
                else
                    j[Camel(p.Name)] = v == null ? JValue.CreateNull() : JToken.FromObject(v, ser);
            }
            return j;
        }

        public AppSettingsResult SetSettings(string group, JObject values)
        {
            SettingsGroup g = Group(group);
            object o = g.Load();
            Dictionary<string, PropertyInfo> props = Editable(g.Type).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            JsonSerializer ser = Webb.Api.ApiJson.Serializer;

            List<string> changed = new List<string>();
            bool restart = false;

            // Check every value before changing anything.
            List<KeyValuePair<PropertyInfo, object>> sets = new List<KeyValuePair<PropertyInfo, object>>();
            foreach (JProperty jp in values?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                PropertyInfo p;
                if (!props.TryGetValue(jp.Name, out p))
                    throw AppControlException.BadRequest("'" + jp.Name + "' is not a field of " + g.Name + " (see GET /settings)");
                if (IsSecret(p) && (string)jp.Value == MaskedSecret)
                    continue;
                object v;
                try
                {
                    v = jp.Value.ToObject(p.PropertyType, ser);
                }
                catch (Exception e)
                {
                    throw AppControlException.BadRequest("'" + jp.Name + "': " + e.Message);
                }
                sets.Add(new KeyValuePair<PropertyInfo, object>(p, v));
            }

            foreach (var kv in sets)
            {
                PropertyInfo p = kv.Key;
                string before = JsonConvert.SerializeObject(p.GetValue(o));
                p.SetValue(o, kv.Value);
                if (before != JsonConvert.SerializeObject(p.GetValue(o)))
                {
                    changed.Add(Camel(p.Name));
                    if (p.GetCustomAttribute<NeedsRestartAttribute>() != null)
                        restart = true;
                }
            }

            if (changed.Count > 0)
                g.Save(o);

            return new AppSettingsResult { Settings = ToJson(g.Type, g.Load()), Changed = changed, NeedsRestart = restart };
        }

        // ───────────────────────── commands ─────────────────────────

        private class Command
        {
            public AppCommandInfo Info;
            public Action Run;
        }

        private List<Command> commands;

        private void Cmd(string name, string group, string summary, Action run, bool raceControl = false)
        {
            commands.Add(new Command { Info = new AppCommandInfo { Name = name, Group = group, Summary = summary, RaceControl = raceControl }, Run = run });
        }

        // Built on first use (the menu button and video manager exist by then).
        private List<Command> CommandList()
        {
            if (commands != null)
                return commands;

            commands = new List<Command>();
            RaceManager rm = EventManager.RaceManager;

            // Race control
            Cmd("start", "Race", "Start the selected race (Start button)", () => ((IAppControl)this).StartRace(false), true);
            Cmd("start-with-video-check", "Race", "Start, through the video-check scene when that is on (Start button)", () => ((IAppControl)this).StartRace(true), true);
            Cmd("stop", "Race", "End the race or cancel the countdown (Stop button)", () => StopRace(false), true);
            Cmd("emergency-stop", "Race", "Emergency stop", () => StopRace(true), true);
            Cmd("clear", "Race", "Clear the race (Clear button)", Clear, true);
            Cmd("resume", "Race", "Resume the selected finished race (Resume button)", ResumeRace, true);
            Cmd("reset", "Race", "Reset the selected race's laps and results (Reset button, without the confirmation)", rm.ResetRace, true);
            Cmd("start-stop-next", "Race", "Start / stop / next, as the shortcut does", () => ((IAppControl)this).StartStopNext(), true);
            Cmd("next-race", "Race", "Select the next race", () => NextRace(false), true);
            Cmd("next-unfinished-race", "Race", "Select the next race that hasn't been run (Next button)", () => NextButton_OnClick(null), true);
            Cmd("previous-race", "Race", "Select the previous race", () => rm.PrevRace(), true);
            Cmd("video-check", "Race", "Show the video-check scene and ask pilots to power up", VideoCheck, true);
            Cmd("flag", "Race", "Record a flag and play the flag sound", () => PlaySound("flag", null));

            // Tabs and scenes
            foreach (var kvp in TabbedMultiNode.AllTabs)
            {
                string text = kvp.Key.Text;
                Cmd("tab-" + Slug(text), "Tabs", "Show the " + text + " tab", () => ShowTab(text));
            }
            foreach (string scene in SceneNames())
            {
                string s = scene;
                Cmd("scene-" + Slug(s), "Scenes", "Show the Live tab on the " + s.CamelCaseToHuman() + " scene", () => ShowScene(s));
            }

            // View
            Cmd("show-pilot-list", "View", "Show the pilot list", () => ShowPilotList(true));
            Cmd("hide-pilot-list", "View", "Hide the pilot list", () => ShowPilotList(false));
            Cmd("toggle-pilot-list", "View", "Show or hide the pilot list", () => TogglePilotList());
            Cmd("toggle-worm", "View", "Show or hide the worm (Race scene)", () => sceneManagerNode.SetWorm(!sceneManagerNode.WormVisible));
            Cmd("show-more-channels", "View", "Show more channel tiles", ChannelsGridNode.IncreaseChannelVisiblity);
            Cmd("show-fewer-channels", "View", "Show fewer channel tiles", ChannelsGridNode.DecreaseChannelVisiblity);
            Cmd("show-laps", "View", "Show laps under the channel tiles", () => ChannelsGridNode.SetLapsVisiblity(true));
            Cmd("hide-laps", "View", "Hide laps under the channel tiles", () => ChannelsGridNode.SetLapsVisiblity(false));
            Cmd("reorder-channels", "View", "Re-order the channel tiles now", () => ChannelsGridNode.Reorder(true));
            Cmd("toggle-subtitles", "View", "Switch subtitles on or off", () => { if (systemStatusNode?.Subtitles != null) systemStatusNode.Subtitles.SetMute(systemStatusNode.Subtitles.Value); });
            for (int i = 0; i < 8; i++)
            {
                int g = i;
                Cmd("toggle-channel-group-" + (g + 1), "View", "Toggle channel group " + (g + 1) + "'s tile (crashed out)", () => ToggleCrashedOut(g));
            }

            // Auto runner
            Cmd("toggle-auto-runner", "Auto runner", "Pause or resume the auto runner", () => AutoRunner?.TogglePause());
            Cmd("auto-runner-plus-10s", "Auto runner", "Add 10 seconds to the auto-runner timer", () => { if (AutoRunner != null) AutoRunner.Timer += TimeSpan.FromSeconds(10); });
            Cmd("auto-runner-minus-10s", "Auto runner", "Take 10 seconds off the auto-runner timer", () => { if (AutoRunner != null) AutoRunner.Timer -= TimeSpan.FromSeconds(10); });

            // Sound
            Cmd("announce-race", "Sound", "Announce the selected race", () => PlaySound("announceRace", null));
            Cmd("announce-results", "Sound", "Announce the race results", () => PlaySound("announceResults", null));
            Cmd("hurry-up", "Sound", "Hurry up everyone", () => PlaySound("hurryUp", null));
            Cmd("until-race-start", "Sound", "Announce the time until the next race", () => PlaySound("untilRaceStart", null));
            Cmd("time-remaining", "Sound", "Announce the time remaining", () => PlaySound("timeRemaining", null));
            Cmd("race-over", "Sound", "Race over", () => PlaySound("raceOver", null));
            for (int i = 1; i <= 5; i++)
            {
                string c = "custom" + i;
                Cmd("custom-sound-" + i, "Sound", "Play custom sound " + i, () => PlaySound(c, null));
            }
            Cmd("stop-sound", "Sound", "Stop the sound playing now", () => SoundManager.StopSound());
            Cmd("mute-tts", "Sound", "Mute speech", () => systemStatusNode.MuteTTS.SetMute(true));
            Cmd("unmute-tts", "Sound", "Unmute speech", () => systemStatusNode.MuteTTS.SetMute(false));
            Cmd("mute-wav", "Sound", "Mute sound files", () => systemStatusNode.MuteWAV.SetMute(true));
            Cmd("unmute-wav", "Sound", "Unmute sound files", () => systemStatusNode.MuteWAV.SetMute(false));

            // OBS
            Cmd("obs-pause", "OBS", "Stop sending automatic OBS changes", () => { if (OBSRemoteControlManager != null) OBSRemoteControlManager.Active = false; });
            Cmd("obs-resume", "OBS", "Resume automatic OBS changes", () => { if (OBSRemoteControlManager != null) OBSRemoteControlManager.Active = true; });
            Cmd("obs-reconnect", "OBS", "Reconnect to OBS", ReloadOBSRemoteControl);

            // Settings dialogs (shown on the FPVTrackside screen)
            Cmd("open-application-settings", "Dialogs", "Application Settings", () => MenuButton.ShowSettingsEditor());
            Cmd("open-auto-runner-settings", "Dialogs", "Auto Runner Settings", () => MenuButton.ShowAutoRunnerSettings());
            Cmd("open-channel-settings", "Dialogs", "Channel Settings", () => MenuButton.ShowChannelSettings());
            Cmd("open-export-settings", "Dialogs", "Export Column Settings", () => MenuButton.ShowExportSettings());
            Cmd("open-event-settings", "Dialogs", "Event Settings", EventEditor);
            Cmd("open-game-settings", "Dialogs", "Game Settings", () => MenuButton.ShowGameTypeEditor());
            Cmd("open-keyboard-shortcuts", "Dialogs", "Keyboard Shortcuts", () => MenuButton.ShowKeyboardShortcuts());
            Cmd("open-obs-settings", "Dialogs", "OBS Remote Control Settings", () => MenuButton.ShowOBSRemoteControlSettings());
            Cmd("open-points-settings", "Dialogs", "Points Settings", () => MenuButton.ShowPointsSettings(), true);
            Cmd("open-sound-editor", "Dialogs", "Sound Editor", () => MenuButton.ShowSoundsSettings());
            Cmd("open-theme-settings", "Dialogs", "Theme Settings", () => MenuButton.ShowThemeSettings(), true);
            Cmd("open-timing-settings", "Dialogs", "Timing Settings", () => MenuButton.ShowTimingSettings(), true);
            if (PlatformTools.HasFeature(PlatformFeature.Video))
                Cmd("open-video-settings", "Dialogs", "Video Input Settings", () => MenuButton.ShowVideoSettings());
            Cmd("open-youtube-chapters", "Dialogs", "YouTube Chapters", () => MenuButton.ShowChapterMarkerDialog());

            // Web pages, opened in the browser on the FPVTrackside machine
            Cmd("open-web-page", "Web", "Open the local web page", () => MenuButton.OpenWebServer());
            Cmd("open-stages", "Web", "Open the stages web app", () => MenuButton.OpenWebServer("stages/"));
            Cmd("open-api-console", "Web", "Open the API console", () => MenuButton.OpenWebServer("api/v1/docs"));

            // New windows
            if (PlatformTools.HasFeature(PlatformFeature.SecondaryWindow) && LayerStack.Game is BaseGame game)
            {
                Cmd("window-log", "Windows", "Open the log in a new window", () => game.QuickLaunchWindow<LogNode>(EventManager, KeyMapper));
                Cmd("window-event-status", "Windows", "Open Event Status in a new window", () => game.QuickLaunchWindow<EventStatusNodeTopBar>(EventManager, KeyMapper));
                Cmd("window-lap-count", "Windows", "Open Lap Count in a new window", () => game.QuickLaunchWindow<LapCountSummaryNode>(EventManager, KeyMapper));
                Cmd("window-lap-records", "Windows", "Open Lap Records in a new window", () => game.QuickLaunchWindow<LapRecordsSummaryNode>(EventManager, KeyMapper));
                Cmd("window-channel-list", "Windows", "Open the pilot channel list in a new window", () => game.QuickLaunchWindow<PilotChanelList>(EventManager, KeyMapper));
                Cmd("window-points", "Windows", "Open Points in a new window", () => game.QuickLaunchWindow<PointsSummaryNode>(EventManager, KeyMapper));
                Cmd("window-replay", "Windows", "Open Replay in a new window", () => game.QuickLaunchWindow<ReplayNode>(EventManager, KeyMapper));
                Cmd("window-rounds", "Windows", "Open Rounds in a new window", () => game.QuickLaunchWindow<UI.Nodes.Rounds.RoundsNode>(EventManager, KeyMapper));
            }

            // Data
            Cmd("delete-all-races", "Data", "Delete every round and race in the event (menu → Delete from event; no confirmation)", () => MenuButton.DeleteAllRaceData(), true);
            Cmd("delete-all-pilots", "Data", "Take every pilot off the event (menu → Delete from event; no confirmation)", () => MenuButton.RemoveAllPilots(), true);
            Cmd("copy-results", "Data", "Copy the results to the FPVTrackside machine's clipboard", () => PlatformTools.Clipboard.SetText(EventManager.GetResultsText(ApplicationProfileSettings.Instance.Units, ApplicationProfileSettings.Instance.ExportDecimalPlaces).ToTSV()));
            Cmd("paste-pilots", "Data", "Add pilots from the FPVTrackside machine's clipboard", () => EventManager.AddPilotsFromLines(PlatformTools.Clipboard.GetLines()));

            return commands;
        }

        public IEnumerable<AppCommandInfo> Commands()
        {
            return CommandList().Select(c => c.Info);
        }

        public void RunCommand(string name)
        {
            Command c = CommandList().FirstOrDefault(x => string.Equals(x.Info.Name, name, StringComparison.OrdinalIgnoreCase));
            if (c == null)
                throw AppControlException.NotFound("command '" + name + "'");
            c.Run();
        }

        // ───────────────────────── leaving the event ─────────────────────────

        // These dispose this layer and the web server answering the request, so
        // they run a moment later, after the response has been sent.
        private void AfterResponse(Action action)
        {
            Task.Delay(500).ContinueWith(_ => PlatformTools.Invoke(action));
        }

        public void OpenEvent(Guid eventId)
        {
            AfterResponse(() =>
            {
                if (LayerStack.Game is BaseGame game)
                    game.Restart(new Event { ID = eventId });
            });
        }

        void IAppControl.BackToEventSelector()
        {
            AfterResponse(BackToEventSelector);
        }

        public void Restart()
        {
            Event ev = EventManager.Event;
            AfterResponse(() =>
            {
                if (LayerStack.Game is BaseGame game)
                    game.Restart(ev);
            });
        }
    }
}
