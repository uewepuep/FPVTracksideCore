using System.Collections.Generic;
using Newtonsoft.Json;
using MainsEngine = Brackets.Mains;

namespace Brackets
{
    /// <summary>
    /// MainsProgressionFormat wraps the Mains engine: a bump-up stack of "Top 4
    /// Advance" qualifiers, one per Main. Each main is a Qual, so the
    /// runner/public/ingest/finalise reuse the qualifier paths via prefixed race ids
    /// ("A:W1-1"). Callers render one qualifier per built main (tabs).
    /// </summary>
    public class MainsProgressionFormat : IRaceFormat
    {
        private MainsEngine m;

        private MainsProgressionFormat(MainsEngine m)
        {
            this.m = m;
        }

        internal static MainsProgressionConfig ToEngineConfig(FormatConfig cfg)
        {
            return new MainsProgressionConfig
            {
                PilotCount = cfg.PilotCount,
                PilotNames = cfg.PilotNames,
                PilotsPerMain = cfg.PilotsPerMain,
                BumpUp = cfg.BumpUp,
                CTA = cfg.QualifierCTA,
                AceWinsRequired = cfg.AceWinsRequired,
                PhotoSeed = cfg.QualifierPhotoSeed,
                CTAMains = cfg.CTAMains,
                PilotsPerRace = cfg.PilotsPerRace,
                Advance = cfg.PilotsToAdvance,
            };
        }

        public static MainsProgressionFormat New(FormatConfig cfg)
        {
            return new MainsProgressionFormat(MainsEngine.New(ToEngineConfig(cfg)));
        }

        public static MainsProgressionFormat FromState(FormatConfig cfg, string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return New(cfg);
            }
            MainsProgressionState st = Decode(raw);
            return new MainsProgressionFormat(MainsEngine.FromState(ToEngineConfig(cfg), st));
        }

        private static MainsProgressionState Decode(string raw)
        {
            MainsProgressionState st;
            try
            {
                st = BracketJson.Deserialize<MainsProgressionState>(raw);
            }
            catch (JsonException e)
            {
                throw new BracketException(e.Message);
            }
            if (st == null)
            {
                st = new MainsProgressionState();
            }
            return st;
        }

        /// <summary>Mains returns the underlying engine for tabbed per-main rendering.</summary>
        public MainsEngine Mains()
        {
            return m;
        }

        public string Kind
        {
            get { return FormatKinds.MainsProgression; }
        }

        public string LayoutKind
        {
            get { return "mains-progression-tabs"; }
        }

        public MainsProgressionState State()
        {
            return m.State();
        }

        public string StateJson()
        {
            return BracketJson.Serialize(m.State());
        }

        public void RestoreState(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }
            MainsProgressionState st = Decode(raw);
            m = MainsEngine.FromState(m.Cfg(), st);
        }

        public void ApplyPlacement(string raceId, IList<string> placement)
        {
            m.ApplyPlacement(raceId, placement);
        }

        public bool CanEditRace(string raceId, out string reason)
        {
            return m.CanEditRace(raceId, out reason);
        }

        public void EditRace(string raceId, IList<string> placement)
        {
            m.EditRace(raceId, placement);
        }

        public void ResetRace(string raceId)
        {
            m.ResetRace(raceId);
        }

        public string Champion()
        {
            return m.Champion();
        }

        public bool IsComplete()
        {
            return m.IsComplete();
        }

        /// <summary>
        /// Standings is the overall competition order, A main first then B, …, with the
        /// pilot's main + place as the detail line.
        /// </summary>
        public IReadOnlyList<Standing> Standings()
        {
            List<RankRow> rows = m.Ranking();
            List<Standing> output = new List<Standing>();
            int i = 0;
            foreach (RankRow r in Go.Iter(rows))
            {
                output.Add(new Standing
                {
                    Rank = i + 1,
                    Pilot = r.Pilot,
                    Detail = r.Main + " Main · " + PosOrdinal(r.MainRank),
                });
                i++;
            }
            return output;
        }

        /// <summary>
        /// Races projects every built main's heats with prefixed ids ("A:W1-1") and a
        /// main-qualified title ("A Main · Winners Round 2 — Race 3").
        /// </summary>
        public IReadOnlyList<RaceView> Races()
        {
            List<RaceView> output = new List<RaceView>();
            foreach (MainView mv in m.MainViews())
            {
                if (mv.Qual == null)
                {
                    continue; // unbuilt main has no heats yet
                }
                BracketState state = mv.Qual.State();
                Dictionary<string, string> titles = QualifierFormat.QualRaceTitles(state);
                foreach (Race r in Go.Iter(state.Races))
                {
                    string prefixed = mv.Label + ":" + r.Id;
                    bool canEdit = false;
                    if (r.IsComplete)
                    {
                        string unused;
                        canEdit = m.CanEditRace(prefixed, out unused);
                    }
                    string title;
                    titles.TryGetValue(r.Id, out title);
                    output.Add(new RaceView
                    {
                        Id = prefixed,
                        Round = r.Round,
                        GroupLabel = mv.Label + " Main · " + QualifierFormat.QualSlotLabel(r.Bracket),
                        Title = mv.Label + " Main · " + (title ?? ""),
                        Pilots = Go.Copy(r.Pilots),
                        Winners = Go.Copy(r.Winners),
                        Losers = Go.Copy(r.Losers),
                        IsComplete = r.IsComplete,
                        FullPlacement = r.FullPlacement,
                        IsVoid = r.IsVoid,
                        ExpectedWinners = mv.Qual.ExpectedWinners(r),
                        CanEdit = canEdit,
                    });
                }
            }
            return output;
        }

        // PosOrdinal renders 1→"1st", 2→"2nd", … for standings detail.
        internal static string PosOrdinal(int n)
        {
            string suffix = "th";
            if (n % 100 < 11 || n % 100 > 13)
            {
                switch (n % 10)
                {
                    case 1:
                        suffix = "st";
                        break;
                    case 2:
                        suffix = "nd";
                        break;
                    case 3:
                        suffix = "rd";
                        break;
                }
            }
            return Go.Itoa(n) + suffix;
        }
    }
}
