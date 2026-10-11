using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;
using Engine = global::Brackets;

namespace Webb.Brackets
{
    // Race brackets on top of FPVTrackside: the same engine as the AUFPV
    // platform (the Brackets project is a port of it, pinned by tests generated
    // from the original), stored in the event folder, with each bracket heat
    // run as a real FPVTrackside race.
    //
    // Pilots are identified to the engine by their FPVTrackside pilot ID.
    //
    // Everything here runs on the UI thread: the REST API hands its calls to it,
    // and the race events it listens to fire there.
    public class BracketService : IDisposable
    {
        private readonly EventManager eventManager;
        private readonly BracketStore store;

        private RaceManager RaceManager { get { return eventManager.RaceManager; } }

        public BracketStore Store { get { return store; } }

        // Raised after a bracket changes (a result recorded, a heat sent…), with its ID.
        public event Action<int> OnBracketChanged;

        public BracketService(EventManager eventManager, Func<string> eventDirectory)
        {
            this.eventManager = eventManager;
            store = new BracketStore(eventDirectory);

            eventManager.RaceManager.OnRaceEnd += OnRaceFinished;
            eventManager.ResultManager.RaceResultsChanged += OnRaceFinished;
        }

        public void Dispose()
        {
            eventManager.RaceManager.OnRaceEnd -= OnRaceFinished;
            eventManager.ResultManager.RaceResultsChanged -= OnRaceFinished;
        }

        // ───────────────────────── engine ─────────────────────────

        public static Engine.FormatConfig ToConfig(BracketRecord rec)
        {
            BracketSetup s = rec.Setup ?? new BracketSetup();
            Engine.FormatConfig cfg = new Engine.FormatConfig
            {
                PilotCount = rec.Pilots.Count,
                PilotNames = rec.Pilots.ToList(),
                EliminationType = s.EliminationType,
                PilotsPerRace = s.PilotsPerRace,
                PilotsToAdvance = s.PilotsToAdvance,
                FinalsFormat = s.FinalsFormat,
                AceWinsRequired = s.AceWinsRequired,
                Mains = s.Mains,
                Rounds = s.Rounds,
                Direction = s.Direction,
                PointsTable = s.PointsTable,
                DropWorst = s.DropWorst,
                MainsDivisions = s.Divisions,
                MainsRosters = s.MainsRosters,
                QualifierCTA = s.QualifierCta,
                QualifierPhotoSeed = s.QualifierSeed == "photo",
                PilotsPerMain = s.PilotsPerMain,
                BumpUp = s.BumpUp,
                CTAMains = s.CtaMains,
            };
            if (rec.Format == Engine.FormatKinds.Mains)
                ApplyMainsDefaults(cfg);
            return cfg;
        }

        // The engine needs every points-mains setting; the API promises that
        // anything left out has a default. These match the AUFPV platform's form.
        public static readonly int[] DefaultPoints = { 25, 18, 15, 12, 10, 8, 6, 4 };
        public const int PilotsPerDefaultMain = 8;

        private static void ApplyMainsDefaults(Engine.FormatConfig cfg)
        {
            if (cfg.Mains == null || cfg.Mains.Count == 0)
            {
                // One main per 8 pilots, labelled A (top seeds), B, C…
                int count = Math.Max(1, Math.Min(26, (cfg.PilotCount + PilotsPerDefaultMain - 1) / PilotsPerDefaultMain));
                cfg.Mains = Enumerable.Range(0, count).Select(i => ((char)('A' + i)).ToString()).ToList();
            }
            if (cfg.Rounds < 1)
                cfg.Rounds = 3;
            if (string.IsNullOrEmpty(cfg.Direction))
                cfg.Direction = "descending";
            if (cfg.PointsTable == null || cfg.PointsTable.Count == 0)
                cfg.PointsTable = DefaultPoints.Select((pts, i) => new { pts, i }).ToDictionary(x => x.i + 1, x => x.pts);
        }

        public Engine.IRaceFormat Format(BracketRecord rec)
        {
            Engine.FormatConfig cfg = ToConfig(rec);
            if (string.IsNullOrEmpty(rec.State))
                return Engine.RaceFormats.Create(rec.Format, cfg);
            return Engine.RaceFormats.FromState(rec.Format, cfg, rec.State);
        }

        private void Persist(BracketRecord rec, Engine.IRaceFormat format)
        {
            rec.State = format.StateJson();
            store.Save(rec);
            OnBracketChanged?.Invoke(rec.Id);
        }

