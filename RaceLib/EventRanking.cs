using System;
using System.Collections.Generic;
using System.Linq;

namespace RaceLib
{
    public enum ResultModes
    {
        Auto,
        LapTime,
        Points,
        LastRace,
        None
    }

    // A pilot's final place in the event, and the stage that decided it (null when
    // it came from rounds with no stage, or the event-wide lap time rule).
    public class EventPlacing
    {
        public Pilot Pilot { get; set; }
        public int? Position { get; set; }
        public Stage Stage { get; set; }

        // Placed by hand (Event.FinalResultPins), not calculated
        public bool Pinned { get; set; }
    }

    // A saved row of the final results (FinalResults.json in the event folder), so the
    // website and anything else gets exactly what the race director saw, pins included.
    public class FinalResult : BaseObject
    {
        public Pilot Pilot { get; set; }
        public int? Position { get; set; }
        public Stage Stage { get; set; }
        public bool Pinned { get; set; }
    }

    // A pilot placed by hand in the final results, eg a tie settled some other way.
    // Everyone else fills the places around the pins in calculated order.
    public class FinalResultPin
    {
        public Guid Pilot { get; set; }
        public int Position { get; set; }
    }

    // Works out each pilot's final position in the event. Ported from the website's
    // EventSummaryBuilder.php (rules in PILOT_STATS_PLAN.md, "Final position rules"):
    //  - ResultMode can force one rule for the whole event: LapTime, Points, LastRace, or None (not ranked).
    //    The rest describes Auto.
    //  - No stages and no Race rounds (pure time trial): rank by best PB lap time.
    //  - Otherwise split the event into segments (each stage, and each run of rounds with no stage) and
    //    work backwards from the last one, so a pilot's last result counts.
    //  - A segment whose pilots held consecutive places in the segment before (a tie-break, a playoff for
    //    3rd, a top-N final) decides those places: its pilots are reordered within them. A segment whose
    //    pilots were spread out (eg a final for each bracket's winner) puts them above everyone else.
    public class EventRanking
    {
        private static readonly EventTypes[] competitiveTypes = { EventTypes.TimeTrial, EventTypes.Race, EventTypes.Endurance };
        private static readonly EventTypes[] pointsTypes = { EventTypes.Race, EventTypes.Endurance };

        private const double NoPosition = 1000;

        private EventManager eventManager;

        // One per pilot per finished race
        private class Row
        {
            public Pilot Pilot;
            public Race Race;
            public Round Round;
            public Stage Stage;
            public int? Position;
            public int Points;
            public Brackets Bracket;
        }

        // Lower keys rank higher, equal keys are tied
        private class Entry
        {
            public Pilot Pilot;
            public double[] Key;
        }

        private class Segment
        {
            public Stage Stage;
            public HashSet<Round> Rounds = new HashSet<Round>();
            public int Order;
            public List<Entry> Ranking;
        }

        public EventRanking(EventManager eventManager)
        {
            this.eventManager = eventManager;
        }

        public IEnumerable<EventPlacing> Calculate(ResultModes resultMode)
        {
            Row[] rows = GetRows().ToArray();
            Dictionary<Pilot, EventPlacing> placings = rows.Select(r => r.Pilot).Distinct().ToDictionary(p => p, p => new EventPlacing() { Pilot = p });

            foreach (EventPlacing placing in GetPositions(rows, resultMode))
            {
                placings[placing.Pilot] = placing;
            }

            List<EventPlacing> ordered = placings.Values.OrderBy(p => p.Position ?? int.MaxValue).ThenBy(p => p.Pilot.Name).ToList();
            return ApplyPins(ordered, eventManager.Event.FinalResultPins);
        }

