using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RaceLib;
using RaceLib.Format;
using Sound;
using Sound.AutoCommentator;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Tools;
using UI.Nodes;

namespace UI
{
    public class AutoRunner : IDisposable
    {
        public EventLayer EventLayer { get; private set; }

        public EventManager EventManager { get; private set; }
        public RaceManager RaceManager { get { return EventManager.RaceManager; } }
        public SoundManager SoundManager { get; private set; }

        public SceneManagerNode SceneManager { get; private set; }

        public AutoCrashOut AutoCrashOut { get { return EventLayer.AutoCrashOut; } }

        public AutoRunnerConfig Config { get; private set; }

        public TimeSpan Timer
        {
            get
            {
                TimeSpan timer = TimerEnd - DateTime.Now;
                if (timer < TimeSpan.Zero)
                {
                    return TimeSpan.Zero;
                }

                return timer;
            }
            set
            {
                TimerEnd = DateTime.Now + value;
            }
        }

        public bool TimesUp
        {
            get 
            { 
                return Timer <= TimeSpan.Zero;
            }
        }

        public DateTime TimerEnd { get; private set; }

        public DateTime NextRaceStartTime
        {
            get
            {
                switch (State)
                {
                    case States.WaitingRaceStart:
                        return TimerEnd;

                    case States.WaitingResults:
                        return TimerEnd + TimeSpan.FromSeconds(Config.SecondsToNextRace);
                        
                    case States.WaitVideo:
                        return TimerEnd;
                }

                return DateTime.Now + TimeSpan.FromSeconds(Config.SecondsToNextRace);
            }
        }

        private DateTime lastUpdate;

        public enum States
        {
            None,
            WaitingResults,
            WaitingRaceStart,
            WaitingRaceFinalLap,
            WaitVideo

        }

        public States State { get; private set; }

        public bool Paused { get; private set; }

        private TimeSpan pausedAt;

        public AutoRunner(EventLayer eventLayer) 
        { 
            EventLayer = eventLayer;
            EventManager = eventLayer.EventManager;
            SoundManager = eventLayer.SoundManager;

            LoadConfig(EventManager.Profile);

            RaceManager.OnRacePreStart += RaceManager_OnRacePreStart;
            RaceManager.OnRaceChanged += RaceManager_OnRaceChanged;
            RaceManager.OnRaceCreated += RaceManager_OnRaceCreated;
            RaceManager.OnRaceEnd += OnRaceEnd;

            lastUpdate = DateTime.Now;

            TogglePause();
        }

        public void Dispose()
        {
            RaceManager.OnRacePreStart -= RaceManager_OnRacePreStart;
            RaceManager.OnRaceChanged -= RaceManager_OnRaceChanged;
            RaceManager.OnRaceCreated -= RaceManager_OnRaceCreated;
            RaceManager.OnRaceEnd -= OnRaceEnd;
            SceneManager.OnSceneChange -= SceneManager_OnSceneChange;
        }

        private void RaceManager_OnRaceCreated(Race race)
        {
            if (SceneManager.Scene == SceneManagerNode.Scenes.RaceResults)
            {
                SetState(States.WaitingResults);
            }
        }

        public void SetSceneManager(SceneManagerNode sceneManager)
        {
            if (SceneManager != null)
            {
                SceneManager.OnSceneChange -= SceneManager_OnSceneChange;
            }

            SceneManager = sceneManager;
            SceneManager.OnSceneChange += SceneManager_OnSceneChange;
        }

        private void RaceManager_OnRacePreStart(Race race)
        {
            SetState(States.None);
        }

        private void SceneManager_OnSceneChange(SceneManagerNode.Scenes scene)
        {
            switch (scene)
            {
                case SceneManagerNode.Scenes.RaceResults:
                    SetState(States.WaitingResults);
                    break;
                case SceneManagerNode.Scenes.PreRace:
                    SetState(States.WaitingRaceStart);
                    break;
                case SceneManagerNode.Scenes.Fullscreen:
                    // do nothing
                    break;
                default: 
                    SetState(States.None); 
                    break;
            }
        }

        private void RaceManager_OnRaceChanged(Race race)
        {
            if (RaceManager.RaceFinished)
            {
                SetState(States.WaitingResults);
            }
            else
            {
                SetState(States.WaitingRaceStart);
            }
        }

        public void LoadConfig(Profile profile)
        {
            Config = AutoRunnerConfig.Load(profile);
        }

