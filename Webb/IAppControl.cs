using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Webb
{
    // Everything the REST API can make the FPVTrackside window do beyond the
    // race data itself: which tab and scene is showing, the race-director
    // buttons, sounds, OBS, settings, dialogs and windows. Implemented by
    // UI.EventLayer; every member is called on the UI thread.
    //
    // Failures are reported by throwing AppControlException, whose status and
    // code the API passes straight back to the caller.
    public interface IAppControl
    {
        AppState GetState();

        // Tabs and scenes. Names are the ones listed in AppState.
        void ShowTab(string tab);
        void ShowScene(string scene);
        void SetView(AppViewChange change);

        // The race-director buttons, exactly as if they had been pressed.
        void StartRace(bool videoCheck);
        void StopRace(bool emergency);
        void ClearRace();
        void ResumeRace();
        void StartStopNext();
        void NextRace(bool unfinishedOnly);

        void ShowReplay(Guid raceId);
        void ToggleCrashedOut(int channelGroup);

        void PlaySound(string sound, string text);

        // OBS. Listing talks to OBS over the network, so it returns a task the
        // API waits on outside the UI thread.
        AppObsState GetObs();
        void SetObs(AppObsChange change);
        Task<string[]> ObsList(string what);
        void ObsSetScene(string scene);
        void ObsTrigger(string trigger);
        void ObsHotkey(string name);
        void ObsSourceFilter(string source, string filter, bool enabled);
        AppObsConfig GetObsConfig();
        void SetObsConfig(AppObsConfig config);

        // Settings groups: whole objects in and out (JSON-compatible).
        IEnumerable<AppSettingsInfo> SettingsGroups();
        object GetSettings(string group);
        AppSettingsResult SetSettings(string group, Newtonsoft.Json.Linq.JObject values);

        // Anything else on screen: the generic command catalogue (menu items,
        // keyboard-shortcut actions, dialogs, windows).
        IEnumerable<AppCommandInfo> Commands();
        void RunCommand(string name);

        // Leaving the event. Both tear down the web server, so they run after
        // the response has gone.
        void OpenEvent(Guid eventId);
        void BackToEventSelector();
        void Restart();
    }

    public class AppControlException : Exception
    {
        public int Status { get; private set; }
        public string Code { get; private set; }

        public AppControlException(int status, string code, string message)
            : base(message)
        {
            Status = status;
            Code = code;
        }

        public static AppControlException NotFound(string what) { return new AppControlException(404, "not_found", what + " not found"); }
        public static AppControlException Conflict(string message) { return new AppControlException(409, "conflict", message); }
        public static AppControlException BadRequest(string message) { return new AppControlException(400, "bad_request", message); }
    }

    public class AppState
    {
        public string Tab { get; set; }
        public List<AppTabInfo> Tabs { get; set; }
        public string Scene { get; set; }
        public List<string> Scenes { get; set; }
        public bool PilotListVisible { get; set; }
        public bool WormVisible { get; set; }
        public bool LapsVisible { get; set; }
        public bool SubtitlesEnabled { get; set; }
        public bool MuteTts { get; set; }
        public bool MuteWav { get; set; }
        public bool AutoRunnerPaused { get; set; }
        public string AutoRunnerState { get; set; }
        public double? AutoRunnerTimerSeconds { get; set; }
        public bool ReplayAvailable { get; set; }
        public bool RaceRunning { get; set; }
        public List<string> Buttons { get; set; }
        public List<AppTimingSystem> TimingSystems { get; set; }
        public AppObsState Obs { get; set; }
    }

    public class AppTabInfo
    {
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public bool Visible { get; set; }
    }

    public class AppTimingSystem
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public bool Connected { get; set; }
        public string Status { get; set; }
    }

    // A partial change: null leaves a setting as it is.
    public class AppViewChange
    {
        public bool? PilotListVisible { get; set; }
        public bool? WormVisible { get; set; }
        public bool? LapsVisible { get; set; }
        public bool? SubtitlesEnabled { get; set; }
        public bool? MuteTts { get; set; }
        public bool? MuteWav { get; set; }
        public bool? AutoRunnerPaused { get; set; }
        public double? AutoRunnerAddSeconds { get; set; }
        public int? ChannelVisibility { get; set; }
        public bool? ReorderChannels { get; set; }
    }

    public class AppObsState
    {
        public bool Enabled { get; set; }
        public bool Connected { get; set; }
        public bool Active { get; set; }
        public List<string> Triggers { get; set; }
    }

    public class AppObsChange
    {
        public bool? Active { get; set; }
        public bool? Reconnect { get; set; }
    }

    public class AppObsConfig
    {
        public bool Enabled { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Password { get; set; }
        public List<AppObsRule> Rules { get; set; }
    }

    // One "when this happens in FPVTrackside, do that in OBS" rule.
    public class AppObsRule
    {
        public string Trigger { get; set; }
        public string Action { get; set; }   // scene | filter | hotkeyName | hotkeySequence
        public string Scene { get; set; }
        public string Source { get; set; }
        public string Filter { get; set; }
        public bool? Enable { get; set; }
        public string Hotkey { get; set; }
        public string Modifiers { get; set; }
    }

    public class AppSettingsInfo
    {
        public string Name { get; set; }
        public string Summary { get; set; }
        public List<AppSettingField> Fields { get; set; }
    }

    public class AppSettingField
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public string Category { get; set; }
        public string Type { get; set; }
        public List<string> Values { get; set; }
        public bool NeedsRestart { get; set; }
    }

    public class AppSettingsResult
    {
        public object Settings { get; set; }
        public List<string> Changed { get; set; }
        public bool NeedsRestart { get; set; }
    }

    public class AppCommandInfo
    {
        public string Name { get; set; }
        public string Group { get; set; }
        public string Summary { get; set; }
        public bool RaceControl { get; set; }
    }
}