        // Pinned pilots take their place; everyone else with a position fills the remaining places in
        // calculated order. Calculated ties stay tied while their pilots stay next to each other.
        private IEnumerable<EventPlacing> ApplyPins(List<EventPlacing> calculated, FinalResultPin[] pins)
        {
            if (pins == null || !pins.Any())
                return calculated;

            Dictionary<Guid, int> pinned = new Dictionary<Guid, int>();
            foreach (FinalResultPin pin in pins.Where(p => p.Position > 0))
            {
                pinned[pin.Pilot] = pin.Position;
            }

            Dictionary<Guid, Pilot> eventPilots = eventManager.Event.Pilots.Where(p => !p.PracticePilot).GroupBy(p => p.ID).ToDictionary(g => g.Key, g => g.First());
            Dictionary<Guid, EventPlacing> byPilot = calculated.ToDictionary(p => p.Pilot.ID);

            // Pins for pilots who have since left the event are ignored
            List<EventPlacing> pinnedPlacings = new List<EventPlacing>();
            foreach (var kvp in pinned.OrderBy(k => k.Value))
            {
                Pilot pilot;
                if (!eventPilots.TryGetValue(kvp.Key, out pilot))
                    continue;

                EventPlacing calc;
                byPilot.TryGetValue(kvp.Key, out calc);
                pinnedPlacings.Add(new EventPlacing() { Pilot = pilot, Position = kvp.Value, Stage = calc?.Stage, Pinned = true });
            }

            HashSet<Guid> pinnedIds = new HashSet<Guid>(pinnedPlacings.Select(p => p.Pilot.ID));
            Queue<EventPlacing> unpinned = new Queue<EventPlacing>(calculated.Where(p => p.Position != null && !pinnedIds.Contains(p.Pilot.ID)));
            Dictionary<int, EventPlacing> pinAt = pinnedPlacings.GroupBy(p => p.Position.Value).ToDictionary(g => g.Key, g => g.First());

            List<EventPlacing> result = new List<EventPlacing>();
            EventPlacing previous = null;
            int? previousCalculated = null;
            int place = 1;
            int total = unpinned.Count + pinnedPlacings.Count;
            while (place <= total)
            {
                EventPlacing pin;
                if (pinAt.TryGetValue(place, out pin))
                {
                    result.Add(pin);
                    previous = null;
                    previousCalculated = null;
                }
                else if (unpinned.Any())
                {
                    EventPlacing next = unpinned.Dequeue();
                    int position = previous != null && previousCalculated == next.Position ? previous.Position.Value : place;
                    previousCalculated = next.Position;
                    previous = new EventPlacing() { Pilot = next.Pilot, Position = position, Stage = next.Stage };
                    result.Add(previous);
                }
                place++;
            }

            // Pinned pilots past the end take the next places, then everyone without a position
            foreach (EventPlacing pin in pinnedPlacings.Where(p => !result.Contains(p)).ToArray())
            {
                pin.Position = result.Count + 1;
                result.Add(pin);
            }
            result.AddRange(calculated.Where(p => p.Position == null && !pinnedIds.Contains(p.Pilot.ID)));
            return result;
        }

        private IEnumerable<Row> GetRows()
        {
            foreach (Race race in eventManager.RaceManager.Races)
            {
                Round round = race.Round;
                if (!race.Valid || !race.Ended || round == null || !round.Valid || !competitiveTypes.Contains(round.EventType))
                    continue;

                Stage stage = round.Stage != null && round.Stage.Valid ? round.Stage : null;

                foreach (Pilot pilot in race.Pilots.Where(p => !p.PracticePilot))
                {
                    Result result = eventManager.ResultManager.GetResult(race, pilot);

                    int? position = null;
                    if (result != null && !result.DNF && result.Position > 0)
                    {
                        position = result.Position;
                    }

                    yield return new Row()
                    {
                        Pilot = pilot,
                        Race = race,
                        Round = round,
                        Stage = stage,
                        Position = position,
                        Points = result != null ? result.Points : 0,
                        Bracket = race.Bracket
                    };
                }
            }
        }

