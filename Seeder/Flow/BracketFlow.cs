using System;
using System.Collections.Generic;

namespace SeederEngine
{
    // Port of the diagram flow logic from the AUFPV platform's race-bracket handlers
    // (race_bracket_preview.go: pilotSlot, computeSlotChart, bracketConnector,
    // simulateBracketFlow, buildConnectorsFromState, nthPlaceFinisher; and
    // race_bracket_handlers.go: predictedSlot, buildPredictedSlots). Only the pure
    // logic is ported — not the SVG rendering or HTTP handlers.

    /// <summary>
    /// BracketConnector records that pilots flowed from one race to another during
    /// a simulated play-through. Multiple pilots between the same pair are coalesced.
    /// </summary>
    public class BracketConnector
    {
        public string FromId { get; set; } = "";
        public string ToId { get; set; } = "";
        public List<string> Pilots { get; set; }
    }

    /// <summary>
    /// PilotSlot records where one pilot came from for the structural chart view.
    /// SourceRaceId is empty for W1 pilots (they enter via qualifying, no source).
    /// </summary>
    public class PilotSlot
    {
        public string SourceRaceId { get; set; } = "";
        public int SourceRank { get; set; } // 1-indexed position they took in the source race
    }

    /// <summary>
    /// PredictedSlot is one predicted incoming pilot for a race — its source race
    /// (empty for W1 pilots, who enter via qualifying), the finish rank in that
    /// source (1-indexed), and a display label.
    /// </summary>
    public class PredictedSlot
    {
        public string SourceRaceId { get; set; } = "";
        public int SourceRank { get; set; }
        public string Label { get; set; } = "";
    }

    public static class BracketFlow
    {
        /// <summary>
        /// SeedRanks returns a function giving each pilot's seed rank — their index
        /// in the original seeding (lower = better seed). Unknown names rank last so they
        /// never jump ahead of a real seed. (Go: bracketSeedRanks.)
        /// </summary>
        public static Func<string, int> SeedRanks(Bracket b)
        {
            Dictionary<string, int> rank = new Dictionary<string, int>();
            List<string> names = b.Config().PilotNames;
            for (int i = 0; i < Go.Len(names); i++)
            {
                rank[names[i]] = i;
            }
            return name =>
            {
                int r;
                if (name != null && rank.TryGetValue(name, out r))
                {
                    return r;
                }
                return 1 << 30;
            };
        }

