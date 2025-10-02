using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace RaceLib
{
    public class PointsSettings
    {
        [Category("Points")]
        public bool DropWorstRound { get; set; }
        [Category("Points")]
        [DisplayName("Position Points (1st, 2nd, 3rd...)")]
        public int[] PositionPoints { get; set; }

        [Category("Points")]
        [DisplayName("DNF For Unfinished Races")]
        public bool DNFForUnfinishedRaces { get; set; }

        [Category("Points")]
        [DisplayName("DNF points")]
        public int DNFPoints { get; set; }

        [Category("Points")]
        [DisplayName("Round position Roll Over into virtual final race (RRO)")]
        public bool RoundPositionRollover { get; set; }

        public PointsSettings()
        {
            DNFForUnfinishedRaces = true;
            DropWorstRound = true;

            PositionPoints = new int[] { 10, 8, 6, 4, 2, 0 };
            DNFPoints = 0;
            RoundPositionRollover = false;
        }

        private const string filename = @"PointSettings.xml";
        public static PointsSettings Read(Profile profile)
        {
            PointsSettings s = null;
            try
            {
                s = Tools.IOTools.Read<PointsSettings>(profile, filename).FirstOrDefault();
                if (s == null)
                {
                    s = new PointsSettings();
                }

                return s;
            }
            catch
            {
                return new PointsSettings();
            }
        }

        public static void Write(Profile profile, PointsSettings points)
        {
            Tools.IOTools.Write(profile, filename, new PointsSettings[] { points });
        }
    }


    public class ResultManager
    {
        [Browsable(false)]
        public EventManager EventManager { get; private set; }

        public List<Result> Results { get; set; }

        public event Action<Race> RaceResultsChanged;

        public PointsSettings PointsSettings { get; set; }

        public ResultManager(EventManager eventManager)
        {
            PointsSettings = PointsSettings.Read(eventManager.Profile);

            if (eventManager != null)
            {
                EventManager = eventManager;
                EventManager.RaceManager.OnLapsRecalculated += OnLapsRecalc;
            }
            Results = new List<Result>();
        }

        private void OnLapsRecalc(Race race)
        {
            if (race.Ended)
            {
                SaveResults(race);
            }
        }

        public void Load(Event eve)
        {
            using (IDatabase db = DatabaseFactory.Open(eve.ID))
            {
                // Load points
                Results = db.LoadResults().ToList();
            }
        }

        public Round GetStartRound(Round endRound)
        {
            Stage endStage = endRound.Stage;

            if (endRound.RoundType != Round.RoundTypes.Round)
            {
                Round current = endRound;
                Round best = current;

                while (current != null)
                {
                    current = EventManager.RoundManager.PreviousRound(current);
                    if (current == null || current.RoundType != endRound.RoundType || current.Stage != endStage)
                    {
                        return best;
                    }

                    best = current;
                }

                return endRound;
            }
            else
            {
                Round lastSumPointsRound = EventManager.RoundManager.RoundsWhere(r => r.Stage != endStage && r.RoundNumber < endRound.RoundNumber).OrderBy(r => r.RoundNumber).LastOrDefault();

                int start = 1;
                if (lastSumPointsRound != null)
                {
                    start = lastSumPointsRound.RoundNumber + 1;
                }
                return EventManager.RoundManager.GetCreateRound(start, endRound.EventType);
            }
        }

        private IEnumerable<Race> GetRoundRaces(Round startRound, Round endRound)
        {
            Round[] rounds = EventManager.RoundManager.GetRoundsBetween(startRound, endRound).ToArray();

            Race[] races = EventManager.RaceManager.GetRaces(r => rounds.Contains(r.Round) && r.Valid);
            for (int i = startRound.RoundNumber; i <= endRound.RoundNumber; i++)
            {
                foreach (Race r in races.Where(ra => ra.RoundNumber == i))
                {
                    yield return r;
                }
            }
        }

        public IEnumerable<Race> GetRoundRaces(Round round)
        {
            if (round == null)
                return new Race[0];

            if (round.Stage == null)
            {
                Round start = GetStartRound(round);
                return GetRoundRaces(start, round);
            }

            return GetStageRaces(round.Stage);
        }

        public Race[] GetStageRaces(Stage stage)
        {
            if (stage == null)
                return new Race[0];

            Round[] rounds = null;
            if (stage.TimeSummary != null && stage.TimeSummary.IncludeAllRounds)
            {
                rounds = EventManager.RoundManager.Rounds;
            }
            else
            {
                rounds = EventManager.RoundManager.GetStageRounds(stage).OrderBy(r => r.Order).ToArray();
            }

            return EventManager.RaceManager.GetRaces(rounds);
        }

        public IEnumerable<Race> GetRoundPointRaces(Round round)
        {
            return GetRoundRaces(round).Where(r => r.Type.HasPoints());
        }

        public IEnumerable<Race> GetStagePointRaces(Stage stage)
        {
            return GetStageRaces(stage).Where(r => r.Type.HasPoints());
        }

        public int GetPositionTotal(Round endRound, Pilot pilot)
        {
            Race[] races = GetRoundPointRaces(endRound).ToArray();
            Pilot[] pilots = races.SelectMany(r => r.Pilots).Distinct().ToArray();

            IEnumerable<Tuple<Pilot, int>> pilotPoints = pilots.Select(p => new Tuple<Pilot, int>(p, GetPointsTotal(endRound, p))).OrderByDescending(pp => pp.Item2);
            var found = pilotPoints.FirstOrDefault(pp => pp.Item1 == pilot);
            if (found == null)
            {
                return pilots.Length;
            }

            int position = 1;
            int points = found.Item2;
            foreach (Tuple<Pilot, int> pilotPoint in pilotPoints)
            {
                if (pilotPoint.Item2 > points)
                {
                    position++;
                }

                if (pilotPoint.Item1 == pilot)
                {
                    return position;
                }
            }
            return pilots.Length;
        }

        public int GetPointsTotal(Round endRound, Pilot pilot)
        {
            IEnumerable<Result> results = GetResults(endRound, pilot);
            bool dropWorstRound = DropWorst(endRound);
            return GetPointsTotal(results, dropWorstRound);
        }

        public int GetPointsTotal(IEnumerable<Result> results, bool dropWorstRound)
        {
            if (!results.Any())
            {
                return 0;
            }
            int total = results.Select(r => r.Points).Sum();

            if (dropWorstRound && results.Count() > 1)
            {
                var raceResults = results.Where(r => r.ResultType == Result.ResultTypes.Race);
                var orderedRounds = raceResults.Select(r => r.Round).OrderBy(r => r.Order);

                Round first = orderedRounds.FirstOrDefault();
                Round last = orderedRounds.LastOrDefault();

                if (first != null && last != null)
                {
                    int roundCount = EventManager.RoundManager.CountRounds(first, last);
                    int raceResultCount = raceResults.Count();

                    // If there are less results than rounds, we don't need to drop a round as the pilot has already missed one.
                    if (raceResultCount >= roundCount)
                    {
                        total -= results.Select(r => r.Points).Min();
                    }
                }
            }
            return total;
        }

        public IEnumerable<Result> GetResults(Round endRound, Pilot pilot)
        {
            IEnumerable<Race> races = GetRoundPointRaces(endRound).Where(r => r.HasPilot(pilot));

            bool rollover = RollOver(endRound);

            return GetResults(races, pilot, rollover);
        }

        public Round GetRollOverRound(Round roundInFinal)
        {
            if (roundInFinal == null)
                return null;

            if (roundInFinal.RoundType != Round.RoundTypes.Final)
            {
                if (roundInFinal.CanBePartofRollover())
                {
                    return roundInFinal;
                }
                else
                {
                    return null;
                }
            }

            Round lastRound = EventManager.RoundManager.GetRound(roundInFinal.RoundNumber - 1, roundInFinal.EventType);
            if (lastRound != null && lastRound != roundInFinal)
            {
                return GetRollOverRound(lastRound);
            }

            return null;
        }

        public IEnumerable<Result> GetResults(IEnumerable<Race> races, Pilot pilot, bool roundPositionRollover, bool recalculate = false)
        {
            List<Result> results;
            lock (Results)
            {
                results = Results.Where(result => result.Pilot == pilot && races.Contains(result.Race)).ToList();
            }

            bool anyFinals = races.Select(r => r.Round).Any(r => r.RoundType == Round.RoundTypes.Final);
            if (roundPositionRollover && anyFinals)
            {
                Round rolloverRound = GetRollOverRound(races.Select(r => r.Round).FirstOrDefault());
                if (rolloverRound != null)
                {
                    Result rollOver = GetRollOver(pilot, rolloverRound, recalculate);
                    if (rollOver != null)
                    {
                        results.Insert(0, rollOver);
                    }
                }
            }

            return results;
        }

        public IEnumerable<Result> GetResults(Round endRound, Pilot pilot, bool roundPositionRollover, bool recalculate = false)
        {
            List<Result> results = GetResults(endRound, pilot).ToList();

            if (roundPositionRollover && endRound.RoundType == Round.RoundTypes.Final)
            {
                Round rolloverRound = GetRollOverRound(endRound);
                if (rolloverRound != null)
                {
                    Result rollOver = GetRollOver(pilot, rolloverRound, recalculate);
                    if (rollOver != null)
                    {
                        results.Insert(0, rollOver);
                    }
                }
            }

            return results;
        }

        public Result GetRollOver(Pilot pilot, Round rolloverRound, bool recalculate)
        {
            Result result = null;
            if (rolloverRound != null && rolloverRound.CanBePartofRollover())
            {
                result = GetCreateRoundRollOverResult(pilot, rolloverRound);

                if (recalculate || result.Position == default)
                {
                    result.Position = GetPositionTotal(rolloverRound, pilot);

                    result.Points = GetPoints(result.Position);

                    using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                    {
                        db.Update(result);
                    }
                }
            }

            return result;
        }

        private Result GetCreateRoundRollOverResult(Pilot pilot, Round rolloverRound)
        {
            Result result = null;

            lock (Results)
            {
                result = Results.FirstOrDefault(r => r.Race == null && r.Event == EventManager.Event && r.Pilot == pilot && r.ResultType == Result.ResultTypes.RoundRollOver && r.Round == rolloverRound);
            }

            if (result != null)
            {
                return result;
            }

            result = new Result();
            result.Event = EventManager.Event;
            result.Pilot = pilot;
            result.Round = rolloverRound;
            result.ResultType = Result.ResultTypes.RoundRollOver;
            result.DNF = false;

            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                db.Insert(result);
            }

            lock (Results)
            {
                Results.Add(result);
            }
            return result;
        }

        public bool DNFed(Race race, Pilot pilot)
        {
            if (race.Type == EventTypes.Endurance)
            {
                return false;
            }

            if (!race.Type.UsesTimingSystem())
            {
                return false;
            }

            if (PointsSettings.DNFForUnfinishedRaces && race.Started)
            {
                int validLaps = race.GetValidLapsCount(pilot, false);
                if (validLaps < race.TargetLaps)
                {
                    return true;
                }
            }
            return false;
        }

        public int GetPoints(int position)
        {
            int index = position - 1;
            int points = 0;

            if (index >= 0 && index < PointsSettings.PositionPoints.Length)
            {
                points = PointsSettings.PositionPoints[index];
            }
            return points;
        }


        public Result GetResult(Race race, Pilot pilot)
        {
            lock (Results)
            {
                return Results.FirstOrDefault(result => result.Pilot == pilot && race == result.Race);
            }
        }

        public Result GetResult(Round round, Pilot pilot)
        {
            lock (Results)
            {
                return Results.FirstOrDefault(result => result.Pilot == pilot && round == result.Round && result.ResultType == Result.ResultTypes.Race);
            }
        }

        public IEnumerable<Result> GetResults(Race race)
        {
            lock (Results)
            {
                return Results.Where(result => race == result.Race);
            }
        }

        public IEnumerable<Result> GetOrderedResults(Race race)
        {
            return GetResults(race).OrderBy(r => r.DNF).ThenBy(r => r.Position);
        }

        public int GetPosition(Race race, Pilot pilot)
        {
            Result result = GetResult(race, pilot);
            if (result != null)
            {
                return result.Position;
            }

            return -1;
        }

        public void ClearPoints(Race race)
        {
            if (ClearPointsNoTrigger(race))
            {
                RaceResultsChanged?.Invoke(race);
            }
        }

        private bool ClearPointsNoTrigger(Race race)
        {
            lock (Results)
            {
                Result[] toRemove = Results.Where(r => r.Race != null && r.Race.ID == race.ID).ToArray();

                if (!toRemove.Any())
                    return false;

                Results.RemoveAll(r => toRemove.Contains(r));

                using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                {
                    db.Delete(toRemove);
                }
                return true;
            }
        }

        public bool SaveResults(Race race)
        {
            return SaveResults(null, race);
        }

        public bool SaveResults(IDatabase db, Race race)
        {
            if (!race.Ended)
                return false;

            if (!race.Round.EventType.HasPoints())
                return false;

            ClearPointsNoTrigger(race);

            List<Result> newResults = new List<Result>();

            foreach (Pilot pilot in race.Pilots)
            {
                int position = race.GetPosition(pilot);
                int points = GetPoints(position);
                bool dnfed = false;

                if (DNFed(race, pilot))
                {
                    points = PointsSettings.DNFPoints;
                    dnfed = true;
                }

                Result r = new Result();
                r.Event = race.Event;
                r.Race = race;
                r.Pilot = pilot;
                r.Round = race.Round;
                r.Points = points;
                r.Position = position;
                r.ResultType = Result.ResultTypes.Race;
                r.DNF = dnfed;
                newResults.Add(r);
            }

            if (db == null)
            {
                using (IDatabase db2 = DatabaseFactory.Open(EventManager.EventId))
                {
                    db2.Insert(newResults);
                }
            }
            else
            {
                db.Insert(newResults);
            }

            lock (Results)
            {
                Results.AddRange(newResults);
            }

            RaceResultsChanged?.Invoke(race);

            return true;
        }

        public Result SetResult(Race race, Pilot pilot, int position, int points, bool dnf)
        {
            if (!race.Ended)
            {
                race.End = DateTime.Now;

                if (!race.Started)
                {
                    race.Start = DateTime.Now;
                }
                using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                {
                    db.Update(race);
                }
            }

            Result r = GetResult(race, pilot);
            if (r == null)
            {
                r = new Result();
                r.Event = race.Event;
                r.Race = race;
                r.Pilot = pilot;
                r.Round = race.Round;
                r.ResultType = Result.ResultTypes.Race;

                lock (Results)
                {
                    Results.Add(r);
                }
            }

            r.Points = points;
            r.Position = position;
            r.DNF = dnf;

            using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
            {
                db.Upsert(r);
            }

            RaceResultsChanged?.Invoke(race);

            return r;
        }

        public string GetResultText(Race race, Pilot pilot, Channel channel)
        {
            if (race.Ended && pilot != null)
            {
                if (race.Type == EventTypes.Race || race.Type == EventTypes.AggregateLaps)
                {
                    Result result = GetResult(race, pilot);
                    if (result != null)
                    {
                        if (result.DNF)
                        {
                            return "DNF";
                        }
                        else
                        {
                            int position = result.Position;
                            return position.ToStringPosition();
                        }
                    }
                    else
                    {
                        return "";
                    }

                }
                else if (race.Type == EventTypes.TimeTrial)
                {
                    if (EventManager.ResultManager.DNFed(race, pilot))
                    {
                        return "DNF";
                    }
                    else
                    {
                        int position = EventManager.LapRecordManager.GetPosition(pilot, EventManager.Event.Laps);
                        return position.ToStringPosition();
                    }
                }
                else if (race.Type == EventTypes.Game)
                {
                    int points = EventManager.GameManager.GetGamePoints(race, channel);
                    return points + " pts";
                }
                else
                {
                    return "-";
                }
            }
            else
            {
                return "";
            }
        }

        public string[][] GetResultsText(Race race, Units units)
        {
            bool showDNF = PointsSettings.DNFForUnfinishedRaces;

            List<string[]> output = new List<string[]>();
            foreach (IEnumerable<Channel> channelGroup in EventManager.Channels.GetChannelGroups())
            {
                List<string> line = new List<string>();

                Pilot p = race.GetPilot(channelGroup);
                Result r = GetResult(race, p);
                if (p != null)
                {
                    TimeSpan lapstime = TimeSpan.MaxValue;
                    TimeSpan fastestLap = TimeSpan.MaxValue;
                    TimeSpan pbTime = TimeSpan.MaxValue;
                    TimeSpan raceTime = TimeSpan.MaxValue;

                    IEnumerable<Lap> laps = race.GetValidLapsInRace(p);
                    if (laps.Any())
                    {
                        fastestLap = laps.Select(l => l.Length).Min();
                        lapstime = laps.BestConsecutive(EventManager.Event.Laps).TotalTime();
                        pbTime = EventManager.LapRecordManager.GetPBLaps(p).TotalTime();

                        raceTime = lapstime;

                        Lap holeShot = race.GetHoleshot(p);
                        if (holeShot != null && raceTime != TimeSpan.MaxValue)
                        {
                            raceTime += holeShot.Length;
                        }
                    }

                    string lapsTimeString = "DNF";
                    string raceTimeString = "DNF";
                    bool dnfed = DNFed(race, p);
                    if (!dnfed)
                    {
                        lapsTimeString = lapstime.TotalSeconds.ToString("0.00");
                        raceTimeString = raceTime.TotalSeconds.ToString("0.00");
                    }

                    foreach (ExportColumn ec in EventManager.ExportColumns.Where(ec1 => ec1.Enabled))
                    {
                        switch (ec.Type)
                        {
                            case ExportColumn.ColumnTypes.PilotName:
                                line.Add(p.Name);
                                break;

                            case ExportColumn.ColumnTypes.Position:
                                if (dnfed && showDNF)
                                {
                                    line.Add("DNF");
                                }
                                else if (r != null)
                                {
                                    line.Add(r.Position.ToString());
                                }
                                else
                                {
                                    line.Add("");
                                }
                                break;

                            case ExportColumn.ColumnTypes.ConsecutiveLapsTime:
                                if (lapstime == TimeSpan.MaxValue)
                                {
                                    line.Add("");
                                }
                                else
                                {
                                    line.Add(lapsTimeString);
                                }
                                break;

                            case ExportColumn.ColumnTypes.FastestLapTime:
                                if (fastestLap == TimeSpan.MaxValue)
                                {
                                    line.Add("");
                                }
                                else
                                {
                                    line.Add(fastestLap.TotalSeconds.ToString("0.00"));
                                }
                                break;
                            case ExportColumn.ColumnTypes.PBTime:
                                if (pbTime == TimeSpan.MaxValue)
                                {
                                    line.Add("");
                                }
                                else
                                {
                                    line.Add(pbTime.TotalSeconds.ToString("0.00"));
                                }
                                break;
                            case ExportColumn.ColumnTypes.RaceTime:
                                if (raceTime == TimeSpan.MaxValue)
                                {
                                    line.Add("");
                                }
                                else
                                {
                                    line.Add(raceTimeString);
                                }
                                break;
                            case ExportColumn.ColumnTypes.RoundNumber:
                                line.Add(race.RoundNumber.ToString());
                                break;

                            case ExportColumn.ColumnTypes.RaceNumber:
                                line.Add(race.RaceNumber.ToString());
                                break;

                            case ExportColumn.ColumnTypes.FastestSpeed:
                                float fastestSpeed = EventManager.SpeedRecordManager.GetFastestSpeed(race, p);
                                line.Add(EventManager.SpeedRecordManager.SpeedToString(fastestSpeed, units));
                                break;

                            case ExportColumn.ColumnTypes.AverageSpeed:
                                line.Add(EventManager.SpeedRecordManager.GetAverageSpeed(race, p).ToString());
                                break;

                            case ExportColumn.ColumnTypes.Distance:
                                float distance = EventManager.SpeedRecordManager.GetDistance(race, p);
                                line.Add(Sector.LengthHuman(units, distance));
                                break;

                            case ExportColumn.ColumnTypes.Lap1Time:
                                line.Add(GetLapTime(1, laps));
                                break;
                            case ExportColumn.ColumnTypes.Lap2Time:
                                line.Add(GetLapTime(2, laps));
                                break;
                            case ExportColumn.ColumnTypes.Lap3Time:
                                line.Add(GetLapTime(3, laps));
                                break;
                            case ExportColumn.ColumnTypes.Lap4Time:
                                line.Add(GetLapTime(4, laps));
                                break;
                            case ExportColumn.ColumnTypes.Lap5Time:
                                line.Add(GetLapTime(5, laps));
                                break;
                            case ExportColumn.ColumnTypes.Lap6Time:
                                line.Add(GetLapTime(6, laps));
                                break;
                            case ExportColumn.ColumnTypes.Points:
                                line.Add(r.Points.ToString()); 
                                break;

                        }
                    }
                }

                output.Add(line.ToArray());
            }

            return output.ToArray();
        }

        private string GetLapTime(int number, IEnumerable<Lap> laps)
        {
            Lap lap = laps.FirstOrDefault(l => l.Number == number);
            if (lap != null)
            {
                return lap.Length.TotalSeconds.ToString("0.00");
            }
            else
            {
                return " ";
            }
        }

        public void Clear()
        {
            lock (Results)
            {
                using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                {
                    db.Delete(Results);
                }
                Results.Clear();
            }
        }

        public void Recalculate(Round endRound)
        {
            Round start = GetStartRound(endRound);

            bool rollOver = RollOver(endRound);

            IEnumerable<Race> races = GetRoundPointRaces(endRound);
            if (endRound.RoundType == Round.RoundTypes.Final && rollOver)
            {
                Round lastOfRounds = EventManager.ResultManager.GetLastRoundBeforeFinals(start);

                Pilot[] pilots = races.SelectMany(r => r.Pilots).Distinct().ToArray();
                foreach (Pilot p in pilots)
                {
                    IEnumerable<Race> pilotRaces = races.Where(r => r.HasPilot(p));
                    if (races.Any())
                    {
                        GetRollOver(p, lastOfRounds, true);
                    }
                }
            }
        }

        public void ReCalculateRaces(Round endRound)
        {
            IEnumerable<Race> races = GetRoundPointRaces(endRound);
            foreach (Race race in races)
            {
                SaveResults(race);
            }
        }

        public Pilot[] GetPositions(IEnumerable<Pilot> pilots, Round lastRound)
        {
            Dictionary<Pilot, int> pilotPoints = GetPilotPoints(pilots, lastRound);

            return pilotPoints.OrderByDescending(kvp => kvp.Value).Select(kvp => kvp.Key).ToArray();
        }

        private bool RollOver(Round round)
        {
            if (round != null && round.Stage != null && round.Stage.PointSummary != null)
            {
                return round.Stage.PointSummary.RoundPositionRollover;
            }
            return false;
        }

        private bool DropWorst(Round round)
        {
            if (round != null && round.Stage != null && round.Stage.PointSummary != null)
            {
                return round.Stage.PointSummary.DropWorstRound;
            }
            return false;
        }

        public Dictionary<Pilot, int> GetPilotPoints(IEnumerable<Pilot> pilots, Round lastRound)
        {
            Dictionary<Pilot, int> pilotPoints = new Dictionary<Pilot, int>();
            IEnumerable<Race> races = GetRoundPointRaces(lastRound);

            bool rollOver = RollOver(lastRound);

            foreach (Pilot p in pilots.Distinct())
            {
                IEnumerable<Race> pilotRaces = races.Where(r => r.HasPilot(p));
                if (races.Any())
                {
                    int points = 0;
                    IEnumerable<Result> results = GetResults(pilotRaces, p, rollOver);
                    if (results.Any())
                    {
                        points = results.Sum(r => r.Points);
                    }

                    pilotPoints.Add(p, points);
                }
            }
            return pilotPoints;
        }

        public Round GetLastRoundBeforeFinals(Round startOfFinals)
        {
            if (startOfFinals != null)
            {
                return EventManager.RoundManager.GetCreateRound(startOfFinals.RoundNumber - 1, startOfFinals.EventType);
            }
            return null;
        }
    }
}
