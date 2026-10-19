using System.Collections.Generic;

namespace SeederEngine
{
    // Port of internal/platform/qualbracket (spec.go).
    //
    // The "Double Elimination — Top 4 Advance" qualifier race format: a MultiGP-style
    // double-elimination bracket that does NOT crown a single champion but ends in a
    // single FINAL of the top 4 finalists (top-2 from the winners-bracket final + top-2
    // from the redemption final) and outputs a ranked "Qualified" top-4 that continue
    // into a later bracket. The final may optionally be run as a chase-the-ace
    // mini-series among the four.
    //
    // The bracket is described declaratively as a Spec: every race lists the exact
    // source slots (a finishing position of a prior race) that feed it. The 16-pilot
    // case is a literal transcription of MultiGP's published bracket sheet so it
    // reproduces it exactly; other field sizes use a generic double-elimination
    // generator. The interpreter (Qual) materialises a BracketState from the spec,
    // reusing the bracket Race/State/slot types so the existing renderer, runner grid,
    // DroneDash ingest and finalise flow all work unchanged.

    /// <summary>
    /// SlotRef points at a finishing position of a prior race: "take the Nth-place
    /// finisher of race FromId". Position is 1-based (1 = winner). When FromId is
    /// empty the ref is a Round-1 seed entry and Position is the absolute seed
    /// (1 = top qualifier).
    /// </summary>
    public struct SlotRef
    {
        public string FromId { get; set; }
        public int Position { get; set; }

        public SlotRef(string fromId, int position)
        {
            FromId = fromId ?? "";
            Position = position;
        }

        public override string ToString()
        {
            return "{" + (FromId ?? "") + " " + Go.Itoa(Position) + "}";
        }
    }

    /// <summary>SpecRace declares one heat structurally — where each of its pilots comes from.</summary>
    public class SpecRace
    {
        public string Id { get; set; } = "";
        public int Round { get; set; }
        public string Slot { get; set; } = "";
        public int Advance { get; set; }        // how many advance out of this race
        public List<SlotRef> Sources { get; set; } // ordered: Sources[i] fills pilot slot i
        // Terminal marks the FINAL heat: its pilots qualify out rather than advancing
        // into another race. FinalTarget mirrors Race.FinalTarget so the
        // engine-driven renderer reports the right expected-winner count.
        public bool Terminal { get; set; }
        public int FinalTarget { get; set; }
    }

    /// <summary>
    /// Spec is a fully-wired qualifier bracket: every body race's inputs are explicit
    /// SlotRefs, plus the two finals whose top-2 each feed the terminal final.
    /// </summary>
    public class Spec
    {
        public List<SpecRace> Body { get; set; }      // winners + redemption body races
        public string WinnersFinalId { get; set; } = ""; // race whose top-2 are winners-side finalists
        public string LosersFinalId { get; set; } = "";  // race whose top-2 are losers-side finalists ("" for tiny fields)
        public List<SlotRef> Finalists { get; set; }  // the (up to 4) refs feeding the terminal final
        public int FinalRound { get; set; }           // round/column for the terminal final
        public bool CTA { get; set; }                 // run the final as a chase-the-ace series
    }

    /// <summary>Spec builders for the qualifier (spec.go).</summary>
    public static class QualSpec
    {
        // DefaultPilotsPerRace / DefaultAdvance are the format's MultiGP standard
        // (4-up, top-2 advance) — the shape every bracket had before the size
        // became configurable, and what a zero Config still gets.
        public const int DefaultPilotsPerRace = 4;
        public const int DefaultAdvance = 2;
        internal const int SpecPPR = DefaultPilotsPerRace;
        internal const int SpecAdvance = DefaultAdvance;

        // SeedSources turns a group of seed numbers into Round-1 SlotRefs.
        internal static List<SlotRef> SeedSources(List<int> seeds)
        {
            List<SlotRef> output = new List<SlotRef>();
            foreach (int s in seeds)
            {
                output.Add(new SlotRef("", s));
            }
            return output;
        }

