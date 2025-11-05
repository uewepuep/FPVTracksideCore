
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Round : BaseObject
    {
        [Category("Editable Details")]
        public string Name { get; set; }

        [Category("Editable Details")]
        public int RoundNumber { get; set; }


        [Category("Editable Details")]
        public EventTypes EventType { get; set; }

        [Category("Editable Details")]
        public bool Valid { get; set; }
        
        [Category("Editable Details")]
        public DateTime ScheduledStart { get; set; }


        [Category("Advanced")]
        public int Order { get; set; }

        [Category("Advanced")]
        public string GameTypeName { get; set; }

        public Stage Stage { get; set; }

        public StageTypes StageType
        {
            get
            {
                if (Stage == null)
                {
                    return StageTypes.Default;
                }
                return Stage.StageType;
            }
        }

        public Round()
        {
            Order = -1;
            Valid = true;
            EventType = EventTypes.Race;
            RoundNumber = 1;
            Name = "";
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

            if (StageType != StageTypes.Default)
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
