using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace RaceLib
{
    public class LapRecordManager
    {
        public int[] ConsecutiveLapsToTrack { get; set; }

        public event PilotLapRecord.NewRecord OnNewPersonalBest;
        public event PilotLapRecord.NewRecord OnNewOveralBest;

        public RaceManager RaceManager { get; private set; }
        public EventManager EventManager { get; private set; }

        private Dictionary<int, Lap[]> overallBest;
        public Dictionary<Pilot, PilotLapRecord> Records { get; private set; }

        private Dictionary<Pilot, PilotPosition> pastPositions;

        public LapRecordManager(RaceManager rm)
        {
            RaceManager = rm;
            rm.OnLapDetected += RecordLap;
            rm.OnLapDisqualified += DisqLap;
            rm.OnLapSplit += Rm_OnLapSplit;
            rm.OnRaceRemoved += Rm_OnRaceRemoved;
            rm.OnRaceChanged += (r) => { CachePastPositions(); };

            EventManager = RaceManager.EventManager;
            EventManager.OnEventChange += UpdateAll;

            Records = new Dictionary<Pilot, PilotLapRecord>();
            ConsecutiveLapsToTrack = new int[0];
            overallBest = new Dictionary<int, Lap[]>();
            pastPositions = new Dictionary<Pilot, PilotPosition>();
        }

        private void Rm_OnRaceRemoved(Race race)
        {
            UpdateAll();
        }

        private void Rm_OnLapSplit(IEnumerable<Lap> laps)
        {
            Pilot pilot = laps.Select(l => l.Pilot).FirstOrDefault();
            if (pilot != null)
            {
                UpdatePilot(pilot);
            }
        }

        public int GetTimePosition(Pilot pilot)
        {
            return GetPosition(pilot, EventManager.Event.Laps);
        }

        public int GetPBTimePosition(Pilot pilot)
        {
            return GetPosition(pilot, EventManager.Event.PBLaps);
        }

        public int GetPosition(Pilot pilot, int laps)
        {
            int position;
            Pilot behindWho;
            TimeSpan behind;

            GetPosition(pilot, laps, out position, out behindWho, out behind);
            return position;
        }

        public bool GetPosition(Pilot pilot, int laps, out int position, out Pilot behindWho, out TimeSpan behind)
        {
            lock (Records)
            {
                position = Records.Count;
                behindWho = null;
                behind = TimeSpan.Zero;

                if (pilot == null)
                    return false;

                if (!ConsecutiveLapsToTrack.Contains(laps))
                    return false;

                PilotLapRecord thisRecord = GetPilotLapRecord(pilot);

                TimeSpan thisTime = thisRecord.GetBestConsecutiveLaps(laps).TotalTime();
                if (thisTime == TimeSpan.MaxValue)
                    return false;
                position = 1;

                IEnumerable<PilotLapRecord> ordered = Records.Values.OrderBy(record => record.GetBestConsecutiveLaps(laps).TotalTime()).ThenBy(plr => plr.Pilot.Name);
                PilotLapRecord prev = null;
                foreach (PilotLapRecord record in ordered)
                {
                    if (record.Pilot == thisRecord.Pilot)
                    {
                        if (prev != null)
                        {
                            TimeSpan prevTime = prev.GetBestConsecutiveLaps(laps).TotalTime();
                            behindWho = prev.Pilot;
                            behind = thisTime - prevTime;
                        }
                        return true;
                    }
                    position++;
                    prev = record;
                }
            }
            return false;
        }

        private void UpdateLapCounts()
        {
            if (EventManager.Event != null)
            {
                List<int> toAdd = new List<int>();
                toAdd.Add(1);
                toAdd.Add(EventManager.Event.PBLaps);
                toAdd.Add(EventManager.Event.Laps);

                if (EventManager.Event.PrimaryTimingSystemLocation == PrimaryTimingSystemLocation.Holeshot)
                {
                    toAdd.Add(0);
                }

                ConsecutiveLapsToTrack = toAdd.Distinct().OrderBy(i => i).ToArray();
            }
        }

        public void RecordLap(Lap lap)
        {
            Race race = lap.Race;
            if (race == null)
                return;

            if (!race.Valid)
                return;

            Lap[] laps = race.GetLaps(l => l.Pilot == lap.Pilot && l.Detection.Valid);
            if (IsRecord(lap.Pilot, laps))
            {
                UpdatePilot(lap.Pilot);
            }
        }

        private bool IsRecord(Pilot pilot, Lap[] laps)
        {
            if (laps.All(l => l.Detection.Valid))
            {
                PilotLapRecord plr = GetPilotLapRecord(pilot);
                foreach (int consecutive in ConsecutiveLapsToTrack)
                {
                    if (plr.GetBestConsecutiveLaps(consecutive).TotalTime() > laps.BestConsecutive(consecutive).TotalTime())
                    {
                        return true;
                    }
                }
                int raceLaps = EventManager.Event.Laps;
                if (EventManager.Event.PrimaryTimingSystemLocation == PrimaryTimingSystemLocation.Holeshot)
                    raceLaps++;

                if (laps.Length == raceLaps) 
                {
                    if (laps.TotalTime() < plr.GetBestRaceTime().TotalTime())
                        return true;
                }
            }
            return false;
        }

        public void DisqLap(Lap lap)
        {
            if (overallBest.Values.SelectMany(s => s).Contains(lap))
            {
                overallBest.Clear();
                UpdateAll();
            }
            else
            {
                UpdatePilot(lap.Pilot);
            }
        }

        public void ResetRace(Race race)
        {
            foreach (Pilot p in race.Pilots)
            {
                UpdatePilot(p);
            }
        }

        public void UpdatePilot(Pilot pilot)
        {
            PilotLapRecord plr = GetPilotLapRecord(pilot);
            if (plr == null)
                return;

            foreach (int consecutive in ConsecutiveLapsToTrack)
            {
                plr.UpdateBestConsecutiveLaps(consecutive);
            }

            if (EventManager.Event.EventType.HasLapCount())
            {
                plr.UpdateRaceTime(EventManager.Event.Laps);
            }
        }

        private PilotLapRecord GetPilotLapRecord(Pilot pilot)
        {
            if (pilot == null)
                return null;

            PilotLapRecord plr = null;
            lock (Records)
            {
                if (!Records.TryGetValue(pilot, out plr))
                {
                    plr = new PilotLapRecord(this, pilot);
                    Records.Add(pilot, plr);
                    plr.OnNewBest += OnNewPilotBest;
                }
            }

            return plr;
        }

        private void OnNewPilotBest(Pilot p, int lapCount, Lap[] laps)
        {
            OnNewPersonalBest?.Invoke(p, lapCount, laps);

            if (laps.Length != 0 && IsOverallBest(lapCount, laps))
            {
                OnNewOveralBest?.Invoke(p, lapCount, laps);
            }
        }

        public bool IsOverallBest(int lapCount, Lap[] laps)
        {
            bool isBest = false;

            if (laps.Length == 0)
            {
                return false;
            }

            Lap[] bestLaps;
            if (overallBest.TryGetValue(lapCount, out bestLaps))
            {
                if (bestLaps.TotalTime() > laps.TotalTime())
                {
                    overallBest[lapCount] = laps;
                    isBest = true;
                }

                if (bestLaps.FirstOrDefault() == laps.FirstOrDefault() && bestLaps.FirstOrDefault() != null)
                {
                    isBest = true;
                }
            }
            else
            {
                overallBest.Add(lapCount, laps);
                isBest = true;
            }

            return isBest;
        }

        public void UpdateAll()
        {
            UpdatePilots(Records.Keys);
        }

        public void UpdatePilots(IEnumerable<Pilot> pilots)
        {
            UpdateLapCounts();

            foreach (Pilot p in pilots)
            {
                GetPilotLapRecord(p);
            }

            lock (Records)
            {
                foreach (PilotLapRecord plr in Records.Values)
                {
                    plr.Clear();
                }

                foreach (PilotLapRecord plr in Records.Values)
                {
                    foreach (int consecutive in ConsecutiveLapsToTrack)
                    {
                        plr.UpdateBestConsecutiveLaps(consecutive);
                    }

                    if (EventManager.Event.EventType.HasLapCount())
                    {
                        plr.UpdateRaceTime(EventManager.Event.Laps);
                    }
                }
            }
        }

        public Lap[] GetPBLaps(Pilot pilot)
        {
            Lap[] output;
            bool overall;
            if (GetBestLaps(pilot, EventManager.Event.PBLaps, out output, out overall))
            {
                return output;
            }

            return new Lap[0];
        }

        public bool GetBestLaps(Pilot pilot, int lapCount, out Lap[] laps, out bool overalBest)
        {
            PilotLapRecord plr = null;
            if (Records.TryGetValue(pilot, out plr))
            {
                laps = plr.GetBestConsecutiveLaps(lapCount);
                if (laps != null && laps.Any())
                {
                    overalBest = IsOverallBest(lapCount, laps);
                    return true;
                }
            }

            overalBest = false;
            laps = new Lap[0];
            return false;
        }

        public bool GetBestRaceTime(Pilot pilot, out Lap[] laps, out bool overalBest)
        {
            PilotLapRecord plr = null;
            if (Records.TryGetValue(pilot, out plr))
            {
                laps = plr.GetBestRaceTime();
                overalBest = false;
                return true;
            }

            overalBest = false;
            laps = new Lap[0];
            return false;
        }

        public bool IsRecordLap(Lap lap, out bool overalBest)
        {
            int lapCount = 1;
            overalBest = false;

            Lap[] laps;
            if (overallBest.TryGetValue(lapCount, out laps))
            {
                if (laps[0].ID == lap.ID)
                {
                    overalBest = true;
                    return true;
                }
            }

            PilotLapRecord plr = GetPilotLapRecord(lap.Pilot);
            if (plr != null)
            {
                laps = plr.GetBestConsecutiveLaps(lapCount);
                if (laps.Any())
                {
                    if (laps[0].ID == lap.ID)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public string[][] ExportPBs()
        {
            List<string> line = new List<string>();

            List<string[]> output = new List<string[]>();

            line.Add("Pilot");

            foreach (int lap in ConsecutiveLapsToTrack)
            {
                if (lap == 0)
                {
                    line.Add("Holeshot");
                }
                else if (lap == 1)
                {
                    line.Add(lap.ToString() + " Lap");
                }
                else
                {
                    line.Add(lap.ToString() + " Laps");
                }
            }

            line.Add("");

            int[] morethanone = ConsecutiveLapsToTrack.Where(r => r > 1).ToArray();

            foreach (int consecutive in morethanone)
            {
                for (int i = 0; i < consecutive; i++)
                {
                    line.Add(consecutive + " Laps - Lap " + (i + 1));
                }
                line.Add("");
            }

            output.Add(line.ToArray());

            lock (Records)
            {
                foreach (var plr in Records.Values.Where(p => !p.Pilot.PracticePilot).OrderBy(r => r.Pilot.Name))
                {
                    line.Clear();

                    line.Add(plr.Pilot.Name);

                    foreach (int consecutive in ConsecutiveLapsToTrack)
                    {
                        Lap[] laps = plr.GetBestConsecutiveLaps(consecutive);
                        if (laps != null && laps.Any())
                        {
                            TimeSpan timeSpan = laps.TotalTime();
                            line.Add(timeSpan.TotalSeconds.ToString("0.000"));
                        }
                        else
                        {
                            line.Add("");
                        }
                    }

                    line.Add("");

                    foreach (int consecutive in morethanone)
                    {
                        Lap[] laps = plr.GetBestConsecutiveLaps(consecutive);

                        foreach (Lap lap in laps)
                        {
                            line.Add(lap.Length.TotalSeconds.ToString("0.000"));
                        }
                        line.Add("");
                    }
                    output.Add(line.ToArray());
                }
            }
            return output.ToArray();
        }

        public void Clear()
        {
            overallBest.Clear();
            Records.Clear();
        }

        public void ClearPilot(Pilot pilot)
        {
            if (pilot == null)
                return;

            lock (Records)
            {
                Records.Remove(pilot);
            }
        }

        public static IEnumerable<Lap> GetBestLaps(IEnumerable<Race> races, Pilot pilot, int consecutive)
        {
            Lap[] bestConsecutive = new Lap[0];
            foreach (Race race in races)
            {
                Lap[] laps = race.GetValidLaps(pilot, false);

                Lap[] thisRacesBest = laps.BestConsecutive(consecutive).ToArray();
                if (thisRacesBest.Any())
                {
                    if (bestConsecutive == null || bestConsecutive.TotalTime() > thisRacesBest.TotalTime())
                    {
                        bestConsecutive = thisRacesBest;
                    }
                }
            }

            return bestConsecutive;
        }

        public static IEnumerable<Lap> GetBestRaceTime(IEnumerable<Race> races, Pilot pilot, int lapCount)
        {
            if (lapCount == 0 || !races.Any())
                return new Lap[0];

            Lap[] best = new Lap[0];
            foreach (Race race in races.Where(r => r.HasPilot(pilot)))
            {
                Lap[] laps = race.GetValidLaps(pilot, true);

                IEnumerable<Lap> bestInRace = null;

                if (laps.Length >= lapCount && laps.Any())
                {
                    if (laps.First().Detection.IsHoleshot)
                    {
                        if (laps.Length >= lapCount + 1)
                            bestInRace = laps.Take(lapCount + 1);
                    }
                    else
                    {
                        bestInRace = laps.Take(lapCount);
                    }
                }

                if (bestInRace == null || !bestInRace.Any())
                    continue;

                if (best == null || bestInRace.TotalTime() < best.TotalTime())
                {
                    best = bestInRace.ToArray();
                }
            }
            return best;
        }

        public static Lap GetBestHoleshot(Race[] races, Pilot pilot)
        {
            Lap best = null;

            foreach (Race race in races)
            {
                if (!race.Valid)
                    continue;

                Lap holeShot = race.GetHoleshot(pilot);
                if (holeShot == null)
                    continue;

                if (best == null)
                {
                    best = holeShot;
                }
                else if (best.Length > holeShot.Length)
                {
                    best = holeShot;
                }
            }
            return best;
        }

        public Pilot[] GetPositions(IEnumerable<Pilot> pilots, int laps)
        {
            return Records.Values.Where(r => pilots.Contains(r.Pilot)).OrderBy(pr => pr.GetBestConsecutiveLaps(laps).TotalTime().TotalSeconds).Select(pr => pr.Pilot).ToArray();
        }

        private void CachePastPositions()
        {
            lock (pastPositions)
            {
                pastPositions.Clear();
                foreach (int laps in ConsecutiveLapsToTrack)
                {
                    IEnumerable<PilotLapRecord> ordered = Records.Values.OrderBy(record => record.GetBestConsecutiveLaps(laps).TotalTime()).ThenBy(plr => plr.Pilot.Name);
                    int position = 1;
                    foreach (PilotLapRecord plr in ordered)
                    {
                        PilotPosition pp;
                        if (!pastPositions.TryGetValue(plr.Pilot, out pp))
                        {
                            pp = new PilotPosition(plr.Pilot);
                            pastPositions.Add(plr.Pilot, pp);
                        }

                        pp.AddPosition(laps, position);
                        position++;
                    }
                }
            }
        }

        public int? GetPastPosition(Pilot p, int laps)
        {
            PilotPosition pp;

            lock (pastPositions)
            {
                if (pastPositions.TryGetValue(p, out pp))
                {
                    int value = pp.GetPosition(laps);
                    if (value > 0)
                        return value;
                }
            }
            return null;
        }
    }

    public class PilotLapRecord
    {
        public delegate void NewRecord(Pilot p, int recordLapCount, Lap[] laps);

        public event NewRecord OnNewBest;

        public Pilot Pilot { get; private set; }
        private Dictionary<int, Lap[]> best;

        private Lap[] bestRaceTime;

        public LapRecordManager RecordManager { get; private set; }

        public string[] Records
        {
            get
            {
                return best.Select(kvp => kvp.Key + ", " + kvp.Value.TotalTime().TotalSeconds).ToArray();
            }
        }

        public PilotLapRecord(LapRecordManager recordManager, Pilot p)
        {
            RecordManager = recordManager;
            Pilot = p;
            best = new Dictionary<int, Lap[]>();
        }

        public void SetBestConsecutiveLaps(IEnumerable<Lap> laps, int lapCount)
        {
            Lap[] aLaps = laps.ToArray();
            if (best.ContainsKey(lapCount))
            {
                best[lapCount] = aLaps;
            }
            else
            {
                best.Add(lapCount, aLaps);
            }
        }

        public Lap[] GetBestConsecutiveLaps(int lapCount)
        {
            Lap[] laps = null;
            if (!best.TryGetValue(lapCount, out laps))
            {
                return new Lap[0];
            }
            return laps;
        }
        public Lap[] GetBestRaceTime()
        {
            return bestRaceTime;
        }

        public void UpdateBestConsecutiveLaps(int lapCount)
        {
            Lap[] bestLaps;

            Race[] races = RecordManager.RaceManager.GetRaces(r => r.HasPilot(Pilot) && r.Type != EventTypes.Practice && r.Valid).ToArray();

            if (lapCount == 0)
            {
                Lap best = LapRecordManager.GetBestHoleshot(races, Pilot);
                if (best == null)
                {
                    bestLaps = new Lap[0];
                }
                else
                {
                    bestLaps = new Lap[] { best };
                }
            }
            else
            {
                bestLaps = LapRecordManager.GetBestLaps(races, Pilot, lapCount).ToArray();
            }

            if (bestLaps.Any())
            {
                if (best.ContainsKey(lapCount))
                {
                    bool oldBestInvalid = best[lapCount].Any(l => !l.Detection.Valid);

                    if (best[lapCount].TotalTime() != bestLaps.TotalTime() || oldBestInvalid)
                    {
                        SetBestConsecutiveLaps(bestLaps.ToArray(), lapCount);

                        OnNewBest?.Invoke(Pilot, lapCount, bestLaps);
                    }
                }
                else
                {
                    SetBestConsecutiveLaps(bestLaps.ToArray(), lapCount);
                    OnNewBest?.Invoke(Pilot, lapCount, bestLaps);
                }
            }
            else
            {
                best.Remove(lapCount);
                OnNewBest?.Invoke(Pilot, lapCount, new Lap[0]);
            }
        }

        public void UpdateRaceTime(int lapCount)
        {
            Race[] races = RecordManager.RaceManager.GetRaces(r => r.HasPilot(Pilot) && r.Type.HasLapCount()).ToArray();
            bestRaceTime = LapRecordManager.GetBestRaceTime(races, Pilot, lapCount).ToArray();
        }

        public void Clear()
        {
            bestRaceTime = new Lap[0];
            best.Clear();
        }

    }

    public class PilotPosition
    {
        public Pilot Pilot { get; private set; }

        private Dictionary<int, int> lapsToPosition;

        public PilotPosition(Pilot pilot)
        {
            this.Pilot = pilot;
            lapsToPosition = new Dictionary<int, int>();
        }

        public void AddPosition(int laps, int position)
        {
            if (lapsToPosition.ContainsKey(laps))
            {
                lapsToPosition[laps] = position;
            }
            else
            {
                lapsToPosition.Add(laps, position);
            }
        }

        public int GetPosition(int laps)
        {
            int position;
            if (lapsToPosition.TryGetValue(laps, out position)) 
            { 
                return position;
            }
            return -1;
        }

        public override string ToString()
        {
            return Pilot.ToString() + " " + string.Join(", ", lapsToPosition.Values.Select(v => v.ToString()));
        }
    }
}
