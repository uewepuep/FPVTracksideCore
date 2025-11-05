using RaceLib.Format;
using RaceLib.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace RaceLib
{
    public class RoundManager
    {
        public EventManager EventManager { get; }
        public Event Event { get { return EventManager.Event; } }
        public RaceManager RaceManager { get { return EventManager.RaceManager; } }
        public ResultManager ResultManager { get { return EventManager.ResultManager; } }
        public SheetFormatManager SheetFormatManager { get; set; }

        public event Action OnRoundAdded;
        public event Action OnRoundRemoved;
        public event Action OnStageChanged;

        public Round[] Rounds
        {
            get
            {
                if (Event == null)
                    return null;
                lock (Event.Rounds)
                {
                    return Event.Rounds.ToArray();
                }
            }
        }

        public RoundManager(EventManager eventManager)
        {
            EventManager = eventManager;
            SheetFormatManager = new SheetFormatManager(this);
            RaceManager.OnRaceEnd += OnRaceResultsChange;
            RaceManager.OnRaceReset += OnRaceResultsChange;
            ResultManager.RaceResultsChanged += OnRaceResultsChange;
        }

        private void OnRaceResultsChange(Race race)
        {
            if (SheetFormatManager != null)
            {
                SheetFormatManager.OnRaceResultChange(race);
            }

            Round r = NextRound(race.Round);
            if (r != null)
            {
                if (r.Stage != null)
                {
                    RoundFormat roundFormat = GetRoundFormat(r.Stage.StageType);
                    GenerateRound(race.Round, roundFormat);
                }
            }
        }

        public Round NextRound(Round round)
        {
            lock (Event.Rounds)
            {
                return Event.Rounds.Where(r => r.Order > round.Order).OrderBy(r => r.Order).FirstOrDefault();
            }
        }
        public int CountRounds(Round start, Round end)
        {
            lock (Event.Rounds)
            {
                return Event.Rounds.Count(r => r.Order >= start.Order && r.Order <= end.Order);
            }
        }

        public IEnumerable<Round> RoundsWhere(Func<Round, bool> predicate)
        {
            lock (Event.Rounds)
            {
                return Event.Rounds.Where(r => predicate(r));
            }
        }

        public void SetStageType(EventTypes type, Round round)
        {
            round.EventType = type;

            // Update the round number based on creation..
            round.RoundNumber = 1 + Event.Rounds.Where(r => r.EventType == round.EventType && r.Order < round.Order).Count();

            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                db.Update(round);
            }

            RaceManager.UpdateRaceRoundNumbers();
        }

        public bool DoesRoundHaveAllPilots(Round round)
        {
            if (round.EventType == EventTypes.CasualPractice)
                return true;

            int inRaces = RaceManager.Races.Where(r => r.Round == round).SelectMany(r => r.Pilots).Distinct().Count();
            int inEvent = Event.PilotChannels.Count;

            return inRaces >= inEvent;
        }

        public bool AllRacesFinished(Round round)
        {
            IEnumerable<Race> races = RaceManager.Races.Where(r => r.Round == round);
            return races.All(r => r.Ended) && races.Any();
        }

        public void SetRoundPilots(Round round, IEnumerable<Tuple<Pilot, Channel>> pilotChannels)
        {
            Race race = null;
            int startNumber = RaceManager.GetRaceCount(round);

            List<Race> races = new List<Race>();

            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                foreach (var tup in pilotChannels)
                {
                    Channel c = tup.Item2;
                    Pilot p = tup.Item1;

                    if (race == null || !race.IsFrequencyFree(c))
                    {
                        race = new Race(Event);
                        race.AutoAssignNumbers = true;
                        race.RaceNumber = startNumber + 1 + races.Count;
                        race.Round = round;
                        races.Add(race);
                    }

                    race.SetPilot(db, c, p);
                }
            }

            foreach (Race r in races)
            {
                RaceManager.AddRace(r);
            }
        }

        public IEnumerable<Round> GetRoundsBetween(Round start, Round end)
        {
            if (start == null || end == null)
            {
                return Enumerable.Empty<Round>();
            }

            if (start.Stage != end.Stage)
            {
                return Enumerable.Empty<Round>();
            }

            lock (Event.Rounds)
            {
                return Event.Rounds.Where(r => r.Order <= end.Order && r.Order >= start.Order && r.Stage == end.Stage).OrderBy(r => r.Order);
            }
        }

        public Round PreviousRound(Round current)
        {
            lock (Event.Rounds)
            {
                return Event.Rounds.OrderByDescending(r => r.Order).FirstOrDefault(r => r.Order < current.Order);
            }
        }

        public IEnumerable<Race> GenerateRound(RoundPlan roundPlan)
        {
            Round newRound = GetCreateRound(RaceManager.GetMaxRoundNumber(roundPlan.CallingRound.EventType) + 1, roundPlan.CallingRound.EventType);

            if (roundPlan.KeepStage)
            {
                newRound.Stage = roundPlan.Stage;
            }

            RoundFormat roundFormat = null;

            switch (roundPlan.PilotSeeding)
            {
                case RoundPlan.PilotOrderingEnum.MinimisePreviouslyFlown:
                default:
                    roundFormat = new AutoFormat(EventManager);
                break;
                case RoundPlan.PilotOrderingEnum.Ordered:
                    roundFormat = new TopFormat(EventManager);
                    break;
                case RoundPlan.PilotOrderingEnum.Seeded:
                    roundFormat = new SeededFormat(EventManager);
                    break;
            }

            return Generate(roundFormat, newRound, roundPlan);
        }

        public IEnumerable<Race> GenerateRound(Round callingRound, RoundFormat roundFormat)
        {
            Round newRound = GetCreateRound(callingRound.RoundNumber + 1, callingRound.EventType);

            RoundPlan roundPlan = new RoundPlan(EventManager, callingRound, null);
            return Generate(roundFormat, newRound, roundPlan);
        }

        public IEnumerable<Race> GenerateSeededX(Round callingRound, IEnumerable<Pilot> pilots)
        {
            Round newRound = GetCreateRound(RaceManager.GetMaxRoundNumber(callingRound.EventType) + 1, callingRound.EventType);

            RoundFormat roundFormat = new SeededFormat(EventManager);

            RoundPlan roundPlan = new RoundPlan(EventManager, callingRound, null);
            roundPlan.Pilots = pilots.ToArray();
            roundPlan.NumberOfRaces = (int)Math.Ceiling((float)roundPlan.Pilots.Length / EventManager.GetMaxPilotsPerRace());

            return Generate(roundFormat, newRound, roundPlan);
        }

        public IEnumerable<Race> GenerateTopX(Round callingRound, IEnumerable<Pilot> pilots)
        {
            Round newRound = GetCreateRound(RaceManager.GetMaxRoundNumber(callingRound.EventType) + 1, callingRound.EventType);

            RoundFormat roundFormat = new TopFormat(EventManager);

            RoundPlan roundPlan = new RoundPlan(EventManager, callingRound, null);
            roundPlan.Pilots = pilots.ToArray();
            roundPlan.NumberOfRaces = (int)Math.Ceiling((float)roundPlan.Pilots.Length / EventManager.GetMaxPilotsPerRace());

            return Generate(roundFormat, newRound, roundPlan);
        }

        public IEnumerable<Race> Generate(RoundFormat roundFormat, Round newRound, RoundPlan roundPlan)
        {
            try
            {
                IEnumerable<Race> preExisting = RaceManager.GetRaces(newRound);

                Race[] aRound;
                using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                {
                    aRound = roundFormat.GenerateRound(db, preExisting, newRound, roundPlan).ToArray();
                }
                foreach (Race r in aRound)
                {
                    RaceManager.AddRace(r);
                }

                RaceManager.UpdateRaceRoundNumbers();
                OnRoundAdded?.Invoke();

                return aRound;
            }
            catch (Exception e)
            {
                Logger.Generation.LogException(this, e);
                return new Race[] { };
            }
        }

        public Round GetFirstRound(EventTypes eventType, Stage stage = null)
        {
            lock (Event.Rounds)
            {
                return Event.Rounds.FirstOrDefault(r => r.EventType == eventType && r.Stage == stage);
            }
        }

        public Round GetLastRound(EventTypes eventType, Stage stage = null)
        {
            lock (Event.Rounds)
            {
                return Event.Rounds.LastOrDefault(r => r.Valid && r.EventType == eventType && r.Stage == stage);
            }
        }

        public Round GetLastRound()
        {
            lock (Event.Rounds)
            {
                return Event.Rounds.LastOrDefault(r => r.Valid);
            }
        }

        public Round GetRound(int roundNumber, EventTypes eventType)
        {
            if (roundNumber == 0)
                roundNumber = 1;

            lock (Event.Rounds)
            {
                return Event.Rounds.FirstOrDefault(r => r.RoundNumber == roundNumber && r.EventType == eventType);
            }
        }

        public Round GetCreateRound(int roundNumber, EventTypes eventType, Stage stage = null)
        {
            if (roundNumber == 0)
                roundNumber = 1;

            lock (Event.Rounds)
            {
                Round round = Event.Rounds.FirstOrDefault(r => r.RoundNumber == roundNumber && r.EventType == eventType && r.Valid);
                if (round == null)
                {
                    round = new Round();
                    round.RoundNumber = roundNumber;
                    round.EventType = eventType;
                    round.Stage = stage;
                    
                    if (eventType == EventTypes.Game)
                    {
                        GameType gameType = EventManager.GameManager.GameType;
                        if (gameType != null)
                            round.GameTypeName = gameType.Name;
                    }

                    if (Event.Rounds.Any())
                    {
                        round.Order = Event.Rounds.Max(r => r.Order) + 100;
                    }
                    else
                    {
                        round.Order = 100;
                    }

                    Event.Rounds.Add(round);

                    using (IDatabase db = DatabaseFactory.Open(Event.ID))
                    {
                        if (!db.Upsert(Event.Rounds))
                        {
                            throw new Exception("Failed to update rounds");
                        }
                        db.Update(Event);
                    }
                }

                return round;
            }
        }

        public void CheckThereIsOneRound()
        {
            if (!Event.Rounds.Any())
            {
                CreateEmptyRound(Event.EventType);
            }
        }

        public bool IsEmpty(Round round)
        {
            return !RaceManager.GetRaces(round).Any();
        }

        public Round CreateEmptyRound(EventTypes eventType, Stage stage = null)
        {
            int maxRoundNumber = RaceManager.GetMaxRoundNumber(eventType);

            Round newRound = GetCreateRound(maxRoundNumber + 1, eventType);
            newRound.Stage = stage;
            OnRoundAdded?.Invoke();

            return newRound;
        }

        public void RemoveRound(Round round)
        {
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                RemoveRound(db, round);
            }
        }

        public void RemoveRound(IDatabase db, Round round)
        {
            Race[] toRemove = RaceManager.Races.Where(r => r.Round == round && !r.Ended).ToArray();
            foreach (Race remove in toRemove)
            {
                RaceManager.RemoveRace(remove, false);
            }

            lock (Event.Rounds)
            {
                Event.Rounds.Remove(round);
                round.Valid = false;
                db.Update(round);
                db.Update(Event);
            }

            RaceManager.UpdateRaceRoundNumbers();

            CheckThereIsOneRound();

            OnRoundRemoved?.Invoke();
        }

        public IEnumerable<Pilot> GetOutputPilots(Round round)
        {
            RoundFormat roundFormat = GetRoundFormat(round.StageType);
            return roundFormat.GetOutputPilots(round);
        }

        public RoundFormat GetRoundFormat(StageTypes type)
        {
            switch (type)
            {
                case StageTypes.DoubleElimination:
                    return new DoubleElimination(EventManager);

                case StageTypes.Final:
                    return new FinalFormat(EventManager);

                case StageTypes.StreetLeague: 
                    return new StreetLeague(EventManager);

                case StageTypes.ChaseTheAce: 
                    return new ChaseTheAce(EventManager);

                case StageTypes.Default:
                default:
                    return new AutoFormat(EventManager);
            }
        }

        public Race CloneLastHeat(Round round)
        {
            IEnumerable<Race> races = RaceManager.Races.Where(r => r.Round == round).OrderBy(r => r.RaceNumber);

            Race race = races.LastOrDefault();
            if (race == null)
                return null;
            
            Race cloned = race.Clone();
            cloned.Round = round;

            RaceManager.AddRace(cloned);

            RaceManager.UpdateRaceRoundNumbers();

            OnRoundAdded?.Invoke();
            return cloned;
        }

        public IEnumerable<Race> CloneRound(Round round)
        {
            IEnumerable<Race> races = RaceManager.Races.Where(r => r.Round == round).OrderBy(r => r.RaceNumber);

            int maxRound = RaceManager.GetMaxRoundNumber(round.EventType);

            Round newRound = GetCreateRound(maxRound + 1, round.EventType);
            newRound.Stage = round.Stage;

            List<Race> newRaces = new List<Race>();
            foreach (Race race in races)
            {
                Race cloned = race.Clone();
                cloned.Round = newRound;
                newRaces.Add(cloned);
            }

            foreach (Race r in newRaces)
            {
                RaceManager.AddRace(r);
            }

            RaceManager.UpdateRaceRoundNumbers();
            OnRoundAdded?.Invoke();
            return newRaces;
        }

        public void GenerateFinal(Round callingRound)
        {
            Round newRound = GetCreateRound(callingRound.RoundNumber + 1, EventManager.Event.EventType);

            RoundFormat roundFormat = new FinalFormat(EventManager);
            RoundPlan plan = new RoundPlan(EventManager, callingRound, null);
            plan.NumberOfRaces = (int)Math.Ceiling(plan.Pilots.Count() / (float)EventManager.Channels.GetChannelGroups().Count());

            Generate(roundFormat, newRound, plan);
        }

        public void DeleteRounds()
        {
            Logger.RaceLog.LogCall(this);
            lock (Event.Rounds)
            {
                using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                {
                    Round[] rounds = Event.Rounds.ToArray();

                    foreach (Round r in rounds)
                    {
                        r.Valid = false;
                    }
                    Event.Rounds.Clear();

                    GetCreateRound(1, Event.EventType);
                    db.Update(rounds);
                }
            }
        }

        public Stage GetCreateStage(Round round, bool autoAssign = true)
        {
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                return GetCreateStage(db, round, autoAssign);   
            }
        }

        public Stage GetCreateStage(IDatabase db, Round round, bool autoAssign = true)
        {
            if (round.Stage == null)
            {
                CreateStage(db, round, autoAssign);
            }

            if (round.Stage != null)
            {
                if (round.Stage.Order != round.Order)
                {
                    round.Stage.Order = round.Order;
                    db.Update(round.Stage);
                }
                return round.Stage;
            }
            return null;
        }

        public Stage CreateStage(IDatabase db, Round round, bool autoAssign = true)
        {
            Stage stage = new Stage();
            stage.ID = Guid.NewGuid();
            round.Stage = stage;

            if (autoAssign)
            {
                IEnumerable<Round> rounds = AutoFindStageRounds(round, stage, round.EventType);
                foreach (Round r in rounds)
                {
                    r.Stage = stage;
                    db.Update(r);
                }
            }
            else
            {
                db.Update(round);
            }
            stage.AutoName(this);

            db.Insert(stage);
            OnStageChanged?.Invoke();

            return round.Stage;
        }

        public void SetStage(Round round, Stage stage)
        {
            if (round.Stage == stage)
                return;
            
            Stage oldStage = round.Stage;
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                round.Stage = stage;
                db.Update(round);

                if (stage != null)
                {
                    db.Update(stage);
                }
            }

            if (oldStage == null || GetStageRounds(oldStage).Any())
            {
                OnStageChanged?.Invoke();
            }
            else
            {
                DeleteStage(oldStage);
            }
        }

        public void DeleteStage(Stage stage)
        {
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                DeleteStage(db, stage);
            }
        }

        public void DeleteStage(IDatabase db, Stage stage)
        {
            Round[] rounds = GetStageRounds(stage).ToArray();
            foreach (Round r in rounds)
            {
                r.Stage = null;
                db.Update(r);
            }

            stage.Valid = false;
            db.Update(stage);
            OnStageChanged?.Invoke();
        }

        public void DeleteStageAndContents(Stage stage)
        {
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                DeleteStageAndContents(db, stage);
            }
        }


        public void DeleteStageAndContents(IDatabase db, Stage stage)
        {
            if (stage.Valid)
            {
                Round[] rounds = GetStageRounds(stage).ToArray();
                foreach (Round r in rounds)
                {
                    Race[] races = RaceManager.GetRaces(r);
                    IEnumerable<Race> finished = races.Where(g => g.Ended);
                    IEnumerable<Race> notFinished = races.Where(g => !g.Ended);
                    
                    foreach (Race race in notFinished)
                    {
                        RaceManager.RemoveRace(race, false);
                    }

                    if (finished.Any())
                    {
                        r.Stage = null;
                        db.Update(r);
                    }
                    else
                    {
                        RemoveRound(db, r);
                    }
                }

                stage.Valid = false;
                db.Update(stage);
                OnStageChanged?.Invoke();
            }
        }

        public IEnumerable<Round> GetStageRounds(Stage stage)
        {
            lock (Event.Rounds)
            {
                return Event.Rounds.Where(r => r.Stage == stage && r != null).OrderBy(r => r.Order).ThenBy(r => r.RoundNumber);
            }
        }

        public void CleanUpOrphanStages()
        {
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                Stage[] validStages = GetStages().ToArray();

                Stage[] orphans = db.All<Stage>().Where(s => s.Valid == true).Except(validStages).ToArray();

                foreach (Stage r in orphans)
                {
                    DeleteStage(db, r);
                }
            }
        }


        public Round GetLastStageRound(Stage stage)
        {
            return GetStageRounds(stage).LastOrDefault();
        }

        public bool IsLastStageRound(Round round)
        {
            if (round.Stage == null) 
                return true;

            return round == GetLastStageRound(round.Stage);
        }

        public IEnumerable<Round> AutoFindStageRounds(Round lastRound, Stage stage, EventTypes eventType)
        {
            if (lastRound == null)
                yield break;

            if (lastRound == null)
                yield break;

            if (lastRound.Stage != null && lastRound.Stage != stage)
                yield break;

            if (lastRound.EventType != eventType)
                yield break;

            Round previousRound = PreviousRound(lastRound);
            foreach (Round round in AutoFindStageRounds(previousRound, stage, eventType))
            {
                yield return round;
            }

            yield return lastRound;
        }

        public bool ToggleSumPoints(Round round)
        {
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                if (round.Stage == null)
                {
                    round.Stage = CreateStage(db, round);
                    round.Stage.PointSummary = new PointSummary(ResultManager.PointsSettings);
                    db.Update(round.Stage);
                    return true;
                }
                else
                {
                    DeleteStage(db, round.Stage);
                    return false;
                }
            }
        }

        public bool ToggleTimePoints(Round round, TimeSummary.TimeSummaryTypes type)
        {
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                if (round.Stage == null)
                {
                    round.Stage = CreateStage(db, round);
                    round.Stage.TimeSummary = new TimeSummary() { TimeSummaryType = type };
                    db.Update(round.Stage);
                    return true;
                }
                else
                {
                    DeleteStage(db, round.Stage);
                    return false;
                }
            }
        }

        public bool ToggleLapCount(Round round)
        {
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                if (round.Stage == null)
                {
                    round.Stage = CreateStage(db, round);
                    round.Stage.LapCountAfterRound = !round.Stage.LapCountAfterRound;

                    db.Update(round.Stage);
                    return true;
                }
                else
                {
                    DeleteStage(db, round.Stage);
                    return false;
                }
            }
        }

        public bool TogglePackCount(Round round)
        {
            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                if (round.Stage == null)
                {
                    round.Stage = CreateStage(db, round);
                    round.Stage.PackCountAfterRound = !round.Stage.PackCountAfterRound;

                    db.Update(round.Stage);
                    return true;
                }
                else
                {
                    DeleteStage(db, round.Stage);
                    return false;
                }
            }
        }

        public IEnumerable<Stage> GetStages()
        {
            return Rounds.Select(x => x.Stage).Where(s => s != null && s.Valid).Distinct();
        }
    }
}
