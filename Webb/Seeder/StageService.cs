using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;
using Engine = global::SeederEngine;

namespace Webb.Seeder
{
    // Race stages on top of FPVTrackside: the same engine as the AUFPV
    // platform (the Stages project is a port of it, pinned by tests generated
    // from the original), stored in the event folder, with each stage race
    // run as a real FPVTrackside race.
    //
    // Pilots are identified to the engine by their FPVTrackside pilot ID.
    //
    // Everything here runs on the UI thread: the REST API hands its calls to it,
    // and the race events it listens to fire there.
    public class StageService : IDisposable
    {
        private readonly EventManager eventManager;
        private readonly StageStore store;

        private RaceManager RaceManager { get { return eventManager.RaceManager; } }

        public StageStore Store { get { return store; } }

        // Raised after a stage changes (a result recorded, a race sent…), with its ID.
        public event Action<int> OnStageChanged;

        // Runs work after the current FPVTrackside callback returns (next frame),
        // so auto-send never adds races from inside RaceLib's own race-end handling.
        private readonly Action<Action> defer;
        private readonly HashSet<int> autoSendQueued = new HashSet<int>();

        // Stage races whose timing result couldn't be applied (Blocked/Failed),
        // as "stageId:raceId". Cleared once the race is applied, entered by
        // hand, reset or locked.
        private readonly HashSet<string> problems = new HashSet<string>();

        private static string ProblemKey(StageRecord rec, string raceId)
        {
            return rec.Id + ":" + raceId;
        }

        public class StatusSummary
        {
            public int Stages;
            public int Problems;
            public int ReadyToSend;
            public int RacesDone;
            public int RacesTotal;
            public bool AllComplete;
        }

        // Every stage at a glance, for the status bar.
        public StatusSummary Summary()
        {
            StatusSummary summary = new StatusSummary();
            List<StageRecord> all = store.All();
            summary.Stages = all.Count;
            summary.Problems = problems.Count;
            summary.AllComplete = all.Count > 0;

            foreach (StageRecord rec in all)
            {
                Engine.IRaceFormat f = Format(rec);
                Dictionary<string, string> waiting = WaitingOn(f);
                foreach (Engine.RaceView rv in Views(f).Where(r => !r.IsVoid))
                {
                    summary.RacesTotal++;
                    if (rv.IsComplete)
                        summary.RacesDone++;
                    else if (Ready(rv, waiting) && LinkedRace(rec, rv.Id) == null)
                        summary.ReadyToSend++;
                }
                if (!f.IsComplete())
                    summary.AllComplete = false;
            }
            return summary;
        }

        public StageService(EventManager eventManager, Func<string> eventDirectory, Action<Action> defer = null)
        {
            this.eventManager = eventManager;
            this.defer = defer ?? (a => a());
            store = new StageStore(eventDirectory);

            eventManager.RaceManager.OnRaceEnd += OnRaceFinished;
            eventManager.ResultManager.RaceResultsChanged += OnRaceFinished;
        }

        public void Dispose()
        {
            eventManager.RaceManager.OnRaceEnd -= OnRaceFinished;
            eventManager.ResultManager.RaceResultsChanged -= OnRaceFinished;
        }

        // ───────────────────────── engine ─────────────────────────

