using System;
using System.Collections.Generic;

namespace BracketEngine
{
    // Port of the public-diagram PROJECTION from the AUFPV platform's race-bracket
    // handlers (race_bracket_public.go: buildProjectedBracket, projectLiveOntoSimulated,
    // newLivePlacements, liveFinisherAtRank, liveFinishRanks, projectedCTARace,
    // augmentConnectorsWithPredicted; race_bracket_overlay.go: buildEliminationPublicSVG;
    // race_bracket_qualifier.go: qualifierPublicSVG; race_bracket_mainsprogression.go:
    // mainStructureSVG / mainsProgressionPublicSVG). Logic only — no SVG.

    /// <summary>
    /// The data the public bracket diagram is drawn from: the live state overlaid on a
    /// seeds-always-win simulation, so unreached heats still show who feeds them.
    /// </summary>
    public class BracketProjection
    {
        /// <summary>Throwaway render state — never persist it or feed it back to the engine.</summary>
        public Bracket Projected { get; set; }

        /// <summary>
        /// Race id → placeholder pilot ("__slot_N__") → the slot it stands for
        /// ("W2-1 · 1st"). Null when the bracket is complete (nothing to predict).
        /// </summary>
        public Dictionary<string, Dictionary<string, PilotSlot>> PredictedSlots { get; set; }

        /// <summary>ConnectorsFromState(Projected) plus edges into the placeholder rows.</summary>
        public List<BracketConnector> Connectors { get; set; }
    }

    public static class BracketDiagram
    {
        /// <summary>
        /// The public diagram for an elimination bracket (Go: buildEliminationPublicSVG).
        /// A finished bracket renders as-is; otherwise it is projected forward. If a fresh
        /// bracket can't be generated from the config the live state is drawn unprojected,
        /// exactly as the platform does.
        /// </summary>
        public static BracketProjection Elimination(Bracket live)
        {
            if (live == null)
            {
                throw new ArgumentNullException(nameof(live));
            }
            Bracket projected = null;
            Dictionary<string, Dictionary<string, PilotSlot>> predicted = null;
            if (!live.IsComplete())
            {
                Bracket fresh = null;
                try
                {
                    fresh = Bracket.New(live.Config());
                }
                catch (BracketException)
                {
                    fresh = null;
                }
                if (fresh != null)
                {
                    List<BracketConnector> conns = BracketFlow.Simulate(fresh);
                    projected = ProjectLiveOntoSimulated(fresh, live, conns, out predicted);
                }
            }
            if (projected == null)
            {
                projected = CloneBracket(live);
            }
            return Finish(projected, predicted);
        }

        /// <summary>
        /// The public diagram for a qualifier (Go: qualifierPublicSVG). The structure
        /// comes from the qualifier's own spec, so the template is
        /// Bracket.FromState(q.Config(), q.Simulate()) rather than Bracket.New.
        /// Throws BracketException if the qualifier's state can't be wrapped.
        /// </summary>
        public static BracketProjection Qualifier(Qual live)
        {
            if (live == null)
            {
                throw new ArgumentNullException(nameof(live));
            }
            Bracket liveB = Bracket.FromState(live.Config(), live.State());
            Bracket projected = null;
            Dictionary<string, Dictionary<string, PilotSlot>> predicted = null;
            if (!live.IsComplete())
            {
                Bracket sim = null;
                try
                {
                    sim = Bracket.FromState(live.Config(), live.Simulate());
                }
                catch (BracketException)
                {
                    sim = null;
                }
                if (sim != null)
                {
                    projected = ProjectLiveOntoSimulated(sim, liveB, BracketFlow.ConnectorsFromState(sim), out predicted);
                }
            }
            if (projected == null)
            {
                projected = CloneBracket(liveB);
            }
            return Finish(projected, predicted);
        }

        /// <summary>
        /// One projection per BUILT main, in the competition's main order (A first), each
        /// keyed by the main's label. Race ids are the main's INNER ids ("W1-1"), not the
        /// "A:W1-1" ids the format exposes. Unbuilt mains are skipped (the platform draws a
        /// structural preview for those, which is not a projection). A main whose state
        /// can't be wrapped is skipped too, as the platform renders it blank.
        /// </summary>
        public static List<(string Label, BracketProjection P)> MainsProgression(Mains live)
        {
            if (live == null)
            {
                throw new ArgumentNullException(nameof(live));
            }
            List<(string, BracketProjection)> output = new List<(string, BracketProjection)>();
            foreach (MainView v in Go.Iter(live.MainViews()))
            {
                if (!v.Built || v.Qual == null)
                {
                    continue;
                }
                BracketProjection p;
                try
                {
                    p = Qualifier(v.Qual);
                }
                catch (BracketException)
                {
                    continue;
                }
                output.Add((v.Label, p));
            }
            return output;
        }