        /// <summary>
        /// SlotChart turns the simulation's connector list into a per-race
        /// pilot → source mapping so the chart renderer can label each row with
        /// "from W1-3 · 2nd" instead of the seed number. (Go: computeSlotChart.)
        /// </summary>
        public static Dictionary<string, Dictionary<string, PilotSlot>> SlotChart(Bracket b, List<BracketConnector> connectors)
        {
            Dictionary<string, Dictionary<string, PilotSlot>> output = new Dictionary<string, Dictionary<string, PilotSlot>>();
            Func<string, int> seedRank = SeedRanks(b);
            // Index source race ID → race for rank lookup.
            Dictionary<string, Race> raceByID = new Dictionary<string, Race>();
            foreach (Race r in Go.Iter(b.State().Races))
            {
                raceByID[r.Id] = r;
            }
            foreach (BracketConnector c in Go.Iter(connectors))
            {
                Race src;
                if (!raceByID.TryGetValue(c.FromId, out src) || src == null)
                {
                    continue;
                }
                // Skip CTA → CTA edges. Subsequent CTA races re-run the same 4
                // finalists, so the chart should display each CTA race with the
                // original feeders (W-final, L-final) — not "CTA-1 · 1st".
                Race dst;
                raceByID.TryGetValue(c.ToId, out dst);
                if (dst != null && src.Bracket == BracketSlot.ChaseTheAce && dst.Bracket == BracketSlot.ChaseTheAce)
                {
                    continue;
                }
                // Rank from the recorded finishing order. SelectWinners (the preview's
                // play-through) does NOT reorder src.Pilots, so the placement lives in
                // Winners (1st..Nth) then Losers; only a FullPlacement run reorders
                // src.Pilots itself. Ranking off src.Pilots order would mislabel a
                // best-seed winner that wasn't listed first in arrival order.
                Dictionary<string, int> rankOf = new Dictionary<string, int>();
                if (src.IsComplete && src.FullPlacement)
                {
                    for (int i = 0; i < Go.Len(src.Pilots); i++)
                    {
                        rankOf[src.Pilots[i]] = i + 1;
                    }
                }
                else
                {
                    for (int i = 0; i < Go.Len(src.Winners); i++)
                    {
                        rankOf[src.Winners[i]] = i + 1;
                    }
                    // SelectWinners doesn't record a finishing order for the losers, so
                    // rank them by seed — matching the best-seed-wins full placement the
                    // live runner records (where everyone finishes in seed order).
                    List<string> losers = new List<string>(Go.Iter(src.Losers));
                    Go.SortStable(losers, (x, y) => seedRank(x) < seedRank(y));
                    int off = Go.Len(src.Winners);
                    for (int i = 0; i < losers.Count; i++)
                    {
                        rankOf[losers[i]] = off + i + 1;
                    }
                }
                Dictionary<string, PilotSlot> slots;
                if (!output.TryGetValue(c.ToId, out slots))
                {
                    slots = new Dictionary<string, PilotSlot>();
                    output[c.ToId] = slots;
                }
                foreach (string pilot in Go.Iter(c.Pilots))
                {
                    int rk;
                    rankOf.TryGetValue(pilot, out rk);
                    slots[pilot] = new PilotSlot { SourceRaceId = c.FromId, SourceRank = rk };
                }
            }
            // Mirror CTA-1's slot labels onto every subsequent CTA race so all
            // chase-the-ace heats display the same source attribution.
            string firstCTA = "";
            foreach (Race r in Go.Iter(b.State().Races))
            {
                if (r.Bracket == BracketSlot.ChaseTheAce)
                {
                    firstCTA = r.Id;
                    break;
                }
            }
            if (firstCTA != "")
            {
                Dictionary<string, PilotSlot> baseSlots;
                output.TryGetValue(firstCTA, out baseSlots);
                foreach (Race r in Go.Iter(b.State().Races))
                {
                    if (r.Bracket != BracketSlot.ChaseTheAce || r.Id == firstCTA)
                    {
                        continue;
                    }
                    Dictionary<string, PilotSlot> slots;
                    if (!output.TryGetValue(r.Id, out slots))
                    {
                        slots = new Dictionary<string, PilotSlot>();
                        output[r.Id] = slots;
                    }
                    foreach (string p in Go.Iter(r.Pilots))
                    {
                        if (slots.ContainsKey(p))
                        {
                            continue;
                        }
                        PilotSlot s;
                        if (baseSlots != null && baseSlots.TryGetValue(p, out s))
                        {
                            slots[p] = s;
                        }
                    }
                }
            }
            return output;
        }

        /// <summary>
        /// NthPlaceFinisher returns the pilot who finished at `rank` (1-based) in a
        /// completed race, using the same ranking SlotChart does: a FullPlacement
        /// result reorders race.Pilots to the finish, so index it directly; a
        /// SelectWinners/ingest result records only Winners (1..N) and leaves the losers
        /// in entry order, so rank those by seed to match the live runner's
        /// best-seed-wins placement. Returns "" when the rank can't be resolved.
        /// </summary>
        public static string NthPlaceFinisher(Race src, int rank, Func<string, int> seedRank)
        {
            if (src == null || rank < 1)
            {
                return "";
            }
            if (src.FullPlacement)
            {
                if (rank - 1 < Go.Len(src.Pilots))
                {
                    return src.Pilots[rank - 1];
                }
                return "";
            }
            if (rank - 1 < Go.Len(src.Winners))
            {
                return src.Winners[rank - 1];
            }
            List<string> losers = new List<string>(Go.Iter(src.Losers));
            if (seedRank != null)
            {
                Go.SortStable(losers, (x, y) => seedRank(x) < seedRank(y));
            }
            int li = rank - 1 - Go.Len(src.Winners);
            if (li >= 0 && li < losers.Count)
            {
                return losers[li];
            }
            return "";
        }

