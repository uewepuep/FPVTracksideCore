using System.Collections.Generic;
using Newtonsoft.Json;

namespace Brackets
{
    /// <summary>LadderState is the persisted state blob for a ladder.</summary>
    public class LadderState
    {
        [JsonProperty("races")]
        public List<LadderRace> Races { get; set; }

        [JsonProperty("pilotsPerRace")]
        public int PilotsPerRace { get; set; }

        [JsonProperty("advance")]
        public int Advance { get; set; }
    }

    /// <summary>LadderRace is one bout in the ladder.</summary>
    public class LadderRace
    {
        [JsonProperty("id")]
        public string Id { get; set; } = ""; // "LR1".."LRk", lowest bout first

        [JsonProperty("round")]
        public int Round { get; set; } // 1-based bout number

        [JsonProperty("pilots")]
        public List<string> Pilots { get; set; }

        [JsonProperty("winners")]
        public List<string> Winners { get; set; }

        [JsonProperty("losers")]
        public List<string> Losers { get; set; }

        [JsonProperty("isComplete")]
        public bool IsComplete { get; set; }

        [JsonProperty("fullPlacement")]
        public bool FullPlacement { get; set; }
    }

    /// <summary>
    /// LadderFormat is a MultiGP-style seeded ladder (a.k.a. "regulation" / iron-man).
    ///
    /// The lowest seeds race first; the top `Advance` finishers of each bout carry
    /// forward and join the next-highest seeds for the following bout, narrowing until
    /// the survivors meet the top seeds in the final. Top seeds therefore get
    /// effective byes — seed 1 enters only in the last bout. Single-elimination: a
    /// pilot who doesn't advance is out.
    ///
    /// The whole ladder is pre-generated so every bout can be exported to FPVTrackside
    /// up front; each bout's fresh seeds are known immediately, and carry-over slots
    /// are filled as upstream bouts complete.
    /// </summary>
    public class LadderFormat : IRaceFormat
    {
        private LadderState state;

        private LadderFormat(LadderState state)
        {
            this.state = state;
        }

        public static LadderFormat New(FormatConfig cfg)
        {
            List<string> names = FormatUtil.TrimNames(cfg.PilotNames);
            int p = cfg.PilotsPerRace;
            int a = cfg.PilotsToAdvance;
            if (names.Count < 2)
            {
                throw new BracketException("ladder: need at least 2 pilots");
            }
            if (p < 2 || p > 8)
            {
                throw new BracketException("ladder: pilotsPerRace must be between 2 and 8");
            }
            if (a < 1 || a >= p)
            {
                throw new BracketException("ladder: pilotsToAdvance must be ≥1 and < pilotsPerRace");
            }

            int n = names.Count;
            LadderState st = new LadderState { PilotsPerRace = p, Advance = a };

            // Consume fresh seeds from the bottom (worst qualifier) upward. Bout 1 takes
            // the lowest P seeds; each later bout reserves A carry-over slots and takes
            // the next (P-A) higher seeds.
            int round = 0;
            if (n <= p)
            {
                // Single final bout containing everyone.
                round++;
                st.Races = Go.Append(st.Races, new LadderRace
                {
                    Id = "LR" + Go.Itoa(round),
                    Round = round,
                    Pilots = Go.Copy(names),
                });
                return new LadderFormat(st);
            }

            int hi = n - p; // upper bound (exclusive) of the next fresh chunk in names
            round++;
            st.Races = Go.Append(st.Races, new LadderRace
            {
                Id = "LR" + Go.Itoa(round),
                Round = round,
                Pilots = Go.Copy(names.GetRange(n - p, p)), // bottom P seeds
            });
            while (hi > 0)
            {
                int freshCount = p - a;
                if (freshCount > hi)
                {
                    freshCount = hi;
                }
                List<string> fresh = Go.Copy(names.GetRange(hi - freshCount, freshCount));
                hi -= freshCount;
                round++;
                st.Races = Go.Append(st.Races, new LadderRace
                {
                    Id = "LR" + Go.Itoa(round),
                    Round = round,
                    Pilots = fresh, // carry-overs appended when the previous bout completes
                });
            }
            return new LadderFormat(st);
        }

