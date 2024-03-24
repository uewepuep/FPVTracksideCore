using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Tools;
using Composition;

namespace Sound
{
    public class SoundManager : IDisposable
    {
        private EventManager eventManager;
        private SoundEffectManager soundEffectManager;

        private SpeechManager speechManager;
        private Dictionary<SoundKey, Sound> sounds;

        private const string filename = @"Sounds.xml";

        public IEnumerable<Sound> Sounds
        {
            get
            {
                return sounds.Values;
            }
            set
            {
                sounds.Clear();

                foreach (Sound s in value)
                {
                    AddSound(s);
                }

                WriteSettings();
            }
        }

        public int[] NextRaceStartsInTimesToAnnounce { get; set; }
        public bool NextRaceTimer { get; set; }

        private Race lastAnnounced;
        private bool announceConnection;
        private bool initialised;

        public static SoundManager Instance { get; private set; }

        public float SillyNameChance;

        public bool MuteTTS
        {
            get
            {
                if (speechManager == null)
                    return true;

                return speechManager.Muted;
            }
            set
            {
                if (speechManager == null)
                    return;

                speechManager.Muted = value;
            }
        }

        public bool MuteWAV
        {
            get
            {
                if (soundEffectManager == null)
                    return true;

                return soundEffectManager.Muted;
            }
            set
            {
                if (soundEffectManager == null)
                    return;

                soundEffectManager.Muted = value;
            }
        }

        public Profile Profile { get; private set; }

        public Units Units { get; set; }

        public event Action<Pilot> OnHighlightPilot;

        private WorkQueue backgroundQueue;
        private bool backgroundQueueStopping;

        public SoundManager(EventManager eventManager, Profile profile)
        {
            this.eventManager = eventManager;
            Profile = profile;
            Units = Units.Metric;

            backgroundQueue = new WorkQueue("Sound Manager Background");


            announceConnection = false;
            Instance = this;

            soundEffectManager = new SoundEffectManager();

            NextRaceStartsInTimesToAnnounce = new int[0];

            InitSounds();

            Logger.SoundLog.LogCall(this);

            if (eventManager != null)
            {
                eventManager.RaceManager.OnRaceStart += RaceManager_OnRaceStart;
                eventManager.RaceManager.OnRaceEnd += RaceOver;
                eventManager.RaceManager.OnLapDetected += Lap;
                eventManager.RaceManager.OnSplitDetection += Sector;
                eventManager.LapRecordManager.OnNewOveralBest += OnNewRecord;
                eventManager.RaceManager.OnRaceChanged += OnRaceChanged;
                eventManager.RaceManager.OnRaceCancelled += RaceManager_OnRaceCancelled;
                eventManager.SpeedRecordManager.OnSpeedCalculated += OnSpeed;
                eventManager.RaceManager.OnSplitDetection += OnSplit;
            }
        }

        public void Dispose()
        {
            backgroundQueue?.Dispose();
            backgroundQueue = null;

            if (eventManager != null)
            {
                eventManager.RaceManager.OnRaceStart -= RaceManager_OnRaceStart;
                eventManager.RaceManager.OnRaceEnd -= RaceOver;
                eventManager.RaceManager.OnLapDetected -= Lap;
                eventManager.RaceManager.OnSplitDetection -= Sector;
                eventManager.LapRecordManager.OnNewOveralBest -= OnNewRecord;
                eventManager.RaceManager.OnRaceChanged -= OnRaceChanged;
                eventManager.RaceManager.OnRaceCancelled -= RaceManager_OnRaceCancelled;
                eventManager.SpeedRecordManager.OnSpeedCalculated -= OnSpeed;
                eventManager.RaceManager.OnSplitDetection -= OnSplit;

            }

            Logger.SoundLog.LogCall(this);
            speechManager?.Dispose();
            speechManager = null;

            soundEffectManager?.Dispose();
            soundEffectManager = null;
        }


        public void SetupSpeaker(PlatformTools platformTools, string voice, int volume)
        {
            speechManager = new SpeechManager(platformTools, voice, volume);
        }