        /// <summary>
        /// Simulate plays a generated bracket to completion with the best-seeded
        /// pilots in each race always winning. It records every (sourceRace, targetRace)
        /// edge so the preview renderer can draw connector lines showing pilot progression.
        /// NB: it plays `b` itself (mutating it), exactly as the Go code does — pass a
        /// fresh bracket. (Go: simulateBracketFlow.)
        /// </summary>
        public static List<BracketConnector> Simulate(Bracket b)
        {
            Dictionary<(string, string), BracketConnector> edges = new Dictionary<(string, string), BracketConnector>();

            // Seed rank: a pilot's index in the original seeding (lower = better seed).
            // Used to make the best-seeded pilots win every heat, so the preview always
            // shows the top seed winning their heat and progressing to champion (rather
            // than whoever happens to be listed first in arrival order).
            Func<string, int> seedRank = SeedRanks(b);

            const int safety = 4000;
            for (int iter = 0; iter < safety; iter++)
            {
                // Pick the lowest-round incomplete race with pilots assigned.
                Race pick = null;
                foreach (Race race in Go.Iter(b.State().Races))
                {
                    if (race.IsComplete || Go.Len(race.Pilots) == 0)
                    {
                        continue;
                    }
                    if (pick == null || race.Round < pick.Round ||
                        (race.Round == pick.Round && string.CompareOrdinal(RaceSortKey(race.Id), RaceSortKey(pick.Id)) < 0))
                    {
                        pick = race;
                    }
                }
                if (pick == null)
                {
                    break;
                }
                int expected = b.ExpectedWinnersCount(pick);
                if (expected > pick.Pilots.Count)
                {
                    expected = pick.Pilots.Count;
                }
                // Winners = the `expected` best-seeded pilots in this heat, ordered
                // best-first (so winners[0] = 1st place is the top seed present).
                List<string> ordered = new List<string>(pick.Pilots);
                Go.SortStable(ordered, (x, y) => seedRank(x) < seedRank(y));
                List<string> winners = ordered.GetRange(0, expected);

                // Pilots that belong to `pick` — only these can legitimately be flowing
                // out of pick when we SelectWinners. A cascade (auto-advance into a bye,
                // downstream race completing) might also add pilots to other races during
                // this call; we must NOT credit those to pick.
                HashSet<string> ownPilots = new HashSet<string>(pick.Pilots);

                // Snapshot which pilots every other race held just before SelectWinners,
                // so we can attribute newly-arrived pilots to this race after.
                Dictionary<string, HashSet<string>> before = new Dictionary<string, HashSet<string>>();
                foreach (Race race in Go.Iter(b.State().Races))
                {
                    if (race.Id == pick.Id)
                    {
                        continue;
                    }
                    before[race.Id] = new HashSet<string>(Go.Iter(race.Pilots));
                }

                try
                {
                    b.SelectWinners(pick.Id, winners);
                }
                catch (BracketException)
                {
                    break;
                }

                foreach (Race race in Go.Iter(b.State().Races))
                {
                    if (race.Id == pick.Id)
                    {
                        continue;
                    }
                    HashSet<string> seen;
                    before.TryGetValue(race.Id, out seen);
                    foreach (string p in Go.Iter(race.Pilots))
                    {
                        if (seen != null && seen.Contains(p))
                        {
                            continue;
                        }
                        if (!ownPilots.Contains(p))
                        {
                            continue; // arrived via a cascading auto-advance, not from pick
                        }
                        (string, string) key = (pick.Id, race.Id);
                        BracketConnector e;
                        if (!edges.TryGetValue(key, out e))
                        {
                            e = new BracketConnector { FromId = pick.Id, ToId = race.Id };
                            edges[key] = e;
                        }
                        e.Pilots = Go.Append(e.Pilots, p);
                    }
                }
            }

            // Backfill connectors for pilots that the cascade-filter rejected.
            // PopulateChaseTheAceFinalists bulk-copies pilots into the CTA-1 slots
            // during whichever side-completion triggers it (typically the L-side,
            // because W usually finishes first). Those pilots have no edge from
            // pick → CTA-1 since they didn't belong to pick. For each such pilot,
            // credit them to the most-recent completed race with a strictly-lower
            // round that had them as a winner. Constraining to lower-round sources
            // prevents nonsense like "W1-1 sourced from CTA-1" (CTA-1 also has
            // seed 1 listed as a winner once the bracket completes).
            BracketState finalState = b.State();
            Dictionary<string, HashSet<string>> covered = new Dictionary<string, HashSet<string>>();
            foreach (BracketConnector e in edges.Values)
            {
                HashSet<string> set;
                if (!covered.TryGetValue(e.ToId, out set))
                {
                    set = new HashSet<string>();
                    covered[e.ToId] = set;
                }
                foreach (string p in Go.Iter(e.Pilots))
                {
                    set.Add(p);
                }
            }
            foreach (Race race in Go.Iter(finalState.Races))
            {
                foreach (string p in Go.Iter(race.Pilots))
                {
                    HashSet<string> cov;
                    if (covered.TryGetValue(race.Id, out cov) && cov.Contains(p))
                    {
                        continue;
                    }
                    // Find the highest-round completed source race below this one
                    // that listed `p` as a winner.
                    Race src = null;
                    foreach (Race cand in Go.Iter(finalState.Races))
                    {
                        if (!cand.IsComplete || cand.Id == race.Id || cand.Round >= race.Round)
                        {
                            continue;
                        }
                        bool found = false;
                        foreach (string w in Go.Iter(cand.Winners))
                        {
                            if (w == p)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            continue;
                        }
                        if (src == null || cand.Round > src.Round)
                        {
                            src = cand;
                        }
                    }
                    if (src == null)
                    {
                        continue;
                    }
                    (string, string) key = (src.Id, race.Id);
                    BracketConnector e;
                    if (!edges.TryGetValue(key, out e))
                    {
                        e = new BracketConnector { FromId = src.Id, ToId = race.Id };
                        edges[key] = e;
                    }
                    e.Pilots = Go.Append(e.Pilots, p);
                    if (!covered.TryGetValue(race.Id, out cov))
                    {
                        cov = new HashSet<string>();
                        covered[race.Id] = cov;
                    }
                    cov.Add(p);
                }
            }

            return SortConnectors(edges.Values);
        }

