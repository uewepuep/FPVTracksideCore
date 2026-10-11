using System.Collections.Generic;
using Xunit;

namespace BracketEngine.Tests
{
    // Port of raceformat/standings_test.go.
    public class RaceFormatStandingsTests
    {
        private static Dictionary<string, int> SeedsFor(int n)
        {
            Dictionary<string, int> m = new Dictionary<string, int>();
            List<string> names = RaceFormatEliminationAdapterTests.PilotNames(n);
            for (int i = 0; i < names.Count; i++)
            {
                m[names[i]] = i + 1;
            }
            return m;
        }

        // TestFinalOrderingEliminationCoversAllPilots drives each elimination/finals combo
        // to completion (seeds win) and checks FinalOrdering fills a contiguous, complete,
        // duplicate-free ranking with the real champion at rank 1 — even though the
        // engine's own Standings() only knows the champion / CTA finalists.
        [Fact]
        public void TestFinalOrderingEliminationCoversAllPilots()
        {
            string[][] combos = new string[][]
            {
                new[] { "single", "double-elimination" },
                new[] { "double", "double-elimination" },
                new[] { "single", "chase-the-ace" },
                new[] { "double", "chase-the-ace" },
            };
            foreach (string[] c in combos)
            {
                foreach (int n in new[] { 4, 5, 8, 12, 16, 32 })
                {
                    string tag = c[0] + "/" + c[1] + " n=" + n;
                    FormatConfig cfg = new FormatConfig
                    {
                        PilotCount = n,
                        PilotNames = RaceFormatEliminationAdapterTests.PilotNames(n),
                        EliminationType = c[0],
                        PilotsPerRace = 4,
                        PilotsToAdvance = 2,
                        FinalsFormat = c[1],
                        AceWinsRequired = 2,
                    };
                    IRaceFormat f = RaceFormats.Create(FormatKinds.Elimination, cfg);
                    RaceFormatEliminationAdapterTests.PlayOut(f);
                    Assert.True(f.IsComplete(), tag + ": playOut left bracket incomplete");

                    IReadOnlyList<Standing> order = RaceFormats.FinalOrdering(f, SeedsFor(n));
                    Assert.True(order.Count == n, tag + ": ordering has " + order.Count + " rows, want " + n);
                    Assert.True(order[0].Pilot == f.Champion(), tag + ": rank 1 = " + order[0].Pilot + ", want champion " + f.Champion());
                    HashSet<string> seen = new HashSet<string>();
                    for (int i = 0; i < order.Count; i++)
                    {
                        Standing s = order[i];
                        Assert.True(s.Rank == i + 1, tag + ": row " + i + " has rank " + s.Rank);
                        Assert.True(seen.Add(s.Pilot), tag + ": pilot " + s.Pilot + " appears twice");
                    }
                    foreach (string p in RaceFormatEliminationAdapterTests.PilotNames(n))
                    {
                        Assert.True(seen.Contains(p), tag + ": pilot " + p + " missing from ordering");
                    }
                }
            }
        }

        // TestFinalOrderingMatchesStandingsWhenComplete confirms that for formats whose
        // Standings() already ranks every pilot (mains, ladder), FinalOrdering is a
        // pass-through — same pilots, same order, nothing appended.
        [Fact]
        public void TestFinalOrderingMatchesStandingsWhenComplete()
        {
            // Mains: 8 pilots, A/B mains, 2 rounds, F1 points.
            MainsFormat mf = MainsFormat.New(RaceFormatMainsTests.MainsConfig(8, new List<string> { "A", "B" }, 2, "descending", RaceFormatMainsTests.F1Points()));
            RaceFormatEliminationAdapterTests.PlayOut(mf);
            Assert.True(mf.IsComplete(), "mains: playOut left format incomplete");
            AssertOrderingEqualsStandings("mains", mf, SeedsFor(8));

            // Ladder: 9 pilots, 4 per race, top 2 advance.
            LadderFormat lf = LadderFormat.New(new FormatConfig
            {
                PilotCount = 9,
                PilotNames = RaceFormatEliminationAdapterTests.PilotNames(9),
                PilotsPerRace = 4,
                PilotsToAdvance = 2,
            });
            RaceFormatEliminationAdapterTests.PlayOut(lf);
            Assert.True(lf.IsComplete(), "ladder: playOut left format incomplete");
            AssertOrderingEqualsStandings("ladder", lf, SeedsFor(9));
        }

        private static void AssertOrderingEqualsStandings(string label, IRaceFormat f, Dictionary<string, int> seeds)
        {
            IReadOnlyList<Standing> std = f.Standings();
            IReadOnlyList<Standing> order = RaceFormats.FinalOrdering(f, seeds);
            Assert.True(order.Count == std.Count, label + ": ordering has " + order.Count + " rows, Standings has " + std.Count);
            for (int i = 0; i < std.Count; i++)
            {
                Assert.True(order[i].Pilot == std[i].Pilot, label + ": row " + i + " = " + order[i].Pilot + ", want " + std[i].Pilot);
            }
        }
    }
}