        private static BracketProjection Finish(Bracket projected, Dictionary<string, Dictionary<string, PilotSlot>> predicted)
        {
            List<BracketConnector> conns = BracketFlow.ConnectorsFromState(projected);
            conns = AugmentConnectorsWithPredicted(conns, predicted);
            return new BracketProjection { Projected = projected, PredictedSlots = predicted, Connectors = conns };
        }

        // The finished-bracket path draws the live bracket itself; hand back a deep copy so
        // the "throwaway" projection can never write through to the live state.
        private static Bracket CloneBracket(Bracket b)
        {
            BracketState st = BracketJson.Deserialize<BracketState>(BracketJson.Serialize(b.State()));
            return Bracket.FromState(b.Config(), st);
        }

        /// <summary>
        /// AugmentConnectorsWithPredicted appends connector edges for predicted-slot
        /// placeholders so the diagram draws lines flowing into not-yet-run races.
        /// ConnectorsFromState only emits edges where a pilot actually appears in a prior
        /// race, so placeholder rows are otherwise disconnected.
        ///
        /// Differs from Go on purpose: Go keeps pointers into the slice it is appending
        /// to, so after a reallocation a placeholder added to an earlier edge is silently
        /// lost, and which ones depends on map iteration order. Here every placeholder
        /// lands on its edge. Targets/placeholders are walked in a deterministic order
        /// (race sort key, then placeholder number). The input connectors are not mutated.
        /// </summary>
        public static List<BracketConnector> AugmentConnectorsWithPredicted(List<BracketConnector> connectors, Dictionary<string, Dictionary<string, PilotSlot>> predictedSlots)
        {
            if (predictedSlots == null || predictedSlots.Count == 0)
            {
                return connectors;
            }
            List<BracketConnector> output = new List<BracketConnector>();
            Dictionary<(string, string), BracketConnector> existing = new Dictionary<(string, string), BracketConnector>();
            foreach (BracketConnector c in Go.Iter(connectors))
            {
                BracketConnector copy = new BracketConnector { FromId = c.FromId, ToId = c.ToId, Pilots = Go.Copy(c.Pilots) };
                output.Add(copy);
                existing[(copy.FromId, copy.ToId)] = copy;
            }
            List<string> targets = new List<string>(predictedSlots.Keys);
            Go.SortStable(targets, (a, b) => string.CompareOrdinal(BracketFlow.RaceSortKey(a), BracketFlow.RaceSortKey(b)) < 0);
            foreach (string targetId in targets)
            {
                Dictionary<string, PilotSlot> raceSlots = predictedSlots[targetId];
                if (raceSlots == null)
                {
                    continue;
                }
                List<string> placeholders = new List<string>(raceSlots.Keys);
                Go.SortStable(placeholders, (a, b) => PlaceholderLess(a, b));
                foreach (string placeholder in placeholders)
                {
                    PilotSlot slot = raceSlots[placeholder];
                    if (slot == null || string.IsNullOrEmpty(slot.SourceRaceId))
                    {
                        continue;
                    }
                    BracketConnector e;
                    if (existing.TryGetValue((slot.SourceRaceId, targetId), out e))
                    {
                        e.Pilots = Go.Append(e.Pilots, placeholder);
                        continue;
                    }
                    e = new BracketConnector { FromId = slot.SourceRaceId, ToId = targetId, Pilots = new List<string> { placeholder } };
                    output.Add(e);
                    existing[(e.FromId, e.ToId)] = e;
                }
            }
            return output;
        }

        private static bool PlaceholderLess(string a, string b)
        {
            int na = PlaceholderNumber(a), nb = PlaceholderNumber(b);
            if (na != nb)
            {
                return na < nb;
            }
            return string.CompareOrdinal(a, b) < 0;
        }

        private static int PlaceholderNumber(string p)
        {
            const string pre = "__slot_";
            if (p != null && p.StartsWith(pre, StringComparison.Ordinal) && p.EndsWith("__", StringComparison.Ordinal) && p.Length > pre.Length + 2)
            {
                int n;
                if (int.TryParse(p.Substring(pre.Length, p.Length - pre.Length - 2), out n))
                {
                    return n;
                }
            }
            return int.MaxValue;
        }