        // ───────────────────────── create / edit ─────────────────────────

        public BracketRecord Create(string name, string format, BracketSetup setup, IList<Pilot> seededPilots, IList<string> seatChannels)
        {
            BracketRecord rec = new BracketRecord
            {
                Name = name,
                Format = string.IsNullOrEmpty(format) ? Engine.FormatKinds.Elimination : format,
                Setup = setup ?? new BracketSetup(),
                Pilots = seededPilots.Select(p => p.ID.ToString()).ToList(),
                PilotNames = seededPilots.ToDictionary(p => p.ID.ToString(), p => p.Name),
                SeatChannels = (seatChannels ?? new List<string>()).ToList(),
            };

            // Generating validates the setup; nothing is stored if the engine refuses it.
            Engine.IRaceFormat f = Engine.RaceFormats.Create(rec.Format, ToConfig(rec));
            rec.State = f.StateJson();
            store.Add(rec);
            Logger.HTTP.Log(this, "Bracket created: " + rec.Name + " (" + rec.Format + ", " + rec.Pilots.Count + " pilots)");
            OnBracketChanged?.Invoke(rec.Id);
            return rec;
        }

        public void Rename(BracketRecord rec, string name)
        {
            rec.Name = name;
            store.Save(rec);
            OnBracketChanged?.Invoke(rec.Id);
        }

        public void SetSeatChannels(BracketRecord rec, IList<string> seats)
        {
            rec.SeatChannels = seats.ToList();
            store.Save(rec);
            OnBracketChanged?.Invoke(rec.Id);
        }

        public void SetAutoResults(BracketRecord rec, bool on)
        {
            rec.AutoResults = on;
            store.Save(rec);
            OnBracketChanged?.Invoke(rec.Id);
        }

        public void Delete(BracketRecord rec)
        {
            store.Delete(rec.Id);
            OnBracketChanged?.Invoke(rec.Id);
        }

        // ───────────────────────── results ─────────────────────────

        public Engine.RaceView View(Engine.IRaceFormat f, string raceId)
        {
            Engine.RaceView rv = Views(f).FirstOrDefault(r => r.Id == raceId);
            if (rv == null)
                throw new Engine.BracketException("heat " + raceId + " is not in this bracket");
            return rv;
        }

        // A result entered by the race director. Locks the heat so a later
        // timing sync can't overwrite it (unlock to hand it back to timing).
        public void RecordResult(BracketRecord rec, string raceId, IList<string> placement, bool lockIt)
        {
            Engine.IRaceFormat f = Format(rec);
            Engine.RaceView rv = View(f, raceId);
            if (rv.IsComplete)
                f.EditRace(raceId, placement);
            else
                f.ApplyPlacement(raceId, placement);

            SetLock(rec, raceId, lockIt);
            Persist(rec, f);
        }

        public void ResetResult(BracketRecord rec, string raceId)
        {
            Engine.IRaceFormat f = Format(rec);
            View(f, raceId);
            f.ResetRace(raceId);
            SetLock(rec, raceId, false);
            Persist(rec, f);
        }

        public void Lock(BracketRecord rec, string raceId, bool locked)
        {
            View(Format(rec), raceId);
            SetLock(rec, raceId, locked);
            store.Save(rec);
            OnBracketChanged?.Invoke(rec.Id);
        }

        private static void SetLock(BracketRecord rec, string raceId, bool locked)
        {
            rec.Locked.Remove(raceId);
            if (locked)
                rec.Locked.Add(raceId);
        }

        // The finishing order FPVTrackside recorded for a race, as bracket pilot
        // IDs: its own results (DNFs last), then anyone in the heat it has no
        // result for. Null when the race hasn't finished.
        public List<string> TimingOrder(Race race, IEnumerable<string> heatPilots)
        {
            if (race == null || !race.Ended)
                return null;

            HashSet<string> inHeat = new HashSet<string>(heatPilots);
            List<string> order = eventManager.ResultManager.GetOrderedResults(race)
                .Where(r => r.Pilot != null)
                .Select(r => r.Pilot.ID.ToString())
                .Where(inHeat.Contains)
                .Distinct()
                .ToList();
            foreach (string p in heatPilots)
            {
                if (!order.Contains(p))
                    order.Add(p);
            }
            return order;
        }

        public enum TimingOutcome { NotLinked, NotFinished, Locked, Unchanged, Applied, Updated, Blocked, Failed }

