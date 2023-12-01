using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB
{
    public class Round : DatabaseObjectT<RaceLib.Round>
    {
        public string Name { get; set; }

        public int RoundNumber { get; set; }

        public string EventType { get; set; }

        public string RoundType { get; set; }

        public bool Valid { get; set; }

        public PointSummary PointSummary { get; set; }

        public TimeSummary TimeSummary { get; set; }

        public bool LapCountAfterRound { get; set; }

        public int Order { get; set; }

        public string SheetFormatFilename { get; set; }

        public Round() { }

        public Round(RaceLib.Round obj)
            : base(obj)
        {
            if (obj.PointSummary != null)
            {
                PointSummary = new PointSummary();
                Copy(obj.PointSummary, PointSummary);   
            }

            if (obj.TimeSummary != null)
            {
                TimeSummary = new TimeSummary();
                Copy(obj.TimeSummary, TimeSummary);
            }
        }

        public override RaceLib.Round GetRaceLibObject(ICollectionDatabase database)
        {
            RaceLib.Round round = base.GetRaceLibObject(database);

            if (PointSummary != null)
            {
                round.PointSummary = new RaceLib.PointSummary();
                Copy(PointSummary, round.PointSummary);
            }

            if (TimeSummary != null)
            {
                round.TimeSummary = new RaceLib.TimeSummary();
                Copy(TimeSummary, round.TimeSummary);
            }

            return round;
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
