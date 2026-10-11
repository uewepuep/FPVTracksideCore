using System.Collections.Generic;
using Xunit;

namespace BracketEngine.Tests
{
    // Port of raceformat/mains_test.go.
    public class RaceFormatMainsTests
    {
        private const string MainsAscending = "ascending";
        private const string MainsDescending = "descending";

        // F1Points is the classic top-heavy preset (descending: 1st earns the most).
        internal static Dictionary<int, int> F1Points()
        {
            return new Dictionary<int, int> { { 1, 25 }, { 2, 18 }, { 3, 15 }, { 4, 12 }, { 5, 10 }, { 6, 8 }, { 7, 6 }, { 8, 4 } };
        }

        // LinearAsc is an ascending preset: 1st = 1 point, lowest total wins.
        private static Dictionary<int, int> LinearAsc()
        {
            return new Dictionary<int, int> { { 1, 1 }, { 2, 2 }, { 3, 3 }, { 4, 4 }, { 5, 5 }, { 6, 6 }, { 7, 7 }, { 8, 8 } };
        }

        internal static FormatConfig MainsConfig(int n, List<string> mains, int rounds, string dir, Dictionary<int, int> pts)
        {
            return new FormatConfig
            {
                PilotCount = n,
                PilotNames = RaceFormatEliminationAdapterTests.PilotNames(n),
                Mains = mains,
                Rounds = rounds,
                Direction = dir,
                PointsTable = pts,
            };
        }

        private static List<string> L(params string[] s)
        {
            return new List<string>(s);
        }

        [Fact]
        public void TestMainsAssignmentBalance()
        {
            for (int n = 3; n <= 40; n++)
            {
                foreach (int mCount in new[] { 1, 2, 3, 4 })
                {
                    if (n < mCount)
                    {
                        continue;
                    }
                    List<string> mains = new List<string>();
                    for (int i = 0; i < mCount; i++)
                    {
                        mains.Add(((char)('A' + i)).ToString());
                    }
                    string tag = "n=" + n + " m=" + mCount;
                    MainsFormat mf = MainsFormat.New(MainsConfig(n, mains, 2, MainsDescending, F1Points()));
                    int min = 1 << 30, max = 0, total = 0;
                    HashSet<string> seen = new HashSet<string>();
                    foreach (Main main in mf.State().Mains)
                    {
                        int sz = main.Pilots.Count;
                        total += sz;
                        if (sz < min)
                        {
                            min = sz;
                        }
                        if (sz > max)
                        {
                            max = sz;
                        }
                        foreach (string p in main.Pilots)
                        {
                            Assert.True(seen.Add(p), tag + ": pilot " + p + " in two mains");
                        }
                    }
                    Assert.True(total == n, tag + ": assigned " + total + " pilots, want " + n);
                    Assert.True(max - min <= 1, tag + ": main sizes unbalanced min=" + min + " max=" + max);
                }
            }
        }

        // TestMainsDescendingMath checks exact totals with the F1 preset over 2 rounds.
        [Fact]
        public void TestMainsDescendingMath()
        {
            // 4 pilots, single A main, 2 rounds.
            MainsFormat mf = MainsFormat.New(MainsConfig(4, L("A"), 2, MainsDescending, F1Points()));
            // Round 1: P1,P2,P3,P4 → 25,18,15,12
            // Round 2: P2,P1,P4,P3 → P2:25 P1:18 P4:15 P3:12
            mf.ApplyPlacement("A-R1", L("P1", "P2", "P3", "P4"));
            mf.ApplyPlacement("A-R2", L("P2", "P1", "P4", "P3"));
            // Totals: P1=43 P2=43 P3=27 P4=27
            Dictionary<string, int> want = new Dictionary<string, int> { { "P1", 43 }, { "P2", 43 }, { "P3", 27 }, { "P4", 27 } };
            List<Standing> ranked = mf.RankMain(mf.State().Mains[0]);
            Dictionary<string, int> got = new Dictionary<string, int>();
            foreach (Standing s in ranked)
            {
                // detail begins with "<total> pts"
                got[s.Pilot] = FscanTotal(s.Detail);
            }
            foreach (KeyValuePair<string, int> kv in want)
            {
                Assert.True(got[kv.Key] == kv.Value, "pilot " + kv.Key + " total=" + got[kv.Key] + " want " + kv.Value);
            }
            // P1 and P2 tie on 43; ties are broken by seeding order, so the higher seed
            // (P1) ranks ahead — no "tied" flag.
            Assert.True(ranked[0].Pilot == "P1" && ranked[1].Pilot == "P2", "expected P1 then P2 by seed; got " + ranked[0].Pilot + ", " + ranked[1].Pilot);
            Assert.False(ranked[0].Tied || ranked[1].Tied, "ties should be resolved by seed, not flagged");
            Assert.True(mf.IsComplete(), "should be complete after both rounds");
            Assert.True(mf.Champion() == "P1", "champion=" + mf.Champion() + " want P1");
        }

