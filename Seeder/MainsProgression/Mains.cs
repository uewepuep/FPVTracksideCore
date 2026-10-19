using System.Collections.Generic;
using Newtonsoft.Json;
using QualEngine = SeederEngine.Qual;

namespace SeederEngine
{
    // Port of internal/platform/mainsbracket.
    //
    // The "Mains progression" race format: a stack of "Double Elimination — Top 4
    // Advance" qualifiers (one per Main, A = top / championship down to the lowest
    // letter). Pilots progress upward via "bump-up": the lowest main starts with the
    // bottom seeds of the field, and the top-N finishers of each main move UP into the
    // next, joining that main's resident pilots (positioned by their original
    // competition seed). The A-main winner is the champion.
    //
    // Each main is a Qual (reused wholesale — generation, propagation, chase-the-ace,
    // SVG, ingest, finalise). The only logic here is the cross-main bump-up cascade
    // and the edit/reset locking between mains.

    /// <summary>
    /// MainsProgressionConfig is the competition-wide setup (Go: mainsbracket.Config).
    /// It is persisted inside the state blob with Go's untagged field names.
    /// </summary>
    public class MainsProgressionConfig
    {
        [JsonProperty("PilotCount")]
        public int PilotCount { get; set; }

        [JsonProperty("PilotNames")]
        public List<string> PilotNames { get; set; } // FULL field in competition seed order, seed 1 first

        [JsonProperty("PilotsPerMain")]
        public int PilotsPerMain { get; set; } // running size of each main, e.g. 16

        [JsonProperty("BumpUp")]
        public int BumpUp { get; set; } // top-N that bump up into the next main, e.g. 4

        [JsonProperty("CTA")]
        public bool CTA { get; set; } // master toggle: run some mains' 4-pilot final as chase-the-ace

        [JsonProperty("AceWinsRequired")]
        public int AceWinsRequired { get; set; } // CTA only; defaults to 2

        [JsonProperty("PhotoSeed")]
        public bool PhotoSeed { get; set; } // per-main 16-pilot seed style (false snake / true photo)

        // CTAMains selects which mains run their final as chase-the-ace (when CTA is
        // on). Empty = the A Main only (the championship final); a list of labels =
        // exactly those; ["*"] = every main.
        [JsonProperty("CTAMains")]
        public List<string> CTAMains { get; set; }

        // PilotsPerRace / Advance are each main's heat shape (4-up/top-2 default;
        // 6-up/top-3 for a Nationals-style 48-pilot main). Passed to every main's
        // qualbracket; the bump-up count is independent of it.
        [JsonProperty("PilotsPerRace")]
        public int PilotsPerRace { get; set; }

        [JsonProperty("Advance")]
        public int Advance { get; set; }

        public MainsProgressionConfig Clone()
        {
            return new MainsProgressionConfig
            {
                PilotCount = PilotCount,
                PilotNames = PilotNames,
                PilotsPerMain = PilotsPerMain,
                BumpUp = BumpUp,
                CTA = CTA,
                AceWinsRequired = AceWinsRequired,
                PhotoSeed = PhotoSeed,
                CTAMains = CTAMains,
                PilotsPerRace = PilotsPerRace,
                Advance = Advance,
            };
        }
    }

    /// <summary>MainState is one main's persisted slice. State is null until Built.</summary>
    public class MainState
    {
        [JsonProperty("label")]
        public string Label { get; set; } = ""; // "A".."E"

        [JsonProperty("roster")]
        public List<string> Roster { get; set; } // seed-ordered lineup; residents-only before Built

        [JsonProperty("built")]
        public bool Built { get; set; } // true once the qualbracket has been materialised

        [JsonProperty("state")]
        public BracketState State { get; set; } // the built main's qualbracket state

