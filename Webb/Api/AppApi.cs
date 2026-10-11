
using Newtonsoft.Json.Linq;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Webb.Api
{
    // The window itself: tabs, live scenes, the race-director buttons, sounds,
    // OBS, settings, dialogs and windows, switching events. Everything here goes
    // through IAppControl (implemented by UI.EventLayer), so each call does what
    // the matching button, menu item or keyboard shortcut does.
    public partial class RestApi
    {
        private IAppControl app;

        private IAppControl App
        {
            get
            {
                if (app == null)
                    throw new ApiException(501, "not_available", "this FPVTrackside has no window to control (e.g. the API sandbox)");
                return app;
            }
        }

        private void RegisterApp()
        {
            const string ui = "Interface";

            Get("app", ui, "What the FPVTrackside window is showing", typeof(AppState), r => App.GetState(),
                "The tab and live scene on screen, every tab and scene you can switch to, the side-panel and overlay switches, the race-director buttons currently shown, timing systems and OBS.");
            Write("PUT", "app/tab", ui, "Switch tab", typeof(TabRequest), typeof(AppState), r =>
            {
                App.ShowTab(Required(r.BodyAs<TabRequest>().Tab, "tab"));
                return App.GetState();
            }, "Tab names are listed in GET /app (`Rounds`, `Live`, `Replay`, `Lap Records`, `Lap Count`, `Points`, `Channel List`, `RSSI Analyser`, `Photo Booth`, `Patreons`); case and spaces are ignored. A disabled tab (Replay with no recording) is refused with 409.");
            Write("PUT", "app/scene", ui, "Switch the live scene", typeof(SceneRequest), typeof(AppState), r =>
            {
                App.ShowScene(Required(r.BodyAs<SceneRequest>().Scene, "scene"));
                return App.GetState();
            }, "Shows the Live tab on the given scene: `PreRace`, `VideoCheck`, `Race`, `FinishLine`, `RaceResults`, `EventStatus`, `Commentators`, `Clear`, or `Auto` (whatever suits the race state, like clicking the Live tab).");
            Write("PATCH", "app/view", ui, "Change the on-screen switches", typeof(AppViewChange), typeof(AppState), r =>
            {
                App.SetView(r.BodyAs<AppViewChange>());
                return App.GetState();
            }, "Pilot list, worm, laps under the video tiles, subtitles, TTS/WAV mute, auto-runner pause and timer, channel tiles (`channelVisibility` +1 shows more, -1 fewer) and re-ordering them. Anything left out is unchanged.");
            Write("POST", "app/replay", ui, "Replay a race", typeof(ReplayRequest), typeof(AppState), r =>
            {
                ReplayRequest body = r.BodyAs<ReplayRequest>();
                if (!body.RaceId.HasValue)
                    throw ApiException.BadRequest("'raceId' is required");
                App.ShowReplay(body.RaceId.Value);
                return App.GetState();
            }, "Selects the race and opens it on the Replay tab. Refused (409) while a race is running or when the race has no recording.");
            Write("POST", "app/channel-groups/{group}/crashed-out", ui, "Toggle a channel group's tile (crashed out)", null, typeof(AppState), r =>
            {
                int group;
                if (!int.TryParse(r.Param("group"), out group) || group < 1)
                    throw ApiException.BadRequest("'group' is the channel group number, from 1");
                App.ToggleCrashedOut(group - 1);
                return App.GetState();
            }, "The same as the toggle-view keyboard shortcut for that group (group 1 is the first).");

            Get("app/commands", ui, "Every command the window can run", typeof(AppCommandInfo), r => App.Commands().ToList(), array: true,
                description: "Menu items, keyboard-shortcut actions, settings dialogs and new windows, by name. Run one with POST /app/commands/{name}.");
            Write("POST", "app/commands/{name}", ui, "Run a command", null, typeof(AppState), r =>
            {
                string name = r.Param("name");
                AppCommandInfo cmd = App.Commands().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                if (cmd == null)
                    throw ApiException.NotFound("command '" + name + "'");
                if (cmd.RaceControl)
                    RequireRaceControl();
                App.RunCommand(cmd.Name);
                Log("command " + cmd.Name);
                return App.GetState();
            }, "Names come from GET /app/commands. Race-control commands need \"HTTP Server race controls enabled\". Commands that open a dialog show it on the FPVTrackside screen.");

            // Events
            Get("app/events", ui, "List the events FPVTrackside has", typeof(EventListItemDto), r => Events(), array: true);
            Write("POST", "app/events/{eventId}/open", ui, "Open another event", null, typeof(OkDto), r =>
            {
                Guid id = r.GuidParam("eventId");
                if (!Events().Any(e => e.Id == id))
                    throw ApiException.NotFound("event");
                if (RaceManager.RaceRunning)
                    throw ApiException.Conflict("a race is running");
                App.OpenEvent(id);
                return new OkDto { Ok = true };
            }, "FPVTrackside closes this event and loads the other one, which restarts the web server: the API is unavailable for a few seconds afterwards. Refused (409) while a race is running.");
            Write("POST", "app/event-selector", ui, "Close the event and go back to event selection", null, typeof(OkDto), r =>
            {
                if (RaceManager.RaceRunning)
                    throw ApiException.Conflict("a race is running");
                App.BackToEventSelector();
                return new OkDto { Ok = true };
            }, "The web server stops with the event; nothing answers until an event is opened again on the FPVTrackside machine.");
            Write("POST", "app/restart", ui, "Reload the event", null, typeof(OkDto), r =>
            {
                if (RaceManager.RaceRunning)
                    throw ApiException.Conflict("a race is running");
                App.Restart();
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
                App.StopRace(true);
                return RaceControlState();
            }, "Ends the race and plays the emergency-stop sound.");
            Write("POST", "race-control/start-stop-next", rc, "The start/stop/next shortcut", null, typeof(RaceControlDto), r =>
            {
                RequireRaceControl();
                App.StartStopNext();
                return RaceControlState();
            }, "Whatever the space-bar shortcut would do now: start, stop, or go to the next race from the results. Only acts on the Live tab, like the shortcut.");

            // Sound
            Get("sound", "Sound", "List the sounds that can be played", typeof(SoundListDto), r => new SoundListDto { Sounds = Sounds.ToList() });
            Write("POST", "sound/play", "Sound", "Play a sound or announcement", typeof(SoundRequest), typeof(OkDto), r =>
            {
                SoundRequest body = r.BodyAs<SoundRequest>();
                App.PlaySound(Required(body.Sound, "sound"), body.Text);
                return new OkDto { Ok = true };
            }, "`announceRace`, `announceResults`, `hurryUp`, `untilRaceStart`, `timeRemaining`, `raceOver`, `flag` (also records a flag), `custom1`…`custom5`, `say` (speaks `text`), or any sound name from GET /sound.");
            Write("POST", "sound/stop", "Sound", "Stop the sound playing now", null, typeof(OkDto), r =>
            {
                App.PlaySound("stop", null);
                return new OkDto { Ok = true };
            });

            // OBS
            Get("obs", "OBS", "OBS remote control status", typeof(AppObsState), r => App.GetObs());
            Write("PATCH", "obs", "OBS", "Pause, resume or reconnect OBS remote control", typeof(AppObsChange), typeof(AppObsState), r =>
            {
                App.SetObs(r.BodyAs<AppObsChange>());
                return App.GetObs();
            }, "`active: false` stops FPVTrackside sending its automatic OBS changes (the same as the pause shortcut); `reconnect: true` drops and re-opens the connection.");
            Get("obs/{list}", "OBS", "List OBS scenes, sources, filters or hotkeys", typeof(ObsListDto), r => ObsList(r.Param("list")),
                "`list` is `scenes`, `sources`, `filters` or `hotkeys`. Asks OBS, so it can take a few seconds.");
            Write("PUT", "obs/scene", "OBS", "Switch OBS's program scene", typeof(ObsSceneRequest), typeof(OkDto), r =>
            {
                App.ObsSetScene(Required(r.BodyAs<ObsSceneRequest>().Scene, "scene"));
                return new OkDto { Ok = true };
            });
            Write("POST", "obs/trigger", "OBS", "Fire one of FPVTrackside's OBS triggers", typeof(ObsTriggerRequest), typeof(OkDto), r =>
            {
                App.ObsTrigger(Required(r.BodyAs<ObsTriggerRequest>().Trigger, "trigger"));
                return new OkDto { Ok = true };
            }, "Runs every OBS rule set up for that trigger (e.g. `PreRaceScene`, `RaceEnd`, `PointsTab`). Triggers are listed in GET /obs.");
            Write("POST", "obs/hotkey", "OBS", "Press an OBS hotkey by name", typeof(ObsHotkeyRequest), typeof(OkDto), r =>
            {
                App.ObsHotkey(Required(r.BodyAs<ObsHotkeyRequest>().Name, "name"));
                return new OkDto { Ok = true };
            }, "Names from GET /obs/hotkeys.");
            Write("POST", "obs/filter", "OBS", "Switch a source filter on or off", typeof(ObsFilterRequest), typeof(OkDto), r =>
            {
                ObsFilterRequest body = r.BodyAs<ObsFilterRequest>();
                App.ObsSourceFilter(Required(body.Source, "source"), Required(body.Filter, "filter"), body.Enabled);
                return new OkDto { Ok = true };
            });
            Get("obs/config", "OBS", "OBS remote control settings and rules", typeof(AppObsConfig), r => App.GetObsConfig(),
                "The password is never returned (`********` when one is set).");
            Write("PUT", "obs/config", "OBS", "Replace OBS remote control settings and rules", typeof(AppObsConfig), typeof(AppObsConfig), r =>
            {
                App.SetObsConfig(r.BodyAs<AppObsConfig>());
                Log("OBS config saved");
                return App.GetObsConfig();
            }, "Saved and applied straight away (the connection is re-opened). Leave `password` null or `********` to keep the saved one. A rule's `action` is `scene` (scene), `filter` (source, filter, enable), `hotkeyName` (hotkey) or `hotkeySequence` (hotkey = an OBS key name such as `OBS_KEY_F1`, modifiers = e.g. `Control, Shift`).");

            // Settings
            Get("settings", "Settings", "List the settings groups and their fields", typeof(AppSettingsInfo), r => App.SettingsGroups().ToList(), array: true);
            Get("settings/{group}", "Settings", "Get a settings group", typeof(object), r => App.GetSettings(r.Param("group")),
                "Field names and types are in GET /settings. Secrets (passwords, keys, tokens) come back as `********`.");
            Write("PATCH", "settings/{group}", "Settings", "Change settings", null, typeof(AppSettingsResult), r =>
            {
                JObject values = r.BodyAs<JObject>();
                AppSettingsResult result = App.SetSettings(r.Param("group"), values);
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

        private static readonly string[] Sounds = new[]
        {
            "announceRace", "announceResults", "hurryUp", "untilRaceStart", "timeRemaining", "raceOver", "flag",
            "custom1", "custom2", "custom3", "custom4", "custom5", "say", "stop",
        }.Concat(Enum.GetNames(typeof(Sound.SoundKey)).Where(n => n != "None")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        private object ObsList(string list)
        {
            string what = (list ?? "").ToLowerInvariant();
            if (what != "scenes" && what != "sources" && what != "filters" && what != "hotkeys")
                throw ApiException.NotFound("OBS list '" + list + "' (use scenes, sources, filters or hotkeys)");
            Task<string[]> task = App.ObsList(what);
            return task.ContinueWith(t => (object)new ObsListDto { Items = (t.Result ?? new string[0]).ToList() });
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