        // TestMainsAscendingMath checks ascending scoring (lowest total wins).
        [Fact]
        public void TestMainsAscendingMath()
        {
            MainsFormat mf = MainsFormat.New(MainsConfig(4, L("A"), 2, MainsAscending, LinearAsc()));
            // R1: P3,P4,P1,P2 → P3:1 P4:2 P1:3 P2:4
            // R2: P3,P1,P4,P2 → P3:1 P1:2 P4:3 P2:4
            mf.ApplyPlacement("A-R1", L("P3", "P4", "P1", "P2"));
            mf.ApplyPlacement("A-R2", L("P3", "P1", "P4", "P2"));
            // Totals: P3=2 P1=5 P4=5 P2=8 → ascending: P3 best, P2 worst.
            List<Standing> ranked = mf.RankMain(mf.State().Mains[0]);
            Assert.True(ranked[0].Pilot == "P3" && ranked[3].Pilot == "P2", "ascending order wrong: best=" + ranked[0].Pilot + " worst=" + ranked[3].Pilot);
            Assert.True(mf.Champion() == "P3", "ascending champion=" + mf.Champion() + " want P3");
        }

        // TestMainsSeedTieBreak verifies a points tie is broken purely by seeding order.
        [Fact]
        public void TestMainsSeedTieBreak()
        {
            Dictionary<int, int> pts = new Dictionary<int, int> { { 1, 3 }, { 2, 1 } };
            MainsFormat mf = MainsFormat.New(MainsConfig(2, L("A"), 2, MainsDescending, pts));
            // R1: P1,P2 → P1:3 P2:1 ; R2: P2,P1 → P2:3 P1:1. Both total 4 → seed → P1.
            mf.ApplyPlacement("A-R1", L("P1", "P2"));
            mf.ApplyPlacement("A-R2", L("P2", "P1"));
            List<Standing> ranked = mf.RankMain(mf.State().Mains[0]);
            Assert.True(ranked[0].Pilot == "P1" && ranked[1].Pilot == "P2", "expected seed order P1 then P2; got " + ranked[0].Pilot + ", " + ranked[1].Pilot);
            Assert.False(ranked[0].Tied, "seed-resolved tie should not be flagged");
        }

        // TestMainsDropWorst verifies the worst N rounds are discounted from each total.
        [Fact]
        public void TestMainsDropWorst()
        {
            Dictionary<int, int> pts = new Dictionary<int, int> { { 1, 10 }, { 2, 5 }, { 3, 1 } };
            FormatConfig cfg = MainsConfig(3, L("A"), 3, MainsDescending, pts);
            cfg.DropWorst = 1;
            MainsFormat mf = MainsFormat.New(cfg);
            // P1 finishes 1,1,3 → 10,10,1 ; drop worst (1) → 20.
            // P2 finishes 2,2,1 → 5,5,10 ; drop worst (5) → 15.
            // P3 finishes 3,3,2 → 1,1,5  ; drop worst (1) → 6.
            mf.ApplyPlacement("A-R1", L("P1", "P2", "P3"));
            mf.ApplyPlacement("A-R2", L("P1", "P2", "P3"));
            mf.ApplyPlacement("A-R3", L("P2", "P3", "P1"));
            Dictionary<string, int> want = new Dictionary<string, int> { { "P1", 20 }, { "P2", 15 }, { "P3", 6 } };
            foreach (Standing s in mf.RankMain(mf.State().Mains[0]))
            {
                int total = FscanTotal(s.Detail);
                Assert.True(total == want[s.Pilot], s.Pilot + " counted total=" + total + " want " + want[s.Pilot] + " (detail " + s.Detail + ")");
            }
            Assert.True(mf.Champion() == "P1", "champion=" + mf.Champion() + " want P1");
        }