        // PhotoSeed16 reproduces the supplied photo's exact round-1 slot placement,
        // mapping its pilots to ranks 1..16 by their event seed (lowest = best). It is
        // NOT a snake — the photo funnels several top seeds toward the same side — so
        // top seeds can meet earlier than under MultiGP snake seeding. Each inner list
        // is one round-1 heat in its channel order.
        internal static readonly int[][] PhotoSeed16 = new int[][]
        {
            new int[] { 5, 12, 6, 8 },   // R1: event seeds 18,52,19,26
            new int[] { 10, 11, 15, 7 }, // R2: event seeds 35,43,56,24
            new int[] { 2, 13, 14, 3 },  // R3: event seeds 7,53,54,8
            new int[] { 9, 1, 4, 16 },   // R4: event seeds 28,5,16,61
        };

        // SpecFor16 is the MultiGP 16-pilot double-elimination "Top 4 advance" bracket
        // (the supplied photo). The redemption drop composition is transcribed verbatim
        // from the sheet. The Round-1 seeding is selectable:
        //
        //   - snake (default): MultiGP snake seeding, heats ordered by top seed 1,4,2,3
        //     (the standard 4-slot bracket split) so seeds 1 and 2 fall on opposite
        //     halves and only meet in the winners final. Heats:
        //     W1-1=[1,8,9,16] W1-2=[4,5,12,13] W1-3=[2,7,10,15] W1-4=[3,6,11,14].
        //   - photo: the photo's exact slot placement (see PhotoSeed16).
        internal static Spec SpecFor16(bool cta, bool photo)
        {
            List<List<int>> heats = new List<List<int>>();
            if (photo)
            {
                foreach (int[] h in PhotoSeed16)
                {
                    heats.Add(new List<int>(h));
                }
            }
            else
            {
                List<List<string>> g = Bracket.BalancedSeeding(SeedNames(16), 4); // top seeds 1,2,3,4
                // Bracket-half order 1,4,2,3 so seeds 1 and 2 fall on opposite halves
                // (W1-1/W1-2 vs W1-3/W1-4) and only meet in the winners final.
                foreach (int idx in new int[] { 0, 3, 1, 2 })
                {
                    heats.Add(NamesToSeeds(g[idx]));
                }
            }
            SpecRace[] w1 = new SpecRace[4];
            for (int i = 0; i < 4; i++)
            {
                w1[i] = new SpecRace
                {
                    Id = "W1-" + Go.Itoa(i + 1),
                    Round = 1,
                    Slot = BracketSlot.Winners,
                    Advance = 2,
                    Sources = SeedSources(heats[i]),
                };
            }

            List<SpecRace> body = new List<SpecRace>
            {
                w1[0], w1[1], w1[2], w1[3],

                // Redemption Round 2 (fed by Round-1 3rd/4th place finishers).
                new SpecRace { Id = "L1-1", Round = 2, Slot = BracketSlot.Losers, Advance = 2, Sources = Refs(
                    Ref(4, "W1-1"), Ref(3, "W1-2"), Ref(3, "W1-3"), Ref(4, "W1-4")) },
                new SpecRace { Id = "L1-2", Round = 2, Slot = BracketSlot.Losers, Advance = 2, Sources = Refs(
                    Ref(3, "W1-1"), Ref(4, "W1-2"), Ref(4, "W1-3"), Ref(3, "W1-4")) },

                // Winners Round 3.
                new SpecRace { Id = "W2-1", Round = 3, Slot = BracketSlot.Winners, Advance = 2, Sources = Refs(
                    Ref(1, "W1-1"), Ref(2, "W1-1"), Ref(1, "W1-2"), Ref(2, "W1-2")) },
                new SpecRace { Id = "W2-2", Round = 3, Slot = BracketSlot.Winners, Advance = 2, Sources = Refs(
                    Ref(1, "W1-3"), Ref(2, "W1-3"), Ref(1, "W1-4"), Ref(2, "W1-4")) },

                // Redemption Round 4 (winners Round-3 3rd/4th + redemption Round-2 top-2).
                new SpecRace { Id = "L2-1", Round = 4, Slot = BracketSlot.Losers, Advance = 2, Sources = Refs(
                    Ref(3, "W2-1"), Ref(1, "L1-1"), Ref(2, "L1-1"), Ref(4, "W2-2")) },
                new SpecRace { Id = "L2-2", Round = 4, Slot = BracketSlot.Losers, Advance = 2, Sources = Refs(
                    Ref(4, "W2-1"), Ref(2, "L1-2"), Ref(1, "L1-2"), Ref(3, "W2-2")) },

                // Winners Round 5 — winners final, top-2 are winners-side finalists.
                new SpecRace { Id = "W3-1", Round = 5, Slot = BracketSlot.Winners, Advance = 2, Sources = Refs(
                    Ref(1, "W2-1"), Ref(2, "W2-1"), Ref(1, "W2-2"), Ref(2, "W2-2")) },

                // Redemption Round 6.
                new SpecRace { Id = "L3-1", Round = 6, Slot = BracketSlot.Losers, Advance = 2, Sources = Refs(
                    Ref(1, "L2-1"), Ref(2, "L2-1"), Ref(1, "L2-2"), Ref(2, "L2-2")) },

                // Redemption Round 7 — redemption final (winners-final 3rd/4th + Round-6 top-2).
                new SpecRace { Id = "L4-1", Round = 7, Slot = BracketSlot.Losers, Advance = 2, Sources = Refs(
                    Ref(3, "W3-1"), Ref(4, "W3-1"), Ref(1, "L3-1"), Ref(2, "L3-1")) },
            };

            return new Spec
            {
                Body = body,
                WinnersFinalId = "W3-1",
                LosersFinalId = "L4-1",
                Finalists = Refs(Ref(1, "W3-1"), Ref(2, "W3-1"), Ref(1, "L4-1"), Ref(2, "L4-1")),
                FinalRound = 8,
                CTA = cta,
            };
        }

