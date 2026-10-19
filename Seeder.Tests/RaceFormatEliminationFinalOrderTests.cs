using System.Collections.Generic;
using Xunit;

namespace SeederEngine.Tests
{
    // Port of raceformat/elimination_final_order_test.go.
    public class RaceFormatEliminationFinalOrderTests
    {
        // OldDogs2026State is the stored bracket_state of the 2026 Nationals Old Dogs
        // bracket (race_brackets.id 110): single elimination, 6-up, top 3, with a
        // full finishing order recorded for the final (R3-1).
        private const string OldDogs2026State = @"{""races"": [{""id"": ""race-1"", ""round"": 1, ""losers"": [""halcyon"", ""Spanna""], ""pilots"": [""Foxur"", ""Clog wog"", ""Croz"", ""halcyon"", ""Spanna""], ""bracket"": ""winners"", ""winners"": [""Foxur"", ""Clog wog"", ""Croz""], ""isComplete"": true, ""fullPlacement"": true}, {""id"": ""race-3"", ""round"": 1, ""losers"": [""Tumblebee"", ""Has0""], ""pilots"": [""tilted"", ""tex"", ""JohnnyGMachine"", ""Tumblebee"", ""Has0""], ""bracket"": ""winners"", ""winners"": [""tilted"", ""tex"", ""JohnnyGMachine""], ""isComplete"": true, ""fullPlacement"": true}, {""id"": ""race-2"", ""round"": 1, ""losers"": [""Nacho"", ""A440""], ""pilots"": [""Voland"", ""bob9"", ""timmytron"", ""Nacho"", ""A440""], ""bracket"": ""winners"", ""winners"": [""Voland"", ""bob9"", ""timmytron""], ""isComplete"": true, ""fullPlacement"": true}, {""id"": ""race-4"", ""round"": 1, ""losers"": [""Shane O"", ""Karade""], ""pilots"": [""CGO"", ""MacDaddy"", ""Manic Tardis"", ""Shane O"", ""Karade""], ""bracket"": ""winners"", ""winners"": [""CGO"", ""MacDaddy"", ""Manic Tardis""], ""isComplete"": true, ""fullPlacement"": true}, {""id"": ""R2-1"", ""round"": 2, ""losers"": [""tex"", ""Croz"", ""Manic Tardis""], ""pilots"": [""Foxur"", ""MacDaddy"", ""bob9"", ""tex"", ""Croz"", ""Manic Tardis""], ""bracket"": ""winners"", ""winners"": [""Foxur"", ""MacDaddy"", ""bob9""], ""isComplete"": true, ""fullPlacement"": true}, {""id"": ""R2-2"", ""round"": 2, ""losers"": [""timmytron"", ""JohnnyGMachine"", ""tilted""], ""pilots"": [""Voland"", ""CGO"", ""Clog wog"", ""timmytron"", ""JohnnyGMachine"", ""tilted""], ""bracket"": ""winners"", ""winners"": [""Voland"", ""CGO"", ""Clog wog""], ""isComplete"": true, ""fullPlacement"": true}, {""id"": ""R3-1"", ""round"": 3, ""losers"": [""bob9"", ""Voland"", ""Clog wog"", ""MacDaddy"", ""CGO""], ""pilots"": [""Foxur"", ""bob9"", ""Voland"", ""Clog wog"", ""MacDaddy"", ""CGO""], ""bracket"": ""winners"", ""winners"": [""Foxur""], ""isComplete"": true, ""fullPlacement"": true}], ""rounds"": 3, ""currentRound"": 1}";

        // TestEliminationStandingsFollowTheFinal pins that the final's recorded
        // finishing order is the podium. Before the fix only the champion came from
        // the bracket and FinalOrdering ranked the rest of the final by seed, which
        // published Foxur, Voland, MacDaddy for a final run Foxur, bob9, Voland,
        // Clog wog.
        [Fact]
        public void TestEliminationStandingsFollowTheFinal()
        {
            List<string> names = new List<string> { "Foxur", "tilted", "Voland", "MacDaddy", "CGO", "bob9", "Tumblebee", "Clog wog", "halcyon", "tex",
                "timmytron", "Shane O", "Karade", "Nacho", "JohnnyGMachine", "Spanna", "Croz", "Has0", "A440", "Manic Tardis" };
            FormatConfig cfg = new FormatConfig
            {
                PilotCount = names.Count,
                PilotNames = names,
                EliminationType = "single",
                PilotsPerRace = 6,
                PilotsToAdvance = 3,
                FinalsFormat = "double-elimination",
                AceWinsRequired = 1,
            };
            EliminationFormat f = EliminationFormat.FromState(cfg, OldDogs2026State);
            Dictionary<string, int> seeds = new Dictionary<string, int>();
            for (int i = 0; i < names.Count; i++)
            {
                seeds[names[i]] = i + 1;
            }
            IReadOnlyList<Standing> got = RaceFormats.FinalOrdering(f, seeds);
            string[] want = { "Foxur", "bob9", "Voland", "Clog wog", "MacDaddy", "CGO" };
            Assert.True(got.Count >= want.Length, "got " + got.Count + " standings, want at least " + want.Length);
            for (int i = 0; i < want.Length; i++)
            {
                Assert.True(got[i].Pilot == want[i] && got[i].Rank == i + 1,
                    "rank " + (i + 1) + " = " + got[i].Pilot + " (rank " + got[i].Rank + "), want " + want[i]);
            }
            Assert.True(got.Count == names.Count, "FinalOrdering listed " + got.Count + " pilots, want all " + names.Count);
        }
    }
}