        public bool HasSpeech()
        {
            if (speechManager == null)
            {
                return false;
            }

            return speechManager.HasSpeech();
        }

        public void WaitForInit()
        {
            while (!initialised)
            {
                Thread.Sleep(10);
            }
        }

        private void InitSounds()
        {
            InitSounds( false);
        }

        private void InitSounds(bool reset)
        {
            try
            {
                sounds = new Dictionary<SoundKey, Sound>();
                try
                {
                    Sounds = IOTools.Read<Sound>(Profile, filename);
                }
                catch
                {
                }

                Sound[] defaultSounds = new Sound[]
                {
                    new Sound() { Key = SoundKey.StartRaceIn, TextToSpeech = "Arm your quads. Starting on the tone in less than {time}", Category = Sound.SoundCategories.Race },
                    new Sound() { Key = SoundKey.RaceStart, TextToSpeech = "Go", Filename = @"sounds/tone.wav", Category = Sound.SoundCategories.Race },
                    new Sound() { Key = SoundKey.RaceOver, TextToSpeech = "Race over", Category = Sound.SoundCategories.Race },

                    new Sound() { Key = SoundKey.TimesUp, TextToSpeech = "Times Up", Filename = @"sounds/horn.wav", Category = Sound.SoundCategories.Race },
                    new Sound() { Key = SoundKey.TimeRemaining, TextToSpeech = "{time} remaining", Category = Sound.SoundCategories.Race },
                    new Sound() { Key = SoundKey.AfterTimesUp, TextToSpeech = "Finish your lap and then land", Category = Sound.SoundCategories.Race },
                    new Sound() { Key = SoundKey.StaggeredStart, TextToSpeech = "Arm your quads. Start on your name", Category = Sound.SoundCategories.Race },
                    new Sound() { Key = SoundKey.StaggeredPilot, TextToSpeech = "{pilot}", Category = Sound.SoundCategories.Race },

                    new Sound() { Key = SoundKey.RaceAnnounce, TextToSpeech = "Next up Round {round} {type} {race} {bracket}. ", Category = Sound.SoundCategories.Announcements },
                    new Sound() { Key = SoundKey.RaceAnnounceResults, TextToSpeech = "Results of Round {round} {type} {race} {bracket}. ", Category = Sound.SoundCategories.Announcements },

                    new Sound() { Key = SoundKey.PilotChannel, TextToSpeech = "{pilot} on {band}{channel}", Category = Sound.SoundCategories.Announcements },
                    new Sound() { Key = SoundKey.PilotResult, TextToSpeech = "{pilot} {position}", Category = Sound.SoundCategories.Announcements },

                    new Sound() { Key = SoundKey.NameTest, TextToSpeech = "{pilot}", Category = Sound.SoundCategories.Announcements },
                    new Sound() { Key = SoundKey.HurryUp, TextToSpeech = "Hurry Up {pilot}", Category = Sound.SoundCategories.Announcements },
                    
                    
                    new Sound() { Key = SoundKey.Detection, TextToSpeech = "BEEP", Filename = @"sounds/detection.wav", Category = Sound.SoundCategories.Detection },
                    new Sound() { Key = SoundKey.DetectionSplit, TextToSpeech = "beep", Filename = @"sounds/split.wav", Category = Sound.SoundCategories.Detection },
                    new Sound() { Key = SoundKey.Sector, TextToSpeech = "{pilot} sector {count} in {position}", Enabled = false, Category = Sound.SoundCategories.Detection },

                    new Sound() { Key = SoundKey.RaceDone, TextToSpeech = "{pilot} finished in {position}", Category = Sound.SoundCategories.Detection },
                    new Sound() { Key = SoundKey.RaceLap, TextToSpeech = "{pilot} lap {lapnumber} in {position}", Category = Sound.SoundCategories.Detection },

                    new Sound() { Key = SoundKey.TimeTrialLap, TextToSpeech = "{pilot} {count} lap{s} in {lapstime}", Category = Sound.SoundCategories.Detection },
                    new Sound() { Key = SoundKey.TimeTrialDone, TextToSpeech = "{pilot} finished {count} lap{s} in {lapstime}, Please land", Category = Sound.SoundCategories.Detection },
                    new Sound() { Key = SoundKey.PracticeLap, TextToSpeech = "{pilot} {laptime}", Category = Sound.SoundCategories.Detection },
                    new Sound() { Key = SoundKey.CasualLap, TextToSpeech = "{pilot} {count} lap{s} in {lapstime}", Category = Sound.SoundCategories.Detection },
                    new Sound() { Key = SoundKey.Holeshot, TextToSpeech = "Holeshot {pilot} {laptime}", Category = Sound.SoundCategories.Detection },

                    new Sound() { Key = SoundKey.NewLapRecord, TextToSpeech = "{pilot} new record {count} lap{s} in {lapstime}", Enabled = false, Category = Sound.SoundCategories.Records },
                    new Sound() { Key = SoundKey.NewHoleshotRecord, TextToSpeech = "{pilot} new record holeshot in {time}", Enabled = false, Category = Sound.SoundCategories.Records },

                    new Sound() { Key = SoundKey.Speed, TextToSpeech = "{pilot} {speed} {speedunits}", Enabled = false, Category = Sound.SoundCategories.Detection },

                    new Sound() { Key = SoundKey.StandDownCancelled, TextToSpeech = "Stand down and disarm, Race Start Cancelled", Category = Sound.SoundCategories.Status},
                    new Sound() { Key = SoundKey.StandDownTimingSystem, TextToSpeech = "Stand down and disarm, Timing system failure", Category = Sound.SoundCategories.Status },
                    new Sound() { Key = SoundKey.TimingSystemDisconnected, TextToSpeech = "Timing system disconnected", Category = Sound.SoundCategories.Status },
                    new Sound() { Key = SoundKey.TimingSystemConnected, TextToSpeech = "Timing system connected", Category = Sound.SoundCategories.Status },
                    new Sound() { Key = SoundKey.TimingSystemsConnected, TextToSpeech = "{count} Timing systems connected", Category = Sound.SoundCategories.Status },

                    new Sound() { Key = SoundKey.NoVideoDelayingRace, TextToSpeech = " Race start delayed as {pilot} has no video. Race starts in {time}", Category = Sound.SoundCategories.Race },

                    new Sound() { Key = SoundKey.UntilRaceStart, TextToSpeech = "{time} until the race start", Category = Sound.SoundCategories.Announcements },


                    new Sound() { Key = SoundKey.Custom1, TextToSpeech = "Custom sound 1", Category = Sound.SoundCategories.Announcements },
                    new Sound() { Key = SoundKey.Custom2, TextToSpeech = "Custom sound 2", Category = Sound.SoundCategories.Announcements },
                    new Sound() { Key = SoundKey.Custom3, TextToSpeech = "Custom sound 3", Category = Sound.SoundCategories.Announcements },
                    new Sound() { Key = SoundKey.Custom4, TextToSpeech = "Custom sound 4", Category = Sound.SoundCategories.Announcements },
                    new Sound() { Key = SoundKey.Custom5, TextToSpeech = "Custom sound 5", Category = Sound.SoundCategories.Announcements },
                };

                foreach (Sound defaultSound in defaultSounds)
                {
                    Sound settingsSound;
                    if (sounds.TryGetValue(defaultSound.Key, out settingsSound))
                    {
                        if (settingsSound.Filename != null && !System.IO.File.Exists(settingsSound.Filename) || reset)
                        {
                            sounds[defaultSound.Key] = defaultSound;
                        }
                    }
                    else
                    {
                        // if its missing add it..
                        sounds.Add(defaultSound.Key, defaultSound);
                    }
                }

                // preload all the waves
                soundEffectManager.LoadSounds(sounds.Where(s => s.Value.HasFile).Select(s => s.Value.Filename));

                WriteSettings();
                initialised = true;
            }
            catch (Exception ex)
            {
                Logger.SoundLog.LogException(this, ex);
            }
        }