        public static Engine.FormatConfig ToConfig(StageRecord rec)
        {
            StageSetup s = rec.Setup ?? new StageSetup();
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

        public Engine.IRaceFormat Format(StageRecord rec)
        {
            Engine.FormatConfig cfg = ToConfig(rec);
            if (string.IsNullOrEmpty(rec.State))
                return Engine.RaceFormats.Create(rec.Format, cfg);
            return Engine.RaceFormats.FromState(rec.Format, cfg, rec.State);
        }

        private void Persist(StageRecord rec, Engine.IRaceFormat format)
        {
            rec.State = format.StateJson();
            store.Save(rec);
            OnStageChanged?.Invoke(rec.Id);
            QueueAutoSend(rec);
        }

        // ───────────────────────── create / edit ─────────────────────────

        public StageRecord Create(string name, string format, StageSetup setup, IList<Pilot> seededPilots, IList<string> seatChannels)
        {
            StageRecord rec = new StageRecord
            {
                Name = name,
                Format = string.IsNullOrEmpty(format) ? Engine.FormatKinds.Elimination : format,
                Setup = setup ?? new StageSetup(),
                Pilots = seededPilots.Select(p => p.ID.ToString()).ToList(),
                PilotNames = seededPilots.ToDictionary(p => p.ID.ToString(), p => p.Name),
                SeatChannels = (seatChannels ?? new List<string>()).ToList(),
            };

            // Generating validates the setup; nothing is stored if the engine refuses it.
            Engine.IRaceFormat f = Engine.RaceFormats.Create(rec.Format, ToConfig(rec));
            rec.State = f.StateJson();
            store.Add(rec);
            Logger.HTTP.Log(this, "Stage created: " + rec.Name + " (" + rec.Format + ", " + rec.Pilots.Count + " pilots)");
            OnStageChanged?.Invoke(rec.Id);
            QueueAutoSend(rec);
            return rec;
        }

        public void Rename(StageRecord rec, string name)
        {
            rec.Name = name;
            store.Save(rec);
            Stage stage = eventManager.RoundManager.GetStages().FirstOrDefault(s => s.ID == rec.TracksideStageId);
            if (stage != null)
            {
                using (IDatabase db = DatabaseFactory.Open(eventManager.EventId))
                {
                    stage.Name = name;
                    db.Update(stage);
                }
            }
            OnStageChanged?.Invoke(rec.Id);
        }

        public void SetSeatChannels(StageRecord rec, IList<string> seats)
        {
            rec.SeatChannels = seats.ToList();
            store.Save(rec);
            OnStageChanged?.Invoke(rec.Id);
        }

        public void SetAutoResults(StageRecord rec, bool on)
        {
            rec.AutoResults = on;
            store.Save(rec);
            OnStageChanged?.Invoke(rec.Id);
        }

        public void SetAutoSend(StageRecord rec, bool on)
        {
            rec.AutoSend = on;
            store.Save(rec);
            OnStageChanged?.Invoke(rec.Id);
            QueueAutoSend(rec);
        }

        // Auto-send: after a change, create every newly ready race in FPVTrackside.
        private void QueueAutoSend(StageRecord rec)
        {
            if (!rec.AutoSend || !autoSendQueued.Add(rec.Id))
                return;
            int id = rec.Id;
            defer(() =>
            {
                autoSendQueued.Remove(id);
                try
                {
                    StageRecord current = store.Get(id);
                    if (current != null && current.AutoSend)
                    {
                        RemoveUnneeded(current);
                        Dictionary<string, string> problems;
                        List<Race> sent = SendReady(current, true, out problems);
                        foreach (KeyValuePair<string, string> p in problems)
                            Logger.HTTP.Log(this, "Stage " + current.Name + " auto-send " + p.Key + ": " + p.Value);
                        if (sent.Count > 0)
                            Logger.HTTP.Log(this, "Stage " + current.Name + " auto-sent " + sent.Count + " race(s)");
                    }
                }
                catch (Exception e)
                {
                    Logger.HTTP.LogException(this, e);
                }
            });
        }

        // Auto-send created races for races the stage then didn't need (e.g. the
        // rest of a chase-the-ace series once the champion is decided): take
        // them back out of FPVTrackside, as long as they haven't been flown.
        private void RemoveUnneeded(StageRecord rec)
        {
            bool changed = false;
            foreach (Engine.RaceView rv in Views(Format(rec)).Where(x => x.IsVoid))
            {
                Race race = LinkedRace(rec, rv.Id);
                if (race == null || race.Started || race == RaceManager.CurrentRace)
                    continue;
                RaceManager.RemoveRace(race, true);
                rec.Links.Remove(rv.Id);
                changed = true;
                Logger.HTTP.Log(this, "Stage " + rec.Name + " removed " + race.RaceName + " (" + rv.Id + " not needed)");
            }
            if (changed)
            {
                store.Save(rec);
                OnStageChanged?.Invoke(rec.Id);
            }
        }

        public void Delete(StageRecord rec)
        {
            store.Delete(rec.Id);
            problems.RemoveWhere(k => k.StartsWith(rec.Id + ":"));
            OnStageChanged?.Invoke(rec.Id);
        }

        // ───────────────────────── results ─────────────────────────

        public Engine.RaceView View(Engine.IRaceFormat f, string raceId)
        {
            Engine.RaceView rv = Views(f).FirstOrDefault(r => r.Id == raceId);
            if (rv == null)
                throw new Engine.BracketException("race " + raceId + " is not in this stage");
            return rv;
        }

        // A result entered by the race director. Locks the race so a later
        // timing sync can't overwrite it (unlock to hand it back to timing).
        public void RecordResult(StageRecord rec, string raceId, IList<string> placement, bool lockIt)
        {
            Engine.IRaceFormat f = Format(rec);
            Engine.RaceView rv = View(f, raceId);
            if (rv.IsComplete)
                f.EditRace(raceId, placement);
            else
                f.ApplyPlacement(raceId, placement);

            SetLock(rec, raceId, lockIt);
            problems.Remove(ProblemKey(rec, raceId));
            Persist(rec, f);
        }

        public void ResetResult(StageRecord rec, string raceId)
        {
            Engine.IRaceFormat f = Format(rec);
            View(f, raceId);
            f.ResetRace(raceId);
            SetLock(rec, raceId, false);
            problems.Remove(ProblemKey(rec, raceId));
            Persist(rec, f);
        }

        public void Lock(StageRecord rec, string raceId, bool locked)
        {
            View(Format(rec), raceId);
            SetLock(rec, raceId, locked);
            if (locked)
                problems.Remove(ProblemKey(rec, raceId));
            store.Save(rec);
            OnStageChanged?.Invoke(rec.Id);
        }

        private static void SetLock(StageRecord rec, string raceId, bool locked)
        {
            rec.Locked.Remove(raceId);
            if (locked)
                rec.Locked.Add(raceId);
        }

        // The finishing order FPVTrackside recorded for a race, as stage pilot
        // IDs: its own results (DNFs last), then anyone in the race it has no
        // result for. Null when the race hasn't finished.
        public List<string> TimingOrder(Race race, IEnumerable<string> stageRacePilots)
        {
            if (race == null || !race.Ended)
                return null;

            HashSet<string> inStageRace = new HashSet<string>(stageRacePilots);
            List<string> order = eventManager.ResultManager.GetOrderedResults(race)
                .Where(r => r.Pilot != null)
                .Select(r => r.Pilot.ID.ToString())
                .Where(inStageRace.Contains)
                .Distinct()
                .ToList();
            foreach (string p in stageRacePilots)
            {
                if (!order.Contains(p))
                    order.Add(p);
            }
            return order;
        }

        public enum TimingOutcome { NotLinked, NotFinished, Locked, Unchanged, Applied, Updated, Blocked, Failed }

        // Applies a linked FPVTrackside race's result to its stage race, the
        // same way the AUFPV platform applies a finished race from timing: a
        // locked race is left alone, an unchanged result is ignored, and a
        // changed one (a re-run) is only applied while no later race that
        // received these pilots has been run.
        public TimingOutcome ApplyFromTiming(StageRecord rec, string raceId, out string detail)
        {
            TimingOutcome outcome = ApplyFromTimingCore(rec, raceId, out detail);
            switch (outcome)
            {
                case TimingOutcome.Blocked:
                case TimingOutcome.Failed:
                    problems.Add(ProblemKey(rec, raceId));
                    break;
                case TimingOutcome.Applied:
                case TimingOutcome.Updated:
                case TimingOutcome.Unchanged:
                case TimingOutcome.Locked:
                case TimingOutcome.NotLinked:
                    problems.Remove(ProblemKey(rec, raceId));
                    break;
            }
            return outcome;
        }

        private TimingOutcome ApplyFromTimingCore(StageRecord rec, string raceId, out string detail)
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
                    Logger.HTTP.Log(this, "Stage " + rec.Name + " race " + raceId + " recorded from timing");
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
                    detail = "the re-run's result differs but " + reason + " — edit it on the stage page if it needs changing";
                    Logger.HTTP.Log(this, "Stage " + rec.Name + " race " + raceId + " re-run NOT applied: " + reason);
                    return TimingOutcome.Blocked;
                }
                f.EditRace(raceId, order);
                Persist(rec, f);
                Logger.HTTP.Log(this, "Stage " + rec.Name + " race " + raceId + " updated from a re-run");
                return TimingOutcome.Updated;
            }
            catch (Engine.BracketException e)
            {
                detail = Terms.Trackside(e.Message);
                Logger.HTTP.Log(this, "Stage " + rec.Name + " race " + raceId + ": " + e.Message);
                return TimingOutcome.Failed;
            }
        }

        // Pulls every linked, finished race's result into the stage. Races are
        // processed in order and repeated until nothing changes, because one
        // result can make the next race's line-up (and so its result) resolvable.
        public Dictionary<string, TimingOutcome> Sync(StageRecord rec)
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
                foreach (StageRecord rec in store.All())
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
                // Never let a stage problem break the race flow on screen.
                Logger.HTTP.LogException(this, e);
            }
        }

        // ───────────────────────── sending races to FPVTrackside ─────────────────────────

        public Race LinkedRace(StageRecord rec, string raceId)
        {
            Guid id;
            if (!rec.Links.TryGetValue(raceId, out id))
                return null;
            Race race = RaceManager.GetRaceByRaceId(id);
            return race != null && race.Valid ? race : null;
        }

        // The FPVTrackside round a stage round's races go in, created on first
        // use. Bracket progression keeps each bracket's rounds apart.
        private Round RoundFor(StageRecord rec, Engine.IRaceFormat f, Engine.RaceView rv)
        {
            string key = RoundKey(rv);
            string name = RoundName(f, key);

            Guid id;
            if (rec.RoundLinks.TryGetValue(key, out id))
            {
                Round existing = eventManager.Event.Rounds.FirstOrDefault(r => r.ID == id && r.Valid);
                if (existing != null)
                {
                    if (existing.Name != name)
                        eventManager.RoundManager.RenameRound(existing, name);
                    return existing;
                }
            }

            Round round = EmptyLastRound() ?? eventManager.RoundManager.CreateEmptyRound(EventTypes.Race);
            eventManager.RoundManager.RenameRound(round, name);
            rec.RoundLinks[key] = round.ID;
            PutInTracksideStage(rec, round);
            return round;
        }

        // The event's last round, if it's empty and not part of a stage: typically the round Seeder was opened
        // from, so use it rather than adding another after it. Made a Race round if it wasn't one.
        private Round EmptyLastRound()
        {
            RoundManager rm = eventManager.RoundManager;
            Round last = rm.GetLastRound();
            if (last == null || last.Stage != null || !rm.IsEmpty(last))
                return null;

            if (last.EventType != EventTypes.Race)
            {
                last.EventType = EventTypes.Race;
                using (IDatabase db = DatabaseFactory.Open(eventManager.EventId))
                {
                    db.Update(last);
                }
            }
            return last;
        }

        private static string RoundKey(Engine.RaceView rv)
        {
            string main = rv.Id.Contains(":") ? rv.Id.Substring(0, rv.Id.IndexOf(':')) : "";
            return (main.Length > 0 ? main + ":" : "") + rv.Round;
        }

        private static readonly System.Text.RegularExpressions.Regex raceSuffix =
            new System.Text.RegularExpressions.Regex(@"\s+—\s+Race \d+$");

        // A round named after the stage rounds in it, e.g. "Winners Round 2 ·
        // Losers Round 2" or "Chase the Ace", rather than a bare "Round 2".
        public static string RoundName(Engine.IRaceFormat f, string key)
        {
            List<string> parts = new List<string>();
            foreach (Engine.RaceView v in Views(f).Where(x => RoundKey(x) == key))
            {
                string title = raceSuffix.Replace(Terms.Trackside(v.Title ?? ""), "");
                if (title.Length > 0 && !parts.Contains(title))
                    parts.Add(title);
            }
            if (parts.Count == 0)
                return "Round " + key;

            // Bracket progression titles all start "Bracket A · "; say it once.
            string prefix = "";
            int dot = parts[0].IndexOf(" · ");
            if (dot > 0 && parts.All(p => p.StartsWith(parts[0].Substring(0, dot + 3))))
            {
                prefix = parts[0].Substring(0, dot + 3);
                parts = parts.Select(p => p.Substring(prefix.Length)).ToList();
            }
            return prefix + string.Join(" · ", parts);
        }

        // Groups the stage's rounds under an FPVTrackside stage of the same
        // name, so the Rounds view shows them together. Its type stays Default:
        // the other types make FPVTrackside generate rounds itself.
        private void PutInTracksideStage(StageRecord rec, Round round)
        {
            RoundManager rm = eventManager.RoundManager;
            Stage stage = rm.GetStages().FirstOrDefault(s => s.ID == rec.TracksideStageId);
            if (stage != null)
            {
                rm.SetStage(round, stage);
                return;
            }

            using (IDatabase db = DatabaseFactory.Open(eventManager.EventId))
            {
                stage = rm.CreateStage(db, round, false);
                stage.Name = rec.Name;
                stage.Order = round.Order;
                db.Update(stage);
            }
            rec.TracksideStageId = stage.ID;
        }

        // The FPVTrackside race bracket for a stage race: Winners or Losers in
        // the elimination formats, the bracket letter (A, B, …) for the mains.
        public static Brackets TracksideBracket(Engine.RaceView rv)
        {
            string id = rv.Id;
            int colon = id.IndexOf(':');
            if (colon > 0)
                return Letter(id.Substring(0, colon));

            string group = rv.GroupLabel ?? "";
            if (group.StartsWith("Winners", StringComparison.OrdinalIgnoreCase))
                return Brackets.Winners;
            if (group.StartsWith("Redemption", StringComparison.OrdinalIgnoreCase) || group.StartsWith("Losers", StringComparison.OrdinalIgnoreCase))
                return Brackets.Losers;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(group, @"^([A-Z]) Main\b");
            if (m.Success)
                return Letter(m.Groups[1].Value);
            return Brackets.None;
        }

        private static Brackets Letter(string label)
        {
            if (label.Length == 1 && label[0] >= 'A' && label[0] <= 'Z')
                return Brackets.A + (label[0] - 'A');
            return Brackets.None;
        }

        // The format's races with empty lists as empty rather than null (the
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

        // Races whose line-up still has seats another race decides, with which
        // races. Elimination keeps a race's known pilots while it waits (W2-1
        // holds W1-1's winners until W1-2 is run), so "has pilots" is not
        // "ready": this reads the same projection the diagram draws, where an
        // undecided seat is a "from race X" placeholder. Ladder bouts lock
        // themselves and points mains know every line-up from the start.
        public static Dictionary<string, string> WaitingOn(Engine.IRaceFormat f)
        {
            Dictionary<string, string> waiting = new Dictionary<string, string>();
            Action<string, Engine.BracketProjection> add = (prefix, p) =>
            {
                if (p == null || p.PredictedSlots == null)
                    return;
                foreach (KeyValuePair<string, Dictionary<string, Engine.PilotSlot>> race in p.PredictedSlots)
                {
                    if (race.Value == null || race.Value.Count == 0)
                        continue;
                    List<string> from = race.Value.Values
                        .Select(slot => slot.SourceRaceId)
                        .Where(x => !string.IsNullOrEmpty(x))
                        .Distinct().OrderBy(x => x).ToList();
                    waiting[prefix + race.Key] = from.Count > 0 ? string.Join(", ", from) : "earlier races";
                }
            };
            try
            {
                Engine.EliminationFormat elim = f as Engine.EliminationFormat;
                Engine.QualifierFormat qual = f as Engine.QualifierFormat;
                Engine.MainsProgressionFormat mains = f as Engine.MainsProgressionFormat;
                if (elim != null)
                    add("", Engine.BracketDiagram.Elimination(elim.Bracket()));
                else if (qual != null)
                    add("", Engine.BracketDiagram.Qualifier(qual.Qual()));
                else if (mains != null)
                    foreach (var m in Engine.BracketDiagram.MainsProgression(mains.Mains()))
                        add(m.Label + ":", m.P);
            }
            catch (Engine.BracketException)
            {
                // No projection: fall back to the engine's own Locked flags.
            }
            return waiting;
        }

        // Races that are ready to be flown: whole line-up known, not run, not
        // void, not waiting on another race.
        public static bool Ready(Engine.RaceView rv, Dictionary<string, string> waiting)
        {
            return !rv.IsComplete && !rv.IsVoid && !rv.Locked && rv.Pilots.Count > 0 && !waiting.ContainsKey(rv.Id);
        }

        // Sends every ready race. onlyNew: just races not yet in FPVTrackside
        // (auto-send — never re-seats a race the race director may have edited);
        // otherwise unstarted linked races are refreshed too.
        public List<Race> SendReady(StageRecord rec, bool onlyNew, out Dictionary<string, string> problems)
        {
            problems = new Dictionary<string, string>();
            List<Race> sent = new List<Race>();
            Engine.IRaceFormat f = Format(rec);
            Dictionary<string, string> waiting = WaitingOn(f);
            foreach (Engine.RaceView rv in Views(f).Where(x => Ready(x, waiting)))
            {
                Race existing = LinkedRace(rec, rv.Id);
                if (existing != null && (onlyNew || existing.Started))
                    continue;
                try
                {
                    sent.Add(Send(rec, rv.Id));
                }
                catch (Engine.BracketException e)
                {
                    problems[rv.Id] = Terms.Trackside(e.Message);
                }
            }
            return sent;
        }

        // Creates (or refreshes the line-up of) the FPVTrackside race for a
        // stage race. A race that has already started is left alone.
        public Race Send(StageRecord rec, string raceId)
        {
            Engine.IRaceFormat f = Format(rec);
            Engine.RaceView rv = View(f, raceId);
            if (rv.IsVoid)
                throw new Engine.BracketException(raceId + " isn't needed (the champion is already decided)");
            if (rv.Pilots.Count == 0)
                throw new Engine.BracketException(raceId + "'s line-up isn't known yet");
            string waitingOn;
            if (WaitingOn(f).TryGetValue(raceId, out waitingOn))
                throw new Engine.BracketException(raceId + " is still waiting on " + waitingOn + " for the rest of its line-up");

            List<Tuple<Pilot, Channel>> lineup = Lineup(rec, f, rv);

            Race race = LinkedRace(rec, raceId);
            if (race != null && race.Started)
                throw new Engine.BracketException(raceId + " has already been flown in FPVTrackside (" + race.RaceName + ")");

            if (race == null)
            {
                race = RaceManager.AddRaceToRound(RoundFor(rec, f, rv));
                rec.Links[raceId] = race.ID;
                using (IDatabase db = DatabaseFactory.Open(eventManager.EventId))
                {
                    race.Bracket = TracksideBracket(rv);
                    db.Update(race);
                }
            }
            RaceManager.SetRacePilots(race, lineup, false);
            if (race == RaceManager.CurrentRace)
                RaceManager.SetRace(race);

            store.Save(rec);
            OnStageChanged?.Invoke(rec.Id);
            Logger.HTTP.Log(this, "Stage " + rec.Name + " race " + raceId + " sent as " + race.RaceName);
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

        public string PilotName(StageRecord rec, string id)
        {
            Guid g;
            Pilot p = Guid.TryParse(id, out g) ? eventManager.GetPilot(g) : null;
            if (p != null)
                return p.Name;
            string name;
            return rec.PilotNames.TryGetValue(id, out name) ? name : id;
        }

        // Channels for a race. A pilot keeps the channel they flew in their
        // previous race of this stage when it's free, so nobody re-tunes
        // between rounds; otherwise the seat channels are filled in order (or,
        // with none set, the pilot's usual channel, then any free one).
        public List<Tuple<Pilot, Channel>> Lineup(StageRecord rec, Engine.IRaceFormat f, Engine.RaceView rv)
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

            // Pass 1: keep last race's channel.
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

            // Seat order matches the stage's line-up order.
            return pilots.Select(p => lineup.First(x => x.Item1 == p)).ToList();
        }

        // Each pilot's channel in the most recent earlier race of this stage
        // that was sent to FPVTrackside.
        private Dictionary<string, Channel> PreviousChannels(StageRecord rec, Engine.IRaceFormat f, Engine.RaceView current)
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
