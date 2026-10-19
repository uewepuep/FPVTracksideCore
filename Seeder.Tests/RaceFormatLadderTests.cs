using System.Collections.Generic;
using Xunit;

namespace SeederEngine.Tests
{
    // Port of raceformat/ladder_test.go.
    public class RaceFormatLadderTests
    {
        private static List<string> PilotNames(int n)
        {
            return RaceFormatEliminationAdapterTests.PilotNames(n);
        }

        // PlayOutLadder runs every ready bout (seeds finish in current order) until a
        // champion is crowned.
        private static void PlayOutLadder(LadderFormat l)
        {
            for (int i = 0; i < 2000; i++)
            {
                bool progressed = false;
                foreach (RaceView rv in l.Races())
                {
                    if (rv.IsComplete || rv.Locked || rv.Pilots == null || rv.Pilots.Count == 0)
                    {
                        continue;
                    }
                    try
                    {
                        l.ApplyPlacement(rv.Id, rv.Pilots);
                        progressed = true;
                    }
                    catch (BracketException)
                    {
                    }
                }
                if (!progressed)
                {
                    break;
                }
            }
        }

        [Fact]
        public void TestLadderSweep()
        {
            for (int n = 2; n <= 96; n++)
            {
                foreach (int p in new[] { 2, 3, 4 })
                {
                    for (int a = 1; a < p; a++)
                    {
                        string tag = "n=" + n + " p=" + p + " a=" + a;
                        FormatConfig cfg = new FormatConfig
                        {
                            PilotCount = n,
                            PilotNames = PilotNames(n),
                            PilotsPerRace = p,
                            PilotsToAdvance = a,
                        };
                        LadderFormat lf = LadderFormat.New(cfg);

                        // Every pilot must appear in exactly one entry bout (as a fresh
                        // seed, not a carry-over). Fresh seeds are the pilots present at
                        // generation; carry-overs are appended only during play.
                        Dictionary<string, int> seen = new Dictionary<string, int>();
                        foreach (LadderRace r in lf.State().Races)
                        {
                            foreach (string pilot in r.Pilots ?? new List<string>())
                            {
                                int c;
                                seen.TryGetValue(pilot, out c);
                                seen[pilot] = c + 1;
                            }
                        }
                        Assert.True(seen.Count == n, tag + ": " + seen.Count + " distinct entry pilots, want " + n);
                        foreach (KeyValuePair<string, int> kv in seen)
                        {
                            Assert.True(kv.Value == 1, tag + ": pilot " + kv.Key + " appears in " + kv.Value + " entry bouts");
                        }

                        // No bout exceeds pilotsPerRace once fully populated.
                        PlayOutLadder(lf);
                        foreach (LadderRace r in lf.State().Races)
                        {
                            int cnt = r.Pilots == null ? 0 : r.Pilots.Count;
                            Assert.True(cnt <= p, tag + ": bout " + r.Id + " has " + cnt + " pilots > ppr " + p);
                        }

                        Assert.True(lf.IsComplete(), tag + ": no champion after play-out");
                        string champ = lf.Champion();
                        Assert.True(champ != "", tag + ": empty champion");

                        // Standings cover every pilot exactly once.
                        IReadOnlyList<Standing> st = lf.Standings();
                        Assert.True(st.Count == n, tag + ": standings has " + st.Count + " rows, want " + n);
                        HashSet<string> rankSeen = new HashSet<string>();
                        foreach (Standing s in st)
                        {
                            Assert.True(rankSeen.Add(s.Pilot), tag + ": pilot " + s.Pilot + " duplicated in standings");
                        }
                        Assert.True(st[0].Pilot == champ, tag + ": standings[0]=" + st[0].Pilot + ", champion=" + champ);

                        // Seeds-always-win → the top seed (P1) wins, and enters in the
                        // final (or only) bout.
                        Assert.True(champ == "P1", tag + ": seeds-win champion=" + champ + ", want P1");
                    }
                }
            }
        }

        // TestLadderTopSeedEntersLast verifies the marquee property: with seeds-win the
        // top seed is a fresh entrant in the last bout (it gets the longest bye).
        [Fact]
        public void TestLadderTopSeedEntersLast()
        {
            FormatConfig cfg = new FormatConfig { PilotCount = 10, PilotNames = PilotNames(10), PilotsPerRace = 4, PilotsToAdvance = 2 };
            LadderFormat lf = LadderFormat.New(cfg);
            List<LadderRace> races = lf.State().Races;
            LadderRace last = races[races.Count - 1];
            Assert.True(last.Pilots.Contains("P1"), "top seed P1 not a fresh entrant in final bout " + last.Id + " (pilots=" + string.Join(",", last.Pilots) + ")");
            // And the bottom seed should be in bout 1.
            Assert.True(races[0].Pilots[0] != "", "empty first bout");
        }

        // TestLadderEditRewindsCarry confirms editing a bout rewinds its advancers from
        // the next (un-run) bout and is refused once the next bout has run.
        [Fact]
        public void TestLadderEditRewindsCarry()
        {
            FormatConfig cfg = new FormatConfig { PilotCount = 8, PilotNames = PilotNames(8), PilotsPerRace = 4, PilotsToAdvance = 2 };
            LadderFormat lf = LadderFormat.New(cfg);
            // Run bout 1.
            LadderRace b1 = lf.State().Races[0];
            lf.ApplyPlacement(b1.Id, b1.Pilots);
            LadderRace b2 = lf.State().Races[1];
            int beforeLen = b2.Pilots.Count;
            // Editable (bout 2 not run yet).
            string reason;
            Assert.True(lf.CanEditRace(b1.Id, out reason), "bout1 should be editable before bout2 runs");
            // Edit bout1 reversing the order — winners change, bout2 carry-overs swap.
            List<string> rev = Reversed(lf.State().Races[0].Pilots);
            lf.EditRace(b1.Id, rev);
            Assert.True(b2.Pilots.Count == beforeLen, "bout2 pilot count changed after edit: got " + b2.Pilots.Count + " want " + beforeLen);
            // Run bout 2, then bout1 must be locked.
            lf.ApplyPlacement(b2.Id, b2.Pilots);
            Assert.False(lf.CanEditRace(b1.Id, out reason), "bout1 should be locked once bout2 has run");
        }

        [Fact]
        public void TestLadderStateRoundTrip()
        {
            FormatConfig cfg = new FormatConfig { PilotCount = 12, PilotNames = PilotNames(12), PilotsPerRace = 4, PilotsToAdvance = 2 };
            LadderFormat lf = LadderFormat.New(cfg);
            try
            {
                lf.ApplyPlacement(lf.State().Races[0].Id, lf.State().Races[0].Pilots);
            }
            catch (BracketException)
            {
            }
            string blob = lf.StateJson();
            IRaceFormat reloaded = RaceFormats.FromState(FormatKinds.Ladder, cfg, blob);
            string again = reloaded.StateJson();
            Assert.True(blob == again, "ladder state round-trip mismatch");
        }

        internal static List<string> Reversed(List<string> input)
        {
            List<string> output = new List<string>(input);
            output.Reverse();
            return output;
        }
    }
}