        // Seeding is the round-1 layout the main was BUILT with ("photo" | "snake"),
        // recorded so reloading a part-raced bracket can never re-wire its round 1
        // under a different layout. "" = built before this was recorded: the main
        // follows cfg.PhotoSeed, which is how it was built.
        [JsonProperty("seeding", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Seeding { get; set; } = "";

        public bool ShouldSerializeSeeding()
        {
            return !string.IsNullOrEmpty(Seeding);
        }
    }

    /// <summary>MainsProgressionState is the whole persisted blob (Go: mainsbracket.State).</summary>
    public class MainsProgressionState
    {
        [JsonProperty("cfg")]
        public MainsProgressionConfig Cfg { get; set; } = new MainsProgressionConfig();

        [JsonProperty("mains")]
        public List<MainState> Mains { get; set; } // ordered A (index 0) … lowest (last)
    }

    /// <summary>RankRow is one pilot's place in the overall competition order.</summary>
    public class RankRow
    {
        public string Pilot { get; set; }
        public string Main { get; set; }
        public int MainRank { get; set; } // 1-based place within the main
    }

    /// <summary>MainView is one main's render info (A first). Qual is null for unbuilt mains.</summary>
    public class MainView
    {
        public string Label { get; set; }
        public bool Built { get; set; }
        public bool Complete { get; set; }
        public List<string> Roster { get; set; }
        public int Residents { get; set; }  // resident count (excludes bumped)
        public string BumpFrom { get; set; } // label of the main below (which bumps into this), "" for lowest
        public Qual Qual { get; set; }
    }

    /// <summary>Mains is the running competition (Go: mainsbracket.Mains).</summary>
    public class Mains
    {
        // IdSep separates a main label from the inner qualbracket race id, e.g. "A:W1-1".
        public const string IdSep = ":";

        internal MainsProgressionConfig cfg;
        internal MainsProgressionState state;
        internal Dictionary<string, QualEngine> quals; // label → interpreter (built mains only)
        internal Dictionary<string, int> seed;   // pilot → 0-based ORIGINAL competition seed

        private Mains()
        {
        }

        /// <summary>
        /// CTAForMain reports whether the given main runs its 4-pilot final as chase-the-
        /// ace. The master CTA toggle gates everything; CTAMains then selects which mains
        /// (empty = the A Main only by default, "*" = every main).
        /// </summary>
        public static bool CTAForMain(MainsProgressionConfig cfg, string label)
        {
            if (!cfg.CTA)
            {
                return false;
            }
            if (Go.Len(cfg.CTAMains) == 0)
            {
                return label == "A";
            }
            foreach (string l in cfg.CTAMains)
            {
                if (l == "*" || l == label)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// New generates a fresh competition from cfg. Only the lowest main is built
        /// immediately (its roster is final — nothing bumps into it); higher mains are
        /// built as the main below them finishes.
        /// </summary>
        public static Mains New(MainsProgressionConfig config)
        {
            MainsProgressionConfig cfg = NormaliseConfig(config);
            if (Go.Len(cfg.PilotNames) < 2)
            {
                throw new BracketException("mainsbracket: need at least 2 pilots");
            }
            if (cfg.BumpUp >= cfg.PilotsPerMain)
            {
                throw new BracketException("mainsbracket: bump-up must be smaller than pilots-per-main");
            }
            Dictionary<string, List<string>> residents;
            List<string> labels;
            ResidentRosters(cfg, out residents, out labels);
            MainsProgressionState st = new MainsProgressionState { Cfg = cfg };
            foreach (string lbl in labels)
            {
                st.Mains = Go.Append(st.Mains, new MainState { Label = lbl, Roster = residents[lbl] });
            }
            Mains m = new Mains { cfg = cfg, state = st, quals = new Dictionary<string, QualEngine>(), seed = SeedMap(cfg.PilotNames) };
            // Build the lowest main (last index) up front — no incoming bump.
            MainState low = st.Mains[st.Mains.Count - 1];
            m.BuildMain(st.Mains.Count - 1, low.Roster);
            return m;
        }

        /// <summary>FromState rehydrates a competition from a persisted State.</summary>
        public static Mains FromState(MainsProgressionConfig config, MainsProgressionState st)
        {
            if (st == null)
            {
                throw new BracketException("mainsbracket: state is nil");
            }
            MainsProgressionConfig cfg = NormaliseConfig(config);
            Mains m = new Mains { cfg = cfg, state = st, quals = new Dictionary<string, QualEngine>(), seed = SeedMap(cfg.PilotNames) };
            st.Cfg = cfg;
            foreach (MainState ms in Go.Iter(st.Mains))
            {
                if (ms.Built && ms.State != null)
                {
                    QualEngine q;
                    try
                    {
                        q = QualEngine.FromState(m.PerMainCfg(ms, ms.Roster), ms.State);
                    }
                    catch (BracketException e)
                    {
                        throw new BracketException("mainsbracket: rehydrate main " + ms.Label + ": " + e.Message);
                    }
                    m.quals[ms.Label] = q;
                }
            }
            return m;
        }

        private static MainsProgressionConfig NormaliseConfig(MainsProgressionConfig config)
        {
            MainsProgressionConfig cfg = config.Clone();
            cfg.PilotNames = TrimNames(cfg.PilotNames);
            cfg.PilotCount = cfg.PilotNames.Count;
            if (cfg.PilotsPerMain < 2)
            {
                cfg.PilotsPerMain = 16;
            }
            if (cfg.BumpUp < 1)
            {
                cfg.BumpUp = 4;
            }
            if (cfg.AceWinsRequired < 1)
            {
                cfg.AceWinsRequired = 2;
            }
            if (cfg.PilotsPerRace < 2 || cfg.PilotsPerRace > 8)
            {
                cfg.PilotsPerRace = QualSpec.DefaultPilotsPerRace;
            }
            if (cfg.Advance < 1)
            {
                cfg.Advance = QualSpec.DefaultAdvance;
            }
            if (cfg.Advance >= cfg.PilotsPerRace)
            {
                cfg.Advance = cfg.PilotsPerRace - 1;
            }
            return cfg;
        }

        // State / Cfg accessors.
        public MainsProgressionState State()
        {
            return state;
        }

        public MainsProgressionConfig Cfg()
        {
            return cfg.Clone();
        }

        public int NumMains()
        {
            return Go.Len(state.Mains);
        }

        // PerMainCfg builds a qualbracket config for one main from its (final) roster.
        // CTA is decided per main (CTAForMain) so chase-the-ace can be limited to, e.g.,
        // just the A Main championship final. The round-1 layout is the one the main
        // was built with (ms.Seeding — see PhotoSeedFor).
        internal QualConfig PerMainCfg(MainState ms, List<string> roster)
        {
            bool photo = cfg.PhotoSeed; // legacy: built before Seeding was recorded
            if (!string.IsNullOrEmpty(ms.Seeding))
            {
                photo = ms.Seeding == "photo";
            }
            return new QualConfig
            {
                PilotNames = Go.Copy(roster),
                CTA = CTAForMain(cfg, ms.Label),
                AceWinsRequired = cfg.AceWinsRequired,
                PhotoSeed = photo && Go.Len(roster) == 16,
                PilotsPerRace = cfg.PilotsPerRace,
                Advance = cfg.Advance,
                Seats = SeatsFor(ms.Label),
            };
        }

        // PhotoSeedFor decides the round-1 layout for a main being built now.
        //
        // A main that RECEIVES bump-ups always seeds snake. The photo layout ("AUFPV —
        // Standard Seed") is a fixed 16-slot sheet that puts seeds 13 and 14 in the
        // same heat and the top four two-per-heat (1 with 4, 2 with 3). In a main whose
        // bottom four seeds are the pilots bumped up from below, that put two bump-ups
        // in one heat and paired the top qualifiers off to eliminate each other — the
        // 2026 Nationals Pro Spec A/B Mains (bracket 104), fixed on the night by
        // switching the bracket to snake. Snake gives every round-1 heat exactly one
        // top-4 seed and one bump-up. A main with nothing coming in (the lowest, or a
        // single main) still honours the director's photo choice.
        private bool PhotoSeedFor(int idx)
        {
            return cfg.PhotoSeed && !ReceivesBump(idx);
        }

        /// <summary>
        /// MainReceivesBump reports whether the main at idx (0 = A) has bump-ups coming
        /// in from below — every main except the lowest, when there is more than one.
        /// </summary>
        public static bool MainReceivesBump(int idx, int numMains, int bumpUp)
        {
            return numMains > 1 && bumpUp > 0 && idx < numMains - 1;
        }

        private bool ReceivesBump(int idx)
        {
            return MainReceivesBump(idx, Go.Len(state.Mains), cfg.BumpUp);
        }

        // SeatsFor is a main's structural size. Every main above the lowest is full by
        // construction (residents + bump-up = PilotsPerMain). The lowest main is the
        // only one that can be short, and it runs the SAME bracket as a full main —
        // PilotsPerMain seats, the missing bottom seeds left empty — so the race
        // director's B Main has exactly the A Main's heats (the ask: "leave unoccupied
        // seats for the missing seed positions"), not a ragged smaller bracket.
        private int SeatsFor(string label)
        {
            if (state == null || Go.Len(state.Mains) < 2)
            {
                return 0;
            }
            if (label == state.Mains[state.Mains.Count - 1].Label)
            {
                return cfg.PilotsPerMain;
            }
            return 0;
        }

        // BuildMain materialises a main's qualbracket from its final roster.
        private void BuildMain(int idx, List<string> roster)
        {
            MainState ms = state.Mains[idx];
            ms.Seeding = "snake";
            if (PhotoSeedFor(idx))
            {
                ms.Seeding = "photo";
            }
            QualEngine q;
            try
            {
                q = QualEngine.New(PerMainCfg(ms, roster));
            }
            catch (BracketException e)
            {
                throw new BracketException("mainsbracket: build main " + ms.Label + ": " + e.Message);
            }
            ms.Roster = Go.Copy(roster);
            ms.Built = true;
            ms.State = q.State();
            quals[ms.Label] = q;
        }

        // TeardownMain un-builds a main back to its residents (used when the main below
        // is reset/edited so its bump set is no longer valid).
        private void TeardownMain(int idx)
        {
            MainState ms = state.Mains[idx];
            ms.Built = false;
            ms.State = null;
            ms.Seeding = ""; // decided afresh when it's rebuilt
            ms.Roster = ResidentsOf(cfg, idx);
            quals.Remove(ms.Label);
        }

        // ── Seeding math ────────────────────────────────────────────────────────────

        // MainCount returns the number of mains for the field: every main above the
        // lowest holds (PilotsPerMain−BumpUp) residents so it runs a FULL PilotsPerMain
        // once the bump-up arrives, and the lowest main holds whatever is left (at most
        // PilotsPerMain) — so this is the smallest M whose resident capacity covers the
        // field.
        internal static int MainCount(int pilotCount, int pilotsPerMain, int bumpUp)
        {
            if (pilotCount <= pilotsPerMain)
            {
                return 1;
            }
            int step = pilotsPerMain - bumpUp; // residents added by each higher main
            int m = 1;
            int cap = pilotsPerMain;
            while (cap < pilotCount)
            {
                m++;
                cap += step;
            }
            return m;
        }

        // MainLabels returns ["A","B",…] truncated to n (capped at 26).
        internal static List<string> MainLabels(int n)
        {
            if (n > 26)
            {
                n = 26;
            }
            List<string> output = new List<string>();
            for (int i = 0; i < n; i++)
            {
                output.Add(((char)('A' + i)).ToString());
            }
            return output;
        }

        // ResidentRosters assigns residents by seed: A (index 0) gets the best residents,
        // every main above the lowest gets (PilotsPerMain−BumpUp), and the LOWEST main
        // (last index) takes the bottom seeds — however many are left over.
        internal static void ResidentRosters(MainsProgressionConfig cfg, out Dictionary<string, List<string>> output, out List<string> labels)
        {
            int n = Go.Len(cfg.PilotNames);
            int M = MainCount(n, cfg.PilotsPerMain, cfg.BumpUp);
            if (M > 26)
            {
                throw new BracketException("mainsbracket: too many mains (max 26)");
            }
            labels = MainLabels(M);
            output = new Dictionary<string, List<string>>();
            if (M == 1)
            {
                output[labels[0]] = Go.Copy(cfg.PilotNames);
                return;
            }
            int idx = 0;
            for (int i = 0; i < M; i++)
            {
                int size = ResidentSize(cfg, i, M);
                if (idx + size > n)
                {
                    size = n - idx;
                }
                output[labels[i]] = Go.Copy(cfg.PilotNames.GetRange(idx, size));
                idx += size;
            }
        }

        // ResidentSize is main i's resident count: every main above the lowest gets
        // (PilotsPerMain−BumpUp), and the LOWEST main absorbs the remainder.
        //
        // A short field has to land somewhere, and it belongs at the BOTTOM. Every main
        // above the lowest races its residents PLUS the bump-up from below, so giving
        // them all (PilotsPerMain−BumpUp) makes each of them run a full PilotsPerMain —
        // the A Main championship included. Putting the remainder in the TOP main (what
        // this used to do) starved the championship instead: a 37-pilot / 16-per-main /
        // bump-4 field gave A nine residents, so the A Main decided the competition over
        // 13 pilots while the C Main — the one nobody's title depends on — ran a full 16.
        // It could also OVERFILL the top main, since remainder + BumpUp has no reason to
        // land on PilotsPerMain.
        //
        // The lowest main is the only one with nothing feeding it, so it is the only one
        // that can be short without unbalancing anything. MainCount's minimality makes
        // the remainder strictly greater than BumpUp and at most PilotsPerMain, so the
        // lowest main always has more pilots than it bumps up.
        internal static int ResidentSize(MainsProgressionConfig cfg, int i, int M)
        {
            if (M == 1)
            {
                return Go.Len(cfg.PilotNames);
            }
            int step = cfg.PilotsPerMain - cfg.BumpUp;
            if (i == M - 1) // lowest main takes whatever is left
            {
                return Go.Len(cfg.PilotNames) - (M - 1) * step;
            }
            return step;
        }

        // ResidentsOf recomputes a main's resident slice (deterministic from cfg).
        internal static List<string> ResidentsOf(MainsProgressionConfig cfg, int idx)
        {
            Dictionary<string, List<string>> residents;
            List<string> labels;
            try
            {
                ResidentRosters(cfg, out residents, out labels);
            }
            catch (BracketException)
            {
                return null;
            }
            if (idx < 0 || idx >= labels.Count)
            {
                return null;
            }
            return residents[labels[idx]];
        }

        // ── Race routing + bump-up cascade ──────────────────────────────────────────

        /// <summary>SplitRaceID splits a prefixed id "A:W1-1" into ("A", "W1-1").</summary>
        public static void SplitRaceID(string id, out string label, out string inner)
        {
            int i = id.IndexOf(IdSep, System.StringComparison.Ordinal);
            if (i >= 0)
            {
                label = id.Substring(0, i);
                inner = id.Substring(i + 1);
                return;
            }
            label = "";
            inner = id;
        }

        internal int IndexOf(string label)
        {
            for (int i = 0; i < Go.Len(state.Mains); i++)
            {
                if (state.Mains[i].Label == label)
                {
                    return i;
                }
            }
            return -1;
        }

        private QualEngine QualOf(string label)
        {
            QualEngine q;
            if (label != null && quals.TryGetValue(label, out q))
            {
                return q;
            }
            return null;
        }

        /// <summary>
        /// ApplyPlacement records a result on the owning main and runs the bump-up
        /// cascade (when that main completes, build the main above it).
        /// </summary>
        public void ApplyPlacement(string prefixedId, IList<string> placement)
        {
            string label, inner;
            SplitRaceID(prefixedId, out label, out inner);
            QualEngine q = QualOf(label);
            if (q == null)
            {
                throw new BracketException("mainsbracket: main " + Go.Quote(label) + " not in play");
            }
            bool wasComplete = q.IsComplete();
            q.ApplyPlacement(inner, placement);
            state.Mains[IndexOf(label)].State = q.State();
            if (!wasComplete && q.IsComplete())
            {
                BumpInto(IndexOf(label));
            }
        }

        // BumpInto builds the main ABOVE main idx (idx-1) from idx's top-BumpUp finishers,
        // re-seeded by original competition seed.
        private void BumpInto(int idx)
        {
            if (idx <= 0) // A (index 0) is the top — nobody above
            {
                return;
            }
            int above = idx - 1;
            if (state.Mains[above].Built)
            {
                return; // already built (handled by the edit/reset paths)
            }
            List<string> bumped = BumpSet(idx);
            List<string> roster = Go.Append(Go.Copy(ResidentsOf(cfg, above)), bumped);
            try
            {
                BuildMain(above, roster);
            }
            catch (BracketException)
            {
                // Go: `_ = m.buildMain(above, roster)` — the error is ignored.
            }
        }

        // BumpSet returns main idx's top-BumpUp finishers sorted by ORIGINAL competition
        // seed (they always seed below the higher main's residents).
        private List<string> BumpSet(int idx)
        {
            QualEngine q = QualOf(state.Mains[idx].Label);
            if (q == null)
            {
                return null;
            }
            List<string> bumped = q.Qualified();
            if (Go.Len(bumped) > cfg.BumpUp)
            {
                bumped = bumped.GetRange(0, cfg.BumpUp);
            }
            List<string> output = Go.Copy(bumped);
            if (output != null)
            {
                Go.SortStable(output, (a, b) => SeedOfPilot(a) < SeedOfPilot(b));
            }
            return output;
        }

        private int SeedOfPilot(string p)
        {
            int s;
            if (p != null && seed.TryGetValue(p, out s))
            {
                return s;
            }
            return 0; // Go map zero value
        }

        /// <summary>
        /// CanEditRace gates an edit/reset: the intra-main qualbracket rule, plus a
        /// conservative cross-main lock — a completed lower main can't be edited once the
        /// main above it has started racing (reset that one first).
        /// </summary>
        public bool CanEditRace(string prefixedId, out string reason)
        {
            string label, inner;
            SplitRaceID(prefixedId, out label, out inner);
            QualEngine q = QualOf(label);
            if (q == null)
            {
                reason = "main not in play";
                return false;
            }
            string r;
            if (!q.CanEditRace(inner, out r))
            {
                reason = r;
                return false;
            }
            int idx = IndexOf(label);
            if (idx > 0 && q.IsComplete())
            {
                QualEngine above = QualOf(state.Mains[idx - 1].Label);
                if (above != null && HasStarted(above.State()))
                {
                    reason = "the " + state.Mains[idx - 1].Label + " Main has already started — reset it first";
                    return false;
                }
            }
            reason = "";
            return true;
        }

        /// <summary>
        /// EditRace re-runs a completed race; if it changes the bump set and the main
        /// above is built-but-not-started, that main is rebuilt from the new bump.
        /// </summary>
        public void EditRace(string prefixedId, IList<string> placement)
        {
            string reason;
            if (!CanEditRace(prefixedId, out reason))
            {
                throw new BracketException("mainsbracket: " + reason);
            }
            string label, inner;
            SplitRaceID(prefixedId, out label, out inner);
            QualEngine q = QualOf(label);
            int idx = IndexOf(label);
            List<string> prevBump = null;
            if (q.IsComplete())
            {
                prevBump = BumpSet(idx);
            }
            q.EditRace(inner, placement);
            state.Mains[idx].State = q.State();
            ReconcileAbove(idx, prevBump);
        }

        /// <summary>
        /// ResetRace clears a completed race; if the main is no longer complete, the main
        /// above (which depended on its bump) is torn down.
        /// </summary>
        public void ResetRace(string prefixedId)
        {
            string reason;
            if (!CanEditRace(prefixedId, out reason))
            {
                throw new BracketException("mainsbracket: " + reason);
            }
            string label, inner;
            SplitRaceID(prefixedId, out label, out inner);
            QualEngine q = QualOf(label);
            int idx = IndexOf(label);
            List<string> prevBump = null;
            if (q.IsComplete())
            {
                prevBump = BumpSet(idx);
            }
            q.ResetRace(inner);
            state.Mains[idx].State = q.State();
            ReconcileAbove(idx, prevBump);
        }

        // ReconcileAbove fixes the main above idx after idx's result changed: rebuild it
        // from the new bump if idx is still complete and the bump changed; tear it down
        // if idx is no longer complete. Recurses (a rebuilt/torn-down higher main was, by
        // the edit lock, not yet started, so the cascade is shallow).
        private void ReconcileAbove(int idx, List<string> prevBump)
        {
            if (idx <= 0)
            {
                return;
            }
            int above = idx - 1;
            QualEngine q = QualOf(state.Mains[idx].Label);
            if (q == null) // idx isn't built → it never built anything above it
            {
                return;
            }
            if (q.IsComplete())
            {
                List<string> newBump = BumpSet(idx);
                if (state.Mains[above].Built && Go.SameSlice(prevBump, newBump))
                {
                    return; // bump unchanged — nothing to do
                }
                List<string> roster = Go.Append(Go.Copy(ResidentsOf(cfg, above)), newBump);
                try
                {
                    BuildMain(above, roster);
                }
                catch (BracketException)
                {
                    // Go: `_ = m.buildMain(above, roster)` — the error is ignored.
                }
            }
            else if (state.Mains[above].Built)
            {
                TeardownMain(above);
            }
            ReconcileAbove(above, null); // a freshly (re)built/torn-down main isn't complete → shallow
        }

        // ── Status + ranking ────────────────────────────────────────────────────────

        /// <summary>IsComplete reports whether every main is built and complete.</summary>
        public bool IsComplete()
        {
            foreach (MainState ms in Go.Iter(state.Mains))
            {
                QualEngine q = QualOf(ms.Label);
                if (q == null || !q.IsComplete())
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Champion returns the A-main winner once A is complete, else "".</summary>
        public string Champion()
        {
            if (Go.Len(state.Mains) == 0)
            {
                return "";
            }
            QualEngine q = QualOf(state.Mains[0].Label);
            if (q == null || !q.IsComplete())
            {
                return "";
            }
            List<string> order = q.Qualified();
            if (Go.Len(order) > 0)
            {
                return order[0];
            }
            return "";
        }

        /// <summary>
        /// Ranking returns the overall competition order: Main A's full ranking first,
        /// then B, then C … For a complete main, the qualified top-N lead, then the
        /// remaining pilots by original seed. Pilots in unbuilt/incomplete mains are
        /// ranked by seed within their main.
        /// </summary>
        public List<RankRow> Ranking()
        {
            // A pilot belongs to their HIGHEST main (the one they bumped up to); they're
            // ranked there once, not in every lower main they passed through.
            Dictionary<string, int> home = new Dictionary<string, int>();
            for (int i = 0; i < Go.Len(state.Mains); i++)
            {
                foreach (string p in Go.Iter(state.Mains[i].Roster))
                {
                    if (!home.ContainsKey(p))
                    {
                        home[p] = i;
                    }
                }
            }
            List<RankRow> output = null;
            for (int i = 0; i < Go.Len(state.Mains); i++)
            {
                MainState ms = state.Mains[i];
                int rank = 0;
                foreach (string p in MainRanking(ms))
                {
                    int h;
                    if (!home.TryGetValue(p, out h))
                    {
                        h = 0; // Go map zero value
                    }
                    if (h != i)
                    {
                        continue; // ranked in a higher main
                    }
                    rank++;
                    output = Go.Append(output, new RankRow { Pilot = p, Main = ms.Label, MainRank = rank });
                }
            }
            return output;
        }

        // MainRanking returns a main's full pilot order. While the main is still running
        // (or unbuilt) we can only show the roster by seed. Once it's complete the
        // qualifier ranks every pilot — the qualified leaders first, then the rest by how
        // far they progressed in the bracket (latest elimination first), not by seed — so
        // a pilot who raced deep into the redemption bracket places above a higher seed
        // knocked out early.
        private List<string> MainRanking(MainState ms)
        {
            List<string> roster = ms.Roster;
            QualEngine q = QualOf(ms.Label);
            if (q == null || !q.IsComplete())
            {
                List<string> sorted = new List<string>(Go.Iter(roster));
                Go.SortStable(sorted, (a, b) => SeedOfPilot(a) < SeedOfPilot(b));
                return sorted;
            }
            // q.Ranking() is keyed on the main's roster and returns it in finishing order.
            List<string> order = q.Ranking();
            HashSet<string> seen = new HashSet<string>();
            List<string> output = new List<string>();
            foreach (string p in order)
            {
                if (!seen.Contains(p))
                {
                    output.Add(p);
                    seen.Add(p);
                }
            }
            // Defensive: append any roster pilot the qualifier didn't rank (shouldn't
            // happen for a complete main) so nobody silently drops out of the standings.
            foreach (string p in Go.Iter(roster))
            {
                if (!seen.Contains(p))
                {
                    output.Add(p);
                    seen.Add(p);
                }
            }
            return output;
        }

        // ── Rendering accessors ─────────────────────────────────────────────────────

        /// <summary>MainViews returns the mains A-first for the tabbed runner.</summary>
        public List<MainView> MainViews()
        {
            List<MainView> output = new List<MainView>();
            for (int i = 0; i < Go.Len(state.Mains); i++)
            {
                MainState ms = state.Mains[i];
                string below = "";
                if (i < state.Mains.Count - 1)
                {
                    below = state.Mains[i + 1].Label;
                }
                QualEngine q = QualOf(ms.Label);
                bool complete = q != null && q.IsComplete();
                output.Add(new MainView
                {
                    Label = ms.Label,
                    Built = ms.Built,
                    Complete = complete,
                    Roster = Go.Copy(ms.Roster),
                    Residents = Go.Len(ResidentsOf(cfg, i)),
                    BumpFrom = below,
                    Qual = q,
                });
            }
            return output;
        }

        /// <summary>Qual returns a built main's interpreter (null if unbuilt) for rendering/lookup.</summary>
        public QualEngine Qual(string label)
        {
            return QualOf(label);
        }

        // ── Simulate (for the setup preview) ────────────────────────────────────────

        /// <summary>
        /// Simulate plays a fresh copy of the whole competition seeds-win to completion,
        /// returning the resulting State so the preview shows every main filled.
        /// </summary>
        public MainsProgressionState Simulate()
        {
            Mains sim;
            try
            {
                sim = New(cfg);
            }
            catch (BracketException)
            {
                return state;
            }
            for (int i = 0; i < 20000; i++)
            {
                string label, inner;
                List<string> order;
                if (!sim.NextSeedsWinRace(out label, out inner, out order))
                {
                    break;
                }
                try
                {
                    sim.ApplyPlacement(label + IdSep + inner, order);
                }
                catch (BracketException)
                {
                    break;
                }
            }
            return sim.state;
        }

        // NextSeedsWinRace picks the lowest-round runnable race across all built mains
        // and returns its seeds-win finishing order (best original seed first).
        internal bool NextSeedsWinRace(out string label, out string inner, out List<string> order)
        {
            string pickLabel = "";
            Race pick = null;
            foreach (MainState ms in Go.Iter(state.Mains))
            {
                QualEngine q = QualOf(ms.Label);
                if (q == null)
                {
                    continue;
                }
                foreach (Race r in Go.Iter(q.State().Races))
                {
                    if (r.IsComplete || r.IsVoid || Go.Len(r.Pilots) == 0)
                    {
                        continue;
                    }
                    if (pick == null || r.Round < pick.Round)
                    {
                        pick = r;
                        pickLabel = ms.Label;
                    }
                }
            }
            if (pick == null)
            {
                label = "";
                inner = "";
                order = null;
                return false;
            }
            order = new List<string>(pick.Pilots);
            Go.SortStable(order, (a, b) => SeedOfPilot(a) < SeedOfPilot(b));
            label = pickLabel;
            inner = pick.Id;
            return true;
        }

        // ── small helpers ───────────────────────────────────────────────────────────

        private static bool HasStarted(BracketState st)
        {
            if (st == null)
            {
                return false;
            }
            foreach (Race r in Go.Iter(st.Races))
            {
                if (r.IsComplete)
                {
                    return true;
                }
            }
            return false;
        }

        internal static List<string> TrimNames(List<string> names)
        {
            List<string> output = new List<string>();
            foreach (string n in Go.Iter(names))
            {
                if (Go.TrimSpace(n) != "")
                {
                    output.Add(n);
                }
            }
            return output;
        }

        private static Dictionary<string, int> SeedMap(List<string> names)
        {
            Dictionary<string, int> output = new Dictionary<string, int>();
            for (int i = 0; i < Go.Len(names); i++)
            {
                output[names[i]] = i;
            }
            return output;
        }
    }
}
