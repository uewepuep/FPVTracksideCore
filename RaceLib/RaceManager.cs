using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Timing;
using Tools;

namespace RaceLib
{
    public class RaceManager : IDisposable
    {
        public TimingSystemManager TimingSystemManager { get; private set; }

        public Race CurrentRace { get; private set; }

        public event Action<Detection> OnSplitDetection;

        public event Lap.LapDelegate OnLapDetected;
        public event Action<IEnumerable<Lap>> OnLapSplit;
        public event Lap.LapDelegate OnLapDisqualified;
        public event Action<Race, Detection> OnDetectionDisqualified;

        public event Race.OnRaceEvent OnLapsRecalculated;
        public event Race.OnRaceEvent OnRaceCreated;
        public event Race.OnRaceEvent OnRaceStart;
        public event Race.OnRaceEvent OnRacePreStart;
        public event Race.OnRaceEvent OnRaceChanged;
        public event Race.OnRaceEvent OnRaceResumed;

        public event Race.OnRaceEvent OnRaceReset;
        public event Race.OnRaceEvent OnRaceEnd;
        public event Race.OnRaceEvent OnRaceClear;
        public event Action<Race, bool> OnRaceCancelled;
        public event Action<Race, TimeSpan> OnRaceTimeRemaining;

        public event Race.OnRaceEvent OnRaceRemoved;

        public event PilotChannelDelegate OnPilotAdded;
        public event PilotChannelDelegate OnPilotRemoved;

        public bool CallOutPilotsBeforeRaceStart { get; set; }

        public bool CanRunRace
        {
            get
            {
                Race currentRace = CurrentRace;

                if (currentRace == null)
                    return false;

                if (currentRace.PilotCount == 0)
                    return false;

                if (currentRace.Running)
                    return false;

                if (currentRace.Ended)
                    return false;

                return true;
            }
        }

        public float PilotCount
        {
            get
            {
                Race currentRace = CurrentRace;

                if (currentRace == null)
                    return 0;

                return currentRace.PilotCount;
            }
        }


        // This race is not *complete* and is assumed to be filled out by sync..
        public Race GetCreateRace(Guid iD)
        {
            Race race = null;
            lock (races)
            {
                race = races.FirstOrDefault(r => r.ID == iD);
            }

            if (race == null)
            {
                race = new Race();
                race.ID = iD;
                race.Event = EventManager.Event;
                race.PrimaryTimingSystemLocation = EventManager.Event.PrimaryTimingSystemLocation;
                lock (races)
                {
                    races.Add(race);
                }
            }

            return race;
        }
        
        public Race GetCreateRace(Round round, int number)
        {
            Race race;
            lock (races)
            {
                race = races.FirstOrDefault(ra => ra.Round.EventType == round.EventType && ra.RoundNumber == round.RoundNumber && ra.RaceNumber == number && ra.Valid);
                if (race != null)
                {
                    return race;
                }
            }

            race = new Race();
            race.AutoAssignNumbers = false;
            race.PrimaryTimingSystemLocation = EventManager.Event.PrimaryTimingSystemLocation;
            race.Event = EventManager.Event;
            race.RaceNumber = number;
            race.Round = round;
            race.Track = Track;

            AddRace(race);
            return race;
        }

        public bool RaceFinished
        {
            get
            {
                Race currentRace = CurrentRace;

                if (currentRace == null)
                    return false;

                if (currentRace.Ended)
                    return true;

                return false;
            }
        }

        public bool RaceRunning
        {
            get
            {
                Race currentRace = CurrentRace;

                if (currentRace == null)
                    return false;

                if (currentRace.Running)
                    return true;

                return false;
            }
        }

        public bool RaceStarted { get { return RaceRunning || RaceFinished; } }

        public EventManager EventManager { get; private set; }
        public Lapalyser Lapalyser { get; private set; }

        public IEnumerable<Channel> FreeChannels
        {
            get
            {
                Race currentRace = CurrentRace;

                if (currentRace == null)
                    return EventManager.Channels;
                return EventManager.Channels.Except(currentRace.Channels);
            }
        }

        public TimeSpan ElapsedTime
        {
            get
            {
                Race currentRace = CurrentRace;

                if (RaceFinished)
                {
                    return (currentRace.End - currentRace.Start) - currentRace.TotalPausedTime;
                }
                else
                {
                    if (RaceRunning)
                    {
                        return (DateTime.Now - currentRace.Start) - currentRace.TotalPausedTime;
                    }
                    else
                    {
                        return TimeSpan.Zero;
                    }
                }
            }
        }

        public TimeSpan RemainingTime
        {
            get
            {
                if (EventManager.Event == null)
                {
                    return TimeSpan.Zero;
                }

                if (!RaceRunning && !RaceFinished)
                {
                    return EventManager.Event.RaceLength;
                }

                TimeSpan time = EventManager.Event.RaceLength - ElapsedTime;
                if (time < TimeSpan.Zero)
                    return TimeSpan.Zero;

                return time;
            }
        }

        public EventTypes EventType { get { return EventManager.Event.EventType; } set { EventManager.Event.EventType = value; } }
        public EventTypes RaceType
        {
            get
            {
                Race currentRace = CurrentRace;

                if (currentRace == null)
                {
                    return EventType;
                }

                return currentRace.Type;
            }
        }

        public int LeadLap
        {
            get
            {
                Race currentRace = CurrentRace;

                if (currentRace == null) return 0;
                return currentRace.LeadLap;
            }
        }

        public int TotalLaps
        {
            get
            {
                Race currentRace = CurrentRace;

                if (currentRace == null) return 0;
                return currentRace.TotalLaps;
            }
        }

        public Race[] Races
        {
            get
            {
                lock (races)
                {
                    return races.ToArray();
                }
            }
        }


        private List<Race> races;

        public bool PreRaceStartDelay { get; private set; }
        public bool StaggeredStart { get; private set; }


        public Track Track { get; private set; }
        public bool TimesUp
        {
            get
            {
                return RemainingTime <= TimeSpan.Zero && EventType != EventTypes.CasualPractice && EventManager.Event.RaceLength > TimeSpan.Zero;
            }
        }

        private TimeSpan lastTimeRemaining;
        private Random random;

