
using Newtonsoft.Json.Linq;
using RaceLib;
using Sound;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tools;

namespace Webb.Api
{
    // The window itself: tabs, live scenes, the race-director buttons, sounds,
    // OBS, settings, dialogs and windows, switching events. Names, checks and
    // errors are worked out here; the window only does what the matching
    // button, menu item or keyboard shortcut does, through IAppControl
    // (UI.EventLayerAppControl).
    public partial class RestApi
    {
        private IAppControl app;
        private SoundManager soundManager;
        private List<AppCommand> commands;

        private IAppControl App
        {
            get
            {
                if (app == null)
                    throw new ApiException(501, "not_available", "this FPVTrackside has no window to control (e.g. the API sandbox)");
                return app;
            }
        }

        private SoundManager Sounds
        {
            get
            {
                if (soundManager == null)
                    throw new ApiException(501, "not_available", "this FPVTrackside has no sound (e.g. the API sandbox)");
                return soundManager;
            }
        }

        private void RegisterApp()
        {
            const string ui = "Interface";

            Get("app", ui, "What the FPVTrackside window is showing", typeof(AppState), r => AppState(),
                "The tab and live scene on screen, every tab and scene you can switch to, the side-panel and overlay switches, the race-director buttons currently shown, timing systems and OBS.");
            Write("PUT", "app/tab", ui, "Switch tab", typeof(TabRequest), typeof(AppState), r =>
            {
                ShowTab(Required(r.BodyAs<TabRequest>().Tab, "tab"));
                return AppState();
            }, "Tab names are listed in GET /app (`Rounds`, `Live`, `Replay`, `Lap Records`, `Lap Count`, `Points`, `Channel List`, `RSSI Analyser`, `Photo Booth`, `Patreons`); case and spaces are ignored. A disabled tab (Replay with no recording) is refused with 409.");
            Write("PUT", "app/scene", ui, "Switch the live scene", typeof(SceneRequest), typeof(AppState), r =>
            {
                ShowScene(Required(r.BodyAs<SceneRequest>().Scene, "scene"));
                return AppState();
            }, "Shows the Live tab on the given scene: `PreRace`, `VideoCheck`, `Race`, `FinishLine`, `RaceResults`, `EventStatus`, `Commentators`, `Clear`, or `Auto` (whatever suits the race state, like clicking the Live tab).");
            Write("PATCH", "app/view", ui, "Change the on-screen switches", typeof(AppViewChange), typeof(AppState), r =>
            {
                AppViewChange change = r.BodyAs<AppViewChange>();
                if (change != null)
                    App.SetView(change);
                return AppState();
            }, "Pilot list, worm, laps under the video tiles, subtitles, TTS/WAV mute, auto-runner pause and timer, channel tiles (`channelVisibility` +1 shows more, -1 fewer) and re-ordering them. Anything left out is unchanged.");
            Write("POST", "app/replay", ui, "Replay a race", typeof(ReplayRequest), typeof(AppState), r =>
            {
                ReplayRequest body = r.BodyAs<ReplayRequest>();
                if (!body.RaceId.HasValue)
                    throw ApiException.BadRequest("'raceId' is required");
                ShowReplay(body.RaceId.Value);
                return AppState();
            }, "Selects the race and opens it on the Replay tab. Refused (409) while a race is running or when the race has no recording.");
            Write("POST", "app/channel-groups/{group}/crashed-out", ui, "Toggle a channel group's tile (crashed out)", null, typeof(AppState), r =>
            {
                int group;
                if (!int.TryParse(r.Param("group"), out group) || group < 1)
                    throw ApiException.BadRequest("'group' is the channel group number, from 1");
                ToggleCrashedOut(group - 1);
                return AppState();
            }, "The same as the toggle-view keyboard shortcut for that group (group 1 is the first).");

            Get("app/commands", ui, "Every command the window can run", typeof(AppCommandInfo), r => Commands().Select(c => new AppCommandInfo { Name = c.Name, Group = c.Group, Summary = c.Summary, RaceControl = c.RaceControl }).ToList(), array: true,
                description: "Menu items, keyboard-shortcut actions, settings dialogs and new windows, by name. Run one with POST /app/commands/{name}.");
            Write("POST", "app/commands/{name}", ui, "Run a command", null, typeof(AppState), r =>
            {
                string name = r.Param("name");
                AppCommand cmd = Commands().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                if (cmd == null)
                    throw ApiException.NotFound("command '" + name + "'");
                if (cmd.RaceControl)
                    RequireRaceControl();
                cmd.Run();
                Log("command " + cmd.Name);
                return AppState();
            }, "Names come from GET /app/commands. Race-control commands need \"HTTP Server race controls enabled\". Commands that open a dialog show it on the FPVTrackside screen.");

            // Events
            Get("app/events", ui, "List the events FPVTrackside has", typeof(EventListItemDto), r => Events(), array: true);
            Write("POST", "app/events/{eventId}/open", ui, "Open another event", null, typeof(OkDto), r =>
            {
                Guid id = r.GuidParam("eventId");
                if (!Events().Any(e => e.Id == id))
                    throw ApiException.NotFound("event");
                LeaveEvent(w => w.OpenEvent(id));
                return new OkDto { Ok = true };
            }, "FPVTrackside closes this event and loads the other one, which restarts the web server: the API is unavailable for a few seconds afterwards. Refused (409) while a race is running.");
            Write("POST", "app/event-selector", ui, "Close the event and go back to event selection", null, typeof(OkDto), r =>
            {
                LeaveEvent(w => w.BackToEventSelector());
                return new OkDto { Ok = true };
            }, "The web server stops with the event; nothing answers until an event is opened again on the FPVTrackside machine.");
            Write("POST", "app/restart", ui, "Reload the event", null, typeof(OkDto), r =>
            {
                LeaveEvent(w => w.Restart());
                return new OkDto { Ok = true };
            }, "Like saving a setting that needs a restart. The API is unavailable for a few seconds.");

            // Race control (window buttons)
            const string rc = "Race control";
            Write("POST", "race-control/clear", rc, "Clear the race (the Clear button)", null, typeof(RaceControlDto), r =>
            {
                RequireRaceControl();
                App.ClearRace();
                return RaceControlState();
            });
            Write("POST", "race-control/resume", rc, "Resume the selected finished race", null, typeof(RaceControlDto), r =>
            {
                RequireRaceControl();
                Race current = RaceManager.CurrentRace;
                if (current == null || !current.Ended)
                    throw ApiException.Conflict("the selected race hasn't finished");
                App.ResumeRace();
                return RaceControlState();
            }, "Picks the race up where it ended, as the Resume button does.");
            Write("POST", "race-control/emergency-stop", rc, "Emergency stop", null, typeof(RaceControlDto), r =>
            {
                RequireRaceControl();
                App.EmergencyStop();
                return RaceControlState();
            }, "Ends the race and plays the emergency-stop sound.");
            Write("POST", "race-control/start-stop-next", rc, "The start/stop/next shortcut", null, typeof(RaceControlDto), r =>
            {
                RequireRaceControl();
                StartStopNext();
                return RaceControlState();
            }, "Whatever the space-bar shortcut would do now: start, stop, or go to the next race from the results. Shows the Live tab first.");

            // Sound
            Get("sound", "Sound", "List the sounds that can be played", typeof(SoundListDto), r => new SoundListDto { Sounds = SoundNames.ToList() });
            Write("POST", "sound/play", "Sound", "Play a sound or announcement", typeof(SoundRequest), typeof(OkDto), r =>
            {
                SoundRequest body = r.BodyAs<SoundRequest>();
                PlaySound(Required(body.Sound, "sound"), body.Text);
                return new OkDto { Ok = true };
            }, "`announceRace`, `announceResults`, `hurryUp`, `untilRaceStart`, `timeRemaining`, `raceOver`, `flag` (also records a flag), `custom1`…`custom5`, `say` (speaks `text`), or any sound name from GET /sound.");
            Write("POST", "sound/stop", "Sound", "Stop the sound playing now", null, typeof(OkDto), r =>
            {
                Sounds.StopSound();
                return new OkDto { Ok = true };
            });

            // OBS
            Get("obs", "OBS", "OBS remote control status", typeof(AppObsState), r => ObsState());
            Write("PATCH", "obs", "OBS", "Pause, resume or reconnect OBS remote control", typeof(AppObsChange), typeof(AppObsState), r =>
            {
                AppObsChange change = r.BodyAs<AppObsChange>();
                if (change?.Reconnect == true)
                    App.ObsReconnect();
                if (change?.Active != null)
                    App.ObsActive = change.Active.Value;
                return ObsState();
            }, "`active: false` stops FPVTrackside sending its automatic OBS changes (the same as the pause shortcut); `reconnect: true` drops and re-opens the connection.");
            Get("obs/{list}", "OBS", "List OBS scenes, sources, filters or hotkeys", typeof(ObsListDto), r => ObsList(r.Param("list")),
                "`list` is `scenes`, `sources`, `filters` or `hotkeys`. Asks OBS, so it can take a few seconds.");
            Write("PUT", "obs/scene", "OBS", "Switch OBS's program scene", typeof(ObsSceneRequest), typeof(OkDto), r =>
            {
                string scene = Required(r.BodyAs<ObsSceneRequest>().Scene, "scene");
                RequireObs();
                App.ObsSetScene(scene);
                return new OkDto { Ok = true };
            });
            Write("POST", "obs/trigger", "OBS", "Fire one of FPVTrackside's OBS triggers", typeof(ObsTriggerRequest), typeof(OkDto), r =>
            {
                ObsTrigger(Required(r.BodyAs<ObsTriggerRequest>().Trigger, "trigger"));
                return new OkDto { Ok = true };
            }, "Runs every OBS rule set up for that trigger (e.g. `PreRaceScene`, `RaceEnd`, `PointsTab`). Triggers are listed in GET /obs.");
            Write("POST", "obs/hotkey", "OBS", "Press an OBS hotkey by name", typeof(ObsHotkeyRequest), typeof(OkDto), r =>
            {
                string name = Required(r.BodyAs<ObsHotkeyRequest>().Name, "name");
                RequireObs();
                App.ObsHotkey(name);
                return new OkDto { Ok = true };
            }, "Names from GET /obs/hotkeys.");
            Write("POST", "obs/filter", "OBS", "Switch a source filter on or off", typeof(ObsFilterRequest), typeof(OkDto), r =>
            {
                ObsFilterRequest body = r.BodyAs<ObsFilterRequest>();
                string source = Required(body.Source, "source");
                string filter = Required(body.Filter, "filter");
                RequireObs();
                App.ObsSourceFilter(source, filter, body.Enabled);
                return new OkDto { Ok = true };
            });
            Get("obs/config", "OBS", "OBS remote control settings and rules", typeof(AppObsConfig), r => App.GetObsConfig(),
                "The password is never returned (`********` when one is set).");
            Write("PUT", "obs/config", "OBS", "Replace OBS remote control settings and rules", typeof(AppObsConfig), typeof(AppObsConfig), r =>
            {
                AppObsConfig config = r.BodyAs<AppObsConfig>();
                if (config == null)
                    throw ApiException.BadRequest("a config is required");
                try
                {
                    App.SetObsConfig(config);
                }
                catch (ArgumentException e)
                {
                    throw ApiException.BadRequest(e.Message);
                }
                Log("OBS config saved");
                return App.GetObsConfig();
            }, "Saved and applied straight away (the connection is re-opened). Leave `password` null or `********` to keep the saved one. A rule's `action` is `scene` (scene), `filter` (source, filter, enable), `hotkeyName` (hotkey) or `hotkeySequence` (hotkey = an OBS key name such as `OBS_KEY_F1`, modifiers = e.g. `Control, Shift`).");

            // Settings
            Get("settings", "Settings", "List the settings groups and their fields", typeof(AppSettingsInfo), r => App.SettingsGroups.Select(AppSettings.Describe).ToList(), array: true);
            Get("settings/{group}", "Settings", "Get a settings group", typeof(object), r => AppSettings.Get(SettingsGroup(r.Param("group"))),
                "Field names and types are in GET /settings. Secrets (passwords, keys, tokens) come back as `********`.");
            Write("PATCH", "settings/{group}", "Settings", "Change settings", null, typeof(AppSettingsResult), r =>
            {
                AppSettingsResult result = AppSettings.Set(SettingsGroup(r.Param("group")), r.BodyAs<JObject>());
                Log("settings " + r.Param("group") + " " + string.Join(",", result.Changed ?? new List<string>()));
                return result;
            }, "Body: a JSON object with just the fields to change. Saved straight away, as the settings dialog's OK does. `needsRestart` says whether a changed field only takes effect after POST /app/restart.");

            // Export
            Get("export/{table}", "Export", "Get an export table", typeof(ExportTableDto), Export,
                "`table` is `pbs`, `raw-laps` or `race-results` — the tables behind the Export menu. `?decimals=` overrides the export decimal places.", query: new[] { "decimals" });

            Examples(new Dictionary<string, string>
            {
                ["PUT app/tab"] = "{ \"tab\": \"Lap Records\" }",
                ["PUT app/scene"] = "{ \"scene\": \"RaceResults\" }",
                ["PATCH app/view"] = "{ \"pilotListVisible\": false, \"wormVisible\": true }",
                ["POST app/replay"] = "{ \"raceId\": \"{{race.id}}\" }",
                ["POST sound/play"] = "{ \"sound\": \"say\", \"text\": \"Pilots to the start line\" }",
                ["PATCH obs"] = "{ \"active\": true }",
                ["PUT obs/scene"] = "{ \"scene\": \"Race\" }",
                ["POST obs/trigger"] = "{ \"trigger\": \"PreRaceScene\" }",
                ["POST obs/hotkey"] = "{ \"name\": \"OBSBasic.StartRecording\" }",
                ["POST obs/filter"] = "{ \"source\": \"Camera\", \"filter\": \"Blur\", \"enabled\": true }",
                ["PATCH settings/{group}"] = "{ \"preRaceScene\": true }",
            });
        }