        /// <summary>
        /// ProjectLiveOntoSimulated overlays the live bracket's recorded results onto a
        /// fully-simulated (seeds-always-win) copy `fresh`, so the public diagram shows
        /// every race — including ones not yet reached — with complete connector flow.
        /// `fresh` must already be played to completion and `freshConnectors` its flow.
        /// Live data takes precedence wherever it exists; not-yet-arrived pilots render as
        /// placeholders ("__slot_N__") described by `predictedSlots`. Mutates and returns
        /// `fresh`. (Go: projectLiveOntoSimulated.)
        /// </summary>
        public static Bracket ProjectLiveOntoSimulated(Bracket fresh, Bracket live, List<BracketConnector> freshConnectors, out Dictionary<string, Dictionary<string, PilotSlot>> predictedSlots)
        {
            Dictionary<string, Dictionary<string, PilotSlot>> freshSlotChart = BracketFlow.SlotChart(fresh, freshConnectors);

            // Capture fresh's predicted pilot order BEFORE we overwrite races.
            Dictionary<string, List<string>> freshPilots = new Dictionary<string, List<string>>();
            foreach (Race fr in Go.Iter(fresh.State().Races))
            {
                freshPilots[fr.Id] = Go.Copy(fr.Pilots);
            }

            Dictionary<string, Race> liveByID = new Dictionary<string, Race>();
            foreach (Race r in Go.Iter(live.State().Races))
            {
                liveByID[r.Id] = r;
            }

            predictedSlots = new Dictionary<string, Dictionary<string, PilotSlot>>();
            int placeholderCounter = 0;
            LivePlacements placed = new LivePlacements(fresh, live, freshSlotChart, freshPilots);
            foreach (Race fr in Go.Iter(fresh.State().Races))
            {
                Race lr;
                liveByID.TryGetValue(fr.Id, out lr);

                List<string> newPilots = null;
                Dictionary<string, PilotSlot> raceSlots = new Dictionary<string, PilotSlot>();
                Dictionary<string, PilotSlot> chartForRace;
                freshSlotChart.TryGetValue(fr.Id, out chartForRace);
                List<string> template;
                freshPilots.TryGetValue(fr.Id, out template);

                foreach (string freshPilot in Go.Iter(template))
                {
                    PilotSlot slot = null;
                    bool hasSlot = chartForRace != null && chartForRace.TryGetValue(freshPilot, out slot);
                    if (!hasSlot || slot.SourceRaceId == "")
                    {
                        // W1 (qualifying) seed — same identity live and fresh; prefer the
                        // live pilot at the same position when live has one.
                        string name = freshPilot;
                        if (lr != null && Go.Len(lr.Pilots) > Go.Len(newPilots))
                        {
                            name = lr.Pilots[Go.Len(newPilots)];
                        }
                        newPilots = Go.Append(newPilots, name);
                        continue;
                    }
                    Race sourceLive;
                    liveByID.TryGetValue(slot.SourceRaceId, out sourceLive);
                    string taken;
                    if (placed.Take(fr.Id, slot, out taken))
                    {
                        newPilots = Go.Append(newPilots, taken);
                        continue;
                    }
                    if (!placed.TargetPopulated(fr.Id))
                    {
                        string finisher;
                        if (LiveFinisherAtRank(sourceLive, slot.SourceRank, out finisher))
                        {
                            newPilots = Go.Append(newPilots, finisher);
                            continue;
                        }
                    }
                    placeholderCounter++;
                    string placeholder = "__slot_" + Go.Itoa(placeholderCounter) + "__";
                    newPilots = Go.Append(newPilots, placeholder);
                    raceSlots[placeholder] = slot;
                }

                fr.Pilots = newPilots;
                if (raceSlots.Count > 0)
                {
                    predictedSlots[fr.Id] = raceSlots;
                }

                if (lr != null)
                {
                    fr.Winners = Go.Copy(lr.Winners);
                    fr.Losers = Go.Copy(lr.Losers);
                    fr.IsComplete = lr.IsComplete;
                    fr.FullPlacement = lr.FullPlacement;
                    fr.FinalTarget = lr.FinalTarget;
                    // Void status is authoritative from live, not the simulation (the sim
                    // clinches the chase-the-ace in the minimum heats and voids the rest).
                    fr.IsVoid = lr.IsVoid;
                    // A full finishing order lives in lr.Pilots; render rows in finish order.
                    if (lr.IsComplete && lr.FullPlacement && Go.Len(lr.Pilots) > 0)
                    {
                        fr.Pilots = Go.Copy(lr.Pilots);
                    }
                }
                else
                {
                    fr.Winners = null;
                    fr.Losers = null;
                    fr.IsComplete = false;
                    fr.FullPlacement = false;
                }
            }

            // Reconcile chase-the-ace races with the live bracket: show a CTA heat only
            // once it has actually been created, and every live CTA heat even when the
            // simulation clinched the series sooner.
            HashSet<string> liveCTA = new HashSet<string>();
            List<Race> liveCTAOrder = new List<Race>();
            foreach (Race r in Go.Iter(live.State().Races))
            {
                if (r.Bracket == BracketSlot.ChaseTheAce)
                {
                    liveCTA.Add(r.Id);
                    liveCTAOrder.Add(r);
                }
            }
            List<Race> kept = new List<Race>();
            HashSet<string> present = new HashSet<string>();
            foreach (Race r in Go.Iter(fresh.State().Races))
            {
                if (r.Bracket == BracketSlot.ChaseTheAce && !liveCTA.Contains(r.Id))
                {
                    predictedSlots.Remove(r.Id); // drop a not-yet-created CTA heat
                    continue;
                }
                kept.Add(r);
                present.Add(r.Id);
            }
            foreach (Race lr in liveCTAOrder)
            {
                if (!present.Contains(lr.Id))
                {
                    kept.Add(ProjectedCTARace(lr)); // live CTA heat the sim didn't predict
                }
            }
            fresh.State().Races = kept;
            return fresh;
        }