        public int[] RemainingTimesToAnnounce { get; set; }

        public bool HasPilots
        {
            get
            {
                Race currentRace = CurrentRace;

                if (currentRace == null) return false;
                return currentRace.PilotCount > 0;
            }
        }

        public RaceManager(EventManager eventManager)
        {
            random = new Random();
            EventManager = eventManager;
            RemainingTimesToAnnounce = new int[] { 0, 10, 30, 60 };
            Lapalyser = new Lapalyser(this);

            TimingSystemManager = new TimingSystemManager();
            TimingSystemManager.DetectionEvent += OnDetection;
            TimingSystemManager.OnConnected += OnTimingSystemReconnect;
            races = new List<Race>();
        }

        public void Dispose()
        {
            TimingSystemManager.Dispose();
        }

        public int GetRaceCount(EventTypes type, Race.Brackets bracket = Race.Brackets.None)
        {
            lock (races)
            {
                return races.Count(r => r.Type == type && r.Bracket == bracket);
            }
        }

        public int GetRaceCount(Round round, Race.Brackets bracket)
        {
            lock (races)
            {
                return races.Count(r => r.Round == round && r.Bracket == bracket);
            }
        }

        public int GetRaceCount(Round round)
        {
            lock (races)
            {
                return races.Count(r => r.Round == round);
            }
        }

        public IEnumerable<int> GetRaceNumbers(Round round)
        {
            lock (races)
            {
                return races.Where(r => r.Round == round).Select(r => r.RaceNumber).OrderBy(i => i);
            }
        }

        public int GetMaxRoundNumber(EventTypes type)
        {
            lock (races)
            {
                IEnumerable<Round> ofType = EventManager.Event.Rounds.Where(r => r.EventType == type);

                if (!ofType.Any())
                {
                    return 0;
                }
                return ofType.Select(r => r.RoundNumber).Max();
            }
        }

        public bool AddPilot(Pilot p)
        {
            Channel c = EventManager.GetChannel(p);
            return AddPilot(c, p);
        }

        public bool AddPilot(Channel channel, Pilot p)
        {
            Logger.RaceLog.LogCall(this, channel, p);

            Race currentRace = CurrentRace;

            // if the current race has ended, make a new race
            if (currentRace != null && currentRace.Ended)
            {
                currentRace = null;
            }

            if (currentRace == null)
            {
                int maxRoundNumber = GetMaxRoundNumber(EventType);
                Round round = EventManager.RoundManager.GetCreateRound(maxRoundNumber, EventType);

                currentRace = new Race();
                currentRace.AutoAssignNumbers = true;
                currentRace.PrimaryTimingSystemLocation = EventManager.Event.PrimaryTimingSystemLocation;
                currentRace.Event = EventManager.Event;
                currentRace.RaceNumber = GetRaceCount(round) + 1;

                currentRace.Round = round;
                currentRace.Track = Track;
                CurrentRace = currentRace;

                AddRace(currentRace);

                OnRaceCreated?.Invoke(currentRace);
                OnRaceChanged?.Invoke(currentRace);
            }

            if (HasPilot(p))
            {
                return false;
            }

            // Only practise/freestyle can add pilots randomly.
            if (EventType != EventTypes.Practice || EventType != EventTypes.Freestyle)
            {
                if (currentRace.Running)
                {
                    Pilot other = currentRace.GetPilot(channel.Frequency);
                    if (other != p)
                    {
                        return false;
                    }
                }
            }

            if (!FreeChannels.Contains(channel))
                return false;

            if (EventManager.GetChannel(p) != channel)
            {
                EventManager.SetPilotChannel(p, channel);
            }

            PilotChannel pc;
            using (Database db = new Database())
            {
                pc = currentRace.SetPilot(db, channel, p);
            }

            if (pc != null)
            {
                if (currentRace.AutoAssignNumbers)
                {
                    AutoAssignNumbers(currentRace);
                }
                
                OnPilotAdded?.Invoke(pc);
                return true;
            }

            return false;
        }

        private void AutoAssignNumbers(Race race)
        {
            int discoverRoundNumber = DiscoverRoundNumber(race);
            int newRoundNumber = Math.Max(discoverRoundNumber, race.RoundNumber);
            race.Round = EventManager.RoundManager.GetCreateRound(newRoundNumber, EventType);
            race.RaceNumber = GetRaceNumbers(race.Round).Max() + 1;

            using (Database db = new Database())
            {
                db.Races.Update(race);
            }
        }

        public bool ChangeChannel(Channel channel, Pilot pilot)
        {
            if (RemovePilotFromCurrentRace(pilot, false))
            {
                return AddPilot(channel, pilot);
            }
            return false;
        }

        public Channel GetFreeChannel(Race race, IEnumerable<Channel> channels = null)
        {
            if (channels == null)
            {
                channels = EventManager.Channels;
            }
            return race.GetFreeFrequencies(channels).FirstOrDefault();
        }

        public Channel GetFreeChannel(Channel prefered)
        {
            return FreeChannels.OrderByDescending(c => c.Frequency == prefered.Frequency).FirstOrDefault();
        }

        public Channel GetFreeChannel(Race race, BandType bandType, IEnumerable<Channel> channels = null)
        {
            if (channels == null)
            {
                channels = EventManager.Channels;
            }
            return race.GetFreeFrequencies(channels.Where(c => c.Band.GetBandType() == bandType)).FirstOrDefault();
        }

        public bool RemovePilotFromCurrentRace(Pilot p, bool canClearRace = true)
        {
            Logger.RaceLog.LogCall(this, p);

            Race currentRace = CurrentRace;

            if (currentRace == null)
                return false;

            if (RaceStarted)
            {
                return false;
            }

            PilotChannel pc;
            using (Database db = new Database())
            {
                pc = currentRace.RemovePilot(db, p);
            }

            if (pc != null)
            {
                if (!RaceStarted && currentRace.PilotCount == 0 && canClearRace)
                {
                    RemoveRace(currentRace, true);
                    CurrentRace = null;
                }

                OnPilotRemoved?.Invoke(pc);

                return true;
            }
            return false;
        }

        public PilotChannel RemovePilot(Race race, Pilot pilot)
        {
            using (Database db = new Database())
            {
                return race.RemovePilot(db, pilot);
            }
        }