        // Applies a linked FPVTrackside race's result to its bracket heat, the
        // same way the AUFPV platform applies a finished heat from timing: a
        // locked heat is left alone, an unchanged result is ignored, and a
        // changed one (a re-run) is only applied while no later heat that
        // received these pilots has been run.
        public TimingOutcome ApplyFromTiming(BracketRecord rec, string raceId, out string detail)
        {
            detail = null;
            Guid ftId;
            if (!rec.Links.TryGetValue(raceId, out ftId))
                return TimingOutcome.NotLinked;
            if (rec.Locked.Contains(raceId))
                return TimingOutcome.Locked;

            Race race = RaceManager.GetRaceByRaceId(ftId);
            if (race == null || !race.Valid || !race.Ended)
                return TimingOutcome.NotFinished;

            Engine.IRaceFormat f = Format(rec);
            Engine.RaceView rv = Views(f).FirstOrDefault(r => r.Id == raceId);
            if (rv == null || rv.IsVoid)
                return TimingOutcome.NotLinked;

            List<string> order = TimingOrder(race, rv.Pilots);
            if (order == null || order.Count == 0)
                return TimingOutcome.NotFinished;

            try
            {
                if (!rv.IsComplete)
                {
                    f.ApplyPlacement(raceId, order);
                    Persist(rec, f);
                    Logger.HTTP.Log(this, "Bracket " + rec.Name + " heat " + raceId + " recorded from timing");
                    return TimingOutcome.Applied;
                }

                // Elimination keeps the line-up in Pilots and the result in
                // Winners/Losers; other formats keep the finish order in Pilots.
                List<string> recorded = rv.Winners.Count > 0 || rv.Losers.Count > 0 ? rv.Winners.Concat(rv.Losers).ToList() : rv.Pilots.ToList();
                if (recorded.SequenceEqual(order))
                    return TimingOutcome.Unchanged;

                string reason;
                if (!f.CanEditRace(raceId, out reason))
                {
                    detail = "the re-run's result differs but " + reason + " — edit it on the bracket page if it needs changing";
                    Logger.HTTP.Log(this, "Bracket " + rec.Name + " heat " + raceId + " re-run NOT applied: " + reason);
                    return TimingOutcome.Blocked;
                }
                f.EditRace(raceId, order);
                Persist(rec, f);
                Logger.HTTP.Log(this, "Bracket " + rec.Name + " heat " + raceId + " updated from a re-run");
                return TimingOutcome.Updated;
            }
            catch (Engine.BracketException e)
            {
                detail = e.Message;
                Logger.HTTP.Log(this, "Bracket " + rec.Name + " heat " + raceId + ": " + e.Message);
                return TimingOutcome.Failed;
            }
        }

        // Pulls every linked, finished race's result into the bracket. Heats are
        // processed in order and repeated until nothing changes, because one
        // result can make the next heat's line-up (and so its result) resolvable.
        public Dictionary<string, TimingOutcome> Sync(BracketRecord rec)
        {
            Dictionary<string, TimingOutcome> outcome = new Dictionary<string, TimingOutcome>();
            for (int pass = 0; pass < 20; pass++)
            {
                bool changed = false;
                foreach (string raceId in rec.Links.Keys.ToList())
                {
                    string detail;
                    TimingOutcome o = ApplyFromTiming(rec, raceId, out detail);
                    outcome[raceId] = o;
                    changed |= o == TimingOutcome.Applied || o == TimingOutcome.Updated;
                }
                if (!changed)
                    break;
            }
            return outcome;
        }

        private void OnRaceFinished(Race race)
        {
            if (race == null || !race.Ended)
                return;
            try
            {
                foreach (BracketRecord rec in store.All())
                {
                    if (!rec.AutoResults)
                        continue;
                    foreach (KeyValuePair<string, Guid> link in rec.Links.Where(l => l.Value == race.ID).ToList())
                    {
                        string detail;
                        ApplyFromTiming(rec, link.Key, out detail);
                    }
                }
            }
            catch (Exception e)
            {
                // Never let a bracket problem break the race flow on screen.
                Logger.HTTP.LogException(this, e);
            }
        }

        // ───────────────────────── sending heats to FPVTrackside ─────────────────────────

        public Race LinkedRace(BracketRecord rec, string raceId)
        {
            Guid id;
            if (!rec.Links.TryGetValue(raceId, out id))
                return null;
            Race race = RaceManager.GetRaceByRaceId(id);
            return race != null && race.Valid ? race : null;
        }