        // ProjectedCTARace copies a live chase-the-ace race into a throwaway race.
        private static Race ProjectedCTARace(Race lr)
        {
            return new Race
            {
                Id = lr.Id,
                Round = lr.Round,
                Pilots = Go.Copy(lr.Pilots),
                Winners = Go.Copy(lr.Winners),
                Losers = Go.Copy(lr.Losers),
                IsComplete = lr.IsComplete,
                IsVoid = lr.IsVoid,
                Bracket = lr.Bracket,
                FinalTarget = lr.FinalTarget,
                FullPlacement = lr.FullPlacement,
            };
        }

        /// <summary>
        /// LiveFinisherAtRank returns the pilot who finished at the 1-indexed `rank` in a
        /// COMPLETED race: race.Pilots for a full placement, else Winners then Losers in
        /// stored order. False for an incomplete race or an unknown rank.
        /// </summary>
        public static bool LiveFinisherAtRank(Race r, int rank, out string pilot)
        {
            pilot = "";
            if (r == null || rank < 1 || !r.IsComplete)
            {
                return false;
            }
            int idx = rank - 1;
            if (r.FullPlacement && idx < Go.Len(r.Pilots))
            {
                pilot = r.Pilots[idx];
                return true;
            }
            if (idx < Go.Len(r.Winners))
            {
                pilot = r.Winners[idx];
                return true;
            }
            int loserIdx = idx - Go.Len(r.Winners);
            if (loserIdx >= 0 && loserIdx < Go.Len(r.Losers))
            {
                pilot = r.Losers[loserIdx];
                return true;
            }
            return false;
        }

        /// <summary>LiveFinishRanks maps each pilot of a completed race to their 1-indexed finishing rank.</summary>
        public static Dictionary<string, int> LiveFinishRanks(Race r)
        {
            Dictionary<string, int> output = new Dictionary<string, int>();
            if (r.FullPlacement && Go.Len(r.Pilots) > 0)
            {
                for (int i = 0; i < r.Pilots.Count; i++)
                {
                    output[r.Pilots[i]] = i + 1;
                }
                return output;
            }
            for (int i = 0; i < Go.Len(r.Winners); i++)
            {
                output[r.Winners[i]] = i + 1;
            }
            for (int i = 0; i < Go.Len(r.Losers); i++)
            {
                if (!output.ContainsKey(r.Losers[i]))
                {
                    output[r.Losers[i]] = Go.Len(r.Winners) + i + 1;
                }
            }
            return output;
        }

