using System.Collections.Generic;
using Xunit;

namespace SeederEngine.Tests
{
    // Port of raceformat/mainsprogression_test.go.
    public class RaceFormatMainsProgressionTests
    {
        private static List<string> MpSeedNames(int n)
        {
            List<string> output = new List<string>();
            for (int i = 0; i < n; i++)
            {
                output.Add("P" + (i + 1));
            }
            return output;
        }

        [Fact]
        public void TestMainsProgressionStateRoundTrip()
        {
            FormatConfig cfg = new FormatConfig { PilotNames = MpSeedNames(64), PilotsPerMain = 16, BumpUp = 4 };
            IRaceFormat f = RaceFormats.Create(FormatKinds.MainsProgression, cfg);
            // Only the lowest main is built up front → its heats appear in Races().
            Assert.True(f.Races().Count != 0, "expected the lowest main's heats in Races()");
            string blob = f.StateJson();
            IRaceFormat f2 = RaceFormats.FromState(FormatKinds.MainsProgression, cfg, blob);
            Assert.True(f2.Races().Count == f.Races().Count, "round-trip changed race count: " + f.Races().Count + " → " + f2.Races().Count);
        }

        [Fact]
        public void TestMainsProgressionRacePrefixes()
        {
            IRaceFormat f = RaceFormats.Create(FormatKinds.MainsProgression, new FormatConfig { PilotNames = MpSeedNames(64), PilotsPerMain = 16, BumpUp = 4 });
            foreach (RaceView r in f.Races())
            {
                // Every projected race id is "<label>:<inner>" and resolvable for editing.
                Assert.True(r.Id.Length >= 3 && r.Id[1] == ':', "race id " + r.Id + " not main-prefixed");
            }
        }

        [Fact]
        public void TestMainsProgressionFinalOrderingCoversField()
        {
            FormatConfig cfg = new FormatConfig { PilotNames = MpSeedNames(64), PilotsPerMain = 16, BumpUp = 4 };
            IRaceFormat f = RaceFormats.Create(FormatKinds.MainsProgression, cfg);
            // Drive the whole competition seeds-win via the engine's Simulate, rehydrate,
            // then assert FinalOrdering ranks all 64 with no dupes.
            MainsProgressionFormat mp = (MainsProgressionFormat)f;
            string blob = BracketJson.Serialize(mp.Mains().Simulate());
            IRaceFormat done = RaceFormats.FromState(FormatKinds.MainsProgression, cfg, blob);
            Assert.True(done.IsComplete(), "simulated competition should be complete");
            Dictionary<string, int> seeds = new Dictionary<string, int>();
            for (int i = 0; i < cfg.PilotNames.Count; i++)
            {
                seeds[cfg.PilotNames[i]] = i + 1;
            }
            IReadOnlyList<Standing> order = RaceFormats.FinalOrdering(done, seeds);
            Assert.True(order.Count == 64, "FinalOrdering covers " + order.Count + ", want 64");
            HashSet<string> seen = new HashSet<string>();
            foreach (Standing s in order)
            {
                Assert.True(seen.Add(s.Pilot), "duplicate in FinalOrdering: " + s.Pilot);
            }
            Assert.True(done.Champion() == "P1", "champion = " + done.Champion() + ", want P1");
        }
    }
}
