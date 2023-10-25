using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Round : BaseObjectT<DB.Round>
    {
        [Category("Editable Details")]
        public string Name { get; set; }

        [Category("Editable Details")]
        public int RoundNumber { get; set; }

        public enum RoundTypes
        {
            Round = 0,
            Final,
            DoubleElimination
        }

        [Category("Editable Details")]
        public EventTypes EventType { get; set; }

        [Category("Editable Details")]
        public RoundTypes RoundType { get; set; }
        

        [Category("Editable Details")]
        public bool Valid { get; set; }

        [System.ComponentModel.Browsable(false)]
        public PointSummary PointSummary { get; set; }

        [System.ComponentModel.Browsable(false)]
        public TimeSummary TimeSummary { get; set; }

        [Category("Editable Details")]
        public bool LapCountAfterRound { get; set; }

        [Category("Advanced")]
        public int Order { get; set; }

        [Category("Advanced")]
        public string SheetFormatFilename { get; set; }

        
        [Category("Advanced")]
        public bool HasSheetFormat
        {
            get
            {
                return !string.IsNullOrEmpty(SheetFormatFilename);
            }
            set
            {
                if (value == false)
                {
                    SheetFormatFilename = null; 
                }
            }
        }

        public Round(DB.Round obj)
            : base(obj)
        {
        }

        public Round()
        {
            Order = -1;
            Valid = true;
            EventType = EventTypes.Race;
            RoundNumber = 1;
            RoundType = RoundTypes.Round;
            LapCountAfterRound = false;
            Name = "";
            PointSummary = null;
            TimeSummary = null;
        }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(Name))
            {
                return Name;
            }
            else
            {
                return EventType + " Round " + RoundNumber;
            }
        }

        public bool CanBePartofRollover()
        {
            if (EventType != EventTypes.Race)
            {
                return false;
            }

            if (RoundType != RoundTypes.Round)
            {
                return false;
            }

            return true;
        }

        public string ToStringShort()
        {
            return EventType.ToString().Substring(0, 1) + RoundNumber;
        }
    }

    public class PointSummary
    {
        public bool RoundPositionRollover { get; set; }

        public bool DropWorstRound { get; set; }

        public PointSummary()
        {
            RoundPositionRollover = false;
            DropWorstRound = true;
        }

        public PointSummary(PointsSettings pointsSettings)
        {
            RoundPositionRollover = pointsSettings.RoundPositionRollover;
            DropWorstRound = pointsSettings.DropWorstRound;
        }
    }

    public class TimeSummary
    {
        public enum TimeSummaryTypes
        {
            PB,
            EventLap,
            RaceTime
        }

        public bool IncludeAllRounds { get; set; }

        public TimeSummaryTypes TimeSummaryType { get; set; }

        public TimeSummary()
        {
            TimeSummaryType = TimeSummaryTypes.PB;
            IncludeAllRounds = false;
        }
    }
}
