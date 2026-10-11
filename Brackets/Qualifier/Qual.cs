using System.Collections.Generic;

namespace Brackets
{
    // Port of internal/platform/qualbracket (qualbracket.go).

    /// <summary>QualConfig is the qualifier setup. PilotNames is the qualifying order — seed 1 first.</summary>
    public class QualConfig
    {
        public int PilotCount { get; set; }
        public List<string> PilotNames { get; set; }
        public bool CTA { get; set; }             // run the final as a chase-the-ace series
        public int AceWinsRequired { get; set; }  // CTA only; defaults to 2
        // PhotoSeed selects the 16-pilot round-1 seeding: false = MultiGP snake
        // (default), true = the photo's exact slot layout. Ignored for other sizes.
        public bool PhotoSeed { get; set; }
        // PilotsPerRace / Advance are the heat shape — 4-up/top-2 (the MultiGP
        // standard, and what a zero value means) or e.g. 6-up/top-3 for a Nationals
        // bracket. The terminal final takes Advance from each side, so it is up to
        // PilotsPerRace pilots and qualifies that many.
        public int PilotsPerRace { get; set; }
        public int Advance { get; set; }
        // Seats is the STRUCTURAL field size — the bracket is generated for this
        // many seed positions and the pilots occupy the top ones, the rest are
        // vacant seats (0 or < the roster = the roster size). The lowest main of a
        // mains progression uses it to run the exact same bracket as a full main:
        // a 40-pilot B Main in a 48-per-main competition is 8 heats of 5 with the
        // bottom seed's seat empty in each, not 7 heats of 5–6 with a ragged
        // redemption side. A seat that stays empty simply advances nobody, and an
        // entirely empty heat voids itself so the heats it feeds don't wait on it.
        public int Seats { get; set; }

        // seats returns the structural field size.
        internal int SeatsResolved()
        {
            if (Seats < Go.Len(PilotNames))
            {
                return Go.Len(PilotNames);
            }
            return Seats;
        }

        public QualConfig Clone()
        {
            return new QualConfig
            {
                PilotCount = PilotCount,
                PilotNames = PilotNames,
                CTA = CTA,
                AceWinsRequired = AceWinsRequired,
                PhotoSeed = PhotoSeed,
                PilotsPerRace = PilotsPerRace,
                Advance = Advance,
                Seats = Seats,
            };
        }
    }

    /// <summary>
    /// Qual is the running qualifier bracket. Its persisted state IS a BracketState
    /// (reusing the bracket Race/slot types) so the existing renderer, runner, ingest
    /// and finalise flow consume it unchanged. The spec is regenerated from cfg on
    /// load, so only the state blob needs persisting.
    /// </summary>
    public class Qual
    {
        internal QualConfig cfg;
        internal Spec spec;
        internal BracketState state;
        internal Dictionary<string, int> seed;                 // pilot name → 0-based qualifying seed
        internal Dictionary<string, List<SlotRef>> sources;    // race id → its source slot refs (body + finals/CTA)

        internal Qual()
        {
        }

        private static void Normalise(QualConfig cfg, List<string> names)
        {
            cfg.PilotNames = names;
            cfg.PilotCount = names.Count;
            if (cfg.AceWinsRequired < 1)
            {
                cfg.AceWinsRequired = 2;
            }
            int ppr = cfg.PilotsPerRace, adv = cfg.Advance;
            QualSpec.NormaliseShape(ref ppr, ref adv);
            cfg.PilotsPerRace = ppr;
            cfg.Advance = adv;
            cfg.Seats = cfg.SeatsResolved();
        }

        /// <summary>New generates a fresh qualifier from cfg.</summary>
        public static Qual New(QualConfig config)
        {
            QualConfig cfg = config.Clone();
            List<string> names = TrimNames(cfg.PilotNames);
            if (names.Count < 2)
            {
                throw new BracketException("qualbracket: need at least 2 pilots");
            }
            Normalise(cfg, names);
            Qual q = new Qual
            {
                cfg = cfg,
                spec = QualSpec.BuildSpec(cfg.Seats, cfg.CTA, cfg.PhotoSeed, cfg.PilotsPerRace, cfg.Advance),
                seed = SeedMap(names),
            };
            q.Generate();
            return q;
        }