        public void SetState(States newState)
        {
            if (newState == State)
                return;

            Logger.AutoRunner.LogCall(this, State, newState);

            bool hasNextRace = RaceManager.GetNextRace(true) != null;

            State = newState;
            switch (State) 
            {
                case States.WaitingRaceStart:
                    Timer = TimeSpan.FromSeconds(Config.SecondsToNextRace);
                    break;

                case States.WaitingRaceFinalLap:
                    Timer = TimeSpan.FromSeconds(Config.SecondsToFinishFinalLapAfterTimesUp);
                    break;

                case States.WaitingResults:
                    // No next race..
                    if (!hasNextRace)
                    {
                        if (!Paused && Config.AutoCreateRounds)
                        {
                            GenerateRound();
                        }
                        else
                        {
                            SetState(States.None);
                            return;
                        }
                    }

                    Timer = TimeSpan.FromSeconds(Config.SecondsToShowResults);
                    break;

                case States.WaitVideo:
                    Timer = TimeSpan.FromSeconds(Config.SecondsDelayIfStatic);
                    break;
            }
        }

        private void GenerateRound()
        {
            Race current = RaceManager.CurrentRace;

            if (current == null)
            {
                SetState(States.None);
                return;
            }

            Round round = current.Round;
            if (round == null)
            {
                SetState(States.None);
                return;
            }

            RoundPlan roundPlan;

            switch (Config.AutoCreateRoundsType)
            {
                case AutoRunnerConfig.AutoCreateRoundsTypes.KeepChannels:
                    roundPlan = new RoundPlan(EventManager, round);
                    roundPlan.ChannelChange = RoundPlan.ChannelChangeEnum.KeepFromPreviousRound;
                    EventManager.RoundManager.GenerateRound(roundPlan);
                    break;

                case AutoRunnerConfig.AutoCreateRoundsTypes.RandomChannels:
                    roundPlan = new RoundPlan(EventManager, round);
                    roundPlan.ChannelChange = RoundPlan.ChannelChangeEnum.Change;
                    EventManager.RoundManager.GenerateRound(roundPlan);
                    break;

                case AutoRunnerConfig.AutoCreateRoundsTypes.CloneLast:
                    EventManager.RoundManager.CloneRound(round);
                    break;
            }
        }

        public void Update()
        {
            DateTime now = DateTime.Now;

            if (Paused)
            {
                Timer = pausedAt;
                return;
            }

            if (!RaceManager.RaceRunning && !EventManager.RaceManager.PreRaceStartDelay)
            {
                if (TimesUp)
                {
                    switch (State)
                    {
                        case States.WaitingRaceStart:

                            Channel channel;

                            if (CheckVideo(out channel))
                            {
                                if (!EventLayer.StartRace())
                                    State = States.None;
                            }
                            else
                            {
                                Pilot pilot = RaceManager.GetPilot(channel);
                                if (pilot != null)
                                {
                                    SetState(States.WaitVideo);
                                    SoundManager.PlayVideoIssuesDelayRace(pilot);
                                    SoundManager.PlayTimeUntilNextRace(Timer);
                                }
                                else
                                {
                                    if (!EventLayer.StartRace())
                                        State = States.None;
                                }
                            }
                            break;

                        case States.WaitVideo:
                            if (!EventLayer.StartRace())
                                State = States.None;
                            break;

                        case States.WaitingResults:
                            RaceManager.NextRace(true);
                            break;

                        case States.WaitingRaceFinalLap:
                            if (RaceManager.RaceFinished)
                            {
                                SetState(States.WaitingResults);
                            }
                            break;
                    }
                }

                if (Config.AnnounceNextRaceIn)
                {
                    AnnounceNextRaceUpdate(now);
                }
            }

            // Auto End races.
            if (RaceManager.RaceRunning)
            {
                if (RaceManager.RaceType == EventTypes.Race)
                {
                    // If all laps are finished.
                    if (EventLayer.EventManager.RaceManager.HasFinishedAllLaps())
                    {
                        EventLayer.StopRace();
                    }
                }

                // if the time is up, plus the extra final lap time is up.
                if (RaceManager.TimesUp)
                {
                    if (State == States.None)
                    {
                        SetState(States.WaitingRaceFinalLap);
                    }

                    if (TimesUp)
                    {
                        EventLayer.StopRace();
                    }
                }
            }

        }

        private bool CheckVideo(out Channel badChannel)
        {
            badChannel = null;
            if (!Config.CheckPilotsVideo)
                return true;

            Race race = RaceManager.CurrentRace;
            if (race == null) 
                return false;

            if (AutoCrashOut == null)
                return true;

            if (!AutoCrashOut.Enabled)
                return true;

            foreach (Channel channel in race.Channels) 
            {
                if (!AutoCrashOut.HasMotion(channel))
                {
                    badChannel = channel;
                    return false;
                }
            }
            return true;
        }

