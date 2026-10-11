using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Brackets.Tests
{
    /// <summary>
    /// Port of internal/platform/bracket/bracket_test.go. Go's t.Errorf (record and
    /// continue) is modelled by collecting messages in an error list that is asserted
    /// empty at the end of each test; t.Fatalf throws immediately.
    /// </summary>
    public class BracketTests
    {
        private class T
        {
            public readonly List<string> Errors = new List<string>();
            public string Prefix = "";

            public void Errorf(string msg)
            {
                Errors.Add(Prefix + msg);
            }

            public void Fatalf(string msg)
            {
                Errors.Add(Prefix + msg);
                throw new Xunit.Sdk.XunitException(string.Join("\n", Errors));
            }

            public void Done()
            {
                Assert.True(Errors.Count == 0, string.Join("\n", Errors));
            }
        }

        private static string Fmt(IEnumerable<string> s)
        {
            return s == null ? "[]" : "[" + string.Join(" ", s) + "]";
        }

        // PlayOutTournament drives the bracket to completion by repeatedly picking the
        // lowest-round incomplete race that has all its pilots and selecting the top N.
        // Returns once no more progress is possible.
        private static void PlayOutTournament(T t, Bracket b)
        {
            int safety = 2000;
            while (safety > 0)
            {
                safety--;
                List<Race> incomplete = new List<Race>();
                foreach (Race r in b.State().Races)
                {
                    if (!r.IsComplete && !r.IsVoid && Go.Len(r.Pilots) > 0)
                    {
                        incomplete.Add(r);
                    }
                }
                if (incomplete.Count == 0)
                {
                    break;
                }
                Go.SortStable(incomplete, (x, y) =>
                {
                    if (x.Round != y.Round)
                    {
                        return x.Round < y.Round;
                    }
                    // Within a round, order numerically by trailing index so CTA-2
                    // comes before CTA-10 (sequential CTA gating needs this).
                    int ni = Bracket.RaceIndexNum(x.Id), nj = Bracket.RaceIndexNum(y.Id);
                    if (ni != nj)
                    {
                        return ni < nj;
                    }
                    return string.CompareOrdinal(x.Id, y.Id) < 0;
                });
                Race race = incomplete[0];
                // Use the bracket library's own expected count — it knows about
                // consolidation races' FinalTarget overrides and single-elim finals.
                int winnerCount = b.ExpectedWinnersCount(race);
                if (winnerCount > race.Pilots.Count)
                {
                    winnerCount = race.Pilots.Count;
                }
                if (winnerCount < 1)
                {
                    winnerCount = 1;
                }
                List<string> winners = race.Pilots.GetRange(0, winnerCount);
                try
                {
                    b.SelectWinners(race.Id, winners);
                }
                catch (BracketException e)
                {
                    t.Fatalf("SelectWinners(" + race.Id + ") failed: " + e.Message);
                }
            }
            if (safety == 0)
            {
                t.Fatalf("play-out exceeded safety limit");
            }
        }

        private static List<string> PilotNames(int n)
        {
            List<string> output = new List<string>();
            for (int i = 0; i < n; i++)
            {
                output.Add("P" + (i + 1));
            }
            return output;
        }

        private static void ValidateInitialBracket(T t, Bracket b)
        {
            BracketConfig cfg = b.Config();
            BracketState state = b.State();

            // All pilots placed in round 1
            List<Race> round1 = state.Races.Where(r => r.Round == 1).ToList();
            int placed = 0;
            foreach (Race r in round1)
            {
                placed += Go.Len(r.Pilots);
            }
            // For chase-the-ace with ≤4 pilots, all pilots may land directly in CTA-1, not W1
            if (cfg.FinalsFormat == FinalsFormat.ChaseTheAce && cfg.EliminationType == EliminationType.Single && cfg.PilotCount <= 4)
            {
                // pilots already in CTA-1 — check round 1 has them
                foreach (Race r in state.Races)
                {
                    if (r.Round == 1 && r.Bracket == BracketSlot.ChaseTheAce)
                    {
                        placed = Go.Len(r.Pilots);
                    }
                }
            }
            if (placed != cfg.PilotCount)
            {
                t.Errorf("placed " + placed + " pilots in round 1, want " + cfg.PilotCount);
            }

            // Round 1 race sizes within ±1 (snake seeding)
            if (round1.Count > 1)
            {
                int minS = Go.Len(round1[0].Pilots), maxS = Go.Len(round1[0].Pilots);
                foreach (Race r in round1)
                {
                    minS = Math.Min(minS, Go.Len(r.Pilots));
                    maxS = Math.Max(maxS, Go.Len(r.Pilots));
                }
                if (maxS - minS > 1)
                {
                    t.Errorf("round 1 race sizes unbalanced: min=" + minS + " max=" + maxS);
                }
            }

            // No race exceeds capacity
            foreach (Race r in state.Races)
            {
                if (Go.Len(r.Pilots) > cfg.PilotsPerRace)
                {
                    t.Errorf("race " + r.Id + " has " + Go.Len(r.Pilots) + " pilots (max " + cfg.PilotsPerRace + ")");
                }
            }
        }

        private static void ValidateFinalState(T t, Bracket b)
        {
            foreach (Race r in b.State().Races)
            {
                if (!r.IsComplete && !r.IsVoid && Go.Len(r.Pilots) > 0)
                {
                    t.Errorf("race " + r.Id + " left incomplete with " + Go.Len(r.Pilots) + " pilots");
                }
            }
            BracketConfig cfg = b.Config();
            if (cfg.FinalsFormat == FinalsFormat.ChaseTheAce && cfg.PilotCount >= 4)
            {
                if (b.State().ChaseTheAce == null || b.State().ChaseTheAce.Champion == "")
                {
                    t.Errorf("expected chase-the-ace champion to be set");
                }
            }
        }

        public static IEnumerable<object[]> AllPilotCountConfigs()
        {
            yield return new object[] { "double+CTA", EliminationType.Double, FinalsFormat.ChaseTheAce };
            yield return new object[] { "single+CTA", EliminationType.Single, FinalsFormat.ChaseTheAce };
            yield return new object[] { "double+GF", EliminationType.Double, FinalsFormat.GrandFinal };
            yield return new object[] { "single+final", EliminationType.Single, FinalsFormat.GrandFinal };
        }

        [Theory]
        [MemberData(nameof(AllPilotCountConfigs))]
        public void TestBracketAllPilotCounts(string label, string eliminationType, string finalsFormat)
        {
            T t = new T();
            for (int pilotCount = 2; pilotCount <= 96; pilotCount++)
            {
                t.Prefix = label + "/" + pilotCount + "_pilots: ";
                Bracket b = Bracket.New(new BracketConfig
                {
                    PilotCount = pilotCount,
                    PilotNames = PilotNames(pilotCount),
                    EliminationType = eliminationType,
                    PilotsPerRace = 4,
                    PilotsToAdvance = 2,
                    FinalsFormat = finalsFormat,
                    AceWinsRequired = 2,
                });
                ValidateInitialBracket(t, b);
                PlayOutTournament(t, b);
                ValidateFinalState(t, b);
            }
            t.Done();
        }

        public static IEnumerable<object[]> TightConfigs()
        {
            int[][] matrix = new int[][]
            {
                new int[] { 3, 2 }, // ← user's case
                new int[] { 4, 2 },
                new int[] { 5, 2 },
                new int[] { 6, 2 },
                new int[] { 6, 3 },
                new int[] { 8, 4 },
            };
            foreach (int[] m in matrix)
            {
                yield return new object[] { "double+CTA", FinalsFormat.ChaseTheAce, m[0], m[1] };
                yield return new object[] { "double+GF", FinalsFormat.GrandFinal, m[0], m[1] };
            }
        }

        // TestBracketCompletesTightConfigs covers double-elim configurations where
        // the winners-bracket's adv/ppr ratio is tight enough that convergence
        // stalls without the "stop when no progress" guard. The hang the user
        // reported (19 pilots × 3-per-race × 2-advance × double+CTA) is in this set.
        //
        // Scope: realistic tournament sizes (≥ ppr*2 + 1 pilots so there's room
        // for some elimination), double-elim only — single-elim with adv close to
        // ppr has separate convergence issues that aren't what the user reported.
        [Theory]
        [MemberData(nameof(TightConfigs))]
        public void TestBracketCompletesTightConfigs(string label, string final, int ppr, int adv)
        {
            T t = new T();
            // Skip tiny brackets where CTA can't be filled
            int start = ppr * 2 + 1;
            if (start < 8)
            {
                start = 8;
            }
            for (int pc = start; pc <= 64; pc++)
            {
                t.Prefix = label + "_pp" + ppr + "_adv" + adv + "/" + pc + "_pilots: ";
                Bracket b = Bracket.New(new BracketConfig
                {
                    PilotCount = pc,
                    PilotNames = PilotNames(pc),
                    EliminationType = EliminationType.Double,
                    PilotsPerRace = ppr,
                    PilotsToAdvance = adv,
                    FinalsFormat = final,
                    AceWinsRequired = 2,
                });
                PlayOutTournament(t, b);

                // (a) no race left with pilots but incomplete
                foreach (Race r in b.State().Races)
                {
                    if (!r.IsComplete && !r.IsVoid && Go.Len(r.Pilots) > 0)
                    {
                        t.Errorf("stuck race " + r.Id + " round=" + r.Round + " slot=" + r.Bracket + " pilots=" + Fmt(r.Pilots) + " expected=" + b.ExpectedWinnersCount(r));
                    }
                }
                // (b) finals format actually produces a champion
                if (final == FinalsFormat.ChaseTheAce)
                {
                    ChaseTheAceState cta = b.State().ChaseTheAce;
                    if (cta == null || !cta.IsComplete)
                    {
                        t.Errorf("CTA never completed; champion=" + Go.Quote(b.Champion()));
                    }
                }
                else if (b.Champion() == "")
                {
                    t.Errorf("no champion was crowned");
                }
            }
            t.Done();
        }

        [Fact]
        public void TestSnakeSeeding()
        {
            T t = new T();
            // 13 pilots / 4 races should snake to 3+3+3+4, top seeds spread out.
            List<List<string>> groups = Bracket.CreateBalancedSeeding(PilotNames(13), 4);
            if (groups.Count != 4)
            {
                t.Fatalf("expected 4 races, got " + groups.Count);
            }
            int[] sizes = new int[] { groups[0].Count, groups[1].Count, groups[2].Count, groups[3].Count };
            int[] want = new int[] { 3, 3, 3, 4 };
            for (int i = 0; i < want.Length; i++)
            {
                if (sizes[i] != want[i])
                {
                    t.Errorf("race " + i + " size = " + sizes[i] + ", want " + want[i]);
                }
            }
            // Seed 1 (P1) and seed 2 (P2) must be in different races.
            string r0 = groups[0][0], r1 = groups[1][0];
            if (r0 != "P1" || r1 != "P2")
            {
                t.Errorf("expected seed 1 in race 0 and seed 2 in race 1, got " + Go.Quote(r0) + " and " + Go.Quote(r1));
            }
            t.Done();
        }

        [Fact]
        public void TestPlaceBalanced()
        {
            T t = new T();
            // 6 W1 winners feeding 2 W2 races should produce 3+3, not 4+2,
            // regardless of which W1 race completes first.
            Bracket b = Bracket.New(new BracketConfig
            {
                PilotCount = 9,
                PilotNames = PilotNames(9),
                EliminationType = EliminationType.Double,
                PilotsPerRace = 4,
                PilotsToAdvance = 2,
                FinalsFormat = FinalsFormat.ChaseTheAce,
                AceWinsRequired = 2,
            });

            // Find W1 races and complete each with the first 2 pilots as winners.
            List<Race> w1 = b.State().Races.Where(r => r.Round == 1).ToList();
            Go.SortStable(w1, (x, y) => string.CompareOrdinal(x.Id, y.Id) < 0);
            foreach (Race r in w1)
            {
                List<string> winners = r.Pilots.GetRange(0, 2);
                b.SelectWinners(r.Id, winners);
            }
            // Check W2-1 and W2-2 sizes
            List<Race> w2 = b.State().Races.Where(r => r.Round == 2 && r.Bracket == BracketSlot.Winners).ToList();
            if (w2.Count != 2)
            {
                t.Fatalf("expected 2 W2 races, got " + w2.Count);
            }
            Go.SortStable(w2, (x, y) => string.CompareOrdinal(x.Id, y.Id) < 0);
            if (Go.Len(w2[0].Pilots) != 3 || Go.Len(w2[1].Pilots) != 3)
            {
                t.Errorf("expected W2-1=3, W2-2=3, got " + Go.Len(w2[0].Pilots) + " and " + Go.Len(w2[1].Pilots));
            }
            t.Done();
        }

        private static Bracket New8CTA(int aceWins)
        {
            return Bracket.New(new BracketConfig
            {
                PilotCount = 8,
                PilotNames = PilotNames(8),
                EliminationType = EliminationType.Double,
                PilotsPerRace = 4,
                PilotsToAdvance = 2,
                FinalsFormat = FinalsFormat.ChaseTheAce,
                AceWinsRequired = aceWins,
            });
        }

        // TestSelectFullPlacement verifies that recording the complete finish order
        // of a race (a) reorders race.Pilots to placement order so the loser ranks
        // are preserved for display, and (b) extracts the right N as winners.
        [Fact]
        public void TestSelectFullPlacement()
        {
            T t = new T();
            Bracket b = New8CTA(0);
            // W1-1 has 4 pilots after snake seeding. Pick an order that's NOT the
            // initial pilot order so we know reordering happened.
            Race w1 = b.State().Races.FirstOrDefault(r => r.Id == "W1-1");
            if (w1 == null)
            {
                t.Fatalf("W1-1 missing");
            }
            if (Go.Len(w1.Pilots) != 4)
            {
                t.Fatalf("expected 4 pilots in W1-1, got " + Go.Len(w1.Pilots));
            }
            List<string> original = new List<string>(w1.Pilots);
            // Reverse the order
            List<string> placement = new List<string> { original[3], original[2], original[1], original[0] };
            b.SelectFullPlacement("W1-1", placement);
            if (!Equal(w1.Pilots, placement))
            {
                t.Errorf("race.Pilots not reordered to placement order: got " + Fmt(w1.Pilots) + " want " + Fmt(placement));
            }
            List<string> wantWinners = placement.GetRange(0, 2);
            if (!Equal(w1.Winners, wantWinners))
            {
                t.Errorf("Winners = " + Fmt(w1.Winners) + ", want " + Fmt(wantWinners) + " (top 2 from placement)");
            }
            if (!Equal(w1.Losers, placement.GetRange(2, 2)))
            {
                t.Errorf("Losers = " + Fmt(w1.Losers) + ", want placement[2:]");
            }
            t.Done();
        }

        // TestEditRaceWhileDownstreamClean covers the happy path: edit a race whose
        // winners landed in a still-incomplete next-round race. The old winners get
        // stripped from the downstream race; the new winners get placed.
        [Fact]
        public void TestEditRaceWhileDownstreamClean()
        {
            T t = new T();
            Bracket b = New8CTA(0);
            Race w1 = b.State().Races.First(r => r.Id == "W1-1");
            List<string> original = new List<string>(w1.Pilots);
            // Initial result — accept the first 2 as winners.
            b.SelectFullPlacement("W1-1", original);
            List<string> firstWinners = new List<string>(w1.Winners);

            // Find a downstream race that received one of the winners; it should be
            // incomplete at this point.
            if (!w1.IsComplete)
            {
                t.Fatalf("W1-1 should be complete after initial submit");
            }
            string reason;
            bool canEdit = b.CanEditRace("W1-1", out reason);
            if (!canEdit)
            {
                t.Fatalf("expected CanEdit true; got false: " + reason);
            }

            // Edit with reversed order so the previous losers are now winners.
            List<string> newPlacement = new List<string> { original[3], original[2], original[1], original[0] };
            b.EditRace("W1-1", newPlacement);
            if (!w1.IsComplete)
            {
                t.Fatalf("W1-1 should still be complete after edit");
            }
            if (Equal(w1.Winners, firstWinners))
            {
                t.Errorf("winners should have changed after edit; still " + Fmt(w1.Winners));
            }
            if (!Equal(w1.Winners, newPlacement.GetRange(0, 2)))
            {
                t.Errorf("Winners = " + Fmt(w1.Winners) + ", want " + Fmt(newPlacement.GetRange(0, 2)));
            }

            // The new winners must now appear in a W2 race (where the old winners
            // used to be). The old winners (now losers) must NOT remain in any
            // winners-bracket downstream race — they should be in the losers
            // bracket if anything.
            HashSet<string> newWinSet = new HashSet<string>(w1.Winners);
            HashSet<string> oldWinSet = new HashSet<string>(firstWinners);
            int foundInW2 = 0;
            foreach (Race r in b.State().Races)
            {
                if (r.Id == "W1-1" || r.Bracket != BracketSlot.Winners || r.Round <= 1)
                {
                    continue;
                }
                foreach (string p in Go.Iter(r.Pilots))
                {
                    if (newWinSet.Contains(p))
                    {
                        foundInW2++;
                    }
                    if (oldWinSet.Contains(p) && !newWinSet.Contains(p))
                    {
                        t.Errorf("old winner " + Go.Quote(p) + " still in winners-bracket race " + r.Id + " after edit");
                    }
                }
            }
            if (foundInW2 != w1.Winners.Count)
            {
                t.Errorf("expected " + w1.Winners.Count + " new winners in W2 races, found " + foundInW2);
            }
            t.Done();
        }

        // TestEditRaceBlockedByCompletedDownstream verifies that once any downstream
        // race has been run, the edit is refused — editing would invalidate work
        // already recorded. (Go's t.Skip branches become early returns.)
        [Fact]
        public void TestEditRaceBlockedByCompletedDownstream()
        {
            T t = new T();
            Bracket b = New8CTA(0);
            // Play out W1 entirely.
            foreach (Race r in new List<Race>(b.State().Races))
            {
                if (r.Round == 1 && r.Bracket == BracketSlot.Winners)
                {
                    b.SelectFullPlacement(r.Id, r.Pilots);
                }
            }
            // Find any W2 race that exists and has pilots — complete it.
            Race w2 = b.State().Races.FirstOrDefault(r => r.Round == 2 && r.Bracket == BracketSlot.Winners && Go.Len(r.Pilots) > 0 && !r.IsComplete);
            Assert.True(w2 != null, "no W2 race available to complete (Go skips here; it never does for this config)");
            b.SelectFullPlacement(w2.Id, w2.Pilots);

            // Now find a W1 race whose winners contributed to the completed W2 and
            // confirm the edit is refused.
            foreach (Race r in b.State().Races)
            {
                if (r.Round != 1 || r.Bracket != BracketSlot.Winners)
                {
                    continue;
                }
                // Is any winner from r now in w2.Pilots?
                foreach (string p in Go.Iter(r.Winners))
                {
                    foreach (string q in Go.Iter(w2.Pilots))
                    {
                        if (p == q)
                        {
                            string reason;
                            bool ok = b.CanEditRace(r.Id, out reason);
                            if (ok)
                            {
                                t.Errorf("CanEdit(" + r.Id + ") = true; expected false (downstream " + w2.Id + " is complete)");
                            }
                            if (reason == "")
                            {
                                t.Errorf("CanEdit(" + r.Id + ") returned empty reason");
                            }
                            bool threw = false;
                            try
                            {
                                b.EditRace(r.Id, r.Pilots);
                            }
                            catch (BracketException)
                            {
                                threw = true;
                            }
                            if (!threw)
                            {
                                t.Errorf("EditRace(" + r.Id + ") returned nil error; expected refusal");
                            }
                            t.Done();
                            return;
                        }
                    }
                }
            }
            Assert.Fail("no W1 race feeds the completed W2 race (Go skips here; it never does for this config)");
        }

        // ---------- chase-the-ace: pre-created max races ----------

        // CtaRaceIDsSorted returns every CTA race ID (incl. voided) in numeric order.
        private static List<string> CtaRaceIDsSorted(Bracket b)
        {
            List<string> ids = new List<string>();
            foreach (Race r in b.State().Races)
            {
                if (r.Bracket == BracketSlot.ChaseTheAce)
                {
                    ids.Add(r.Id);
                }
            }
            Go.SortStable(ids, (x, y) => Bracket.RaceIndexNum(x) < Bracket.RaceIndexNum(y));
            return ids;
        }

        // DriveNonCTASides plays out every winners/losers race (seeds win) so the CTA
        // finalists get populated, without touching the CTA finals themselves.
        private static void DriveNonCTASides(T t, Bracket b)
        {
            int safety = 2000;
            while (safety > 0)
            {
                safety--;
                bool ran = false;
                foreach (Race r in b.State().Races)
                {
                    if (r.Bracket == BracketSlot.ChaseTheAce || r.IsComplete || r.IsVoid || Go.Len(r.Pilots) == 0)
                    {
                        continue;
                    }
                    int n = b.ExpectedWinnersCount(r);
                    if (n > r.Pilots.Count)
                    {
                        n = r.Pilots.Count;
                    }
                    if (n < 1)
                    {
                        n = 1;
                    }
                    try
                    {
                        b.SelectWinners(r.Id, r.Pilots.GetRange(0, n));
                    }
                    catch (BracketException e)
                    {
                        t.Fatalf("SelectWinners(" + r.Id + "): " + e.Message);
                    }
                    ran = true;
                    break;
                }
                if (!ran)
                {
                    break;
                }
            }
            if (safety == 0)
            {
                t.Fatalf("driveNonCTASides exceeded safety limit");
            }
        }

        // TestChaseTheAceMaxRacesPreCreated asserts the number of CTA finals races
        // created up front equals (RequiredWins-1)*finalistCount + 1.
        [Theory]
        [InlineData("8_double_reqWins2", 8, 2, 5)]   // (2-1)*4 + 1 = 5
        [InlineData("8_double_reqWins3", 8, 3, 9)]   // (3-1)*4 + 1 = 9
        [InlineData("16_double_reqWins2", 16, 2, 5)]
        [InlineData("8_double_reqWins4", 8, 4, 13)]  // (4-1)*4 + 1 = 13
        public void TestChaseTheAceMaxRacesPreCreated(string name, int pilotCount, int requiredWins, int wantRaces)
        {
            T t = new T();
            t.Prefix = name + ": ";
            Bracket b = Bracket.New(new BracketConfig
            {
                PilotCount = pilotCount,
                PilotNames = PilotNames(pilotCount),
                EliminationType = EliminationType.Double,
                PilotsPerRace = 4,
                PilotsToAdvance = 2,
                FinalsFormat = FinalsFormat.ChaseTheAce,
                AceWinsRequired = requiredWins,
            });
            List<string> ids = CtaRaceIDsSorted(b);
            if (ids.Count != wantRaces)
            {
                t.Errorf("got " + ids.Count + " CTA races " + Fmt(ids) + ", want " + wantRaces);
            }
            // IDs must be the contiguous CTA-1 … CTA-max sequence.
            for (int i = 0; i < ids.Count; i++)
            {
                string want = "CTA-" + (i + 1);
                if (ids[i] != want)
                {
                    t.Errorf("CTA race " + i + " id = " + Go.Quote(ids[i]) + ", want " + Go.Quote(want));
                }
            }
            t.Done();
        }

        // TestChaseTheAceMaxFormulaHelper checks the pure max-race formula directly.
        [Fact]
        public void TestChaseTheAceMaxFormulaHelper()
        {
            T t = new T();
            int[][] cases = new int[][]
            {
                new int[] { 4, 2, 5 },
                new int[] { 4, 3, 9 },
                new int[] { 4, 4, 13 },
                new int[] { 2, 2, 3 },
                new int[] { 3, 2, 4 },
                new int[] { 4, 1, 1 }, // single win required → one race only
            };
            foreach (int[] c in cases)
            {
                int got = Bracket.MaxChaseTheAceRaces(c[0], c[1]);
                if (got != c[2])
                {
                    t.Errorf("maxChaseTheAceRaces(" + c[0] + "," + c[1] + ") = " + got + ", want " + c[2]);
                }
            }
            t.Done();
        }

        // TestChaseTheAceAllRacesCarryFinalists verifies that once the W/L sides are
        // done, EVERY pre-created CTA race holds the 4 finalists (so they can all be
        // exported to the timing system up front).
        [Fact]
        public void TestChaseTheAceAllRacesCarryFinalists()
        {
            T t = new T();
            Bracket b = New8CTA(2);
            DriveNonCTASides(t, b);
            ChaseTheAceState cta = b.State().ChaseTheAce;
            if (cta == null || Go.Len(cta.Finalists) != 4)
            {
                t.Fatalf("expected 4 finalists, got " + (cta == null ? "nil" : Fmt(cta.Finalists)));
            }
            foreach (Race r in b.State().Races)
            {
                if (r.Bracket != BracketSlot.ChaseTheAce)
                {
                    continue;
                }
                if (!Equal(r.Pilots, cta.Finalists))
                {
                    t.Errorf("CTA race " + r.Id + " pilots = " + Fmt(r.Pilots) + ", want finalists " + Fmt(cta.Finalists));
                }
            }
            t.Done();
        }

        // FindEditableCTAFeeder returns a completed non-CTA race that feeds the chase-
        // the-ace finalists, still has a non-advancing pilot to promote, and is editable
        // (its only run downstream is the un-run CTA) — i.e. the redemption final.
        private static Race FindEditableCTAFeeder(Bracket b)
        {
            ChaseTheAceState cta = b.State().ChaseTheAce;
            foreach (Race r in b.State().Races)
            {
                if (r.Bracket == BracketSlot.ChaseTheAce || !r.IsComplete || Go.Len(r.Pilots) <= Go.Len(r.Winners))
                {
                    continue;
                }
                bool feeds = false;
                foreach (string w in Go.Iter(r.Winners))
                {
                    if (Go.Iter(cta.Finalists).Contains(w))
                    {
                        feeds = true;
                        break;
                    }
                }
                if (!feeds)
                {
                    continue;
                }
                string unused;
                if (b.CanEditRace(r.Id, out unused))
                {
                    return r;
                }
            }
            return null;
        }

        // TestChaseTheAceFeederEditableUntilSeriesStarts is the regression guard for the
        // premature edit-lock: a winners/redemption final that feeds the chase-the-ace
        // final must stay editable until a CTA heat is actually run, and correcting it
        // before then must update who makes the final. Previously the finalist set locked
        // the instant both sides completed, freezing a just-entered redemption result.
        [Fact]
        public void TestChaseTheAceFeederEditableUntilSeriesStarts()
        {
            T t = new T();
            Bracket b = New8CTA(2);
            DriveNonCTASides(t, b);

            ChaseTheAceState cta = b.State().ChaseTheAce;
            if (cta == null || Go.Len(cta.Finalists) != 4)
            {
                t.Fatalf("expected 4 finalists after the sides ran, got " + (cta == null ? "nil" : Fmt(cta.Finalists)));
            }

            Race feeder = FindEditableCTAFeeder(b);
            if (feeder == null)
            {
                t.Fatalf("no editable CTA feeder found (redemption final should be editable pre-series)");
            }

            // Promote a non-advancing pilot of the feeder to 1st — this changes WHO makes
            // the final, which the finalist set must follow.
            string promoted = "";
            foreach (string p in feeder.Pilots)
            {
                if (!Go.Iter(feeder.Winners).Contains(p))
                {
                    promoted = p;
                    break;
                }
            }
            if (promoted == "")
            {
                t.Fatalf("feeder " + feeder.Id + " has no non-advancer to promote: " + Fmt(feeder.Pilots));
            }
            if (Go.Iter(cta.Finalists).Contains(promoted))
            {
                t.Fatalf("precondition: " + promoted + " already a finalist");
            }
            List<string> newOrder = new List<string> { promoted };
            foreach (string p in feeder.Pilots)
            {
                if (p != promoted)
                {
                    newOrder.Add(p);
                }
            }
            b.EditRace(feeder.Id, newOrder);
            if (!Go.Iter(b.State().ChaseTheAce.Finalists).Contains(promoted))
            {
                t.Fatalf("finalists did not follow the corrected feeder: " + Fmt(b.State().ChaseTheAce.Finalists) + " (want " + promoted + " in)");
            }

            // Run the first CTA heat — now the feeder must freeze.
            string firstCTA = CtaRaceIDsSorted(b)[0];
            Race cr = b.FindRace(firstCTA);
            b.SelectFullPlacement(firstCTA, new List<string>(cr.Pilots));
            string reason;
            if (b.CanEditRace(feeder.Id, out reason))
            {
                t.Errorf("feeder " + feeder.Id + " must be frozen once a CTA heat has been run");
            }
            t.Done();
        }

        // TestChaseTheAcePlayThroughVoidsRemainder plays the CTA out to a champion and
        // asserts the right number of races ran and the remainder were voided (not the
        // on-demand append of the old behaviour).
        [Fact]
        public void TestChaseTheAcePlayThroughVoidsRemainder()
        {
            T t = new T();
            Bracket b = New8CTA(2);
            DriveNonCTASides(t, b);
            ChaseTheAceState cta = b.State().ChaseTheAce;
            List<string> finalists = new List<string>(cta.Finalists);

            // Two different pilots win CTA-1 and CTA-2 (no clinch), then the first
            // wins CTA-3 to reach 2 wins.
            b.SelectWinners("CTA-1", new List<string> { finalists[0] });
            b.SelectWinners("CTA-2", new List<string> { finalists[1] });
            b.SelectWinners("CTA-3", new List<string> { finalists[0] });
            if (cta.Champion != finalists[0])
            {
                t.Errorf("champion = " + Go.Quote(cta.Champion) + ", want " + Go.Quote(finalists[0]));
            }
            if (!cta.IsComplete || !b.IsComplete())
            {
                t.Errorf("expected CTA + bracket complete");
            }
            // All 5 pre-created heats remain in state; the 3 that ran are complete, the
            // 2 surplus are voided (kept so an un-crowning edit can reopen them).
            int ctaCount = 0, ranCount = 0, voidCount = 0;
            foreach (Race r in b.State().Races)
            {
                if (r.Bracket != BracketSlot.ChaseTheAce)
                {
                    continue;
                }
                ctaCount++;
                if (r.IsComplete)
                {
                    ranCount++;
                    if (r.IsVoid)
                    {
                        t.Errorf("CTA race " + r.Id + " is complete but also voided");
                    }
                }
                else if (r.IsVoid)
                {
                    voidCount++;
                }
                else
                {
                    t.Errorf("CTA race " + r.Id + " is neither complete nor voided after champion");
                }
            }
            if (ctaCount != 5)
            {
                t.Errorf("CTA races present = " + ctaCount + ", want 5 (none deleted)");
            }
            if (ranCount != 3)
            {
                t.Errorf("completed CTA races = " + ranCount + ", want 3");
            }
            if (voidCount != 2)
            {
                t.Errorf("voided surplus CTA races = " + voidCount + ", want 2");
            }
            t.Done();
        }

        // TestChaseTheAceEarlyChampionVoidsRest verifies a straight-sets champion (wins
        // RequiredWins back-to-back) voids every later CTA race immediately.
        [Fact]
        public void TestChaseTheAceEarlyChampionVoidsRest()
        {
            T t = new T();
            Bracket b = New8CTA(2);
            DriveNonCTASides(t, b);
            ChaseTheAceState cta = b.State().ChaseTheAce;
            string champ = cta.Finalists[0];

            b.SelectWinners("CTA-1", new List<string> { champ });
            b.SelectWinners("CTA-2", new List<string> { champ });
            if (cta.Champion != champ)
            {
                t.Fatalf("champion = " + Go.Quote(cta.Champion) + ", want " + Go.Quote(champ));
            }
            foreach (string id in new string[] { "CTA-3", "CTA-4", "CTA-5" })
            {
                Race r = b.FindRace(id);
                if (r == null)
                {
                    t.Errorf(id + " should remain (voided, not deleted) after early champion");
                    continue;
                }
                if (!r.IsVoid)
                {
                    t.Errorf(id + " should be voided after early champion");
                }
            }
            foreach (string id in new string[] { "CTA-1", "CTA-2" })
            {
                Race r = b.FindRace(id);
                if (r == null)
                {
                    t.Errorf(id + " should remain (it ran)");
                    continue;
                }
                if (r.IsVoid)
                {
                    t.Errorf(id + " ran and must not be voided");
                }
            }
            t.Done();
        }

        private static bool Throws(Action a)
        {
            try
            {
                a();
            }
            catch (BracketException)
            {
                return true;
            }
            return false;
        }

        // TestChaseTheAceOutOfOrderRejected asserts a CTA result can only be recorded
        // for the next un-run heat, and that voided/post-champion entries are refused.
        [Fact]
        public void TestChaseTheAceOutOfOrderRejected()
        {
            T t = new T();
            Bracket b = New8CTA(2);
            DriveNonCTASides(t, b);
            ChaseTheAceState cta = b.State().ChaseTheAce;
            List<string> finalists = new List<string>(cta.Finalists);

            // CTA-2 before CTA-1 must be rejected.
            if (!Throws(() => b.SelectWinners("CTA-2", new List<string> { finalists[0] })))
            {
                t.Errorf("expected out-of-order CTA-2 to be rejected before CTA-1 runs");
            }
            // Full-placement entry path is gated too.
            if (!Throws(() => b.SelectFullPlacement("CTA-3", finalists)))
            {
                t.Errorf("expected out-of-order CTA-3 full placement to be rejected");
            }
            // CTA-1 then CTA-2 are accepted in order; champion is crowned.
            b.SelectWinners("CTA-1", new List<string> { finalists[0] });
            b.SelectWinners("CTA-2", new List<string> { finalists[0] });
            // Post-champion entry on a voided race is refused.
            if (!Throws(() => b.SelectWinners("CTA-3", new List<string> { finalists[0] })))
            {
                t.Errorf("expected CTA-3 to be rejected after champion crowned");
            }
            t.Done();
        }

        // TestChaseTheAceEditDoesNotDoubleCount is the regression for the bug where
        // editing (or re-ingesting) a CTA heat's result inflated the winner's win tally
        // — a single heat counted twice, prematurely crowning a champion and voiding the
        // remaining heats. EditRace routes through ResetRace + a fresh apply, so the win
        // must stay at exactly 1 no matter how many times the same result is re-applied.
        [Fact]
        public void TestChaseTheAceEditDoesNotDoubleCount()
        {
            T t = new T();
            Bracket b = New8CTA(2);
            DriveNonCTASides(t, b);
            ChaseTheAceState cta = b.State().ChaseTheAce;
            string champ = cta.Finalists[0];

            // Run CTA-1 (one legitimate win), then edit/re-ingest it several times with
            // the same winner — the kind of repeated apply FPVTrackside does on re-sends.
            b.SelectWinners("CTA-1", new List<string> { champ });
            for (int i = 0; i < 3; i++)
            {
                b.EditRace("CTA-1", new List<string> { champ });
            }
            int got = cta.Wins.Get(champ);
            if (got != 1)
            {
                t.Fatalf("after 1 win + 3 re-applies, wins = " + got + ", want 1 (double-count bug)");
            }
            if (cta.Champion != "" || cta.IsComplete)
            {
                t.Fatalf("champion crowned off a single CTA win (champion=" + Go.Quote(cta.Champion) + " complete=" + cta.IsComplete + ")");
            }
            // The remaining heats must still be live (not voided away by a false crown).
            Race next = b.NextChaseTheAceRace();
            if (next == null || next.Id != "CTA-2")
            {
                t.Fatalf("next runnable CTA = " + (next == null ? "nil" : next.Id) + ", want CTA-2");
            }
            // A second genuine win legitimately crowns.
            b.SelectWinners("CTA-2", new List<string> { champ });
            if (cta.Champion != champ || !cta.IsComplete)
            {
                t.Fatalf("champion not crowned after 2 real wins (champion=" + Go.Quote(cta.Champion) + ")");
            }
            t.Done();
        }

        // TestChaseTheAceUncrownReopensVoids verifies that editing the clinching heat to
        // a non-clinching result un-crowns the champion and brings the voided surplus
        // heats back into play (they are voided, not deleted).
        [Fact]
        public void TestChaseTheAceUncrownReopensVoids()
        {
            T t = new T();
            Bracket b = New8CTA(2);
            DriveNonCTASides(t, b);
            ChaseTheAceState cta = b.State().ChaseTheAce;
            string a = cta.Finalists[0], c = cta.Finalists[1];

            // a wins CTA-1 and CTA-2 → champion, surplus (CTA-3..5) voided.
            b.SelectWinners("CTA-1", new List<string> { a });
            b.SelectWinners("CTA-2", new List<string> { a });
            if (!cta.IsComplete)
            {
                t.Fatalf("expected champion after two wins for " + Go.Quote(a));
            }
            Race r3v = b.FindRace("CTA-3");
            if (r3v == null || !r3v.IsVoid)
            {
                t.Fatalf("CTA-3 should be voided while champion stands");
            }

            // Correct CTA-2 to a different winner — no one has 2 wins now.
            b.EditRace("CTA-2", new List<string> { c });
            if (cta.Champion != "" || cta.IsComplete)
            {
                t.Fatalf("champion should be un-crowned after edit (champion=" + Go.Quote(cta.Champion) + ")");
            }
            if (cta.Wins.Get(a) != 1 || cta.Wins.Get(c) != 1)
            {
                t.Fatalf("wins after un-crown: " + a + "=" + cta.Wins.Get(a) + " " + c + "=" + cta.Wins.Get(c) + ", want 1 and 1");
            }
            // CTA-3 must be live again so the series can continue to a decision.
            Race r3 = b.FindRace("CTA-3");
            if (r3 == null || r3.IsVoid)
            {
                t.Fatalf("CTA-3 should be reopened (live) after un-crown");
            }
            Race next = b.NextChaseTheAceRace();
            if (next == null || next.Id != "CTA-3")
            {
                t.Fatalf("next runnable CTA = " + (next == null ? "nil" : next.Id) + ", want CTA-3");
            }
            t.Done();
        }

        private static bool Equal(List<string> a, List<string> b)
        {
            return Go.SameSlice(a, b);
        }

        // TestPlaceSeededAcrossPilotCounts checks that, after all W1 races finish with
        // top-N pilots advancing (seeds win), the seeded placement keeps top seeds apart
        // and prevents same-heat rematches in W2 — across a wide range of pilot counts
        // and pilots-per-race. Skips counts where W2 collapses to a single race (forced).
        //
        // For exactly-saturated configs (numAdvancers == numTargets*pilotsPerRace), the
        // no-rematch guarantee is best-effort: zero capacity slack can force a single
        // overflow back to the source's other target. The top-2 seed split is still
        // guaranteed (it's the user-visible MultiGP property).
        [Theory]
        [InlineData(4, 2)]
        [InlineData(4, 3)]
        [InlineData(3, 2)]
        [InlineData(6, 2)]
        [InlineData(6, 3)]
        [InlineData(8, 4)]
        public void TestPlaceSeededAcrossPilotCounts(int pilotsPerRace, int pilotsAdvance)
        {
            T t = new T();
            t.Prefix = "pp" + pilotsPerRace + "_adv" + pilotsAdvance + ": ";
            for (int pc = pilotsPerRace + 1; pc <= 96; pc++)
            {
                Bracket b = Bracket.New(new BracketConfig
                {
                    PilotCount = pc,
                    PilotNames = PilotNames(pc),
                    EliminationType = EliminationType.Double,
                    PilotsPerRace = pilotsPerRace,
                    PilotsToAdvance = pilotsAdvance,
                    FinalsFormat = FinalsFormat.GrandFinal,
                });

                // Collect W1 races and the pilots in each (in seed order).
                List<Race> w1 = b.State().Races.Where(r => r.Round == 1 && r.Bracket == BracketSlot.Winners).ToList();
                Go.SortStable(w1, (x, y) => string.CompareOrdinal(x.Id, y.Id) < 0);

                // Seeds win their heats: advance the top N pilots from each race.
                // Skip auto-advanced races (e.g. 2-pilot heat with advance=2 → auto-complete).
                Dictionary<string, List<string>> heatTops = new Dictionary<string, List<string>>();
                foreach (Race r in w1)
                {
                    if (r.IsComplete)
                    {
                        heatTops[r.Id] = new List<string>(Go.Iter(r.Winners));
                        continue;
                    }
                    int n = pilotsAdvance;
                    if (n > r.Pilots.Count)
                    {
                        n = r.Pilots.Count;
                    }
                    List<string> winners = r.Pilots.GetRange(0, n);
                    heatTops[r.Id] = winners;
                    try
                    {
                        b.SelectWinners(r.Id, winners);
                    }
                    catch (BracketException e)
                    {
                        t.Fatalf("pc=" + pc + ": SelectWinners(" + r.Id + "): " + e.Message);
                    }
                }

                List<Race> w2 = b.State().Races.Where(r => r.Round == 2 && r.Bracket == BracketSlot.Winners).ToList();
                if (w2.Count < 2)
                {
                    continue; // forced into single W2 race / final — no split possible
                }

                Dictionary<string, string> raceOf = new Dictionary<string, string>();
                foreach (Race r in w2)
                {
                    foreach (string p in Go.Iter(r.Pilots))
                    {
                        raceOf[p] = r.Id;
                    }
                }
                Func<string, string> ro = p => raceOf.TryGetValue(p, out string v) ? v : "";

                // (1) seeds 1 and 2 must be in different W2 races (always)
                if (ro("P1") != "" && ro("P2") != "" && ro("P1") == ro("P2"))
                {
                    t.Errorf("pc=" + pc + ": seeds 1 and 2 share W2 race " + ro("P1"));
                }

                // (2) no W1 heat's top-N advancers should all land in the same W2 race
                // (best-effort: only enforced when there's some capacity slack — when
                // the next round is exactly saturated, overflow can force a rematch)
                int totalAdvancers = 0;
                foreach (List<string> tops in heatTops.Values)
                {
                    totalAdvancers += tops.Count;
                }
                int capacity = w2.Count * pilotsPerRace;
                bool saturated = totalAdvancers >= capacity;
                if (!saturated)
                {
                    foreach (KeyValuePair<string, List<string>> kv in heatTops)
                    {
                        List<string> tops = kv.Value;
                        if (tops.Count < 2)
                        {
                            continue;
                        }
                        string first = ro(tops[0]);
                        bool same = true;
                        foreach (string p in tops.Skip(1))
                        {
                            if (ro(p) != first)
                            {
                                same = false;
                                break;
                            }
                        }
                        if (same && first != "")
                        {
                            t.Errorf("pc=" + pc + ": all of heat " + kv.Key + "'s advancers ended up in W2 race " + first);
                        }
                    }
                }

                // (3) no W2 race may exceed PilotsPerRace
                foreach (Race r in w2)
                {
                    if (Go.Len(r.Pilots) > pilotsPerRace)
                    {
                        t.Errorf("pc=" + pc + ": W2 race " + r.Id + " has " + Go.Len(r.Pilots) + " pilots (max " + pilotsPerRace + ")");
                    }
                }
            }
            t.Done();
        }

        // TestPlaceSeededSplitsTopSeeds pins MultiGP-style placement: when 3 W1 heats each
        // advance their top 2 into 2 W2 races, the heats' 1st-placers must split across
        // the two W2 races (so seeds 1 and 2 don't meet in W2), and each heat's 1st and
        // 2nd advancer must land in different W2 races.
        [Fact]
        public void TestPlaceSeededSplitsTopSeeds()
        {
            T t = new T();
            Bracket b = Bracket.New(new BracketConfig
            {
                PilotCount = 9,
                PilotNames = PilotNames(9),
                EliminationType = EliminationType.Single,
                PilotsPerRace = 4,
                PilotsToAdvance = 2,
                FinalsFormat = FinalsFormat.GrandFinal,
            });

            List<Race> w1 = b.State().Races.Where(r => r.Round == 1).ToList();
            Go.SortStable(w1, (x, y) => string.CompareOrdinal(x.Id, y.Id) < 0);
            if (w1.Count != 3)
            {
                t.Fatalf("expected 3 W1 races, got " + w1.Count);
            }

            // Top 2 in each heat advance — i.e. seed order is preserved.
            Dictionary<string, string[]> heatTops = new Dictionary<string, string[]>();
            foreach (Race r in w1)
            {
                List<string> winners = new List<string> { r.Pilots[0], r.Pilots[1] };
                heatTops[r.Id] = new string[] { winners[0], winners[1] };
                b.SelectWinners(r.Id, winners);
            }

            List<Race> w2 = b.State().Races.Where(r => r.Round == 2 && r.Bracket == BracketSlot.Winners).ToList();
            if (w2.Count != 2)
            {
                t.Fatalf("expected 2 W2 races, got " + w2.Count);
            }
            Go.SortStable(w2, (x, y) => string.CompareOrdinal(x.Id, y.Id) < 0);

            Dictionary<string, string> raceOf = new Dictionary<string, string>();
            foreach (Race r in w2)
            {
                foreach (string p in Go.Iter(r.Pilots))
                {
                    raceOf[p] = r.Id;
                }
            }
            Func<string, string> ro = p => raceOf.TryGetValue(p, out string v) ? v : "";

            // (1) Seeds 1 and 2 must be in different W2 races.
            if (ro("P1") == "" || ro("P2") == "")
            {
                t.Fatalf("expected seeds 1 and 2 placed in W2");
            }
            if (ro("P1") == ro("P2"))
            {
                t.Errorf("seeds 1 and 2 ended up in same W2 race " + Go.Quote(ro("P1")) + " (top seeds should split)");
            }

            // (2) Each heat's 1st and 2nd must go to different W2 races (no rematch in W2).
            foreach (KeyValuePair<string, string[]> kv in heatTops)
            {
                if (ro(kv.Value[0]) == ro(kv.Value[1]))
                {
                    t.Errorf("heat " + kv.Key + "'s 1st (" + kv.Value[0] + ") and 2nd (" + kv.Value[1] + ") both in " + ro(kv.Value[0]) + " — should split");
                }
            }

            // (3) Distribution should still be 3+3.
            if (Go.Len(w2[0].Pilots) != 3 || Go.Len(w2[1].Pilots) != 3)
            {
                t.Errorf("expected W2 sizes 3+3, got " + Go.Len(w2[0].Pilots) + " and " + Go.Len(w2[1].Pilots));
            }
            t.Done();
        }

        // TestMultiPilotHeatNotAutoResulted verifies that a heat where everyone
        // advances (not enough pilots to eliminate anyone) is NOT auto-resulted when an
        // upstream result cascades through checkAutoAdvanceAll — the pilots still race
        // it, so it must stay open for the director to record the finishing order. Only
        // a genuine solo bye auto-advances.
        [Fact]
        public void TestMultiPilotHeatNotAutoResulted()
        {
            T t = new T();
            // 5 pilots, 4/race, 2 advance → snake seeding makes a 3-pilot heat and a
            // 2-pilot heat in round 1; the 2-pilot heat has advance=2 (everyone through).
            Bracket b = Bracket.New(new BracketConfig
            {
                PilotCount = 5,
                PilotNames = PilotNames(5),
                EliminationType = EliminationType.Double,
                PilotsPerRace = 4,
                PilotsToAdvance = 2,
                FinalsFormat = FinalsFormat.GrandFinal,
            });

            Race twoPilotHeat = null, biggerHeat = null;
            foreach (Race r in b.State().Races)
            {
                if (r.Round == 1 && r.Bracket == BracketSlot.Winners)
                {
                    if (Go.Len(r.Pilots) == 2)
                    {
                        twoPilotHeat = r;
                    }
                    else if (Go.Len(r.Pilots) > 2)
                    {
                        biggerHeat = r;
                    }
                }
            }
            if (twoPilotHeat == null || biggerHeat == null)
            {
                t.Fatalf("expected a 2-pilot and a >2-pilot W1 heat");
            }
            if (twoPilotHeat.IsComplete)
            {
                t.Fatalf("2-pilot heat " + twoPilotHeat.Id + " auto-resulted at generation");
            }

            // Recording the bigger heat triggers the auto-advance cascade.
            b.SelectFullPlacement(biggerHeat.Id, new List<string>(biggerHeat.Pilots));

            if (twoPilotHeat.IsComplete)
            {
                t.Errorf("2-pilot heat " + twoPilotHeat.Id + " was auto-resulted before the pilots raced it");
            }
            t.Done();
        }

        // TestChaseTheAceFinalistsFollowPilotsPerRace pins the chase-the-ace final at a
        // full heat rather than a fixed four pilots: a club racing 6-up finishes with
        // six finalists, a 3-up club with three (four would not fit the heat). The
        // classic 4-up config is unchanged.
        [Theory]
        [InlineData(EliminationType.Double, 4, 2, 24, 4)]
        [InlineData(EliminationType.Double, 6, 2, 24, 6)]
        [InlineData(EliminationType.Double, 3, 1, 24, 3)]
        [InlineData(EliminationType.Single, 4, 2, 16, 4)]
        [InlineData(EliminationType.Single, 6, 2, 24, 6)]
        // Field smaller than a heat: every pilot is a finalist.
        [InlineData(EliminationType.Double, 6, 2, 5, 5)]
        public void TestChaseTheAceFinalistsFollowPilotsPerRace(string elim, int ppr, int adv, int pilots, int finalists)
        {
            T t = new T();
            t.Prefix = elim + "_pp" + ppr + "_adv" + adv + "_" + pilots + "pilots: ";
            Bracket b = Bracket.New(new BracketConfig
            {
                PilotCount = pilots,
                PilotNames = PilotNames(pilots),
                EliminationType = elim,
                PilotsPerRace = ppr,
                PilotsToAdvance = adv,
                FinalsFormat = FinalsFormat.ChaseTheAce,
                AceWinsRequired = 2,
            });
            PlayOutTournament(t, b);

            ChaseTheAceState cta = b.State().ChaseTheAce;
            if (cta == null)
            {
                t.Fatalf("no chase-the-ace state");
            }
            if (Go.Len(cta.Finalists) != finalists)
            {
                t.Errorf("finalists = " + Go.Len(cta.Finalists) + " (" + Fmt(cta.Finalists) + "), want " + finalists);
            }
            if (!cta.IsComplete || cta.Champion == "")
            {
                t.Errorf("CTA never completed; champion=" + Go.Quote(cta.Champion));
            }
            // Every CTA heat that actually ran carried the full finalist field.
            foreach (Race r in b.State().Races)
            {
                if (r.Bracket != BracketSlot.ChaseTheAce || r.IsVoid || !r.IsComplete)
                {
                    continue;
                }
                if (Go.Len(r.Pilots) != finalists)
                {
                    t.Errorf(r.Id + " has " + Go.Len(r.Pilots) + " pilots, want " + finalists);
                }
            }
            t.Done();
        }

        // LosersShape returns the redemption bracket as one entry per L round:
        // "L{n}:{heats}" plus "<W{r}" when that round takes winners-round r's droppers.
        private static List<string> LosersShape(Bracket b)
        {
            SortedDictionary<int, int> rounds = new SortedDictionary<int, int>();
            Dictionary<int, int> from = new Dictionary<int, int>();
            foreach (Race r in b.State().Races)
            {
                if (r.Bracket != BracketSlot.Losers)
                {
                    continue;
                }
                rounds.TryGetValue(r.Round, out int c);
                rounds[r.Round] = c + 1;
                if (r.FromWinnersRound > 0)
                {
                    from[r.Round] = r.FromWinnersRound;
                }
            }
            List<string> output = new List<string>();
            int i = 0;
            foreach (KeyValuePair<int, int> kv in rounds)
            {
                string s = "L" + (i + 1) + ":" + kv.Value;
                if (from.TryGetValue(kv.Key, out int w) && w > 0)
                {
                    s += "<W" + w;
                }
                output.Add(s);
                i++;
            }
            return output;
        }

        public static IEnumerable<object[]> LosersCases()
        {
            // The 16-pilot MultiGP double-elimination sheet, transcribed by hand
            // in the qualbracket package: L1×2, L2×2, L3×1 (halving), L4×1.
            yield return new object[] { "16 pilots, 4-up, top 2 (MultiGP sheet)", 16, 4, 2, new string[] { "L1:2<W1", "L2:2<W2", "L3:1", "L4:1<W3" } };
            yield return new object[] { "48 pilots, 6-up, top 3", 48, 6, 3, new string[] { "L1:4<W1", "L2:4<W2", "L3:2", "L4:2<W3", "L5:1", "L6:1<W4" } };
            yield return new object[] { "24 pilots, 6-up, top 3", 24, 6, 3, new string[] { "L1:2<W1", "L2:2<W2", "L3:1", "L4:1<W3" } };
        }

        // The redemption bracket runs the real double-elimination cadence: after the
        // first two intakes it alternates a MINOR round (halves the redemption pool,
        // nobody new enters) with a MAJOR round (survivors + the next winners round's
        // droppers). Feeding winners round N straight into losers round N instead let
        // the pool run ahead of the field it merged with — a 48-pilot / 6-up / top-3
        // bracket got a 3-heat redemption round 3 (12 survivors + 6 fresh droppers)
        // where the sheet halves 12 into 2 heats first and takes the droppers in round 4.
        [Theory]
        [MemberData(nameof(LosersCases))]
        public void TestLosersBracketAlternatesMinorAndMajorRounds(string name, int pilotCount, int ppr, int adv, string[] want)
        {
            BracketConfig cfg = new BracketConfig
            {
                PilotCount = pilotCount,
                PilotsPerRace = ppr,
                PilotsToAdvance = adv,
                EliminationType = EliminationType.Double,
                FinalsFormat = FinalsFormat.ChaseTheAce,
                AceWinsRequired = 2,
                PilotNames = new List<string>(),
            };
            for (int i = 0; i < cfg.PilotCount; i++)
            {
                cfg.PilotNames.Add("P" + (i + 1).ToString("00"));
            }
            Bracket b = Bracket.New(cfg);
            List<string> got = LosersShape(b);
            Assert.True(Fmt(got) == Fmt(want), name + ": redemption shape\n got " + Fmt(got) + "\nwant " + Fmt(want));
        }

        // A winners round's droppers must land in the redemption round stamped for them,
        // not the round that happens to sit one column across — with minor rounds in the
        // mix those are no longer the same thing.
        [Fact]
        public void TestWinnersDroppersLandInTheirStampedRedemptionRound()
        {
            BracketConfig cfg = new BracketConfig
            {
                PilotCount = 48,
                PilotsPerRace = 6,
                PilotsToAdvance = 3,
                EliminationType = EliminationType.Double,
                FinalsFormat = FinalsFormat.ChaseTheAce,
                AceWinsRequired = 2,
                PilotNames = new List<string>(),
            };
            for (int i = 0; i < cfg.PilotCount; i++)
            {
                cfg.PilotNames.Add("P" + (i + 1).ToString("00"));
            }
            Bracket b = Bracket.New(cfg);
            // W3 drops into L4 (round 5) here, NOT L3 (round 4) — L3 is the halving round.
            int got = b.LosersDropRound(3);
            Assert.True(got == 5, "losersDropRound(3) = " + got + ", want 5 (L4)");
            // A bracket saved before FromWinnersRound existed keeps the old mapping.
            foreach (Race r in b.State().Races)
            {
                r.FromWinnersRound = 0;
            }
            got = b.LosersDropRound(3);
            Assert.True(got == 4, "legacy fallback losersDropRound(3) = " + got + ", want 4");
        }
    }
}