        public void Reset()
        {
            InitSounds(true);
        }
        private void OnSplit(Detection detection)
        {
            PlaySound(SoundKey.DetectionSplit, new SpeechParameters());
        }

        private void RaceManager_OnRaceStart(Race race)
        {
            if (!eventManager.RaceManager.StaggeredStart)
            {
                Start();
            }
        }

        private void OnRaceChanged(Race race)
        {
            if (race != null && eventManager.RaceManager.CanRunRace)
            {
                AnnounceRace(race);
            }
        }

        public Sound GetSound(SoundKey key)
        {
            return sounds[key];
        }

        public void AnnounceRace(Race race, bool force = false)
        {
            if (race == null)
                return;

            if (race.Type == EventTypes.CasualPractice)
                return;

            Sound soundPilotChannel = GetSound(SoundKey.PilotChannel);
            if (soundPilotChannel == null)
                return;

            if (race == lastAnnounced && !force)
                return;

            lastAnnounced = race;

            if (!race.Pilots.Any())
                return;

            backgroundQueueStopping = false;
            backgroundQueue.Clear();
            backgroundQueue.Enqueue(() =>
            {
                SpeechParameters parameters = new SpeechParameters();
                parameters.Priority = 1000;
                parameters.SecondsExpiry = 10;
                parameters.Add(SpeechParameters.Types.round, race.RoundNumber);
                parameters.Add(SpeechParameters.Types.race, race.RaceNumber);
                parameters.Add(SpeechParameters.Types.type, RaceStringFormatter.Instance.GetEventTypeText(race.Type));

                if (race.Bracket == Race.Brackets.None)
                {
                    parameters.Add(SpeechParameters.Types.bracket, "");
                }
                else
                {
                    parameters.Add(SpeechParameters.Types.bracket, race.Bracket);
                }
                
                if (backgroundQueueStopping)
                {
                    HighlightPilot(null);
                    return;
                }

                PlaySoundBlocking(SoundKey.RaceAnnounce, parameters);

                foreach (PilotChannel pc in race.PilotChannels.OrderBy(a => a.Channel.Frequency))
                {
                    if (pc.Pilot == null || pc.Channel == null)
                        continue;

                    SpeechParameters pilotChannelParameters = new SpeechParameters();
                    pilotChannelParameters.Priority = 1000;
                    pilotChannelParameters.SecondsExpiry = 60;
                    pilotChannelParameters.Add(SpeechParameters.Types.pilot, pc.Pilot.Phonetic);
                    pilotChannelParameters.Add(SpeechParameters.Types.band, pc.Channel.GetSpokenBandLetter());
                    pilotChannelParameters.Add(SpeechParameters.Types.channel, pc.Channel.Number);

                    if (backgroundQueueStopping)
                        break;

                    HighlightPilot(pc.Pilot);
                    PlaySoundBlocking(SoundKey.PilotChannel, pilotChannelParameters);
                }
                HighlightPilot(null);
            });
        }