        private IEnumerable<EventPlacing> GetPositions(Row[] rows, ResultModes resultMode)
        {
            switch (resultMode)
            {
                case ResultModes.None:
                    return new EventPlacing[0];
                case ResultModes.LapTime:
                    return AssignPositions(new[] { new Segment() { Ranking = Timed(RankByTime(rows, TimeSummary.TimeSummaryTypes.PB)) } });
                case ResultModes.Points:
                    return AssignPositions(new[] { new Segment() { Ranking = RankByEventPoints(rows) } });
                case ResultModes.LastRace:
                    return AssignPositions(new[] { new Segment() { Ranking = RankByLastRace(rows) } });
            }

            List<Segment> segments = GetSegments(rows);

            bool hasStages = segments.Any(s => s.Stage != null);
            bool hasRaces = rows.Any(r => pointsTypes.Contains(r.Round.EventType));
            if (!hasStages && !hasRaces)
            {
                // Pure time trial: best PB lap time over the event
                return AssignPositions(new[] { new Segment() { Ranking = Timed(RankByTime(rows, TimeSummary.TimeSummaryTypes.PB)) } });
            }

            // Fold segments that decide consecutive places into the segment before them
            List<Segment> effective = new List<Segment>();
            foreach (Segment segment in segments)
            {
                segment.Ranking = RankSegment(segment, rows);

                Segment last = effective.LastOrDefault();
                if (last != null && DecidesConsecutivePlaces(last.Ranking, segment.Ranking))
                {
                    last.Ranking = ReorderPlaces(last.Ranking, segment.Ranking);
                    if (segment.Stage != null)
                    {
                        last.Stage = segment.Stage;
                    }
                    continue;
                }

                effective.Add(segment);
            }

            effective.Reverse();
            return AssignPositions(effective);
        }

        // Segments in the order they ran. Each valid stage is one segment, and each run of rounds with
        // no stage is another.
        private List<Segment> GetSegments(Row[] rows)
        {
            IEnumerable<Round> rounds = rows.Select(r => r.Round).Distinct().OrderBy(r => r.Order);

            List<Segment> segments = new List<Segment>();
            Dictionary<Stage, Segment> stageSegments = new Dictionary<Stage, Segment>();
            foreach (Round round in rounds)
            {
                Stage stage = round.Stage != null && round.Stage.Valid ? round.Stage : null;
                Segment last = segments.LastOrDefault();

                Segment segment;
                if (stage != null && stageSegments.TryGetValue(stage, out segment))
                {
                }
                else if (stage == null && last != null && last.Stage == null)
                {
                    segment = last;
                }
                else
                {
                    segment = new Segment() { Stage = stage };
                    segments.Add(segment);
                    if (stage != null)
                    {
                        stageSegments.Add(stage, segment);
                    }
                }

                segment.Rounds.Add(round);
                segment.Order = Math.Max(segment.Order, round.Order);
            }

            return segments.OrderBy(s => s.Order).ToList();
        }

        private List<Entry> RankSegment(Segment segment, Row[] rows)
        {
            Row[] segmentRows = rows.Where(r => segment.Rounds.Contains(r.Round)).ToArray();
            Stage stage = segment.Stage;

            if (stage != null && stage.Standings != null && stage.Standings.Rows != null && stage.Standings.Rows.Any())
            {
                return RankByStandings(segmentRows, stage.Standings.Rows);
            }

            if (stage != null && stage.PointSummary != null)
            {
                return RankByStagePoints(segment, segmentRows);
            }

            if (stage != null && stage.TimeSummary != null)
            {
                TimeSummary.TimeSummaryTypes type = stage.TimeSummary.TimeSummaryType;
                if (stage.TimeSummary.IncludeAllRounds)
                {
                    // Include every earlier round, not just this stage's
                    Row[] timeRows = rows.Where(r => r.Round.Order <= segment.Order).ToArray();
                    HashSet<Pilot> pilots = new HashSet<Pilot>(segmentRows.Select(r => r.Pilot));
                    return RankByTime(timeRows, type).Where(e => pilots.Contains(e.Pilot)).ToList();
                }
                return RankByTime(segmentRows, type);
            }

            return RankByLastRace(segmentRows);
        }

