using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using Sound;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UI.OBSRemoteControlManager;
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

        private DateTime lastUpdate;

        public enum States
        {
            None,
            WaitingRaceEnd,
            WaitingResults,
            WaitingRaceStart
        }

        public States State { get; private set; }

        public bool Paused { get; private set; }

        private TimeSpan pausedAt;

        public AutoRunner(EventLayer eventLayer) 
        { 
            EventLayer = eventLayer;
            EventManager = eventLayer.EventManager;
            SoundManager = eventLayer.SoundManager;

            LoadConfig();

            RaceManager.OnRacePreStart += RaceManager_OnRacePreStart;
            RaceManager.OnRaceChanged += RaceManager_OnRaceChanged;
            RaceManager.OnRaceEnd += OnRaceEnd;

            lastUpdate = DateTime.Now;
        }

        public void SetSceneManager(SceneManagerNode sceneManager)
        {
            SceneManager = sceneManager;
            sceneManager.OnSceneChange += SceneManager_OnSceneChange;
        }

        private void RaceManager_OnRacePreStart(Race race)
        {
            SetState(States.None);
        }

        public void Dispose()
        {
            RaceManager.OnRaceChanged -= RaceManager_OnRaceChanged;
            RaceManager.OnRaceEnd -= OnRaceEnd;
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

        public void LoadConfig()
        {
            Config = AutoRunnerConfig.Load();
        }

        public void SetState(States newState)
        {
            bool hasNextRace = RaceManager.GetNextRace(true) != null;

            switch (newState) 
            {
                case States.WaitingRaceStart:
                    Timer = TimeSpan.FromSeconds(Config.SecondsToNextRace);
                    break;
                case States.WaitingRaceEnd:
                    Timer = TimeSpan.FromSeconds(Config.SecondsToFinishFinalLapAfterTimesUp);
                    break;
                case States.WaitingResults:

                    // No next race..
                    if (!hasNextRace)
                    {
                        State = States.None;
                        return;
                    }

                    Timer = TimeSpan.FromSeconds(Config.SecondsToShowResults);
                    break;
            }
            State = newState;
        }

        public void Update()
        {
            DateTime now = DateTime.Now;

            if (Paused)
            {
                Timer = pausedAt;

                lastUpdate = now;
                return;
            }

            if (!RaceManager.RaceRunning && !EventManager.RaceManager.PreRaceStartDelay)
            {
                if (Config.AnnounceNextRaceIn)
                {
                    AnnounceNextRaceUpdate(now);
                }

                if (Config.AutoRunRaces && TimesUp)
                {
                    switch (State)
                    {
                        case States.WaitingRaceStart:
                            if (CheckVideo())
                            {
                                EventLayer.StartRace();
                            }
                            break;

                        case States.WaitingResults:
                            RaceManager.NextRace(true);
                            break;
                    }
                }
            }

            // Auto End races.
            if (RaceManager.RaceRunning && Config.AutoRunRaces)
            {
                // If all laps are finished.
                if (EventLayer.EventManager.RaceManager.HasFinishedAllLaps())
                {
                    RaceManager.EndRace();
                }

                // if the time is up, plus the extra final lap time is up.
                if (RaceManager.TimesUp)
                {
                    if (State != States.WaitingRaceEnd)
                    {
                        SetState(States.WaitingRaceEnd);
                    }

                    if (TimesUp)
                    {
                        RaceManager.EndRace();
                    }
                }
            }

            lastUpdate = now;
        }

        private bool CheckVideo()
        {
            return true;
        }

        private void AnnounceNextRaceUpdate(DateTime now)
        {
            Race nextRace = RaceManager.GetNextRace(true, true);
            if (nextRace != null)
            {
                DateTime nextRaceStartTime;

                switch (State)
                {
                    case States.WaitingRaceStart:
                        nextRaceStartTime = TimerEnd;
                        break;
                    case States.WaitingResults:
                        nextRaceStartTime = TimerEnd + TimeSpan.FromSeconds(Config.SecondsToNextRace);
                        break;
                    default:
                        return;

                }

                TimeSpan lastTimeToRace = nextRaceStartTime - lastUpdate;
                TimeSpan currentTimeToRace = nextRaceStartTime - now;

                foreach (int seconds in Config.NextRaceInAnnounceSeconds.OrderByDescending(c => c))
                {
                    TimeSpan callAt = TimeSpan.FromSeconds(seconds);
                    if (callAt >= currentTimeToRace && callAt < lastTimeToRace)
                    {
                        if (!EventManager.RaceManager.RaceRunning && !EventManager.RaceManager.PreRaceStartDelay)
                        {
                            SpeechParameters soundParameters = new SpeechParameters();
                            soundParameters.AddTime(SpeechParameters.Types.time, callAt);
                            SoundManager.PlaySound(SoundKey.UntilNextRace, soundParameters);
                            break;
                        }
                    }
                }
            }
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
        }
    }

    public class AutoRunnerConfig
    {
        [Category("Auto Runner")]
        public bool AutoRunRaces { get; set; }

        [Category("Auto Runner")]
        public int SecondsToShowResults { get; set; }

        [Category("Auto Runner")]
        public int SecondsToNextRace { get; set; }

        [Category("Auto Runner")]
        public int SecondsToFinishFinalLapAfterTimesUp { get; set; }


        [Category("Video")]
        public bool CheckPilotsVideo { get; set; }

        [Category("Video")]
        public int SecondsDelayIfStatic { get; set; }


        [Category("Sound")]
        public bool AnnounceNextRaceIn { get; set; }

        [Category("Sound")]
        public int[] NextRaceInAnnounceSeconds { get; set; }

        public AutoRunnerConfig()
        {
            CheckPilotsVideo = true;
            SecondsDelayIfStatic = 90;

            SecondsToNextRace = 300;
            SecondsToShowResults = 60;
            SecondsToFinishFinalLapAfterTimesUp = 10;

            NextRaceInAnnounceSeconds = new int[] { 10, 20, 30, 45, 60, 90, 120, 180, 240, 300 };
            AnnounceNextRaceIn = false;
        }

        protected const string filename = @"data/AutoRunnerConfig.xml";
        public static AutoRunnerConfig Load()
        {
            AutoRunnerConfig config = new AutoRunnerConfig();

            bool error = false;
            try
            {
                AutoRunnerConfig[] s = IOTools.Read<AutoRunnerConfig>(filename);

                if (s != null && s.Any())
                {
                    config = s[0];
                    Write(config);
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
                Write(s);
                config = s;
            }

            return config;
        }

        public static void Write(AutoRunnerConfig s)
        {
            IOTools.Write(filename, s);
        }

        public override string ToString()
        {
            return "Auto Runner Config";
        }
    }
}