        private void HighlightPilot(Pilot pilot)
        {
            OnHighlightPilot?.Invoke(pilot);
        }

        public void AnnounceResults(Race race)
        {
            if (race == null)
                return;

            if (race.Type == EventTypes.CasualPractice)
                return;

            Sound soundPilotChannel = GetSound(SoundKey.PilotResult);
            if (soundPilotChannel == null)
                return;

            if (!race.Pilots.Any())
                return;

            backgroundQueueStopping = false;
            backgroundQueue.Clear();
            backgroundQueue.Enqueue(() =>
            {
                if (backgroundQueueStopping)
                {
                    HighlightPilot(null);
                    return;
                }

                Result[] results = eventManager.ResultManager.GetResults(race).OrderBy(r => r.Position).ToArray();

               
                SpeechParameters parameters = new SpeechParameters();
                parameters.Priority = 1000;
                parameters.SecondsExpiry = 10;
                parameters.Add(SpeechParameters.Types.round, race.RoundNumber);
                parameters.Add(SpeechParameters.Types.race, race.RaceNumber);
                parameters.Add(SpeechParameters.Types.type, RaceStringFormatter.Instance.GetEventTypeText(race.Type));

                if (race.Bracket == Race.Brackets.None)
                {
                    parameters.Add(SpeechParameters.Types.bracket, "");
                }
                else
                {
                    parameters.Add(SpeechParameters.Types.bracket, race.Bracket);
                }

                PlaySoundBlocking(SoundKey.RaceAnnounceResults, parameters);
                if (backgroundQueueStopping)
                    return;

                foreach (Result result in results)
                {
                    if (result == null || result.Pilot == null)
                        continue;

                    SpeechParameters pilotChannelParameters = new SpeechParameters();
                    pilotChannelParameters.Add(SpeechParameters.Types.pilot, result.Pilot.Phonetic);
                    pilotChannelParameters.Add(SpeechParameters.Types.position, result.DNF ? "DNF" : result.Position);

                    if (backgroundQueueStopping)
                        break;

                    HighlightPilot(result.Pilot);
                    PlaySoundBlocking(SoundKey.PilotResult, pilotChannelParameters);
                }

                HighlightPilot(null);
            });
        }

