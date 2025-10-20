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

        public bool Valid { get; set; }

        public int Order { get; set; }

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