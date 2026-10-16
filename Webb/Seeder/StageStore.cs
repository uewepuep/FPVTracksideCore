using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Webb.Seeder
{
    // The setup a stage was created with: everything the engine needs to
    // rebuild its format, in the same shape (and JSON names) as the AUFPV
    // platform's format_config, so a setup can be copied between the two.
    public class StageSetup
    {
        [JsonProperty("eliminationType")] public string EliminationType { get; set; } = "double";
        [JsonProperty("finalsFormat")] public string FinalsFormat { get; set; } = "chase-the-ace";
        [JsonProperty("pilotsPerRace")] public int PilotsPerRace { get; set; } = 4;
        [JsonProperty("pilotsToAdvance")] public int PilotsToAdvance { get; set; } = 2;
        [JsonProperty("aceWinsRequired")] public int AceWinsRequired { get; set; } = 2;

        [JsonProperty("qualifierCta")] public bool QualifierCta { get; set; }
        [JsonProperty("qualifierSeed")] public string QualifierSeed { get; set; } = "snake";

        [JsonProperty("pilotsPerMain")] public int PilotsPerMain { get; set; } = 16;
        [JsonProperty("bumpUp")] public int BumpUp { get; set; } = 4;
        [JsonProperty("ctaMains")] public List<string> CtaMains { get; set; }

        [JsonProperty("mains")] public List<string> Mains { get; set; }
        [JsonProperty("rounds")] public int Rounds { get; set; } = 3;
        [JsonProperty("direction")] public string Direction { get; set; } = "descending";
        [JsonProperty("pointsTable")] public Dictionary<int, int> PointsTable { get; set; }
        [JsonProperty("dropWorst")] public int DropWorst { get; set; }
        [JsonProperty("divisions")] public List<global::BracketEngine.MainsDivision> Divisions { get; set; }
        // Explicit pilot → main assignment (pilot IDs), mains format only.
        [JsonProperty("mainsRosters")] public Dictionary<string, List<string>> MainsRosters { get; set; }
    }

    // One stage as stored in the event folder.
    public class StageRecord
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("format")] public string Format { get; set; }
        [JsonProperty("setup")] public StageSetup Setup { get; set; } = new StageSetup();

        // Pilots in seed order (seed 1 first), as FPVTrackside pilot IDs — the
        // engine works on these strings, so renaming a pilot never breaks a stage.
        [JsonProperty("pilots")] public List<string> Pilots { get; set; } = new List<string>();
        // Names at creation, for pilots later removed from the event.
        [JsonProperty("pilotNames")] public Dictionary<string, string> PilotNames { get; set; } = new Dictionary<string, string>();

        // The engine's own state, verbatim (same JSON as the AUFPV platform's bracket_state).
        [JsonProperty("state")] public string State { get; set; }

        // Stage race ID → the FPVTrackside race it was sent to.
        [JsonProperty("links")] public Dictionary<string, Guid> Links { get; set; } = new Dictionary<string, Guid>();
        // Stage round key → the FPVTrackside round its races go in.
        [JsonProperty("roundLinks")] public Dictionary<string, Guid> RoundLinks { get; set; } = new Dictionary<string, Guid>();
        // The FPVTrackside stage those rounds are grouped under (empty until the first race is sent).
        [JsonProperty("tracksideStage")] public Guid TracksideStageId { get; set; }
        // Races whose result was set by hand: FPVTrackside's timing never overwrites them.
        [JsonProperty("locked")] public List<string> Locked { get; set; } = new List<string>();
        // Channel per seat (seat 1 first), e.g. ["R1","R2","R7","R8"]. Empty = pick free channels.
        [JsonProperty("seatChannels")] public List<string> SeatChannels { get; set; } = new List<string>();
        // Apply results from FPVTrackside automatically when a linked race finishes.
        [JsonProperty("autoResults")] public bool AutoResults { get; set; } = true;
        // Create each race in FPVTrackside as soon as its line-up is known.
        [JsonProperty("autoSend")] public bool AutoSend { get; set; }

        [JsonProperty("created")] public DateTime Created { get; set; }
        [JsonProperty("updated")] public DateTime Updated { get; set; }
    }

    // SeederPlan.json in the event's folder. Small and read whole; every write
    // goes to a temp file first so a crash mid-save can't leave a half file.
    public class StageStore
    {
        // Not "Stages.json": FPVTrackside keeps its own Stage objects in that file.
        public const string FileName = "SeederPlan.json";
        // What the file was called before, newest first. Read only; the next save writes FileName.
        private static readonly string[] LegacyFileNames = { "StagePlans.json", "Brackets.json" };

        private readonly Func<string> eventDirectory;
        private readonly object sync = new object();

        public StageStore(Func<string> eventDirectory)
        {
            this.eventDirectory = eventDirectory;
        }

        private class FileShape
        {
            [JsonProperty("version")] public int Version { get; set; } = 1;
            [JsonProperty("stages")] public List<StageRecord> Stages { get; set; } = new List<StageRecord>();
            // Brackets.json kept its list under "brackets"; read only.
            [JsonProperty("brackets")] public List<StageRecord> Legacy { set { if (value != null) Stages.AddRange(value); } }
        }

        private string Path()
        {
            string dir = eventDirectory();
            if (string.IsNullOrEmpty(dir))
                throw new InvalidOperationException("no event is open");
            return System.IO.Path.Combine(dir, FileName);
        }

        public List<StageRecord> All()
        {
            lock (sync)
            {
                return Read().Stages;
            }
        }

        public StageRecord Get(int id)
        {
            return All().FirstOrDefault(b => b.Id == id);
        }

        public StageRecord Add(StageRecord record)
        {
            lock (sync)
            {
                FileShape file = Read();
                record.Id = file.Stages.Count == 0 ? 1 : file.Stages.Max(b => b.Id) + 1;
                record.Created = record.Updated = DateTime.Now;
                file.Stages.Add(record);
                Write(file);
                return record;
            }
        }

        public void Save(StageRecord record)
        {
            lock (sync)
            {
                FileShape file = Read();
                int i = file.Stages.FindIndex(b => b.Id == record.Id);
                if (i < 0)
                    throw new InvalidOperationException("stage " + record.Id + " no longer exists");
                record.Updated = DateTime.Now;
                file.Stages[i] = record;
                Write(file);
            }
        }

        public bool Delete(int id)
        {
            lock (sync)
            {
                FileShape file = Read();
                int removed = file.Stages.RemoveAll(b => b.Id == id);
                if (removed > 0)
                    Write(file);
                return removed > 0;
            }
        }

        private FileShape Read()
        {
            string path = Path();
            if (!File.Exists(path))
            {
                // An event set up before the rename: read the old file; the next save writes SeederPlan.json
                string dir = System.IO.Path.GetDirectoryName(path);
                path = LegacyFileNames.Select(n => System.IO.Path.Combine(dir, n)).FirstOrDefault(File.Exists);
                if (path == null)
                    return new FileShape();
            }
            return JsonConvert.DeserializeObject<FileShape>(File.ReadAllText(path)) ?? new FileShape();
        }

        private void Write(FileShape file)
        {
            string path = Path();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(file, Formatting.Indented));
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
    }
}