        [Fact]
        public void TestMainsCompletionGating()
        {
            MainsFormat mf = MainsFormat.New(MainsConfig(6, L("A", "B"), 2, MainsDescending, F1Points()));
            Assert.False(mf.IsComplete(), "should not be complete before any rounds");
            Assert.True(mf.Champion() == "", "no champion before A main complete");
            // Complete A main only.
            Main a = mf.State().Mains[0];
            foreach (MainsRace r in a.Races)
            {
                mf.ApplyPlacement(r.Id, r.Pilots);
            }
            Assert.True(mf.Champion() != "", "A-main champion should be set once A main complete");
            Assert.False(mf.IsComplete(), "not complete until B main done too");
            Main b = mf.State().Mains[1];
            foreach (MainsRace r in b.Races)
            {
                mf.ApplyPlacement(r.Id, r.Pilots);
            }
            Assert.True(mf.IsComplete(), "should be complete after all mains");
        }

        // TestMainsExplicitRosters checks that a manual pilot→main assignment is honoured
        // (not seed-chunked) and that points ties still break by global seed order.
        [Fact]
        public void TestMainsExplicitRosters()
        {
            FormatConfig cfg = MainsConfig(4, L("A", "B"), 2, MainsDescending, new Dictionary<int, int> { { 1, 10 }, { 2, 5 } });
            // Manually put seeds 1 & 4 in A, seeds 2 & 3 in B (not the seed-chunk default).
            cfg.MainsRosters = new Dictionary<string, List<string>> { { "A", L("P1", "P4") }, { "B", L("P2", "P3") } };
            MainsFormat mf = MainsFormat.New(cfg);
            Main a = mf.State().Mains[0];
            Assert.True(a.Pilots.Count == 2 && a.Pilots[0] == "P1" && a.Pilots[1] == "P4", "A main roster = " + string.Join(",", a.Pilots) + ", want [P1 P4] (seed-ordered)");
            Assert.True(mf.State().Mains[1].Pilots[0] == "P2", "B main roster should have P2 first");
            // A main, both rounds: P1 and P4 alternate 1st/2nd → both total 15. Tie must
            // break by global seed: P1 (seed 0) ahead of P4 (seed 3).
            mf.ApplyPlacement("A-R1", L("P1", "P4"));
            mf.ApplyPlacement("A-R2", L("P4", "P1"));
            List<Standing> ranked = mf.RankMain(a);
            Assert.True(ranked[0].Pilot == "P1" && ranked[1].Pilot == "P4", "tie should break by global seed P1>P4; got " + ranked[0].Pilot + ", " + ranked[1].Pilot);
        }

        // TestMainsRosterValidation rejects assignments that don't cover every pilot once.
        [Fact]
        public void TestMainsRosterValidation()
        {
            FormatConfig cfg = MainsConfig(4, L("A", "B"), 2, MainsDescending, new Dictionary<int, int> { { 1, 10 }, { 2, 5 } });
            cfg.MainsRosters = new Dictionary<string, List<string>> { { "A", L("P1", "P2") }, { "B", L("P3") } }; // P4 missing
            Assert.Throws<BracketException>(() => MainsFormat.New(cfg));
            cfg.MainsRosters = new Dictionary<string, List<string>> { { "A", L("P1", "P2") }, { "B", L("P2", "P3", "P4") } }; // P2 twice
            Assert.Throws<BracketException>(() => MainsFormat.New(cfg));
        }

        [Fact]
        public void TestMainsStateRoundTrip()
        {
            FormatConfig cfg = MainsConfig(9, L("A", "B", "C"), 3, MainsDescending, F1Points());
            MainsFormat mf = MainsFormat.New(cfg);
            mf.ApplyPlacement("A-R1", mf.State().Mains[0].Races[0].Pilots);
            string blob = mf.StateJson();
            IRaceFormat reloaded = RaceFormats.FromState(FormatKinds.Mains, cfg, blob);
            string again = reloaded.StateJson();
            Assert.True(blob == again, "mains state round-trip mismatch");
        }

        [Fact]
        public void TestMainsValidation()
        {
            // No points table.
            Assert.Throws<BracketException>(() => MainsFormat.New(MainsConfig(4, L("A"), 2, MainsDescending, null)));
            // Bad direction.
            Assert.Throws<BracketException>(() => MainsFormat.New(MainsConfig(4, L("A"), 2, "sideways", F1Points())));
            // More mains than pilots.
            Assert.Throws<BracketException>(() => MainsFormat.New(MainsConfig(1, L("A", "B"), 1, MainsDescending, F1Points())));
            // Drop-worst must be fewer than the round count.
            FormatConfig dw = MainsConfig(4, L("A"), 3, MainsDescending, F1Points());
            dw.DropWorst = 3;
            Assert.Throws<BracketException>(() => MainsFormat.New(dw));
        }