        private static SlotRef Ref(int pos, string from)
        {
            return new SlotRef(from, pos);
        }

        private static List<SlotRef> Refs(params SlotRef[] refs)
        {
            return new List<SlotRef>(refs);
        }

        // BuildSpec selects the spec for a field. A 16-pilot 4-up/top-2 field is pinned
        // to the MultiGP sheet (with selectable snake/photo round-1 seeding); everything
        // else — every other size, and ANY other heat shape (a 6-up Nationals bracket)
        // — uses the generic generator (always snake-separated).
        public static Spec BuildSpec(int n, bool cta, bool photoSeed, int ppr, int adv)
        {
            if (n == 16 && ppr == SpecPPR && adv == SpecAdvance)
            {
                return SpecFor16(cta, photoSeed);
            }
            return GenericSpec(n, cta, ppr, adv);
        }

        // GenericSpec builds a generic double-elimination → top-N qualifier for an
        // arbitrary field size at ppr pilots per race with adv advancing (4/2 is the
        // format's MultiGP standard; a 6-up Nationals bracket runs 6/3). It is not
        // guaranteed to match MultiGP's published sheets for non-16 sizes, but always
        // converges: a winners final and (when the field is big enough) a redemption
        // final feed a single terminal final of up to ppr pilots — adv from each side —
        // whose top-min(ppr,N) qualify.
        internal static Spec GenericSpec(int n, bool cta, int ppr, int adv)
        {
            NormaliseShape(ref ppr, ref adv);
            List<string> seeds = SeedNames(n);

            // Tiny field: one heat ranks everyone — that heat IS the qualifying final.
            if (n <= ppr)
            {
                SpecRace final = new SpecRace
                {
                    Id = "W1-1",
                    Round = 1,
                    Slot = BracketSlot.GrandFinal,
                    Advance = Go.MinInt(ppr, n),
                    Terminal = true,
                    FinalTarget = Go.MinInt(ppr, n),
                    Sources = SeedSources(RangeInts(1, n)),
                };
                return new Spec { Body = new List<SpecRace> { final }, WinnersFinalId = "W1-1", FinalRound = 1, CTA = cta };
            }

            List<SpecRace> body = new List<SpecRace>();

            // ---- Winners bracket ----
            int round = 1;
            List<List<string>> groups = Bracket.BalancedSeeding(seeds, ppr);
            List<string> prevIDs = new List<string>();
            List<int> prevSizes = new List<int>();
            for (int i = 0; i < groups.Count; i++)
            {
                List<string> g = groups[i];
                string id = "W1-" + Go.Itoa(i + 1);
                body.Add(new SpecRace
                {
                    Id = id,
                    Round = 1,
                    Slot = BracketSlot.Winners,
                    Advance = Go.MinInt(adv, g.Count),
                    Sources = SeedSources(NamesToSeeds(g)),
                });
                prevIDs.Add(id);
                prevSizes.Add(g.Count);
            }
            // droppedByRound[r] = the loser refs (positions adv+1..size) produced by
            // winners round r — they fall into the losers bracket.
            List<List<SlotRef>> droppedByRound = new List<List<SlotRef>> { LoserRefs(prevIDs, prevSizes, adv) };

            int wrnum = 1;
            while (prevIDs.Count > 1)
            {
                List<SlotRef> advRefs = AdvancerRefs(prevIDs, prevSizes, adv);
                wrnum++;
                round += 2; // winners occupy odd columns; redemption interleaves on even
                List<List<SlotRef>> chunks = ChunkRefs(advRefs, ppr);
                List<string> ids = new List<string>();
                List<int> sizes = new List<int>();
                for (int i = 0; i < Go.Len(chunks); i++)
                {
                    List<SlotRef> ch = chunks[i];
                    string id = "W" + Go.Itoa(wrnum) + "-" + Go.Itoa(i + 1);
                    body.Add(new SpecRace
                    {
                        Id = id,
                        Round = round,
                        Slot = BracketSlot.Winners,
                        Advance = Go.MinInt(adv, ch.Count),
                        Sources = ch,
                    });
                    ids.Add(id);
                    sizes.Add(ch.Count);
                }
                prevIDs = ids;
                prevSizes = sizes;
                droppedByRound.Add(LoserRefs(ids, sizes, adv));
            }
            string winnersFinalID = prevIDs[0];
            int winnersFinalRound = round;

            // ---- Losers (redemption) bracket ----
            //
            // Real double-elimination cadence — the one the 16-pilot sheet transcribes
            // and the elimination engine's LosersRoundPlan follows. The first two intakes
            // run back to back (W1's drops, then the survivors + W2's drops); after that
            // the redemption side ALTERNATES a MINOR round, which halves the pool with
            // nobody new entering, and a MAJOR round that folds in the next winners
            // round's drops. The minor round is what makes the numbers meet: a major
            // round of k heats leaves 2k survivors, halving makes that k, and the next
            // winners round (half as many heats again) drops k — so every major round is
            // full heats. This generator used to fold the next drops straight in after
            // every round, so W3's two droppers landed on L2's four survivors and the
            // halving ran as two heats of three — the 13-pilot C Main got an R6-1 AND an
            // R6-2 where the sheet has one. Only the 16 field escaped, via SpecFor16.
            List<SlotRef> pool = new List<SlotRef>(droppedByRound[0]);
            int lround = 2;
            int lrnum = 0;
            List<string> lastIDs = new List<string>();
            System.Action reduce = () =>
            {
                List<int> sizes;
                lastIDs = EmitLosersRound(body, ref lrnum, lround, pool, ppr, adv, out sizes);
                lround += 2;
                pool = AdvancerRefs(lastIDs, sizes, adv);
            };
            for (int w = 1; w < droppedByRound.Count; w++)
            {
                if (w == 1)
                {
                    if (pool.Count > 1) // first intake: W1's drops alone (a lone dropper waits)
                    {
                        reduce();
                    }
                }
                else if (pool.Count > adv) // minor round: halve, nobody enters
                {
                    reduce();
                }
                pool.AddRange(droppedByRound[w]);
                reduce(); // major round: survivors + this winners round's drops
            }
            // A ragged field can leave the last major round as several heats — keep
            // halving until a single redemption final remains.
            while (lastIDs.Count > 1)
            {
                reduce();
            }
            string losersFinalID = "";
            if (lastIDs.Count == 1)
            {
                losersFinalID = lastIDs[0];
            }

            // ---- Terminal final ----
            int finalRound = winnersFinalRound;
            if (lround - 2 > finalRound)
            {
                finalRound = lround - 2;
            }
            finalRound++;

            // The terminal final takes adv from each side (top-2 + top-2 = the sheet's
            // 4; top-3 + top-3 = a 6-up final).
            List<SlotRef> finalists = new List<SlotRef>();
            if (losersFinalID != "")
            {
                for (int p = 1; p <= adv; p++)
                {
                    finalists.Add(new SlotRef(winnersFinalID, p));
                }
                for (int p = 1; p <= adv; p++)
                {
                    finalists.Add(new SlotRef(losersFinalID, p));
                }
            }
            else
            {
                // No redemption final materialised — qualify the winners-final top-ppr.
                for (int p = 1; p <= ppr; p++)
                {
                    finalists.Add(new SlotRef(winnersFinalID, p));
                }
            }

            return new Spec
            {
                Body = body,
                WinnersFinalId = winnersFinalID,
                LosersFinalId = losersFinalID,
                Finalists = finalists,
                FinalRound = finalRound,
                CTA = cta,
            };
        }