        public bool HasPilot(Pilot p)
        {
            Race currentRace = CurrentRace;

            if (currentRace == null)
                return false;

            return currentRace.HasPilot(p);
        }

        public PilotChannel ClearChannel(Channel channel)
        {
            Logger.RaceLog.LogCall(this, channel);

            Race currentRace = CurrentRace;

            if (currentRace == null)
                return null;

            if (currentRace.Running)
                return null;

            PilotChannel pc;
            using (Database db = new Database())
            {
                pc = currentRace.ClearChannel(db, channel);
                db.Races.Update(currentRace);
            }
            if (pc != null)
            {
                OnPilotRemoved?.Invoke(pc);
                return pc;
            }
            return null;
        }

        public int DiscoverRoundNumber(Race race)
        {
            if (race == null)
                return 0;

            int maxRound = 0;
            foreach (Pilot p in race.Pilots)
            {
                IEnumerable<Race> races = Races.Where(r => (r.Type == race.Type && r != race && r.Creation < race.Creation) && r.Pilots.Contains(p));
                if (races.Any())
                {
                    int round = races.Select(r => r.RoundNumber).Max();
                    maxRound = Math.Max(maxRound, round);
                }
            }

            return maxRound + 1;
        }

        public bool PreRaceStart()
        {
            OnRacePreStart?.Invoke(CurrentRace);

            PreRaceStartDelay = true;
            return true;
        }

        public bool StartRace()
        {
            return StartRaceInLessThan(EventManager.Event.MinStartDelay, EventManager.Event.MaxStartDelay);
        }
                
        public bool StartDetection()
        {
            Race currentRace = CurrentRace;
            if (currentRace == null)
            {
                return false;
            }

            List<ListeningFrequency> frequencies = new List<ListeningFrequency>();
            bool enoughFrequenciesForEvent = TimingSystemManager.MaxPilots >= EventManager.Channels.Select(c => c.Frequency).Distinct().Count();
            if (enoughFrequenciesForEvent)
            {
                foreach (Channel eventChannel in EventManager.Channels)
                {
                    ListeningFrequency listeningFrequency;
                    PilotChannel pilotChannel = currentRace.PilotChannelsSafe.FirstOrDefault(r => r.Channel.Frequency == eventChannel.Frequency);
                    if (pilotChannel != null)
                    {
                        listeningFrequency = new ListeningFrequency(eventChannel.Frequency, pilotChannel.Pilot.TimingSensitivityPercent / 100.0f);
                    }
                    else
                    {
                        listeningFrequency = new ListeningFrequency(eventChannel.Frequency, 0);
                    }

                    frequencies.Add(listeningFrequency);
                }

                Logger.RaceLog.LogCall(this, CurrentRace, "Frequencies locked to receivers");
            }
            else
            {
                if (currentRace.Type == EventTypes.CasualPractice)
                {
                    frequencies = EventManager.Channels.Select(c => new ListeningFrequency(c.Frequency, 1)).ToList();
                }
                else
                {
                    frequencies = currentRace.PilotChannelsSafe.Select(pc => new ListeningFrequency(pc.Channel.Frequency, pc.Pilot.TimingSensitivityPercent / 100.0f)).ToList();
                }

                Logger.RaceLog.LogCall(this, CurrentRace, "Frequencies dynamically assigned to receivers");
            }

            if (!TimingSystemManager.SetListeningFrequencies(frequencies))
            {
                return false;
            }

            if (!TimingSystemManager.StartDetection())
            {
                return false;
            }

            return true;
        }
        
        public bool StartStaggered(TimeSpan delay, Action<PilotChannel> onStart)
        {
            PreRaceStartDelay = false;
            StaggeredStart = true;
            DateTime now = DateTime.Now;

            Logger.RaceLog.LogCall(this, CurrentRace, delay);

            if (!CanRunRace)
            {
                StaggeredStart = false;
                return false;
            }

            Race currentRace = CurrentRace;

            if (currentRace == null)
            {
                StaggeredStart = false;
                return false;
            }

            if (!TimingSystemManager.IsDetecting)
            {
                StaggeredStart = false;
                return false;
            }

            PilotChannel[] pilotChannels = currentRace.PilotChannelsSafe.Where(pc => pc.Pilot != null)
                                          .OrderBy(pc => EventManager.LapRecordManager.GetTimePosition(pc.Pilot))
                                          .ThenBy(pc => pc.Channel.Frequency)
                                          .ToArray();

            currentRace.PrimaryTimingSystemLocation = EventManager.Event.PrimaryTimingSystemLocation;
            currentRace.TargetLaps = EventManager.Event.Laps;
            currentRace.Start = DateTime.Now;
            currentRace.End = default(DateTime);
            currentRace.AutoAssignNumbers = false;

            lock (races)
            {
                if (!races.Contains(currentRace))
                {
                    races.Add(currentRace);
                    OnRaceCreated(currentRace);
                }
            }

            using (Database db = new Database())
            {
                db.Races.Upsert(currentRace);
                CheckEventStart(db);
            }

            OnRaceStart?.Invoke(currentRace);

            foreach (PilotChannel pc in pilotChannels)
            {
                Thread.Sleep(delay);
                onStart(pc);
            }

            StaggeredStart = false;
            return true;
        }