        private List<Entry> RankByStandings(Row[] rows, Format.StandingsRow[] standingsRows)
        {
            Dictionary<Guid, int> index = new Dictionary<Guid, int>();
            for (int i = 0; i < standingsRows.Length; i++)
            {
                Guid? id = standingsRows[i].PilotId;
                if (id.HasValue && !index.ContainsKey(id.Value))
                {
                    index.Add(id.Value, i);
                }
            }

            List<Entry> ranking = new List<Entry>();
            foreach (Pilot pilot in Pilots(rows))
            {
                int i;
                if (!index.TryGetValue(pilot.ID, out i))
                {
                    i = standingsRows.Length;
                }
                ranking.Add(new Entry() { Pilot = pilot, Key = new double[] { i } });
            }
            return Sort(ranking);
        }

        // Best bracket raced in, then the stage's points total, as the Points tab works it out
        // (drop worst round, round position rollover).
        private List<Entry> RankByStagePoints(Segment segment, Row[] rows)
        {
            Round endRound = segment.Rounds.OrderBy(r => r.Order).Last();

            List<Entry> ranking = new List<Entry>();
            foreach (Pilot pilot in Pilots(rows))
            {
                Row[] pointsRows = rows.Where(r => r.Pilot == pilot && pointsTypes.Contains(r.Round.EventType)).ToArray();
                if (!pointsRows.Any())
                {
                    ranking.Add(new Entry() { Pilot = pilot, Key = new double[] { double.MaxValue, 0 } });
                    continue;
                }

                // Best bracket they raced in, so a bracket-less playoff race doesn't drop them below the B bracket
                int bracketRank = pointsRows.Min(r => BracketRank(r.Bracket));
                int total = eventManager.ResultManager.GetPointsTotal(endRound, pilot);
                ranking.Add(new Entry() { Pilot = pilot, Key = new double[] { bracketRank, -total } });
            }
            return Sort(ranking);
        }

        // Points over the whole event, no dropped rounds
        private List<Entry> RankByEventPoints(Row[] rows)
        {
            List<Entry> ranking = new List<Entry>();
            foreach (Pilot pilot in Pilots(rows))
            {
                Row[] pointsRows = rows.Where(r => r.Pilot == pilot && pointsTypes.Contains(r.Round.EventType)).ToArray();
                if (!pointsRows.Any())
                {
                    ranking.Add(new Entry() { Pilot = pilot, Key = new double[] { double.MaxValue, 0 } });
                    continue;
                }

                int bracketRank = pointsRows.Min(r => BracketRank(r.Bracket));
                ranking.Add(new Entry() { Pilot = pilot, Key = new double[] { bracketRank, -pointsRows.Sum(r => r.Points) } });
            }
            return Sort(ranking);
        }

        // Pilots with no time rank last, tied
        private List<Entry> RankByTime(IEnumerable<Row> rows, TimeSummary.TimeSummaryTypes type)
        {
            Dictionary<Pilot, double?> best = new Dictionary<Pilot, double?>();
            foreach (Row r in rows)
            {
                double? current;
                best.TryGetValue(r.Pilot, out current);
                best[r.Pilot] = Min(current, GetTime(r, type));
            }

            List<Entry> ranking = new List<Entry>();
            foreach (var kvp in best)
            {
                double[] key = kvp.Value.HasValue ? new double[] { 0, kvp.Value.Value } : new double[] { 1, 0 };
                ranking.Add(new Entry() { Pilot = kvp.Key, Key = key });
            }
            return Sort(ranking);
        }

