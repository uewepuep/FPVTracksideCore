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

        private static string filename = @"data/Sounds.xml";

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

        public SoundManager(EventManager eventManager)
        {
            announceConnection = false;
            Instance = this;

            soundEffectManager = new SoundEffectManager();

            NextRaceStartsInTimesToAnnounce = new int[0];

            InitSounds();

            Logger.SoundLog.LogCall(this);

            this.eventManager = eventManager;

            if (eventManager != null)
            {
                eventManager.RaceManager.OnRaceStart += (r) =>
                {
                    if (!eventManager.RaceManager.StaggeredStart)
                    {
                        Start();
                    }
                };
                eventManager.RaceManager.OnRaceEnd += (r) => { RaceOver(); };
                eventManager.RaceManager.OnLapDetected += (lap) => { Lap(lap); };
                eventManager.RaceManager.OnSplitDetection += Sector;
                eventManager.LapRecordManager.OnNewOveralBest += OnNewRecord;
                eventManager.RaceManager.OnRaceEnd += OnRaceEnd;
                eventManager.RaceManager.OnRaceChanged += OnRaceChanged;
                eventManager.RaceManager.OnRaceCancelled += RaceManager_OnRaceCancelled;
            }
        }

        public void SetupSpeaker(PlatformTools platformTools, string voice)
        {
            speechManager = new SpeechManager(platformTools, voice);
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
            InitSounds(false);
        }

        private void InitSounds(bool reset)
        {
            try
            {
                sounds = new Dictionary<SoundKey, Sound>();
                try
                {
                    Sounds = IOTools.Read<Sound>(filename);
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

                new Sound() { Key = SoundKey.AnnounceRace, TextToSpeech = "Next up Round {round} {type} {race} {bracket}, With {pilots}", Category = Sound.SoundCategories.Announcements },
                new Sound() { Key = SoundKey.AnnounceRaceResults, TextToSpeech = "Results of Round {round} {type} {race} {bracket}. {pilots}", Category = Sound.SoundCategories.Announcements },
                new Sound() { Key = SoundKey.UntilNextRace, TextToSpeech = "{time} until the next race", Category = Sound.SoundCategories.Announcements },
                new Sound() { Key = SoundKey.NameTest, TextToSpeech = "{pilot}", Category = Sound.SoundCategories.Announcements },
                new Sound() { Key = SoundKey.HurryUp, TextToSpeech = "Hurry Up {pilot}", Category = Sound.SoundCategories.Announcements },
                new Sound() { Key = SoundKey.PilotChannel, TextToSpeech = "{pilot} on {band}{channel}", Category = Sound.SoundCategories.Announcements },

                new Sound() { Key = SoundKey.PilotResult, TextToSpeech = "{pilot} {position}", Category = Sound.SoundCategories.Announcements },

                new Sound() { Key = SoundKey.Detection, TextToSpeech = "beep", Filename = @"sounds/detection.wav", Category = Sound.SoundCategories.Detection },
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


                new Sound() { Key = SoundKey.StandDownCancelled, TextToSpeech = "Stand down and disarm, Race Start Cancelled", Category = Sound.SoundCategories.Status},
                new Sound() { Key = SoundKey.StandDownTimingSystem, TextToSpeech = "Stand down and disarm, Timing system failure", Category = Sound.SoundCategories.Status },
                new Sound() { Key = SoundKey.TimingSystemDisconnected, TextToSpeech = "Timing system disconnected", Category = Sound.SoundCategories.Status },
                new Sound() { Key = SoundKey.TimingSystemConnected, TextToSpeech = "Timing system connected", Category = Sound.SoundCategories.Status },
                new Sound() { Key = SoundKey.TimingSystemsConnected, TextToSpeech = "{count} Timing systems connected", Category = Sound.SoundCategories.Status },
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

        private void OnRaceChanged(Race race)
        {
            if (race != null && eventManager.RaceManager.CanRunRace)
            {
                AnnounceRace(race);
            }
        }

        private void OnRaceEnd(Race race)
        {
            eventManager.TimedActionManager.Cancel(TimedActionManager.ActionTypes.NextRaceCallout);
            eventManager.TimedActionManager.Enqueue(DateTime.Now.AddSeconds(5), TimedActionManager.ActionTypes.NextRaceCallout, () =>
            {
                Race next = eventManager.RaceManager.GetNextRace(true);
                if (next != null)
                {
                    NextRaceStarts(next);
                }
            });
        }

        private void NextRaceStarts(Race race)
        {
            if (NextRaceTimer && NextRaceStartsInTimesToAnnounce.Any())
            {
                eventManager.TimedActionManager.Cancel(TimedActionManager.ActionTypes.NextRaceStarts);

                DateTime end = DateTime.Now.AddSeconds(NextRaceStartsInTimesToAnnounce.Max());

                foreach (int seconds in NextRaceStartsInTimesToAnnounce)
                {
                    DateTime time = end.AddSeconds(-seconds);
                    TimeSpan timespan = TimeSpan.FromSeconds(seconds);
                    eventManager.TimedActionManager.Enqueue(time, TimedActionManager.ActionTypes.NextRaceStarts, () =>
                    {
                        if (eventManager.RaceManager.CurrentRace == race || eventManager.RaceManager.GetNextRace(true) == race)
                        {
                            if (!eventManager.RaceManager.RaceRunning && !eventManager.RaceManager.PreRaceStartDelay)
                            {
                                SpeechParameters soundParameters = new SpeechParameters();
                                soundParameters.AddTime(SpeechParameters.Types.time, timespan);
                                PlaySound(SoundKey.UntilNextRace, soundParameters);
                            }
                        }

                    });
                }
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

            List<SpeechParameters> subSoundParameters = new List<SpeechParameters>();
            foreach (PilotChannel pc in race.PilotChannels.OrderBy(a => a.Channel.Frequency))
            {
                if (pc.Pilot == null || pc.Channel == null)
                    continue;

                SpeechParameters pilotChannelParameters = new SpeechParameters();
                pilotChannelParameters.Add(SpeechParameters.Types.pilot, pc.Pilot.Phonetic);
                pilotChannelParameters.Add(SpeechParameters.Types.band, pc.Channel.Band.GetCharacter());
                pilotChannelParameters.Add(SpeechParameters.Types.channel, pc.Channel.Number);

                subSoundParameters.Add(pilotChannelParameters);
            }

            SpeechParameters parameters = new SpeechParameters();
            parameters.Priority = 1000;
            parameters.SecondsExpiry = 10;
            parameters.AddSubParameters(SpeechParameters.Types.pilots, soundPilotChannel, subSoundParameters);
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

            PlaySound(SoundKey.AnnounceRace, parameters);
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

            Result[] results = eventManager.ResultManager.GetResults(race).OrderBy(r => r.Position).ToArray();

            List<SpeechParameters> subSoundParameters = new List<SpeechParameters>();
            foreach (Result result in results)
            {
                if (result.Pilot == null)
                    continue;

                SpeechParameters pilotChannelParameters = new SpeechParameters();
                pilotChannelParameters.Add(SpeechParameters.Types.pilot, result.Pilot.Phonetic);
                pilotChannelParameters.Add(SpeechParameters.Types.position, result.DNF ? "DNF" : result.Position);
                subSoundParameters.Add(pilotChannelParameters);
            }

            SpeechParameters parameters = new SpeechParameters();
            parameters.Priority = 1000;
            parameters.SecondsExpiry = 10;
            parameters.AddSubParameters(SpeechParameters.Types.pilots, soundPilotChannel, subSoundParameters);
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

            PlaySound(SoundKey.AnnounceRaceResults, parameters);
        }

        public void PilotChannel(Pilot pilot, Channel channel)
        {
            SpeechParameters parameters = new SpeechParameters();
            parameters.Priority = 1000;
            parameters.SecondsExpiry = 30;
            parameters.Add(SpeechParameters.Types.pilot, pilot.Phonetic);
            parameters.Add(SpeechParameters.Types.band, channel.Band.GetCharacter());
            parameters.Add(SpeechParameters.Types.channel, channel.Number);

            PlaySound(SoundKey.PilotChannel, parameters);
        }

        public void WriteSettings()
        {
            IOTools.Write(filename, sounds.Values.OrderBy(s => s.Key).ToArray());
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
            SpeechRequest speechRequest = new SpeechRequest(text, 0, new SpeechParameters(), DateTime.Now + expiry, null);
            speechManager?.EnqueueSpeech(speechRequest);
        }

        public void Dispose()
        {
            Logger.SoundLog.LogCall(this);
            speechManager?.Dispose();
            speechManager = null;

            soundEffectManager?.Dispose();
            soundEffectManager = null;
        }

        public void StopSound()
        {
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

        private SoundRequest PlaySound(SoundKey soundKey, SpeechParameters soundParameters)
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
                SoundEffectRequest effectRequest = new SoundEffectRequest(sound.Filename, parameters.Priority, DateTime.Now + expiry, onFinished);
                soundEffectManager?.EnqueueSoundEffect(effectRequest);
                request = effectRequest;
            }
            else
            {
                SpeechRequest speechRequest = new SpeechRequest(sound.TextToSpeech, sound.Rate, parameters, DateTime.Now + expiry, onFinished);
                speechManager?.EnqueueSpeech(speechRequest);
                request = speechRequest;
            }

            return request;
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

        public void RaceOver()
        {
            StopSound();
            PlaySound(SoundKey.RaceOver, new SpeechParameters() { Priority = 10000 });
        }

        public void RaceTimerElapsed(Race race, TimeSpan timeRemaining)
        {
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

            if (timeRemaining == TimeSpan.Zero)
            {
                StopSound();
                PlaySound(SoundKey.TimesUp, onFinished, parameters);
            }
            else
            {
                parameters.AddTime(SpeechParameters.Types.time, timeRemaining);
                PlaySound(SoundKey.TimeRemaining, parameters);
            }
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
            parameters.Add(SpeechParameters.Types.pilot, lap.Pilot.Phonetic);
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
            StopSound();
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

        public SoundRequest(int priority, DateTime expiry, Action onFinish)
        {
            Priority = priority;
            Expiry = expiry;
            OnFinish = onFinish;
        }
    }
}