        public bool StartRaceInLessThan(TimeSpan minDelay, TimeSpan maxDelay)
        {
            TimeSpan delayLength = maxDelay - minDelay;
            TimeSpan randomTime = minDelay + TimeSpan.FromMilliseconds(delayLength.TotalMilliseconds * random.NextDouble());
            DateTime now = DateTime.Now;
            DateTime startTime = now + randomTime;

            Logger.RaceLog.LogCall(this, CurrentRace, minDelay, maxDelay, randomTime);

            if (!CanRunRace)
            {
                PreRaceStartDelay = false;
                return false;
            }

            Race currentRace = CurrentRace;

            if (currentRace == null)
            {
                PreRaceStartDelay = false;
                return false;
            }
            
            if (currentRace.Type != EventTypes.Freestyle)
            {
                if(!TimingSystemManager.IsDetecting)
                {
                    PreRaceStartDelay = false;
                    return false;
                }
            }

            while (PreRaceStartDelay && DateTime.Now < startTime)
            {
                Thread.Sleep(10);
            }

            // If we've cancelled, race delay will be false.
            if (PreRaceStartDelay)
            {
                Logger.RaceLog.LogCall(this, CurrentRace, "Requested wait", randomTime, "Actual wait", DateTime.Now - now);

                currentRace.PrimaryTimingSystemLocation = EventManager.Event.PrimaryTimingSystemLocation;
                currentRace.TargetLaps = EventManager.Event.Laps;
                currentRace.Start = DateTime.Now;
                currentRace.End = default(DateTime);
                currentRace.AutoAssignNumbers = false;

                lock (races)
                {
                    if (!races.Contains(currentRace))
                    {
                        races.Add(currentRace);
                        OnRaceCreated(currentRace);
                    }
                }

                using (Database db = new Database())
                {
                    db.Races.Upsert(currentRace);
                    CheckEventStart(db);
                }

                OnRaceStart?.Invoke(currentRace);

                PreRaceStartDelay = false;
            }
            return true;
        }

        private void CheckEventStart(Database db)
        {
            if (EventManager.Event.Start == default)
            {
                EventManager.Event.Start = DateTime.Now;
                db.Events.Upsert(EventManager.Event);
            }
        }

        public bool CancelRaceStart(bool failure)
        {
            Logger.RaceLog.LogCall(this);

            PreRaceStartDelay = false;

            OnRaceCancelled?.Invoke(CurrentRace, failure);

            return true;
        }

        public bool EndRace()
        {
            Logger.RaceLog.LogCall(this, CurrentRace);

            Race currentRace = CurrentRace;

            if (currentRace == null)
                return false;

            if (!currentRace.Running)
                return false;

            if (EventType != EventTypes.Freestyle)
            {
                TimingSystemManager.EndDetection();
            }

            currentRace.End = DateTime.Now;
            currentRace.TargetLaps = EventManager.Event.Laps;

            using (Database db = new Database())
            {
                db.Races.Update(currentRace);
            }

            EventManager.ResultManager.SaveResults(currentRace);

            OnRaceEnd?.Invoke(currentRace);

            return true;
        }

        public bool ClearRace()
        {
            Logger.RaceLog.LogCall(this, CurrentRace);

            Race currentRace = CurrentRace;

            if (currentRace == null)
                return false;

            Race current = currentRace;

            using (Database db = new Database())
            {
                db.Races.Update(currentRace);
                CurrentRace = null;
            }


            OnRaceChanged?.Invoke(null);
            OnRaceClear?.Invoke(current);

            return true;
        }

        public void AddRace(Race race)
        {
            Logger.RaceLog.LogCall(this, CurrentRace);

            race.Event = EventManager.Event;

            if (race.Type == EventTypes.CasualPractice)
            {
                SetupCasualPractice(race);
            }

            lock (races)
            {
                races.Add(race);
            }

            using (Database db = new Database())
            {
                db.PilotChannels.Upsert(race.PilotChannelsSafe);
                db.Races.Upsert(race);
            }

            OnRaceCreated?.Invoke(race);
        }

        public Race AddRaceToRound(Round round)
        {
            Race race = new Race();
            race.Round = round;
            race.RaceNumber = GetRaceCount(round) + 1;

            AddRace(race);
            UpdateRaceRoundNumbers();
            return race;
        }

        public void LoadRaces(Event eve)
        {
            Race[] races;
            using (Database db = new Database())
            {
                races = db.Races
                .Include(r => r.PilotChannels)
                .Include(r => r.PilotChannels.Select(pc => pc.Pilot))
                .Include(r => r.PilotChannels.Select(pc => pc.Channel))
                .Include(r => r.Laps)
                .Include(r => r.Detections)
                .Include(r => r.Detections.Select(d => d.Pilot))
                .Include(r => r.Round)
                .Include(r => r.Event)
                .Include(r => r.Track)
                .Find(r => r.Event.ID == eve.ID && r.Valid).OrderBy(r => r.Creation).ToArray();
            }

            LoadRaces(races);
        }

        public void LoadRaces(IEnumerable<Race> load)
        {
            Logger.RaceLog.LogCall(this);

            if (load.Any())
            {
                lock (races)
                {
                    races.AddRange(load);
                }

                lock (races)
                {
                    foreach (Race race in races)
                    {
                        // Keep the event object synced in memory
                        race.Event = EventManager.Event;

                        if (race.TargetLaps == 0)
                        {
                            race.TargetLaps = EventManager.Event.Laps;
                        }

                        if (race.Round == null)
                        {
                            int round = DiscoverRoundNumber(race);
                            race.Round = EventManager.RoundManager.GetCreateRound(round, EventManager.Event.EventType);
                        }

                        foreach (Lap lap in race.Laps)
                        {
                            // When loading, we need to set the race object reference onto each Lap for performance quick access reasons.
                            lap.Race = race;

                            // Same with detections...
                            if (lap.Detection != null)
                            {
                                Detection d = race.Detections.FirstOrDefault(da => da.ID == lap.Detection.ID);
                                if (d != null)
                                {
                                    lap.Detection = d;
                                }
                            }
                        }
                    }
                }
            }
        }

        public void RemoveRace(Race remove, bool canRemoveEmptyRound)
        {
            Logger.RaceLog.LogCall(this, remove);

            if (CurrentRace == remove)
            {
                SetRace(null);
            }

            lock (races)
            {
                races.Remove(remove);
            }

            using (Database db = new Database())
            {
                remove.Valid = false;
                db.Races.Update(remove);
            }

            if (canRemoveEmptyRound && GetRaces(remove.Round).Count() == 0)
            {
                EventManager.RoundManager.RemoveRound(remove.Round);
            }

            UpdateRaceRoundNumbers();

            EventManager.ResultManager.ClearPoint(remove);
            OnRaceRemoved?.Invoke(remove);
        }

        public void NextRace(bool unfinishedOnly)
        {
            Race next = GetNextRace(unfinishedOnly);
            if (next != null)
            {
                SetRace(next);
            }
        }

        public void PrevRace()
        {
            Race next = GetPrevRace();
            if (next != null)
            {
                SetRace(next);
            }
        }

