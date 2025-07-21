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

        public event System.Action OnRoundAdded;
        public event System.Action OnRoundRemoved;

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
                if (r.RoundType == Round.RoundTypes.DoubleElimination)
                {
                    GenerateDoubleElimination(race.Round);
                }

                if (r.RoundType == Round.RoundTypes.Final)
                {
                    GenerateFinal(race.Round);
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

        public void SetRoundType(EventTypes type, Round round)
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

            if (start.RoundType != end.RoundType)
            {
                return Enumerable.Empty<Round>();
            }

            return Event.Rounds.Where(r => r.Order <= end.Order && r.Order >= start.Order && r.RoundType == end.RoundType).OrderBy(r => r.Order);
        }

        public Round PreviousRound(Round current)
        {
            return Event.Rounds.OrderByDescending(r => r.Order).FirstOrDefault(r => r.Order < current.Order);
        }

        public IEnumerable<Race> GenerateRound(RoundPlan roundPlan)
        {
            Round newRound = GetCreateRound(RaceManager.GetMaxRoundNumber(roundPlan.CallingRound.EventType) + 1, roundPlan.CallingRound.EventType);

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

        public IEnumerable<Race> GenerateChaseTheAce(Round callingRound)
        {
            Round newRound = GetCreateRound(callingRound.RoundNumber + 1, callingRound.EventType);

            ChaseTheAce roundFormat = new ChaseTheAce(EventManager);

            RoundPlan roundPlan = new RoundPlan(EventManager, callingRound);
            return Generate(roundFormat, newRound, roundPlan);
        }

        public IEnumerable<Race> GenerateDoubleElimination(Round callingRound)
        {
            Round newRound = GetCreateRound(callingRound.RoundNumber + 1, callingRound.EventType);

            DoubleElimination roundFormat = new DoubleElimination(EventManager);

            RoundPlan roundPlan = new RoundPlan(EventManager, callingRound);
            return Generate(roundFormat, newRound, roundPlan);
        }

        public IEnumerable<Race> GenerateSeededX(Round callingRound, IEnumerable<Pilot> pilots)
        {
            Round newRound = GetCreateRound(RaceManager.GetMaxRoundNumber(callingRound.EventType) + 1, callingRound.EventType);

            RoundFormat roundFormat = new SeededFormat(EventManager);

            RoundPlan roundPlan = new RoundPlan(EventManager, callingRound);
            roundPlan.Pilots = pilots.ToArray();
            roundPlan.NumberOfRaces = (int)Math.Ceiling((float)roundPlan.Pilots.Length / EventManager.GetMaxPilotsPerRace());

            return Generate(roundFormat, newRound, roundPlan);
        }

        public IEnumerable<Race> GenerateTopX(Round callingRound, IEnumerable<Pilot> pilots)
        {
            Round newRound = GetCreateRound(RaceManager.GetMaxRoundNumber(callingRound.EventType) + 1, callingRound.EventType);

            RoundFormat roundFormat = new TopFormat(EventManager);

            RoundPlan roundPlan = new RoundPlan(EventManager, callingRound);
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

        public Round GetFirstRound(EventTypes eventType, Round.RoundTypes roundType = Round.RoundTypes.Round)
        {
            lock (Event.Rounds)
            {
                return Event.Rounds.FirstOrDefault(r => r.EventType == eventType && r.RoundType == roundType);
            }
        }
        public Round GetLastRound(EventTypes eventType, Round.RoundTypes roundType = Round.RoundTypes.Round)
        {
            lock (Event.Rounds)
            {
                return Event.Rounds.LastOrDefault(r => r.Valid && r.EventType == eventType && r.RoundType == roundType);
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

        public Round GetCreateRound(int roundNumber, EventTypes eventType)
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

        public Round CreateEmptyRound(EventTypes eventType)
        {
            int maxRoundNumber = RaceManager.GetMaxRoundNumber(eventType);

            Round newRound = GetCreateRound(maxRoundNumber + 1, eventType);
            OnRoundAdded?.Invoke();

            return newRound;
        }

        public void RemoveRound(Round round)
        {
            Race[] toRemove = RaceManager.Races.Where(r => r.Round == round && !r.Ended).ToArray();
            foreach (Race remove in toRemove)
            {
                RaceManager.RemoveRace(remove, false);
            }

            lock (Event.Rounds)
            {
                Event.Rounds.Remove(round);
                using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                {
                    round.Valid = false;
                    db.Update(round);
                    db.Update(Event);
                }
            }

            RaceManager.UpdateRaceRoundNumbers();

            CheckThereIsOneRound();

            OnRoundRemoved?.Invoke();
        }

        public IEnumerable<Pilot> GetOutputPilots(Round round)
        {
            RoundFormat roundFormat = GetRoundFormat(round.RoundType);
            return roundFormat.GetOutputPilots(round);
        }

        public RoundFormat GetRoundFormat(Round.RoundTypes type)
        {
            switch (type)
            {
                case Round.RoundTypes.DoubleElimination:
                    return new DoubleElimination(EventManager);
                case Round.RoundTypes.Final:
                    return new FinalFormat(EventManager);
                case Round.RoundTypes.Round:
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
            newRound.RoundType = round.RoundType;

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

        public IEnumerable<Race> NextPointsRound(Round round)
        {
            IEnumerable<Race> races = RaceManager.Races.Where(r => r.Round == round).OrderBy(r => r.RaceNumber);

            int maxRound = RaceManager.GetMaxRoundNumber(round.EventType);

            Round newRound = GetCreateRound(maxRound + 1, round.EventType);
            newRound.RoundType = round.RoundType;

            List<Race> newRaces = new List<Race>();
            var Pilots = new List<Pilot>();

            foreach (Race race in races)
            {
                foreach(var pilot in race.Pilots)
                {
                    Pilots.Add(pilot);
                }
               
            }

            // Sort pilots into races based on their event total points, lowest points in earliest rounds
            // For uneven groups push empty spots to higher groups
            var pilotPoints = ResultManager.Results.GroupBy(r => r.Pilot).Select(r => new {pilot= r.Key, points= r.Sum(r => r.Points)}).OrderBy(r => r.points).ToList();
            var groupBalance = GetGroupBalance(pilotPoints.Count, races.First().Channels.Length);
            var sampleRace = races.First();

            foreach(var group in groupBalance)
            {
                Race r = sampleRace.Clone();
                r.Round = newRound;
                foreach(var pc in r.PilotChannels)
                {
                    pc.Pilot = null;
                }
                List<Pilot> unassignedPilots = new List<Pilot>();
                for(int i = (group.Count -1); i > -1; i--)
                {
                    var pilot = pilotPoints.ElementAt(group[i]).pilot;
                    var lastRace = races.First(e => e.Pilots.Contains(pilot));
                    var lastChannel = lastRace.PilotChannels.First(e => e.Pilot == pilot);
                    var currentChannel = r.PilotChannels.First(e => e.Channel == lastChannel.Channel);
                    if(currentChannel.Pilot == null)
                    {
                        currentChannel.Pilot = pilot;
                    } else
                    {
                        unassignedPilots.Add(pilot);
                    }
                }
                while(unassignedPilots.Count > 0 )
                {
                    var unassignedPilot = unassignedPilots.ElementAt(0);
                    foreach(var pc in r.PilotChannels)
                    {
                        if(pc.Pilot == null)
                        {
                            pc.Pilot = unassignedPilot;
                            unassignedPilots.RemoveAt(0);
                            break;
                        }
                    }
                }
                newRaces.Add(r);
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
            RoundPlan plan = new RoundPlan(EventManager, callingRound);
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

        public List<List<int>> GetGroupBalance(int entries, int groupSize) {
            List<int> bigGroup = new List<int>();
            for(int i = 0; i < entries; i++)
            {
                bigGroup.Add(i);
            }

            List<List<int>> chunks = new List<List<int>>();
            for(int i = 0;i < bigGroup.Count; i+= groupSize)
            {
                var chunk = new List<int>();
                for(int j = i; j < i + groupSize; j++)
                {
                    if (j < bigGroup.Count)
                    {
                        var element = bigGroup.ElementAtOrDefault(j);
                        chunk.Add(element);
                    }
                }
                chunks.Add(chunk);
            }

            if(chunks.Count > 1)
            {
                // Index of last but one
                var lastChunkTakenFrom = chunks.Count - 2;

                // while the graph list of the last chunk minus the graph list of the first is > 1
                while (Math.Abs(chunks[chunks.Count - 1].Count - chunks[0].Count) > 1)
                {
                    // move a graph from the last but one chunk to the last chunk
                    var movementChunk = chunks[lastChunkTakenFrom][chunks[lastChunkTakenFrom].Count - 1];
                    chunks[lastChunkTakenFrom].RemoveAt(chunks[lastChunkTakenFrom].Count - 1);
                    chunks[chunks.Count - 1].Add(movementChunk);

                    lastChunkTakenFrom--;

                    // set back to last but one index
                    if (lastChunkTakenFrom < 0)
                        lastChunkTakenFrom = chunks.Count - 2;
                }

            }

            //Clean up the numbers
            var fixedIndex = 0;
            for (var i = 0; i < chunks.Count; i++)
            {
                for (var k = 0; k < chunks[i].Count; k++)
                {
                    chunks[i][k] = fixedIndex;
                    fixedIndex++;
                }
            }

            return chunks;
        }

    }
}