        /// <summary>FromState rehydrates a qualifier from a persisted Config + State.</summary>
        public static Qual FromState(QualConfig config, BracketState st)
        {
            if (st == null)
            {
                throw new BracketException("qualbracket: state is nil");
            }
            QualConfig cfg = config.Clone();
            List<string> names = TrimNames(cfg.PilotNames);
            if (names.Count < 2)
            {
                throw new BracketException("qualbracket: need at least 2 pilots");
            }
            Normalise(cfg, names);
            Spec spec = QualSpec.BuildSpec(cfg.Seats, cfg.CTA, cfg.PhotoSeed, cfg.PilotsPerRace, cfg.Advance);
            if (!StateMatchesSpec(st, spec))
            {
                // The persisted state was generated under a different heat shape —
                // every bracket built before the shape was configurable is 4/2, and a
                // bracket whose row already said 6/3 (the columns were stored but
                // ignored) was materialised 4-up. Un-run: regenerate under the shape
                // asked for. Results in: keep the state and interpret it under the
                // shape it was built with, so nothing already raced is lost.
                if (!StateHasResults(st))
                {
                    Qual fresh = new Qual { cfg = cfg, spec = spec, seed = SeedMap(names) };
                    fresh.Generate();
                    return fresh;
                }
                // Legacy shapes, most recent first: the roster-sized bracket at this
                // shape (before seats), then the roster-sized 4/2 bracket.
                int[][] tries = new int[][]
                {
                    new int[] { names.Count, cfg.PilotsPerRace, cfg.Advance },
                    new int[] { names.Count, QualSpec.DefaultPilotsPerRace, QualSpec.DefaultAdvance },
                };
                foreach (int[] t in tries)
                {
                    Spec legacy = QualSpec.BuildSpec(t[0], cfg.CTA, cfg.PhotoSeed, t[1], t[2]);
                    if (StateMatchesSpec(st, legacy))
                    {
                        cfg.Seats = t[0];
                        cfg.PilotsPerRace = t[1];
                        cfg.Advance = t[2];
                        spec = legacy;
                        break;
                    }
                }
            }
            Qual q = new Qual { cfg = cfg, spec = spec, seed = SeedMap(names), state = st };
            q.BuildSources();
            return q;
        }

        // StateMatchesSpec reports whether a persisted state carries exactly the spec's
        // body races (by id) — the test for "was this state built from this spec".
        internal static bool StateMatchesSpec(BracketState st, Spec spec)
        {
            HashSet<string> have = new HashSet<string>();
            foreach (Race r in Go.Iter(st.Races))
            {
                have.Add(r.Id);
            }
            foreach (SpecRace sr in Go.Iter(spec.Body))
            {
                if (!have.Contains(sr.Id))
                {
                    return false;
                }
            }
            int body = 0;
            foreach (Race r in Go.Iter(st.Races))
            {
                if (r.Bracket != BracketSlot.ChaseTheAce && r.Id != "FINAL")
                {
                    body++;
                }
            }
            return body == Go.Len(spec.Body);
        }