        public static LadderFormat FromState(FormatConfig cfg, string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return New(cfg);
            }
            return new LadderFormat(Decode(raw));
        }

        private static LadderState Decode(string raw)
        {
            LadderState st;
            try
            {
                st = BracketJson.Deserialize<LadderState>(raw);
            }
            catch (JsonException e)
            {
                throw new BracketException(e.Message);
            }
            return st ?? new LadderState();
        }

        public string Kind
        {
            get { return FormatKinds.Ladder; }
        }

        public string LayoutKind
        {
            get { return "ladder-grid"; }
        }

        public LadderState State()
        {
            return state;
        }

        public string StateJson()
        {
            return BracketJson.Serialize(state);
        }

        public void RestoreState(string raw)
        {
            state = Decode(raw);
        }

        // RaceIndex returns the list index of a bout by id, or -1.
        private int RaceIndex(string id)
        {
            for (int i = 0; i < Go.Len(state.Races); i++)
            {
                if (state.Races[i].Id == id)
                {
                    return i;
                }
            }
            return -1;
        }

        private bool IsFinal(int idx)
        {
            return idx == Go.Len(state.Races) - 1;
        }

        // AdvanceCount is how many pilots carry forward from a bout. The final bout
        // crowns one champion.
        private int AdvanceCount(int idx)
        {
            if (IsFinal(idx))
            {
                return 1;
            }
            return state.Advance;
        }

        // Ready reports whether a bout has all its pilots (carry-overs delivered). Bout 0
        // is always ready; later bouts wait for the previous bout to complete.
        private bool Ready(int idx)
        {
            return idx == 0 || state.Races[idx - 1].IsComplete;
        }

        public IReadOnlyList<RaceView> Races()
        {
            List<RaceView> output = new List<RaceView>();
            for (int i = 0; i < Go.Len(state.Races); i++)
            {
                LadderRace r = state.Races[i];
                string label = "Ladder Bout " + Go.Itoa(r.Round);
                if (IsFinal(i))
                {
                    label = "Final";
                }
                RaceView rv = new RaceView
                {
                    Id = r.Id,
                    Round = r.Round,
                    GroupLabel = label,
                    Title = label, // "Ladder Bout 1" / "Final" — already self-contained
                    Pilots = Go.Copy(r.Pilots),
                    Winners = Go.Copy(r.Winners),
                    Losers = Go.Copy(r.Losers),
                    IsComplete = r.IsComplete,
                    FullPlacement = r.FullPlacement,
                    ExpectedWinners = AdvanceCount(i),
                };
                if (r.IsComplete)
                {
                    string unused;
                    rv.CanEdit = CanEditRace(r.Id, out unused);
                }
                else if (!Ready(i))
                {
                    rv.Locked = true;
                    rv.LockedReason = "waiting on " + state.Races[i - 1].Id;
                }
                output.Add(rv);
            }
            return output;
        }

        public void ApplyPlacement(string raceId, IList<string> placement)
        {
            int idx = RaceIndex(raceId);
            if (idx < 0)
            {
                throw new BracketException("ladder: race " + raceId + " not found");
            }
            LadderRace race = state.Races[idx];
            if (race.IsComplete)
            {
                throw new BracketException("ladder: race " + raceId + " already complete (use edit)");
            }
            if (!Ready(idx))
            {
                throw new BracketException("ladder: race " + raceId + " is waiting on upstream pilots");
            }
            string err = FormatUtil.SamePilotSet(race.Pilots, placement);
            if (err != null)
            {
                throw new BracketException("ladder: " + raceId + " placement " + err);
            }

            race.Pilots = Go.Copy(placement);
            race.FullPlacement = true;
            int a = AdvanceCount(idx);
            int count = placement == null ? 0 : placement.Count;
            if (a > count)
            {
                a = count;
            }
            List<string> winners = new List<string>();
            List<string> losers = new List<string>();
            for (int i = 0; i < count; i++)
            {
                if (i < a)
                {
                    winners.Add(placement[i]);
                }
                else
                {
                    losers.Add(placement[i]);
                }
            }
            race.Winners = Go.Copy(winners);
            race.Losers = Go.Copy(losers);
            race.IsComplete = true;

            // Carry advancers into the next bout.
            if (!IsFinal(idx))
            {
                LadderRace next = state.Races[idx + 1];
                next.Pilots = Go.Append(next.Pilots, race.Winners);
            }
        }