        // For segments with no result (eg elimination brackets): the later the last race a pilot reached,
        // the higher they rank, then by their position in that race. Only Race/Enduro rounds count;
        // pilots who only flew time trials go after, by lap time.
        private List<Entry> RankByLastRace(Row[] rows)
        {
            Row[] raceRows = rows.Where(r => pointsTypes.Contains(r.Round.EventType)).ToArray();

            // A race where everyone has the same position, or none, says nothing about order (eg an abandoned
            // re-run with everyone 4th), so skip it as long as there's a race that does
            Row[] informative = raceRows.GroupBy(r => r.Race).Where(g => g.Select(r => r.Position).Distinct().Count() > 1).SelectMany(g => g).ToArray();
            if (informative.Any())
            {
                raceRows = informative;
            }

            if (!raceRows.Any())
            {
                return RankByTime(rows, TimeSummary.TimeSummaryTypes.PB);
            }

            Race[] races = raceRows.Select(r => r.Race).Distinct().OrderBy(r => r.Round.Order).ThenBy(r => r.RaceNumber).ToArray();
            Dictionary<Race, int> raceIndex = new Dictionary<Race, int>();
            for (int i = 0; i < races.Length; i++)
            {
                raceIndex.Add(races[i], i);
            }

            Dictionary<Pilot, Row> last = new Dictionary<Pilot, Row>();
            foreach (Row r in raceRows)
            {
                Row current;
                if (!last.TryGetValue(r.Pilot, out current) || raceIndex[r.Race] > raceIndex[current.Race])
                {
                    last[r.Pilot] = r;
                }
            }

            List<Entry> ranking = new List<Entry>();
            foreach (var kvp in last)
            {
                Row r = kvp.Value;
                ranking.Add(new Entry() { Pilot = kvp.Key, Key = new double[] { 0, -raceIndex[r.Race], r.Position ?? NoPosition } });
            }

            IEnumerable<Row> timeOnly = rows.Where(r => !last.ContainsKey(r.Pilot));
            foreach (Entry entry in RankByTime(timeOnly, TimeSummary.TimeSummaryTypes.PB))
            {
                ranking.Add(new Entry() { Pilot = entry.Pilot, Key = new double[] { 1, entry.Key[0], entry.Key[1] } });
            }
            return Sort(ranking);
        }

        // True if every pilot in ranking was in previous and they held consecutive places there.
        // Pilots tied in previous count as interchangeable, so a tie with an outsider doesn't break the run.
        private static bool DecidesConsecutivePlaces(List<Entry> previous, List<Entry> ranking)
        {
            if (ranking.Count < 2)
                return false;

            Dictionary<Pilot, int> groupStart = GroupStarts(previous);
            HashSet<Pilot> inSegment = new HashSet<Pilot>(ranking.Select(e => e.Pilot));

            List<int> starts = new List<int>();
            foreach (Pilot pilot in inSegment)
            {
                int start;
                if (!groupStart.TryGetValue(pilot, out start))
                    return false;
                starts.Add(start);
            }

            int lo = starts.Min();
            int hi = starts.Max();
            foreach (var kvp in groupStart)
            {
                if (!inSegment.Contains(kvp.Key) && kvp.Value > lo && kvp.Value < hi)
                    return false;
            }
            return true;
        }

        // Moves the ranking pilots, in their ranking order, to where the first of them sat in previous.
        // Anyone tied with them in previous goes after them.
        private static List<Entry> ReorderPlaces(List<Entry> previous, List<Entry> ranking)
        {
            HashSet<Pilot> inSegment = new HashSet<Pilot>(ranking.Select(e => e.Pilot));
            Dictionary<Pilot, int> groupStart = GroupStarts(previous);

            int insertAt = ranking.Min(e => groupStart[e.Pilot]);

            Entry[] others = previous.Where(e => !inSegment.Contains(e.Pilot)).ToArray();

            List<Tuple<Entry, bool>> tagged = new List<Tuple<Entry, bool>>();
            tagged.AddRange(others.Where(e => groupStart[e.Pilot] < insertAt).Select(e => Tuple.Create(e, false)));
            tagged.AddRange(ranking.Select(e => Tuple.Create(e, true)));
            tagged.AddRange(others.Where(e => groupStart[e.Pilot] >= insertAt).Select(e => Tuple.Create(e, false)));

            // New keys: one per place, keeping ties only between neighbours that were tied before
            // (outside pilots) or are still tied (segment pilots)
            List<Entry> result = new List<Entry>();
            int place = -1;
            for (int i = 0; i < tagged.Count; i++)
            {
                Entry entry = tagged[i].Item1;
                bool tiedWithPrevious = i > 0 && tagged[i - 1].Item2 == tagged[i].Item2 && SameKey(tagged[i - 1].Item1.Key, entry.Key);
                if (!tiedWithPrevious)
                {
                    place++;
                }
                result.Add(new Entry() { Pilot = entry.Pilot, Key = new double[] { place } });
            }
            return result;
        }

