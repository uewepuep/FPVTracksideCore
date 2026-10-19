using System.Collections.Generic;
using Xunit;

namespace SeederEngine.Tests
{
    // Port of raceformat/elimination_adapter_test.go.
    public class RaceFormatEliminationAdapterTests
    {
        // PilotNames builds n sequential seed names ("P1".."Pn").
        internal static List<string> PilotNames(int n)
        {
            List<string> output = new List<string>();
            for (int i = 0; i < n; i++)
            {
                output.Add("P" + (i + 1));
            }
            return output;
        }

        internal class Move
        {
            public string RaceId;
            public List<string> Placement;
        }

        // PlayOut runs every runnable race (seeds finish in their current race order)
        // until no progress is made, driving the format purely through the IRaceFormat
        // interface, and records every move applied so it can be replayed verbatim.
        internal static List<Move> PlayOut(IRaceFormat f)
        {
            List<Move> moves = new List<Move>();
            for (int i = 0; i < 500; i++)
            {
                bool progressed = false;
                foreach (RaceView rv in f.Races())
                {
                    if (rv.IsComplete || rv.IsVoid || rv.Locked)
                    {
                        continue;
                    }
                    if (rv.Pilots == null || rv.Pilots.Count == 0)
                    {
                        continue;
                    }
                    List<string> placement = new List<string>(rv.Pilots);
                    try
                    {
                        f.ApplyPlacement(rv.Id, placement);
                        moves.Add(new Move { RaceId = rv.Id, Placement = placement });
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
            return moves;
        }

        // TestEliminationAdapterParity drives a bracket through both the raw engine and
        // the format adapter with identical placements and asserts identical champion +
        // serialized state. This is the de-risking gate: the adapter must be a faithful
        // pass-through.
        [Fact]
        public void TestEliminationAdapterParity()
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
                for (int n = 2; n <= 48; n++)
                {
                    string tag = c[0] + "/" + c[1] + " n=" + n;
                    FormatConfig cfg = new FormatConfig
                    {
                        PilotCount = n,
                        PilotNames = PilotNames(n),
                        EliminationType = c[0],
                        PilotsPerRace = 4,
                        PilotsToAdvance = 2,
                        FinalsFormat = c[1],
                        AceWinsRequired = 2,
                    };

                    // Drive the adapter and capture the exact move sequence.
                    IRaceFormat f = RaceFormats.Create(FormatKinds.Elimination, cfg);
                    List<Move> moves = PlayOut(f);

                    // Replay the identical sequence on the raw engine.
                    Bracket raw = Bracket.New(EliminationFormat.EliminationConfig(cfg));
                    foreach (Move mv in moves)
                    {
                        raw.ApplyPlacement(mv.RaceId, mv.Placement);
                    }

                    Assert.True(raw.Champion() == f.Champion(), tag + " champion mismatch: raw=" + raw.Champion() + " adapter=" + f.Champion());
                    string rawJson = BracketJson.Serialize(raw.State());
                    string adpJson = f.StateJson();
                    Assert.True(rawJson == adpJson, tag + " state mismatch");
                }
            }
        }

        // TestFromStateRoundTrip confirms a serialized adapter state rehydrates to an
        // equivalent format (the path buildFormat takes when loading a persisted row).
        [Fact]
        public void TestFromStateRoundTrip()
        {
            FormatConfig cfg = new FormatConfig
            {
                PilotCount = 16,
                PilotNames = PilotNames(16),
                EliminationType = "double",
                PilotsPerRace = 4,
                PilotsToAdvance = 2,
                FinalsFormat = "chase-the-ace",
                AceWinsRequired = 2,
            };
            IRaceFormat f = RaceFormats.Create(FormatKinds.Elimination, cfg);
            // Run a couple of races, persist, reload.
            IReadOnlyList<RaceView> races = f.Races();
            if (races.Count > 0)
            {
                try
                {
                    f.ApplyPlacement(races[0].Id, races[0].Pilots);
                }
                catch (BracketException)
                {
                }
            }
            string blob = f.StateJson();
            IRaceFormat reloaded = RaceFormats.FromState(FormatKinds.Elimination, cfg, blob);
            string again = reloaded.StateJson();
            Assert.True(blob == again, "round-trip state mismatch");
        }
    }
}