        private static string Required(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw ApiException.BadRequest("'" + field + "' is required");
            return value.Trim();
        }

        // Names compared without case, spaces or punctuation ("Lap Records" = "laprecords").
        private static string Slug(string s)
        {
            return new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        // ───────────────────────── state ─────────────────────────

        private IEnumerable<string> SceneNames()
        {
            yield return "Auto";
            foreach (string s in App.Scenes)
                yield return s;
            yield return "Commentators";
        }

        private AppState AppState()
        {
            AppView view = App.View;
            Race current = RaceManager.CurrentRace;

            return new AppState
            {
                Tab = App.CurrentTab,
                Tabs = App.Tabs.ToList(),
                Scene = App.CurrentScene,
                Scenes = SceneNames().ToList(),
                PilotListVisible = view.PilotListVisible,
                WormVisible = view.WormVisible,
                LapsVisible = view.LapsVisible,
                SubtitlesEnabled = view.SubtitlesEnabled,
                MuteTts = soundManager?.MuteTTS ?? false,
                MuteWav = soundManager?.MuteWAV ?? false,
                AutoRunnerPaused = view.AutoRunnerPaused,
                AutoRunnerState = view.AutoRunnerState,
                AutoRunnerTimerSeconds = view.AutoRunnerTimerSeconds,
                ReplayAvailable = current != null && current.Ended && App.HasReplay(current),
                RaceRunning = RaceManager.RaceRunning,
                Buttons = App.Buttons.ToList(),
                TimingSystems = RaceManager.TimingSystemManager.AllSystems
                    .Select(t => new AppTimingSystem { Type = t.Type.ToString(), Name = t.ToString(), Connected = t.Connected })
                    .ToList(),
                Obs = ObsState(),
            };
        }

        // ───────────────────────── tabs, scenes and buttons ─────────────────────────

        private void ShowTab(string tab)
        {
            AppTabInfo found = App.Tabs.FirstOrDefault(t => Slug(t.Name) == Slug(tab));
            if (found == null)
                throw ApiException.NotFound("tab '" + tab + "'");
            if (!found.Visible)
                throw ApiException.Conflict("the " + found.Name + " tab isn't available here");
            if (!found.Enabled)
                throw ApiException.Conflict("the " + found.Name + " tab is disabled right now" + (Slug(found.Name) == "replay" ? " (no recording of the selected race)" : ""));
            App.ShowTab(found.Name);
        }

        private void ShowScene(string scene)
        {
            string want = Slug(scene);
            if (want == "auto" || want == "live")
            {
                App.ShowLive();
                return;
            }
            if (want == "commentators")
            {
                App.ShowCommentators();
                return;
            }
            string found = App.Scenes.FirstOrDefault(s => Slug(s) == want);
            if (found == null)
                throw ApiException.NotFound("scene '" + scene + "'");
            App.ShowScene(found);
        }

        private void ShowReplay(Guid raceId)
        {
            Race race = RaceManager.GetRaceByRaceId(raceId);
            if (race == null)
                throw ApiException.NotFound("race");
            if (RaceManager.RaceRunning)
                throw ApiException.Conflict("a race is running");
            if (!race.Ended || !App.HasReplay(race))
                throw ApiException.Conflict("there is no recording of that race");
            App.ShowReplay(race);
        }

        private void ToggleCrashedOut(int channelGroup)
        {
            if (!eventManager.GetChannelGroup(channelGroup).Any())
                throw ApiException.NotFound("channel group " + (channelGroup + 1));
            App.ToggleCrashedOut(channelGroup);
        }

        // The start/stop/next shortcut only acts on the Live tab.
        private void StartStopNext()
        {
            if (Slug(App.CurrentTab) != "live")
                App.ShowLive();
            App.StartStopNext();
        }

        private void StartRaceFromCommand(bool videoCheck)
        {
            if (videoCheck)
                App.StartRaceWithVideoCheck();
            else if (!raceControl.StartRace())
                throw ApiException.Conflict("FPVTrackside refused to start the race");
        }

        // ───────────────────────── sound ─────────────────────────

        private static readonly string[] SoundNames = new[]
        {
            "announceRace", "announceResults", "hurryUp", "untilRaceStart", "timeRemaining", "raceOver", "flag",
            "custom1", "custom2", "custom3", "custom4", "custom5", "say", "stop",
        }.Concat(Enum.GetNames(typeof(SoundKey)).Where(n => n != "None")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        private void PlaySound(string sound, string text)
        {
            SoundManager sm = Sounds;
            Race race = RaceManager.CurrentRace;
            string key = Slug(sound);

            sm.StopSound();
            switch (key)
            {
                case "stop":
                    return;
                case "announcerace": sm.AnnounceRace(race, true); return;
                case "announceresults": sm.AnnounceResults(race); return;
                case "hurryup": sm.HurryUpEveryone(); return;
                case "timeremaining": sm.TimeRemaining(race, RaceManager.RemainingTime); return;
                case "raceover": sm.RaceOver(); return;
                case "untilracestart":
                    DateTime? next = App.NextRaceStartTime;
                    if (next == null)
                        throw ApiException.Conflict("there is no auto runner");
                    sm.PlayTimeUntilNextRace(next.Value - DateTime.Now);
                    return;
                case "flag":
                    eventManager.AddFlag();
                    sm.PlaySound(SoundKey.Flag);
                    return;
                case "say":
                    if (string.IsNullOrWhiteSpace(text))
                        throw ApiException.BadRequest("'text' is required for say");
                    sm.SponsorRead(text, TimeSpan.FromSeconds(60));
                    return;
            }

            foreach (SoundKey sk in Enum.GetValues(typeof(SoundKey)))
            {
                if (sk != SoundKey.None && Slug(sk.ToString()) == key)
                {
                    sm.PlaySound(sk);
                    return;
                }
            }
            throw ApiException.NotFound("sound '" + sound + "'");
        }

        // ───────────────────────── OBS ─────────────────────────

        private AppObsState ObsState()
        {
            return new AppObsState
            {
                Enabled = App.ObsEnabled,
                Connected = App.ObsConnected,
                Active = App.ObsActive,
                Triggers = App.ObsTriggers.ToList(),
            };
        }

        private void RequireObs()
        {
            if (!App.ObsAvailable)
                throw ApiException.Conflict("OBS remote control is switched off (OBS Remote Control Settings, or PUT /obs/config)");
        }

        private void ObsTrigger(string trigger)
        {
            string found = App.ObsTriggers.FirstOrDefault(t => string.Equals(t, trigger, StringComparison.OrdinalIgnoreCase));
            if (found == null)
                throw ApiException.NotFound("OBS trigger '" + trigger + "'");
            RequireObs();
            if (!App.ObsActive)
                throw ApiException.Conflict("OBS remote control is paused; PATCH /obs {\"active\": true} first");
            App.ObsTrigger(found);
        }

        private object ObsList(string list)
        {
            string what = (list ?? "").ToLowerInvariant();
            if (what != "scenes" && what != "sources" && what != "filters" && what != "hotkeys")
                throw ApiException.NotFound("OBS list '" + list + "' (use scenes, sources, filters or hotkeys)");
            RequireObs();

            // The OBS client only calls back on success; a failure just goes quiet.
            Task<string[]> task = App.ObsList(what);
            Task timeout = Task.Delay(TimeSpan.FromSeconds(10));
            return Task.WhenAny(task, timeout).ContinueWith(t =>
            {
                if (t.Result != task)
                    throw new ApiException(502, "obs_unavailable", "OBS didn't answer — check it is running with obs-websocket enabled, and the host, port and password");
                return (object)new ObsListDto { Items = (task.Result ?? new string[0]).ToList() };
            });
        }

        // ───────────────────────── settings ─────────────────────────

        private AppSettingsGroup SettingsGroup(string name)
        {
            AppSettingsGroup g = App.SettingsGroups.FirstOrDefault(x => Slug(x.Name) == Slug(name));
            if (g == null)
                throw ApiException.NotFound("settings group '" + name + "' (see GET /settings)");
            return g;
        }

        // ───────────────────────── commands ─────────────────────────

        // Race, tab, scene, view and sound commands are built here from the
        // same calls as their endpoints; the window adds the ones only it can
        // run (dialogs, windows, web pages, data). Built on first use.
        private List<AppCommand> Commands()
        {
            if (commands != null)
                return commands;

            List<AppCommand> list = new List<AppCommand>();
            void Cmd(string name, string group, string summary, Action run, bool raceControl = false)
            {
                list.Add(new AppCommand(name, group, summary, run, raceControl));
            }
            void View(AppViewChange change) { App.SetView(change); }

            // Race control
            Cmd("start", "Race", "Start the selected race (Start button)", () => StartRaceFromCommand(false), true);
            Cmd("start-with-video-check", "Race", "Start, through the video-check scene when that is on (Start button)", () => StartRaceFromCommand(true), true);
            Cmd("stop", "Race", "End the race or cancel the countdown (Stop button)", () => App.StopRace(), true);
            Cmd("emergency-stop", "Race", "Emergency stop", () => App.EmergencyStop(), true);
            Cmd("clear", "Race", "Clear the race (Clear button)", () => App.ClearRace(), true);
            Cmd("resume", "Race", "Resume the selected finished race (Resume button)", () => App.ResumeRace(), true);
            Cmd("reset", "Race", "Reset the selected race's laps and results (Reset button, without the confirmation)", () => RaceManager.ResetRace(), true);
            Cmd("start-stop-next", "Race", "Start / stop / next, as the shortcut does", StartStopNext, true);
            Cmd("next-race", "Race", "Select the next race", () => App.NextRace(), true);
            Cmd("next-unfinished-race", "Race", "Select the next race that hasn't been run (Next button)", () => App.NextButton(), true);
            Cmd("previous-race", "Race", "Select the previous race", () => RaceManager.PrevRace(), true);
            Cmd("video-check", "Race", "Show the video-check scene and ask pilots to power up", () => App.VideoCheck(), true);
            Cmd("flag", "Race", "Record a flag and play the flag sound", () => PlaySound("flag", null));

            // Tabs and scenes
            foreach (AppTabInfo tab in App.Tabs)
            {
                string text = tab.Name;
                Cmd("tab-" + Slug(text), "Tabs", "Show the " + text + " tab", () => ShowTab(text));
            }
            foreach (string scene in SceneNames())
            {
                string s = scene;
                Cmd("scene-" + Slug(s), "Scenes", "Show the Live tab on the " + s.CamelCaseToHuman() + " scene", () => ShowScene(s));
            }

            // View
            Cmd("show-pilot-list", "View", "Show the pilot list", () => View(new AppViewChange { PilotListVisible = true }));
            Cmd("hide-pilot-list", "View", "Hide the pilot list", () => View(new AppViewChange { PilotListVisible = false }));
            Cmd("toggle-pilot-list", "View", "Show or hide the pilot list", () => View(new AppViewChange { PilotListVisible = !App.View.PilotListVisible }));
            Cmd("toggle-worm", "View", "Show or hide the worm (Race scene)", () => View(new AppViewChange { WormVisible = !App.View.WormVisible }));
            Cmd("show-more-channels", "View", "Show more channel tiles", () => View(new AppViewChange { ChannelVisibility = 1 }));
            Cmd("show-fewer-channels", "View", "Show fewer channel tiles", () => View(new AppViewChange { ChannelVisibility = -1 }));
            Cmd("show-laps", "View", "Show laps under the channel tiles", () => View(new AppViewChange { LapsVisible = true }));
            Cmd("hide-laps", "View", "Hide laps under the channel tiles", () => View(new AppViewChange { LapsVisible = false }));
            Cmd("reorder-channels", "View", "Re-order the channel tiles now", () => View(new AppViewChange { ReorderChannels = true }));
            Cmd("toggle-subtitles", "View", "Switch subtitles on or off", () => View(new AppViewChange { SubtitlesEnabled = !App.View.SubtitlesEnabled }));
            for (int i = 0; i < 8; i++)
            {
                int g = i;
                Cmd("toggle-channel-group-" + (g + 1), "View", "Toggle channel group " + (g + 1) + "'s tile (crashed out)", () => ToggleCrashedOut(g));
            }

            // Auto runner
            Cmd("toggle-auto-runner", "Auto runner", "Pause or resume the auto runner", () => View(new AppViewChange { AutoRunnerPaused = !App.View.AutoRunnerPaused }));
            Cmd("auto-runner-plus-10s", "Auto runner", "Add 10 seconds to the auto-runner timer", () => View(new AppViewChange { AutoRunnerAddSeconds = 10 }));
            Cmd("auto-runner-minus-10s", "Auto runner", "Take 10 seconds off the auto-runner timer", () => View(new AppViewChange { AutoRunnerAddSeconds = -10 }));

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
            Cmd("stop-sound", "Sound", "Stop the sound playing now", () => Sounds.StopSound());
            Cmd("mute-tts", "Sound", "Mute speech", () => View(new AppViewChange { MuteTts = true }));
            Cmd("unmute-tts", "Sound", "Unmute speech", () => View(new AppViewChange { MuteTts = false }));
            Cmd("mute-wav", "Sound", "Mute sound files", () => View(new AppViewChange { MuteWav = true }));
            Cmd("unmute-wav", "Sound", "Unmute sound files", () => View(new AppViewChange { MuteWav = false }));

            // OBS
            Cmd("obs-pause", "OBS", "Stop sending automatic OBS changes", () => App.ObsActive = false);
            Cmd("obs-resume", "OBS", "Resume automatic OBS changes", () => App.ObsActive = true);
            Cmd("obs-reconnect", "OBS", "Reconnect to OBS", () => App.ObsReconnect());

            list.AddRange(App.Commands);

            commands = list;
            return commands;
        }

        // ───────────────────────── leaving the event ─────────────────────────

        // Opening another event, the event selector and restarting all dispose
        // the web server answering this request, so they run a moment later,
        // after the response has been sent.
        private void LeaveEvent(Action<IAppControl> action)
        {
            IAppControl window = App;
            if (RaceManager.RaceRunning)
                throw ApiException.Conflict("a race is running");
            Task.Delay(500).ContinueWith(_ =>
            {
                if (invokeOnUIThread != null)
                    invokeOnUIThread(() => action(window));
                else
                    action(window);
            });
        }

        private List<EventListItemDto> Events()
        {
            using (IDatabase db = DatabaseFactory.Open(Guid.Empty))
            {
                return db.GetSimpleEvents()
                    .Where(e => e.Enabled)
                    .OrderByDescending(e => e.Start)
                    .Select(e => new EventListItemDto { Id = e.ID, Name = e.Name, Start = e.Start, Open = e.ID == eventManager.EventId })
                    .ToList();
            }
        }

        private object Export(ApiRequest r)
        {
            int decimals = 2;
            string d = r.Query["decimals"];
            if (!string.IsNullOrEmpty(d) && (!int.TryParse(d, out decimals) || decimals < 0 || decimals > 6))
                throw ApiException.BadRequest("'decimals' must be 0 to 6");

            string[][] table;
            switch ((r.Param("table") ?? "").ToLowerInvariant())
            {
                case "pbs": table = eventManager.LapRecordManager.ExportPBs(decimals); break;
                case "raw-laps": table = RaceManager.GetRawLaps(decimals); break;
                case "race-results": table = RaceManager.GetRaceResultsText(Units.Metric, decimals); break;
                default: throw ApiException.NotFound("export '" + r.Param("table") + "' (use pbs, raw-laps or race-results)");
            }

            table = table ?? new string[0][];
            return new ExportTableDto
            {
                Columns = table.FirstOrDefault()?.ToList() ?? new List<string>(),
                Rows = table.Skip(1).Select(row => row.ToList()).ToList(),
            };
        }
    }

    public class TabRequest { [Doc("Tab name, as listed in GET /app.", Required = true, Example = "Live")] public string Tab { get; set; } }
    public class SceneRequest { [Doc("Scene name, as listed in GET /app, or Auto.", Required = true, Example = "Race")] public string Scene { get; set; } }
    public class ReplayRequest { [Doc("The race to replay.", Required = true)] public Guid? RaceId { get; set; } }
    public class SoundRequest
    {
        [Doc("What to play.", Required = true, Example = "announceRace")] public string Sound { get; set; }
        [Doc("Words to speak, for `say`.")] public string Text { get; set; }
    }
    public class SoundListDto { [Doc("Names accepted by POST /sound/play.")] public List<string> Sounds { get; set; } }
    public class ObsSceneRequest { [Doc("OBS scene name.", Required = true)] public string Scene { get; set; } }
    public class ObsTriggerRequest { [Doc("FPVTrackside OBS trigger, as listed in GET /obs.", Required = true, Example = "PreRaceScene")] public string Trigger { get; set; } }
    public class ObsHotkeyRequest { [Doc("OBS hotkey action name.", Required = true)] public string Name { get; set; } }
    public class ObsFilterRequest
    {
        [Doc("OBS source.", Required = true)] public string Source { get; set; }
        [Doc("Filter on that source.", Required = true)] public string Filter { get; set; }
        [Doc("On or off.")] public bool Enabled { get; set; }
    }
    public class ObsListDto { public List<string> Items { get; set; } }
    public class EventListItemDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public DateTime Start { get; set; }
        [Doc("This is the event open now.")] public bool Open { get; set; }
    }
    public class ExportTableDto
    {
        public List<string> Columns { get; set; }
        public List<List<string>> Rows { get; set; }
    }
}
