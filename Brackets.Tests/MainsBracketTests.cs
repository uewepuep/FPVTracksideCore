using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BracketEngine.Tests
{
    // Port of internal/platform/mainsbracket/mainsbracket_test.go.
    public class MainsBracketTests
    {
        private static List<string> SeedNames(int n)
        {
            List<string> output = new List<string>();
            for (int i = 0; i < n; i++)
            {
                output.Add("P" + (i + 1));
            }
            return output;
        }

        private static string Show(IEnumerable<string> s)
        {
            return "[" + string.Join(" ", s ?? new string[0]) + "]";
        }

        private static Mains MustNew(int n, int perMain, int bump)
        {
            try
            {
                return Mains.New(new MainsProgressionConfig { PilotNames = SeedNames(n), PilotsPerMain = perMain, BumpUp = bump });
            }
            catch (BracketException e)
            {
                Assert.Fail("New(" + n + "/" + perMain + "/" + bump + "): " + e.Message);
                return null;
            }
        }

        // Go map read: zero value when absent.
        private static int SeedOf(Mains m, string p)
        {
            int s;
            return m.seed.TryGetValue(p, out s) ? s : 0;
        }

        private static Qual QualOf(Mains m, string label)
        {
            Qual q;
            return m.quals.TryGetValue(label, out q) ? q : null;
        }

        // PlayMainSeedsWin runs every runnable race of one main, best original seed
        // winning, until the main is complete. Routes through the public ApplyPlacement
        // so the bump-up cascade fires.
        private static void PlayMainSeedsWin(Mains m, string label)
        {
            for (int i = 0; i < 5000; i++)
            {
                Qual q = QualOf(m, label);
                Assert.True(q != null, "main " + label + " not built");
                Race pick = null;
                foreach (Race r in q.State().Races)
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
                    return;
                }
                List<string> order = new List<string>(pick.Pilots);
                Go.SortStable(order, (a, b) => SeedOf(m, a) < SeedOf(m, b));
                try
                {
                    m.ApplyPlacement(label + ":" + pick.Id, order);
                }
                catch (BracketException e)
                {
                    Assert.Fail("apply " + label + ":" + pick.Id + ": " + e.Message);
                }
            }
        }

        private static List<string> SeedRange(int lo, int hi)
        {
            List<string> output = new List<string>();
            for (int s = lo; s <= hi; s++)
            {
                output.Add("P" + s);
            }
            return output;
        }

        [Fact]
        public void TestResidentSeeding()
        {
            Mains m = MustNew(64, 16, 4);
            Assert.True(m.NumMains() == 5, "mains = " + m.NumMains() + ", want 5");
            Dictionary<string, List<string>> want = new Dictionary<string, List<string>>
            {
                { "A", SeedRange(1, 12) }, { "B", SeedRange(13, 24) }, { "C", SeedRange(25, 36) },
                { "D", SeedRange(37, 48) }, { "E", SeedRange(49, 64) },
            };
            foreach (MainState ms in m.state.Mains)
            {
                List<string> got = Mains.ResidentsOf(m.cfg, m.IndexOf(ms.Label));
                Assert.True(Go.SameSlice(got, want[ms.Label]), "main " + ms.Label + " residents = " + Show(got) + ", want " + Show(want[ms.Label]));
            }
            // Only the lowest main (E) is built up front.
            Assert.True(m.state.Mains[4].Built && !m.state.Mains[0].Built,
                "expected only the lowest main built initially: E=" + m.state.Mains[4].Built + " A=" + m.state.Mains[0].Built);
        }

        [Fact]
        public void TestBumpUpByOriginalSeedBelowResidents()
        {
            Mains m = MustNew(64, 16, 4);
            PlayMainSeedsWin(m, "E"); // E's best 4 seeds (P49-P52) win and bump to D

            MainState d = m.state.Mains[3];
            Assert.True(d.Built, "D should be built after E completes");
            Assert.True(d.Roster.Count == 16, "D roster len = " + d.Roster.Count + ", want 16");
            // Residents P37-P48 first, then bumped P49-P52 (sorted by original seed).
            List<string> wantResidents = SeedRange(37, 48);
            List<string> wantBumped = SeedRange(49, 52);
            List<string> gotRes = d.Roster.GetRange(0, 12);
            List<string> gotBump = d.Roster.GetRange(12, d.Roster.Count - 12);
            Assert.True(Go.SameSlice(gotRes, wantResidents), "D residents = " + Show(gotRes) + ", want " + Show(wantResidents));
            Assert.True(Go.SameSlice(gotBump, wantBumped), "D bumped = " + Show(gotBump) + ", want " + Show(wantBumped));
            // Every bumped pilot seeds strictly below every resident.
            foreach (string b in gotBump)
            {
                foreach (string r in gotRes)
                {
                    Assert.False(SeedOf(m, b) < SeedOf(m, r), "bumped " + b + " seeded above resident " + r);
                }
            }
        }

        [Fact]
        public void TestCascadeToChampion()
        {
            Mains m = MustNew(64, 16, 4);
            MainsProgressionState st = m.Simulate();
            Mains sim = Mains.FromState(m.cfg, st);
            Assert.True(sim.IsComplete(), "simulated competition should be complete");
            string champ = sim.Champion();
            Assert.True(champ == "P1", "champion = \"" + champ + "\", want P1 (seed 1 wins every heat)");
            List<RankRow> rank = sim.Ranking();
            Assert.True(rank.Count == 64, "ranking covers " + rank.Count + " pilots, want 64");
            Assert.True(rank[0].Pilot == "P1" && rank[0].Main == "A", "rank[0] = " + rank[0].Pilot + "/" + rank[0].Main + ", want P1 in A");
            // A-main pilots all rank ahead of B-main pilots (A first).
            bool seenB = false;
            foreach (RankRow r in rank)
            {
                if (r.Main == "B")
                {
                    seenB = true;
                }
                Assert.False(r.Main == "A" && seenB, "A-main pilot " + r.Pilot + " ranked after a B-main pilot");
            }
            // No dupes.
            HashSet<string> seen = new HashSet<string>();
            foreach (RankRow r in rank)
            {
                Assert.False(seen.Contains(r.Pilot), "duplicate pilot in ranking: " + r.Pilot);
                seen.Add(r.Pilot);
            }
        }

        [Fact]
        public void TestEditLockWhenHigherStarted()
        {
            Mains m = MustNew(64, 16, 4);
            PlayMainSeedsWin(m, "E"); // E complete, D built
            // Run one D race so D has started.
            Qual dq = QualOf(m, "D");
            Race first = null;
            foreach (Race r in dq.State().Races)
            {
                if (!r.IsComplete && Go.Len(r.Pilots) > 0)
                {
                    first = r;
                    break;
                }
            }
            Assert.True(first != null, "no runnable D race");
            m.ApplyPlacement("D:" + first.Id, new List<string>(first.Pilots));
            // Now editing E's terminal must be refused (D started).
            string eFinal = TerminalID(m, "E");
            string reason;
            bool ok = m.CanEditRace("E:" + eFinal, out reason);
            Assert.False(ok, "expected E edit to be locked once D started; got ok");
            Assert.True(reason.Contains("D Main"), "lock reason = \"" + reason + "\", want mention of D Main");
        }

        [Fact]
        public void TestEditRebuildsHigherWhenNotStarted()
        {
            // bump=2 so the top-2 of E's 4-pilot final actually changes when reordered
            // (with bump=4 the whole final IS the bump set, so reordering can't change it).
            Mains m = MustNew(64, 16, 2);
            PlayMainSeedsWin(m, "E"); // E complete, D built (not started)
            int dResidents = Go.Len(Mains.ResidentsOf(m.cfg, 3));
            List<string> dRoster = m.state.Mains[3].Roster;
            List<string> beforeBump = dRoster.GetRange(dResidents, dRoster.Count - dResidents);

            // Re-run E's terminal final with a different winner among the finalists so the
            // bump set changes, and assert D is rebuilt.
            string eFinal = TerminalID(m, "E");
            Qual q = QualOf(m, "E");
            Race finalRace = null;
            foreach (Race r in q.State().Races)
            {
                if (r.Id == eFinal)
                {
                    finalRace = r;
                }
            }
            if (finalRace == null || Go.Len(finalRace.Pilots) < 2)
            {
                return; // Go: t.Skip("no multi-pilot terminal final to permute")
            }
            List<string> rev = new List<string>(finalRace.Pilots);
            rev.Reverse();
            m.EditRace("E:" + eFinal, rev);
            List<string> dRoster2 = m.state.Mains[3].Roster;
            List<string> afterBump = dRoster2.GetRange(dResidents, dRoster2.Count - dResidents);
            Assert.False(Go.SameSlice(beforeBump, afterBump), "expected D's bumped set to change after re-ranking E final; still " + Show(afterBump));
            Assert.True(m.state.Mains[3].Built, "D should still be built after the edit");
        }

        [Fact]
        public void TestResetUnbuildsHigher()
        {
            Mains m = MustNew(64, 16, 4);
            PlayMainSeedsWin(m, "E");
            Assert.True(m.state.Mains[3].Built, "D should be built after E completes");
            string eFinal = TerminalID(m, "E");
            m.ResetRace("E:" + eFinal);
            Assert.False(m.state.Mains[3].Built, "D should be un-built after E's final is reset");
            Assert.True(Go.SameSlice(m.state.Mains[3].Roster, Mains.ResidentsOf(m.cfg, 3)), "D roster should revert to residents after reset");
        }

        public static IEnumerable<object[]> CTAForMainCases()
        {
            yield return new object[] { "master off", false, null, "A", false };
            yield return new object[] { "default A only — A", true, null, "A", true };
            yield return new object[] { "default A only — B", true, null, "B", false };
            yield return new object[] { "explicit list — in", true, new[] { "A", "B" }, "B", true };
            yield return new object[] { "explicit list — out", true, new[] { "A", "B" }, "C", false };
            yield return new object[] { "all sentinel", true, new[] { "*" }, "E", true };
            yield return new object[] { "all sentinel but master off", false, new[] { "*" }, "A", false };
        }

        [Theory]
        [MemberData(nameof(CTAForMainCases))]
        public void TestCTAForMain(string name, bool cta, string[] ctaMains, string label, bool want)
        {
            MainsProgressionConfig cfg = new MainsProgressionConfig { CTA = cta, CTAMains = ctaMains == null ? null : new List<string>(ctaMains) };
            bool got = Mains.CTAForMain(cfg, label);
            Assert.True(got == want, name + ": CTAForMain(\"" + label + "\") = " + got + ", want " + want);
        }

        private static bool HasCTA(Mains sim, string label)
        {
            Qual q = QualOf(sim, label);
            if (q == null)
            {
                return false;
            }
            return q.State().Races.Any(r => r.Bracket == BracketSlot.ChaseTheAce);
        }

        [Fact]
        public void TestPerMainCTAApplied()
        {
            // Default (CTA on, no CTAMains) → only the A Main runs chase-the-ace.
            Mains m = Mains.New(new MainsProgressionConfig { PilotNames = SeedNames(64), PilotsPerMain = 16, BumpUp = 4, CTA = true });
            Mains sim = Mains.FromState(m.Cfg(), m.Simulate());
            Assert.True(HasCTA(sim, "A"), "A Main should run chase-the-ace by default");
            Assert.False(HasCTA(sim, "E"), "E Main should NOT run chase-the-ace by default");

            // CTAMains ["*"] → every main runs chase-the-ace.
            Mains all = Mains.New(new MainsProgressionConfig { PilotNames = SeedNames(64), PilotsPerMain = 16, BumpUp = 4, CTA = true, CTAMains = new List<string> { "*" } });
            Mains simAll = Mains.FromState(all.Cfg(), all.Simulate());
            foreach (string lbl in new[] { "A", "C", "E" })
            {
                Assert.True(HasCTA(simAll, lbl), "with CTAMains=[*], " + lbl + " Main should run chase-the-ace");
            }
        }

        [Fact]
        public void TestRaceIDNamespacing()
        {
            Mains m = MustNew(64, 16, 4);
            Qual q = QualOf(m, "E");
            foreach (Race r in q.State().Races)
            {
                string prefixed = "E:" + r.Id;
                string lbl, inner;
                Mains.SplitRaceID(prefixed, out lbl, out inner);
                Assert.True(lbl == "E" && inner == r.Id, "SplitRaceID(\"" + prefixed + "\") = (" + lbl + "," + inner + "), want (E," + r.Id + ")");
            }
        }

        private static bool SimCompletes(Mains m, out string err)
        {
            err = "";
            try
            {
                Mains sim = Mains.FromState(m.cfg, m.Simulate());
                return sim.IsComplete();
            }
            catch (BracketException e)
            {
                err = e.Message;
                return false;
            }
        }

        [Fact]
        public void TestUnevenField()
        {
            Mains m = MustNew(50, 16, 4); // → 4 mains; A,B,C=12 residents, D(lowest)=14
            Assert.True(m.NumMains() == 4, "mains = " + m.NumMains() + ", want 4");
            int a = Go.Len(Mains.ResidentsOf(m.cfg, 0));
            Assert.True(a == 12, "A residents = " + a + ", want 12 (a full main once the bump arrives)");
            int low = Go.Len(Mains.ResidentsOf(m.cfg, 3));
            Assert.True(low == 14, "lowest residents = " + low + ", want 14 (the remainder lands at the bottom)");
            // Whole thing simulates to completion without panicking.
            string err;
            Assert.True(SimCompletes(m, out err), "uneven field did not complete: err=" + err);
        }

        // A short field belongs at the BOTTOM of the stack. Every main above the lowest
        // races its residents plus the bump-up from below, so they all get
        // (PilotsPerMain−BumpUp) residents and run a FULL main — the A Main championship
        // included. The remainder used to land in the top main, which starved the
        // championship: 37 pilots at 16-per-main / bump-4 gave the A Main nine residents,
        // so the title was decided over 13 pilots while the C Main ran a full 16.
        [Fact]
        public void TestRemainderLandsInTheLowestMainNotTheChampionship()
        {
            int[][] cases = new int[][]
            {
                new[] { 37, 16, 4 }, new[] { 50, 16, 4 }, new[] { 64, 16, 4 }, new[] { 29, 16, 4 }, new[] { 17, 16, 4 },
                new[] { 100, 12, 3 }, new[] { 43, 8, 2 }, new[] { 9, 4, 1 },
            };
            foreach (int[] tc in cases)
            {
                int field = tc[0], perMain = tc[1], bump = tc[2];
                string tag = field + "@" + perMain + "/" + bump;
                Mains m = MustNew(field, perMain, bump);
                int M = m.NumMains();
                int step = perMain - bump;
                int total = 0;
                for (int i = 0; i < M; i++)
                {
                    int n = Go.Len(Mains.ResidentsOf(m.cfg, i));
                    total += n;
                    if (i == M - 1)
                    {
                        // The lowest main races its residents alone — short is fine, but it
                        // must still have more pilots than it sends up, and never overfill.
                        Assert.False(n <= bump || n > perMain, tag + ": lowest main has " + n + " residents, want in (" + bump + ", " + perMain + "]");
                        continue;
                    }
                    Assert.True(n == step, tag + ": main " + i + " has " + n + " residents, want " + step + " so it runs a full " + perMain);
                }
                Assert.True(total == field, tag + ": residents total " + total + ", want the whole field");
                // And it still plays out.
                string err;
                Assert.True(SimCompletes(m, out err), tag + ": did not complete: err=" + err);
            }
        }

        // An exactly-divisible field is unaffected by where the remainder goes — the
        // pin that says the change can't disturb a bracket already running one.
        [Fact]
        public void TestExactFieldSplitIsUnchanged()
        {
            Mains m = MustNew(64, 16, 4);
            Dictionary<string, List<string>> want = new Dictionary<string, List<string>>
            {
                { "A", SeedRange(1, 12) }, { "B", SeedRange(13, 24) }, { "C", SeedRange(25, 36) },
                { "D", SeedRange(37, 48) }, { "E", SeedRange(49, 64) },
            };
            foreach (MainState ms in m.state.Mains)
            {
                List<string> got = Mains.ResidentsOf(m.cfg, m.IndexOf(ms.Label));
                Assert.True(Go.SameSlice(got, want[ms.Label]), "main " + ms.Label + " residents = " + Show(got) + ", want " + Show(want[ms.Label]));
            }
        }

        [Fact]
        public void TestPersistRoundTrip()
        {
            Mains m = MustNew(64, 16, 4);
            PlayMainSeedsWin(m, "E");
            string blob = BracketJson.Serialize(m.State());
            MainsProgressionState st = BracketJson.Deserialize<MainsProgressionState>(blob);
            Mains m2 = Mains.FromState(m.cfg, st);
            Assert.True(m2.NumMains() == m.NumMains() && m2.state.Mains[3].Built, "round-trip lost the built D main");
            Assert.True(Go.SameSlice(m.state.Mains[3].Roster, m2.state.Mains[3].Roster), "round-trip changed D's roster");
        }

        // TerminalID returns a main's terminal final race id (the grand-final heat),
        // or the only race for a tiny field.
        private static string TerminalID(Mains m, string label)
        {
            Qual q = QualOf(m, label);
            foreach (Race r in q.State().Races)
            {
                if (r.Bracket == BracketSlot.GrandFinal)
                {
                    return r.Id;
                }
            }
            // Fallback: last race.
            List<Race> races = q.State().Races;
            if (Go.Len(races) > 0)
            {
                return races[races.Count - 1].Id;
            }
            Assert.Fail("no terminal race in main " + label);
            return "";
        }

        private static void PlayLabel(Mains m, string only)
        {
            while (true)
            {
                string label, inner;
                List<string> order;
                bool ok = m.NextSeedsWinRace(out label, out inner, out order);
                if (!ok || label != only)
                {
                    break;
                }
                try
                {
                    m.ApplyPlacement(only + ":" + inner, order);
                }
                catch (BracketException e)
                {
                    Assert.Fail(only + " " + inner + ": " + e.Message);
                }
            }
        }

        // TestSixUpMains is the Nationals ask: 90 pilots, 48 per main, 6 bump up,
        // 6-up / top-3 heats. B Main (43rd–90th) runs 6-up double elimination to a
        // single 6-pilot final; its 6 qualifiers bump into A Main (1st–42nd + 6).
        [Fact]
        public void TestSixUpMains()
        {
            List<string> names = SeedNames(90);
            Mains m = Mains.New(new MainsProgressionConfig { PilotNames = names, PilotsPerMain = 48, BumpUp = 6, PilotsPerRace = 6, Advance = 3, CTA = true, CTAMains = new List<string> { "A" } });
            Assert.True(m.NumMains() == 2, "want A + B, got " + m.NumMains() + " mains");
            Qual b = m.Qual("B");
            Assert.True(b != null, "B Main must be built up front");
            Assert.True(m.State().Mains[1].Roster.Count == 48, "B roster = " + m.State().Mains[1].Roster.Count + ", want 48");
            foreach (SpecRace sr in b.Spec().Body)
            {
                Assert.False(sr.Sources.Count > 6, "B " + sr.Id + " is " + sr.Sources.Count + "-up");
            }
            Assert.True(Go.Len(b.Spec().Finalists) == 6 && !b.Spec().CTA,
                "B must end in a single 6-pilot final (no CTA): finalists=" + Go.Len(b.Spec().Finalists) + " cta=" + b.Spec().CTA);
            // Play B out seeds-win; the 6 qualifiers bump into A.
            PlayLabel(m, "B");
            Assert.True(m.Qual("B").IsComplete(), "B did not complete");
            Qual a = m.Qual("A");
            Assert.True(a != null && m.State().Mains[0].Roster.Count == 48,
                "A must be built with 42 residents + 6 bumped: built=" + (a != null) + " roster=" + Go.Len(m.State().Mains[0].Roster));
            Assert.True(a.Spec().CTA && Go.Len(a.Spec().Finalists) == 6,
                "A must run a 6-pilot chase-the-ace final: cta=" + a.Spec().CTA + " finalists=" + Go.Len(a.Spec().Finalists));
        }

        // TestLowestMainPaddedToCapacity: 81 pilots / 48 per main / 6 bump → A has 42
        // residents and B 39. B must run the full 48-seat bracket (8 heats of 6 seats,
        // the 9 missing bottom seeds empty), while A — full by construction — is
        // untouched. This is the "B Main must have exactly A Main's races" ask.
        [Fact]
        public void TestLowestMainPaddedToCapacity()
        {
            List<string> names = SeedNames(81);
            Mains m = Mains.New(new MainsProgressionConfig { PilotNames = names, PilotsPerMain = 48, BumpUp = 6, PilotsPerRace = 6, Advance = 3 });
            Qual b = m.Qual("B");
            Assert.True(b != null && m.State().Mains[1].Roster.Count == 39, "B roster: " + Go.Len(m.State().Mains[1].Roster));
            int r1 = 0, seats = 0;
            foreach (Race r in b.State().Races)
            {
                if (r.Round == 1)
                {
                    r1++;
                    seats += Go.Len(r.Pilots);
                }
            }
            Assert.True(r1 == 8 && seats == 39, "B round 1: " + r1 + " heats seating " + seats + " pilots; want 8 heats / 39");
            Assert.True(b.Config().PilotsPerRace == 6 && Go.Len(b.Spec().Finalists) == 6, "B shape wrong");
            // Play B; A then runs full: 42 + 6 = 48, no padding needed.
            PlayLabel(m, "B");
            Qual a = m.Qual("A");
            Assert.True(a != null && m.State().Mains[0].Roster.Count == 48, "A: built=" + (a != null) + " roster=" + Go.Len(m.State().Mains[0].Roster));
            int ar1 = a.State().Races.Count(r => r.Round == 1);
            Assert.True(ar1 == 8, "A round 1 heats = " + ar1 + ", want 8 — B mirrors A");
        }

        // TestBumpedMainSeedsSnakeEvenWhenPhotoChosen is the 2026 Nationals Pro Spec
        // bracket (104): photo ("AUFPV — Standard Seed") seeding on a mains
        // progression. The photo sheet puts seeds 13 and 14 in one heat and the top
        // four two-per-heat, so the main above got two bumped-up pilots in W1-3 and
        // its top qualifiers paired off. A main that RECEIVES bump-ups must seed snake:
        // every round-1 heat gets exactly one top-4 seed and exactly one bump-up. The
        // lowest main (nothing coming in) still honours the photo choice.
        [Fact]
        public void TestBumpedMainSeedsSnakeEvenWhenPhotoChosen()
        {
            // 40 pilots / 16 per main / bump 4 → A 12 + B 12 residents, C (lowest) 16.
            Mains m = Mains.New(new MainsProgressionConfig { PilotNames = SeedNames(40), PilotsPerMain = 16, BumpUp = 4, PhotoSeed = true });
            Assert.True(m.state.Mains[2].Seeding == "photo", "lowest main seeding = \"" + m.state.Mains[2].Seeding + "\", want photo (nothing bumps into it)");
            PlayMainSeedsWin(m, "C"); // C done → B built with C's top four
            PlayMainSeedsWin(m, "B"); // B done → A built with B's top four
            foreach (string label in new[] { "B", "A" })
            {
                int idx = m.IndexOf(label);
                MainState ms = m.state.Mains[idx];
                Assert.True(ms.Seeding == "snake", label + " seeding = \"" + ms.Seeding + "\", want snake (it receives bump-ups)");
                HashSet<string> top4 = new HashSet<string>(ms.Roster.GetRange(0, 4));
                HashSet<string> bumped = new HashSet<string>(ms.Roster.GetRange(12, ms.Roster.Count - 12));
                foreach (Race r in QualOf(m, label).State().Races)
                {
                    if (r.Round != 1 || r.Bracket != BracketSlot.Winners)
                    {
                        continue;
                    }
                    int nt = 0, nb = 0;
                    foreach (string p in r.Pilots ?? new List<string>())
                    {
                        if (top4.Contains(p))
                        {
                            nt++;
                        }
                        if (bumped.Contains(p))
                        {
                            nb++;
                        }
                    }
                    Assert.True(nt == 1 && nb == 1, label + ":" + r.Id + " " + Show(r.Pilots) + " has " + nt + " top-4 seeds and " + nb + " bump-ups, want 1 and 1");
                }
            }
        }

        // TestRecordedSeedingSurvivesConfigChange pins that a built main reloads
        // under the layout it was BUILT with, whatever the bracket's seeding setting
        // says now — so changing the setting mid-event (as bracket 104 did on the
        // night) can never re-wire a main's round 1 under the pilots who raced it.
        // A main from before the layout was recorded ("") follows the setting.
        [Fact]
        public void TestRecordedSeedingSurvivesConfigChange()
        {
            Mains m = Mains.New(new MainsProgressionConfig { PilotNames = SeedNames(16), PilotsPerMain = 16, BumpUp = 4, PhotoSeed = true });
            MainState ms = m.state.Mains[0];
            Assert.True(ms.Seeding == "photo", "single main seeding = \"" + ms.Seeding + "\", want photo");
            Assert.True(m.PerMainCfg(ms, ms.Roster).PhotoSeed, "built-photo main didn't load as photo");
            MainsProgressionConfig cfg = m.cfg.Clone();
            cfg.PhotoSeed = false;
            Mains m2 = Mains.FromState(cfg, m.state);
            Assert.True(m2.PerMainCfg(ms, ms.Roster).PhotoSeed, "switching the bracket to snake re-seeded a main built as photo");
            ms.Seeding = "";
            Assert.False(m2.PerMainCfg(ms, ms.Roster).PhotoSeed, "a legacy main (no recorded seeding) should follow the current setting");
        }
    }
}