        private static List<BracketConnector> SortConnectors(IEnumerable<BracketConnector> edges)
        {
            List<BracketConnector> output = new List<BracketConnector>();
            foreach (BracketConnector e in edges)
            {
                output.Add(new BracketConnector { FromId = e.FromId, ToId = e.ToId, Pilots = e.Pilots });
            }
            Go.SortStable(output, (x, y) =>
            {
                if (x.FromId != y.FromId)
                {
                    return string.CompareOrdinal(x.FromId, y.FromId) < 0;
                }
                return string.CompareOrdinal(x.ToId, y.ToId) < 0;
            });
            return output;
        }

        /// <summary>
        /// ConnectorsFromState infers source→target edges for the renderer by reading
        /// the bracket's actual persisted state — used by the public live view, where no
        /// simulation has happened but the bracket has real progress recorded.
        ///
        /// For each pilot in each race, finds the most-recent prior race they appeared
        /// in (any role: winner, loser, or still-incomplete) and emits that as the
        /// source. Captures both winners-bracket → winners-bracket flow and losers
        /// drops from the winners side into the losers side. (Go: buildConnectorsFromState.)
        /// </summary>
        public static List<BracketConnector> ConnectorsFromState(Bracket b)
        {
            BracketState state = b.State();
            Dictionary<(string, string), BracketConnector> edges = new Dictionary<(string, string), BracketConnector>();

            // Group races by round and build pilot → list-of-(race, round) appearances.
            Dictionary<string, List<(string raceId, int round)>> pilotRaces = new Dictionary<string, List<(string, int)>>();
            foreach (Race r in Go.Iter(state.Races))
            {
                foreach (string p in Go.Iter(r.Pilots))
                {
                    List<(string, int)> list;
                    if (!pilotRaces.TryGetValue(p, out list))
                    {
                        list = new List<(string, int)>();
                        pilotRaces[p] = list;
                    }
                    list.Add((r.Id, r.Round));
                }
            }

            foreach (Race target in Go.Iter(state.Races))
            {
                foreach (string p in Go.Iter(target.Pilots))
                {
                    // Find the latest prior race (round < target.Round) where this pilot appeared.
                    string src = "";
                    int bestRound = -1;
                    foreach ((string raceId, int round) app in pilotRaces[p])
                    {
                        if (app.round >= target.Round)
                        {
                            continue;
                        }
                        if (app.round > bestRound)
                        {
                            bestRound = app.round;
                            src = app.raceId;
                        }
                    }
                    if (src == "")
                    {
                        continue;
                    }
                    (string, string) key = (src, target.Id);
                    BracketConnector e;
                    if (!edges.TryGetValue(key, out e))
                    {
                        e = new BracketConnector { FromId = src, ToId = target.Id };
                        edges[key] = e;
                    }
                    e.Pilots = Go.Append(e.Pilots, p);
                }
            }

            return SortConnectors(edges.Values);
        }

