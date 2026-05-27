using RaceLib.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace DB.JSON
{
    public class Stage : DatabaseObjectT<RaceLib.Stage>
    {
        public string Name { get; set; }
        public PointSummary PointSummary { get; set; }

        public TimeSummary TimeSummary { get; set; }

        public bool LapCountAfterRound { get; set; }

        public string SheetFormatFilename { get; set; }

        public string ScriptFormatFilename { get; set; }

        public bool Valid { get; set; }

        public int Order { get; set; }

        public RaceLib.StageTypes StageType { get; set; }

        public StandingsResult Standings { get; set; }

        public Stage() { }

        public Stage(RaceLib.Stage obj)
            : base(obj)
        {
            Valid = obj.Valid;

            if (obj.PointSummary != null)
            {
                PointSummary = new PointSummary();
                ReflectionTools.Copy(obj.PointSummary, PointSummary);
            }

            if (obj.TimeSummary != null)
            {
                TimeSummary = new TimeSummary();
                ReflectionTools.Copy(obj.TimeSummary, TimeSummary);
            }

            Standings = obj.Standings == null ? null : new StandingsResult
            {
                Headings = obj.Standings.Headings,
                Rows = obj.Standings.Rows?.Select(r => new StandingsRow { Name = r.Name, PilotId = r.PilotId?.ToString(), Values = r.Values }).ToArray()
            };
        }

        public override RaceLib.Stage GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.Stage stage = base.GetRaceLibObject(database);

            if (PointSummary != null)
            {
                stage.PointSummary = new RaceLib.PointSummary();
                ReflectionTools.Copy(PointSummary, stage.PointSummary);
            }

            if (TimeSummary != null)
            {
                stage.TimeSummary = new RaceLib.TimeSummary();
                ReflectionTools.Copy(TimeSummary, stage.TimeSummary);
            }

            stage.Standings = Standings == null ? null : new RaceLib.Format.StandingsResult
            {
                Headings = Standings.Headings,
                Rows = Standings.Rows?.Select(r => new RaceLib.Format.StandingsRow { Name = r.Name, PilotId = Guid.TryParse(r.PilotId, out Guid g) ? g : (Guid?)null, Values = r.Values }).ToArray()
            };

            return stage;
        }
    }

    public class PointSummary
    {
        public bool RoundPositionRollover { get; set; }

        public bool DropWorstRound { get; set; }
    }

    public class TimeSummary
    {
        public bool IncludeAllRounds { get; set; }

        public string TimeSummaryType { get; set; }
    }
}