using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Stage : BaseObject
    {
        public string Name { get; set; }

        public int Order { get; set; }


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

        [Category("Advanced")]
        public string GameTypeName { get; set; }

        public Color Color { get; set; }

        public Stage()
        {
            LapCountAfterRound = false;
            PointSummary = null;
            TimeSummary = null;
            Color = Color.Yellow;
            Name = "Stage 1";
        }

    }
}