        // HasLosersRaces / AnyLosersRoundStamped identify a redemption bracket saved
        // before Race.FromWinnersRound existed: it has redemption heats but none of them
        // records which winners round feeds it.
        internal static bool HasLosersRaces(List<Race> races)
        {
            foreach (Race r in Go.Iter(races))
            {
                if (r.Bracket == BracketSlot.Losers)
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool AnyLosersRoundStamped(List<Race> races)
        {
            foreach (Race r in Go.Iter(races))
            {
                if (r.Bracket == BracketSlot.Losers && r.FromWinnersRound > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// PredictedSlots runs a fresh seed-wins simulation against the same
        /// config to produce per-race "W2-1 · 3rd" labels. Used to show what
        /// feeds each not-yet-populated race so race directors can see the L-bracket
        /// flow before upstream W races finish. Returns null if a fresh bracket can't be
        /// built. (Go: buildPredictedSlots.)
        /// </summary>
        public static Dictionary<string, List<PredictedSlot>> PredictedSlots(Bracket b)
        {
            BracketConfig cfg = b.Config();
            Bracket fresh;
            try
            {
                fresh = Bracket.New(cfg);
            }
            catch (BracketException)
            {
                return null;
            }
            List<BracketConnector> connectors = Simulate(fresh);
            Dictionary<string, Dictionary<string, PilotSlot>> slots = SlotChart(fresh, connectors);
            // A saved bracket can have a different SHAPE from a freshly generated one —
            // it was generated by an older version of the engine, or the runtime spawned
            // an overflow heat. Labelling a live race off a simulation of a different
            // bracket is worse than labelling nothing, so predictions are only used for
            // (lane, round) cells whose heat count still matches.
            Dictionary<(string, int), int> liveCount = new Dictionary<(string, int), int>();
            foreach (Race r in Go.Iter(b.State().Races))
            {
                int c;
                liveCount.TryGetValue((r.Bracket, r.Round), out c);
                liveCount[(r.Bracket, r.Round)] = c + 1;
            }
            Dictionary<(string, int), int> freshCount = new Dictionary<(string, int), int>();
            foreach (Race r in Go.Iter(fresh.State().Races))
            {
                int c;
                freshCount.TryGetValue((r.Bracket, r.Round), out c);
                freshCount[(r.Bracket, r.Round)] = c + 1;
            }
            // A heat count can match by coincidence while the two rounds mean different
            // things, so a saved bracket whose redemption side predates the double-elim
            // cadence fix (no FromWinnersRound stamped anywhere, see LosersRoundPlan) gets
            // NO redemption or finals labels at all — its L4 takes the winners FINAL's
            // droppers where a fresh L4 takes winners round 3's, and naming the wrong
            // feeder heat mid-event is worse than naming none. The winners side is
            // untouched by that fix, so it keeps its labels.
            bool legacyRedemption = HasLosersRaces(b.State().Races) &&
                !AnyLosersRoundStamped(b.State().Races) &&
                AnyLosersRoundStamped(fresh.State().Races);

            Dictionary<string, List<PredictedSlot>> output = new Dictionary<string, List<PredictedSlot>>();
            foreach (Race race in Go.Iter(fresh.State().Races))
            {
                if (legacyRedemption && race.Bracket != BracketSlot.Winners)
                {
                    continue;
                }
                int lc, fc;
                liveCount.TryGetValue((race.Bracket, race.Round), out lc);
                freshCount.TryGetValue((race.Bracket, race.Round), out fc);
                if (lc != fc)
                {
                    continue;
                }
                Dictionary<string, PilotSlot> raceSlots;
                slots.TryGetValue(race.Id, out raceSlots);
                List<PredictedSlot> predicted = new List<PredictedSlot>();
                foreach (string p in Go.Iter(race.Pilots))
                {
                    PilotSlot s;
                    if (raceSlots != null && raceSlots.TryGetValue(p, out s) && s.SourceRaceId != "")
                    {
                        predicted.Add(new PredictedSlot
                        {
                            SourceRaceId = s.SourceRaceId,
                            SourceRank = s.SourceRank,
                            Label = s.SourceRaceId + " · " + PosOrdinalShort(s.SourceRank),
                        });
                    }
                    else
                    {
                        predicted.Add(new PredictedSlot { Label = "Seed " + p });
                    }
                }
                output[race.Id] = predicted;
            }
            return output;
        }

        /// <summary>RaceSortKey zero-pads a race id's trailing number so ids sort numerically.</summary>
        public static string RaceSortKey(string id)
        {
            int dash = id.LastIndexOf('-');
            if (dash < 0 || dash == id.Length - 1)
            {
                return id;
            }
            int n;
            if (Go.Atoi(id.Substring(dash + 1), out n))
            {
                return id.Substring(0, dash) + "-" + FormatPadded(n);
            }
            return id;
        }

        // Go's fmt "%06d" for the non-negative numbers Atoi can yield here.
        private static string FormatPadded(int n)
        {
            return Go.Itoa(n).PadLeft(6, '0');
        }

        /// <summary>PosOrdinalShort renders 1→"1st", 2→"2nd", 11→"11th" …</summary>
        public static string PosOrdinalShort(int n)
        {
            int v = n % 100;
            if (v >= 11 && v <= 13)
            {
                return Go.Itoa(n) + "th";
            }
            switch (n % 10)
            {
                case 1:
                    return Go.Itoa(n) + "st";
                case 2:
                    return Go.Itoa(n) + "nd";
                case 3:
                    return Go.Itoa(n) + "rd";
                default:
                    return Go.Itoa(n) + "th";
            }
        }
    }
}