        // pilot => index of the first entry of the tie group they're in
        private static Dictionary<Pilot, int> GroupStarts(List<Entry> ranking)
        {
            Dictionary<Pilot, int> starts = new Dictionary<Pilot, int>();
            for (int i = 0; i < ranking.Count; i++)
            {
                Entry entry = ranking[i];
                starts[entry.Pilot] = i > 0 && SameKey(entry.Key, ranking[i - 1].Key) ? starts[ranking[i - 1].Pilot] : i;
            }
            return starts;
        }

        // Walks segments from last to first, giving each unplaced pilot the next position. Tied pilots share one.
        private static IEnumerable<EventPlacing> AssignPositions(IEnumerable<Segment> segmentsLastFirst)
        {
            Dictionary<Pilot, EventPlacing> positions = new Dictionary<Pilot, EventPlacing>();
            int next = 1;
            foreach (Segment segment in segmentsLastFirst)
            {
                double[] previousKey = null;
                int previousPosition = 0;
                foreach (Entry entry in segment.Ranking)
                {
                    if (positions.ContainsKey(entry.Pilot))
                        continue;

                    int position = previousKey != null && SameKey(entry.Key, previousKey) ? previousPosition : next;
                    positions.Add(entry.Pilot, new EventPlacing() { Pilot = entry.Pilot, Position = position, Stage = segment.Stage });

                    previousKey = entry.Key;
                    previousPosition = position;
                    next++;
                }
            }
            return positions.Values;
        }

        // Drops pilots with no time, so they get no position
        private static List<Entry> Timed(List<Entry> ranking)
        {
            return ranking.Where(e => e.Key[0] == 0).ToList();
        }

        private double? GetTime(Row r, TimeSummary.TimeSummaryTypes type)
        {
            TimeSpan time;
            switch (type)
            {
                case TimeSummary.TimeSummaryTypes.EventLap:
                    Lap[] laps = LapRecordManager.GetBestRaceTime(new[] { r.Race }, r.Pilot, eventManager.Event.Laps).ToArray();
                    if (!laps.Any())
                        return null;
                    time = laps.TotalTime();
                    break;
                case TimeSummary.TimeSummaryTypes.RaceTime:
                    Result result = eventManager.ResultManager.GetResult(r.Race, r.Pilot);
                    if (result == null || result.DNF)
                        return null;
                    time = result.Time;
                    break;
                default:
                    time = r.Race.GetBestLapsTime(r.Pilot, eventManager.Event.PBLaps);
                    break;
            }

            if (time <= TimeSpan.Zero || time == TimeSpan.MaxValue)
                return null;
            return time.TotalSeconds;
        }

        // Winners, Losers, then A, B, C..., then None
        private static int BracketRank(Brackets bracket)
        {
            switch (bracket)
            {
                case Brackets.Winners: return 0;
                case Brackets.Losers: return 1;
                case Brackets.None: return 100;
                default: return 2 + (bracket - Brackets.A);
            }
        }

        private static IEnumerable<Pilot> Pilots(IEnumerable<Row> rows)
        {
            return rows.Select(r => r.Pilot).Distinct();
        }

        // Sorted by key, pilot id as the final tie-breaker so results don't change between runs
        private static List<Entry> Sort(List<Entry> ranking)
        {
            return ranking.OrderBy(e => e.Key, KeyComparer.Instance).ThenBy(e => e.Pilot.ID).ToList();
        }

        private static bool SameKey(double[] a, double[] b)
        {
            return KeyComparer.Instance.Compare(a, b) == 0;
        }

        private static double? Min(double? a, double? b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return Math.Min(a.Value, b.Value);
        }

        private class KeyComparer : IComparer<double[]>
        {
            public static readonly KeyComparer Instance = new KeyComparer();

            // Element by element, like PHP's array <=>; a shorter key sorts first
            public int Compare(double[] a, double[] b)
            {
                if (a.Length != b.Length)
                    return a.Length.CompareTo(b.Length);

                for (int i = 0; i < a.Length; i++)
                {
                    int c = a[i].CompareTo(b[i]);
                    if (c != 0)
                        return c;
                }
                return 0;
            }
        }
    }
}