        public void LastFinishedRace()
        {
            Race race = GetRaces(r => r.Ended).OrderByDescending(r => r.RaceOrder).FirstOrDefault();
            if (race != null)
            {
                SetRace(race);
            }
        }

        public void SetTargetLaps(int laps)
        {
            Race current = CurrentRace;
            if (current != null)
            {
                current.TargetLaps = laps;
                using (Database db = new Database())
                {
                    db.Races.Update(current);
                }
            }
        }

        public bool SetRace(Race race)
        {
            Logger.RaceLog.LogCall(this, race);

            Race currentRace = CurrentRace;

            if (currentRace != null && currentRace != race)
            {
                if (currentRace.Running)
                    return false;

                ClearRace();
            }
            CurrentRace = race;

            if (race != null && !race.Ended)
            {
                SetTargetLaps(EventManager.Event.Laps);
            }

            currentRace = CurrentRace;

            if (currentRace != null)
            {
                if (EventType != currentRace.Type)
                {
                    EventType = currentRace.Type;
                }

                if (EventType == EventTypes.CasualPractice)
                {
                    SetupCasualPractice(currentRace);
                }

                if (!currentRace.Ended)
                {
                    EventManager.SetPilotChannels(currentRace);
                }

                //Update the first detection so it matches the event so you can change it on an existing race and rerun it.
                currentRace.PrimaryTimingSystemLocation = EventManager.Event.PrimaryTimingSystemLocation;
                OnRaceChanged?.Invoke(currentRace);

                if (currentRace != null)
                {
                    foreach (var pc in currentRace.PilotChannelsSafe)
                    {
                        OnPilotAdded?.Invoke(pc);
                    }
                }

                return true;
            }
            return false;
        }

        public void SetupCasualPractice(Race race)
        {
            foreach (Channel channel in FreeChannels)
            {
                string bandChannel = channel.GetBandChannelText();

                Pilot pilot = new Pilot(bandChannel + " (" + race.Round.RoundNumber + "-" + race.RaceNumber + ")");
                pilot.Phonetic = bandChannel;
                pilot.PracticePilot = true;


                using (Database db = new Database())
                {
                    db.Pilots.Insert(pilot);
                    race.SetPilot(db, channel, pilot);
                }

                EventManager.AddPilot(pilot, channel);
            }
        }

        public void ResetRace()
        {
            ResetRace(CurrentRace);
        }

        public void ResetRace(Race race)
        {
            Logger.RaceLog.LogCall(this, race);

            if (race != null)
            {
                using (Database db = new Database())
                {
                    race.ResetRace(db);
                }
                EventManager.LapRecordManager.ResetRace(race);
                EventManager.ResultManager.ClearPoint(race);

                OnRaceReset?.Invoke(race);
            }
        }

        public int GetCurrentLapNumber(Pilot pilot)
        {
            Race currentRace = CurrentRace;

            if (currentRace == null)
                return 0;

            // Calculate which lap this is in..
            int lapCount = currentRace.GetValidLapsCount(pilot, true) + 1;

            // Account for the holeshot
            if (currentRace.PrimaryTimingSystemLocation == PrimaryTimingSystemLocation.Holeshot)
            {
                lapCount--;
            }

            return lapCount;
        }

        public bool HasFinishedAllLaps(Pilot pilot)
        {
            Race currentRace = CurrentRace;

            if (currentRace == null)
                return false;

            if (GetCurrentLapNumber(pilot) > currentRace.TargetLaps)
            {
                return true;
            }

            return false;
        }

        public bool HasFinishedAllLaps()
        {
            Race currentRace = CurrentRace;

            if (currentRace == null)
                return false;

            foreach (Pilot p in currentRace.Pilots)
            {
                if (!HasFinishedAllLaps(p))
                {
                    return false;
                }
            }
            return true;
        }

        public void OnDetection(TimingSystemType type, int timingSystem, int freq, DateTime time, bool isLapEnd, int peak)
        {
            Race currentRace = CurrentRace;

            if (currentRace == null)
                return;

            if (!currentRace.Running)
                return;

            PilotChannel pilotChannel = currentRace.GetPilotChannel(freq);
            if (pilotChannel == null)
                return;

            Channel channel = pilotChannel.Channel;
            Pilot pilot = pilotChannel.Pilot;
            if (channel == null || pilot == null)
                return;

            int lapCount = GetCurrentLapNumber(pilot);

            Detection d = new Detection(type, timingSystem, pilot, channel, time, lapCount, isLapEnd, peak);
            if (isLapEnd)
            {
                AddLap(d);
            }
            else
            {
                using (Database db = new Database())
                {
                    db.Detections.Insert(d);
                }

                lock (currentRace.Detections)
                {
                    currentRace.Detections.Add(d);
                }

                OnSplitDetection?.Invoke(d);
            }
        }

        public void AddLap(Detection detection)
        {
            Logger.RaceLog.LogCall(this, detection);

            Race currentRace = CurrentRace;

            if (EventType == EventTypes.Freestyle)
                return;

            if (currentRace == null)
                return;

            Event eve = EventManager.Event;
            if (eve == null)
                return;

            // If its added manually by race director every lap is valid.
            if (detection.TimingSystemType != TimingSystemType.Manual)
            {
                // We start the timer before the race start, so just ignore any times in there...
                if (currentRace.Start > detection.Time)
                {
                    detection.Valid = false;
                }

                //Inside race start ignore window. Which is disabled in the UI for holeshot..
                if (currentRace.Start + eve.RaceStartIgnoreDetections > detection.Time && eve.PrimaryTimingSystemLocation == PrimaryTimingSystemLocation.EndOfLap)
                {
                    detection.Valid = false;
                }

                // End of time + 1 lap
                if (TimesUp)
                {
                    // Get last lap
                    Lap last = currentRace.GetValidLapsLast(detection.Pilot, 1).FirstOrDefault();
                    if (last != null && last.EndRaceTime > EventManager.Event.RaceLength)
                    {
                        detection.Valid = false;
                    }
                }
            }

            Lap lap;
            using (Database db = new Database())
            {
                lap = currentRace.RecordLap(db, detection);
            }

            Lapalyser.OnLap(lap);

            if (detection.Valid && lap != null)
            {
                OnLapDetected?.Invoke(lap);
            }
        }

