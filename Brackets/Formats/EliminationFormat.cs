using System.Collections.Generic;

namespace Brackets
{
    /// <summary>
    /// EliminationFormat wraps the existing bracket engine whole. Every method
    /// delegates straight through, so the proven generation/advancement logic and the
    /// persisted BracketState JSON shape are unchanged. Callers that still need the
    /// concrete engine for elimination-specific rendering (the bracket grid, predicted
    /// slots, SVG flow) reach it via Bracket().
    /// </summary>
    public class EliminationFormat : IRaceFormat
    {
        private Bracket b;

        private EliminationFormat(Bracket b)
        {
            this.b = b;
        }

        // EliminationConfig maps the superset FormatConfig onto the engine's BracketConfig.
        internal static BracketConfig EliminationConfig(FormatConfig cfg)
        {
            return new BracketConfig
            {
                PilotCount = cfg.PilotCount,
                PilotNames = cfg.PilotNames,
                EliminationType = cfg.EliminationType ?? "",
                PilotsPerRace = cfg.PilotsPerRace,
                PilotsToAdvance = cfg.PilotsToAdvance,
                FinalsFormat = cfg.FinalsFormat ?? "",
                AceWinsRequired = cfg.AceWinsRequired,
            };
        }

        public static EliminationFormat New(FormatConfig cfg)
        {
            return new EliminationFormat(Brackets.Bracket.New(EliminationConfig(cfg)));
        }

        public static EliminationFormat FromState(FormatConfig cfg, string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return New(cfg);
            }
            BracketState st = DecodeState(raw);
            return new EliminationFormat(Brackets.Bracket.FromState(EliminationConfig(cfg), st));
        }

        internal static BracketState DecodeState(string raw)
        {
            BracketState st;
            try
            {
                st = BracketJson.Deserialize<BracketState>(raw);
            }
            catch (Newtonsoft.Json.JsonException e)
            {
                throw new BracketException(e.Message);
            }
            if (st == null)
            {
                // Go: json.Unmarshal("null") leaves the zero State in place.
                st = new BracketState();
            }
            return st;
        }

        /// <summary>Bracket returns the underlying engine for elimination-specific rendering.</summary>
        public Bracket Bracket()
        {
            return b;
        }

        public string Kind
        {
            get { return FormatKinds.Elimination; }
        }

        public string LayoutKind
        {
            get { return "bracket-grid"; }
        }

        public BracketState State()
        {
            return b.State();
        }

        public string StateJson()
        {
            return BracketJson.Serialize(b.State());
        }