        public void PilotChannel(Pilot pilot, Channel channel)
        {
            SpeechParameters parameters = new SpeechParameters();
            parameters.Priority = 1000;
            parameters.SecondsExpiry = 30;
            parameters.Add(SpeechParameters.Types.pilot, pilot.Phonetic);
            parameters.Add(SpeechParameters.Types.band, channel.GetSpokenBandLetter());
            parameters.Add(SpeechParameters.Types.channel, channel.Number);

            PlaySound(SoundKey.PilotChannel, parameters);
        }

        public void WriteSettings()
        {
            IOTools.Write(Profile, filename, sounds.Values.OrderBy(s => s.Key).ToArray());
        }

        private void AddSound(Sound sound)
        {
            if (!sounds.ContainsKey(sound.Key))
            {
                sounds.Add(sound.Key, sound);
            }
        }

        public void HurryUpEveryone()
        {
            PlaySound(SoundKey.HurryUp, new SpeechParameters(SpeechParameters.Types.pilot, "everyone"));
        }

        public void HurryUp(Pilot p)
        {
            PlaySound(SoundKey.HurryUp, new SpeechParameters(SpeechParameters.Types.pilot, p.Phonetic));
        }

        public void SpeakName(Pilot obj)
        {
            PlaySound(SoundKey.NameTest, new SpeechParameters(SpeechParameters.Types.pilot, obj.Phonetic));
        }

        public void SponsorRead(string text, TimeSpan expiry)
        {
            StopSound();

            SpeechRequest speechRequest = new SpeechRequest(text, 0, 100, new SpeechParameters(), DateTime.Now + expiry, null);
            speechManager?.EnqueueSpeech(speechRequest);
        }

        private void OnSpeed(Split split, float speedms)
        {
            int speed = eventManager.SpeedRecordManager.SpeedToUnit(speedms, Units);
            string units = UnitToWords();

            SpeechParameters parameters = new SpeechParameters();
            parameters.Priority = 0;
            parameters.Add(SpeechParameters.Types.speedunits, units);
            parameters.Add(SpeechParameters.Types.speed, speed);
            parameters.Add(SpeechParameters.Types.pilot, split.Pilot.Phonetic);

            PlaySound(SoundKey.Speed, parameters);
        }

        public string UnitToWords()
        {
            switch (Units)
            {
                case Units.Imperial:
                    return "miles per hour";
                case Units.Metric:
                default:
                    return "kilometers per hour";
            }
        }

        public void StopSound()
        {
            backgroundQueueStopping = true;
            speechManager?.StopSpeech();
        }

        public void PlaySound(SoundKey soundKey)
        {
            PlaySound(soundKey, new SpeechParameters());
        }

        public void PlayTestSound(SoundKey soundKey)
        {
            SpeechParameters parameters = SpeechParameters.Random(this);
            parameters.Forced = true;
            PlaySound(soundKey, parameters);
        }

        public SoundRequest PlaySound(SoundKey soundKey, SpeechParameters soundParameters)
        {
            return PlaySound(soundKey, null, soundParameters);
        }