        // StateHasResults reports whether any race has been run (a voided empty heat
        // is not a result).
        internal static bool StateHasResults(BracketState st)
        {
            foreach (Race r in Go.Iter(st.Races))
            {
                if (r.IsComplete && !r.IsVoid)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Config returns a BracketConfig view so the qualifier state can be wrapped in a
        /// throwaway Bracket for the shared SVG/grid renderer (which only reads
        /// state + PilotsToAdvance/FinalTarget for expected-winner counts).
        /// </summary>
        public BracketConfig Config()
        {
            return new BracketConfig
            {
                PilotCount = cfg.PilotCount,
                PilotNames = Go.Copy(cfg.PilotNames),
                EliminationType = EliminationType.Double,
                PilotsPerRace = cfg.PilotsPerRace,
                PilotsToAdvance = cfg.Advance,
                FinalsFormat = FinalsFormat.GrandFinal,
                AceWinsRequired = cfg.AceWinsRequired,
            };
        }

        /// <summary>State returns the live state.</summary>
        public BracketState State()
        {
            return state;
        }

        /// <summary>Cfg returns the qualifier's own config (for RestoreState).</summary>
        public QualConfig Cfg()
        {
            return cfg.Clone();
        }

        /// <summary>ExpectedWinners reports how many pilots advance/qualify out of a race.</summary>
        public int ExpectedWinners(Race r)
        {
            return ExpectedWinnersInternal(r);
        }

        /// <summary>Spec returns the resolved bracket spec (for predicted-slot labels).</summary>
        public Spec Spec()
        {
            return spec;
        }

        /// <summary>
        /// Sources returns each race's source slot refs (for predicted-slot labels in the
        /// runner grid). Round-1 races map to seed refs (FromId empty).
        /// </summary>
        public Dictionary<string, List<SlotRef>> Sources()
        {
            return sources;
        }

        // BuildSources indexes every race's source refs (body race sources, plus the
        // terminal final / CTA heats' finalist refs).
        private void BuildSources()
        {
            sources = new Dictionary<string, List<SlotRef>>();
            foreach (SpecRace sr in Go.Iter(spec.Body))
            {
                sources[sr.Id] = sr.Sources;
            }
            // Terminal final (non-CTA) and every CTA heat are fed by the finalist refs.
            if (!SpecHasTerminalInBody())
            {
                if (spec.CTA)
                {
                    foreach (Race r in Go.Iter(state.Races))
                    {
                        if (r.Bracket == BracketSlot.ChaseTheAce)
                        {
                            sources[r.Id] = spec.Finalists;
                        }
                    }
                }
                else
                {
                    sources["FINAL"] = spec.Finalists;
                }
            }
        }

        private bool SpecHasTerminalInBody()
        {
            foreach (SpecRace sr in Go.Iter(spec.Body))
            {
                if (sr.Terminal)
                {
                    return true;
                }
            }
            return false;
        }

        // Generate materialises the initial state from the spec.
        private void Generate()
        {
            BracketState st = new BracketState();
            foreach (SpecRace sr in Go.Iter(spec.Body))
            {
                Race r = new Race { Id = sr.Id, Round = sr.Round, Bracket = sr.Slot, FinalTarget = sr.FinalTarget };
                st.Races = Go.Append(st.Races, r);
            }
            int rounds = spec.FinalRound;
            if (!SpecHasTerminalInBody())
            {
                if (spec.CTA)
                {
                    int max = MaxCTARaces(FinalistCount(), cfg.AceWinsRequired);
                    for (int n = 1; n <= max; n++)
                    {
                        st.Races = Go.Append(st.Races, new Race
                        {
                            Id = "CTA-" + Go.Itoa(n),
                            Round = spec.FinalRound,
                            Bracket = BracketSlot.ChaseTheAce,
                        });
                    }
                    st.ChaseTheAce = new ChaseTheAceState
                    {
                        Wins = new GoMap<int>(),
                        Points = new GoMap<int>(),
                        RequiredWins = cfg.AceWinsRequired,
                    };
                }
                else
                {
                    st.Races = Go.Append(st.Races, new Race
                    {
                        Id = "FINAL",
                        Round = spec.FinalRound,
                        Bracket = BracketSlot.GrandFinal,
                        FinalTarget = FinalistCount(),
                    });
                }
            }
            foreach (Race r in Go.Iter(st.Races))
            {
                if (r.Round > rounds)
                {
                    rounds = r.Round;
                }
            }
            st.Rounds = rounds;
            state = st;
            BuildSources();
            Propagate();
        }

        // FinalistCount is how many pilots reach the terminal final (≤ pilots per race).
        private int FinalistCount()
        {
            int n = Go.Len(spec.Finalists);
            if (n > 0 && n < cfg.PilotsPerRace)
            {
                return n;
            }
            return Go.MinInt(cfg.PilotsPerRace, cfg.PilotCount);
        }

        internal Race FindRace(string id)
        {
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Id == id)
                {
                    return r;
                }
            }
            return null;
        }

        // FinishOrder returns a completed race's finishing order (best first). Full
        // placements carry the exact order in Pilots; partial entries rank the recorded
        // winners then the losers by seed (matching the seeds-win convention).
        internal List<string> FinishOrder(Race r)
        {
            if (!r.IsComplete)
            {
                return null;
            }
            if (r.FullPlacement)
            {
                return r.Pilots;
            }
            List<string> order = Go.Copy(r.Winners);
            List<string> losers = Go.Copy(r.Losers);
            if (losers != null)
            {
                Go.SortStable(losers, (a, b) => SeedOf(a) < SeedOf(b));
            }
            return Go.Append(order, losers);
        }

