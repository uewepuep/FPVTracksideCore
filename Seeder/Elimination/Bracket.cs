using System;
using System.Collections.Generic;

namespace SeederEngine
{
    /// <summary>
    /// Bracket couples a BracketConfig with its mutable BracketState. Port of
    /// internal/platform/bracket.Bracket.
    /// </summary>
    public class Bracket
    {
        private BracketConfig cfg;
        private BracketState state;
        private Dictionary<string, int> seedMap; // pilot name → absolute qualifying seed (0-indexed)

        private Bracket()
        {
        }

        /// <summary>
        /// New creates a Bracket from cfg and generates the initial race structure.
        /// Throws BracketException if the config is invalid.
        /// </summary>
        public static Bracket New(BracketConfig config)
        {
            BracketConfig c = config.Clone();
            ValidateConfig(c);
            Bracket b = new Bracket { cfg = c, state = new BracketState(), seedMap = BuildSeedMap(c.PilotNames) };
            b.Generate();
            return b;
        }

        /// <summary>
        /// FromState rebuilds a Bracket from a persisted Config + State (e.g. loaded from
        /// the database). No regeneration happens — the state is used as-is.
        /// </summary>
        public static Bracket FromState(BracketConfig config, BracketState st)
        {
            BracketConfig c = config.Clone();
            ValidateConfig(c);
            if (st == null)
            {
                throw new BracketException("bracket: state is nil");
            }
            return new Bracket { cfg = c, state = st, seedMap = BuildSeedMap(c.PilotNames) };
        }

        // BuildSeedMap derives the pilot-name → absolute-seed lookup from the
        // qualifying order in cfg.PilotNames. Seed 0 = pilot 1 (the top qualifier).
        private static Dictionary<string, int> BuildSeedMap(List<string> pilots)
        {
            Dictionary<string, int> m = new Dictionary<string, int>();
            int i = 0;
            foreach (string p in Go.Iter(pilots))
            {
                m[p] = i;
                i++;
            }
            return m;
        }

        // SeedOf returns the absolute qualifying seed for a pilot name, or a large
        // sentinel if the pilot isn't known (defensive — keeps unknown pilots last
        // in placement-order sorting rather than panicking).
        internal int SeedOf(string pilot)
        {
            int s;
            if (pilot != null && seedMap.TryGetValue(pilot, out s))
            {
                return s;
            }
            return 1 << 30;
        }

        /// <summary>Config returns a copy of the config.</summary>
        public BracketConfig Config()
        {
            return cfg.Clone();
        }

        /// <summary>State returns the live state. Mutating the returned value mutates the bracket.</summary>
        public BracketState State()
        {
            return state;
        }

        internal static void ValidateConfig(BracketConfig cfg)
        {
            if (cfg.PilotCount < 2)
            {
                throw new BracketException("bracket: need at least 2 pilots");
            }
            if (cfg.PilotsPerRace < 2 || cfg.PilotsPerRace > 8)
            {
                throw new BracketException("bracket: pilotsPerRace must be between 2 and 8");
            }
            if (cfg.PilotsToAdvance < 1 || cfg.PilotsToAdvance >= cfg.PilotsPerRace)
            {
                throw new BracketException("bracket: pilotsToAdvance must be ≥1 and < pilotsPerRace");
            }
            // Trim/validate pilot names
            List<string> names = new List<string>();
            foreach (string n in Go.Iter(cfg.PilotNames))
            {
                string t = Go.TrimSpace(n);
                if (t != "")
                {
                    names.Add(t);
                }
            }
            if (names.Count < 2)
            {
                throw new BracketException("bracket: need at least 2 non-empty pilot names");
            }
            if (names.Count > cfg.PilotCount)
            {
                names = names.GetRange(0, cfg.PilotCount);
            }
            cfg.PilotCount = names.Count;
            cfg.PilotNames = names;
            if (string.IsNullOrEmpty(cfg.EliminationType))
            {
                cfg.EliminationType = EliminationType.Double;
            }
            if (string.IsNullOrEmpty(cfg.FinalsFormat))
            {
                cfg.FinalsFormat = FinalsFormat.GrandFinal;
            }
            if (cfg.FinalsFormat == FinalsFormat.ChaseTheAce && cfg.AceWinsRequired < 1)
            {
                cfg.AceWinsRequired = 2;
            }
        }

        // ---------- structural calculations ----------

        internal struct RoundInfo
        {
            public int Round;
            public int Pilots;
            public int Races;
            public int Advancing;
            public int Losers;
        }

        // CalculateBracketStructure walks the winners bracket until pilots ≤ target. Each
        // entry is one round with the real pilot count, race count, advancing and losers.
        internal static List<RoundInfo> CalculateBracketStructure(int totalPilots, int pilotsPerRace, int pilotsToAdvance, int targetFinalists)
        {
            if (targetFinalists < 1)
            {
                targetFinalists = pilotsToAdvance;
            }
            List<RoundInfo> output = new List<RoundInfo>();
            int current = totalPilots;
            int round = 0;
            while (current > targetFinalists)
            {
                round++;
                int races = Go.CeilDiv(current, pilotsPerRace);
                int advancing = races * pilotsToAdvance;
                if (advancing > current)
                {
                    advancing = current;
                }
                output.Add(new RoundInfo { Round = round, Pilots = current, Races = races, Advancing = advancing, Losers = current - advancing });
                if (advancing >= current)
                {
                    break; // no progress
                }
                current = advancing;
                if (round > 20)
                {
                    break;
                }
            }
            return output;
        }

        // CalculateRoundsNeeded returns how many winners-bracket rounds it takes to
        // reduce startingPilots down to targetPilots, stopping when the bracket math
        // can't make further progress (next round would have the same pilot count as
        // the current one — e.g. a 2-race round of 2-pilot heats with advance=2 just
        // passes everyone forward).
        internal static int CalculateRoundsNeeded(int startingPilots, int targetPilots, int pilotsPerRace, int pilotsToAdvance)
        {
            if (startingPilots <= targetPilots)
            {
                return 0;
            }
            int rounds = 0;
            int current = startingPilots;
            while (current > targetPilots)
            {
                int races = Go.CeilDiv(current, pilotsPerRace);
                int next = races * pilotsToAdvance;
                if (next >= current)
                {
                    // No further reduction possible at this ratio (e.g. 2-pilot
                    // races advancing 2). The bracket can't keep shrinking — stop
                    // here and let the finals format (CTA / grand-final) handle
                    // the remaining pilots.
                    break;
                }
                rounds++;
                current = next;
                if (rounds > 20)
                {
                    break;
                }
            }
            return rounds;
        }

        // LosersRoundSpec is one round of the redemption (losers) bracket as planned by
        // LosersRoundPlan.
        internal struct LosersRoundSpec
        {
            public int Counter; // L{counter}
            public int Races;
            // FromWinnersRound is the winners round whose droppers enter here, or 0 for
            // a "minor" round that only halves the redemption pool.
            public int FromWinnersRound;
            // FinalTarget > 0 marks the redemption decider — a single race run for
            // exactly that many finals spots (see the consolidation note below).
            public int FinalTarget;
        }

        // LosersRoundPlan lays out the redemption bracket in the real double-elimination
        // cadence: after the first two rounds it alternates a MINOR round (halves the
        // redemption pool, nobody new enters) with a MAJOR round (the survivors plus the
        // next winners round's droppers).
        //
        // It used to feed winners round N straight into losers round N, with no minor
        // rounds at all, which let the redemption pool run ahead of the field it was
        // merging with and produced lopsided round sizes — a 48-pilot / 6-up / top-3
        // bracket got a 3-heat redemption round 3 (12 survivors + 6 fresh droppers)
        // where the correct sheet halves 12 → 2 heats first and takes the droppers in
        // round 4. The 16-pilot MultiGP sheet hand-transcribed in the qualbracket
        // package (L1×2, L2×2, L3×1, L4×1) is the reference this reproduces.
        //
        // wbExtraLosers is the number of pilots dropped by the winners consolidation
        // race (see BuildWinnersBracket) and wbExtraRound the round it sits in; they are
        // folded in as one more winners-round drop. losersTarget is how many pilots the
        // redemption side owes the finals format.
        internal static List<LosersRoundSpec> LosersRoundPlan(int totalPilots, int pilotsPerRace, int pilotsToAdvance, int winnersRounds, int wbExtraLosers, int wbExtraRound, int losersTarget)
        {
            if (pilotsPerRace < 1 || pilotsToAdvance < 1)
            {
                return null;
            }
            List<RoundInfo> winners = CalculateBracketStructure(totalPilots, pilotsPerRace, pilotsToAdvance, pilotsToAdvance);

            // drops[i] = the droppers entering from one winners round, in running order.
            List<int[]> drops = new List<int[]>(); // {round, count}
            for (int wr = 0; wr < winnersRounds; wr++)
            {
                int count = 0;
                if (wr < winners.Count)
                {
                    count = winners[wr].Losers;
                }
                drops.Add(new int[] { wr + 1, count });
            }
            if (wbExtraLosers > 0)
            {
                bool merged = false;
                for (int i = 0; i < drops.Count; i++)
                {
                    if (drops[i][0] == wbExtraRound)
                    {
                        drops[i][1] += wbExtraLosers;
                        merged = true;
                        break;
                    }
                }
                if (!merged)
                {
                    drops.Add(new int[] { wbExtraRound, wbExtraLosers });
                }
            }

            List<LosersRoundSpec> plan = null;
            int counter = 0, pool = 0, majors = 0;
            foreach (int[] d in drops)
            {
                // Minor (halving) round: from the third intake on, the pool is reduced
                // before the next winners round's droppers join it.
                if (majors >= 2 && pool > pilotsToAdvance)
                {
                    int racesMinor = Go.CeilDiv(pool, pilotsPerRace);
                    int next = racesMinor * pilotsToAdvance;
                    if (next < pool)
                    {
                        counter++;
                        plan = Go.Append(plan, new LosersRoundSpec { Counter = counter, Races = racesMinor });
                        pool = next;
                    }
                }
                int total = pool + d[1];
                if (total == 0)
                {
                    continue;
                }
                counter++;
                int races = Go.CeilDiv(total, pilotsPerRace);
                plan = Go.Append(plan, new LosersRoundSpec { Counter = counter, Races = races, FromWinnersRound = d[0] });
                pool = Go.MinInt(total, races * pilotsToAdvance);
                majors++;
            }

            // Tail: keep halving until the redemption side is down to one race's worth.
            while (pool > pilotsToAdvance)
            {
                int races = Go.CeilDiv(pool, pilotsPerRace);
                int next = races * pilotsToAdvance;
                if (next >= pool)
                {
                    break;
                }
                counter++;
                plan = Go.Append(plan, new LosersRoundSpec { Counter = counter, Races = races });
                pool = next;
                if (counter > (winnersRounds + 1) * 4)
                {
                    break;
                }
            }

            // Redemption decider: same idea as the winners side — if the bracket stops
            // with more pilots than the finals format can take from this side, add a
            // single race so those pilots actually race for the spot instead of getting
            // silently truncated when finalists are picked.
            if (losersTarget > 0 && pool > losersTarget && pool <= 8)
            {
                counter++;
                plan = Go.Append(plan, new LosersRoundSpec { Counter = counter, Races = 1, FinalTarget = losersTarget });
            }
            return plan;
        }

