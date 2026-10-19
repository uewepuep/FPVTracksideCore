using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace SeederEngine.Tests
{
    /// <summary>
    /// Golden parity: replays every scenario recorded from the AUFPV platform's Go
    /// engine (Fixtures/*.json.gz, written by a throwaway Go generator) with the same
    /// deterministic policy, and asserts every recorded value — state blob, race views,
    /// standings, champion, completeness, CanEditRace, error messages, engine internals,
    /// BracketFlow outputs and the public-diagram projection — matches structurally.
    /// </summary>
    public class GoldenParityTests
    {
        private static string FixtureDir()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string cand = Path.Combine(dir, "Fixtures");
                if (Directory.Exists(cand) && Directory.GetFiles(cand, "*.json.gz").Length > 0)
                {
                    return cand;
                }
                cand = Path.Combine(dir, "Seeder.Tests", "Fixtures");
                if (Directory.Exists(cand) && Directory.GetFiles(cand, "*.json.gz").Length > 0)
                {
                    return cand;
                }
                dir = Path.GetDirectoryName(dir);
            }
            throw new InvalidOperationException("Fixtures directory not found");
        }

        public static IEnumerable<object[]> Scenarios()
        {
            foreach (string f in Directory.GetFiles(FixtureDir(), "*.json.gz").OrderBy(x => x, StringComparer.Ordinal))
            {
                yield return new object[] { Path.GetFileName(f) };
            }
        }

        [Fact]
        public void FixturesArePresent()
        {
            Assert.True(Scenarios().Count() > 500, "expected the golden fixture set");
        }

        /// <summary>
        /// Single elimination, 9 pilots, 4-up, 3 advance never finishes in Go (a Go engine
        /// bug, kept for parity): round 1 is three heats of three that each advance ONE;
        /// race-1's winner P1 is never placed (R2-1 stays empty), and R2-2 holds two pilots
        /// who both "advance", so every round spawns another identical two-pilot heat. The
        /// golden matrix excludes such configs (Go only stops at the simulation's
        /// 4000-iteration safety cap); this pins the port to the first 12 selections,
        /// transcribed from the Go engine.
        /// </summary>
        [Fact]
        public void NonTerminatingSingleElimMatchesGo()
        {
            List<string> names = Enumerable.Range(1, 9).Select(i => "P" + i).ToList();
            BracketConfig cfg = new BracketConfig
            {
                PilotCount = 9, PilotNames = names, EliminationType = EliminationType.Single,
                PilotsPerRace = 4, PilotsToAdvance = 3, FinalsFormat = FinalsFormat.GrandFinal,
            };
            Assert.False(Driver.ElimTerminates(cfg));
            Bracket b = Bracket.New(cfg);
            Func<string, int> seed = BracketFlow.SeedRanks(b);
            string[] wantPicks = { "race-1", "race-3", "race-2", "R2-2", "R3-1", "R4-1", "R5-1", "R6-1", "R7-1", "R8-1", "R9-1", "R10-1" };
            int[] wantRaces = { 3, 5, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
            for (int it = 0; it < 12; it++)
            {
                Race pick = null;
                foreach (Race r in b.State().Races)
                {
                    if (r.IsComplete || Go.Len(r.Pilots) == 0)
                    {
                        continue;
                    }
                    if (pick == null || r.Round < pick.Round)
                    {
                        pick = r;
                    }
                }
                Assert.Equal(wantPicks[it], pick.Id);
                List<string> ord = new List<string>(pick.Pilots);
                Go.SortStable(ord, (x, y) => seed(x) < seed(y));
                b.SelectWinners(pick.Id, ord.GetRange(0, Math.Min(b.ExpectedWinnersCount(pick), ord.Count)));
                Assert.Equal(wantRaces[it], b.State().Races.Count);
            }
            string got = string.Join("|", b.State().Races.Select(r => r.Id + " " + r.Round + " " + string.Join(",", Go.Iter(r.Pilots)) + " " + r.IsComplete));
            Assert.Equal("race-1 1 P1,P6,P7 True|race-3 1 P2,P5,P8 True|race-2 1 P3,P4,P9 True|R2-1 2  False|R2-2 2 P2,P3 True|"
                + "R3-1 3 P2,P3 True|R4-1 4 P2,P3 True|R5-1 5 P2,P3 True|R6-1 6 P2,P3 True|R7-1 7 P2,P3 True|R8-1 8 P2,P3 True|"
                + "R9-1 9 P2,P3 True|R10-1 10 P2,P3 True|R11-1 11 P2,P3 False", got);
        }

        [Theory]
        [MemberData(nameof(Scenarios))]
        public void Replay(string file)
        {
            JObject sc;
            using (FileStream fs = File.OpenRead(Path.Combine(FixtureDir(), file)))
            using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
            using (StreamReader sr = new StreamReader(gz))
            using (JsonTextReader jr = new JsonTextReader(sr) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal })
            {
                sc = JObject.Load(jr);
            }
            // Go iterates a map before auto-advancing byes, so a few scenarios have more
            // than one valid outcome; the generator stores every one it saw in "alt".
            // The port is deterministic and must reproduce one of them exactly.
            List<JArray> variants = new List<JArray> { (JArray)sc["steps"] };
            if (sc["alt"] is JArray alt)
            {
                variants.AddRange(alt.Cast<JArray>());
            }
            Exception first = null;
            foreach (JArray steps in variants)
            {
                try
                {
                    new Driver(sc, steps).Run();
                    return;
                }
                catch (Exception e) when (e is Xunit.Sdk.XunitException)
                {
                    first = first ?? e;
                }
            }
            throw new Xunit.Sdk.XunitException((variants.Count > 1 ? "matched none of " + variants.Count + " Go variants; first: " : "") + first.Message);
        }

        // ---------------- deterministic PRNG (mirror of the Go generator) ----------------

        private class Rng
        {
            private ulong x;

            public Rng(ulong seed)
            {
                x = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
            }

            public ulong Next()
            {
                ulong v = x;
                v ^= v << 13;
                v ^= v >> 7;
                v ^= v << 17;
                x = v;
                return v;
            }

            public int Intn(int n)
            {
                if (n <= 0)
                {
                    return 0;
                }
                return (int)(Next() % (ulong)n);
            }
        }

        // ---------------- driver (mirror of the Go generator's driver) ----------------

        internal class Driver
        {
            private readonly JObject sc;
            private readonly JArray steps;
            private readonly string kind;
            private readonly FormatConfig cfg;
            private readonly string order;
            private readonly bool partial, mutate, pickRandom;
            private readonly int rehydrateEvery, stateEvery, flowAt;
            private readonly Rng r;
            private readonly Dictionary<string, int> seedIdx = new Dictionary<string, int>();
            private readonly Dictionary<string, int> seeds = new Dictionary<string, int>();
            private IRaceFormat f;
            private int stepNo; // index of the next recorded step
            private int actions, mutations;

            public Driver(JObject scenario, JArray stepList = null)
            {
                sc = scenario;
                steps = stepList ?? (JArray)sc["steps"];
                kind = (string)sc["kind"];
                cfg = sc["config"].ToObject<FormatConfig>();
                JObject p = (JObject)sc["policy"];
                order = (string)p["order"];
                partial = (bool)p["partial"];
                mutate = (bool)p["mutate"];
                pickRandom = (bool)p["pickRandom"];
                rehydrateEvery = (int)p["rehydrateEvery"];
                stateEvery = (int)p["stateEvery"];
                flowAt = (int)p["flowAt"];
                r = new Rng(ulong.Parse(p["seed"].ToString(), CultureInfo.InvariantCulture));
                List<string> names = cfg.PilotNames ?? new List<string>();
                for (int i = 0; i < names.Count; i++)
                {
                    if (!seedIdx.ContainsKey(names[i]))
                    {
                        seedIdx[names[i]] = i;
                        seeds[names[i]] = i + 1;
                    }
                }
            }

            private string Where => (string)sc["name"] + " step " + stepNo;

            public void Run()
            {
                string createErr = (string)sc["createErr"] ?? "";
                try
                {
                    f = RaceFormats.Create(kind, cfg);
                }
                catch (BracketException e)
                {
                    Assert.True(createErr == e.Message, Where + ": create error\n  go: " + createErr + "\n  c#: " + e.Message);
                    Assert.Empty(steps);
                    return;
                }
                Assert.True(createErr == "", Where + ": Go failed to create (" + createErr + ") but C# succeeded");
                Record("create", "", null, null, true);
                Loop();
                Assert.True(stepNo == steps.Count, (string)sc["name"] + ": C# recorded " + stepNo + " steps, Go recorded " + steps.Count);
            }

            private static int TrailingInt(string id)
            {
                int i = id.LastIndexOf('-');
                if (i < 0)
                {
                    return 0;
                }
                int n;
                return Go.Atoi(id.Substring(i + 1), out n) ? n : 0;
            }

            private static void SortViews(List<RaceView> vs)
            {
                Go.SortStable(vs, (a, b) =>
                {
                    if (a.Round != b.Round)
                    {
                        return a.Round < b.Round;
                    }
                    int ta = TrailingInt(a.Id), tb = TrailingInt(b.Id);
                    if (ta != tb)
                    {
                        return ta < tb;
                    }
                    return string.CompareOrdinal(a.Id, b.Id) < 0;
                });
            }

            private int SeedKey(string p)
            {
                int s;
                return p != null && seedIdx.TryGetValue(p, out s) ? s : 1 << 30;
            }

            private List<string> Order(List<string> pilots)
            {
                List<string> output = new List<string>(Go.Iter(pilots));
                switch (order)
                {
                    case "reverse":
                        Go.SortStable(output, (a, b) => SeedKey(a) > SeedKey(b));
                        break;
                    case "random":
                        for (int i = output.Count - 1; i > 0; i--)
                        {
                            int j = r.Intn(i + 1);
                            string t = output[i];
                            output[i] = output[j];
                            output[j] = t;
                        }
                        break;
                    default:
                        Go.SortStable(output, (a, b) => SeedKey(a) < SeedKey(b));
                        break;
                }
                return output;
            }

            private List<string> Shuffle(List<string> input)
            {
                List<string> output = new List<string>(Go.Iter(input));
                for (int i = output.Count - 1; i > 0; i--)
                {
                    int j = r.Intn(i + 1);
                    string t = output[i];
                    output[i] = output[j];
                    output[j] = t;
                }
                return output;
            }

            private bool WantState()
            {
                return stateEvery > 0 && stepNo % stateEvery == 0;
            }

            private static string Try(Action a)
            {
                try
                {
                    a();
                    return null;
                }
                catch (BracketException e)
                {
                    return e.Message;
                }
            }

            private string Apply(string race, List<string> placement)
            {
                string err = Try(() => f.ApplyPlacement(race, placement));
                actions++;
                Record("apply", race, placement, err, WantState());
                MaybeRehydrate();
                return err;
            }

            private void MaybeRehydrate()
            {
                if (rehydrateEvery <= 0 || actions % rehydrateEvery != 0)
                {
                    return;
                }
                string raw = f.StateJson();
                IRaceFormat nf = null;
                string err = Try(() => nf = RaceFormats.FromState(kind, cfg, raw));
                if (err != null)
                {
                    Record("rehydrate", "", null, err, true);
                    return;
                }
                f = nf;
                Record("rehydrate", "", null, null, true);
            }

            private void Loop()
            {
                HashSet<string> stuck = new HashSet<string>();
                for (int step = 0; step < 3000; step++)
                {
                    List<RaceView> cands = new List<RaceView>();
                    foreach (RaceView v in f.Races())
                    {
                        if (v.IsComplete || v.IsVoid || v.Locked || Go.Len(v.Pilots) == 0 || stuck.Contains(v.Id))
                        {
                            continue;
                        }
                        cands.Add(v);
                    }
                    if (cands.Count == 0)
                    {
                        break;
                    }
                    SortViews(cands);
                    RaceView pick = cands[0];
                    if (pickRandom)
                    {
                        int lowest = cands.Count(c => c.Round == cands[0].Round);
                        pick = cands[r.Intn(lowest)];
                    }
                    List<string> placement = Order(pick.Pilots);
                    string err;
                    if (partial && r.Intn(3) == 0 && pick.ExpectedWinners > 0 && pick.ExpectedWinners < placement.Count)
                    {
                        err = Apply(pick.Id, placement.GetRange(0, pick.ExpectedWinners));
                        if (err != null)
                        {
                            err = Apply(pick.Id, placement);
                        }
                    }
                    else
                    {
                        err = Apply(pick.Id, placement);
                    }
                    if (err != null)
                    {
                        stuck.Add(pick.Id);
                    }
                    if (actions == flowAt)
                    {
                        Record("checkpoint", "", null, null, true);
                    }
                    if (mutate && mutations < 8)
                    {
                        Mutate(pick);
                    }
                }
                Record("final", "", null, null, true);
            }

            private void Mutate(RaceView pick)
            {
                int which = r.Intn(8);
                IReadOnlyList<RaceView> views = f.Races();
                List<RaceView> doneOK = new List<RaceView>(), doneLocked = new List<RaceView>(), open = new List<RaceView>();
                foreach (RaceView v in views)
                {
                    if (v.IsComplete)
                    {
                        (v.CanEdit ? doneOK : doneLocked).Add(v);
                    }
                    else
                    {
                        open.Add(v);
                    }
                }
                switch (which)
                {
                    case 0:
                    {
                        mutations++;
                        List<string> pl = new List<string> { "__nobody__" };
                        pl.AddRange(Go.Iter(pick.Pilots));
                        string err = Try(() => f.ApplyPlacement(pick.Id, pl));
                        Record("apply", pick.Id, pl, err, false);
                        break;
                    }
                    case 1:
                    {
                        if (doneOK.Count == 0)
                        {
                            return;
                        }
                        mutations++;
                        RaceView v = doneOK[r.Intn(doneOK.Count)];
                        List<string> pl = Shuffle(v.Pilots);
                        string err = Try(() => f.EditRace(v.Id, pl));
                        Record("edit", v.Id, pl, err, true);
                        break;
                    }
                    case 2:
                    {
                        if (doneOK.Count == 0)
                        {
                            return;
                        }
                        mutations++;
                        RaceView v = doneOK[r.Intn(doneOK.Count)];
                        string err = Try(() => f.ResetRace(v.Id));
                        Record("reset", v.Id, null, err, true);
                        break;
                    }
                    case 3:
                    {
                        if (doneLocked.Count == 0)
                        {
                            return;
                        }
                        mutations++;
                        RaceView v = doneLocked[r.Intn(doneLocked.Count)];
                        List<string> pl = Shuffle(v.Pilots);
                        string err = Try(() => f.EditRace(v.Id, pl));
                        Record("edit", v.Id, pl, err, false);
                        break;
                    }
                    case 4:
                    {
                        mutations++;
                        List<string> pl = new List<string> { "x" };
                        string err = Try(() => f.ApplyPlacement("NOPE-9", pl));
                        Record("apply", "NOPE-9", pl, err, false);
                        break;
                    }
                    case 5:
                    {
                        if (open.Count == 0)
                        {
                            return;
                        }
                        mutations++;
                        RaceView v = open[r.Intn(open.Count)];
                        List<string> pl = Shuffle(v.Pilots);
                        string err = Try(() => f.ApplyPlacement(v.Id, pl));
                        Record("apply", v.Id, pl, err, true);
                        break;
                    }
                    case 6:
                    case 7:
                    {
                        // A pilot who isn't in the race (6) or a duplicated pilot (7), on an open race.
                        List<RaceView> cand = open.Where(x => Go.Len(x.Pilots) >= 2).ToList();
                        if (cand.Count == 0)
                        {
                            return;
                        }
                        mutations++;
                        RaceView v = cand[r.Intn(cand.Count)];
                        List<string> pl = Shuffle(v.Pilots);
                        if (which == 6)
                        {
                            pl[0] = "__nobody__";
                        }
                        else
                        {
                            pl[1] = pl[0];
                        }
                        string err = Try(() => f.ApplyPlacement(v.Id, pl));
                        Record("apply", v.Id, pl, err, true);
                        break;
                    }
                }
            }

            private void Record(string op, string race, List<string> placement, string err, bool full)
            {
                Assert.True(stepNo < steps.Count, Where + ": C# took an extra step (" + op + " " + race + ") Go never recorded");
                JObject want = (JObject)steps[stepNo];
                string wantOp = (string)want["op"];
                string wantRace = (string)want["race"] ?? "";
                string wantErr = (string)want["err"] ?? "";
                Assert.True(op == wantOp && race == wantRace, Where + ": op mismatch go=" + wantOp + " " + wantRace + " c#=" + op + " " + race);
                JToken wantPl = want["placement"];
                JToken gotPl = placement == null || placement.Count == 0 ? null : JArray.FromObject(placement);
                string diff = Diff(wantPl ?? JValue.CreateNull(), gotPl ?? JValue.CreateNull(), "placement");
                Assert.True(diff == null, Where + ": " + diff);
                Assert.True(wantErr == (err ?? ""), Where + " (" + op + " " + race + "): error mismatch\n  go: " + wantErr + "\n  c#: " + err);
                bool heavy = op == "create" || op == "checkpoint" || op == "final";
                JObject got = Snapshot(full, heavy);
                diff = Diff(want["snap"], got, "snap");
                Assert.True(diff == null, Where + " (" + op + " " + race + "): " + diff);
                stepNo++;
            }

            // ---------------- snapshot (mirror of the Go generator) ----------------

            private static JToken Strs(IEnumerable<string> s)
            {
                return s == null ? (JToken)JValue.CreateNull() : new JArray(s.Select(x => (JToken)x));
            }

            private static JArray Views(IReadOnlyList<RaceView> vs)
            {
                JArray a = new JArray();
                foreach (RaceView v in vs)
                {
                    a.Add(new JObject
                    {
                        ["id"] = v.Id, ["round"] = v.Round, ["group"] = v.GroupLabel, ["title"] = v.Title,
                        ["pilots"] = Strs(v.Pilots), ["winners"] = Strs(v.Winners), ["losers"] = Strs(v.Losers),
                        ["complete"] = v.IsComplete, ["full"] = v.FullPlacement, ["void"] = v.IsVoid,
                        ["expected"] = v.ExpectedWinners, ["canEdit"] = v.CanEdit, ["locked"] = v.Locked, ["lockedReason"] = v.LockedReason,
                    });
                }
                return a;
            }

            private static JArray Standings(IEnumerable<Standing> ss)
            {
                JArray a = new JArray();
                foreach (Standing s in Go.Iter(ss))
                {
                    a.Add(new JObject { ["rank"] = s.Rank, ["pilot"] = s.Pilot, ["detail"] = s.Detail, ["tied"] = s.Tied });
                }
                return a;
            }

            private JObject Snapshot(bool full, bool heavy)
            {
                IReadOnlyList<RaceView> vs = f.Races();
                JObject snap = new JObject
                {
                    ["views"] = Views(vs),
                    ["standings"] = Standings(f.Standings()),
                    ["champion"] = f.Champion(),
                    ["complete"] = f.IsComplete(),
                    ["final"] = Standings(RaceFormats.FinalOrdering(f, seeds)),
                };
                JObject ce = new JObject();
                foreach (RaceView v in vs)
                {
                    string reason;
                    bool ok = f.CanEditRace(v.Id, out reason);
                    ce[v.Id] = new JArray(ok, reason ?? "");
                }
                {
                    string reason;
                    bool ok = f.CanEditRace("NOPE-9", out reason);
                    ce["NOPE-9"] = new JArray(ok, reason ?? "");
                }
                snap["canEdit"] = ce;
                if (full)
                {
                    snap["state"] = ParseRaw(f.StateJson());
                }
                if (heavy)
                {
                    snap["extra"] = Extra(f);
                    EliminationFormat ef = f as EliminationFormat;
                    QualifierFormat qf = f as QualifierFormat;
                    MainsProgressionFormat mf = f as MainsProgressionFormat;
                    if (ef != null)
                    {
                        snap["flow"] = FlowSnap(ef.Bracket());
                        snap["proj"] = ElimTerminates(ef.Bracket().Config())
                            ? (JToken)Proj(BracketDiagram.Elimination(ef.Bracket()))
                            : new JObject { ["nonterminating"] = true };
                    }
                    else if (qf != null)
                    {
                        Qual q = qf.Qual();
                        Bracket lb = TryWrap(q);
                        if (lb != null)
                        {
                            snap["flow"] = FlowSnap(lb);
                        }
                        snap["proj"] = Proj(BracketDiagram.Qualifier(q));
                    }
                    else if (mf != null)
                    {
                        JObject mflow = new JObject();
                        foreach (MainView v in mf.Mains().MainViews())
                        {
                            if (v.Built && v.Qual != null)
                            {
                                Bracket lb = TryWrap(v.Qual);
                                if (lb != null)
                                {
                                    mflow[v.Label] = FlowSnap(lb);
                                }
                            }
                        }
                        snap["flow"] = mflow;
                        JArray pa = new JArray();
                        foreach ((string label, BracketProjection p) in BracketDiagram.MainsProgression(mf.Mains()))
                        {
                            pa.Add(new JObject { ["label"] = label, ["p"] = Proj(p) });
                        }
                        snap["proj"] = pa;
                    }
                }
                return snap;
            }

            // Mirror of the generator's elimTerminates: some configs make the engine
            // spawn a new round forever, so a seeds-win play-through never ends.
            internal static bool ElimTerminates(BracketConfig cfg)
            {
                Bracket b;
                try
                {
                    b = Bracket.New(cfg);
                }
                catch (BracketException)
                {
                    return true;
                }
                Func<string, int> seedRank = BracketFlow.SeedRanks(b);
                for (int i = 0; i < 600; i++)
                {
                    Race pick = null;
                    foreach (Race race in Go.Iter(b.State().Races))
                    {
                        if (race.IsComplete || Go.Len(race.Pilots) == 0)
                        {
                            continue;
                        }
                        if (pick == null || race.Round < pick.Round ||
                            (race.Round == pick.Round && string.CompareOrdinal(BracketFlow.RaceSortKey(race.Id), BracketFlow.RaceSortKey(pick.Id)) < 0))
                        {
                            pick = race;
                        }
                    }
                    if (pick == null)
                    {
                        return true;
                    }
                    int expected = Math.Min(b.ExpectedWinnersCount(pick), pick.Pilots.Count);
                    List<string> ordered = new List<string>(pick.Pilots);
                    Go.SortStable(ordered, (x, y) => seedRank(x) < seedRank(y));
                    try
                    {
                        b.SelectWinners(pick.Id, ordered.GetRange(0, expected));
                    }
                    catch (BracketException)
                    {
                        return true;
                    }
                }
                return false;
            }

            private static Bracket TryWrap(Qual q)
            {
                try
                {
                    return Bracket.FromState(q.Config(), q.State());
                }
                catch (BracketException)
                {
                    return null;
                }
            }

            private static JToken ParseRaw(string json)
            {
                using (JsonTextReader jr = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal })
                {
                    return JToken.Load(jr);
                }
            }

            private static JArray Connectors(IEnumerable<BracketConnector> cs)
            {
                JArray a = new JArray();
                foreach (BracketConnector c in Go.Iter(cs))
                {
                    a.Add(new JObject { ["from"] = c.FromId, ["to"] = c.ToId, ["pilots"] = Strs(c.Pilots) });
                }
                return a;
            }

            private static JObject Chart(Dictionary<string, Dictionary<string, PilotSlot>> m)
            {
                JObject o = new JObject();
                foreach (KeyValuePair<string, Dictionary<string, PilotSlot>> race in m)
                {
                    JObject inner = new JObject();
                    foreach (KeyValuePair<string, PilotSlot> s in race.Value)
                    {
                        inner[s.Key] = new JArray(s.Value.SourceRaceId, s.Value.SourceRank);
                    }
                    o[race.Key] = inner;
                }
                return o;
            }

            private static JObject Proj(BracketProjection p)
            {
                return new JObject
                {
                    ["state"] = ParseRaw(BracketJson.Serialize(p.Projected.State())),
                    ["predicted"] = p.PredictedSlots == null ? (JToken)JValue.CreateNull() : Chart(p.PredictedSlots),
                    ["connectors"] = Connectors(p.Connectors),
                };
            }

            private static JObject FlowSnap(Bracket b)
            {
                JObject o = new JObject();
                bool nonTerm = !ElimTerminates(b.Config());
                Bracket fresh = null;
                if (nonTerm)
                {
                    o["nonterminating"] = true;
                }
                else
                {
                    try
                    {
                        fresh = Bracket.New(b.Config());
                    }
                    catch (BracketException e)
                    {
                        o["freshErr"] = e.Message;
                    }
                }
                if (fresh != null)
                {
                    List<BracketConnector> conns = BracketFlow.Simulate(fresh);
                    o["simulate"] = Connectors(conns);
                    o["simChart"] = Chart(BracketFlow.SlotChart(fresh, conns));
                    o["simState"] = ParseRaw(BracketJson.Serialize(fresh.State()));
                }
                List<BracketConnector> live = BracketFlow.ConnectorsFromState(b);
                o["fromState"] = Connectors(live);
                o["liveChart"] = Chart(BracketFlow.SlotChart(b, live));
                Dictionary<string, List<PredictedSlot>> pred = nonTerm ? null : BracketFlow.PredictedSlots(b);
                if (nonTerm)
                {
                    // skipped, as in the generator
                }
                else if (pred == null)
                {
                    o["predicted"] = null;
                }
                else
                {
                    JObject pm = new JObject();
                    foreach (KeyValuePair<string, List<PredictedSlot>> kv in pred)
                    {
                        JArray arr = new JArray();
                        foreach (PredictedSlot p in kv.Value)
                        {
                            arr.Add(new JArray(p.SourceRaceId, p.SourceRank, p.Label));
                        }
                        pm[kv.Key] = arr;
                    }
                    o["predicted"] = pm;
                }
                Func<string, int> seedRank = BracketFlow.SeedRanks(b);
                JObject nth = new JObject();
                foreach (Race r in Go.Iter(b.State().Races))
                {
                    if (!r.IsComplete)
                    {
                        continue;
                    }
                    JArray arr = new JArray();
                    for (int k = 0; k <= Go.Len(r.Pilots) + 1; k++)
                    {
                        arr.Add(BracketFlow.NthPlaceFinisher(r, k, seedRank));
                    }
                    nth[r.Id] = arr;
                }
                o["nth"] = nth;
                return o;
            }

            private static JArray Refs(IEnumerable<SlotRef> rs)
            {
                JArray a = new JArray();
                foreach (SlotRef r in Go.Iter(rs))
                {
                    a.Add(new JArray(r.FromId, r.Position));
                }
                return a;
            }

            private static JObject SpecSnap(Spec s)
            {
                JArray body = new JArray();
                foreach (SpecRace sr in Go.Iter(s.Body))
                {
                    body.Add(new JObject
                    {
                        ["id"] = sr.Id, ["round"] = sr.Round, ["slot"] = sr.Slot, ["advance"] = sr.Advance,
                        ["sources"] = Refs(sr.Sources), ["terminal"] = sr.Terminal, ["finalTarget"] = sr.FinalTarget,
                    });
                }
                return new JObject
                {
                    ["body"] = body, ["winnersFinal"] = s.WinnersFinalId, ["losersFinal"] = s.LosersFinalId,
                    ["finalists"] = Refs(s.Finalists), ["finalRound"] = s.FinalRound, ["cta"] = s.CTA,
                };
            }

            private static JObject QualSnap(Qual q, bool withSim)
            {
                QualConfig c = q.Cfg();
                JObject o = new JObject
                {
                    ["qualified"] = Strs(q.Qualified()),
                    ["ranking"] = Strs(q.Ranking()),
                    ["complete"] = q.IsComplete(),
                    ["cfg"] = new JArray(c.PilotCount, c.PilotsPerRace, c.Advance, c.Seats, c.AceWinsRequired, c.CTA, c.PhotoSeed),
                };
                JObject exp = new JObject();
                foreach (Race r in Go.Iter(q.State().Races))
                {
                    exp[r.Id] = q.ExpectedWinners(r);
                }
                o["expected"] = exp;
                if (withSim)
                {
                    o["spec"] = SpecSnap(q.Spec());
                    JObject src = new JObject();
                    foreach (KeyValuePair<string, List<SlotRef>> kv in q.Sources())
                    {
                        src[kv.Key] = Refs(kv.Value);
                    }
                    o["sources"] = src;
                    o["simulate"] = ParseRaw(BracketJson.Serialize(q.Simulate()));
                }
                return o;
            }

            private static JObject Extra(IRaceFormat f)
            {
                JObject o = new JObject { ["kind"] = f.Kind, ["layout"] = f.LayoutKind };
                if (f is EliminationFormat ef)
                {
                    Bracket b = ef.Bracket();
                    JObject crm = new JObject();
                    foreach (Race r in Go.Iter(b.State().Races))
                    {
                        crm[r.Id] = new JArray(b.CanReceiveMorePilots(r), b.ExpectedWinnersCount(r));
                    }
                    o["receive"] = crm;
                    BracketConfig c = b.Config();
                    o["cfg"] = new JArray(c.PilotCount, c.EliminationType, c.PilotsPerRace, c.PilotsToAdvance, c.FinalsFormat, c.AceWinsRequired);
                }
                else if (f is QualifierFormat qf)
                {
                    o["qual"] = QualSnap(qf.Qual(), true);
                }
                else if (f is MainsProgressionFormat mf)
                {
                    Mains m = mf.Mains();
                    JArray rows = new JArray();
                    foreach (RankRow row in Go.Iter(m.Ranking()))
                    {
                        rows.Add(new JArray(row.Pilot, row.Main, row.MainRank));
                    }
                    o["ranking"] = rows;
                    JArray mv = new JArray();
                    JObject quals = new JObject();
                    foreach (MainView v in m.MainViews())
                    {
                        mv.Add(new JObject
                        {
                            ["label"] = v.Label, ["built"] = v.Built, ["complete"] = v.Complete, ["roster"] = Strs(v.Roster),
                            ["residents"] = v.Residents, ["bumpFrom"] = v.BumpFrom, ["hasQual"] = v.Qual != null,
                        });
                        Qual q = m.Qual(v.Label);
                        if (q != null)
                        {
                            quals[v.Label] = QualSnap(q, false);
                        }
                        o["cta_" + v.Label] = Mains.CTAForMain(m.Cfg(), v.Label);
                    }
                    o["mainViews"] = mv;
                    o["quals"] = quals;
                    o["numMains"] = m.NumMains();
                    o["simulate"] = ParseRaw(BracketJson.Serialize(m.Simulate()));
                }
                else if (f is MainsFormat t)
                {
                    JArray ms = new JArray();
                    foreach (MainStandings s in Go.Iter(t.MainStandings()))
                    {
                        ms.Add(new JObject { ["label"] = s.Label, ["standings"] = Standings(s.Standings) });
                    }
                    o["mainStandings"] = ms;
                    JArray ds = new JArray();
                    foreach (DivisionStandings d in Go.Iter(t.DivisionStandings()))
                    {
                        JArray inner = new JArray();
                        foreach (MainStandings s in Go.Iter(d.Mains))
                        {
                            inner.Add(new JObject { ["label"] = s.Label, ["standings"] = Standings(s.Standings) });
                        }
                        ds.Add(new JObject { ["name"] = d.Name, ["classId"] = d.ClassId, ["champion"] = d.Champion, ["complete"] = d.Complete, ["mains"] = inner });
                    }
                    o["divisions"] = ds;
                    MainsState st = t.State();
                    List<string> labels = new List<string> { "", "ZZ" };
                    foreach (Main m in Go.Iter(st.Mains))
                    {
                        labels.Add(m.Label);
                    }
                    JObject sbs = new JObject();
                    JObject ranks = new JObject();
                    foreach (string l in labels)
                    {
                        MainScoreboard sb;
                        bool ok = t.MainScoreboard(l, out sb);
                        JArray rws = new JArray();
                        if (sb != null)
                        {
                            foreach (MainScoreRow row in Go.Iter(sb.Rows))
                            {
                                JArray cells = new JArray();
                                foreach (MainRoundCell c in Go.Iter(row.Cells))
                                {
                                    cells.Add(new JArray(c.Round, c.Points, c.Position, c.HasResult, c.Dropped));
                                }
                                rws.Add(new JObject { ["pilot"] = row.Pilot, ["total"] = row.Total, ["cells"] = cells });
                            }
                        }
                        sbs["l_" + l] = new JObject
                        {
                            ["ok"] = ok, ["label"] = sb == null ? "" : sb.Label, ["rounds"] = sb == null ? 0 : sb.Rounds, ["rows"] = rws,
                        };
                        JObject rt = new JObject();
                        int rounds = st.Cfg == null ? 0 : st.Cfg.Rounds;
                        for (int mr = 0; mr <= rounds + 1; mr++)
                        {
                            Dictionary<string, int> mrt = t.MainRanksThrough(l, mr);
                            if (mrt == null)
                            {
                                rt[Go.Itoa(mr)] = null;
                            }
                            else
                            {
                                JObject mo = new JObject();
                                foreach (KeyValuePair<string, int> kv in mrt)
                                {
                                    mo[kv.Key] = kv.Value;
                                }
                                rt[Go.Itoa(mr)] = mo;
                            }
                        }
                        ranks["l_" + l] = rt;
                    }
                    o["scoreboards"] = sbs;
                    o["ranksThrough"] = ranks;
                }
                return o;
            }
        }

        // ---------------- structural JSON comparison ----------------

        /// <summary>
        /// Diff returns null when equal, else a description of the first difference. Object
        /// key order is ignored; numbers compare by value (int vs float).
        /// </summary>
        internal static string Diff(JToken want, JToken got, string path)
        {
            want = want ?? JValue.CreateNull();
            got = got ?? JValue.CreateNull();
            bool wn = want.Type == JTokenType.Integer || want.Type == JTokenType.Float;
            bool gn = got.Type == JTokenType.Integer || got.Type == JTokenType.Float;
            if (wn && gn)
            {
                decimal a = want.Value<decimal>(), b = got.Value<decimal>();
                return a == b ? null : path + ": go " + Short(want) + " c# " + Short(got);
            }
            if (want.Type != got.Type)
            {
                return path + ": go " + Short(want) + " c# " + Short(got);
            }
            switch (want.Type)
            {
                case JTokenType.Object:
                {
                    JObject wo = (JObject)want, go = (JObject)got;
                    foreach (JProperty p in wo.Properties())
                    {
                        JToken gv;
                        if (!go.TryGetValue(p.Name, out gv))
                        {
                            return path + "." + p.Name + ": missing in c# (go " + Short(p.Value) + ")";
                        }
                        string d = Diff(p.Value, gv, path + "." + p.Name);
                        if (d != null)
                        {
                            return d;
                        }
                    }
                    foreach (JProperty p in go.Properties())
                    {
                        if (wo[p.Name] == null && !wo.ContainsKey(p.Name))
                        {
                            return path + "." + p.Name + ": extra in c# (" + Short(p.Value) + ")";
                        }
                    }
                    return null;
                }
                case JTokenType.Array:
                {
                    JArray wa = (JArray)want, ga = (JArray)got;
                    for (int i = 0; i < Math.Min(wa.Count, ga.Count); i++)
                    {
                        string d = Diff(wa[i], ga[i], path + "[" + i + "]");
                        if (d != null)
                        {
                            return d;
                        }
                    }
                    if (wa.Count != ga.Count)
                    {
                        return path + ": length go " + wa.Count + " c# " + ga.Count + " (go " + Short(want) + " c# " + Short(got) + ")";
                    }
                    return null;
                }
                default:
                    return JToken.DeepEquals(want, got) ? null : path + ": go " + Short(want) + " c# " + Short(got);
            }
        }

        private static string Short(JToken t)
        {
            string s = t.ToString(Formatting.None);
            return s.Length > 300 ? s.Substring(0, 300) + "…" : s;
        }
    }
}