        // TestMainsDivisions verifies that grouping mains into divisions gives each
        // division its own champion (winner of its top main) and that there is no single
        // overall champion when more than one division exists.
        [Fact]
        public void TestMainsDivisions()
        {
            FormatConfig cfg = MainsConfig(8, L("A", "B", "C", "D"), 1, MainsDescending, F1Points());
            cfg.MainsDivisions = new List<MainsDivision>
            {
                new MainsDivision { Name = "Sport", Mains = L("A", "B") },
                new MainsDivision { Name = "Open", Mains = L("C", "D") },
            };
            MainsFormat mf = MainsFormat.New(cfg);
            // Default seed-chunk: A=[P1,P2] B=[P3,P4] C=[P5,P6] D=[P7,P8].
            mf.ApplyPlacement("A-R1", L("P2", "P1")); // P2 wins Sport's top main
            mf.ApplyPlacement("B-R1", L("P3", "P4"));
            mf.ApplyPlacement("C-R1", L("P6", "P5")); // P6 wins Open's top main
            mf.ApplyPlacement("D-R1", L("P7", "P8"));
            // No single overall champion with multiple divisions.
            Assert.True(mf.Champion() == "", "overall champion should be empty with divisions, got " + mf.Champion());
            List<DivisionStandings> ds = mf.DivisionStandings();
            Assert.True(ds.Count == 2, "want 2 divisions, got " + ds.Count);
            Assert.True(ds[0].Name == "Sport" && ds[0].Champion == "P2", "division 0 = " + ds[0].Name + " champ " + ds[0].Champion + ", want Sport/P2");
            Assert.True(ds[1].Name == "Open" && ds[1].Champion == "P6", "division 1 = " + ds[1].Name + " champ " + ds[1].Champion + ", want Open/P6");
            Assert.True(ds[0].Complete && ds[1].Complete, "both divisions should be complete");
            // Global standings follow division order: Sport (A then B) before Open (C then D).
            IReadOnlyList<Standing> st = mf.Standings();
            Assert.True(st.Count == 8 && st[0].Pilot == "P2" && st[4].Pilot == "P6", "standings order wrong (len " + st.Count + ")");
        }

        // TestMainsDivisionValidation rejects groupings that don't cover the mains exactly.
        [Fact]
        public void TestMainsDivisionValidation()
        {
            System.Func<FormatConfig> baseCfg = () => MainsConfig(8, L("A", "B", "C", "D"), 1, MainsDescending, F1Points());
            // A main left out of every division.
            FormatConfig c = baseCfg();
            c.MainsDivisions = new List<MainsDivision> { new MainsDivision { Name = "X", Mains = L("A", "B") }, new MainsDivision { Name = "Y", Mains = L("C") } };
            Assert.Throws<BracketException>(() => MainsFormat.New(c));
            // A main in two divisions.
            c = baseCfg();
            c.MainsDivisions = new List<MainsDivision> { new MainsDivision { Name = "X", Mains = L("A", "B") }, new MainsDivision { Name = "Y", Mains = L("B", "C", "D") } };
            Assert.Throws<BracketException>(() => MainsFormat.New(c));
            // Unknown main referenced.
            c = baseCfg();
            c.MainsDivisions = new List<MainsDivision> { new MainsDivision { Name = "X", Mains = L("A", "B", "C", "D", "Z") } };
            Assert.Throws<BracketException>(() => MainsFormat.New(c));
            // A single division of everything keeps an overall champion.
            c = baseCfg();
            c.MainsDivisions = new List<MainsDivision> { new MainsDivision { Name = "All", Mains = L("A", "B", "C", "D") } };
            MainsFormat mf = MainsFormat.New(c);
            foreach (string id in new[] { "A-R1", "B-R1", "C-R1", "D-R1" })
            {
                Main owner;
                MainsRace r = mf.FindRace(id, out owner);
                try
                {
                    mf.ApplyPlacement(id, r.Pilots);
                }
                catch (BracketException)
                {
                }
            }
            Assert.True(mf.Champion() != "", "a single division should still produce an overall champion");
        }

