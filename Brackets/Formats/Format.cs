using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Brackets
{
    // Port of internal/platform/raceformat — a pluggable abstraction over
    // drone-racing event formats.
    //
    // The existing single/double elimination engine (Bracket) is the first format,
    // wrapped whole by EliminationFormat so its proven generation and advancement
    // logic stays untouched. New formats (seeded ladder, points mains) are clean-room
    // implementations of the IRaceFormat interface.
    //
    // Every format persists its state as a JSON blob (race_brackets.bracket_state on
    // the AUFPV platform). A discriminator (race_brackets.format) records which
    // FormatKind a row uses, so FromState knows which concrete state class to
    // rehydrate. The JSON shapes are byte-compatible with the Go engine's.

    /// <summary>FormatKind discriminators (persisted on race_brackets.format).</summary>
    public static class FormatKinds
    {
        // Elimination is the legacy single/double elimination engine with a
        // grand-final or chase-the-ace finals. Default for existing rows.
        public const string Elimination = "elimination";
        // Ladder is the MultiGP-style seeded ladder.
        public const string Ladder = "ladder";
        // Mains is the points-based A/B/C mains format.
        public const string Mains = "mains";
        // Qualifier is the "Double Elimination — Top 4 Advance" qualifier: a
        // double-elim that ends in a 4-pilot final and outputs a ranked top-4 that
        // qualify onward (no single champion). Backed by Qual.
        public const string Qualifier = "qualifier";
        // MainsProgression is a bump-up stack of qualifiers: one "Top 4 Advance"
        // bracket per Main (A = championship … lowest letter); the top finishers of
        // each main bump up into the next. Backed by Mains.
        public const string MainsProgression = "mainsprogression";
    }

    /// <summary>Every Go `error` return becomes a throw of this, with the same message text.</summary>
    public class BracketException : Exception
    {
        public BracketException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// MainsDivision is a named group of mains scored as one competition. The first
    /// main listed is the division's top main, whose winner is the division champion.
    /// </summary>
    public class MainsDivision
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("mains")]
        public List<string> Mains { get; set; }

        // Skills optionally records the skill levels this division was filtered to at
        // creation (e.g. ["Pro"] or ["Intermediate","Novice"]). It's informational —
        // the setup form does the skill-based pilot distribution and submits the
        // resulting rosters; this is kept so the division can show its skill tag.
        // Empty = no skill filter was applied.
        [JsonProperty("skills")]
        public List<string> Skills { get; set; }

        // ClassId optionally links the division to a championship class whose
        // season-long pilot roster (championship_class_pilots) drove the pilot
        // distribution at creation. Like Skills it is informational here — the setup
        // form distributes and submits the resulting rosters — but the finalise flow
        // uses it to pre-select the matching championship + class for the division's
        // results. 0 = no class link.
        [JsonProperty("classId", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int ClassId { get; set; }

        public bool ShouldSerializeSkills()
        {
            return Skills != null && Skills.Count > 0;
        }
    }

    /// <summary>
    /// FormatConfig is the superset of setup parameters across every format (Go:
    /// raceformat.Config). Each format reads only the fields it needs: elimination
    /// uses the EliminationType/Finals… fields, ladder reuses
    /// PilotsPerRace/PilotsToAdvance, mains uses the Mains… fields. Format-specific
    /// params are decoded from race_brackets.format_config.
    /// </summary>
    public class FormatConfig
    {
        public int PilotCount { get; set; }
        public List<string> PilotNames { get; set; } // qualifying order — seed 1 first

        // Elimination + ladder.
        public string EliminationType { get; set; } = "";
        public int PilotsPerRace { get; set; }
        public int PilotsToAdvance { get; set; }
        public string FinalsFormat { get; set; } = "";
        public int AceWinsRequired { get; set; }

        // Mains (decoded from format_config).
        public List<string> Mains { get; set; }
        public int Rounds { get; set; }
        public string Direction { get; set; } = "";
        public Dictionary<int, int> PointsTable { get; set; }
        public int DropWorst { get; set; }
        // MainsRosters is an optional explicit pilot→main assignment from the setup
        // form's drag-drop. When empty, pilots are chunked into mains by seed.
        public Dictionary<string, List<string>> MainsRosters { get; set; }
        // MainsDivisions optionally groups the mains into independent competitions.
        // Empty = single competition over all mains.
        public List<MainsDivision> MainsDivisions { get; set; }

        // Qualifier: run the 4-pilot final as a chase-the-ace mini-series.
        public bool QualifierCTA { get; set; }
        // QualifierPhotoSeed selects the 16-pilot round-1 seeding (true = photo's
        // exact layout, false/default = MultiGP snake). Ignored for other sizes.
        public bool QualifierPhotoSeed { get; set; }

        // Mains progression (a stack of qualifiers). PilotsPerMain is each main's
        // running size; BumpUp is how many of each main's finishers move up into the
        // next. Reuses QualifierCTA/AceWinsRequired/QualifierPhotoSeed per main.
        public int PilotsPerMain { get; set; }
        public int BumpUp { get; set; }
        // CTAMains selects which mains run their 4-pilot final as chase-the-ace (when
        // QualifierCTA is on). Empty = the A Main only; ["*"] = every main.
        public List<string> CTAMains { get; set; }
    }

    /// <summary>
    /// RaceView is the format-neutral projection of one heat that handlers, the
    /// ingest, and copy payloads consume. It is a superset of Race; GroupLabel
    /// generalises the hard-coded winners/losers/grand-final/chase-the-ace lanes into
    /// a free-form label ("Winners", "A Main", "Ladder Bout 2").
    /// </summary>
    public class RaceView
    {
        public string Id { get; set; } = "";
        public int Round { get; set; }
        public string GroupLabel { get; set; } = "";
        // Title is the full human-friendly heading for the race, format-specific and
        // self-contained (e.g. "A Main — Round 1", "Winners Round 2 — Race 3",
        // "Chase the Ace — Heat 2", "Final"). Unlike GroupLabel it already folds in
        // the round/race/heat, so callers display it verbatim.
        public string Title { get; set; } = "";
        public List<string> Pilots { get; set; }
        public List<string> Winners { get; set; }
        public List<string> Losers { get; set; }
        public bool IsComplete { get; set; }
        public bool FullPlacement { get; set; }
        public bool IsVoid { get; set; }
        // ExpectedWinners is how many pilots advance (elimination/ladder) or how
        // many must be ranked for the result to count (mains rank everyone).
        public int ExpectedWinners { get; set; }
        // CanEdit reports whether a completed race may still be re-run.
        public bool CanEdit { get; set; }
        // Locked marks a race that can't be run yet (e.g. a CTA heat awaiting the
        // previous heat); LockedReason names the blocker.
        public bool Locked { get; set; }
        public string LockedReason { get; set; } = "";
    }

    /// <summary>Standing is one row in a format's running order.</summary>
    public class Standing
    {
        public int Rank { get; set; }
        public string Pilot { get; set; } = "";
        // Detail is a format-specific summary: "2 wins", "47 pts (R1:12 R2:18)", or
        // "" for pure single-result elimination.
        public string Detail { get; set; } = "";
        // Tied flags a pilot whose rank is unresolved on placements alone (the race
        // director may choose to run a tie-break). Formats never auto-create one.
        public bool Tied { get; set; }
    }

    /// <summary>
    /// IRaceFormat is the strategy every race format implements (Go: raceformat.Format).
    /// Every error becomes a BracketException.
    /// </summary>
    public interface IRaceFormat
    {
        string Kind { get; }

        // LayoutKind selects the runner/public template partial:
        // "bracket-grid" | "ladder-grid" | "mains-table" | "mains-progression-tabs".
        string LayoutKind { get; }

        // StateJson returns the JSON-serialised state blob to persist (= json.Marshal(f.State())).
        string StateJson();

        // Races projects the format's heats for the UI and ingest.
        IReadOnlyList<RaceView> Races();

        // ApplyPlacement records a finish order for one race and advances state.
        void ApplyPlacement(string raceId, IList<string> placement);

        // CanEditRace reports whether a completed race may still be re-run, and why
        // not when it can't.
        bool CanEditRace(string raceId, out string reason);

        // EditRace re-runs a completed race, rewinding any clean downstream state.
        void EditRace(string raceId, IList<string> placement);

        // ResetRace un-runs a completed race back to pending, rewinding any clean
        // downstream state. Subject to the same CanEditRace guard as EditRace; a
        // no-op on a race that hasn't been run.
        void ResetRace(string raceId);

        // Champion is the overall winner, or "" while undecided.
        string Champion();

        // IsComplete reports whether the format has crowned a champion / finished.
        bool IsComplete();

        // Standings is the full running order. Elimination returns a champion-only
        // (or empty) list; mains and ladder return the complete ranking.
        IReadOnlyList<Standing> Standings();
    }

    /// <summary>Format registry (Go: raceformat.New / FromState / FinalOrdering).</summary>
    public static class RaceFormats
    {
        /// <summary>Create generates a fresh format of the given kind from cfg ("" means elimination).</summary>
        public static IRaceFormat Create(string kind, FormatConfig cfg)
        {
            switch (kind ?? "")
            {
                case FormatKinds.Elimination:
                case "":
                    return EliminationFormat.New(cfg);
                case FormatKinds.Ladder:
                    return LadderFormat.New(cfg);
                case FormatKinds.Mains:
                    return MainsFormat.New(cfg);
                case FormatKinds.Qualifier:
                    return QualifierFormat.New(cfg);
                case FormatKinds.MainsProgression:
                    return MainsProgressionFormat.New(cfg);
                default:
                    throw new BracketException("raceformat: unknown format " + Go.Quote(kind));
            }
        }

        /// <summary>FromState rehydrates a format of the given kind from a persisted state blob.</summary>
        public static IRaceFormat FromState(string kind, FormatConfig cfg, string stateJson)
        {
            switch (kind ?? "")
            {
                case FormatKinds.Elimination:
                case "":
                    return EliminationFormat.FromState(cfg, stateJson);
                case FormatKinds.Ladder:
                    return LadderFormat.FromState(cfg, stateJson);
                case FormatKinds.Mains:
                    return MainsFormat.FromState(cfg, stateJson);
                case FormatKinds.Qualifier:
                    return QualifierFormat.FromState(cfg, stateJson);
                case FormatKinds.MainsProgression:
                    return MainsProgressionFormat.FromState(cfg, stateJson);
                default:
                    throw new BracketException("raceformat: unknown format " + Go.Quote(kind));
            }
        }

        /// <summary>
        /// FinalOrdering returns a best-effort full finishing order for every pilot the
        /// format has raced, suitable for pre-filling a race director's results-review
        /// screen.
        ///
        /// It starts from Standings() — authoritative as far as it goes — and appends any
        /// pilot not already ranked, ordered by the latest round they appeared in
        /// (descending: pilots who survived to a deeper round finish higher), with seed as
        /// the tiebreak. For ladder and mains, Standings() already ranks everyone, so the
        /// appended tail is empty and the result equals Standings(). For elimination,
        /// Standings() yields only the champion (or the ordered chase-the-ace finalists),
        /// so this fills the knocked-out pilots in beneath them.
        ///
        /// Appended rows carry Tied=true to flag that their placement is inferred rather
        /// than decided on the track — the review UI surfaces this so the RD can adjust.
        /// seeds maps pilot name to 1-based seed; missing entries sort last within a round.
        /// </summary>
        public static IReadOnlyList<Standing> FinalOrdering(IRaceFormat f, IDictionary<string, int> seeds)
        {
            IReadOnlyList<Standing> ranked = f.Standings();
            HashSet<string> seen = new HashSet<string>();
            List<Standing> output = new List<Standing>();
            foreach (Standing s in Go.Iter(ranked))
            {
                if (seen.Contains(s.Pilot)) // Standings may list a pilot twice (e.g. a 2-pilot CTA)
                {
                    continue;
                }
                seen.Add(s.Pilot);
                output.Add(new Standing { Rank = s.Rank, Pilot = s.Pilot, Detail = s.Detail, Tied = s.Tied });
            }

            // Latest round each pilot appeared in, plus a stable roster in first-seen order.
            Dictionary<string, int> lastRound = new Dictionary<string, int>();
            List<string> roster = new List<string>();
            foreach (RaceView rv in f.Races())
            {
                foreach (string p in Go.Iter(rv.Pilots))
                {
                    int lr;
                    if (!lastRound.TryGetValue(p, out lr))
                    {
                        roster.Add(p);
                        lr = 0;
                        lastRound[p] = 0;
                    }
                    if (rv.Round > lr)
                    {
                        lastRound[p] = rv.Round;
                    }
                }
            }

            List<string> rest = new List<string>();
            foreach (string p in roster)
            {
                if (!seen.Contains(p))
                {
                    rest.Add(p);
                }
            }
            Func<string, int> seedOf = p =>
            {
                int s;
                if (seeds != null && seeds.TryGetValue(p, out s))
                {
                    return s;
                }
                return 1 << 30;
            };
            Go.SortStable(rest, (a, b) =>
            {
                if (lastRound[a] != lastRound[b])
                {
                    return lastRound[a] > lastRound[b];
                }
                return seedOf(a) < seedOf(b);
            });

            foreach (string p in rest)
            {
                output.Add(new Standing { Pilot = p, Tied = true });
            }
            // Renumber contiguously so ranks are always 1..N regardless of any gaps or
            // duplicates Standings() may have carried.
            for (int i = 0; i < output.Count; i++)
            {
                output[i].Rank = i + 1;
            }
            return output;
        }
    }

    /// <summary>Shared helpers (raceformat/util.go).</summary>
    internal static class FormatUtil
    {
        // TrimNames trims whitespace and drops empty pilot names, preserving order.
        public static List<string> TrimNames(List<string> names)
        {
            List<string> output = new List<string>();
            foreach (string n in Go.Iter(names))
            {
                string t = Go.TrimSpace(n);
                if (t != "")
                {
                    output.Add(t);
                }
            }
            return output;
        }

        // SamePilotSet checks that placement is a permutation of pilots (same multiset).
        // Returns a descriptive error message when it isn't, else null.
        public static string SamePilotSet(List<string> pilots, IList<string> placement)
        {
            int pc = placement == null ? 0 : placement.Count;
            if (pc != Go.Len(pilots))
            {
                return "must rank every pilot in the race";
            }
            Dictionary<string, int> want = new Dictionary<string, int>();
            foreach (string p in Go.Iter(pilots))
            {
                int c;
                want.TryGetValue(p, out c);
                want[p] = c + 1;
            }
            foreach (string p in Go.Iter(placement))
            {
                int c;
                want.TryGetValue(p, out c);
                if (c == 0)
                {
                    return "contains a pilot not in the race: " + p;
                }
                want[p] = c - 1;
            }
            return null;
        }

        // RemoveAll returns src with every element present in drop removed (all copies).
        public static List<string> RemoveAll(List<string> src, List<string> drop)
        {
            if (Go.Len(drop) == 0)
            {
                return src;
            }
            Dictionary<string, int> rm = new Dictionary<string, int>();
            foreach (string d in drop)
            {
                int c;
                rm.TryGetValue(d, out c);
                rm[d] = c + 1;
            }
            List<string> output = null;
            foreach (string s in Go.Iter(src))
            {
                int c;
                rm.TryGetValue(s, out c);
                if (c > 0)
                {
                    rm[s] = c - 1;
                    continue;
                }
                output = Go.Append(output, s);
            }
            // Go: out := src[:0:0] — a zero-length non-nil slice when src is non-nil.
            if (output == null && src != null)
            {
                output = new List<string>();
            }
            return output;
        }
    }
}