        private void AnnounceNextRaceUpdate(DateTime now)
        {
            Race nextRace = RaceManager.GetNextRace(true, true);
            if (nextRace != null)
            {
                DateTime nextRaceStartTime = NextRaceStartTime;

                if (State == States.None)
                {
                    lastUpdate = now;
                    return;
                }

                TimeSpan lastTimeToRace = nextRaceStartTime - lastUpdate;
                TimeSpan currentTimeToRace = nextRaceStartTime - now;

                // If we're more than 5 seconds out.. ignore
                if (Math.Abs(lastTimeToRace.TotalSeconds - currentTimeToRace.TotalSeconds) > 1)
                {
                    lastUpdate = now;
                    return;
                }

                foreach (int seconds in Config.NextRaceInAnnounceSeconds.Distinct().OrderByDescending(c => c))
                {
                    TimeSpan callAt = TimeSpan.FromSeconds(seconds);
                    if (callAt >= currentTimeToRace && callAt < lastTimeToRace)
                    {
                        if (!EventManager.RaceManager.RaceRunning && !EventManager.RaceManager.PreRaceStartDelay)
                        {
                            Logger.AutoRunner.LogCall(this, callAt, lastTimeToRace, currentTimeToRace);
                            SoundManager.PlayTimeUntilNextRace(callAt);
                            break;
                        }
                    }
                }
            }

            lastUpdate = now;
        }

        private void OnRaceEnd(Race race)
        {
            SetState(States.WaitingResults);
        }

        public void TogglePause()
        {
            Paused = !Paused;

            if (Paused) 
            {
                pausedAt = Timer;
            }

            Logger.AutoRunner.LogCall(this, Paused, pausedAt);
        }
    }

    public class AutoRunnerConfig
    {
        [Category("Auto Runner")]
        [DisplayName("Seconds to show results")]
        public int SecondsToShowResults { get; set; }

        [Category("Auto Runner")]
        [DisplayName("Seconds to next race (inc. results)")]
        public int SecondsToNextRace { get; set; }

        [Category("Auto Runner")]
        [DisplayName("Seconds to finish lap after timer runs out")]
        public int SecondsToFinishFinalLapAfterTimesUp { get; set; }


        [Category("Video")]
        public bool CheckPilotsVideo { get; set; }

        [Category("Video")]
        public int SecondsDelayIfStatic { get; set; }


        [Category("Sound")]
        public bool AnnounceNextRaceIn { get; set; }

        [Category("Sound")]
        public int[] NextRaceInAnnounceSeconds { get; set; }

        [Category("Rounds")]
        public bool AutoCreateRounds { get; set; }

        public enum AutoCreateRoundsTypes
        {
            KeepChannels,
            RandomChannels,
            CloneLast
        }
        [Category("Rounds")]
        public AutoCreateRoundsTypes AutoCreateRoundsType { get; set; }

        public AutoRunnerConfig()
        {
            CheckPilotsVideo = true;
            SecondsDelayIfStatic = 90;

            SecondsToNextRace = 300;
            SecondsToShowResults = 60;
            SecondsToFinishFinalLapAfterTimesUp = 10;

            NextRaceInAnnounceSeconds = new int[] { 10, 20, 30, 45, 60, 90, 120, 180, 240, 300 };
            AnnounceNextRaceIn = false;
            AutoCreateRounds = false;
            AutoCreateRoundsType = AutoCreateRoundsTypes.CloneLast;
        }

        protected const string filename = "AutoRunnerConfig.xml";
        public static AutoRunnerConfig Load(Profile profile)
        {
            AutoRunnerConfig config = new AutoRunnerConfig();

            bool error = false;
            try
            {
                AutoRunnerConfig[] s = IOTools.Read<AutoRunnerConfig>(profile, filename);

                if (s != null && s.Any())
                {
                    config = s[0];
                    Write(profile, config);
                }
                else
                {
                    error = true;
                }
            }
            catch
            {
                error = true;
            }

            if (error)
            {
                AutoRunnerConfig s = new AutoRunnerConfig();
                Write(profile,s);
                config = s;
            }

            return config;
        }

        public static void Write(Profile profile, AutoRunnerConfig s)
        {
            IOTools.Write(profile, filename, s);
        }

        public override string ToString()
        {
            return "Auto Runner Config";
        }
    }
}