        /// <summary>
        /// BalancedSeeding snake-seeds pilots across the minimum number of races needed
        /// at pilotsPerRace, evening out the per-race counts (max−min ≤ 1) and spreading
        /// top seeds into different races. Exported for reuse by filler-heat generation;
        /// see CreateBalancedSeeding.
        /// </summary>
        public static List<List<string>> BalancedSeeding(List<string> pilots, int pilotsPerRace)
        {
            return CreateBalancedSeeding(pilots, pilotsPerRace);
        }

        // CreateBalancedSeeding snake-seeds pilots across races. For 13 pilots / 4 races
        // → 3+3+3+4 (max−min ≤ 1) with top seeds in different races.
        internal static List<List<string>> CreateBalancedSeeding(List<string> pilots, int pilotsPerRace)
        {
            int total = Go.Len(pilots);
            int races = Go.CeilDiv(total, pilotsPerRace);
            if (races == 0)
            {
                return null;
            }
            List<List<string>> output = new List<List<string>>();
            for (int i = 0; i < races; i++)
            {
                output.Add(new List<string>());
            }
            for (int i = 0; i < total; i++)
            {
                int row = i / races;
                int pos = i % races;
                int target = pos;
                if (row % 2 == 1)
                {
                    target = races - 1 - pos;
                }
                output[target].Add(pilots[i]);
            }
            // Drop any empty trailing races (shouldn't happen with snake but parity with TS).
            List<List<string>> cleaned = new List<List<string>>();
            foreach (List<string> r in output)
            {
                if (r.Count > 0)
                {
                    cleaned.Add(r);
                }
            }
            return cleaned;
        }

        // CalculateInterleavedHeatOrder returns a run order that splits the race list
        // in half and interleaves: H1, H(half+1), H2, H(half+2), … Gives pilots more
        // rest between back-to-back heats.
        internal static List<int> CalculateInterleavedHeatOrder(int numHeats)
        {
            if (numHeats <= 1)
            {
                return new List<int> { 1 };
            }
            int half = (numHeats + 1) / 2;
            int[] first = new int[half];
            for (int i = 0; i < half; i++)
            {
                first[i] = i + 1;
            }
            int[] second = new int[numHeats - half];
            for (int i = 0; i < numHeats - half; i++)
            {
                second[i] = half + 1 + i;
            }
            List<int> output = new List<int>();
            for (int i = 0; i < half; i++)
            {
                output.Add(first[i]);
                if (i < second.Length)
                {
                    output.Add(second[i]);
                }
            }
            return output;
        }

        // ---------- generation ----------

        private void Generate()
        {
            state.Races = null;
            state.Rounds = 0;
            state.CurrentRound = 1;
            state.ChaseTheAce = null;

            List<string> seeded = Go.Copy(cfg.PilotNames);

            if (cfg.EliminationType == EliminationType.Single)
            {
                GenerateSingleElim(seeded);
                return;
            }

            if (cfg.FinalsFormat == FinalsFormat.ChaseTheAce)
            {
                GenerateChaseTheAce(seeded);
                return;
            }

            GenerateDoubleElim(seeded);
        }

        private void AddRace(Race r)
        {
            state.Races = Go.Append(state.Races, r);
        }

        private void GenerateSingleElim(List<string> seeded)
        {
            int roundsToFinalists;
            if (cfg.FinalsFormat == FinalsFormat.ChaseTheAce)
            {
                int targetFinalists = CtaFinalistCount();
                roundsToFinalists = CalculateRoundsNeeded(cfg.PilotCount, targetFinalists, cfg.PilotsPerRace, cfg.PilotsToAdvance);
                if (roundsToFinalists < 1 && cfg.PilotCount > targetFinalists)
                {
                    // A field that needs no reduction round by the round maths (e.g. 9
                    // pilots, 4-up, 3 advancing) still can't put everyone in the final —
                    // keep one qualifying round, or the CTA heats land in round 1
                    // alongside it and nothing ever feeds them.
                    roundsToFinalists = 1;
                }
                state.Rounds = roundsToFinalists + 1; // CTA race
            }
            else
            {
                int target = cfg.PilotsPerRace;
                if (cfg.PilotCount < target)
                {
                    target = cfg.PilotCount;
                }
                roundsToFinalists = CalculateRoundsNeeded(cfg.PilotCount, target, cfg.PilotsPerRace, cfg.PilotsToAdvance);
                int final = 0;
                if (cfg.PilotCount > 1)
                {
                    final = 1;
                }
                state.Rounds = roundsToFinalists + final;
            }

            // Skip W1 entirely when every pilot is already a chase-the-ace finalist.
            bool ctaSkipsWinners = cfg.FinalsFormat == FinalsFormat.ChaseTheAce && cfg.PilotCount <= CtaFinalistCount();

            if (!ctaSkipsWinners)
            {
                List<List<string>> groups = CreateBalancedSeeding(seeded, cfg.PilotsPerRace);
                List<int> order = CalculateInterleavedHeatOrder(Go.Len(groups));
                for (int i = 0; i < Go.Len(groups); i++)
                {
                    AddRace(new Race
                    {
                        Id = "race-" + Go.Itoa(order[i]),
                        Round = 1,
                        Pilots = Go.Copy(groups[i]),
                        Bracket = BracketSlot.Winners,
                    });
                }
            }

            int pilotsRemaining = cfg.PilotCount;
            for (int round = 2; round <= state.Rounds; round++)
            {
                int prev = CountRacesInRound(state.Races, round - 1);
                int newCount = prev * cfg.PilotsToAdvance;
                if (newCount >= pilotsRemaining && round < state.Rounds)
                {
                    newCount = pilotsRemaining * 3 / 4;
                    if (newCount < 1)
                    {
                        newCount = 1;
                    }
                }
                pilotsRemaining = newCount;

                if (round == state.Rounds)
                {
                    // For chase-the-ace the CTA finals races are created in one
                    // batch after the loop (so the finalist-count + required-wins
                    // max-race math is centralised). For grand-final, add the
                    // single championship race here.
                    if (cfg.FinalsFormat != FinalsFormat.ChaseTheAce)
                    {
                        AddRace(new Race
                        {
                            Id = "R" + Go.Itoa(round) + "-1",
                            Round = round,
                            Bracket = BracketSlot.Winners,
                        });
                    }
                    continue;
                }

                if (pilotsRemaining <= cfg.PilotsToAdvance)
                {
                    continue; // direct advancement, no race
                }
                int races = Go.CeilDiv(pilotsRemaining, cfg.PilotsPerRace);
                for (int i = 0; i < races; i++)
                {
                    AddRace(new Race
                    {
                        Id = "R" + Go.Itoa(round) + "-" + Go.Itoa(i + 1),
                        Round = round,
                        Bracket = BracketSlot.Winners,
                    });
                }
            }

            if (cfg.FinalsFormat == FinalsFormat.ChaseTheAce)
            {
                // The round feeding the final must collectively advance exactly the
                // finalist count — with a wider-than-4 final, two semis advancing the
                // ordinary 2 each can't fill six seats.
                SetSemiFinalTargets(CtaFinalistCount());
                InitChaseTheAceState(seeded);
                // Pre-create the full set of CTA finals races up front. When every
                // pilot is already a finalist (≤4-pilot field that skips the winners
                // bracket), seed each race with all of them now.
                List<string> ctaPilots = null;
                if (ctaSkipsWinners)
                {
                    ctaPilots = Go.Copy(seeded);
                }
                if (!HasChaseTheAceRace(state.Races))
                {
                    AppendChaseTheAceRaces(state.Rounds, ctaPilots);
                }
                PopulateChaseTheAceFinalists();
            }
        }

        private void GenerateChaseTheAce(List<string> seeded)
        {
            int winnersTarget, losersTarget;
            FinalSideTargets(cfg, out winnersTarget, out losersTarget);
            int winnersRounds = CalculateRoundsNeeded(cfg.PilotCount, winnersTarget, cfg.PilotsPerRace, cfg.PilotsToAdvance);

            int wbExtraLosers, wbExtraRound;
            BuildWinnersBracket(seeded, winnersRounds, out wbExtraLosers, out wbExtraRound);
            BuildLosersBracket(winnersRounds, wbExtraLosers, wbExtraRound);

            // A side whose last race advances the ordinary PilotsToAdvance can't fill
            // its share of a wider-than-4 chase-the-ace final (a 6-up final wants 3 a
            // side, but a winners final advancing 2 supplies 2). Widen that last race so
            // it races for every finals spot its side owns.
            WidenFinalRace(BracketSlot.Winners, winnersTarget);
            WidenFinalRace(BracketSlot.Losers, losersTarget);

            int finalRound = MaxBuiltRound() + 1;
            InitChaseTheAceState(seeded);
            AppendChaseTheAceRaces(finalRound, null);
            state.Rounds = finalRound;
        }

