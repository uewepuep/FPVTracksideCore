using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Webb.Brackets
{
    // The setup a bracket was created with: everything the engine needs to
    // rebuild its format, in the same shape (and JSON names) as the AUFPV
    // platform's format_config, so a setup can be copied between the two.
    public class BracketSetup
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
        [JsonProperty("divisions")] public List<global::Brackets.MainsDivision> Divisions { get; set; }
        // Explicit pilot → main assignment (pilot IDs), mains format only.
        [JsonProperty("mainsRosters")] public Dictionary<string, List<string>> MainsRosters { get; set; }
    }

    // One bracket as stored in the event folder.
    public class BracketRecord
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("format")] public string Format { get; set; }
        [JsonProperty("setup")] public BracketSetup Setup { get; set; } = new BracketSetup();

        // Pilots in seed order (seed 1 first), as FPVTrackside pilot IDs — the
        // engine works on these strings, so renaming a pilot never breaks a bracket.
        [JsonProperty("pilots")] public List<string> Pilots { get; set; } = new List<string>();
        // Names at creation, for pilots later removed from the event.
        [JsonProperty("pilotNames")] public Dictionary<string, string> PilotNames { get; set; } = new Dictionary<string, string>();

        // The engine's own state, verbatim (same JSON as the AUFPV platform's bracket_state).
        [JsonProperty("state")] public string State { get; set; }

        // Bracket heat ID → the FPVTrackside race it was sent to.
        [JsonProperty("links")] public Dictionary<string, Guid> Links { get; set; } = new Dictionary<string, Guid>();
        // Bracket round key → the FPVTrackside round its heats go in.
        [JsonProperty("roundLinks")] public Dictionary<string, Guid> RoundLinks { get; set; } = new Dictionary<string, Guid>();
        // Heats whose result was set by hand: FPVTrackside's timing never overwrites them.
        [JsonProperty("locked")] public List<string> Locked { get; set; } = new List<string>();
        // Channel per seat (seat 1 first), e.g. ["R1","R2","R7","R8"]. Empty = pick free channels.
        [JsonProperty("seatChannels")] public List<string> SeatChannels { get; set; } = new List<string>();
        // Apply results from FPVTrackside automatically when a linked race finishes.
        [JsonProperty("autoResults")] public bool AutoResults { get; set; } = true;

        [JsonProperty("created")] public DateTime Created { get; set; }
        [JsonProperty("updated")] public DateTime Updated { get; set; }
    }

    // Brackets.json in the event's folder. Small and read whole; every write
    // goes to a temp file first so a crash mid-save can't leave a half file.
    public class BracketStore
    {
        public const string FileName = "Brackets.json";

        private readonly Func<string> eventDirectory;
        private readonly object sync = new object();

        public BracketStore(Func<string> eventDirectory)
        {
            this.eventDirectory = eventDirectory;
        }

        private class FileShape
        {
            [JsonProperty("version")] public int Version { get; set; } = 1;
            [JsonProperty("brackets")] public List<BracketRecord> Brackets { get; set; } = new List<BracketRecord>();
        }

        private string Path()
        {
            string dir = eventDirectory();
            if (string.IsNullOrEmpty(dir))
                throw new InvalidOperationException("no event is open");
            return System.IO.Path.Combine(dir, FileName);
        }

        public List<BracketRecord> All()
        {
            lock (sync)
            {
                return Read().Brackets;
            }
        }

        public BracketRecord Get(int id)
        {
            return All().FirstOrDefault(b => b.Id == id);
        }

        public BracketRecord Add(BracketRecord record)
        {
            lock (sync)
            {
                FileShape file = Read();
                record.Id = file.Brackets.Count == 0 ? 1 : file.Brackets.Max(b => b.Id) + 1;
                record.Created = record.Updated = DateTime.Now;
                file.Brackets.Add(record);
                Write(file);
                return record;
            }
        }

        public void Save(BracketRecord record)
        {
            lock (sync)
            {
                FileShape file = Read();
                int i = file.Brackets.FindIndex(b => b.Id == record.Id);
                if (i < 0)
                    throw new InvalidOperationException("bracket " + record.Id + " no longer exists");
                record.Updated = DateTime.Now;
                file.Brackets[i] = record;
                Write(file);
            }
        }

        public bool Delete(int id)
        {
            lock (sync)
            {
                FileShape file = Read();
                int removed = file.Brackets.RemoveAll(b => b.Id == id);
                if (removed > 0)
                    Write(file);
                return removed > 0;
            }
        }

        private FileShape Read()
        {
            string path = Path();
            if (!File.Exists(path))
                return new FileShape();
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
