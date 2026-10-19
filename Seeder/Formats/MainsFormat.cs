using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace SeederEngine
{
    /// <summary>MainsConfig holds the mains setup parameters (persisted in format_config).</summary>
    public class MainsConfig
    {
        [JsonProperty("mains")]
        public List<string> Mains { get; set; } // ordered, A = top tier

        [JsonProperty("rounds")]
        public int Rounds { get; set; } // races each main runs

        [JsonProperty("direction")]
        public string Direction { get; set; } = ""; // "ascending" | "descending"

        [JsonProperty("pointsTable")]
        public SortedDictionary<int, int> PointsTable { get; set; } // finish position (1-based) → points

        // DropWorst discounts each pilot's N worst-scoring rounds from their total
        // (0 = count every round). Always keeps at least one round.
        [JsonProperty("dropWorst")]
        public int DropWorst { get; set; }

        // Rosters is the explicit pilot→main assignment captured at creation (empty
        // when pilots were auto-chunked by seed). Kept for reference/regeneration.
        [JsonProperty("rosters")]
        public GoMap<List<string>> Rosters { get; set; }

        // Divisions optionally splits the mains into independent competitions, each
        // with its own champion (the winner of its first/top main). Empty means a
        // single competition over all mains (the A-main winner is the overall
        // champion). All divisions share the same rounds / points / direction.
        [JsonProperty("divisions")]
        public List<MainsDivision> Divisions { get; set; }

        public bool ShouldSerializeRosters()
        {
            return Rosters != null && Rosters.Count > 0;
        }

        public bool ShouldSerializeDivisions()
        {
            return Divisions != null && Divisions.Count > 0;
        }
    }

    /// <summary>MainsState is the persisted state blob for a mains format.</summary>
    public class MainsState
    {
        [JsonProperty("cfg")]
        public MainsConfig Cfg { get; set; } = new MainsConfig();

        [JsonProperty("mains")]
        public List<Main> Mains { get; set; }

        // SeedOrder is the full grid order (seed 1 first). Tie-breaks use it so a
        // pilot manually placed in a main still ranks by their global seed. Empty on
        // brackets created before explicit assignment — falls back to in-main order.
        [JsonProperty("seedOrder")]
        public List<string> SeedOrder { get; set; }

        public bool ShouldSerializeSeedOrder()
        {
            return SeedOrder != null && SeedOrder.Count > 0;
        }
    }

    /// <summary>Main is one skill tier with its roster and per-round races.</summary>
    public class Main
    {
        [JsonProperty("label")]
        public string Label { get; set; } = "";

        [JsonProperty("pilots")]
        public List<string> Pilots { get; set; }

        [JsonProperty("races")]
        public List<MainsRace> Races { get; set; }
    }

    /// <summary>MainsRace is one round within a main.</summary>
    public class MainsRace
    {
        [JsonProperty("id")]
        public string Id { get; set; } = ""; // "A-R1"

        [JsonProperty("round")]
        public int Round { get; set; }

        [JsonProperty("pilots")]
        public List<string> Pilots { get; set; }

        [JsonProperty("placement")]
        public List<string> Placement { get; set; }

        [JsonProperty("isComplete")]
        public bool IsComplete { get; set; }
    }

    /// <summary>MainStandings is the per-main ranking for table rendering.</summary>
    public class MainStandings
    {
        public string Label { get; set; } = "";
        public List<Standing> Standings { get; set; }
    }

    /// <summary>
    /// MainRoundCell is one round's scoring for a pilot in the per-main scoreboard.
    /// HasResult is false for rounds not yet run (rendered as a blank cell); Dropped
    /// marks a round discounted by drop-worst; Position is the pilot's finishing place
    /// that round (1-based) — the overlay golds a cell when Position == 1 (a round win).
    /// </summary>
    public class MainRoundCell
    {
        public int Round { get; set; }
        public int Points { get; set; }
        public int Position { get; set; }
        public bool HasResult { get; set; }
        public bool Dropped { get; set; }
    }

    /// <summary>
    /// MainScoreRow is one pilot's row in the per-main scoreboard. Cells always has
    /// exactly MainScoreboard.Rounds entries (index 0 = round 1).
    /// </summary>
    public class MainScoreRow
    {
        public string Pilot { get; set; } = "";
        public int Total { get; set; }
        public List<MainRoundCell> Cells { get; set; }
    }

    /// <summary>
    /// MainScoreboard is a single main's full points table — per-round point cells
    /// plus the counted total — used by the OBS overlay. Rows are in finishing order
    /// (winner first).
    /// </summary>
    public class MainScoreboard
    {
        public string Label { get; set; } = "";
        public int Rounds { get; set; }
        public List<MainScoreRow> Rows { get; set; }
    }

    /// <summary>
    /// DivisionStandings groups the per-main scoreboards by division and reports each
    /// division's champion (winner of its top main, once that main's rounds are all
    /// recorded) and completion. With no configured divisions there is a single
    /// entry with Name "" covering every main.
    /// </summary>
    public class DivisionStandings
    {
        public string Name { get; set; } = "";
        public int ClassId { get; set; } // linked championship class (0 = none) — see MainsDivision.ClassId
        public string Champion { get; set; } = "";
        public bool Complete { get; set; }
        public List<MainStandings> Mains { get; set; }
    }

    /// <summary>
    /// MainsFormat is a points-based A/B/C… mains format. Pilots are split by seed
    /// into ordered mains (A = top tier); each main runs a fixed number of rounds;
    /// per-placement points are summed across rounds and pilots are ranked by total.
    /// Rounds are independent races — no pilot ever moves between them; only the
    /// points accumulation links them. Scoring can be descending (high points = best,
    /// 1st earns the most) or ascending (low points = best, F1-style position points).
    /// </summary>
    public class MainsFormat : IRaceFormat
    {
        private const string MainsAscending = "ascending";
        private const string MainsDescending = "descending";

        private MainsState state;

        private MainsFormat(MainsState state)
        {
            this.state = state;
        }

        public static MainsFormat New(FormatConfig cfg)
        {
            List<string> names = FormatUtil.TrimNames(cfg.PilotNames);
            MainsConfig mc = new MainsConfig
            {
                Mains = cfg.Mains,
                Rounds = cfg.Rounds,
                Direction = cfg.Direction ?? "",
                PointsTable = cfg.PointsTable == null ? null : new SortedDictionary<int, int>(cfg.PointsTable),
                DropWorst = cfg.DropWorst,
                Rosters = cfg.MainsRosters == null ? null : new GoMap<List<string>>(cfg.MainsRosters),
                Divisions = cfg.MainsDivisions,
            };
            ValidateMainsConfig(mc, names.Count);

            Dictionary<string, int> seedIdx = new Dictionary<string, int>();
            for (int i = 0; i < names.Count; i++)
            {
                seedIdx[names[i]] = i;
            }
            Dictionary<string, List<string>> rosters = MainsRosters(mc.Mains, names, cfg.MainsRosters, seedIdx);

            MainsState st = new MainsState { Cfg = mc, SeedOrder = Go.Copy(names) };
            foreach (string label in mc.Mains)
            {
                List<string> roster = rosters[label];
                Main m = new Main { Label = label, Pilots = roster };
                for (int rnd = 1; rnd <= mc.Rounds; rnd++)
                {
                    m.Races = Go.Append(m.Races, new MainsRace
                    {
                        Id = label + "-R" + Go.Itoa(rnd),
                        Round = rnd,
                        Pilots = Go.Copy(roster),
                    });
                }
                st.Mains = Go.Append(st.Mains, m);
            }
            return new MainsFormat(st);
        }

        internal static void ValidateMainsConfig(MainsConfig mc, int pilotCount)
        {
            List<string> mains = new List<string>();
            foreach (string m in Go.Iter(mc.Mains))
            {
                string t = Go.TrimSpace(m);
                if (t != "")
                {
                    mains.Add(t);
                }
            }
            if (mains.Count < 1)
            {
                throw new BracketException("mains: need at least 1 main");
            }
            if (pilotCount < mains.Count)
            {
                throw new BracketException("mains: need at least one pilot per main (" + Go.Itoa(pilotCount) + " pilots, " + Go.Itoa(mains.Count) + " mains)");
            }
            mc.Mains = mains;
            if (mc.Rounds < 1)
            {
                throw new BracketException("mains: need at least 1 round");
            }
            if (mc.Direction != MainsAscending && mc.Direction != MainsDescending)
            {
                throw new BracketException("mains: direction must be \"ascending\" or \"descending\"");
            }
            if (mc.DropWorst < 0)
            {
                mc.DropWorst = 0;
            }
            if (mc.DropWorst >= mc.Rounds)
            {
                throw new BracketException("mains: drop-worst (" + Go.Itoa(mc.DropWorst) + ") must be fewer than the number of rounds (" + Go.Itoa(mc.Rounds) + ")");
            }
            if (mc.PointsTable == null || mc.PointsTable.Count == 0)
            {
                throw new BracketException("mains: a points table is required");
            }
            ValidateDivisions(mc);
        }

        // ValidateDivisions checks (and normalises) the optional division grouping: every
        // division must hold at least one known main, no main may appear twice, and the
        // grouping must cover every main exactly once. Empty Divisions is valid (single
        // competition over all mains).
        internal static void ValidateDivisions(MainsConfig mc)
        {
            if (Go.Len(mc.Divisions) == 0)
            {
                return;
            }
            HashSet<string> valid = new HashSet<string>(mc.Mains);
            Dictionary<string, string> seen = new Dictionary<string, string>(); // main → division name
            List<MainsDivision> cleaned = new List<MainsDivision>();
            foreach (MainsDivision d in mc.Divisions)
            {
                string name = Go.TrimSpace(d.Name);
                List<string> dm = new List<string>();
                foreach (string raw in Go.Iter(d.Mains))
                {
                    string l = Go.TrimSpace(raw);
                    if (l == "")
                    {
                        continue;
                    }
                    if (!valid.Contains(l))
                    {
                        throw new BracketException("mains: division " + Go.Quote(name) + " references unknown main " + Go.Quote(l));
                    }
                    string prev;
                    if (seen.TryGetValue(l, out prev))
                    {
                        throw new BracketException("mains: main " + Go.Quote(l) + " is assigned to both " + Go.Quote(prev) + " and " + Go.Quote(name));
                    }
                    seen[l] = name;
                    dm.Add(l);
                }
                if (dm.Count == 0)
                {
                    throw new BracketException("mains: division " + Go.Quote(name) + " has no mains");
                }
                List<string> skills = null;
                foreach (string s in Go.Iter(d.Skills))
                {
                    string t = Go.TrimSpace(s);
                    if (t != "")
                    {
                        skills = Go.Append(skills, t);
                    }
                }
                cleaned.Add(new MainsDivision { Name = name, Mains = dm, Skills = skills, ClassId = d.ClassId });
            }
            if (seen.Count != mc.Mains.Count)
            {
                throw new BracketException("mains: divisions cover " + Go.Itoa(seen.Count) + " of " + Go.Itoa(mc.Mains.Count) + " mains — every main must be in exactly one division");
            }
            mc.Divisions = cleaned;
        }

        // MainSizes splits n pilots across m mains as evenly as possible, with the last
        // (lowest) mains absorbing any remainder.
        internal static List<int> MainSizes(int n, int m)
        {
            int baseSize = n / m;
            int rem = n % m;
            List<int> sizes = new List<int>();
            for (int i = 0; i < m; i++)
            {
                int s = baseSize;
                if (i >= m - rem)
                {
                    s++;
                }
                sizes.Add(s);
            }
            return sizes;
        }

        // MainsRosters resolves each main's pilot roster. When an explicit assignment is
        // supplied (drag-drop in the setup form) it is used verbatim after validating it
        // covers every pilot exactly once; otherwise pilots are chunked by seed (lower
        // mains absorb the remainder). Each roster is ordered by global seed.
        internal static Dictionary<string, List<string>> MainsRosters(List<string> labels, List<string> names, Dictionary<string, List<string>> explicitRosters, Dictionary<string, int> seedIdx)
        {
            Dictionary<string, List<string>> output = new Dictionary<string, List<string>>();
            if (explicitRosters != null && explicitRosters.Count > 0)
            {
                Dictionary<string, string> assigned = new Dictionary<string, string>();
                foreach (string label in labels)
                {
                    List<string> ex;
                    explicitRosters.TryGetValue(label, out ex);
                    foreach (string p in Go.Iter(ex))
                    {
                        if (!seedIdx.ContainsKey(p))
                        {
                            throw new BracketException("mains: assignment references unknown pilot " + Go.Quote(p));
                        }
                        string prev;
                        if (assigned.TryGetValue(p, out prev))
                        {
                            throw new BracketException("mains: pilot " + Go.Quote(p) + " assigned to both " + prev + " and " + label);
                        }
                        assigned[p] = label;
                    }
                }
                if (assigned.Count != names.Count)
                {
                    throw new BracketException("mains: assignment covers " + Go.Itoa(assigned.Count) + " of " + Go.Itoa(names.Count) + " pilots");
                }
                foreach (string label in labels)
                {
                    List<string> ex;
                    explicitRosters.TryGetValue(label, out ex);
                    List<string> roster = Go.Copy(ex);
                    if (roster != null)
                    {
                        Go.SortStable(roster, (a, b) => seedIdx[a] < seedIdx[b]);
                    }
                    if (Go.Len(roster) < 1)
                    {
                        throw new BracketException("mains: " + label + " main has no pilots");
                    }
                    output[label] = roster;
                }
                return output;
            }
            List<int> sizes = MainSizes(names.Count, labels.Count);
            int cursor = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                output[labels[i]] = Go.Copy(names.GetRange(cursor, sizes[i]));
                cursor += sizes[i];
            }
            return output;
        }

        public static MainsFormat FromState(FormatConfig cfg, string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return New(cfg);
            }
            return new MainsFormat(Decode(raw));
        }

        private static MainsState Decode(string raw)
        {
            MainsState st;
            try
            {
                st = BracketJson.Deserialize<MainsState>(raw);
            }
            catch (JsonException e)
            {
                throw new BracketException(e.Message);
            }
            if (st == null)
            {
                st = new MainsState();
            }
            if (st.Cfg == null)
            {
                st.Cfg = new MainsConfig();
            }
            return st;
        }

        public string Kind
        {
            get { return FormatKinds.Mains; }
        }

        public string LayoutKind
        {
            get { return "mains-table"; }
        }

        public MainsState State()
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

        // FindRace returns the race and its owning main by id.
        internal MainsRace FindRace(string id, out Main owner)
        {
            foreach (Main main in Go.Iter(state.Mains))
            {
                foreach (MainsRace r in Go.Iter(main.Races))
                {
                    if (r.Id == id)
                    {
                        owner = main;
                        return r;
                    }
                }
            }
            owner = null;
            return null;
        }

        public IReadOnlyList<RaceView> Races()
        {
            // Map each main label to its (optional, named) division so titles can be
            // prefixed with the division when a day runs more than one competition.
            Dictionary<string, string> divByMain = new Dictionary<string, string>();
            foreach (MainsDivision d in ResolveDivisions())
            {
                foreach (string label in Go.Iter(d.Mains))
                {
                    divByMain[label] = d.Name;
                }
            }
            List<RaceView> output = new List<RaceView>();
            foreach (Main main in Go.Iter(state.Mains))
            {
                foreach (MainsRace r in Go.Iter(main.Races))
                {
                    List<string> pilots = r.Pilots;
                    if (r.IsComplete && Go.Len(r.Placement) > 0)
                    {
                        pilots = r.Placement;
                    }
                    string title = main.Label + " Main — Round " + Go.Itoa(r.Round);
                    string div;
                    if (divByMain.TryGetValue(main.Label, out div) && !string.IsNullOrEmpty(div))
                    {
                        title = div + " · " + title;
                    }
                    output.Add(new RaceView
                    {
                        Id = r.Id,
                        Round = r.Round,
                        GroupLabel = main.Label + " Main · Round " + Go.Itoa(r.Round),
                        Title = title,
                        Pilots = Go.Copy(pilots),
                        IsComplete = r.IsComplete,
                        FullPlacement = r.IsComplete,
                        ExpectedWinners = Go.Len(r.Pilots), // rank everyone
                        CanEdit = r.IsComplete,             // rounds are independent — always editable
                    });
                }
            }
            return output;
        }

        public void ApplyPlacement(string raceId, IList<string> placement)
        {
            Main owner;
            MainsRace r = FindRace(raceId, out owner);
            if (r == null)
            {
                throw new BracketException("mains: race " + raceId + " not found");
            }
            if (r.IsComplete)
            {
                throw new BracketException("mains: race " + raceId + " already complete (use edit)");
            }
            string err = FormatUtil.SamePilotSet(r.Pilots, placement);
            if (err != null)
            {
                throw new BracketException("mains: " + raceId + " placement " + err);
            }
            r.Placement = Go.Copy(placement);
            r.IsComplete = true;
        }

        // CanEditRace is always allowed for mains — rounds have no downstream dependency.
        public bool CanEditRace(string raceId, out string reason)
        {
            Main owner;
            if (FindRace(raceId, out owner) == null)
            {
                reason = "race not found";
                return false;
            }
            reason = "";
            return true;
        }

        public void EditRace(string raceId, IList<string> placement)
        {
            Main owner;
            MainsRace r = FindRace(raceId, out owner);
            if (r == null)
            {
                throw new BracketException("mains: race " + raceId + " not found");
            }
            string err = FormatUtil.SamePilotSet(r.Pilots, placement);
            if (err != null)
            {
                throw new BracketException("mains: " + raceId + " placement " + err);
            }
            r.Placement = Go.Copy(placement);
            r.IsComplete = true;
        }

        /// <summary>
        /// ResetRace un-runs a completed round, clearing its placement/points so it
        /// becomes pending. Mains rounds have no downstream dependency. No-op if not run.
        /// </summary>
        public void ResetRace(string raceId)
        {
            Main owner;
            MainsRace r = FindRace(raceId, out owner);
            if (r == null)
            {
                throw new BracketException("mains: race " + raceId + " not found");
            }
            r.Placement = null;
            r.IsComplete = false;
        }

        // RoundScore is one completed round's result for a pilot.
        private class RoundScore
        {
            public int Round;
            public int Pts;
            public int Pos; // finishing position that round (1-based)
        }

        // PilotStats holds a pilot's accumulated scoring within a main.
        private class PilotStats
        {
            public string Pilot;
            public List<RoundScore> Rounds = new List<RoundScore>(); // completed rounds, in round order
            public int Total;                                        // counted total after drop-worst
            public HashSet<int> Dropped = new HashSet<int>();        // round numbers discounted by drop-worst
            public int SeedIdx;                                      // seeding order — the sole tie-break
        }

        private int Points(int pos)
        {
            int v;
            if (state.Cfg.PointsTable != null && state.Cfg.PointsTable.TryGetValue(pos, out v))
            {
                return v;
            }
            return 0;
        }

        // RankedStats computes each pilot's accumulated scoring within one main and
        // returns them in finishing order (best first): counted points (worst N rounds
        // dropped), ties broken by seeding order. Shared by RankMain (Detail strings) and
        // MainScoreboard (structured per-round cells) so they can't drift.
        private List<PilotStats> RankedStats(Main main)
        {
            return RankedStatsThrough(main, 0);
        }

        // RankedStatsThrough is RankedStats counting only completed rounds with
        // round <= maxRound (maxRound <= 0 = every completed round). It backs the
        // round-over-round position-movement deltas on the OBS scoreboard overlays: the
        // drop-worst maths is re-run over the capped round set so the prior standings are
        // exactly what they were before the latest round, not an approximation.
        private List<PilotStats> RankedStatsThrough(Main main, int maxRound)
        {
            bool asc = state.Cfg.Direction == MainsAscending;
            // Tie-break seed = global grid position (so a manually-placed pilot still
            // ranks by their real seed). Falls back to in-main order for legacy state.
            Dictionary<string, int> seed = new Dictionary<string, int>();
            for (int i = 0; i < Go.Len(state.SeedOrder); i++)
            {
                seed[state.SeedOrder[i]] = i;
            }
            Dictionary<string, PilotStats> stats = new Dictionary<string, PilotStats>();
            for (int i = 0; i < Go.Len(main.Pilots); i++)
            {
                string p = main.Pilots[i];
                int si;
                if (!seed.TryGetValue(p, out si))
                {
                    si = i;
                }
                stats[p] = new PilotStats { Pilot = p, SeedIdx = si };
            }
            foreach (MainsRace r in Go.Iter(main.Races))
            {
                if (!r.IsComplete)
                {
                    continue;
                }
                if (maxRound > 0 && r.Round > maxRound)
                {
                    continue;
                }
                for (int pos = 0; pos < Go.Len(r.Placement); pos++)
                {
                    PilotStats s;
                    if (stats.TryGetValue(r.Placement[pos], out s) && s != null)
                    {
                        s.Rounds.Add(new RoundScore { Round = r.Round, Pts = Points(pos + 1), Pos = pos + 1 });
                    }
                }
            }
            foreach (PilotStats s in stats.Values)
            {
                ApplyDrops(s, asc);
            }

            List<PilotStats> ranked = new List<PilotStats>();
            foreach (string p in Go.Iter(main.Pilots))
            {
                ranked.Add(stats[p]);
            }
            Go.SortStable(ranked, (a, b) => CompareStats(a, b, asc) < 0);
            return ranked;
        }

        // RankMain computes the ordered standings within one main: counted points
        // (worst N rounds dropped), ties broken by seeding order. The list is in
        // finishing order (best first).
        internal List<Standing> RankMain(Main main)
        {
            List<PilotStats> ranked = RankedStats(main);
            List<Standing> output = new List<Standing>();
            foreach (PilotStats s in ranked)
            {
                output.Add(new Standing { Pilot = s.Pilot, Detail = Detail(s) });
            }
            return output;
        }

        // ApplyDrops discounts the pilot's worst DropWorst completed rounds (capped so at
        // least one round always counts) and sets the counted total. "Worst" = lowest
        // points when high-is-best (descending), highest points when low-is-best (ascending).
        private void ApplyDrops(PilotStats s, bool asc)
        {
            s.Total = 0;
            foreach (RoundScore rs in s.Rounds)
            {
                s.Total += rs.Pts;
            }
            int drop = state.Cfg.DropWorst;
            int max = s.Rounds.Count - 1;
            if (drop > max)
            {
                drop = max;
            }
            if (drop <= 0)
            {
                return;
            }
            List<RoundScore> order = new List<RoundScore>(s.Rounds);
            Go.SortStable(order, (a, b) =>
            {
                if (asc)
                {
                    return a.Pts > b.Pts; // ascending scoring: worst = highest points
                }
                return a.Pts < b.Pts; // descending scoring: worst = lowest points
            });
            for (int i = 0; i < drop; i++)
            {
                s.Total -= order[i].Pts;
                s.Dropped.Add(order[i].Round);
            }
        }

        // CompareStats returns <0 if a ranks ahead of b, else >0. Counted points decide
        // (per direction); ties are broken purely by seeding order (lower seed index
        // first), which is unique per pilot so there are never unresolved ties.
        private static int CompareStats(PilotStats a, PilotStats b, bool asc)
        {
            if (a.Total != b.Total)
            {
                if (asc)
                {
                    return a.Total - b.Total; // lower is better
                }
                return b.Total - a.Total; // higher is better
            }
            return a.SeedIdx - b.SeedIdx; // tie → higher seed (lower index) wins
        }

        private static string Detail(PilotStats s)
        {
            StringBuilder b = new StringBuilder();
            b.Append(Go.Itoa(s.Total));
            b.Append(" pts");
            if (s.Rounds.Count > 0)
            {
                b.Append(" (");
                for (int i = 0; i < s.Rounds.Count; i++)
                {
                    RoundScore rs = s.Rounds[i];
                    if (i > 0)
                    {
                        b.Append(" ");
                    }
                    if (s.Dropped.Contains(rs.Round))
                    {
                        b.Append("✗"); // discounted by drop-worst
                    }
                    b.Append("R");
                    b.Append(Go.Itoa(rs.Round));
                    b.Append(":");
                    b.Append(Go.Itoa(rs.Pts));
                }
                b.Append(")");
            }
            return b.ToString();
        }

        // ResolveDivisions returns the effective division grouping. When none are
        // configured, all mains form one unnamed division (the single-competition case),
        // preserving the original A-main-wins-overall behaviour.
        private List<MainsDivision> ResolveDivisions()
        {
            if (Go.Len(state.Cfg.Divisions) > 0)
            {
                return state.Cfg.Divisions;
            }
            List<string> labels = new List<string>();
            foreach (Main main in Go.Iter(state.Mains))
            {
                labels.Add(main.Label);
            }
            return new List<MainsDivision> { new MainsDivision { Name = "", Mains = labels } };
        }

        private Main MainByLabel(string label)
        {
            foreach (Main main in Go.Iter(state.Mains))
            {
                if (main.Label == label)
                {
                    return main;
                }
            }
            return null;
        }

        /// <summary>
        /// Standings concatenates each main's ranking (top main first), grouped by
        /// division order, with a continuous global rank. Pilots in lower mains always
        /// rank below the main above.
        /// </summary>
        public IReadOnlyList<Standing> Standings()
        {
            List<Standing> output = new List<Standing>();
            int rank = 0;
            foreach (MainsDivision d in ResolveDivisions())
            {
                foreach (string label in Go.Iter(d.Mains))
                {
                    Main main = MainByLabel(label);
                    if (main == null)
                    {
                        continue;
                    }
                    foreach (Standing s in RankMain(main))
                    {
                        rank++;
                        s.Rank = rank;
                        output.Add(s);
                    }
                }
            }
            return output;
        }

        /// <summary>MainStandings returns the per-main rankings for table rendering, in main order.</summary>
        public List<MainStandings> MainStandings()
        {
            List<MainStandings> output = new List<MainStandings>();
            foreach (Main main in Go.Iter(state.Mains))
            {
                output.Add(new MainStandings { Label = main.Label, Standings = RankMain(main) });
            }
            return output;
        }

        /// <summary>
        /// MainScoreboard returns the structured scoreboard for one main. An empty label
        /// selects the first main; returns false when label names no existing main.
        /// </summary>
        public bool MainScoreboard(string label, out MainScoreboard scoreboard)
        {
            Main main = null;
            if (string.IsNullOrEmpty(label))
            {
                if (Go.Len(state.Mains) > 0)
                {
                    main = state.Mains[0];
                }
            }
            else
            {
                main = MainByLabel(label);
            }
            if (main == null)
            {
                scoreboard = new MainScoreboard();
                return false;
            }
            int rounds = state.Cfg.Rounds;
            MainScoreboard sb = new MainScoreboard { Label = main.Label, Rounds = rounds };
            foreach (PilotStats s in RankedStats(main))
            {
                MainScoreRow row = new MainScoreRow { Pilot = s.Pilot, Total = s.Total, Cells = new List<MainRoundCell>() };
                for (int i = 0; i < rounds; i++)
                {
                    row.Cells.Add(new MainRoundCell { Round = i + 1 });
                }
                foreach (RoundScore rs in s.Rounds)
                {
                    if (rs.Round >= 1 && rs.Round <= rounds)
                    {
                        row.Cells[rs.Round - 1] = new MainRoundCell
                        {
                            Round = rs.Round,
                            Points = rs.Pts,
                            Position = rs.Pos,
                            HasResult = true,
                            Dropped = s.Dropped.Contains(rs.Round),
                        };
                    }
                }
                sb.Rows = Go.Append(sb.Rows, row);
            }
            scoreboard = sb;
            return true;
        }

        /// <summary>
        /// MainRanksThrough returns each pilot's 1-based finishing rank within a main
        /// counting only completed rounds up to and including maxRound (maxRound <= 0 =
        /// all rounds). Only pilots with at least one counted round in that window appear,
        /// so a pilot who first scored after maxRound is absent (the OBS overlay treats
        /// that as "new"). It powers the scoreboards' round-over-round movement arrows:
        /// compare the live rank to MainRanksThrough(label, latestRound-1). An empty label
        /// selects the first main; null when the label names no main.
        /// </summary>
        public Dictionary<string, int> MainRanksThrough(string label, int maxRound)
        {
            Main main = null;
            if (string.IsNullOrEmpty(label))
            {
                if (Go.Len(state.Mains) > 0)
                {
                    main = state.Mains[0];
                }
            }
            else
            {
                main = MainByLabel(label);
            }
            if (main == null)
            {
                return null;
            }
            List<PilotStats> ranked = RankedStatsThrough(main, maxRound);
            Dictionary<string, int> output = new Dictionary<string, int>();
            int rank = 0;
            foreach (PilotStats s in ranked)
            {
                if (s.Rounds.Count == 0)
                {
                    continue; // no counted round in this window — not yet ranked
                }
                rank++;
                output[s.Pilot] = rank;
            }
            return output;
        }

        /// <summary>DivisionStandings — see the DivisionStandings class.</summary>
        public List<DivisionStandings> DivisionStandings()
        {
            List<MainsDivision> divs = ResolveDivisions();
            List<DivisionStandings> output = new List<DivisionStandings>();
            foreach (MainsDivision d in divs)
            {
                DivisionStandings ds = new DivisionStandings { Name = d.Name, ClassId = d.ClassId, Complete = true };
                for (int i = 0; i < Go.Len(d.Mains); i++)
                {
                    Main main = MainByLabel(d.Mains[i]);
                    if (main == null)
                    {
                        continue;
                    }
                    List<Standing> ranked = RankMain(main);
                    ds.Mains = Go.Append(ds.Mains, new MainStandings { Label = main.Label, Standings = ranked });
                    bool complete = MainComplete(main);
                    if (!complete)
                    {
                        ds.Complete = false;
                    }
                    if (i == 0 && complete && ranked.Count > 0) // top main decides the champion
                    {
                        ds.Champion = ranked[0].Pilot;
                    }
                }
                output.Add(ds);
            }
            return output;
        }

        // MainComplete reports whether every round of a main has been recorded.
        private static bool MainComplete(Main main)
        {
            foreach (MainsRace r in Go.Iter(main.Races))
            {
                if (!r.IsComplete)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Champion is the overall winner. It exists only for the single-competition case
        /// (no configured divisions, or a lone division) — the winner of the top main once
        /// all its rounds are complete. With multiple divisions there is no single overall
        /// champion (each division has its own — see DivisionStandings).
        /// </summary>
        public string Champion()
        {
            List<MainsDivision> divs = ResolveDivisions();
            if (divs.Count != 1 || Go.Len(divs[0].Mains) == 0)
            {
                return "";
            }
            Main top = MainByLabel(divs[0].Mains[0]);
            if (top == null || !MainComplete(top))
            {
                return "";
            }
            List<Standing> ranked = RankMain(top);
            if (ranked.Count == 0)
            {
                return "";
            }
            return ranked[0].Pilot;
        }

        /// <summary>IsComplete is true once every round of every main has been recorded.</summary>
        public bool IsComplete()
        {
            foreach (Main main in Go.Iter(state.Mains))
            {
                foreach (MainsRace r in Go.Iter(main.Races))
                {
                    if (!r.IsComplete)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