        // The FPVTrackside round a bracket round's heats go in, created on first
        // use. Mains progression keeps each main's rounds apart.
        private Round RoundFor(BracketRecord rec, Engine.RaceView rv)
        {
            string main = rv.Id.Contains(":") ? rv.Id.Substring(0, rv.Id.IndexOf(':')) : "";
            string key = (main.Length > 0 ? main + ":" : "") + rv.Round;

            Guid id;
            if (rec.RoundLinks.TryGetValue(key, out id))
            {
                Round existing = eventManager.Event.Rounds.FirstOrDefault(r => r.ID == id && r.Valid);
                if (existing != null)
                    return existing;
            }

            Round round = eventManager.RoundManager.CreateEmptyRound(EventTypes.Race);
            eventManager.RoundManager.RenameRound(round, rec.Name + " — " + (main.Length > 0 ? main + " Main " : "") + "Round " + rv.Round);
            rec.RoundLinks[key] = round.ID;
            return round;
        }

        // The format's heats with empty lists as empty rather than null (the
        // engine mirrors Go, where an empty slice is nil). Use this, not
        // f.Races(), everywhere outside the engine.
        public static List<Engine.RaceView> Views(Engine.IRaceFormat f)
        {
            List<Engine.RaceView> views = (f.Races() ?? new List<Engine.RaceView>()).ToList();
            foreach (Engine.RaceView rv in views)
            {
                rv.Pilots = rv.Pilots ?? new List<string>();
                rv.Winners = rv.Winners ?? new List<string>();
                rv.Losers = rv.Losers ?? new List<string>();
            }
            return views;
        }

        // Heats that are ready to be flown: line-up known, not run, not void,
        // not waiting on another heat.
        public static bool Ready(Engine.RaceView rv)
        {
            return !rv.IsComplete && !rv.IsVoid && !rv.Locked && rv.Pilots.Count > 0;
        }

        // Creates (or refreshes the line-up of) the FPVTrackside race for a
        // bracket heat. A race that has already started is left alone.
        public Race Send(BracketRecord rec, string raceId)
        {
            Engine.IRaceFormat f = Format(rec);
            Engine.RaceView rv = View(f, raceId);
            if (rv.IsVoid)
                throw new Engine.BracketException(raceId + " isn't needed (the champion is already decided)");
            if (rv.Pilots.Count == 0)
                throw new Engine.BracketException(raceId + "'s line-up isn't known yet");

            List<Tuple<Pilot, Channel>> lineup = Lineup(rec, f, rv);

            Race race = LinkedRace(rec, raceId);
            if (race != null && race.Started)
                throw new Engine.BracketException(raceId + " has already been flown in FPVTrackside (" + race.RaceName + ")");

            if (race == null)
            {
                race = RaceManager.AddRaceToRound(RoundFor(rec, rv));
                rec.Links[raceId] = race.ID;
            }
            RaceManager.SetRacePilots(race, lineup, false);
            if (race == RaceManager.CurrentRace)
                RaceManager.SetRace(race);

            store.Save(rec);
            OnBracketChanged?.Invoke(rec.Id);
            Logger.HTTP.Log(this, "Bracket " + rec.Name + " heat " + raceId + " sent as " + race.RaceName);
            return race;
        }

        public Pilot PilotById(string id)
        {
            Guid g;
            if (!Guid.TryParse(id, out g))
                return null;
            Pilot p = eventManager.GetPilot(g);
            if (p != null)
                return p;
            using (IDatabase db = DatabaseFactory.Open(eventManager.EventId))
            {
                return db.All<Pilot>().FirstOrDefault(x => x.ID == g);
            }
        }

        public string PilotName(BracketRecord rec, string id)
        {
            Guid g;
            Pilot p = Guid.TryParse(id, out g) ? eventManager.GetPilot(g) : null;
            if (p != null)
                return p.Name;
            string name;
            return rec.PilotNames.TryGetValue(id, out name) ? name : id;
        }

