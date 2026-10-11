using System.Collections.Generic;
using Xunit;

namespace Brackets.Tests
{
    // Port of raceformat/reset_test.go.
    public class RaceFormatResetTests
    {
        // AppearsInOtherRace reports whether any of names appears in a race (other than
        // exceptId) across the format's heats — used to detect downstream propagation.
        private static bool AppearsInOtherRace(IRaceFormat f, string exceptId, IEnumerable<string> names)
        {
            HashSet<string> set = new HashSet<string>(names);
            foreach (RaceView rv in f.Races())
            {
                if (rv.Id == exceptId)
                {
                    continue;
                }
                foreach (string p in rv.Pilots ?? new List<string>())
                {
                    if (set.Contains(p))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static RaceView RaceByID(IRaceFormat f, string id)
        {
            foreach (RaceView rv in f.Races())
            {
                if (rv.Id == id)
                {
                    return rv;
                }
            }
            return new RaceView();
        }

        // TestResetRace checks that ResetRace un-runs a completed heat, strips its
        // advancers from downstream, is a no-op when not run, and is refused once a
        // downstream heat has been run.
        [Fact]
        public void TestResetRace()
        {
            System.Func<IRaceFormat> mk = () => EliminationFormat.New(new FormatConfig
            {
                PilotCount = 8,
                PilotNames = RaceFormatEliminationAdapterTests.PilotNames(8),
                EliminationType = "double",
                PilotsPerRace = 4,
                PilotsToAdvance = 2,
                FinalsFormat = "double-elimination",
            });

            // 1. Run one first-round heat, then reset it.
            IRaceFormat f = mk();
            RaceView first = null;
            foreach (RaceView rv in f.Races())
            {
                if (rv.Pilots != null && rv.Pilots.Count == 4 && !rv.IsComplete)
                {
                    first = rv;
                    break;
                }
            }
            Assert.True(first != null, "no runnable first-round race");
            List<string> order = new List<string>(first.Pilots);
            f.ApplyPlacement(first.Id, order);
            Assert.True(RaceByID(f, first.Id).IsComplete, "race should be complete after apply");
            Assert.True(AppearsInOtherRace(f, first.Id, order.GetRange(0, 2)), "winners should have propagated downstream after apply");

            f.ResetRace(first.Id);
            Assert.False(RaceByID(f, first.Id).IsComplete, "race should be pending after reset");
            Assert.False(AppearsInOtherRace(f, first.Id, order.GetRange(0, 2)), "winners should be stripped from downstream after reset");

            // 2. Reset on a not-run race is a no-op (no error).
            f.ResetRace(first.Id);

            // 3. Reset is refused once a downstream heat has been run.
            IRaceFormat f2 = mk();
            List<RaceFormatEliminationAdapterTests.Move> moves = RaceFormatEliminationAdapterTests.PlayOut(f2); // play the whole thing
            Assert.True(moves.Count != 0, "expected the bracket to play out");
            Assert.Throws<BracketException>(() => f2.ResetRace(moves[0].RaceId));
        }
    }
}