        internal int SeedOf(string name)
        {
            int s;
            if (name != null && seed.TryGetValue(name, out s))
            {
                return s;
            }
            return 1 << 30;
        }

        // SlotState is what a source slot resolves to.
        private enum SlotState
        {
            Pending, // the source race hasn't run yet
            Ready,   // a pilot
            Vacant,  // settled, and nobody: an unfilled seed seat, a
                     // finishing position a short heat never produced, or a voided empty heat
        }

        // Resolve returns the pilot occupying a source slot and its state.
        private SlotState Resolve(SlotRef rf, out string pilot)
        {
            pilot = "";
            if (string.IsNullOrEmpty(rf.FromId)) // Round-1 seed entry
            {
                if (rf.Position >= 1 && rf.Position <= Go.Len(cfg.PilotNames))
                {
                    pilot = cfg.PilotNames[rf.Position - 1];
                    return SlotState.Ready;
                }
                return SlotState.Vacant;
            }
            Race src = FindRace(rf.FromId);
            if (src == null)
            {
                return SlotState.Vacant;
            }
            if (src.IsVoid && !src.IsComplete)
            {
                return SlotState.Vacant; // an empty heat that will never run
            }
            if (!src.IsComplete)
            {
                return SlotState.Pending;
            }
            List<string> order = FinishOrder(src);
            if (rf.Position >= 1 && rf.Position <= Go.Len(order))
            {
                pilot = order[rf.Position - 1];
                return SlotState.Ready;
            }
            return SlotState.Vacant;
        }

        // Propagate (re)derives the pilot lineup of every incomplete race from its source
        // refs. A race whose sources all resolve is filled; otherwise it's cleared. This
        // is idempotent, so editing an upstream result cascades correctly. Repeats until
        // the state stops changing. The chase-the-ace finalists are kept in sync with
        // the winners/redemption finals until the series actually starts (see below).
        private void Propagate()
        {
            for (int pass = 0; pass < Go.Len(state.Races) + 2; pass++)
            {
                bool changed = false;
                foreach (Race r in Go.Iter(state.Races))
                {
                    if (r.IsComplete)
                    {
                        continue;
                    }
                    List<SlotRef> refs;
                    if (!sources.TryGetValue(r.Id, out refs))
                    {
                        continue;
                    }
                    List<string> filled = new List<string>();
                    bool pending = false;
                    foreach (SlotRef rf in Go.Iter(refs))
                    {
                        string p;
                        SlotState st = Resolve(rf, out p);
                        if (st == SlotState.Pending)
                        {
                            pending = true;
                            break;
                        }
                        if (st == SlotState.Ready)
                        {
                            filled.Add(p);
                        }
                    }
                    List<string> want = null;
                    if (!pending)
                    {
                        want = filled;
                    }
                    if (!Go.SameSlice(r.Pilots, want))
                    {
                        r.Pilots = want;
                        changed = true;
                    }
                    // A body heat whose every seat has settled EMPTY can never be run:
                    // void it so the heats it feeds see its slots as vacant instead of
                    // waiting on it forever (a short field padded to its main's seats).
                    // It un-voids if an upstream edit sends someone its way.
                    if (r.Bracket != BracketSlot.ChaseTheAce)
                    {
                        bool isVoid = !pending && Go.Len(refs) > 0 && Go.Len(want) == 0;
                        if (r.IsVoid != isVoid)
                        {
                            r.IsVoid = isVoid;
                            changed = true;
                        }
                    }
                }
                if (!changed)
                {
                    break;
                }
            }
            // Keep the chase-the-ace finalists in sync with the winners/redemption
            // finals UNTIL the series has actually started. The set resolves
            // automatically the moment both feeders complete, but a race director may
            // still need to correct a feeder result before any CTA heat is flown — so we
            // re-derive it on every propagate until the first CTA heat runs, then freeze
            // it (from then on editing a feeder is blocked because a completed CTA heat
            // carrying the finalists is a run downstream race). ResolvedFinalists returns
            // null while either feeder is incomplete, which clears a stale set when a
            // feeder is un-run.
            if (spec.CTA && state.ChaseTheAce != null && !CtaStarted())
            {
                state.ChaseTheAce.Finalists = ResolvedFinalists();
            }
        }