        private SoundRequest PlaySound(SoundKey soundKey, System.Action onFinished = null)
        {
            return PlaySound(soundKey, onFinished, new SpeechParameters());
        }

        private SoundRequest PlaySound(SoundKey soundKey, System.Action onFinished, SpeechParameters parameters)
        {
            Sound sound;
            if (!sounds.TryGetValue(soundKey, out sound))
            {
                if (onFinished != null)
                {
                    onFinished();
                }

                Logger.SoundLog.Log(this, "Couldn't found sound key", soundKey);
                return null;
            }

            if (!sound.Enabled && !parameters.Forced)
            {
                if (onFinished != null)
                {
                    onFinished();
                }
                return null;
            }

            TimeSpan expiry = TimeSpan.FromSeconds(parameters.SecondsExpiry);

            SoundRequest request;

            if (sound.HasFile)
            {
                SoundEffectRequest effectRequest = new SoundEffectRequest(sound.Filename, parameters.Priority, sound.Volume, DateTime.Now + expiry, onFinished);
                soundEffectManager?.EnqueueSoundEffect(effectRequest);
                request = effectRequest;
            }
            else
            {
                SpeechRequest speechRequest = new SpeechRequest(sound.TextToSpeech, sound.Rate, sound.Volume, parameters, DateTime.Now + expiry, onFinished);
                speechManager?.EnqueueSpeech(speechRequest);
                request = speechRequest;
            }

            return request;
        }

        public void PlaySoundBlocking(SoundKey soundKey, SpeechParameters soundParameters)
        {
            using (AutoResetEvent autoResetEvent = new AutoResetEvent(false))
            {
                PlaySound(soundKey, () => { autoResetEvent.Set(); }, soundParameters);

                autoResetEvent.WaitOne(TimeSpan.FromSeconds(soundParameters.SecondsExpiry));
            }
        }


        public void StartRaceIn(TimeSpan timeSpan, System.Action onFinishedSpeech)
        {
            SpeechParameters sp = new SpeechParameters(SpeechParameters.Types.time, timeSpan.TotalSeconds.ToString());
            sp.Priority = 10000;
            sp.SecondsExpiry = 120;

            PlaySound(SoundKey.StartRaceIn, onFinishedSpeech, sp);
        }

        public void Start()
        {
            PlaySound(SoundKey.RaceStart, new SpeechParameters() { Priority = 10000 });
        }


        public void RaceOver(Race race)
        {
            RaceOver();
        }

        public void RaceOver()
        {
            StopSound();
            PlaySound(SoundKey.RaceOver, new SpeechParameters() { Priority = 10000 });
        }

        public void TimesUp(Race race)
        {
            if (race == null)
                return;

            SpeechParameters parameters = new SpeechParameters();
            parameters.Priority = 9000;

            Action onFinished = null;
            if (race.Type != EventTypes.Freestyle)
            {
                onFinished = () =>
                {
                    PlaySound(SoundKey.AfterTimesUp, parameters);
                };
            }

            StopSound();
            PlaySound(SoundKey.TimesUp, onFinished, parameters);
        }

        public void TimeRemaining(Race race, TimeSpan timeRemaining)
        {
            if (race == null)
                return;

            SpeechParameters parameters = new SpeechParameters();
            parameters.Priority = 9000;

            parameters.AddTime(SpeechParameters.Types.time, timeRemaining);
            PlaySound(SoundKey.TimeRemaining, parameters);
        }

        private int LapNumberToPriority(Detection detection, int position)
        {
            int lapNumber = Math.Min(9, detection.LapNumber);

            return (lapNumber * 100) - position;
        }