        public void RestoreState(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }
            BracketState st = DecodeState(raw);
            b = Brackets.Bracket.FromState(b.Config(), st);
        }

        public void ApplyPlacement(string raceId, IList<string> placement)
        {
            b.ApplyPlacement(raceId, placement);
        }

        public bool CanEditRace(string raceId, out string reason)
        {
            return b.CanEditRace(raceId, out reason);
        }

        public void EditRace(string raceId, IList<string> placement)
        {
            b.EditRace(raceId, placement);
        }

        public void ResetRace(string raceId)
        {
            b.ResetRace(raceId);
        }

        public string Champion()
        {
            return b.Champion();
        }

        public bool IsComplete()
        {
            return b.IsComplete();
        }

        // SlotLabel maps an engine BracketSlot to a display group label.
        internal static string SlotLabel(string slot)
        {
            switch (slot)
            {
                case BracketSlot.Winners:
                    return "Winners";
                case BracketSlot.Losers:
                    return "Redemption";
                case BracketSlot.GrandFinal:
                    return "Grand Final";
                case BracketSlot.ChaseTheAce:
                    return "Chase the Ace";
                default:
                    return slot;
            }
        }

        // RaceTitles builds a full display title per race id. Because single- and
        // double-elimination use different race-id schemes (race-N / R%d-%d vs W%d-%d /
        // L%d-%d) the round comes from the engine's Round field, and the race index
        // within a (bracket, round) group is computed by enumeration rather than parsed
        // out of the id (which can be "L4-extra"). Single elimination gets the friendlier
        // Final / Semifinal / Quarterfinal names for its closing rounds.
        internal Dictionary<string, string> RaceTitles()
        {
            BracketState state = b.State();
            Dictionary<(string, int), int> counts = new Dictionary<(string, int), int>(); // races per (slot, round)
            Dictionary<string, int> index = new Dictionary<string, int>();               // race id -> 1-based position within its group
            int maxWinners = 0;
            foreach (Race r in Go.Iter(state.Races))
            {
                (string, int) g = (r.Bracket, r.Round);
                int c;
                counts.TryGetValue(g, out c);
                counts[g] = c + 1;
                index[r.Id] = c + 1;
                if (r.Bracket == BracketSlot.Winners && r.Round > maxWinners)
                {
                    maxWinners = r.Round;
                }
            }
            bool single = b.Config().EliminationType == EliminationType.Single;
            Dictionary<string, string> titles = new Dictionary<string, string>();
            foreach (Race r in Go.Iter(state.Races))
            {
                int n = index[r.Id];
                bool multi = counts[(r.Bracket, r.Round)] > 1;
                string baseTitle;
                switch (r.Bracket)
                {
                    case BracketSlot.Winners:
                        if (single && r.Round == maxWinners)
                        {
                            baseTitle = "Final";
                        }
                        else if (single && r.Round == maxWinners - 1)
                        {
                            baseTitle = "Semifinal";
                        }
                        else if (single && r.Round == maxWinners - 2)
                        {
                            baseTitle = "Quarterfinal";
                        }
                        else if (single)
                        {
                            baseTitle = "Round " + Go.Itoa(r.Round);
                        }
                        else
                        {
                            baseTitle = "Winners Round " + Go.Itoa(r.Round);
                        }
                        break;
                    case BracketSlot.Losers:
                        baseTitle = "Redemption Round " + Go.Itoa(r.Round);
                        break;
                    case BracketSlot.GrandFinal:
                        if (r.Id == "GF-2")
                        {
                            titles[r.Id] = "Grand Final — Reset";
                        }
                        else
                        {
                            titles[r.Id] = "Grand Final";
                        }
                        continue;
                    case BracketSlot.ChaseTheAce:
                        titles[r.Id] = "Chase the Ace — Heat " + Go.Itoa(n);
                        continue;
                    default:
                        baseTitle = SlotLabel(r.Bracket);
                        break;
                }
                if (multi)
                {
                    baseTitle += " — Race " + Go.Itoa(n);
                }
                titles[r.Id] = baseTitle;
            }
            return titles;
        }

        public IReadOnlyList<RaceView> Races()
        {
            BracketState state = b.State();
            Dictionary<string, string> titles = RaceTitles();
            List<RaceView> output = new List<RaceView>();
            foreach (Race r in Go.Iter(state.Races))
            {
                bool canEdit = false;
                if (r.IsComplete)
                {
                    string unused;
                    canEdit = b.CanEditRace(r.Id, out unused);
                }
                string title;
                titles.TryGetValue(r.Id, out title);
                output.Add(new RaceView
                {
                    Id = r.Id,
                    Round = r.Round,
                    GroupLabel = SlotLabel(r.Bracket),
                    Title = title ?? "",
                    Pilots = Go.Copy(r.Pilots),
                    Winners = Go.Copy(r.Winners),
                    Losers = Go.Copy(r.Losers),
                    IsComplete = r.IsComplete,
                    FullPlacement = r.FullPlacement,
                    IsVoid = r.IsVoid,
                    ExpectedWinners = b.ExpectedWinnersCount(r),
                    CanEdit = canEdit,
                });
            }
            return output;
        }

        /// <summary>
        /// Standings returns the chase-the-ace ranking when present, otherwise a
        /// champion-only list (elimination has no general running order).
        /// </summary>
        public IReadOnlyList<Standing> Standings()
        {
            BracketState state = b.State();
            ChaseTheAceState cta = state.ChaseTheAce;
            if (cta != null && cta.IsComplete)
            {
                List<Standing> output = new List<Standing>();
                output.Add(new Standing { Rank = 1, Pilot = cta.Champion, Detail = AceDetail(cta, cta.Champion) });
                int i = 0;
                foreach (string p in Go.Iter(cta.Standings))
                {
                    output.Add(new Standing { Rank = i + 2, Pilot = p, Detail = AceDetail(cta, p) });
                    i++;
                }
                return output;
            }
            string champ = b.Champion();
            if (champ != "")
            {
                // When the deciding heat recorded a full finishing order, that order
                // IS the podium. Returning the champion alone left FinalOrdering to
                // rank everyone else in the final by seed: the 2026 Nationals Old Dogs
                // final ran Foxur, bob9, Voland, Clog wog but was published as
                // Foxur, Voland (seed 3), MacDaddy (seed 4).
                Race final = DeciderRace(state.Races, champ);
                if (final != null && final.FullPlacement)
                {
                    List<Standing> output = new List<Standing>();
                    int i = 0;
                    foreach (string p in Go.Iter(final.Pilots))
                    {
                        output.Add(new Standing { Rank = i + 1, Pilot = p });
                        i++;
                    }
                    if (output.Count > 0 && output[0].Pilot == champ)
                    {
                        return output;
                    }
                }
                return new List<Standing> { new Standing { Rank = 1, Pilot = champ } };
            }
            return new List<Standing>();
        }

        // DeciderRace is the race that crowned champ: the latest-round complete,
        // non-void heat the champion won.
        internal static Race DeciderRace(List<Race> races, string champ)
        {
            Race best = null;
            foreach (Race r in Go.Iter(races))
            {
                if (r == null || !r.IsComplete || r.IsVoid || Go.Len(r.Winners) == 0 || r.Winners[0] != champ)
                {
                    continue;
                }
                if (best == null || r.Round >= best.Round)
                {
                    best = r;
                }
            }
            return best;
        }

        private static string AceDetail(ChaseTheAceState cta, string pilot)
        {
            int wins = cta.Wins == null ? 0 : cta.Wins.Get(pilot);
            if (wins == 1)
            {
                return "1 win";
            }
            return Go.Itoa(wins) + " wins";
        }
    }
}
