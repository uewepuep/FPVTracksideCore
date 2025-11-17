using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace RaceLib
{
    public enum StageTypes
    {
        Default,
        DoubleElimination,
        ChaseTheAce,
        StreetLeague,
        Final
    }

    public class Stage : BaseObject
    {
        public string Name { get; set; }

        [System.ComponentModel.Browsable(false)]
        public PointSummary PointSummary { get; set; }

        [System.ComponentModel.Browsable(false)]
        public TimeSummary TimeSummary { get; set; }

        [System.ComponentModel.Browsable(false)]
        public bool PackCountAfterRound { get; set; }

        [Category("Editable Details")]
        public bool LapCountAfterRound { get; set; }

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

        public bool GeneratesRounds
        {
            get
            {
                if (StageType == StageTypes.Default || StageType == StageTypes.Final)
                {
                    return HasSheetFormat;
                }

                return true;
            }
        }

        [Category("Advanced")]
        public string GameTypeName { get; set; }

        public Color Color { get; set; }
        public bool Valid { get; set; }

        public int Order { get; set; }
        public StageTypes StageType { get; set; }

        public Stage()
        {
            LapCountAfterRound = false;
            PointSummary = null;
            TimeSummary = null;
            Color = Color.Transparent;
            Name = "";
            Valid = true;
            StageType = StageTypes.Default;
        }

        public void AutoName(RoundManager roundManager)
        {
            Name = "Stage";

            if (StageType != StageTypes.Default)
            {
                Name = StageType.ToString().CamelCaseToHuman();
            }

            IEnumerable<Round> rounds = roundManager.GetStageRounds(this);
            if (rounds.Any())
            {
                Name = Name + " " + (roundManager.GetStages().Count());
            }
        }

        public override string ToString()
        {
            if (Name != null)
                return Name;

            return base.ToString();
        }

        public bool HasResult
        {
            get
            {
                return PointSummary != null || TimeSummary != null || PackCountAfterRound || LapCountAfterRound;
            }
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