        public void Lap(Lap lap)
        {
            if (lap == null) return;

            PlaySound(SoundKey.Detection, new SpeechParameters());

            Lap[] validLaps = lap.Race.GetValidLapsLast(lap.Pilot, lap.Race.TargetLaps);

            SpeechParameters parameters = new SpeechParameters();
            parameters.Priority = 0;
            if (lap.Pilot.SillyName == null)
            {
                parameters.Add(SpeechParameters.Types.pilot, lap.Pilot.Phonetic);
            }
            else
            {
                parameters.Add(SpeechParameters.Types.pilot, new Random().NextDouble() < SillyNameChance ? lap.Pilot.SillyName : lap.Pilot.Phonetic);
            }
            parameters.Add(SpeechParameters.Types.lapnumber, lap.Number.ToString());
            parameters.Add(SpeechParameters.Types.count, validLaps.Length);
            parameters.AddRaceTime(SpeechParameters.Types.laptime, lap.Length);
            parameters.AddRaceTime(SpeechParameters.Types.lapstime, validLaps.TotalTime());
            parameters.AddRaceTime(SpeechParameters.Types.racetime, lap.EndRaceTime);
            parameters.AddRaceTime(SpeechParameters.Types.time, lap.EndRaceTime);
            parameters.Add(SpeechParameters.Types.s, validLaps.Length == 1 ? "" : "s");

            // Skip lap 0.
            if (lap.Number == 0)
            {
                if (lap.Race.TotalLaps == 1)
                {
                    parameters.Priority = 10;
                    PlaySound(SoundKey.Holeshot, parameters);
                }
                return;
            }

            switch (eventManager.RaceManager.RaceType)
            {
                case EventTypes.AggregateLaps:
                case EventTypes.Race:
                    {
                        int position = lap.Race.GetPosition(lap.Pilot);

                        parameters.Priority = LapNumberToPriority(lap.Detection, position);

                        // don't read out laps after the end of the race.
                        if (lap.Number > lap.Race.TargetLaps && eventManager.RaceManager.RaceType == EventTypes.Race)
                            return;

                        parameters.Add(SpeechParameters.Types.position, position);

                        if (lap.Number == lap.Race.TargetLaps)
                        {
                            parameters.Priority = 1000;
                            parameters.SecondsExpiry = 30;
                            PlaySound(SoundKey.RaceDone, parameters);
                        }
                        else
                        {
                            PlaySound(SoundKey.RaceLap, parameters);
                        }
                        break;
                    }
                case EventTypes.TimeTrial:
                    {
                        int position = eventManager.LapRecordManager.GetPosition(lap.Pilot, lap.Race.TargetLaps);

                        parameters.Add(SpeechParameters.Types.position, position);

                        if (eventManager.RaceManager.TimesUp)
                        {
                            PlaySound(SoundKey.TimeTrialDone, parameters);
                        }
                        else // normal in time..
                        {
                            parameters.Priority = LapNumberToPriority(lap.Detection, position);
                            PlaySound(SoundKey.TimeTrialLap, parameters);
                        }

                        break;
                    }
                case EventTypes.CasualPractice:
                    {
                        int position = eventManager.LapRecordManager.GetPosition(lap.Pilot, lap.Race.TargetLaps);
                        parameters.Add(SpeechParameters.Types.position, position);
                        parameters.Priority = LapNumberToPriority(lap.Detection, position);
                        PlaySound(SoundKey.CasualLap, parameters);
                        break;
                    }

                case EventTypes.Practice:
                    PlaySound(SoundKey.PracticeLap, parameters);
                    break;
            }
        }

        public void StaggeredStart(Action p)
        {
            StopSound();
            SpeechParameters sp = new SpeechParameters();
            sp.Priority = 1000;
            sp.SecondsExpiry = 120;

            PlaySound(SoundKey.StaggeredStart, p, sp);
        }

        public void StaggeredPilot(PilotChannel pilotChannel)
        {
            StopSound();

            SpeechParameters sp = new SpeechParameters();
            sp.Add(SpeechParameters.Types.pilot, pilotChannel.Pilot.Phonetic);
            sp.Add(SpeechParameters.Types.channel, pilotChannel.Channel);
            sp.Priority = 1000;
            sp.SecondsExpiry = 120;

            PlaySound(SoundKey.StaggeredPilot, sp);
        }