        // CtaStarted reports whether the chase-the-ace series has begun — at least one
        // finals heat has been run (or the champion is already decided). Until then the
        // finalist set is provisional and the winners/redemption finals stay editable.
        private bool CtaStarted()
        {
            ChaseTheAceState cta = state.ChaseTheAce;
            if (cta == null)
            {
                return false;
            }
            if (cta.IsComplete)
            {
                return true;
            }
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == BracketSlot.ChaseTheAce && r.IsComplete)
                {
                    return true;
                }
            }
            return false;
        }

        // ResolvedFinalists returns the terminal finalists once no finalist ref is
        // still pending, else null. A vacant slot (a feeder that ran short) is skipped.
        private List<string> ResolvedFinalists()
        {
            List<string> output = new List<string>();
            foreach (SlotRef rf in Go.Iter(spec.Finalists))
            {
                string p;
                SlotState st = Resolve(rf, out p);
                switch (st)
                {
                    case SlotState.Pending:
                        return null;
                    case SlotState.Ready:
                        output.Add(p);
                        break;
                }
            }
            return output;
        }

        // ExpectedWinnersInternal mirrors Bracket.ExpectedWinnersCount for the qualifier's races.
        private int ExpectedWinnersInternal(Race r)
        {
            if (r.FinalTarget > 0)
            {
                int t = r.FinalTarget;
                if (Go.Len(r.Pilots) > 0 && t > Go.Len(r.Pilots))
                {
                    t = Go.Len(r.Pilots);
                }
                return t;
            }
            if (r.Bracket == BracketSlot.ChaseTheAce)
            {
                return 1;
            }
            if (Go.Len(r.Pilots) == 0)
            {
                return cfg.Advance;
            }
            return Go.MinInt(cfg.Advance, Go.Len(r.Pilots));
        }

        /// <summary>ApplyPlacement records a race result (full finishing order) and advances state.</summary>
        public void ApplyPlacement(string raceId, IList<string> placement)
        {
            Race r = FindRace(raceId);
            if (r == null)
            {
                throw new BracketException("qualbracket: race " + Go.Quote(raceId) + " not found");
            }
            if (r.IsComplete)
            {
                throw new BracketException("qualbracket: race " + Go.Quote(raceId) + " already complete");
            }
            if (r.Bracket == BracketSlot.ChaseTheAce)
            {
                CheckCTAOrder(r);
            }
            Record(r, placement);
            if (r.Bracket == BracketSlot.ChaseTheAce)
            {
                AdvanceCTA(r);
            }
            Propagate();
        }

        // Record validates and writes a finishing order onto a race.
        private void Record(Race r, IList<string> placement)
        {
            int expected = ExpectedWinnersInternal(r);
            int count = placement == null ? 0 : placement.Count;
            if (count < expected)
            {
                throw new BracketException("qualbracket: race " + Go.Quote(r.Id) + " needs at least " + Go.Itoa(expected) + " positions, got " + Go.Itoa(count));
            }
            HashSet<string> pset = new HashSet<string>(Go.Iter(r.Pilots));
            bool full = count == Go.Len(r.Pilots);
            HashSet<string> seen = new HashSet<string>();
            foreach (string p in Go.Iter(placement))
            {
                if (!pset.Contains(p))
                {
                    throw new BracketException("qualbracket: " + Go.Quote(p) + " not in race " + Go.Quote(r.Id));
                }
                if (seen.Contains(p))
                {
                    throw new BracketException("qualbracket: duplicate " + Go.Quote(p));
                }
                seen.Add(p);
            }
            if (full)
            {
                r.Pilots = Go.Copy(placement);
            }
            if (expected > count)
            {
                expected = count;
            }
            List<string> top = new List<string>();
            for (int i = 0; i < expected; i++)
            {
                top.Add(placement[i]);
            }
            r.Winners = Go.Copy(top);
            HashSet<string> wset = new HashSet<string>(Go.Iter(r.Winners));
            r.Losers = Go.Truncate(r.Losers);
            foreach (string p in Go.Iter(r.Pilots))
            {
                if (!wset.Contains(p))
                {
                    r.Losers = Go.Append(r.Losers, p);
                }
            }
            r.FullPlacement = full;
            r.IsComplete = true;
        }

        // CheckCTAOrder enforces sequential chase-the-ace heats.
        private void CheckCTAOrder(Race r)
        {
            if (r.IsVoid)
            {
                throw new BracketException("qualbracket: chase-the-ace heat " + Go.Quote(r.Id) + " is void");
            }
            ChaseTheAceState cta = state.ChaseTheAce;
            if (cta != null && cta.IsComplete)
            {
                throw new BracketException("qualbracket: chase-the-ace already complete (champion " + Go.Quote(cta.Champion) + ")");
            }
            Race next = NextCTARace();
            if (next != null && next.Id != r.Id)
            {
                throw new BracketException("qualbracket: chase-the-ace heats run in order; " + Go.Quote(next.Id) + " is next, not " + Go.Quote(r.Id));
            }
        }

        internal Race NextCTARace()
        {
            List<Race> ctas = new List<Race>();
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == BracketSlot.ChaseTheAce)
                {
                    ctas.Add(r);
                }
            }
            Go.SortStable(ctas, (a, b) => CtaIndex(a.Id) < CtaIndex(b.Id));
            foreach (Race r in ctas)
            {
                if (!r.IsVoid && !r.IsComplete)
                {
                    return r;
                }
            }
            return null;
        }

        // AdvanceCTA accumulates wins/points and crowns the CTA champion (mirrors the
        // bracket engine's chase-the-ace scoring).
        private void AdvanceCTA(Race done)
        {
            ChaseTheAceState cta = state.ChaseTheAce;
            if (cta == null || Go.Len(done.Winners) == 0)
            {
                return;
            }
            if (cta.Points == null)
            {
                cta.Points = new GoMap<int>();
            }
            if (cta.Wins == null)
            {
                cta.Wins = new GoMap<int>();
            }
            if (done.FullPlacement)
            {
                int i = 0;
                foreach (string p in Go.Iter(done.Pilots))
                {
                    cta.Points[p] = cta.Points.Get(p) + i + 1;
                    i++;
                }
            }
            else
            {
                cta.Points[done.Winners[0]] = cta.Points.Get(done.Winners[0]) + 1;
            }
            string winner = done.Winners[0];
            cta.Wins[winner] = cta.Wins.Get(winner) + 1;
            if (cta.Wins[winner] >= cta.RequiredWins)
            {
                cta.Champion = winner;
                cta.IsComplete = true;
                ComputeCTAStandings();
                RemoveUnrunCTA();
            }
        }

        internal void ComputeCTAStandings()
        {
            ChaseTheAceState cta = state.ChaseTheAce;
            List<string> others = new List<string>();
            foreach (string p in Go.Iter(cta.Finalists))
            {
                if (p != cta.Champion)
                {
                    others.Add(p);
                }
            }
            // Fewest points first; a points tie goes to the higher qualifying seed
            // (the rule points-mains uses). Finalist order lists the winners side
            // first, so a stable sort alone handed every tie to the winners-bracket
            // pilot regardless of seed or the heats themselves.
            Go.SortStable(others, (a, b) =>
            {
                int pa = cta.Points.Get(a), pb = cta.Points.Get(b);
                if (pa != pb)
                {
                    return pa < pb;
                }
                return SeedOf(a) < SeedOf(b);
            });
            List<string> standings = new List<string> { cta.Champion };
            standings.AddRange(others);
            cta.Standings = standings;
        }

        private void RemoveUnrunCTA()
        {
            List<Race> kept = new List<Race>();
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == BracketSlot.ChaseTheAce && !r.IsComplete)
                {
                    continue;
                }
                kept.Add(r);
            }
            state.Races = state.Races == null ? null : kept;
        }

        /// <summary>CanEditRace reports whether a completed race may still be re-run.</summary>
        public bool CanEditRace(string raceId, out string reason)
        {
            Race r = FindRace(raceId);
            if (r == null)
            {
                reason = "race not found";
                return false;
            }
            if (!r.IsComplete)
            {
                reason = "";
                return true;
            }
            HashSet<string> placed = new HashSet<string>();
            foreach (string p in Go.Iter(r.Winners))
            {
                placed.Add(p);
            }
            foreach (string p in Go.Iter(r.Losers))
            {
                placed.Add(p);
            }
            foreach (Race other in Go.Iter(state.Races))
            {
                if (other.Id == raceId || other.Round <= r.Round || !other.IsComplete)
                {
                    continue;
                }
                foreach (string p in Go.Iter(other.Pilots))
                {
                    if (placed.Contains(p))
                    {
                        reason = "downstream race " + other.Id + " has already been run";
                        return false;
                    }
                }
            }
            // No separate chase-the-ace finalists guard: once the series has started, the
            // completed CTA heats (which carry the finalist roster) are caught by the
            // downstream-IsComplete loop above, so a feeder is correctly frozen. Before
            // the series starts the finalists are provisional (Propagate keeps them in
            // sync with the feeders), so a feeder may be corrected freely.
            reason = "";
            return true;
        }

        /// <summary>EditRace re-runs a completed race with a new placement, re-deriving downstream.</summary>
        public void EditRace(string raceId, IList<string> placement)
        {
            string reason;
            if (!CanEditRace(raceId, out reason))
            {
                throw new BracketException("qualbracket: cannot edit " + Go.Quote(raceId) + ": " + reason);
            }
            Race r = FindRace(raceId);
            if (r == null)
            {
                throw new BracketException("qualbracket: race " + Go.Quote(raceId) + " not found");
            }
            if (!r.IsComplete)
            {
                ApplyPlacement(raceId, placement);
                return;
            }
            r.Winners = null;
            r.Losers = null;
            r.IsComplete = false;
            r.FullPlacement = false;
            ApplyPlacement(raceId, placement);
        }

        /// <summary>
        /// ResetRace un-runs a completed race: clears its winners/losers/complete state
        /// and re-propagates, so downstream heats that depended on this result are
        /// recomputed (their pilots cleared until it's re-run). Refused when a downstream
        /// heat that received this result has already been run. No-op if not yet run.
        /// </summary>
        public void ResetRace(string raceId)
        {
            string reason;
            if (!CanEditRace(raceId, out reason))
            {
                throw new BracketException("qualbracket: cannot reset " + Go.Quote(raceId) + ": " + reason);
            }
            Race r = FindRace(raceId);
            if (r == null)
            {
                throw new BracketException("qualbracket: race " + Go.Quote(raceId) + " not found");
            }
            if (!r.IsComplete)
            {
                return;
            }
            r.Winners = null;
            r.Losers = null;
            r.IsComplete = false;
            r.FullPlacement = false;
            Propagate();
        }

        /// <summary>IsComplete reports whether the terminal final (or CTA series) is decided.</summary>
        public bool IsComplete()
        {
            if (spec.CTA && !SpecHasTerminalInBody())
            {
                return state.ChaseTheAce != null && state.ChaseTheAce.IsComplete;
            }
            Race t = TerminalRace();
            return t != null && t.IsComplete;
        }

        // TerminalRace returns the FINAL heat (or the tiny-field single race).
        private Race TerminalRace()
        {
            if (SpecHasTerminalInBody())
            {
                foreach (SpecRace sr in spec.Body)
                {
                    if (sr.Terminal)
                    {
                        return FindRace(sr.Id);
                    }
                }
            }
            return FindRace("FINAL");
        }

        /// <summary>
        /// Qualified returns the ranked top-N pilots who advance ("continue into the next
        /// bracket"). Empty until the final is decided.
        /// </summary>
        public List<string> Qualified()
        {
            if (spec.CTA && !SpecHasTerminalInBody())
            {
                ChaseTheAceState cta = state.ChaseTheAce;
                if (cta != null && cta.IsComplete)
                {
                    return Go.Copy(cta.Standings);
                }
                return null;
            }
            Race t = TerminalRace();
            if (t == null || !t.IsComplete)
            {
                return null;
            }
            return FinishOrder(t);
        }

        /// <summary>
        /// Ranking returns every pilot in competition finishing order: the qualified
        /// finalists first (in their final placing), then the remaining pilots ordered by
        /// how far they progressed — latest elimination first (a pilot who survived to a
        /// deeper round finishes higher), breaking ties by finishing position in their
        /// final heat, then by seed. Unlike a plain seed sort, this rewards a pilot who
        /// raced deep into the redemption bracket over a higher seed knocked out early.
        /// </summary>
        public List<string> Ranking()
        {
            List<string> output = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            foreach (string p in Go.Iter(Qualified()))
            {
                if (!seen.Contains(p))
                {
                    output.Add(p);
                    seen.Add(p);
                }
            }

            // For everyone else, find the deepest (highest-round) completed heat they
            // appeared in — for a finished qualifier that's the heat that eliminated them —
            // and their finishing position within it.
            List<Exit> rest = new List<Exit>();
            foreach (string name in Go.Iter(cfg.PilotNames))
            {
                if (seen.Contains(name))
                {
                    continue;
                }
                Exit e = new Exit { Name = name, Round = -1, Pos = 1 << 30 };
                foreach (Race r in Go.Iter(state.Races))
                {
                    if (!r.IsComplete)
                    {
                        continue;
                    }
                    int idx = IndexOf(FinishOrder(r), name);
                    if (idx >= 0 && r.Round > e.Round)
                    {
                        e.Round = r.Round;
                        e.Pos = idx;
                    }
                }
                rest.Add(e);
            }
            Go.SortStable(rest, (a, b) =>
            {
                if (a.Round != b.Round)
                {
                    return a.Round > b.Round; // deeper round ranks higher
                }
                if (a.Pos != b.Pos)
                {
                    return a.Pos < b.Pos; // better finish in the final heat
                }
                return SeedOf(a.Name) < SeedOf(b.Name);
            });
            foreach (Exit e in rest)
            {
                output.Add(e.Name);
            }
            return output;
        }

        private class Exit
        {
            public string Name;
            public int Round; // deepest completed-heat round; -1 if they never raced one
            public int Pos;   // 0-based finishing position in that heat; large if unknown
        }

        private static int IndexOf(List<string> ss, string want)
        {
            for (int i = 0; i < Go.Len(ss); i++)
            {
                if (ss[i] == want)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Simulate plays a fresh copy of this qualifier to completion with the
        /// best-seeded pilots winning every heat, returning the resulting state. Used by
        /// the structure preview so empty downstream races show the seed flow.
        /// </summary>
        public BracketState Simulate()
        {
            Qual sim;
            try
            {
                sim = New(cfg);
            }
            catch (BracketException)
            {
                return state;
            }
            for (int i = 0; i < 4000; i++)
            {
                Race pick = null;
                foreach (Race r in Go.Iter(sim.state.Races))
                {
                    if (r.IsComplete || r.IsVoid || Go.Len(r.Pilots) == 0)
                    {
                        continue;
                    }
                    if (pick == null || r.Round < pick.Round)
                    {
                        pick = r;
                    }
                }
                if (pick == null)
                {
                    break;
                }
                List<string> order = new List<string>(pick.Pilots);
                Go.SortStable(order, (a, b) => sim.SeedOf(a) < sim.SeedOf(b));
                try
                {
                    sim.ApplyPlacement(pick.Id, order);
                }
                catch (BracketException)
                {
                    break;
                }
            }
            return sim.state;
        }

        // ---- helpers ----

        internal static List<string> TrimNames(List<string> names)
        {
            List<string> output = new List<string>();
            foreach (string n in Go.Iter(names))
            {
                if (!string.IsNullOrEmpty(n))
                {
                    output.Add(n);
                }
            }
            return output;
        }

        internal static Dictionary<string, int> SeedMap(List<string> names)
        {
            Dictionary<string, int> m = new Dictionary<string, int>();
            for (int i = 0; i < names.Count; i++)
            {
                m[names[i]] = i;
            }
            return m;
        }

        internal static int CtaIndex(string id)
        {
            int n = 0;
            for (int i = id.Length - 1; i >= 0; i--)
            {
                if (id[i] == '-')
                {
                    n = Go.ScanInt(id.Substring(i + 1));
                    break;
                }
            }
            return n;
        }

        // MaxCTARaces is the worst-case number of chase-the-ace heats: every finalist
        // but one wins requiredWins-1 heats, then one decides it.
        internal static int MaxCTARaces(int finalistCount, int requiredWins)
        {
            if (finalistCount < 1)
            {
                finalistCount = 1;
            }
            if (requiredWins < 1)
            {
                requiredWins = 1;
            }
            return (requiredWins - 1) * finalistCount + 1;
        }
    }
}