        public bool CanEditRace(string raceId, out string reason)
        {
            int idx = RaceIndex(raceId);
            if (idx < 0)
            {
                reason = "race not found";
                return false;
            }
            if (!state.Races[idx].IsComplete)
            {
                reason = "";
                return true;
            }
            // Refuse once the bout that received these advancers has itself been run.
            if (!IsFinal(idx) && state.Races[idx + 1].IsComplete)
            {
                reason = "the next bout has already been run";
                return false;
            }
            reason = "";
            return true;
        }

        public void EditRace(string raceId, IList<string> placement)
        {
            int idx = RaceIndex(raceId);
            if (idx < 0)
            {
                throw new BracketException("ladder: race " + raceId + " not found");
            }
            string reason;
            if (!CanEditRace(raceId, out reason))
            {
                throw new BracketException("ladder: cannot edit " + raceId + ": " + reason);
            }
            LadderRace race = state.Races[idx];
            // Rewind the advancers this bout pushed into the next (un-run) bout.
            if (race.IsComplete && !IsFinal(idx))
            {
                LadderRace next = state.Races[idx + 1];
                next.Pilots = FormatUtil.RemoveAll(next.Pilots, race.Winners);
            }
            race.Winners = null;
            race.Losers = null;
            race.IsComplete = false;
            race.FullPlacement = false;
            ApplyPlacement(raceId, placement);
        }

        /// <summary>
        /// ResetRace un-runs a completed bout: rewinds the advancers it pushed into the
        /// next (un-run) bout and clears its own result so it becomes pending. Refused
        /// once the next bout has been run. No-op if not yet run.
        /// </summary>
        public void ResetRace(string raceId)
        {
            int idx = RaceIndex(raceId);
            if (idx < 0)
            {
                throw new BracketException("ladder: race " + raceId + " not found");
            }
            string reason;
            if (!CanEditRace(raceId, out reason))
            {
                throw new BracketException("ladder: cannot reset " + raceId + ": " + reason);
            }
            LadderRace race = state.Races[idx];
            if (!race.IsComplete)
            {
                return;
            }
            if (!IsFinal(idx))
            {
                LadderRace next = state.Races[idx + 1];
                next.Pilots = FormatUtil.RemoveAll(next.Pilots, race.Winners);
            }
            race.Winners = null;
            race.Losers = null;
            race.IsComplete = false;
            race.FullPlacement = false;
        }

        public string Champion()
        {
            if (Go.Len(state.Races) == 0)
            {
                return "";
            }
            LadderRace last = state.Races[state.Races.Count - 1];
            if (last.IsComplete && Go.Len(last.Winners) > 0)
            {
                return last.Winners[0];
            }
            return "";
        }

        public bool IsComplete()
        {
            return Champion() != "";
        }

        /// <summary>
        /// Standings ranks the final bout's finishers (champion first) then the pilots
        /// eliminated in each earlier bout, latest bout first (later eliminations rank
        /// higher) — placements only.
        /// </summary>
        public IReadOnlyList<Standing> Standings()
        {
            List<Standing> output = new List<Standing>();
            if (!IsComplete())
            {
                return output;
            }
            List<LadderRace> races = state.Races;
            int rank = 0;
            // Final bout, in placement order.
            LadderRace final = races[races.Count - 1];
            foreach (string p in Go.Iter(final.Pilots))
            {
                rank++;
                output.Add(new Standing { Rank = rank, Pilot = p, Detail = "Final" });
            }
            // Earlier bouts' eliminated pilots, latest first.
            for (int i = races.Count - 2; i >= 0; i--)
            {
                foreach (string p in Go.Iter(races[i].Losers))
                {
                    rank++;
                    output.Add(new Standing { Rank = rank, Pilot = p, Detail = "Bout " + Go.Itoa(races[i].Round) });
                }
            }
            return output;
        }
    }
}