        public void AddManualLap(Pilot pilot, DateTime time)
        {
            Race currentRace = CurrentRace;
            if (currentRace == null)
                return;

            int lapCount = GetCurrentLapNumber(pilot);

            Channel c = currentRace.GetChannel(pilot);
            EventManager.RaceManager.AddLap(new Detection(TimingSystemType.Manual, 0, pilot, c, time, lapCount, true, 0));
            EventManager.RaceManager.RecalcuateLaps(pilot, currentRace);
        }

        public void AddManualLap(Pilot pilot, TimeSpan time)
        {
            Race currentRace = CurrentRace;
            if (currentRace == null)
                return;

            int lapCount = GetCurrentLapNumber(pilot);

            DateTime from = currentRace.Start;
            Lap last = currentRace.GetValidLaps(pilot, true).LastOrDefault();
            if (last != null)
            {
                from = last.Detection.Time;
            }

            Channel c = currentRace.GetChannel(pilot);
            EventManager.RaceManager.AddLap(new Detection(TimingSystemType.Manual, 0, pilot, c, from + time, lapCount, true, 0));
            EventManager.RaceManager.RecalcuateLaps(pilot, currentRace);
        }

        public IEnumerable<Race> FindRaces(IEnumerable<Tuple<Pilot, Channel>> pcs)
        {
            lock (races)
            {
                foreach (Race r in races)
                {
                    bool match = true;
                    foreach (var tup in pcs)
                    {
                        Channel c = r.GetChannel(tup.Item1);
                        if (c != tup.Item2)
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        yield return r;
                    }
                }
            }
        }

        public void RecalcuateLaps(Pilot pilot, Race race)
        {
            Logger.RaceLog.LogCall(this, pilot, race);

            using (Database db = new Database())
            {
                race.ReCalculateLaps(db, pilot);
            }

            OnLapsRecalculated?.Invoke(race);
        }


        public void Update(GameTime gameTime)
        {
            if (EventManager.Event.RaceLength != TimeSpan.Zero && RaceRunning && EventType != EventTypes.CasualPractice)
            {
                int[] timesToCheck = RemainingTimesToAnnounce;

                Race currentRace = CurrentRace;

                foreach (int timeSeconds in timesToCheck)
                {
                    TimeSpan timeSpan = TimeSpan.FromSeconds(timeSeconds);

                    if (lastTimeRemaining > timeSpan && RemainingTime <= timeSpan && currentRace != null)
                    {
                        OnRaceTimeRemaining?.Invoke(currentRace, timeSpan);
                    }
                }

                lastTimeRemaining = RemainingTime;
            }
        }

        public IEnumerable<Race> GetRaces(Round round)
        {
            lock (races)
            {
                return races.Where(r => r.Round == round).ToArray();
            }
        }
        public Race[] GetRaces(Pilot p)
        {
            lock (races)
            {
                return races.Where(r => r.HasPilot(p)).ToArray();
            }
        }

        public Race[] GetRaces(Func<Race, bool> predicate)
        {
            lock (races)
            {
                return races.Where(r => predicate(r)).ToArray();
            }
        }

        public Race[] GetRaces(Round start, Round end) // Inclusive
        {
            if (start.RoundType != end.RoundType || end.EventType != start.EventType)
                return new Race[0];

            return EventManager.RaceManager.GetRaces(r => r.Round != null 
                                                  && r.RoundNumber >= start.RoundNumber
                                                  && r.RoundNumber <= end.RoundNumber
                                                  && r.Round.EventType == end.EventType
                                                  && r.Round.RoundType == end.RoundType);
        }

        public IEnumerable<Race> GetRacesIncludingInvalid()
        {
            // Copy races
            List<Race> copy;
            
            lock (races)
            {
                copy = races.ToList();
            }

            using (Database db = new Database())
            {
                IEnumerable<Race> invalid = db.Races
                    .Include(r => r.PilotChannels)
                    .Include(r => r.PilotChannels.Select(pc => pc.Pilot))
                    .Include(r => r.PilotChannels.Select(pc => pc.Channel))
                    .Include(r => r.Laps)
                    .Include(r => r.Detections)
                    .Include(r => r.Round)
                    .Include(r => r.Event)
                    .Include(r => r.Track)
                    .Find(r => r.Event.ID == EventManager.Event.ID && r.Valid == false);

                copy.AddRange(invalid);
            }

            return copy;
        }


        public void DeleteRaces()
        {
            Logger.RaceLog.LogCall(this);
            lock (races)
            {
                List<Lap> laps = races.SelectMany(r => r.Laps).ToList();
                List<Detection> detections = races.SelectMany(r => r.Detections).ToList();

                EventManager.Event.Rounds.Clear();

                EventManager.ResultManager.Clear();

                using (Database db = new Database())
                {
                    db.Detections.Delete(detections);
                    db.Laps.Delete(laps);
                    db.Races.Delete(races);

                    db.Events.Update(EventManager.Event);
                }

                races.Clear();
            }
        }
        
        public Race GetNextRace(bool unfinishedOnly, bool allowCurrent = true)
        {
            lock (races)
            {
                int currentOrder = 0;

                Race race = CurrentRace;
                if (race == null || unfinishedOnly)
                {
                    return races.Where(r => !r.Ended && r.PilotChannels.Any() && (r != race || allowCurrent)).OrderBy(r => r.Round.Order).ThenBy(r => r.RaceOrder).FirstOrDefault();
                }

                if (race != null)
                {
                    currentOrder = race.RaceOrder;
                }

                return races.Where(r => r.RaceOrder > currentOrder && (r != race || allowCurrent)).OrderBy(r => r.Round.Order).ThenBy(r => r.RaceOrder).FirstOrDefault();
            }
        }

        public Race GetPrevRace()
        {
            lock (races)
            {
                int currentOrder = int.MaxValue;

                Race cr = CurrentRace;
                if (cr != null)
                {
                    currentOrder = cr.RaceOrder;
                    return races.Where(r => r.RaceOrder < currentOrder).OrderByDescending(r => r.Round.Creation).ThenByDescending(r => r.RaceOrder).FirstOrDefault();
                }
                return null;
            }
        }


