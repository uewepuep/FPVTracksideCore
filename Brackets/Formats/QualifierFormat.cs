using System.Collections.Generic;
using QualEngine = Brackets.Qual;

namespace Brackets
{
    /// <summary>
    /// QualifierFormat wraps the Qual interpreter (the "Double Elimination — Top 4
    /// Advance" format). Its persisted state is a BracketState, so callers reuse the
    /// elimination grid by wrapping that state; the qualifier exposes the interpreter
    /// via Qual() for the spec-derived predicted-slot labels.
    /// </summary>
    public class QualifierFormat : IRaceFormat
    {
        private QualEngine q;

        private QualifierFormat(QualEngine q)
        {
            this.q = q;
        }

        internal static QualConfig QualifierConfig(FormatConfig cfg)
        {
            return new QualConfig
            {
                PilotCount = cfg.PilotCount,
                PilotNames = cfg.PilotNames,
                CTA = cfg.QualifierCTA,
                AceWinsRequired = cfg.AceWinsRequired,
                PhotoSeed = cfg.QualifierPhotoSeed,
                PilotsPerRace = cfg.PilotsPerRace,
                Advance = cfg.PilotsToAdvance,
            };
        }

        public static QualifierFormat New(FormatConfig cfg)
        {
            return new QualifierFormat(QualEngine.New(QualifierConfig(cfg)));
        }

        public static QualifierFormat FromState(FormatConfig cfg, string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return New(cfg);
            }
            BracketState st = EliminationFormat.DecodeState(raw);
            return new QualifierFormat(QualEngine.FromState(QualifierConfig(cfg), st));
        }

        /// <summary>Qual returns the underlying interpreter for qualifier-specific rendering.</summary>
        public QualEngine Qual()
        {
            return q;
        }

        public string Kind
        {
            get { return FormatKinds.Qualifier; }
        }

        public string LayoutKind
        {
            get { return "bracket-grid"; }
        }

        public BracketState State()
        {
            return q.State();
        }

        public string StateJson()
        {
            return BracketJson.Serialize(q.State());
        }

        public void RestoreState(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }
            BracketState st = EliminationFormat.DecodeState(raw);
            q = QualEngine.FromState(q.Cfg(), st);
        }

        public void ApplyPlacement(string raceId, IList<string> placement)
        {
            q.ApplyPlacement(raceId, placement);
        }

        public bool CanEditRace(string raceId, out string reason)
        {
            return q.CanEditRace(raceId, out reason);
        }

        public void EditRace(string raceId, IList<string> placement)
        {
            q.EditRace(raceId, placement);
        }

        public void ResetRace(string raceId)
        {
            q.ResetRace(raceId);
        }

        /// <summary>
        /// Champion is empty by design — the qualifier crowns nobody; it produces a
        /// ranked top-4 that qualify onward (see Standings/Qualified).
        /// </summary>
        public string Champion()
        {
            return "";
        }

        public bool IsComplete()
        {
            return q.IsComplete();
        }

        /// <summary>Standings returns the ranked top-4 qualified pilots, each tagged "Qualified".</summary>
        public IReadOnlyList<Standing> Standings()
        {
            List<string> qual = q.Qualified();
            List<Standing> output = new List<Standing>();
            int i = 0;
            foreach (string p in Go.Iter(qual))
            {
                output.Add(new Standing { Rank = i + 1, Pilot = p, Detail = "Qualified" });
                i++;
            }
            return output;
        }

        /// <summary>Races projects the qualifier's heats with qualifier-specific titles.</summary>
        public IReadOnlyList<RaceView> Races()
        {
            BracketState state = q.State();
            Dictionary<string, string> titles = QualRaceTitles(state);
            List<RaceView> output = new List<RaceView>();
            foreach (Race r in Go.Iter(state.Races))
            {
                bool canEdit = false;
                if (r.IsComplete)
                {
                    string unused;
                    canEdit = q.CanEditRace(r.Id, out unused);
                }
                string title;
                titles.TryGetValue(r.Id, out title);
                output.Add(new RaceView
                {
                    Id = r.Id,
                    Round = r.Round,
                    GroupLabel = QualSlotLabel(r.Bracket),
                    Title = title ?? "",
                    Pilots = Go.Copy(r.Pilots),
                    Winners = Go.Copy(r.Winners),
                    Losers = Go.Copy(r.Losers),
                    IsComplete = r.IsComplete,
                    FullPlacement = r.FullPlacement,
                    IsVoid = r.IsVoid,
                    ExpectedWinners = q.ExpectedWinners(r),
                    CanEdit = canEdit,
                });
            }
            return output;
        }

        // QualSlotLabel maps a slot to a qualifier display label. The terminal final
        // (grand-final slot) reads "Final" since the qualifier has no grand final.
        internal static string QualSlotLabel(string slot)
        {
            switch (slot)
            {
                case BracketSlot.Winners:
                    return "Winners";
                case BracketSlot.Losers:
                    return "Redemption";
                case BracketSlot.GrandFinal:
                    return "Final";
                case BracketSlot.ChaseTheAce:
                    return "Chase the Ace";
                default:
                    return slot;
            }
        }

        // QualRaceTitles is the shared title builder for a qualifier BracketState —
        // reused by the mains-progression wrapper, which renders one qualifier per main.
        // (Winners/Redemption Round N — Race M, the top-4 Final, and chase-the-ace heats.)
        internal static Dictionary<string, string> QualRaceTitles(BracketState state)
        {
            Dictionary<(string, int), int> counts = new Dictionary<(string, int), int>();
            Dictionary<string, int> index = new Dictionary<string, int>();
            foreach (Race r in Go.Iter(state.Races))
            {
                (string, int) g = (r.Bracket, r.Round);
                int c;
                counts.TryGetValue(g, out c);
                counts[g] = c + 1;
                index[r.Id] = c + 1;
            }
            Dictionary<string, string> titles = new Dictionary<string, string>();
            foreach (Race r in Go.Iter(state.Races))
            {
                int n = index[r.Id];
                bool multi = counts[(r.Bracket, r.Round)] > 1;
                string baseTitle;
                switch (r.Bracket)
                {
                    case BracketSlot.Winners:
                        baseTitle = "Winners Round " + Go.Itoa(r.Round);
                        break;
                    case BracketSlot.Losers:
                        baseTitle = "Redemption Round " + Go.Itoa(r.Round);
                        break;
                    case BracketSlot.GrandFinal:
                        titles[r.Id] = "Final — Top 4 Qualify";
                        continue;
                    case BracketSlot.ChaseTheAce:
                        titles[r.Id] = "Final (Chase the Ace) — Heat " + Go.Itoa(n);
                        continue;
                    default:
                        baseTitle = QualSlotLabel(r.Bracket);
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
    }
}
