using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Brackets.Tests
{
    /// <summary>
    /// Ports of handlers/race_bracket_public_test.go (TestProjectedCTAMirrorsLive) and
    /// handlers/race_bracket_projection_upset_test.go, plus tests of the projection API.
    /// </summary>
    public class BracketDiagramTests
    {
        private static HashSet<string> CtaIds(Bracket b)
        {
            return new HashSet<string>(b.State().Races.Where(r => r.Bracket == BracketSlot.ChaseTheAce).Select(r => r.Id));
        }

        private static List<string> PilotsOf(Bracket b, string id)
        {
            Race r = b.State().Races.FirstOrDefault(x => x.Id == id);
            return r == null ? null : r.Pilots;
        }

        private static List<string> Names(int n)
        {
            return Enumerable.Range(1, n).Select(i => "P" + i).ToList();
        }

        [Fact]
        public void ProjectedCTAMirrorsLive()
        {
            Bracket live = Bracket.New(new BracketConfig
            {
                PilotCount = 4,
                PilotNames = new List<string> { "A", "B", "C", "D" },
                EliminationType = EliminationType.Single,
                PilotsPerRace = 4,
                PilotsToAdvance = 2,
                FinalsFormat = FinalsFormat.ChaseTheAce,
                AceWinsRequired = 2,
            });
            Action<string> assertMirrors = stage =>
            {
                Bracket projected = BracketDiagram.Elimination(live).Projected;
                Assert.True(CtaIds(live).SetEquals(CtaIds(projected)), stage + ": projection CTA heats differ from live");
            };
            assertMirrors("initial");
            live.SelectFullPlacement("CTA-1", new List<string> { "A", "B", "C", "D" });
            assertMirrors("after CTA-1");
            Bracket p = BracketDiagram.Elimination(live).Projected;
            Assert.DoesNotContain(p.State().Races, r => r.Bracket == BracketSlot.ChaseTheAce && r.Id == "CTA-2" && r.IsComplete);
        }

        [Fact]
        public void ProjectionFollowsEnginePlacementAfterUpset()
        {
            List<string> names = new List<string> { "Wilf", "Red2rotor", "Enjenir", "Foxur", "Mirella", "Lucid", "Willman", "Bola", "bob9", "tex", "Zoppo" };
            Bracket live = Bracket.New(new BracketConfig
            {
                PilotCount = names.Count,
                PilotNames = names,
                EliminationType = EliminationType.Double,
                PilotsPerRace = 4,
                PilotsToAdvance = 2,
                FinalsFormat = FinalsFormat.ChaseTheAce,
                AceWinsRequired = 2,
            });
            List<string> w11 = PilotsOf(live, "W1-1");
            List<string> w12 = PilotsOf(live, "W1-2");
            List<string> upset = new List<string> { w11[w11.Count - 1] };
            upset.AddRange(w11.Take(w11.Count - 1));
            live.SelectFullPlacement("W1-1", upset);
            live.SelectFullPlacement("W1-2", new List<string>(w12));

            Bracket projected = BracketDiagram.Elimination(live).Projected;
            foreach (string id in new[] { "W2-1", "W2-2", "L1-1", "L1-2" })
            {
                HashSet<string> want = new HashSet<string>(Go.Iter(PilotsOf(live, id)));
                HashSet<string> got = new HashSet<string>(Go.Iter(PilotsOf(projected, id)).Where(p => !p.StartsWith("__slot_")));
                Assert.True(want.SetEquals(got), id + ": live " + string.Join(",", want) + " projected " + string.Join(",", got));
            }
        }

        [Fact]
        public void FreshEliminationIsFullyPredicted()
        {
            Bracket live = Bracket.New(new BracketConfig
            {
                PilotCount = 8, PilotNames = Names(8), EliminationType = EliminationType.Double,
                PilotsPerRace = 4, PilotsToAdvance = 2, FinalsFormat = FinalsFormat.GrandFinal,
            });
            string before = BracketJson.Serialize(live.State());
            BracketProjection p = BracketDiagram.Elimination(live);
            Assert.Equal(before, BracketJson.Serialize(live.State())); // live untouched
            Assert.NotSame(live, p.Projected);
            // Round 1 holds real pilots; every downstream heat is all placeholders.
            Assert.All(PilotsOf(p.Projected, "W1-1"), x => Assert.StartsWith("P", x));
            Assert.All(PilotsOf(p.Projected, "W2-1"), x => Assert.StartsWith("__slot_", x));
            Assert.True(p.PredictedSlots.ContainsKey("W2-1"));
            // Every placeholder gets exactly one connector into its race.
            foreach (KeyValuePair<string, Dictionary<string, PilotSlot>> race in p.PredictedSlots)
            {
                foreach (KeyValuePair<string, PilotSlot> slot in race.Value)
                {
                    int edges = p.Connectors.Count(c => c.ToId == race.Key && c.FromId == slot.Value.SourceRaceId && c.Pilots.Contains(slot.Key));
                    Assert.Equal(1, edges);
                }
            }
            // Placeholders are numbered in race order and never repeat.
            List<string> all = p.Projected.State().Races.SelectMany(r => Go.Iter(r.Pilots)).Where(x => x.StartsWith("__slot_")).ToList();
            Assert.Equal(all.Count, all.Distinct().Count());
        }

        [Fact]
        public void CompleteEliminationIsALiveCopy()
        {
            Bracket live = Bracket.New(new BracketConfig
            {
                PilotCount = 4, PilotNames = Names(4), EliminationType = EliminationType.Single,
                PilotsPerRace = 2, PilotsToAdvance = 1, FinalsFormat = FinalsFormat.GrandFinal,
            });
            for (int guard = 0; guard < 50 && !live.IsComplete(); guard++)
            {
                Race r = live.State().Races.First(x => !x.IsComplete && !x.IsVoid && Go.Len(x.Pilots) > 0);
                live.SelectFullPlacement(r.Id, new List<string>(r.Pilots));
            }
            Assert.True(live.IsComplete());
            BracketProjection p = BracketDiagram.Elimination(live);
            Assert.Null(p.PredictedSlots);
            Assert.NotSame(live.State(), p.Projected.State());
            Assert.Equal(BracketJson.Serialize(live.State()), BracketJson.Serialize(p.Projected.State()));
            Assert.Equal(BracketJson.Serialize(BracketFlow.ConnectorsFromState(live)), BracketJson.Serialize(p.Connectors));
        }

        [Fact]
        public void AugmentKeepsEveryPlaceholder()
        {
            // Go's augmentConnectorsWithPredicted can lose a placeholder appended to an
            // edge after the slice reallocated; the port must not.
            List<BracketConnector> baseConns = new List<BracketConnector>
            {
                new BracketConnector { FromId = "W1-1", ToId = "W2-1", Pilots = new List<string> { "P1" } },
            };
            Dictionary<string, Dictionary<string, PilotSlot>> pred = new Dictionary<string, Dictionary<string, PilotSlot>>
            {
                ["W2-1"] = new Dictionary<string, PilotSlot>
                {
                    ["__slot_2__"] = new PilotSlot { SourceRaceId = "W1-2", SourceRank = 1 },
                    ["__slot_1__"] = new PilotSlot { SourceRaceId = "W1-1", SourceRank = 2 },
                    ["__slot_3__"] = new PilotSlot { SourceRaceId = "W1-2", SourceRank = 2 },
                },
                ["L1-1"] = new Dictionary<string, PilotSlot>
                {
                    ["__slot_4__"] = new PilotSlot { SourceRaceId = "W1-1", SourceRank = 3 },
                    ["__slot_5__"] = new PilotSlot { SourceRaceId = "", SourceRank = 0 },
                },
            };
            List<BracketConnector> got = BracketDiagram.AugmentConnectorsWithPredicted(baseConns, pred);
            Assert.Single(baseConns[0].Pilots); // input not mutated
            Assert.Equal(new[] { "P1", "__slot_1__" }, got.Single(c => c.FromId == "W1-1" && c.ToId == "W2-1").Pilots);
            Assert.Equal(new[] { "__slot_2__", "__slot_3__" }, got.Single(c => c.FromId == "W1-2" && c.ToId == "W2-1").Pilots);
            Assert.Equal(new[] { "__slot_4__" }, got.Single(c => c.FromId == "W1-1" && c.ToId == "L1-1").Pilots);
            Assert.Equal(3, got.Count);
            Assert.Same(baseConns, BracketDiagram.AugmentConnectorsWithPredicted(baseConns, null));
        }

        [Fact]
        public void LiveFinisherAtRankAndRanks()
        {
            Race full = new Race { Id = "X", IsComplete = true, FullPlacement = true, Pilots = new List<string> { "a", "b", "c" } };
            string p;
            Assert.True(BracketDiagram.LiveFinisherAtRank(full, 2, out p));
            Assert.Equal("b", p);
            Assert.False(BracketDiagram.LiveFinisherAtRank(full, 4, out p));
            Assert.False(BracketDiagram.LiveFinisherAtRank(full, 0, out p));
            Race part = new Race { Id = "Y", IsComplete = true, Pilots = new List<string> { "a", "b", "c" }, Winners = new List<string> { "c" }, Losers = new List<string> { "a", "b" } };
            Assert.True(BracketDiagram.LiveFinisherAtRank(part, 1, out p));
            Assert.Equal("c", p);
            Assert.True(BracketDiagram.LiveFinisherAtRank(part, 3, out p));
            Assert.Equal("b", p);
            part.IsComplete = false;
            Assert.False(BracketDiagram.LiveFinisherAtRank(part, 1, out p));
            part.IsComplete = true;
            Dictionary<string, int> ranks = BracketDiagram.LiveFinishRanks(part);
            Assert.Equal(1, ranks["c"]);
            Assert.Equal(2, ranks["a"]);
            Assert.Equal(3, ranks["b"]);
        }

        [Fact]
        public void QualifierProjectionOverlaysLiveResults()
        {
            Qual q = Qual.New(new QualConfig { PilotNames = Names(16) });
            BracketProjection start = BracketDiagram.Qualifier(q);
            Assert.NotEmpty(start.PredictedSlots);
            Race w11 = q.State().Races.First(r => r.Id == "W1-1");
            List<string> order = new List<string>(w11.Pilots);
            order.Reverse();
            q.ApplyPlacement("W1-1", order);
            BracketProjection mid = BracketDiagram.Qualifier(q);
            // The two W1-1 advancers (the bottom two seeds of that heat) are now real
            // pilots in the projection, wherever the engine placed them.
            foreach (string winner in order.Take(2))
            {
                Assert.Contains(mid.Projected.State().Races, r => r.Round > 1 && Go.Iter(r.Pilots).Contains(winner));
            }
            Assert.True(mid.PredictedSlots.Values.Sum(d => d.Count) < start.PredictedSlots.Values.Sum(d => d.Count));
        }

        [Fact]
        public void MainsProgressionProjectsBuiltMainsOnly()
        {
            Mains m = Mains.New(new MainsProgressionConfig { PilotNames = Names(37), PilotsPerMain = 16, BumpUp = 4 });
            List<(string Label, BracketProjection P)> got = BracketDiagram.MainsProgression(m);
            List<string> built = m.MainViews().Where(v => v.Built && v.Qual != null).Select(v => v.Label).ToList();
            Assert.Equal(built, got.Select(x => x.Label).ToList());
            Assert.NotEmpty(got);
            foreach ((string label, BracketProjection p) in got)
            {
                Assert.DoesNotContain(p.Projected.State().Races, r => r.Id.Contains(":"));
            }
        }
    }
}