        public void UpdateRaceRoundNumbers()
        {
            lock (races)
            {
                Logger.RaceLog.LogCall(this);
                using (Database db = new Database())
                {
                    var types = from round in EventManager.Event.Rounds.OrderBy(r => r.Order)
                                group round by round.EventType into newGroup
                                select newGroup;
                    
                    foreach (var roundTypeGroup in types)
                    {
                        int roundNumber = 1;
                        foreach (Round round in roundTypeGroup)
                        {
                            round.RoundNumber = roundNumber;
                            db.Rounds.Update(round);

                            int raceNumber = 1;

                            IEnumerable<Race> races = GetRaces(round).OrderBy(r => r.RaceNumber).ThenBy(r => r.Creation);
                            foreach (Race race in races)
                            {
                                race.RaceNumber = raceNumber;
                                raceNumber++;

                                db.Races.Update(race);
                            }
                            roundNumber++;
                        }
                    }
                }
            }
        }

        public string GetRaceResultsText(string delimiter = "\t")
        {
            string result = "";

            foreach (Round round in EventManager.Event.Rounds)
            {
                foreach (Race race in GetRaces(round).OrderBy(r => r.RaceNumber))
                {
                    result += "Race " + race.RoundRaceNumber + "\r\n";

                    foreach (ExportColumn ec in EventManager.ExportColumns.Where(ec1 => ec1.Enabled))
                    {
                        result += ec.ToString() + " " + delimiter;
                    }

                    result += "\r\n";
                    result += EventManager.ResultManager.GetResultsText(race, delimiter);
                    result += "\r\n";
                }

            }
            return result;
        }

        public string GetRawLaps()
        {
            string output = "";

            string[] titles = new string[] { "Round", "Race", "Race Start", "Pilot", "Lap Number", "Length", "Race Time", "Valid"};

            output += string.Join(",", titles) + "\n";

            foreach (Race race in Races.OrderBy(r => r.Start))
            {
                foreach (Lap lap in race.Laps.OrderBy(l => l.End))
                {
                    Pilot pilot = EventManager.GetPilot(lap.Pilot.ID);

                    if (pilot != null)
                    {
                        List<string> line = new List<string>();
                        line.Add(race.RoundNumber.ToString());
                        line.Add(race.RaceNumber.ToString());
                        line.Add(race.Start.ToString());
                        line.Add(pilot.Name);
                        line.Add(lap.Number.ToString());
                        line.Add(lap.Length.TotalSeconds.ToString("0.000"));
                        line.Add(lap.EndRaceTime.TotalSeconds.ToString("0.000"));
                        line.Add(lap.Detection.Valid.ToString());
                        output += string.Join(",", line) + "\n";
                    }
                    
                }
            }

            return output;
        }

        public Race GetRaceToRecover()
        {
            // find a race 
            Race toRecover;
            lock (races)
            {
                toRecover = races.Where(r => r.Running).OrderByDescending(r => r.Start).FirstOrDefault();
            }

            return toRecover;
        }

        public DateTime GetPauseStart(Race paused)
        {
            DateTime startOfPause;
            if (paused.Ended)
            {
                startOfPause = paused.End;
            }
            else
            {
                if (paused.Detections.Count > 0)
                {
                    startOfPause = paused.Detections.OrderBy(d => d.Time).LastOrDefault().Time;
                }
                else
                {
                    startOfPause = paused.Start;
                }
            }
            return startOfPause;
        }


        public bool ResumeRace(Race toResume)
        {
            Logger.RaceLog.LogCall(this, toResume);
            if (toResume == null)
            {
                return false;
            }

            DateTime startOfPause = GetPauseStart(toResume);

            if (startOfPause != default(DateTime))
            {
                TimeSpan pauseTime = DateTime.Now - startOfPause;
                toResume.TotalPausedTime += pauseTime;
            }

            if (toResume != CurrentRace)
            {
                SetRace(toResume);
            }

            if (!StartDetection())
                return false;

            
            if (toResume.Ended)
            {
                toResume.End = default(DateTime);
            }


            OnRaceResumed?.Invoke(toResume);

            return true;
        }

        public bool SwapPilots(Pilot newPilot, Channel newChannel, Race oldRace)
        {
            Race newRace = CurrentRace;
            if (CurrentRace == null)
                return false;

            PilotChannel oldPilotNewRace = newRace.GetPilotChannel(newChannel);
            PilotChannel newPilotOldRace = null;

            if (oldRace != null)
            {
                newPilotOldRace = oldRace.GetPilotChannel(newPilot);
            }

            bool success = false;

            using (Database db = new Database())
            {
                success = newRace.SwapPilots(db, newPilot, newChannel, oldRace);
            }

            if (success)
            {
                if (oldPilotNewRace != null)
                    OnPilotRemoved?.Invoke(oldPilotNewRace);

                if (oldRace == CurrentRace)
                {
                    OnPilotRemoved?.Invoke(newPilotOldRace);
                
                    PilotChannel oldPilotOldRace = oldRace.GetPilotChannel(oldPilotNewRace.Pilot);
                    OnPilotAdded?.Invoke(oldPilotOldRace);
                }

                PilotChannel newPilotNewRace = newRace.GetPilotChannel(newPilot);
                OnPilotAdded?.Invoke(newPilotNewRace);
                return true;
            }

            return false;
        }

        private void OnTimingSystemReconnect(int count)
        {
            if (RaceRunning)
            {
                Race current = CurrentRace;
                if (current != null)
                {
                    ResumeRace(current);
                }
            }
        }

        public void GenerateResults(DummyTimingSystem dummyTimingSystem, Race race)
        {
            if (race.Ended)
                return;

            CurrentRace = race;
            int requiredLaps = EventManager.Event.Laps;

            if (EventManager.Event.PrimaryTimingSystemLocation == PrimaryTimingSystemLocation.Holeshot)
                requiredLaps++;

            double avgLap = dummyTimingSystem.DummingSettings.TypicalLapTime.TotalSeconds;

            if (race.Type != EventTypes.Race && avgLap > 0)
            {
                requiredLaps = (int)Math.Ceiling(120 / avgLap);
            }

            race.Start = DateTime.Now.AddSeconds(-requiredLaps * avgLap);

            IEnumerable<Channel> shuffled = race.Channels;
            foreach (Channel channel in shuffled)
            {
                foreach (DateTime detection in dummyTimingSystem.GetTriggers(race.Start, requiredLaps))
                {
                    OnDetection(TimingSystemType.Dummy, 0, channel.Frequency, detection, true, 1000);
                }
            }

            EndRace();
        }

