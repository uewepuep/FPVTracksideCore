using System.Collections.Generic;
using Newtonsoft.Json;

namespace SeederEngine
{
    // Port of internal/platform/bracket (bracket.go) — MultiGP-style drone-racing
    // tournament brackets.
    //
    // Ported from the drone-racing Nuxt project (composables/useBracket.ts). Supports
    // single + double elimination, traditional grand-final or chase-the-ace finals,
    // 2-4 pilots per race, configurable advancement.
    //
    // The bracket is generated up-front from a config; SelectWinners drives progression
    // race-by-race. Winners and losers are placed into subsequent races using snake
    // seeding (round 1) and least-filled balanced placement (later rounds).

    /// <summary>EliminationType determines whether losers get a second chance.</summary>
    public static class EliminationType
    {
        public const string Single = "single";
        public const string Double = "double";
    }

    /// <summary>FinalsFormat picks the championship round style.</summary>
    public static class FinalsFormat
    {
        public const string GrandFinal = "double-elimination"; // legacy name kept for parity with TS source
        public const string ChaseTheAce = "chase-the-ace";
    }

    /// <summary>BracketSlot identifies which sub-bracket a race belongs to.</summary>
    public static class BracketSlot
    {
        public const string Winners = "winners";
        public const string Losers = "losers";
        public const string GrandFinal = "grand-final";
        public const string ChaseTheAce = "chase-the-ace";
        // Filler marks a non-competitive "filler heat" for knocked-out pilots.
        // The bracket engine never generates, advances, scores, or reads these —
        // they're carried in State.FillerRaces purely as data, populated by handler
        // code so eliminated pilots keep getting heats. See race_bracket_filler.go.
        public const string Filler = "filler";
    }

    /// <summary>BracketConfig is the tournament setup. PilotNames is the qualifying order — seed 1 first.</summary>
    public class BracketConfig
    {
        public int PilotCount { get; set; }
        public List<string> PilotNames { get; set; }
        public string EliminationType { get; set; }
        public int PilotsPerRace { get; set; }
        public int PilotsToAdvance { get; set; }
        public string FinalsFormat { get; set; }
        public int AceWinsRequired { get; set; } // chase-the-ace only; defaults to 2

        public BracketConfig Clone()
        {
            return new BracketConfig
            {
                PilotCount = PilotCount,
                PilotNames = PilotNames,
                EliminationType = EliminationType,
                PilotsPerRace = PilotsPerRace,
                PilotsToAdvance = PilotsToAdvance,
                FinalsFormat = FinalsFormat,
                AceWinsRequired = AceWinsRequired,
            };
        }
    }

    /// <summary>Race is a single heat in the bracket.</summary>
    public class Race
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("round")]
        public int Round { get; set; }

        [JsonProperty("pilots")]
        public List<string> Pilots { get; set; }

        [JsonProperty("winners")]
        public List<string> Winners { get; set; }

        [JsonProperty("losers")]
        public List<string> Losers { get; set; }

        [JsonProperty("isComplete")]
        public bool IsComplete { get; set; }

        [JsonProperty("bracket")]
        public string Bracket { get; set; } = "";

        // FinalTarget is set for "consolidation" races added when a side of
        // the bracket can't naturally converge to the finals-format target
        // (e.g. CTA needs 2 pilots from each side, but 3-ppr/2-advance ends
        // with 4 W-bracket winners). When > 0 it overrides the normal
        // per-race advance count and the race may exceed PilotsPerRace.
        [JsonProperty("finalTarget", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int FinalTarget { get; set; }

        // FullPlacement is true when SelectFullPlacement recorded the
        // complete finish order. When false, only Winners (the top N
        // advancers) were ranked — the remaining pilots in race.Pilots
        // are eliminated but their relative finishing positions weren't
        // captured, so the UI should leave their position labels blank
        // rather than guessing.
        [JsonProperty("fullPlacement", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool FullPlacement { get; set; }

        // IsVoid is set on chase-the-ace finals races that were pre-created
        // up front (so all CTA heats can be exported to the timing system in
        // advance) but turned out not to be needed because a champion was
        // crowned earlier. A voided race is never runnable, is excluded from
        // completion checks, and refuses results. Its ID stays stable so any
        // already-exported timing heat that maps to it is harmless.
        [JsonProperty("isVoid", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsVoid { get; set; }

        // FromWinnersRound is set on the losers-bracket rounds that absorb a
        // winners round's droppers ("major" rounds). 0 means the round takes no
        // new droppers — either a "minor" (halving) round that just reduces the
        // redemption pool, or a bracket generated before this field existed, in
        // which case the runtime falls back to the old winners-round-N → losers-
        // round-N mapping. See LosersRoundPlan.
        [JsonProperty("fromWinnersRound", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int FromWinnersRound { get; set; }
    }

    /// <summary>ChaseTheAceState tracks running CTA-finals progress.</summary>
    public class ChaseTheAceState
    {
        [JsonProperty("wins")]
        public GoMap<int> Wins { get; set; }

        // Points accumulates per-pilot finishing-position totals across every
        // CTA race: 1pt for 1st, 2pt for 2nd, 3pt for 3rd, 4pt for 4th, etc.
        // Lower total = better non-champion finish.
        [JsonProperty("points")]
        public GoMap<int> Points { get; set; }

        [JsonProperty("finalists")]
        public List<string> Finalists { get; set; }

        [JsonProperty("isComplete")]
        public bool IsComplete { get; set; }

        [JsonProperty("champion")]
        public string Champion { get; set; } = "";

        // Standings is the final non-champion ranking sorted by Points (asc)
        // once a Champion has been crowned. Set by ComputeChaseTheAceStandings.
        [JsonProperty("standings")]
        public List<string> Standings { get; set; }

        // TieBreakNeeded lists groups of pilots tied on points in the final
        // standings; each group needs its own race-off to determine final
        // placement. Empty when no ties remain.
        [JsonProperty("tieBreakNeeded")]
        public List<List<string>> TieBreakNeeded { get; set; }

        [JsonProperty("requiredWins")]
        public int RequiredWins { get; set; }

        public bool ShouldSerializeStandings()
        {
            return Standings != null && Standings.Count > 0;
        }

        public bool ShouldSerializeTieBreakNeeded()
        {
            return TieBreakNeeded != null && TieBreakNeeded.Count > 0;
        }
    }

    /// <summary>BracketState is the full mutable tournament state (Go: bracket.State).</summary>
    public class BracketState
    {
        [JsonProperty("races")]
        public List<Race> Races { get; set; }

        [JsonProperty("rounds")]
        public int Rounds { get; set; }

        [JsonProperty("currentRound")]
        public int CurrentRound { get; set; }

        [JsonProperty("chaseTheAce")]
        public ChaseTheAceState ChaseTheAce { get; set; }

        // FillerRaces holds non-competitive "filler heats" (BracketSlot.Filler) for pilots
        // who have been knocked out of the bracket, so they keep flying. These are
        // generated and replaced wholesale by handler code (regenerateFillerHeats)
        // — the bracket engine never touches them. Their IDs ("F2-1", …) live in a
        // separate namespace from competitive race IDs.
        [JsonProperty("fillerRaces")]
        public List<Race> FillerRaces { get; set; }

        public bool ShouldSerializeChaseTheAce()
        {
            return ChaseTheAce != null;
        }

        public bool ShouldSerializeFillerRaces()
        {
            return FillerRaces != null && FillerRaces.Count > 0;
        }
    }
}