        // EmitLosersRound chunks the pool into redemption heats, appends them, and
        // returns their ids + sizes. lrnum is bumped per round for stable L{n}-{i} ids.
        private static List<string> EmitLosersRound(List<SpecRace> body, ref int lrnum, int lround, List<SlotRef> pool, int ppr, int adv, out List<int> sizes)
        {
            lrnum++;
            List<List<SlotRef>> chunks = ChunkRefs(pool, ppr);
            List<string> ids = new List<string>();
            sizes = new List<int>();
            for (int i = 0; i < Go.Len(chunks); i++)
            {
                List<SlotRef> ch = chunks[i];
                string id = "L" + Go.Itoa(lrnum) + "-" + Go.Itoa(i + 1);
                body.Add(new SpecRace
                {
                    Id = id,
                    Round = lround,
                    Slot = BracketSlot.Losers,
                    Advance = Go.MinInt(adv, ch.Count),
                    Sources = ch,
                });
                ids.Add(id);
                sizes.Add(ch.Count);
            }
            return ids;
        }

        // AdvancerRefs lists every advancing slot (positions 1..adv, capped to each
        // race's size) across a round's races, ordered position-major so the winners
        // of different heats are spread before the runners-up when re-chunked.
        internal static List<SlotRef> AdvancerRefs(List<string> ids, List<int> sizes, int adv)
        {
            List<SlotRef> refs = new List<SlotRef>();
            for (int p = 1; p <= adv; p++)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    if (p <= sizes[i])
                    {
                        refs.Add(new SlotRef(ids[i], p));
                    }
                }
            }
            return refs;
        }

        // LoserRefs lists every eliminated slot (positions adv+1..size) across a
        // round's races — the pilots that drop into the losers bracket.
        internal static List<SlotRef> LoserRefs(List<string> ids, List<int> sizes, int adv)
        {
            List<SlotRef> refs = new List<SlotRef>();
            for (int i = 0; i < ids.Count; i++)
            {
                for (int p = adv + 1; p <= sizes[i]; p++)
                {
                    refs.Add(new SlotRef(ids[i], p));
                }
            }
            return refs;
        }

        // ChunkRefs splits refs into groups of at most size, evening out the last two
        // groups so no heat is left with a single pilot when it can be avoided.
        internal static List<List<SlotRef>> ChunkRefs(List<SlotRef> refs, int size)
        {
            if (refs.Count == 0)
            {
                return null;
            }
            int races = (refs.Count + size - 1) / size;
            List<List<SlotRef>> output = new List<List<SlotRef>>();
            for (int i = 0; i < races; i++)
            {
                output.Add(new List<SlotRef>());
            }
            // Balanced fill: distribute round-robin so counts differ by at most 1.
            for (int i = 0; i < refs.Count; i++)
            {
                output[i % races].Add(refs[i]);
            }
            return output;
        }

        // NormaliseShape applies the defaults and keeps the shape sane: 2..8 per race,
        // at least one advancing and at least one dropping.
        internal static void NormaliseShape(ref int ppr, ref int adv)
        {
            if (ppr < 2 || ppr > 8)
            {
                ppr = DefaultPilotsPerRace;
            }
            if (adv < 1)
            {
                adv = DefaultAdvance;
            }
            if (adv >= ppr)
            {
                adv = ppr - 1;
            }
        }

        internal static List<int> RangeInts(int lo, int hi)
        {
            List<int> output = new List<int>();
            for (int i = lo; i <= hi; i++)
            {
                output.Add(i);
            }
            return output;
        }

        // SeedNames returns ["1","2",…,"n"] — placeholder pilot names used to derive the
        // deterministic Round-1 seeding via Bracket.BalancedSeeding.
        internal static List<string> SeedNames(int n)
        {
            List<string> output = new List<string>();
            for (int i = 0; i < n; i++)
            {
                output.Add(Go.Itoa(i + 1));
            }
            return output;
        }

        // NamesToSeeds maps the seed-name groups back to integer seeds.
        internal static List<int> NamesToSeeds(List<string> names)
        {
            List<int> output = new List<int>();
            foreach (string s in names)
            {
                output.Add(Go.ScanInt(s));
            }
            return output;
        }
    }
}
