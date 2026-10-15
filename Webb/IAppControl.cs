using RaceLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Webb
{
    // What the REST API needs from the FPVTrackside window: the things only the
    // UI can do. Implemented in UI by EventLayerAppControl; every member is
    // called on the UI thread and does what the matching button, menu item or
    // shortcut does. Name matching, checks and errors are the API's job
    // (Webb/Api/AppApi.cs), so names passed in here are exact.
    public interface IAppControl
    {
        // Tabs, by their button text, in order.
        IEnumerable<AppTabInfo> Tabs { get; }
        string CurrentTab { get; }
        void ShowTab(string tab);

        // The Live tab's scenes (SceneManagerNode.Scenes names).
        IEnumerable<string> Scenes { get; }
        string CurrentScene { get; }
        void ShowLive();
        void ShowScene(string scene);
        void ShowCommentators();

        AppView View { get; }
        void SetView(AppViewChange change);

        // The race-director buttons currently shown (start, stop, clear…).
        IEnumerable<string> Buttons { get; }

        void StartRaceWithVideoCheck();
        void StopRace();
        void EmergencyStop();
        void ClearRace();
        void ResumeRace();
        void StartStopNext();
        void NextRace();
        // The Next button: back to the last finished race when none is
        // selected, otherwise the next race that hasn't been run.
        void NextButton();
        void VideoCheck();

        bool HasReplay(Race race);
        void ShowReplay(Race race);
        void ToggleCrashedOut(int channelGroup);

        // When the auto runner will start the next race (null without one).
        DateTime? NextRaceStartTime { get; }

        // OBS remote control. ObsAvailable is false when it's switched off.
        bool ObsEnabled { get; }
        bool ObsConnected { get; }
        bool ObsAvailable { get; }
        bool ObsActive { get; set; }
        IEnumerable<string> ObsTriggers { get; }
        void ObsReconnect();
        Task<string[]> ObsList(string what);
        void ObsSetScene(string scene);
        void ObsTrigger(string trigger);
        void ObsHotkey(string name);
        void ObsSourceFilter(string source, string filter, bool enabled);
        AppObsConfig GetObsConfig();
        // Throws ArgumentException for a rule it can't use.
        void SetObsConfig(AppObsConfig config);

        IEnumerable<AppSettingsGroup> SettingsGroups { get; }

        // Commands only the window can run: dialogs, new windows, web pages
        // and data. The API adds race, tab, scene, view and sound commands.
        IEnumerable<AppCommand> Commands { get; }

        // Leaving the event. These dispose the web server, so the API calls
        // them after the response has gone.
        void OpenEvent(Guid eventId);
        void BackToEventSelector();
        void Restart();
    }

    public class AppView
    {
        public bool PilotListVisible { get; set; }
        public bool WormVisible { get; set; }
        public bool LapsVisible { get; set; }
        public bool SubtitlesEnabled { get; set; }
        public bool AutoRunnerPaused { get; set; }
        public string AutoRunnerState { get; set; }
        public double? AutoRunnerTimerSeconds { get; set; }
    }

    public class AppSettingsGroup
    {
        public string Name { get; set; }
        public string Summary { get; set; }
        public Type Type { get; set; }
        public Func<object> Load { get; set; }
        public Action<object> Save { get; set; }
        public Func<PropertyInfo, bool> NeedsRestart { get; set; }
    }

    public class AppCommand
    {
        public string Name { get; set; }
        public string Group { get; set; }
        public string Summary { get; set; }
        public bool RaceControl { get; set; }
        public Action Run { get; set; }

        public AppCommand(string name, string group, string summary, Action run, bool raceControl = false)
        {
            Name = name;
            Group = group;
            Summary = summary;
            Run = run;
            RaceControl = raceControl;
        }
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