        // AppendChaseTheAceRaces pre-creates the maximum possible CTA finals races
        // (CTA-1 … CTA-max) all in finalRound. `pilots` is set on every race when the
        // finalists are already known at generation time (e.g. ≤4-pilot single-elim
        // where everyone is a finalist); pass null to leave them empty until
        // PopulateChaseTheAceFinalists fills them. Safe to call only once — it assumes
        // no CTA races exist yet.
        private void AppendChaseTheAceRaces(int finalRound, List<string> pilots)
        {
            int requiredWins = cfg.AceWinsRequired;
            if (requiredWins < 1)
            {
                requiredWins = 2;
            }
            int finalistCount = CtaFinalistCount();
            int max = MaxChaseTheAceRaces(finalistCount, requiredWins);
            for (int n = 1; n <= max; n++)
            {
                List<string> p = null;
                if (Go.Len(pilots) > 0)
                {
                    p = Go.Copy(pilots);
                }
                AddRace(new Race
                {
                    Id = "CTA-" + Go.Itoa(n),
                    Round = finalRound,
                    Pilots = p,
                    Bracket = BracketSlot.ChaseTheAce,
                });
            }
        }

        private void GenerateDoubleElim(List<string> seeded)
        {
            int winnersFinalists = Go.MinInt(cfg.PilotsToAdvance, cfg.PilotCount / 2);
            if (winnersFinalists < 1)
            {
                winnersFinalists = 1;
            }
            int winnersRounds = CalculateRoundsNeeded(cfg.PilotCount, winnersFinalists, cfg.PilotsPerRace, cfg.PilotsToAdvance);

            int wbExtraLosers, wbExtraRound;
            BuildWinnersBracket(seeded, winnersRounds, out wbExtraLosers, out wbExtraRound);
            BuildLosersBracket(winnersRounds, wbExtraLosers, wbExtraRound);

            // Place the GF after whatever's actually been built — BuildWinnersBracket
            // and BuildLosersBracket may have added consolidation rounds beyond the
            // theoretical winnersRounds / losersRounds counts.
            int gfRound = MaxBuiltRound() + 1;
            state.Rounds = gfRound + 1;
            AddRace(new Race { Id = "GF-1", Round = gfRound, Bracket = BracketSlot.GrandFinal });
            AddRace(new Race { Id = "GF-2", Round = gfRound + 1, Bracket = BracketSlot.GrandFinal });
        }