        private void Sector(Detection detection)
        {
            Race race = eventManager.RaceManager.CurrentRace;

            if (race.Type != EventTypes.Race)
                return;

            Pilot pilot = detection.Pilot;

            if (detection.LapNumber > race.TargetLaps)
                return;

            if (race != null && pilot != null)
            {
                int position = race.GetPosition(pilot);

                SpeechParameters parameters = new SpeechParameters();
                parameters.Add(SpeechParameters.Types.pilot, pilot.Phonetic);
                parameters.Add(SpeechParameters.Types.count, detection.SectorNumber.ToString());
                parameters.Add(SpeechParameters.Types.position, position);
                parameters.Priority = LapNumberToPriority(detection, position);

                PlaySound(SoundKey.Sector, parameters);
            }
        }

        private void OnNewRecord(Pilot pilot, int lapCount, Lap[] laps)
        {
            SpeechParameters parameters = new SpeechParameters();
            parameters.Priority = 1000;
            parameters.Add(SpeechParameters.Types.pilot, pilot.Phonetic);
            parameters.Add(SpeechParameters.Types.count, laps.Length.ToString());

            if (lapCount == 0)
            {
                parameters.Add(SpeechParameters.Types.racetime, laps[0].Length);
                PlaySound(SoundKey.NewHoleshotRecord, parameters);
            }
            else
            {
                parameters.Add(SpeechParameters.Types.racetime, laps.TotalTime());
                PlaySound(SoundKey.NewLapRecord, parameters);
            }
        }

        private void RaceManager_OnRaceCancelled(Race arg1, bool failure)
        {
            StopSound();
            if (failure)
            {
                TimingSystemFailure();
            }
            else
            {
                StandDown();
            }
        }

        private void StandDown()
        {
            PlaySound(SoundKey.StandDownCancelled, new SpeechParameters() { Priority = 10000 });
        }

        private void TimingSystemFailure()
        {
            PlaySound(SoundKey.StandDownTimingSystem, new SpeechParameters() { Priority = 10000 });
        }

        public void TimingSystemDisconnected()
        {
            PlaySound(SoundKey.TimingSystemDisconnected,
                () =>
                {
                    announceConnection = true;
                },
                new SpeechParameters() { Priority = 10000 });
        }

        public void TimingSystemsConnected(int count)
        {
            if (!announceConnection)
            {
                return;
            }
            announceConnection = false;

            SpeechParameters parameters = new SpeechParameters();
            parameters.Priority = 10000;

            if (count == 1)
            {
                PlaySound(SoundKey.TimingSystemConnected, parameters);
            }
            else
            {
                parameters.Add(SpeechParameters.Types.count, count.ToString());
                PlaySound(SoundKey.TimingSystemsConnected, parameters);
            }
        }

        public void PlayTimeUntilNextRace(TimeSpan time)
        {
            SpeechParameters soundParameters = new SpeechParameters();
            soundParameters.SecondsExpiry = 1;
            soundParameters.AddTime(SpeechParameters.Types.time, time);
            PlaySound(SoundKey.UntilRaceStart, soundParameters);
        }

        public void PlayVideoIssuesDelayRace(TimeSpan time, Pilot pilot)
        {
            StopSound();

            SpeechParameters soundParameters = new SpeechParameters();
            soundParameters.AddTime(SpeechParameters.Types.time, time);
            soundParameters.Add(SpeechParameters.Types.pilot, pilot.Phonetic);
            soundParameters.Priority = 1111;
            soundParameters.SecondsExpiry = 10;
            PlaySound(SoundKey.NoVideoDelayingRace, soundParameters);
        }
    }

    class SoundWorkItem : WorkItem
    {
        public int Priority { get; set; }

    }

    public abstract class SoundRequest
    {
        public DateTime Expiry { get; private set; }
        public int Priority { get; private set; }
        public System.Action OnFinish { get; private set; }

        public int Volume { get; private set; }

        public SoundRequest(int priority, int volume, DateTime expiry, Action onFinish)
        {
            Priority = priority;
            Expiry = expiry;
            OnFinish = onFinish;
            Volume = volume;
        }
    }
}