        // TestMainsDivisionSkills verifies a division's optional skill tags are normalised
        // (trimmed, blanks dropped) and survive a state round-trip. The skill filter itself
        // is applied in the setup form; the engine just carries the tags for reference.
        [Fact]
        public void TestMainsDivisionSkills()
        {
            FormatConfig cfg = MainsConfig(8, L("A", "B", "C", "D"), 1, MainsDescending, F1Points());
            cfg.MainsDivisions = new List<MainsDivision>
            {
                new MainsDivision { Name = "Sport", Mains = L("A", "B"), Skills = L(" Pro ", "") },
                new MainsDivision { Name = "Open", Mains = L("C", "D"), Skills = L("Intermediate", "Novice") },
            };
            MainsFormat mf = MainsFormat.New(cfg);
            List<MainsDivision> divs = mf.State().Cfg.Divisions;
            Assert.True(divs.Count == 2, "want 2 divisions, got " + divs.Count);
            Assert.True(divs[0].Skills != null && divs[0].Skills.Count == 1 && divs[0].Skills[0] == "Pro", "Sport skills not normalised");
            Assert.True(divs[1].Skills.Count == 2 && divs[1].Skills[0] == "Intermediate" && divs[1].Skills[1] == "Novice", "Open skills wrong");
            // Round-trip through the persisted state blob.
            string blob = mf.StateJson();
            IRaceFormat reloaded = RaceFormats.FromState(FormatKinds.Mains, cfg, blob);
            List<MainsDivision> rs = ((MainsFormat)reloaded).State().Cfg.Divisions;
            Assert.True(rs.Count == 2 && rs[1].Skills[1] == "Novice", "skills lost on round-trip");
        }

        // TestMainScoreboard verifies the structured per-main scoreboard (used by the OBS
        // overlay): row order matches RankMain, per-round point cells, the dropped flag,
        // and blank cells for rounds not yet run.
        [Fact]
        public void TestMainScoreboard()
        {
            Dictionary<int, int> pts = new Dictionary<int, int> { { 1, 10 }, { 2, 5 }, { 3, 1 } };
            FormatConfig cfg = MainsConfig(3, L("A"), 3, MainsDescending, pts);
            cfg.DropWorst = 1;
            MainsFormat mf = MainsFormat.New(cfg);
            // Only the first two rounds are run; round 3 stays pending (blank cells).
            // P1 finishes 1,1 → 10,10. Drop is capped to len(rounds)-1 = 1, so P1's worst
            // of the two 10s is dropped → counted total 10.
            mf.ApplyPlacement("A-R1", L("P1", "P2", "P3"));
            mf.ApplyPlacement("A-R2", L("P1", "P2", "P3"));

            MainScoreboard sb;
            Assert.False(mf.MainScoreboard("Z", out sb), "MainScoreboard(\"Z\") should not resolve a nonexistent main");
            Assert.True(mf.MainScoreboard("", out sb), "MainScoreboard(\"\") not ok"); // empty selects the first main
            Assert.True(sb.Label == "A" && sb.Rounds == 3 && sb.Rows.Count == 3, "scoreboard label=" + sb.Label + " rounds=" + sb.Rounds + " want A/3/3");
            Assert.True(sb.Rows[0].Pilot == "P1", "row0=" + sb.Rows[0].Pilot + " want P1"); // finishing order, winner first
            MainScoreRow p1 = sb.Rows[0];
            Assert.True(p1.Cells.Count == 3, "P1 cells=" + p1.Cells.Count + " want 3");
            Assert.True(p1.Cells[0].HasResult && p1.Cells[1].HasResult, "P1 rounds 1+2 should have results");
            Assert.True(p1.Cells[0].Points == 10 && p1.Cells[1].Points == 10, "P1 round points want 10,10");
            Assert.True(p1.Cells[0].Position == 1 && p1.Cells[1].Position == 1, "P1 round positions want 1,1"); // P1 won both run rounds
            Assert.False(p1.Cells[2].HasResult, "P1 round 3 should be blank (not run)");
            int dropped = 0;
            foreach (MainRoundCell c in p1.Cells)
            {
                if (c.Dropped)
                {
                    dropped++;
                }
            }
            Assert.True(dropped == 1, "P1 dropped cells=" + dropped + " want 1");
            Assert.True(p1.Total == 10, "P1 total=" + p1.Total + " want 10 (20 minus one dropped 10)");
        }

        // FscanTotal parses the leading integer of a detail string like "43 pts (…)".
        private static int FscanTotal(string s)
        {
            int n = 0;
            int i = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                n = n * 10 + (s[i] - '0');
                i++;
            }
            return n;
        }
    }
}