        /// <summary>
        /// LivePlacements resolves a projected slot ("W1-1 · 1st" feeding W2-1) to the
        /// pilot the engine ACTUALLY placed in that target race. The engine re-ranks a
        /// heat's advancers by qualifying seed before snaking them across the next round,
        /// so after an upset the 1st-place finisher goes where the simulation sent the
        /// 2nd-place one. For each target the live pilots from a given source are matched
        /// to that source's projected slots in order: slots by simulated rank, pilots by
        /// live finishing rank. (Go: livePlacements.)
        /// </summary>
        internal class LivePlacements
        {
            private readonly Dictionary<string, Dictionary<string, List<string>>> byTarget = new Dictionary<string, Dictionary<string, List<string>>>();
            private readonly Dictionary<string, Dictionary<string, Dictionary<int, int>>> slotIdx = new Dictionary<string, Dictionary<string, Dictionary<int, int>>>();
            private readonly HashSet<string> populated = new HashSet<string>();

            public LivePlacements(Bracket fresh, Bracket live, Dictionary<string, Dictionary<string, PilotSlot>> freshSlotChart, Dictionary<string, List<string>> freshPilots)
            {
                Dictionary<string, Race> liveByID = new Dictionary<string, Race>();
                foreach (Race r in Go.Iter(live.State().Races))
                {
                    liveByID[r.Id] = r;
                }
                foreach (Race fr in Go.Iter(fresh.State().Races))
                {
                    Race lr;
                    if (!liveByID.TryGetValue(fr.Id, out lr) || lr == null || Go.Len(lr.Pilots) == 0)
                    {
                        continue;
                    }
                    populated.Add(fr.Id);
                    Dictionary<string, PilotSlot> chart;
                    freshSlotChart.TryGetValue(fr.Id, out chart);
                    List<string> template;
                    freshPilots.TryGetValue(fr.Id, out template);
                    Dictionary<string, List<int>> ranksBySource = new Dictionary<string, List<int>>();
                    foreach (string p in Go.Iter(template))
                    {
                        PilotSlot slot;
                        if (chart != null && chart.TryGetValue(p, out slot) && slot.SourceRaceId != "")
                        {
                            List<int> l;
                            if (!ranksBySource.TryGetValue(slot.SourceRaceId, out l))
                            {
                                l = new List<int>();
                                ranksBySource[slot.SourceRaceId] = l;
                            }
                            l.Add(slot.SourceRank);
                        }
                    }
                    foreach (KeyValuePair<string, List<int>> kv in ranksBySource)
                    {
                        Race srcLive;
                        if (!liveByID.TryGetValue(kv.Key, out srcLive) || srcLive == null || !srcLive.IsComplete)
                        {
                            continue;
                        }
                        Dictionary<string, int> rankOf = LiveFinishRanks(srcLive);
                        List<string> from = new List<string>();
                        foreach (string p in lr.Pilots)
                        {
                            if (rankOf.ContainsKey(p))
                            {
                                from.Add(p);
                            }
                        }
                        if (from.Count == 0)
                        {
                            continue;
                        }
                        Go.SortStable(from, (a, b) => rankOf[a] < rankOf[b]);
                        List<int> sorted = new List<int>(kv.Value);
                        sorted.Sort();
                        Dictionary<int, int> idx = new Dictionary<int, int>();
                        for (int i = 0; i < sorted.Count; i++)
                        {
                            if (i < from.Count)
                            {
                                idx[sorted[i]] = i;
                            }
                        }
                        Dictionary<string, List<string>> bt;
                        if (!byTarget.TryGetValue(fr.Id, out bt))
                        {
                            bt = new Dictionary<string, List<string>>();
                            byTarget[fr.Id] = bt;
                            slotIdx[fr.Id] = new Dictionary<string, Dictionary<int, int>>();
                        }
                        bt[kv.Key] = from;
                        slotIdx[fr.Id][kv.Key] = idx;
                    }
                }
            }

            /// <summary>Take returns the live pilot occupying `slot` in `target`, if the engine placed one there.</summary>
            public bool Take(string target, PilotSlot slot, out string pilot)
            {
                pilot = "";
                Dictionary<string, Dictionary<int, int>> bySrc;
                Dictionary<int, int> idx;
                int i;
                if (!slotIdx.TryGetValue(target, out bySrc) || !bySrc.TryGetValue(slot.SourceRaceId, out idx) || !idx.TryGetValue(slot.SourceRank, out i))
                {
                    return false;
                }
                pilot = byTarget[target][slot.SourceRaceId][i];
                return true;
            }

            public bool TargetPopulated(string target)
            {
                return populated.Contains(target);
            }
        }
    }
}
