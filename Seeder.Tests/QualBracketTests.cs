using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SeederEngine.Tests
{
    // Port of internal/platform/qualbracket/qualbracket_test.go.
    public class QualBracketTests
    {
        // SrcKey renders a SlotRef as "POS@FROM" (or "seedN") for compact assertions.
        private static string SrcKey(SlotRef r)
        {
            if (string.IsNullOrEmpty(r.FromId))
            {
                return "seed" + r.Position;
            }
            return r.Position + "@" + r.FromId;
        }

        private static List<string> SrcKeys(List<SlotRef> refs)
        {
            List<string> output = new List<string>();
            foreach (SlotRef r in refs ?? new List<SlotRef>())
            {
                output.Add(SrcKey(r));
            }
            return output;
        }

        private static bool EqualStrs(List<string> a, List<string> b)
        {
            return Go.SameSlice(a, b);
        }

        private static string Show(IEnumerable<string> s)
        {
            return "[" + string.Join(" ", s ?? new string[0]) + "]";
        }

        private static List<string> SeedNames(int n)
        {
            return QualSpec.SeedNames(n);
        }

        private static Dictionary<string, SpecRace> ById(Spec spec)
        {
            Dictionary<string, SpecRace> byID = new Dictionary<string, SpecRace>();
            foreach (SpecRace r in spec.Body)
            {
                byID[r.Id] = r;
            }
            return byID;
        }

        // TestSpec16ExactGraph asserts the 16-pilot spec matches MultiGP's published
        // "Top 4 advance" double-elimination sheet (the supplied photo) verbatim.
        [Fact]
        public void TestSpec16ExactGraph()
        {
            Spec spec = QualSpec.SpecFor16(false, false);
            Dictionary<string, SpecRace> byID = ById(spec);

            Dictionary<string, List<string>> want = new Dictionary<string, List<string>>
            {
                // Winners Round 1 — snake seeding ordered by top seed 1,4,2,3 so seeds 1
                // and 2 sit on opposite halves and only meet in the FINAL.
                { "W1-1", new List<string> { "seed1", "seed8", "seed9", "seed16" } },
                { "W1-2", new List<string> { "seed4", "seed5", "seed12", "seed13" } },
                { "W1-3", new List<string> { "seed2", "seed7", "seed10", "seed15" } },
                { "W1-4", new List<string> { "seed3", "seed6", "seed11", "seed14" } },
                // Redemption Round 2.
                { "L1-1", new List<string> { "4@W1-1", "3@W1-2", "3@W1-3", "4@W1-4" } },
                { "L1-2", new List<string> { "3@W1-1", "4@W1-2", "4@W1-3", "3@W1-4" } },
                // Winners Round 3.
                { "W2-1", new List<string> { "1@W1-1", "2@W1-1", "1@W1-2", "2@W1-2" } },
                { "W2-2", new List<string> { "1@W1-3", "2@W1-3", "1@W1-4", "2@W1-4" } },
                // Redemption Round 4.
                { "L2-1", new List<string> { "3@W2-1", "1@L1-1", "2@L1-1", "4@W2-2" } },
                { "L2-2", new List<string> { "4@W2-1", "2@L1-2", "1@L1-2", "3@W2-2" } },
                // Winners Round 5 (winners final).
                { "W3-1", new List<string> { "1@W2-1", "2@W2-1", "1@W2-2", "2@W2-2" } },
                // Redemption Round 6.
                { "L3-1", new List<string> { "1@L2-1", "2@L2-1", "1@L2-2", "2@L2-2" } },
                // Redemption Round 7 (redemption final).
                { "L4-1", new List<string> { "3@W3-1", "4@W3-1", "1@L3-1", "2@L3-1" } },
            };
            foreach (KeyValuePair<string, List<string>> kv in want)
            {
                Assert.True(byID.ContainsKey(kv.Key), "race " + kv.Key + " missing from spec");
                List<string> got = SrcKeys(byID[kv.Key].Sources);
                Assert.True(EqualStrs(got, kv.Value), "race " + kv.Key + " sources = " + Show(got) + ", want " + Show(kv.Value));
            }
            // Terminal final.
            Assert.True(spec.WinnersFinalId == "W3-1" && spec.LosersFinalId == "L4-1",
                "finals = " + spec.WinnersFinalId + "/" + spec.LosersFinalId + ", want W3-1/L4-1");
            List<string> fin = SrcKeys(spec.Finalists);
            Assert.True(EqualStrs(fin, new List<string> { "1@W3-1", "2@W3-1", "1@L4-1", "2@L4-1" }), "finalists = " + Show(fin));
        }

        // TestSnakeSeparation16 plays 16 seeds-win and asserts the top two seeds do not
        // share a heat before the winners final (W3-1, round 5). Snake seeding puts them
        // on opposite halves of the winners bracket, so the earliest they can meet is the
        // winners final (which gathers the top-4 winners-side pilots) — never round 3.
        [Fact]
        public void TestSnakeSeparation16()
        {
            Qual q = Qual.New(new QualConfig { PilotNames = SeedNames(16) });
            PlaySeedsWin(q);
            foreach (Race r in q.state.Races)
            {
                bool has1 = false, has2 = false;
                foreach (string p in r.Pilots ?? new List<string>())
                {
                    if (p == "1")
                    {
                        has1 = true;
                    }
                    if (p == "2")
                    {
                        has2 = true;
                    }
                }
                Assert.False(has1 && has2 && r.Round < 5,
                    "seeds 1 and 2 meet too early in race " + r.Id + " (round " + r.Round + "): " + Show(r.Pilots));
            }
        }

        // TestPhotoSeed16 asserts the photo-layout option reproduces the photo's exact
        // round-1 slot placement (ranks mapped to seeds 1..16) and still converges.
        [Fact]
        public void TestPhotoSeed16()
        {
            Spec spec = QualSpec.SpecFor16(false, true);
            Dictionary<string, SpecRace> byID = ById(spec);
            Dictionary<string, List<string>> want = new Dictionary<string, List<string>>
            {
                { "W1-1", new List<string> { "seed5", "seed12", "seed6", "seed8" } },
                { "W1-2", new List<string> { "seed10", "seed11", "seed15", "seed7" } },
                { "W1-3", new List<string> { "seed2", "seed13", "seed14", "seed3" } },
                { "W1-4", new List<string> { "seed9", "seed1", "seed4", "seed16" } },
            };
            foreach (KeyValuePair<string, List<string>> kv in want)
            {
                List<string> got = SrcKeys(byID[kv.Key].Sources);
                Assert.True(EqualStrs(got, kv.Value), "photo " + kv.Key + " = " + Show(got) + ", want " + Show(kv.Value));
            }
            // The redemption (losers) flow must match the photo regardless of the
            // round-1 seeding choice — it is wired by race id/position, not by seed.
            Dictionary<string, List<string>> redemption = new Dictionary<string, List<string>>
            {
                { "L1-1", new List<string> { "4@W1-1", "3@W1-2", "3@W1-3", "4@W1-4" } },
                { "L1-2", new List<string> { "3@W1-1", "4@W1-2", "4@W1-3", "3@W1-4" } },
                { "L2-1", new List<string> { "3@W2-1", "1@L1-1", "2@L1-1", "4@W2-2" } },
                { "L2-2", new List<string> { "4@W2-1", "2@L1-2", "1@L1-2", "3@W2-2" } },
                { "L3-1", new List<string> { "1@L2-1", "2@L2-1", "1@L2-2", "2@L2-2" } },
                { "L4-1", new List<string> { "3@W3-1", "4@W3-1", "1@L3-1", "2@L3-1" } },
            };
            foreach (KeyValuePair<string, List<string>> kv in redemption)
            {
                List<string> got = SrcKeys(byID[kv.Key].Sources);
                Assert.True(EqualStrs(got, kv.Value), "photo redemption " + kv.Key + " = " + Show(got) + ", want " + Show(kv.Value) + " (must match the photo)");
            }

            Qual q = Qual.New(new QualConfig { PilotNames = SeedNames(16), PhotoSeed = true });
            PlaySeedsWin(q);
            List<string> qual = q.Qualified();
            Assert.True(Go.Len(qual) == 4, "photo qualified = " + Show(qual) + ", want 4");
        }

        // TestQual16Materialises checks the interpreter builds a clean state for 16 and
        // the FINAL heat carries the top-4 target.
        [Fact]
        public void TestQual16Materialises()
        {
            Qual q = Qual.New(new QualConfig { PilotNames = SeedNames(16) });
            Race final = q.FindRace("FINAL");
            Assert.True(final != null, "no FINAL race");
            Assert.True(final.FinalTarget == 4, "FINAL.FinalTarget = " + final.FinalTarget + ", want 4");
            // Round-1 winners races are seeded; downstream races empty.
            Race w11 = q.FindRace("W1-1");
            Assert.True(w11 != null && Go.Len(w11.Pilots) == 4, "W1-1 pilots wrong");
            Race w31 = q.FindRace("W3-1");
            Assert.True(w31 != null && Go.Len(w31.Pilots) == 0, "W3-1 should start empty");
        }

        // TestSimulate16TopFour plays 16 to completion seeds-win and asserts the top
        // seed qualifies first and exactly 4 pilots qualify.
        [Fact]
        public void TestSimulate16TopFour()
        {
            Qual q = Qual.New(new QualConfig { PilotNames = SeedNames(16) });
            PlaySeedsWin(q);
            Assert.True(q.IsComplete(), "bracket did not complete");
            List<string> qual = q.Qualified();
            Assert.True(Go.Len(qual) == 4, "qualified = " + Show(qual) + ", want 4");
            Assert.True(qual[0] == "1", "top qualifier = " + qual[0] + ", want seed 1");
        }

        // TestCTA16 runs 16 with chase-the-ace and asserts a champion emerges from the 4.
        [Fact]
        public void TestCTA16()
        {
            Qual q = Qual.New(new QualConfig { PilotNames = SeedNames(16), CTA = true });
            Assert.True(q.FindRace("FINAL") == null, "CTA bracket should have no FINAL race");
            Assert.True(q.FindRace("CTA-1") != null, "CTA bracket should pre-create CTA heats");
            PlaySeedsWin(q);
            Assert.True(q.IsComplete(), "CTA did not complete");
            List<string> got = q.Qualified();
            Assert.True(Go.Len(got) == 4 && got[0] == "1", "CTA qualified = " + Show(got));
        }

        // TestPlayOutAllSizes plays every field size 2..96 to completion and checks the
        // spec is well-formed: no dangling source refs, the bracket completes, and
        // exactly min(4,N) pilots qualify.
        [Fact]
        public void TestPlayOutAllSizes()
        {
            for (int n = 2; n <= 96; n++)
            {
                Qual q = Qual.New(new QualConfig { PilotNames = SeedNames(n) });
                AssertNoDanglingRefs(q, n);
                PlaySeedsWin(q);
                Assert.True(q.IsComplete(), "n=" + n + ": did not complete");
                int want = Go.MinInt(4, n);
                List<string> got = q.Qualified();
                Assert.True(Go.Len(got) == want, "n=" + n + ": qualified " + Show(got) + ", want " + want);
            }
        }

        // AssertNoDanglingRefs checks every source ref points at a race that can produce
        // at least that finishing position.
        private static void AssertNoDanglingRefs(Qual q, int n)
        {
            // Round-1 race sizes come from the materialised pilots; others from advance
            // math are validated structurally by playout, so only check Round-1 + that
            // every FromID exists.
            foreach (KeyValuePair<string, List<SlotRef>> kv in q.sources)
            {
                foreach (SlotRef rf in kv.Value ?? new List<SlotRef>())
                {
                    if (string.IsNullOrEmpty(rf.FromId))
                    {
                        Assert.False(rf.Position < 1 || rf.Position > n, "n=" + n + " race " + kv.Key + ": seed ref " + rf.Position + " out of range");
                        continue;
                    }
                    Assert.True(q.FindRace(rf.FromId) != null, "n=" + n + " race " + kv.Key + ": source " + rf.FromId + " does not exist");
                }
            }
        }

        // PlaySeedsWin drives a bracket to completion, best seed winning each heat.
        private static void PlaySeedsWin(Qual q)
        {
            Play(q, false, false, "playout did not converge");
        }

        // PlayWorstWin drives a bracket to completion with the WORST seed winning each
        // heat — the inverse of PlaySeedsWin. This forces upsets so the elimination
        // depth order diverges from the seed order, exercising Ranking().
        private static void PlayWorstWin(Qual q)
        {
            Play(q, true, false, "worst-win playout did not converge");
        }

        // PlayBodySeedsWin runs every non-CTA (body) race in round order, best seed
        // winning, so the chase-the-ace finalists resolve but no CTA heat is flown yet.
        private static void PlayBodySeedsWin(Qual q)
        {
            Play(q, false, true, "body playout did not converge");
        }

        private static void Play(Qual q, bool worst, bool bodyOnly, string failMsg)
        {
            for (int i = 0; i < 5000; i++)
            {
                Race pick = null;
                foreach (Race r in q.state.Races)
                {
                    if (r.IsComplete || r.IsVoid || Go.Len(r.Pilots) == 0)
                    {
                        continue;
                    }
                    if (bodyOnly && r.Bracket == BracketSlot.ChaseTheAce)
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
                if (worst)
                {
                    Go.SortStable(order, (a, b) => q.SeedOf(a) > q.SeedOf(b));
                }
                else
                {
                    Go.SortStable(order, (a, b) => q.SeedOf(a) < q.SeedOf(b));
                }
                try
                {
                    q.ApplyPlacement(pick.Id, order);
                }
                catch (BracketException e)
                {
                    Assert.Fail("apply " + pick.Id + ": " + e.Message);
                }
            }
            Assert.Fail(failMsg);
        }

        private static bool ContainsStr(List<string> s, string v)
        {
            return s != null && s.Contains(v);
        }

        // TestRankingByProgressionNotSeed verifies Ranking() orders the non-qualified
        // pilots by how far they progressed (deepest elimination first), not by seed.
        // With worst-seed-wins the deepest survivors are the low seeds, so a plain seed
        // sort would be visibly wrong.
        [Fact]
        public void TestRankingByProgressionNotSeed()
        {
            Qual q = Qual.New(new QualConfig { PilotNames = SeedNames(16) });
            PlayWorstWin(q);
            Assert.True(q.IsComplete(), "bracket did not complete");

            List<string> rank = q.Ranking();
            Assert.True(rank.Count == 16, "ranking has " + rank.Count + " pilots, want 16");

            // Deepest completed heat each pilot reached (independent recompute).
            Dictionary<string, int> last = new Dictionary<string, int>();
            foreach (Race r in q.state.Races)
            {
                if (!r.IsComplete)
                {
                    continue;
                }
                foreach (string p in r.Pilots ?? new List<string>())
                {
                    int cur;
                    last.TryGetValue(p, out cur);
                    if (r.Round > cur)
                    {
                        last[p] = r.Round;
                    }
                }
            }
            System.Func<string, int> lastOf = p =>
            {
                int v;
                last.TryGetValue(p, out v);
                return v;
            };

            List<string> qualified = q.Qualified();
            Assert.True(Go.Len(qualified) == 4, "qualified = " + Show(qualified) + ", want 4");
            // The qualified finalists lead the ranking, in their final placing.
            for (int i = 0; i < qualified.Count; i++)
            {
                Assert.True(rank[i] == qualified[i], "rank " + (i + 1) + " = " + rank[i] + ", want qualified " + qualified[i]);
            }

            // The non-qualified tail must be non-increasing in elimination depth.
            List<string> tail = rank.GetRange(qualified.Count, rank.Count - qualified.Count);
            for (int i = 1; i < tail.Count; i++)
            {
                Assert.False(lastOf(tail[i - 1]) < lastOf(tail[i]),
                    "tail not depth-ordered: " + tail[i - 1] + " (round " + lastOf(tail[i - 1]) + ") ranked above " + tail[i] + " (round " + lastOf(tail[i]) + ")");
            }

            // And it must NOT be a plain seed sort (the old behaviour).
            List<string> bySeed = new List<string>(tail);
            Go.SortStable(bySeed, (a, b) => q.SeedOf(a) < q.SeedOf(b));
            Assert.False(Go.SameSlice(tail, bySeed), "non-qualified tail is plain seed order, want progression order: " + Show(tail));
        }

        // TestCTAFeederEditableUntilSeriesStarts is the regression guard for the
        // premature edit-lock: the winners/redemption finals that feed the chase-the-ace
        // final must stay editable until a CTA heat is actually run, and correcting a
        // feeder before then must update who makes the final. Previously the finalist
        // set locked the instant both feeders completed (propagate resolves it
        // automatically), freezing a just-entered redemption-final result.
        [Fact]
        public void TestCTAFeederEditableUntilSeriesStarts()
        {
            Qual q = Qual.New(new QualConfig { PilotNames = SeedNames(16), CTA = true });
            PlayBodySeedsWin(q);

            ChaseTheAceState cta = q.state.ChaseTheAce;
            Assert.True(cta != null && Go.Len(cta.Finalists) == 4, "finalists not resolved after the feeders ran");
            // The redemption final L4-1 feeds only the (un-run) CTA final, so it must
            // still be editable — this is the exact heat that was wrongly frozen.
            // (W3-1, the winners final, legitimately has a run downstream: L4-1 takes its
            // 3rd/4th, so it stays blocked by the ordinary downstream guard.)
            string reason;
            Assert.True(q.CanEditRace("L4-1", out reason), "redemption final L4-1 not editable before CTA started: " + reason);

            // Promote L4-1's 3rd-place pilot into an advancing slot — this changes WHO
            // makes the final, which the finalist set must follow.
            Race l4 = q.FindRace("L4-1");
            Assert.True(Go.Len(l4.Pilots) >= 3, "L4-1 has too few pilots: " + Show(l4.Pilots));
            string promoted = l4.Pilots[2];
            Assert.False(ContainsStr(cta.Finalists, promoted), "test precondition: " + promoted + " already a finalist");
            List<string> newOrder = new List<string> { l4.Pilots[2], l4.Pilots[0], l4.Pilots[1], l4.Pilots[3] };
            q.EditRace("L4-1", newOrder);
            Assert.True(ContainsStr(q.state.ChaseTheAce.Finalists, promoted),
                "finalists did not follow the corrected L4-1: " + Show(q.state.ChaseTheAce.Finalists) + " (want " + promoted + " in)");

            // Run the first CTA heat — now the feeders must freeze.
            Race next = q.NextCTARace();
            Assert.True(next != null, "no CTA heat available to run");
            q.ApplyPlacement(next.Id, new List<string>(next.Pilots));
            Assert.False(q.CanEditRace("L4-1", out reason), "L4-1 must be frozen once a CTA heat has been run");
        }

        // SpecGraph flattens a spec's body into id → SORTED source keys ("pos@race") so
        // a shape can be pinned by composition. Order is deliberately not pinned here:
        // the generator lists advancers position-major (every 1st before every 2nd) and
        // the sheet lists them race-major, which is a seat order, not a structure.
        private static Dictionary<string, List<string>> SpecGraph(Spec sp)
        {
            Dictionary<string, List<string>> output = new Dictionary<string, List<string>>();
            foreach (SpecRace sr in sp.Body)
            {
                List<string> keys = SrcKeys(sr.Sources);
                keys.Sort(string.CompareOrdinal);
                output[sr.Id] = keys;
            }
            return output;
        }

        private static List<string> Get(Dictionary<string, List<string>> g, string id)
        {
            List<string> v;
            g.TryGetValue(id, out v);
            return v;
        }

        // The generic generator must run the real double-elimination cadence: after the
        // first two intakes the redemption side alternates a MINOR round (halve the pool,
        // nobody new enters) with a MAJOR round that folds in the next winners round's
        // drops. It used to fold every round's drops straight in, so the 13-pilot C Main
        // of a mains-progression bracket got an L3-1 AND an L3-2 (R6-1/R6-2 on screen)
        // where the sheet has one heat — reported straight off the runner: "R6-1 and R6-2
        // should just be a single race of R6-1 featuring 1st and 2nd placings from R4-1
        // and R4-2 … 1st and 2nd of that race progress to R8-1 against 3rd and 4th
        // placings from W5-1."
        [Fact]
        public void TestGeneric13RedemptionHalvesBeforeTheWinnersFinalDrops()
        {
            Dictionary<string, List<string>> g = SpecGraph(QualSpec.GenericSpec(13, false, 4, 2));
            Assert.False(g.ContainsKey("L3-2"), "L3-2 exists — the halving round ran as two heats: " + Show(Get(g, "L3-2")));
            Dictionary<string, List<string>> want = new Dictionary<string, List<string>>
            {
                { "L3-1", new List<string> { "1@L2-1", "2@L2-1", "1@L2-2", "2@L2-2" } }, // the minor round: L2's top-2s only
                { "L4-1", new List<string> { "3@W3-1", "4@W3-1", "1@L3-1", "2@L3-1" } }, // redemption final vs the winners-final droppers
            };
            foreach (KeyValuePair<string, List<string>> kv in want)
            {
                kv.Value.Sort(string.CompareOrdinal);
                Assert.True(EqualStrs(Get(g, kv.Key), kv.Value), kv.Key + " sources = " + Show(Get(g, kv.Key)) + ", want " + Show(kv.Value));
            }
        }

        // With the cadence right, the generic generator reproduces the 16-pilot sheet's
        // redemption structure on its own (SpecFor16 stays the transcription of record,
        // but the generator and the sheet must not disagree about the shape).
        [Fact]
        public void TestGeneric16MatchesTheSheetRedemption()
        {
            Dictionary<string, List<string>> gen = SpecGraph(QualSpec.GenericSpec(16, false, 4, 2));
            Dictionary<string, List<string>> sheet = SpecGraph(QualSpec.SpecFor16(false, false));
            foreach (string id in new[] { "L3-1", "L4-1" })
            {
                Assert.True(EqualStrs(Get(gen, id), Get(sheet, id)), id + ": generic " + Show(Get(gen, id)) + ", sheet " + Show(Get(sheet, id)));
            }
            foreach (string id in gen.Keys)
            {
                Assert.True(sheet.ContainsKey(id), "generic emits " + id + ", which the sheet does not have");
            }
            Assert.True(Get(gen, "L3-1") != null && Get(sheet, "L3-1") != null, "expected an L3-1 in both");
        }

        // Across every field size a MAJOR redemption round must never be preceded by a
        // round whose survivors alone would overfill it — i.e. the halving happened. Put
        // concretely: no redemption round may contain BOTH a winners-round dropper and
        // a pilot from a redemption round two steps back (that is what skipping the
        // minor round produces).
        [Fact]
        public void TestGenericRedemptionAlternatesMinorAndMajor()
        {
            for (int n = 9; n <= 96; n++)
            {
                Spec sp = QualSpec.GenericSpec(n, false, 4, 2);
                Dictionary<string, int> lround = new Dictionary<string, int>();
                foreach (SpecRace sr in sp.Body)
                {
                    if (sr.Slot == BracketSlot.Losers)
                    {
                        lround[sr.Id] = sr.Round;
                    }
                }
                foreach (SpecRace sr in sp.Body)
                {
                    if (sr.Slot != BracketSlot.Losers)
                    {
                        continue;
                    }
                    bool hasDrop = false, hasStale = false;
                    foreach (SlotRef rf in sr.Sources)
                    {
                        int fr;
                        bool isL = lround.TryGetValue(rf.FromId ?? "", out fr);
                        if (!isL)
                        {
                            hasDrop = true;
                        }
                        else if (sr.Round - fr > 2)
                        {
                            hasStale = true;
                        }
                    }
                    Assert.False(hasDrop && hasStale,
                        "n=" + n + ": " + sr.Id + " mixes winners droppers with survivors from two rounds back: " + Show(SrcKeys(sr.Sources)));
                }
            }
        }

        // TestSixUpTopSix pins the configurable heat shape: a 48-pilot 6-up / top-3
        // bracket (the Nationals ask) runs every round 6-up, ends in a 6-pilot final
        // fed 3 + 3 from the two sides, and qualifies 6 — and it plays out to
        // completion under seeds-win. 4/2 is unchanged: the 16 field still gets the
        // MultiGP sheet exactly.
        [Fact]
        public void TestSixUpTopSix()
        {
            List<string> names = SeedNames(48);
            Qual q = Qual.New(new QualConfig { PilotNames = names, PilotsPerRace = 6, Advance = 3 });
            Dictionary<int, int> sizes = new Dictionary<int, int>();
            foreach (SpecRace sr in q.Spec().Body)
            {
                if (sr.Round == 1)
                {
                    int c;
                    sizes.TryGetValue(sr.Sources.Count, out c);
                    sizes[sr.Sources.Count] = c + 1;
                }
            }
            Assert.True(sizes.ContainsKey(6) && sizes[6] == 8 && sizes.Count == 1, "round 1 must be 8 heats of 6");
            foreach (SpecRace sr in q.Spec().Body)
            {
                Assert.False(sr.Sources.Count > 6, sr.Id + " has " + sr.Sources.Count + " pilots — more than 6-up");
                Assert.False(!sr.Terminal && sr.Advance != 3 && sr.Sources.Count > 3, sr.Id + " advances " + sr.Advance + ", want 3");
            }
            Assert.True(Go.Len(q.Spec().Finalists) == 6, "terminal final must take 3 + 3 = 6 finalists, got " + Go.Len(q.Spec().Finalists));
            Assert.True(q.Config().PilotsPerRace == 6 && q.Config().PilotsToAdvance == 3, "bracket.Config view must carry the shape");
            BracketState st = q.Simulate();
            Qual sim = Qual.FromState(new QualConfig { PilotNames = names, PilotsPerRace = 6, Advance = 3 }, st);
            Assert.True(sim.IsComplete() && Go.Len(sim.Qualified()) == 6,
                "seeds-win play-out: complete=" + sim.IsComplete() + " qualified=" + Show(sim.Qualified()));
            // CTA: worst case (wins−1)×6 + 1 heats pre-created.
            Qual qc = Qual.New(new QualConfig { PilotNames = names, PilotsPerRace = 6, Advance = 3, CTA = true, AceWinsRequired = 2 });
            int cta = qc.State().Races.Count(r => r.Bracket == BracketSlot.ChaseTheAce);
            Assert.True(cta == 7, "6 finalists at 2 wins → 7 CTA heats, got " + cta);
            // The default shape is untouched.
            Qual q16 = Qual.New(new QualConfig { PilotNames = SeedNames(16) });
            Assert.True(q16.Config().PilotsPerRace == 4 && Go.Len(q16.Spec().Finalists) == 4 && q16.Spec().WinnersFinalId == "W3-1",
                "16-pilot default must still be the MultiGP sheet");
        }

        // TestRehydrateAcrossShapeChange: a state built 4-up under a row that now
        // says 6/3 (the columns were stored but ignored before the shape was
        // configurable) regenerates 6-up while un-run, and keeps its 4-up state and
        // shape once results are in.
        [Fact]
        public void TestRehydrateAcrossShapeChange()
        {
            List<string> names = SeedNames(24);
            Qual old = Qual.New(new QualConfig { PilotNames = names });
            Qual fresh = Qual.FromState(new QualConfig { PilotNames = names, PilotsPerRace = 6, Advance = 3 }, old.State());
            Assert.True(fresh.Config().PilotsPerRace == 6 && Go.Len(fresh.Spec().Finalists) == 6,
                "un-run state must regenerate under the requested shape");
            // Run one heat 4-up, then rehydrate as 6/3: the results must survive.
            Qual played = Qual.New(new QualConfig { PilotNames = names });
            Race r1 = played.State().Races[0];
            played.ApplyPlacement(r1.Id, new List<string>(r1.Pilots));
            Qual kept = Qual.FromState(new QualConfig { PilotNames = names, PilotsPerRace = 6, Advance = 3 }, played.State());
            Assert.True(kept.Config().PilotsPerRace == 4 && kept.State().Races[0].IsComplete,
                "a raced 4-up state must stay 4-up with its result: ppr=" + kept.Config().PilotsPerRace + " complete=" + kept.State().Races[0].IsComplete);
        }

        // TestSeatsPadToCapacity is the race manager's ask: a 40-pilot B Main in a
        // 48-per-main competition must run the SAME bracket as a full 48 — 8 heats of
        // 6 seats with the missing bottom seeds left empty (5 pilots each) — not 7
        // ragged heats. It plays out to a 6-pilot final and qualifies 6.
        [Fact]
        public void TestSeatsPadToCapacity()
        {
            List<string> names = SeedNames(40);
            Qual full = Qual.New(new QualConfig { PilotNames = SeedNames(48), PilotsPerRace = 6, Advance = 3 });
            Qual q = Qual.New(new QualConfig { PilotNames = names, PilotsPerRace = 6, Advance = 3, Seats = 48 });
            Assert.True(q.Spec().Body.Count == full.Spec().Body.Count,
                "padded bracket must have the full field's heats: " + q.Spec().Body.Count + " vs " + full.Spec().Body.Count);
            for (int i = 0; i < q.Spec().Body.Count; i++)
            {
                Assert.True(q.Spec().Body[i].Id == full.Spec().Body[i].Id, "heat " + i + ": " + q.Spec().Body[i].Id + " vs " + full.Spec().Body[i].Id);
            }
            int r1 = 0;
            foreach (Race r in q.State().Races)
            {
                if (r.Round == 1)
                {
                    r1++;
                    Assert.True(Go.Len(r.Pilots) == 5, r.Id + " should have 5 pilots (one empty seat), got " + Go.Len(r.Pilots));
                }
            }
            Assert.True(r1 == 8, "want 8 round-1 heats, got " + r1);
            Assert.True(q.Config().PilotCount == 40, "PilotCount stays the roster: " + q.Config().PilotCount);
            Qual sim = Qual.FromState(new QualConfig { PilotNames = names, PilotsPerRace = 6, Advance = 3, Seats = 48 }, q.Simulate());
            Assert.True(sim.IsComplete() && Go.Len(sim.Qualified()) == 6, "play-out: complete=" + sim.IsComplete() + " qualified=" + Go.Len(sim.Qualified()));
            // Seats below the roster are ignored; a roster-sized bracket is unchanged.
            Qual plain = Qual.New(new QualConfig { PilotNames = names, PilotsPerRace = 6, Advance = 3, Seats = 10 });
            Assert.False(plain.Config().PilotCount != 40 || plain.Spec().Body.Count == full.Spec().Body.Count,
                "Seats below the roster must fall back to the roster size");
        }

        // TestEmptySeatsVoidAndFlow: a field far short of its seats leaves whole
        // heats empty; those void themselves so the heats they feed don't wait, and
        // the bracket still completes.
        [Fact]
        public void TestEmptySeatsVoidAndFlow()
        {
            List<string> names = SeedNames(4);
            Qual q = Qual.New(new QualConfig { PilotNames = names, PilotsPerRace = 6, Advance = 3, Seats = 48 });
            int voids = 0;
            foreach (Race r in q.State().Races)
            {
                if (r.Round == 1 && Go.Len(r.Pilots) == 0)
                {
                    Assert.True(r.IsVoid, r.Id + " is empty but not void");
                    voids++;
                }
            }
            Assert.True(voids == 4, "4 pilots snake into 4 of 8 heats; want 4 void heats, got " + voids);
            Qual sim = Qual.FromState(new QualConfig { PilotNames = names, PilotsPerRace = 6, Advance = 3, Seats = 48 }, q.Simulate());
            Assert.True(sim.IsComplete() && Go.Len(sim.Qualified()) == 4, "complete=" + sim.IsComplete() + " qualified=" + Show(sim.Qualified()));
            Assert.False(Qual.StateHasResults(q.State()), "void heats must not read as results");
        }

        // TestCTAPointsTieGoesToHigherSeed replays the 2026 Nationals Pro Spec A Main:
        // heepsy (seed 6, via the winners final) and Davey FPV (seed 1, via
        // redemption) tied on 7 points. Finalist order lists the winners side first,
        // so the old stable sort gave heepsy 3rd; a points tie must go to the seed.
        [Fact]
        public void TestCTAPointsTieGoesToHigherSeed()
        {
            Qual q = new Qual
            {
                seed = new Dictionary<string, int> { { "Davey FPV", 0 }, { "Screecher", 1 }, { "IQ0", 2 }, { "heepsy", 5 } },
                state = new BracketState
                {
                    ChaseTheAce = new ChaseTheAceState
                    {
                        Finalists = new List<string> { "IQ0", "heepsy", "Screecher", "Davey FPV" },
                        Points = new GoMap<int> { { "Screecher", 2 }, { "IQ0", 4 }, { "heepsy", 7 }, { "Davey FPV", 7 } },
                        Champion = "Screecher",
                    },
                },
            };
            q.ComputeCTAStandings();
            List<string> want = new List<string> { "Screecher", "IQ0", "Davey FPV", "heepsy" };
            List<string> got = q.state.ChaseTheAce.Standings;
            Assert.True(Show(got) == Show(want), "standings = " + Show(got) + ", want " + Show(want));
        }
    }
}