        // MaxBuiltRound returns the highest Round value currently in state.Races,
        // used to compute where to place finals after the W/L brackets (including
        // any consolidation rounds) are built.
        private int MaxBuiltRound()
        {
            int maxR = 0;
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Round > maxR)
                {
                    maxR = r.Round;
                }
            }
            return maxR;
        }

        // BuildWinnersBracket builds the winners side and returns the number of losers
        // produced by the winners consolidation/final round (the race that narrows the
        // last 2+ winners down to the finals target) plus the round that race sits in.
        // Those losers drop into the losers bracket and must be counted when sizing it —
        // see BuildLosersBracket. (0, 0) when no consolidation race was needed.
        private void BuildWinnersBracket(List<string> seeded, int winnersRounds, out int extraLosers, out int extraRound)
        {
            int target, unused;
            FinalSideTargets(cfg, out target, out unused);
            List<List<string>> groups = CreateBalancedSeeding(seeded, cfg.PilotsPerRace);
            for (int i = 0; i < Go.Len(groups); i++)
            {
                AddRace(new Race
                {
                    Id = "W1-" + Go.Itoa(i + 1),
                    Round = 1,
                    Pilots = Go.Copy(groups[i]),
                    Bracket = BracketSlot.Winners,
                });
            }
            int lastRound = 1;
            for (int round = 2; round <= winnersRounds; round++)
            {
                int prevRaces = CountRacesInBracketRound(state.Races, round - 1, BracketSlot.Winners);
                int pilots = prevRaces * cfg.PilotsToAdvance;
                if (pilots <= cfg.PilotsToAdvance)
                {
                    continue;
                }
                int races = Go.CeilDiv(pilots, cfg.PilotsPerRace);
                if (races >= prevRaces)
                {
                    break;
                }
                for (int i = 0; i < races; i++)
                {
                    AddRace(new Race
                    {
                        Id = "W" + Go.Itoa(round) + "-" + Go.Itoa(i + 1),
                        Round = round,
                        Bracket = BracketSlot.Winners,
                    });
                }
                lastRound = round;
            }

            // Consolidation race: if the loop stopped with more winners than the
            // finals format can take from this side (CTA wants 2, GF wants 1), add a
            // single explicit decider so the race director picks the actual
            // advancers rather than having the library silently truncate.
            int prev = CountRacesInBracketRound(state.Races, lastRound, BracketSlot.Winners);
            int remaining = prev * cfg.PilotsToAdvance;
            if (remaining > target && remaining <= 8)
            {
                AddRace(new Race
                {
                    Id = "W" + Go.Itoa(lastRound + 1) + "-1",
                    Round = lastRound + 1,
                    Bracket = BracketSlot.Winners,
                    FinalTarget = target,
                });
                // This decider races `remaining` pilots for `target` finals spots; the
                // rest are eliminated into the losers bracket's tail.
                extraLosers = remaining - target;
                extraRound = lastRound + 1;
                return;
            }
            extraLosers = 0;
            extraRound = 0;
        }

        // FinalSideTargets is how many pilots each bracket side (winners and losers in
        // double-elim) should converge to so the finals format can run cleanly: the
        // chase-the-ace finalists split across the two sides (4 finalists → 2 + 2, 6 →
        // 3 + 3, 3 → 1 + 2), 1 + 1 for a grand final. The split matches
        // DeriveChaseTheAceFinalists exactly, so no pilot races a decider for a spot
        // that is then truncated away. Single-elim always converges to 1.
        internal static void FinalSideTargets(BracketConfig cfg, out int winners, out int losers)
        {
            if (cfg.EliminationType == EliminationType.Single || cfg.FinalsFormat != FinalsFormat.ChaseTheAce)
            {
                winners = 1;
                losers = 1;
                return;
            }
            int want = CtaFinalists(cfg);
            winners = want / 2;
            if (winners < 1)
            {
                winners = 1;
            }
            losers = want - winners;
            if (losers < 1)
            {
                losers = 1;
            }
        }

        // BuildLosersBracket builds the redemption side from LosersRoundPlan. wbExtraLosers
        // is the number of pilots dropped by the winners consolidation/final race and
        // wbExtraRound the round that race sits in; they enter as one more winners-round
        // intake.
        private void BuildLosersBracket(int winnersRounds, int wbExtraLosers, int wbExtraRound)
        {
            int unused, losersTarget;
            FinalSideTargets(cfg, out unused, out losersTarget);
            List<LosersRoundSpec> plan = LosersRoundPlan(cfg.PilotCount, cfg.PilotsPerRace, cfg.PilotsToAdvance,
                winnersRounds, wbExtraLosers, wbExtraRound, losersTarget);
            // L_n races sit at round counter+1 so they interleave with W rounds — L1 runs
            // alongside W2, L2 alongside W3, etc. (parity with the original drone-racing UI).
            foreach (LosersRoundSpec spec in Go.Iter(plan))
            {
                for (int i = 0; i < spec.Races; i++)
                {
                    AddRace(new Race
                    {
                        Id = "L" + Go.Itoa(spec.Counter) + "-" + Go.Itoa(i + 1),
                        Round = spec.Counter + 1,
                        Bracket = BracketSlot.Losers,
                        FinalTarget = spec.FinalTarget,
                        FromWinnersRound = spec.FromWinnersRound,
                    });
                }
            }
        }

        // SetSemiFinalTargets spreads `want` finals spots across the single-elimination
        // round that feeds the final, so those heats race for exactly the number of
        // seats the final holds. A round that already advances exactly `want` (the
        // classic two semis × two advancing = four finalists) is left alone, as is a
        // round with more heats than seats — there the finalists are taken off the top
        // as before.
        private void SetSemiFinalTargets(int want)
        {
            int semiRound = state.Rounds - 1;
            if (semiRound < 1 || want < 1)
            {
                return;
            }
            List<Race> semis = new List<Race>();
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Round == semiRound && r.Bracket == BracketSlot.Winners)
                {
                    semis.Add(r);
                }
            }
            int n = semis.Count;
            if (n == 0 || n > want || n * cfg.PilotsToAdvance == want)
            {
                return;
            }
            int baseCount = want / n, extra = want % n;
            for (int i = 0; i < semis.Count; i++)
            {
                Race r = semis[i];
                if (r.FinalTarget > 0)
                {
                    continue;
                }
                int t = baseCount;
                if (i < extra)
                {
                    t++;
                }
                r.FinalTarget = t;
            }
        }

        // WidenFinalRace makes the single last race of a bracket side advance `target`
        // pilots instead of the config's PilotsToAdvance. It is a no-op when the side
        // ends in several races (they collectively already supply enough), when the
        // side already ends in an explicit consolidation race (FinalTarget set), or
        // when target isn't larger than the normal advance count — so the classic
        // 4-finalist chase-the-ace is untouched.
        private void WidenFinalRace(string slot, int target)
        {
            if (target <= cfg.PilotsToAdvance)
            {
                return;
            }
            int maxRound = 0;
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == slot && r.Round > maxRound)
                {
                    maxRound = r.Round;
                }
            }
            if (maxRound == 0)
            {
                return;
            }
            List<Race> last = new List<Race>();
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == slot && r.Round == maxRound)
                {
                    last.Add(r);
                }
            }
            if (last.Count != 1 || last[0].FinalTarget > 0)
            {
                return;
            }
            last[0].FinalTarget = target;
        }

        private void InitChaseTheAceState(List<string> seeded)
        {
            GoMap<int> wins = new GoMap<int>();
            GoMap<int> points = new GoMap<int>();
            foreach (string p in Go.Iter(seeded))
            {
                wins[p] = 0;
                points[p] = 0;
            }
            int requiredWins = cfg.AceWinsRequired;
            if (requiredWins < 1)
            {
                requiredWins = 2;
            }
            state.ChaseTheAce = new ChaseTheAceState
            {
                Wins = wins,
                Points = points,
                Finalists = new List<string>(),
                RequiredWins = requiredWins,
            };
        }

        // ---------- advancement ----------

        /// <summary>
        /// SelectWinners records the result of a race and advances pilots to the next races.
        /// `winners` must be in placement order; the rest of race.Pilots become losers.
        /// </summary>
        public void SelectWinners(string raceId, IList<string> winners)
        {
            Race race = FindRace(raceId);
            if (race == null)
            {
                throw new BracketException("bracket: race " + Go.Quote(raceId) + " not found");
            }
            if (race.IsComplete)
            {
                throw new BracketException("bracket: race " + Go.Quote(raceId) + " already complete");
            }
            CheckChaseTheAceEntry(race);

            int expected = ExpectedWinnersCountInternal(race);
            int winnersCount = winners == null ? 0 : winners.Count;
            if (winnersCount > expected)
            {
                throw new BracketException("bracket: too many winners (" + Go.Itoa(winnersCount) + " > " + Go.Itoa(expected) + ") for race " + Go.Quote(raceId));
            }

            // Validate every winner is actually in the race
            HashSet<string> pilotSet = StringSet(race.Pilots);
            foreach (string w in Go.Iter(winners))
            {
                if (!pilotSet.Contains(w))
                {
                    throw new BracketException("bracket: winner " + Go.Quote(w) + " not in race " + Go.Quote(raceId));
                }
            }

            race.Winners = Go.Copy(winners);
            race.Losers = Go.Truncate(race.Losers);
            HashSet<string> winSet = StringSet(winners);
            foreach (string p in Go.Iter(race.Pilots))
            {
                if (!winSet.Contains(p))
                {
                    race.Losers = Go.Append(race.Losers, p);
                }
            }
            race.IsComplete = winnersCount == expected;
            // Partial entry: the user only ranked the top N. Losers' positions
            // weren't captured, so make sure FullPlacement is cleared (e.g. if
            // the race was previously edited as full and the new edit is partial).
            race.FullPlacement = false;

            if (race.IsComplete)
            {
                Advance(race);
            }
        }

        /// <summary>
        /// SelectFullPlacement records the complete finishing order for a race. The
        /// first ExpectedWinnersCount entries become winners (advance to the next
        /// round); the rest stay in race.Pilots in placement order so the loser
        /// finish order is preserved for display ("3rd: P9, 4th: P12 …").
        ///
        /// `placement` must contain every pilot currently in the race, with no
        /// duplicates and no extras.
        /// </summary>
        public void SelectFullPlacement(string raceId, IList<string> placement)
        {
            Race race = FindRace(raceId);
            if (race == null)
            {
                throw new BracketException("bracket: race " + Go.Quote(raceId) + " not found");
            }
            if (race.IsComplete)
            {
                throw new BracketException("bracket: race " + Go.Quote(raceId) + " already complete");
            }
            CheckChaseTheAceEntry(race);
            int placementCount = placement == null ? 0 : placement.Count;
            if (placementCount != Go.Len(race.Pilots))
            {
                throw new BracketException("bracket: placement has " + Go.Itoa(placementCount) + " pilots, race has " + Go.Itoa(Go.Len(race.Pilots)));
            }
            HashSet<string> pilotSet = StringSet(race.Pilots);
            HashSet<string> seen = new HashSet<string>();
            foreach (string p in Go.Iter(placement))
            {
                if (!pilotSet.Contains(p))
                {
                    throw new BracketException("bracket: " + Go.Quote(p) + " not in race " + Go.Quote(raceId));
                }
                if (seen.Contains(p))
                {
                    throw new BracketException("bracket: duplicate " + Go.Quote(p) + " in placement");
                }
                seen.Add(p);
            }
            // Reorder the race's pilots so the displayed order matches the finish.
            race.Pilots = Go.Copy(placement);
            int expected = ExpectedWinnersCountInternal(race);
            if (expected > placementCount)
            {
                expected = placementCount;
            }
            List<string> winners = new List<string>();
            for (int i = 0; i < expected; i++)
            {
                winners.Add(placement[i]);
            }
            race.Winners = Go.Copy(winners);
            race.Losers = Go.Truncate(race.Losers);
            HashSet<string> winSet = StringSet(winners);
            foreach (string p in Go.Iter(race.Pilots))
            {
                if (!winSet.Contains(p))
                {
                    race.Losers = Go.Append(race.Losers, p);
                }
            }
            race.IsComplete = winners.Count == expected;
            // Mark as full placement BEFORE Advance() runs — AdvanceChaseTheAce
            // reads FullPlacement to decide whether to award position points to
            // every pilot (1pt 1st, 2pt 2nd, …) or just 1pt to the winner.
            race.FullPlacement = true;
            if (race.IsComplete)
            {
                Advance(race);
            }
        }

        /// <summary>
        /// CanEditRace reports whether a completed race's result can still be revised.
        /// Edits are refused once any downstream race that received this race's pilots
        /// has itself been run (or once the chase-the-ace finalists have been locked
        /// in), because re-running this race would invalidate work already recorded.
        ///
        /// Returns ok=true with reason="" for incomplete races (no edit needed —
        /// submit normally) and for completed races whose downstream is still clean.
        /// </summary>
        public bool CanEditRace(string raceId, out string reason)
        {
            Race race = FindRace(raceId);
            if (race == null)
            {
                reason = "race not found";
                return false;
            }
            if (!race.IsComplete)
            {
                reason = "";
                return true;
            }

            HashSet<string> placed = StringSet(race.Winners);
            foreach (string l in Go.Iter(race.Losers))
            {
                placed.Add(l);
            }

            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Id == raceId)
                {
                    continue;
                }
                if (r.Round <= race.Round)
                {
                    continue;
                }
                if (!r.IsComplete)
                {
                    continue;
                }
                foreach (string p in Go.Iter(r.Pilots))
                {
                    if (placed.Contains(p))
                    {
                        reason = "downstream race " + r.Id + " has already been run";
                        return false;
                    }
                }
            }

            // No separate chase-the-ace finalists guard: once the series has started the
            // completed CTA heats (which carry the finalist roster) are caught by the
            // downstream-IsComplete loop above, so a feeder is correctly frozen. Before
            // the series starts the finalists are provisional (re-synced with the feeders
            // by PopulateChaseTheAceFinalists), so a feeder may be corrected freely.
            reason = "";
            return true;
        }

        /// <summary>
        /// EditRace replaces a completed race's result with a new full placement.
        /// Removes the race's prior outputs (its winners + losers) from any incomplete
        /// downstream races, clears the race's complete/winners/losers state, then
        /// records the new placement via SelectFullPlacement.
        ///
        /// Refuses (throwing) if CanEditRace says the downstream has already
        /// progressed past the point of safe editing.
        /// </summary>
        public void EditRace(string raceId, IList<string> newPlacement)
        {
            // Rewind this race (and any clean downstream state) first, then record the
            // new result. ResetRace is a no-op on a not-yet-run race, so a fresh edit
            // falls straight through to ApplyPlacement.
            ResetRace(raceId);
            ApplyPlacement(raceId, newPlacement);
        }

        /// <summary>
        /// ResetRace un-runs a completed race: it strips the pilots this race advanced
        /// or dropped from any still-incomplete downstream race, then clears the race's
        /// own winners/losers/complete state so it becomes pending again. Refuses (via
        /// CanEditRace) when a downstream race that received these pilots has already
        /// been run. A no-op on a race that hasn't been run.
        /// </summary>
        public void ResetRace(string raceId)
        {
            string reason;
            if (!CanEditRace(raceId, out reason))
            {
                throw new BracketException("bracket: cannot edit " + Go.Quote(raceId) + ": " + reason);
            }
            Race race = FindRace(raceId);
            if (race == null)
            {
                throw new BracketException("bracket: race " + Go.Quote(raceId) + " not found");
            }
            if (!race.IsComplete)
            {
                return; // nothing to undo
            }

            // Build the set of pilots previously advanced/dropped out of this race.
            HashSet<string> placed = StringSet(race.Winners);
            foreach (string l in Go.Iter(race.Losers))
            {
                placed.Add(l);
            }

            // Strip those pilots from any downstream race that's still incomplete.
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Id == raceId)
                {
                    continue;
                }
                if (r.Round <= race.Round)
                {
                    continue;
                }
                if (r.IsComplete)
                {
                    continue;
                }
                if (Go.Len(r.Pilots) == 0)
                {
                    continue;
                }
                List<string> kept = new List<string>();
                foreach (string p in r.Pilots)
                {
                    if (!placed.Contains(p))
                    {
                        kept.Add(p);
                    }
                }
                if (kept.Count < r.Pilots.Count)
                {
                    r.Pilots = Go.Copy(kept);
                }
            }

            // Reset this race's completion state.
            race.Winners = null;
            race.Losers = null;
            race.IsComplete = false;
            race.FullPlacement = false;

            // Un-running a CTA finals heat has to reverse its contribution to the
            // running tally, or a later re-run/edit would count the win twice (and
            // could falsely crown a champion). The recompute is derived purely from the
            // still-completed heats, so clearing this one's IsComplete above is enough —
            // it also un-crowns a champion and reopens any voided surplus heats when the
            // clinching heat is the one being reset.
            if (race.Bracket == BracketSlot.ChaseTheAce)
            {
                RecomputeChaseTheAceProgress();
            }

            // If this reset left a chase-the-ace feeder incomplete (and the series hasn't
            // started), drop the now-stale provisional finalists so they re-derive when
            // the feeder is re-run. No-op for non-CTA brackets / once the series began.
            PopulateChaseTheAceFinalists();
        }

        /// <summary>
        /// ApplyPlacement is the shared "record a race result" core used by both fresh
        /// submission (after EditRace's reset, or first run via the handler) and edit
        /// flows. Accepts either a full placement (every pilot in race.Pilots, in
        /// finish order) or the top-N winners only (anything else stays a loser in
        /// race.Pilots order). Refuses fewer than the expected-winners count.
        /// </summary>
        public void ApplyPlacement(string raceId, IList<string> placement)
        {
            Race race = FindRace(raceId);
            if (race == null)
            {
                throw new BracketException("bracket: race " + Go.Quote(raceId) + " not found");
            }
            int expected = ExpectedWinnersCountInternal(race);
            int count = placement == null ? 0 : placement.Count;
            if (count == Go.Len(race.Pilots))
            {
                SelectFullPlacement(raceId, placement);
                return;
            }
            if (count >= expected)
            {
                List<string> top = new List<string>();
                for (int i = 0; i < expected; i++)
                {
                    top.Add(placement[i]);
                }
                SelectWinners(raceId, top);
                return;
            }
            throw new BracketException("bracket: race " + Go.Quote(raceId) + " needs at least " + Go.Itoa(expected) + " positions, got " + Go.Itoa(count));
        }

        /// <summary>
        /// ExpectedWinnersCount returns how many winners a race expects — 1 for finals
        /// (CTA / grand final / single-elim championship round), otherwise PilotsToAdvance
        /// (capped at the actual pilot count).
        /// </summary>
        public int ExpectedWinnersCount(Race race)
        {
            return ExpectedWinnersCountInternal(race);
        }

        private int ExpectedWinnersCountInternal(Race race)
        {
            // Consolidation races have a hard-coded target (e.g. 2 for CTA, 1 for
            // grand-final) regardless of how many pilots ended up in them. Cap to
            // the actual pilot count so an empty consolidation race doesn't
            // demand more winners than it has pilots.
            if (race.FinalTarget > 0)
            {
                int t = race.FinalTarget;
                if (Go.Len(race.Pilots) > 0 && t > Go.Len(race.Pilots))
                {
                    t = Go.Len(race.Pilots);
                }
                return t;
            }
            if (race.Bracket == BracketSlot.ChaseTheAce ||
                race.Bracket == BracketSlot.GrandFinal ||
                (cfg.EliminationType == EliminationType.Single && race.Round == state.Rounds))
            {
                return 1;
            }
            if (Go.Len(race.Pilots) == 0)
            {
                return cfg.PilotsToAdvance;
            }
            return Go.MinInt(cfg.PilotsToAdvance, Go.Len(race.Pilots));
        }

        private void Advance(Race completed)
        {
            // Single elimination
            if (cfg.EliminationType == EliminationType.Single)
            {
                if (completed.Bracket == BracketSlot.ChaseTheAce)
                {
                    AdvanceChaseTheAce(completed);
                    return;
                }

                int nextRound = completed.Round + 1;
                List<Race> nextRaces = RacesInRound(nextRound);
                if (cfg.FinalsFormat == FinalsFormat.ChaseTheAce && HasChaseTheAceRace(nextRaces))
                {
                    // The next round IS the chase-the-ace final. Its line-up is the
                    // derived finalist set — never a seeding of this heat's winners
                    // across the pre-created CTA heats, which scattered one or two
                    // pilots into CTA-1, CTA-2, … and burned them before the series
                    // even started.
                    PopulateChaseTheAceFinalists();
                    return;
                }
                if (nextRaces.Count == 0)
                {
                    // Either tournament is complete, or we need to create races/send to finals.
                    int currentWinners = TotalWinnersInRound(completed.Round);
                    if (currentWinners == 1)
                    {
                        return; // champion
                    }
                    if (cfg.FinalsFormat == FinalsFormat.ChaseTheAce)
                    {
                        PopulateChaseTheAceFinalists();
                        return;
                    }
                    // Create the next round's races based on total expected winners.
                    int expected = EstimatedNextRoundPilots(completed.Round);
                    if (expected > 1)
                    {
                        int races = Go.CeilDiv(expected, cfg.PilotsPerRace);
                        for (int i = 0; i < races; i++)
                        {
                            AddRace(new Race
                            {
                                Id = "R" + Go.Itoa(nextRound) + "-" + Go.Itoa(i + 1),
                                Round = nextRound,
                                Bracket = BracketSlot.Winners,
                            });
                        }
                        nextRaces = RacesInRound(nextRound);
                    }
                }
                if (nextRaces.Count > 0)
                {
                    PlaceSeeded(completed, completed.Winners, nextRaces);
                }
                if (cfg.FinalsFormat == FinalsFormat.ChaseTheAce)
                {
                    PopulateChaseTheAceFinalists();
                }
                CheckAutoAdvanceAll();
                return;
            }

            // Double elimination
            switch (completed.Bracket)
            {
                case BracketSlot.Winners:
                    {
                        int nextRound = completed.Round + 1;
                        List<Race> nextRaces = RacesInBracketRound(nextRound, BracketSlot.Winners);
                        if (nextRaces.Count > 0)
                        {
                            PlaceSeeded(completed, completed.Winners, nextRaces);
                        }
                        else if (Go.Len(completed.Winners) > 0)
                        {
                            // Last winners round — send to finals.
                            if (cfg.FinalsFormat == FinalsFormat.ChaseTheAce)
                            {
                                PopulateChaseTheAceFinalists();
                            }
                            else
                            {
                                Race gf = FindRace("GF-1");
                                if (gf != null)
                                {
                                    gf.Pilots = AppendUnique(gf.Pilots, completed.Winners);
                                }
                            }
                        }
                        if (Go.Len(completed.Losers) > 0)
                        {
                            // MultiGP-style seed-aware drop: each W race's losers go to a
                            // deterministic L race based on the W race's snake position in
                            // its W round, so two top-seed-droppers from different W heats
                            // don't end up in the same L1 race. Falls back to the old
                            // least-filled logic only when no matching L round exists at the
                            // expected round number (irregular bracket shape).
                            List<Race> lTargets = RacesInBracketRound(LosersDropRound(completed.Round), BracketSlot.Losers);
                            if (lTargets.Count > 0)
                            {
                                List<string> remaining = PlaceSeeded(completed, completed.Losers, lTargets);
                                if (Go.Len(remaining) > 0)
                                {
                                    PlaceLosersInBracket(remaining, completed.Round);
                                }
                            }
                            else
                            {
                                PlaceLosersInBracket(completed.Losers, completed.Round);
                            }
                        }
                        break;
                    }

                case BracketSlot.Losers:
                    {
                        int nextRound = completed.Round + 1;
                        List<Race> nextRaces = RacesInBracketRound(nextRound, BracketSlot.Losers);
                        if (nextRaces.Count > 0)
                        {
                            // Same snake-shift as the winners side, applied to losers→losers
                            // progression — top L1 advancers don't immediately rematch in L2.
                            List<string> remaining = PlaceSeeded(completed, completed.Winners, nextRaces);
                            if (Go.Len(remaining) > 0)
                            {
                                // Create another race in the next losers round for overflow.
                                // L_n is at round n+1, so counter = round - 1.
                                int counter = nextRound - 1;
                                int existing = nextRaces.Count;
                                Race newRace = new Race
                                {
                                    Id = "L" + Go.Itoa(counter) + "-" + Go.Itoa(existing + 1),
                                    Round = nextRound,
                                    Pilots = Go.Copy(remaining),
                                    Bracket = BracketSlot.Losers,
                                };
                                AddRace(newRace);
                            }
                        }
                        else if (Go.Len(completed.Winners) > 0)
                        {
                            // Final losers round — to finals.
                            if (cfg.FinalsFormat == FinalsFormat.ChaseTheAce)
                            {
                                PopulateChaseTheAceFinalists();
                            }
                            else
                            {
                                Race gf = FindRace("GF-1");
                                if (gf != null)
                                {
                                    gf.Pilots = AppendUnique(gf.Pilots, completed.Winners);
                                }
                            }
                        }
                        break;
                    }

                case BracketSlot.GrandFinal:
                    if (completed.Id == "GF-1")
                    {
                        string winner = "";
                        if (Go.Len(completed.Winners) > 0)
                        {
                            winner = completed.Winners[0];
                        }
                        List<string> losersFinalists = GetLosersFinalists();
                        if (StringInSlice(winner, losersFinalists))
                        {
                            Race gf2 = FindRace("GF-2");
                            if (gf2 != null)
                            {
                                gf2.Pilots = Go.Copy(completed.Pilots);
                            }
                        }
                    }
                    break;

                case BracketSlot.ChaseTheAce:
                    AdvanceChaseTheAce(completed);
                    return;
            }

            if (cfg.FinalsFormat == FinalsFormat.ChaseTheAce)
            {
                PopulateChaseTheAceFinalists();
            }
            CheckAutoAdvanceAll();
        }

        // CheckChaseTheAceEntry gates results on chase-the-ace finals races. CTA races
        // are all pre-created up front, so a result must only be accepted for the
        // lowest-numbered un-run heat, never out of order, never for a voided heat, and
        // never once a champion has already been crowned. Non-CTA races are unaffected.
        private void CheckChaseTheAceEntry(Race race)
        {
            if (race.Bracket != BracketSlot.ChaseTheAce)
            {
                return;
            }
            if (race.IsVoid)
            {
                throw new BracketException("bracket: chase-the-ace race " + Go.Quote(race.Id) + " is void (champion already decided)");
            }
            ChaseTheAceState cta = state.ChaseTheAce;
            if (cta != null && cta.IsComplete)
            {
                throw new BracketException("bracket: chase-the-ace already complete (champion " + Go.Quote(cta.Champion) + ")");
            }
            Race next = NextChaseTheAceRace();
            if (next != null && next.Id != race.Id)
            {
                throw new BracketException("bracket: chase-the-ace races must be run in order; " + Go.Quote(next.Id) + " is next, not " + Go.Quote(race.Id));
            }
        }

        private void AdvanceChaseTheAce(Race completed)
        {
            // The tally is derived wholesale from the completed CTA heats rather than
            // incremented per-call, so re-running, editing, or re-ingesting a result
            // (all of which route through ResetRace + a fresh apply) can never
            // double-count a heat. `completed` is unused — it's already recorded on the
            // race by the caller; the recompute picks it up from state.
            RecomputeChaseTheAceProgress();
        }

        // RecomputeChaseTheAceProgress derives the entire chase-the-ace tally — per-
        // pilot wins and position points, the champion, and the final standings —
        // purely from the completed CTA heats, in race order. Because it starts from a
        // clean slate every time, it is idempotent: running a heat, editing its result,
        // resetting it, or re-ingesting the same result from the timing system all
        // converge on the same state, so a win is counted exactly once. When a champion
        // emerges the surplus un-run heats are voided (not deleted) so an edit that
        // removes the clinching win can reopen them.
        private void RecomputeChaseTheAceProgress()
        {
            ChaseTheAceState cta = state.ChaseTheAce;
            if (cta == null)
            {
                return;
            }

            cta.Wins = new GoMap<int>();
            cta.Points = new GoMap<int>();
            foreach (string f in Go.Iter(cta.Finalists))
            {
                cta.Wins[f] = 0;
                cta.Points[f] = 0;
            }
            cta.Champion = "";
            cta.IsComplete = false;
            cta.Standings = null;
            cta.TieBreakNeeded = null;

            // Replay completed heats in CTA-1, CTA-2, … order. Voided placeholders and
            // un-run heats contribute nothing.
            foreach (Race r in ChaseTheAceRaces())
            {
                if (r.IsVoid || !r.IsComplete || Go.Len(r.Winners) == 0)
                {
                    continue;
                }
                if (r.FullPlacement)
                {
                    // SelectFullPlacement reorders race.Pilots to finish order, so
                    // race.Pilots[i] is the (i+1)th-place finisher. 1pt for 1st, 2 for
                    // 2nd, … (lower total = better non-champion finish).
                    int i = 0;
                    foreach (string p in Go.Iter(r.Pilots))
                    {
                        cta.Points[p] = cta.Points.Get(p) + i + 1;
                        i++;
                    }
                }
                else
                {
                    // Partial entry — only the winner is known. 1 point to them.
                    cta.Points[r.Winners[0]] = cta.Points.Get(r.Winners[0]) + 1;
                }
                string winner = r.Winners[0];
                cta.Wins[winner] = cta.Wins.Get(winner) + 1;
                if (cta.Wins[winner] >= cta.RequiredWins)
                {
                    cta.Champion = winner;
                    cta.IsComplete = true;
                    ComputeChaseTheAceStandings();
                    break;
                }
            }

            ReconcileChaseTheAceVoids();
        }

        // ReconcileChaseTheAceVoids marks the un-run CTA heats void once a champion is
        // crowned (all heats are pre-created up front so they can be exported to the
        // timing system in advance; the surplus is no longer needed) and un-voids every
        // heat while the series is still undecided — so an edit that removes a clinching
        // win brings the remaining heats back into play. Completed heats are never void.
        private void ReconcileChaseTheAceVoids()
        {
            ChaseTheAceState cta = state.ChaseTheAce;
            bool decided = cta != null && cta.IsComplete;
            foreach (Race r in ChaseTheAceRaces())
            {
                if (r.IsComplete)
                {
                    r.IsVoid = false;
                    continue;
                }
                r.IsVoid = decided;
            }
        }

        // ComputeChaseTheAceStandings ranks the non-champion finalists by total
        // Points (ascending). Pilots tied on points are surfaced in TieBreakNeeded
        // so the UI can prompt the race director for a tie-break race.
        private void ComputeChaseTheAceStandings()
        {
            ChaseTheAceState cta = state.ChaseTheAce;
            if (cta == null || cta.Champion == "")
            {
                return;
            }
            List<string> others = new List<string>();
            foreach (string p in Go.Iter(cta.Finalists))
            {
                if (p != cta.Champion)
                {
                    others.Add(p);
                }
            }
            // Fewest points first; a points tie goes to the higher qualifying seed
            // (the rule points-mains uses) — finalist order would otherwise hand
            // every tie to the winners-side pilot. TieBreakNeeded still flags the
            // tie so the director can run a decider and override on Finalise.
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

            // Detect ties — adjacent non-champion pilots with equal points.
            cta.TieBreakNeeded = null;
            int i = 0;
            while (i < others.Count)
            {
                int j = i;
                while (j + 1 < others.Count && cta.Points.Get(others[j + 1]) == cta.Points.Get(others[i]))
                {
                    j++;
                }
                if (j > i)
                {
                    List<string> group = others.GetRange(i, j - i + 1);
                    cta.TieBreakNeeded = Go.Append(cta.TieBreakNeeded, group);
                }
                i = j + 1;
            }
        }

        // PlaceBalanced drops each pilot into the next-round race with the fewest pilots
        // that still has room. Returns any pilots that couldn't be placed.
        private List<string> PlaceBalanced(List<string> pilots, List<Race> targets)
        {
            List<Race> affected = new List<Race>();
            List<string> remaining = new List<string>();
            foreach (string p in Go.Iter(pilots))
            {
                Race pick = null;
                foreach (Race r in targets)
                {
                    if (r.FinalTarget == 0 && Go.Len(r.Pilots) >= cfg.PilotsPerRace)
                    {
                        continue;
                    }
                    if (pick == null || Go.Len(r.Pilots) < Go.Len(pick.Pilots))
                    {
                        pick = r;
                    }
                }
                if (pick == null)
                {
                    remaining.Add(p);
                    continue;
                }
                pick.Pilots = Go.Append(pick.Pilots, p);
                if (!affected.Contains(pick))
                {
                    affected.Add(pick);
                }
            }
            // NB: Go iterates a map here (random order); insertion order is used.
            foreach (Race r in affected)
            {
                CheckAutoAdvance(r);
            }
            return remaining;
        }

        // PlaceSeeded distributes winners from a completed winners-bracket race into the
        // next round's target races using MultiGP-style snake placement.
        //
        // Conceptually, all advancers from a round form one sequence — every source's
        // rank-0 winner first (in source order), then every source's rank-1, etc. — and
        // that sequence is snake-distributed across the targets:
        //
        //	advancerIdx = rank*numSources + srcIdx
        //	target      = snake(advancerIdx, numTargets)
        //
        // Each race only places its own winners, but because the formula is deterministic
        // per (srcIdx, rank), the same overall layout is built up regardless of which
        // race completes when. This guarantees:
        //
        //   - top seeds (lowest srcIdx, rank 0) land in different next-round races
        //   - a heat's 1st and 2nd advancer land in different next-round races
        //   - per-target load matches what the bracket math sized the next round for
        //     (ceilDiv(totalAdvancers, pilotsPerRace) targets), so capacity is respected
        //
        // Returns any winners that could not be placed (every target full).
        private List<string> PlaceSeeded(Race completed, List<string> winnersIn, List<Race> targets)
        {
            if (Go.Len(winnersIn) == 0 || targets.Count == 0)
            {
                return Go.Copy(winnersIn);
            }

            List<Race> siblings = new List<Race>();
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Round == completed.Round && r.Bracket == completed.Bracket)
                {
                    siblings.Add(r);
                }
            }
            Go.SortStable(siblings, (a, b) => RaceIndexNum(a.Id) < RaceIndexNum(b.Id));
            int srcIdx = -1;
            for (int i = 0; i < siblings.Count; i++)
            {
                if (siblings[i].Id == completed.Id)
                {
                    srcIdx = i;
                    break;
                }
            }
            if (srcIdx < 0)
            {
                return PlaceBalanced(winnersIn, targets);
            }

            List<Race> sortedTargets = new List<Race>(targets);
            Go.SortStable(sortedTargets, (a, b) => RaceIndexNum(a.Id) < RaceIndexNum(b.Id));
            int numTargets = sortedTargets.Count;

            // snakePos = this source's snake-mapped target index (top seeds first).
            int row = srcIdx / numTargets;
            int pos = srcIdx % numTargets;
            int snakePos = pos;
            if (row % 2 == 1)
            {
                snakePos = numTargets - 1 - pos;
            }

            // Re-rank the inbound pilots by their ABSOLUTE qualifying seed so the
            // snake-shift carries the bracket-wide seed structure forward. Without
            // this, an upset (e.g. seed 16 beats seed 2 in a heat) makes seed 2
            // look like a "rank 1" pilot and lands them in the same next-round
            // race as the actual rank-0 top seed — letting seeds 1 and 2 collide
            // rounds earlier than the MultiGP-style pre-charted bracket would.
            List<string> winners = new List<string>(winnersIn);
            Go.SortStable(winners, (a, b) => SeedOf(a) < SeedOf(b));

            List<Race> affected = new List<Race>();
            List<string> remaining = new List<string>();
            HashSet<int> usedByThisRace = new HashSet<int>();
            for (int rank = 0; rank < winners.Count; rank++)
            {
                string winner = winners[rank];
                // Rank 0 goes to the snake target; each subsequent rank shifts one over.
                // This puts a heat's 1st and 2nd in different next-round races.
                int targetIdx = (snakePos + rank) % numTargets;
                Race target = sortedTargets[targetIdx];

                bool full = target.FinalTarget == 0 && Go.Len(target.Pilots) >= cfg.PilotsPerRace;
                bool clash = usedByThisRace.Contains(targetIdx);
                if (full || clash)
                {
                    // Walk forward looking for a target that is (a) not full and
                    // (b) not already used by this source race. This avoids both
                    // capacity overflow and same-source rematches under overflow.
                    bool placed = false;
                    for (int offset = 1; offset < numTargets; offset++)
                    {
                        int altIdx = (targetIdx + offset) % numTargets;
                        Race alt = sortedTargets[altIdx];
                        if (Go.Len(alt.Pilots) >= cfg.PilotsPerRace)
                        {
                            continue;
                        }
                        if (usedByThisRace.Contains(altIdx))
                        {
                            continue;
                        }
                        alt.Pilots = Go.Append(alt.Pilots, winner);
                        MarkAffected(affected, alt);
                        usedByThisRace.Add(altIdx);
                        placed = true;
                        break;
                    }
                    if (placed)
                    {
                        continue;
                    }
                    // Second pass: relax the same-source constraint if nothing else fits.
                    for (int offset = 0; offset < numTargets; offset++)
                    {
                        int altIdx = (targetIdx + offset) % numTargets;
                        Race alt = sortedTargets[altIdx];
                        if (Go.Len(alt.Pilots) >= cfg.PilotsPerRace)
                        {
                            continue;
                        }
                        alt.Pilots = Go.Append(alt.Pilots, winner);
                        MarkAffected(affected, alt);
                        usedByThisRace.Add(altIdx);
                        placed = true;
                        break;
                    }
                    if (!placed)
                    {
                        remaining.Add(winner);
                    }
                    continue;
                }
                target.Pilots = Go.Append(target.Pilots, winner);
                MarkAffected(affected, target);
                usedByThisRace.Add(targetIdx);
            }
            // NB: Go iterates a map here (random order); insertion order is used.
            foreach (Race r in affected)
            {
                CheckAutoAdvance(r);
            }
            return remaining;
        }

        private static void MarkAffected(List<Race> affected, Race r)
        {
            if (!affected.Contains(r))
            {
                affected.Add(r);
            }
        }

        // RaceIndexNum extracts the trailing integer from a race ID like "W2-13".
        // Falls back to 0 for IDs without a numeric suffix (e.g. "L1-extra").
        internal static int RaceIndexNum(string id)
        {
            int dash = id.LastIndexOf('-');
            if (dash < 0 || dash == id.Length - 1)
            {
                return 0;
            }
            int n;
            if (!Go.Atoi(id.Substring(dash + 1), out n))
            {
                return 0;
            }
            return n;
        }

        // LosersDropRound is the absolute round of the redemption races that take
        // winners-round wRound's droppers, read off the FromWinnersRound stamped at
        // generation. Brackets generated before that field existed (already-saved state)
        // carry 0 everywhere and fall back to the old winners-round-N → L_N mapping, so
        // an in-progress bracket keeps advancing exactly as it did.
        internal int LosersDropRound(int wRound)
        {
            int best = 0;
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == BracketSlot.Losers && r.FromWinnersRound == wRound)
                {
                    if (best == 0 || r.Round < best)
                    {
                        best = r.Round;
                    }
                }
            }
            if (best > 0)
            {
                return best;
            }
            return wRound + 1;
        }

        // PlaceLosersInBracket puts losers from winners-round N into losers-round N.
        // Uses round-robin distribution for separation. Creates a fallback race if no
        // matching losers race has room.
        private void PlaceLosersInBracket(List<string> losers, int winnersRoundLost)
        {
            if (Go.Len(losers) == 0)
            {
                return;
            }
            int absoluteRound = LosersDropRound(winnersRoundLost);
            int maxL = 0;
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == BracketSlot.Losers && r.Round > maxL)
                {
                    maxL = r.Round;
                }
            }
            if (maxL == 0)
            {
                return;
            }
            if (absoluteRound > maxL)
            {
                absoluteRound = maxL;
            }

            List<Race> candidates = new List<Race>();
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == BracketSlot.Losers && r.Round == absoluteRound && !r.IsComplete && Go.Len(r.Pilots) < cfg.PilotsPerRace)
                {
                    candidates.Add(r);
                }
            }
            if (candidates.Count == 0)
            {
                // Fallback: any incomplete losers race with room.
                foreach (Race r in Go.Iter(state.Races))
                {
                    if (r.Bracket == BracketSlot.Losers && !r.IsComplete && Go.Len(r.Pilots) < cfg.PilotsPerRace)
                    {
                        candidates.Add(r);
                    }
                }
            }
            if (candidates.Count == 0)
            {
                // Dynamically create a new race for them.
                int counter = absoluteRound - 1;
                Race newRace = new Race
                {
                    Id = "L" + Go.Itoa(counter) + "-extra",
                    Round = absoluteRound,
                    Bracket = BracketSlot.Losers,
                };
                AddRace(newRace);
                candidates = new List<Race> { newRace };
            }
            Go.SortStable(candidates, (a, b) => string.CompareOrdinal(a.Id, b.Id) < 0);

            List<Race> affected = new List<Race>();
            // Drop into the LEAST-FILLED candidate first. PlaceLosersInBracket is
            // called once per source W-race completion, so a fixed round-robin
            // across calls always picks candidates[0] first — that's how W2-1's
            // dropper AND W2-2's dropper both ended up in L2-1, giving L2-1 four
            // pilots and L2-2 two (auto-advance). Least-filled balances across
            // calls automatically.
            foreach (string l in losers)
            {
                Race pick = null;
                foreach (Race r in candidates)
                {
                    if (r.FinalTarget == 0 && Go.Len(r.Pilots) >= cfg.PilotsPerRace)
                    {
                        continue;
                    }
                    if (pick == null || Go.Len(r.Pilots) < Go.Len(pick.Pilots))
                    {
                        pick = r;
                    }
                }
                if (pick == null)
                {
                    break;
                }
                pick.Pilots = Go.Append(pick.Pilots, l);
                MarkAffected(affected, pick);
            }
            // NB: Go iterates a map here (random order); insertion order is used.
            foreach (Race r in affected)
            {
                CheckAutoAdvance(r);
            }
        }

        // PopulateChaseTheAceFinalists fills every pre-created CTA race with the
        // finalists once both bracket sides are done. All CTA races carry the same
        // finalist roster so any of them can be exported to the timing system ahead
        // of time.
        private void PopulateChaseTheAceFinalists()
        {
            ChaseTheAceState cta = state.ChaseTheAce;
            if (cta == null || CtaStarted())
            {
                // No CTA, or the series has already started → the finalist set is frozen
                // (editing a feeder is then blocked by the ordinary downstream check).
                return;
            }

            int want = CtaFinalistCount();
            List<string> finalists = DeriveChaseTheAceFinalists(want);
            if (Go.Len(finalists) < want)
            {
                // A feeder is still incomplete — e.g. a winners/redemption final was just
                // reset for re-entry. Drop any provisional set so the final lineup can't
                // go stale; it re-derives when the feeder is re-run.
                ClearProvisionalFinalists();
                return;
            }
            finalists = finalists.GetRange(0, want);
            cta.Finalists = finalists;
            if (cta.Points == null)
            {
                cta.Points = new GoMap<int>();
            }
            if (cta.Wins == null)
            {
                cta.Wins = new GoMap<int>();
            }
            foreach (string p in finalists)
            {
                cta.Wins[p] = 0;
                cta.Points[p] = 0;
            }
            // Seed the finalists into EVERY pre-created CTA race (not just the first)
            // so all heats can be exported up front. Skip voided/completed races.
            foreach (Race r in ChaseTheAceRaces())
            {
                if (r.IsVoid || r.IsComplete)
                {
                    continue;
                }
                r.Pilots = Go.Copy(finalists);
            }
        }

        // CtaStarted reports whether the chase-the-ace series has actually begun — a real
        // finals heat has been RUN (recording a win) or the champion is decided. Keyed on
        // wins, NOT "any CTA heat complete": single elimination spreads winners across the
        // pre-created CTA heats and then auto-completes any left holding a lone pilot
        // (CheckAutoAdvanceAll), and those placeholders never run through AdvanceChaseTheAce
        // so they leave wins at zero. Until a real heat runs the finalist set is provisional
        // and the winners/redemption finals stay editable.
        private bool CtaStarted()
        {
            ChaseTheAceState cta = state.ChaseTheAce;
            if (cta == null)
            {
                return false;
            }
            if (cta.IsComplete || cta.Champion != "")
            {
                return true;
            }
            if (cta.Wins != null)
            {
                foreach (int w in cta.Wins.Values)
                {
                    if (w > 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // ClearProvisionalFinalists drops a not-yet-locked finalist set so a feeder reset
        // before the series starts doesn't leave a stale final lineup; it re-derives once
        // the feeder is re-run. It deliberately leaves the CTA heats' Pilots alone: under
        // single elimination those are an accumulator Advance() fills incrementally as
        // semifinals complete, and under double elimination they're overwritten when the
        // finalists re-resolve. The all-pilots-zero Wins/Points maps from
        // InitChaseTheAceState are also left intact (AdvanceChaseTheAce only reads finalist
        // entries; standings only iterate the current finalists).
        private void ClearProvisionalFinalists()
        {
            ChaseTheAceState cta = state.ChaseTheAce;
            if (cta == null)
            {
                return;
            }
            cta.Finalists = null;
        }

        // DeriveChaseTheAceFinalists computes the CTA finalists from the resolved
        // winners/redemption finals (or the semifinal for single elimination), or returns
        // fewer than `want` (caller treats that as "not ready") while a side is incomplete.
        private List<string> DeriveChaseTheAceFinalists(int want)
        {
            List<string> finalists = new List<string>();
            if (cfg.EliminationType == EliminationType.Single)
            {
                // Get the finalists from the semifinal round (round before CTA).
                Race ctaRace = FindChaseTheAceRace();
                if (ctaRace == null)
                {
                    return null;
                }
                if (Go.Len(ctaRace.Pilots) >= want)
                {
                    finalists.AddRange(ctaRace.Pilots.GetRange(0, want));
                }
                else
                {
                    int semiRound = ctaRace.Round - 1;
                    foreach (Race r in Go.Iter(state.Races))
                    {
                        if (r.Round == semiRound && r.Bracket == BracketSlot.Winners && r.IsComplete)
                        {
                            finalists.AddRange(Go.Iter(r.Winners));
                        }
                    }
                }
            }
            else
            {
                List<string> w = GetWinnersFinalists();
                List<string> l = GetLosersFinalists();
                if (w == null || l == null)
                {
                    return null; // sides not both complete yet
                }
                // Target `want` CTA finalists (usually 4): ideally split evenly
                // across the two sides. When either side has more (tight
                // ppr/advance ratios converge the bracket to 4-pilot rounds
                // instead of 2-pilot ones), take the top of each. When a side has
                // fewer, top up from the other.
                int target = want;
                int wTake = target / 2;
                int lTake = target - wTake;
                if (wTake > w.Count)
                {
                    lTake += wTake - w.Count;
                    wTake = w.Count;
                }
                if (lTake > l.Count)
                {
                    int extra = lTake - l.Count;
                    lTake = l.Count;
                    if (wTake + extra <= w.Count)
                    {
                        wTake += extra;
                    }
                }
                finalists.AddRange(w.GetRange(0, wTake));
                finalists.AddRange(l.GetRange(0, lTake));
            }
            return finalists;
        }

        // CheckAutoAdvance marks a race complete when it has the right number of pilots
        // and no more can arrive. E.g. a 2-pilot race with pilotsToAdvance=2 needs no run.
        private void CheckAutoAdvance(Race r)
        {
            if (r.IsComplete || r.IsVoid)
            {
                return;
            }
            if (CanReceiveMorePilotsInternal(r))
            {
                return;
            }
            bool isFinal = r.Round == state.Rounds || r.Bracket == BracketSlot.GrandFinal || r.Bracket == BracketSlot.ChaseTheAce;
            if (isFinal)
            {
                if (Go.Len(r.Pilots) == 1)
                {
                    r.Winners = Go.Copy(r.Pilots);
                    r.IsComplete = true;
                }
                return;
            }
            if (Go.Len(r.Pilots) == 0)
            {
                return;
            }
            // Only a genuine solo bye — a single pilot with nobody else able to arrive —
            // auto-advances, because one pilot can't race. A heat with two or more pilots
            // is still flown on the day even when everyone in it advances (not enough
            // pilots to eliminate anyone), so it stays open for the race director to
            // record the actual finishing order rather than being auto-resulted before
            // the pilots have raced.
            if (Go.Len(r.Pilots) == 1)
            {
                r.Winners = Go.Copy(r.Pilots);
                r.Losers = Go.Truncate(r.Losers);
                r.IsComplete = true;
                Advance(r);
            }
        }

        private void CheckAutoAdvanceAll()
        {
            // Snapshot references — Advance() may append to state.Races.
            List<Race> snapshot = new List<Race>(Go.Iter(state.Races));
            foreach (Race r in snapshot)
            {
                if (!r.IsComplete && !r.IsVoid && Go.Len(r.Pilots) > 0)
                {
                    CheckAutoAdvance(r);
                }
            }
        }

        /// <summary>
        /// CanReceiveMorePilots reports whether a race is still waiting on upstream
        /// results to deliver more pilots — used by the runner UI to block submitting
        /// a result until everyone the race expects has actually arrived.
        /// </summary>
        public bool CanReceiveMorePilots(Race target)
        {
            return CanReceiveMorePilotsInternal(target);
        }

        private bool CanReceiveMorePilotsInternal(Race target)
        {
            if (Go.Len(target.Pilots) >= cfg.PilotsPerRace)
            {
                return false;
            }
            switch (target.Bracket)
            {
                case BracketSlot.Losers:
                    foreach (Race r in Go.Iter(state.Races))
                    {
                        if (r.Bracket == BracketSlot.Winners && !r.IsComplete && Go.Len(r.Pilots) > 0)
                        {
                            return true;
                        }
                    }
                    foreach (Race r in Go.Iter(state.Races))
                    {
                        if (r.Bracket == BracketSlot.Losers && r.Round < target.Round && !r.IsComplete && Go.Len(r.Pilots) > 0)
                        {
                            return true;
                        }
                    }
                    break;
                case BracketSlot.Winners:
                    foreach (Race r in Go.Iter(state.Races))
                    {
                        if (r.Bracket == BracketSlot.Winners && r.Round < target.Round && !r.IsComplete && Go.Len(r.Pilots) > 0)
                        {
                            return true;
                        }
                    }
                    break;
                case BracketSlot.GrandFinal:
                    foreach (Race r in Go.Iter(state.Races))
                    {
                        if ((r.Bracket == BracketSlot.Winners || r.Bracket == BracketSlot.Losers) && !r.IsComplete && Go.Len(r.Pilots) > 0)
                        {
                            return true;
                        }
                    }
                    break;
            }
            return false;
        }

        // ---------- queries ----------

        /// <summary>Champion returns the tournament winner, or empty string if not yet determined.</summary>
        public string Champion()
        {
            if (cfg.FinalsFormat == FinalsFormat.ChaseTheAce)
            {
                if (state.ChaseTheAce != null && state.ChaseTheAce.IsComplete)
                {
                    return state.ChaseTheAce.Champion ?? "";
                }
                return "";
            }
            if (cfg.EliminationType == EliminationType.Single)
            {
                Race final = LastWinnersRace();
                if (final != null && final.IsComplete && Go.Len(final.Winners) > 0)
                {
                    return final.Winners[0];
                }
                return "";
            }
            Race gf2 = FindRace("GF-2");
            if (gf2 != null && gf2.IsComplete && Go.Len(gf2.Winners) > 0)
            {
                return gf2.Winners[0];
            }
            Race gf1 = FindRace("GF-1");
            if (gf1 != null && gf1.IsComplete && Go.Len(gf1.Winners) > 0)
            {
                List<string> winnersFinalists = GetWinnersFinalists();
                if (StringInSlice(gf1.Winners[0], winnersFinalists))
                {
                    return gf1.Winners[0];
                }
            }
            return "";
        }

        /// <summary>IsComplete returns true if a champion has been determined.</summary>
        public bool IsComplete()
        {
            return Champion() != "";
        }

        // ---------- internal helpers ----------

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

        private Race FindChaseTheAceRace()
        {
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == BracketSlot.ChaseTheAce)
                {
                    return r;
                }
            }
            return null;
        }

        // ChaseTheAceRaces returns every chase-the-ace finals race (including voided
        // ones) sorted by their trailing race index (CTA-1, CTA-2, …). Voided races
        // stay in the list so callers that care about IDs see the full set; callers
        // that only want live races should filter on !r.IsVoid.
        internal List<Race> ChaseTheAceRaces()
        {
            List<Race> output = new List<Race>();
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == BracketSlot.ChaseTheAce)
                {
                    output.Add(r);
                }
            }
            Go.SortStable(output, (a, b) => RaceIndexNum(a.Id) < RaceIndexNum(b.Id));
            return output;
        }

        // NextChaseTheAceRace returns the lowest-numbered chase-the-ace race that is
        // neither complete nor voided — the only CTA race allowed to receive a result.
        // Returns null when none remain (champion crowned or all run).
        internal Race NextChaseTheAceRace()
        {
            foreach (Race r in ChaseTheAceRaces())
            {
                if (r.IsVoid)
                {
                    continue;
                }
                if (!r.IsComplete)
                {
                    return r;
                }
            }
            return null;
        }

        // CtaFinalistCount is how many finalists the chase-the-ace round runs with.
        // The bracket converges toward 4 finalists; with fewer than 4 pilots overall
        // every pilot is a finalist. This is used both to size the pre-created CTA
        // race set and (defensively) elsewhere.
        internal int CtaFinalistCount()
        {
            return CtaFinalists(cfg);
        }

        // CtaFinalists is how many pilots contest the chase-the-ace final: a full heat
        // (PilotsPerRace), capped by the field size. It follows PilotsPerRace rather
        // than a fixed 4 because the final is a heat like any other — a club racing
        // 6-up finishes with six finalists, a 3-up club with three (four pilots would
        // not fit the heat at all). Legacy/zero configs fall back to the historical 4.
        internal static int CtaFinalists(BracketConfig cfg)
        {
            int ppr = cfg.PilotsPerRace;
            if (ppr < 2)
            {
                ppr = 4;
            }
            return Go.MinInt(ppr, cfg.PilotCount);
        }

        // MaxChaseTheAceRaces returns the maximum number of CTA heats that could ever
        // be required: every finalist but one wins (requiredWins-1) heats without
        // anyone clinching, then one more heat decides it. That worst case is
        // (requiredWins-1)*finalistCount + 1 races. Pre-creating this many lets the
        // race director export all CTA heats to the timing system up front.
        internal static int MaxChaseTheAceRaces(int finalistCount, int requiredWins)
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

        internal List<Race> RacesInRound(int round)
        {
            List<Race> output = new List<Race>();
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Round == round)
                {
                    output.Add(r);
                }
            }
            return output;
        }

        internal List<Race> RacesInBracketRound(int round, string slot)
        {
            List<Race> output = new List<Race>();
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Round == round && r.Bracket == slot)
                {
                    output.Add(r);
                }
            }
            return output;
        }

        internal int WinnersRoundCount()
        {
            HashSet<int> seen = new HashSet<int>();
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == BracketSlot.Winners)
                {
                    seen.Add(r.Round);
                }
            }
            return seen.Count;
        }

        private int TotalWinnersInRound(int round)
        {
            int total = 0;
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Round == round && r.IsComplete)
                {
                    total += Go.Len(r.Winners);
                }
            }
            return total;
        }

        private int EstimatedNextRoundPilots(int round)
        {
            int total = 0;
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Round != round || r.Bracket != BracketSlot.Winners)
                {
                    continue;
                }
                if (r.IsComplete)
                {
                    total += Go.Len(r.Winners);
                }
                else if (Go.Len(r.Pilots) > 0)
                {
                    total += Go.MinInt(cfg.PilotsToAdvance, Go.Len(r.Pilots));
                }
            }
            return total;
        }

        private Race LastWinnersRace()
        {
            Race last = null;
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == BracketSlot.Winners)
                {
                    last = r;
                }
            }
            return last;
        }

        // GetWinnersFinalists collects every winners-bracket race in the highest
        // round, sorted by trailing race index, then concatenates the winners. This
        // matters when the last winners round has more than one race (e.g. tight
        // configs like 3-per-race/2-advance/4-finalists, where two parallel 2-pilot
        // auto-advancing heats produce 4 finalists across 2 races). Returns null if
        // any race in the last round is incomplete — the caller waits.
        internal List<string> GetWinnersFinalists()
        {
            return SideFinalists(BracketSlot.Winners);
        }

        // GetLosersFinalists is the losers-side counterpart to GetWinnersFinalists.
        internal List<string> GetLosersFinalists()
        {
            return SideFinalists(BracketSlot.Losers);
        }

        private List<string> SideFinalists(string slot)
        {
            int maxRound = 0;
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == slot && r.Round > maxRound)
                {
                    maxRound = r.Round;
                }
            }
            if (maxRound == 0)
            {
                return null;
            }
            List<Race> lastRound = new List<Race>();
            foreach (Race r in Go.Iter(state.Races))
            {
                if (r.Bracket == slot && r.Round == maxRound)
                {
                    if (!r.IsComplete)
                    {
                        return null;
                    }
                    lastRound.Add(r);
                }
            }
            Go.SortStable(lastRound, (a, b) => RaceIndexNum(a.Id) < RaceIndexNum(b.Id));
            List<string> output = new List<string>();
            foreach (Race r in lastRound)
            {
                output.AddRange(Go.Iter(r.Winners));
            }
            return output;
        }

        // ---------- small utilities ----------

        internal static int CountRacesInRound(List<Race> races, int round)
        {
            int n = 0;
            foreach (Race r in Go.Iter(races))
            {
                if (r.Round == round)
                {
                    n++;
                }
            }
            return n;
        }

        internal static int CountRacesInBracketRound(List<Race> races, int round, string slot)
        {
            int n = 0;
            foreach (Race r in Go.Iter(races))
            {
                if (r.Round == round && r.Bracket == slot)
                {
                    n++;
                }
            }
            return n;
        }

        private static HashSet<string> StringSet(IEnumerable<string> ss)
        {
            HashSet<string> output = new HashSet<string>();
            foreach (string s in Go.Iter(ss))
            {
                output.Add(s);
            }
            return output;
        }

        private static bool StringInSlice(string s, List<string> ss)
        {
            foreach (string x in Go.Iter(ss))
            {
                if (x == s)
                {
                    return true;
                }
            }
            return false;
        }

        private static List<string> AppendUnique(List<string> dst, List<string> vals)
        {
            HashSet<string> seen = StringSet(dst);
            foreach (string v in Go.Iter(vals))
            {
                if (seen.Contains(v))
                {
                    continue;
                }
                seen.Add(v);
                dst = Go.Append(dst, v);
            }
            return dst;
        }

        internal static bool HasChaseTheAceRace(List<Race> races)
        {
            foreach (Race r in Go.Iter(races))
            {
                if (r.Bracket == BracketSlot.ChaseTheAce)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