        public IEnumerable<Lap> SplitLap(Lap original, int splits)
        {
            Logger.RaceLog.LogCall(this, original);

            //New lap is before old lap.
            // New laps -- Original Lap 

            TimeSpan newLength = TimeSpan.FromSeconds(original.Length.TotalSeconds / splits);
            int timingSystem = 0; // Always the 0th(main) system

            DateTime lapStart = original.Start;
            DateTime lapEnd = lapStart;
            int lapNumber = original.Number;

            List<Lap> newLaps = new List<Lap>();
            for (int i = 0; i < (splits - 1); i++)
            {
                lapEnd = lapStart + newLength;

                Detection detection = new Detection(TimingSystemType.Manual, timingSystem, original.Pilot, original.Detection.Channel, lapEnd, lapNumber, true, 0);
                lock (original.Race.Detections)
                {
                    original.Race.Detections.Add(detection);
                }

                Lap newLap = new Lap(original.Race, original.Start, detection);
                lock (original.Race.Laps)
                {
                    original.Race.Laps.Add(newLap);
                }

                lapNumber++;
                lapStart = lapEnd;

                newLaps.Add(newLap);
            }

            original.Start = lapEnd;
            original.Detection.LapNumber = lapNumber;

            using (Database db = new Database())
            {
                db.Detections.Insert(newLaps.Select(l => l.Detection));
                db.Laps.Insert(newLaps);

                db.Races.Update(original.Race);
                db.Laps.Update(original);
                db.Detections.Update(original.Detection);

                original.Race.ReCalculateLaps(db, original.Pilot);
            }

            OnLapSplit?.Invoke(newLaps);

            OnLapsRecalculated?.Invoke(original.Race);

            return newLaps;
        }


        public void DisqualifySplit(Split split, Detection.ValidityTypes validityType = Detection.ValidityTypes.ManualOverride)
        {
            Lap lap = split.Race.GetLaps(l => l.Detection == split.Detection).FirstOrDefault();
            if (lap != null)
            {
                DisqualifyLap(lap, validityType);
            }
            else
            {
                SetDetectionValidity(split.Race, split.Detection, false, validityType);
            }
        }

        public void DisqualifyLap(Lap lap, Detection.ValidityTypes validityType = Detection.ValidityTypes.ManualOverride)
        {
            SetLapValidity(lap, false, validityType);
        }

        public void SetLapValidity(Lap lap, bool valid, Detection.ValidityTypes validityType = Detection.ValidityTypes.ManualOverride)
        {
            SetDetectionValidity(lap.Race, lap.Detection, valid, validityType);
            OnLapDisqualified?.Invoke(lap);
        }

        public void SetDetectionValidity(Race race, Detection detection, bool valid, Detection.ValidityTypes validityType = Detection.ValidityTypes.ManualOverride)
        {
            // Don't change it if its the same.
            if (detection.Valid == valid)
                return;

            // Don't change it if it's already manually overrided and we're setting it to auto.
            if (detection.ValidityType == Detection.ValidityTypes.ManualOverride && validityType == Detection.ValidityTypes.Auto)
                return;

            detection.ValidityType = validityType;
            detection.Valid = valid;
            using (Database db = new Database())
            {
                db.Detections.Update(detection);
                race.ReCalculateLaps(db, detection.Pilot);
            }

            OnDetectionDisqualified?.Invoke(race, detection);

            OnLapsRecalculated?.Invoke(race);
        }

        public Race GetPreviousRace(Pilot pilot, Race race)
        {
            return GetRaces(pilot).Where(r => r.RaceOrder < race.RaceOrder).OrderByDescending(r => r.RaceOrder).FirstOrDefault();
        }

        public IEnumerable<Channel> GetChannels(IEnumerable<Race> races)
        {
            return races.SelectMany(r => r.Channels).Distinct();
        }

        public struct PreferedChannel
        {
            public Channel Channel { get; set; }
            public int ChangeCount { get; set; }
        }

        private PreferedChannel GetPreferedChannel(Pilot pilot, Race race)
        {
            PreferedChannel preferedChannel = new PreferedChannel();

            Race[] pilotRaces = GetRaces(r => r.HasPilot(pilot) && r.RaceOrder < race.RaceOrder).ToArray();
            preferedChannel.ChangeCount = pilot.CountChannelChanges(races);

            preferedChannel.Channel = races.Where(r => r.RaceOrder < race.RaceOrder).OrderByDescending(r => r.RaceOrder).Select(r => r.GetChannel(pilot)).FirstOrDefault();
            if (preferedChannel.Channel == null)
            {
                preferedChannel.Channel = EventManager.GetChannel(pilot);
            }

            return preferedChannel;
        }

        public void OptimiseChannels(Database db, Race race)
        {
            if (race.Ended)
            {
                return;
            }

            Dictionary<Pilot, PreferedChannel> pilotPrefered = race.Pilots.ToDictionary(p => p, p => GetPreferedChannel(p, race));
            if (!race.ClearPilots(db))
            {
                return;
            }

            IEnumerable<Channel> channels = EventManager.Channels;
            
            var orderedPilots = pilotPrefered.OrderBy(kvp => channels.CountBandTypes(kvp.Value.Channel.Band.GetBandType())).ThenByDescending(kvp => kvp.Value.ChangeCount).ToArray();
            foreach (var kvp in orderedPilots)
            {
                Pilot pilot = kvp.Key;
                Channel preferedChannel = kvp.Value.Channel;

                if (!race.IsFrequencyFree(preferedChannel))
                {
                    preferedChannel = GetFreeChannel(race, preferedChannel.Band.GetBandType());
                }

                if (preferedChannel == null)
                {
                    preferedChannel = GetFreeChannel(race);
                }

                race.SetPilot(db, preferedChannel, pilot);
            }
        }
    }
}