        // Channels for a heat. A pilot keeps the channel they flew in their
        // previous heat of this bracket when it's free, so nobody re-tunes
        // between rounds; otherwise the seat channels are filled in order (or,
        // with none set, the pilot's usual channel, then any free one).
        public List<Tuple<Pilot, Channel>> Lineup(BracketRecord rec, Engine.IRaceFormat f, Engine.RaceView rv)
        {
            Channel[] eventChannels = eventManager.Channels ?? new Channel[0];
            List<Channel> seats = rec.SeatChannels
                .Select(s => Api.RestApi.ResolveChannel(s, eventChannels))
                .Where(c => c != null)
                .ToList();

            Dictionary<string, Channel> previous = PreviousChannels(rec, f, rv);
            List<Tuple<Pilot, Channel>> lineup = new List<Tuple<Pilot, Channel>>();
            Func<Channel, bool> taken = c => lineup.Any(x => x.Item2 == c || x.Item2.InterferesWith(c));

            List<Pilot> pilots = new List<Pilot>();
            foreach (string id in rv.Pilots)
            {
                Pilot p = PilotById(id);
                if (p == null)
                    throw new Engine.BracketException("pilot " + PilotName(rec, id) + " is no longer in FPVTrackside");
                if (eventManager.GetPilot(p.ID) == null)
                    eventManager.AddPilot(p);
                pilots.Add(p);
            }

            // Pass 1: keep last heat's channel.
            List<Pilot> unplaced = new List<Pilot>();
            foreach (Pilot p in pilots)
            {
                Channel c;
                if (previous.TryGetValue(p.ID.ToString(), out c) && (seats.Count == 0 || seats.Contains(c)) && !taken(c))
                    lineup.Add(Tuple.Create(p, c));
                else
                    unplaced.Add(p);
            }

            // Pass 2: seat channels in order, else usual channel, else any free one.
            foreach (Pilot p in unplaced)
            {
                Channel c = null;
                if (seats.Count > 0)
                {
                    c = seats.FirstOrDefault(s => !taken(s));
                }
                else
                {
                    Channel usual = eventManager.GetChannel(p);
                    c = usual != null && !taken(usual) ? usual : eventChannels.FirstOrDefault(x => !taken(x));
                }
                if (c == null)
                    throw new Engine.BracketException("not enough free channels for " + rv.Id + " (" + rv.Pilots.Count + " pilots)");
                lineup.Add(Tuple.Create(p, c));
            }

            // Seat order matches the bracket's line-up order.
            return pilots.Select(p => lineup.First(x => x.Item1 == p)).ToList();
        }

        // Each pilot's channel in the most recent earlier heat of this bracket
        // that was sent to FPVTrackside.
        private Dictionary<string, Channel> PreviousChannels(BracketRecord rec, Engine.IRaceFormat f, Engine.RaceView current)
        {
            Dictionary<string, Channel> result = new Dictionary<string, Channel>();
            List<Engine.RaceView> earlier = Views(f).Where(r => r.Id != current.Id && r.Round <= current.Round).OrderBy(r => r.Round).ToList();
            foreach (Engine.RaceView rv in earlier)
            {
                Race race = LinkedRace(rec, rv.Id);
                if (race == null)
                    continue;
                foreach (RacePilotChannel pc in race.PilotChannelsSafe)
                {
                    if (pc.Pilot != null && pc.Channel != null)
                        result[pc.Pilot.ID.ToString()] = pc.Channel;
                }
            }
            return result;
        }

        // ───────────────────────── seeding ─────────────────────────

        public class SeedRow
        {
            public Pilot Pilot;
            public TimeSpan? Best;
            public int Races;
        }

        // Qualifying order from the event's own results: each pilot's best
        // `laps` consecutive laps across the given rounds (default: every
        // time-trial round, else every finished race), fastest first; pilots
        // without a time follow in name order.
        public List<SeedRow> Seeding(IList<Round> rounds, int laps, IEnumerable<Pilot> pilots)
        {
            IEnumerable<Race> races = RaceManager.Races.Where(r => r.Ended);
            if (rounds != null && rounds.Count > 0)
            {
                races = races.Where(r => rounds.Contains(r.Round));
            }
            else
            {
                List<Race> tt = races.Where(r => r.Type == EventTypes.TimeTrial).ToList();
                if (tt.Any())
                    races = tt;
            }
            List<Race> list = races.ToList();
            if (laps < 1)
                laps = Math.Max(1, eventManager.Event.PBLaps);

            List<SeedRow> rows = new List<SeedRow>();
            foreach (Pilot p in pilots)
            {
                TimeSpan? best = null;
                int count = 0;
                foreach (Race r in list.Where(x => x.HasPilot(p)))
                {
                    count++;
                    TimeSpan t = r.GetBestLapsTime(p, laps);
                    if (t > TimeSpan.Zero && t != TimeSpan.MaxValue && (best == null || t < best))
                        best = t;
                }
                rows.Add(new SeedRow { Pilot = p, Best = best, Races = count });
            }
            return rows.OrderBy(r => r.Best.HasValue ? 0 : 1).ThenBy(r => r.Best ?? TimeSpan.MaxValue).ThenBy(r => r.Pilot.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
