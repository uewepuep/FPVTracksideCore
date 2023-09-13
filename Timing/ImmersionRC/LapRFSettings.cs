using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Timing.ImmersionRC
{
    public class LapRFSettings : TimingSystemSettings
    {
        [DisplayName("Gain/Threshold Presets")]
        public CalibrationValues GainThresholdPresetValues
        {
            get
            {
                if (GainAll == "59" && ThresholdAll == "800")
                    return CalibrationValues.Standard5Inch;
                if (GainAll == "54" && ThresholdAll == "1110")
                    return CalibrationValues.WhoopsIndoors;
                return CalibrationValues.Custom;
            }
            set
            {
                switch (value)
                {
                    case CalibrationValues.Standard5Inch:
                        GainAll = "59";
                        ThresholdAll = "800";
                        break;
                    case CalibrationValues.WhoopsIndoors:
                        GainAll = "54";
                        ThresholdAll = "1110";
                        break;
                }
            }
        }

        private double minimumTriggerSeconds;

        [Category("Trigger Time Limits")]
        public double MinimumTriggerSeconds { get { return minimumTriggerSeconds; } set { minimumTriggerSeconds = Math.Max(0.1, value); } }
        [Category("Trigger Time Limits")]
        public double RaceStartMinimumSeconds { get; set; }

        [Browsable(false)]
        public TimeSpan MinimumTriggerTime { get { return TimeSpan.FromSeconds(minimumTriggerSeconds); } }

        [Browsable(false)]
        public TimeSpan RaceStartMinTime { get { return TimeSpan.FromSeconds(RaceStartMinimumSeconds); } }
        
        [Category("Trigger Time Limits")]
        public double DetectionReportDelaySeconds { get; set; }

        [Category("Alerts")]
        public double VoltageAlarm { get; set; }

        public enum CalibrationValues
        {
            Standard5Inch,
            WhoopsIndoors,
            Custom
        }


        [Category("Receiver All")]
        public string GainAll
        {
            get
            {
                if (Gain1 == Gain2 &&
                    Gain2 == Gain3 &&
                    Gain3 == Gain4 &&
                    Gain4 == Gain5 &&
                    Gain5 == Gain6 &&
                    Gain6 == Gain7 &&
                    Gain7 == Gain8)
                {
                    return Gain1.ToString();
                }
                return "";
            }
            set
            {
                int i;
                if (int.TryParse(value, out i))
                {
                    Gain1 = i;
                    Gain2 = i;
                    Gain3 = i;
                    Gain4 = i;

                    Gain5 = i;
                    Gain6 = i;
                    Gain7 = i;
                    Gain8 = i;
                }
            }
        }

        [Category("Receiver All")]
        public string ThresholdAll
        {
            get
            {
                if (Threshold1 == Threshold2 &&
                    Threshold2 == Threshold3 &&
                    Threshold3 == Threshold4 &&
                    Threshold4 == Threshold5 &&
                    Threshold5 == Threshold6 &&
                    Threshold6 == Threshold7 &&
                    Threshold7 == Threshold8)
                {
                    return Threshold1.ToString();
                }
                return "";
            }
            set
            {
                int i;
                if (int.TryParse(value, out i))
                {
                    Threshold1 = i;
                    Threshold2 = i;
                    Threshold3 = i;
                    Threshold4 = i;

                    Threshold5 = i;
                    Threshold6 = i;
                    Threshold7 = i;
                    Threshold8 = i;
                }
            }
        }

        [Category("Receiver 1")]
        public int Gain1 { get; set; }

        [Category("Receiver 1")]
        public int Threshold1 { get; set; }

        [Category("Receiver 1")]
        public bool Enable1 { get; set; }


        [Category("Receiver 2")]
        public int Gain2 { get; set; }

        [Category("Receiver 2")]
        public int Threshold2 { get; set; }

        [Category("Receiver 2")]
        public bool Enable2 { get; set; }


        [Category("Receiver 3")]
        public int Gain3 { get; set; }

        [Category("Receiver 3")]
        public int Threshold3 { get; set; }

        [Category("Receiver 3")]
        public bool Enable3 { get; set; }


        [Category("Receiver 4")]
        public int Gain4 { get; set; }

        [Category("Receiver 4")]
        public int Threshold4 { get; set; }

        [Category("Receiver 4")]
        public bool Enable4 { get; set; }


        [Category("Receiver 5")]
        public int Gain5 { get; set; }

        [Category("Receiver 5")]
        public int Threshold5 { get; set; }

        [Category("Receiver 5")]
        public bool Enable5 { get; set; }


        [Category("Receiver 6")]
        public int Gain6 { get; set; }
        
        [Category("Receiver 6")]
        public int Threshold6 { get; set; }

        [Category("Receiver 6")]
        public bool Enable6 { get; set; }


        [Category("Receiver 7")]
        public int Gain7 { get; set; }

        [Category("Receiver 7")]
        public int Threshold7 { get; set; }

        [Category("Receiver 7")]
        public bool Enable7 { get; set; }


        [Category("Receiver 8")]
        public int Gain8 { get; set; }

        [Category("Receiver 8")]
        public int Threshold8 { get; set; }

        [Category("Receiver 8")]
        public bool Enable8 { get; set; }

        [Category("Debug")]
        public bool LegacyFirmwareTimeRangeFix { get; set; }

        [Browsable(false)]
        public int[] Gains { get { return new int[] { Gain1, Gain2, Gain3, Gain4, Gain5, Gain6, Gain7, Gain8 }; } }
        [Browsable(false)]
        public int[] Thresholds { get { return new int[] { Threshold1, Threshold2, Threshold3, Threshold4, Threshold5, Threshold6, Threshold7, Threshold8 }; } }
        [Browsable(false)]
        public bool[] Enables { get { return new bool[] { Enable1, Enable2, Enable3, Enable4, Enable5, Enable6, Enable7, Enable8 }; } }

        public LapRFSettings()
        {
            DetectionReportDelaySeconds = 0.3f;
            MinimumTriggerSeconds = 1;
            RaceStartMinimumSeconds = 0;

            //Defaults from demo app
            Gain1 = 59;
            Gain2 = 59;
            Gain3 = 59;
            Gain4 = 59;
            Gain5 = 59;
            Gain6 = 59;
            Gain7 = 59;
            Gain8 = 59;

            Threshold1 = 800;
            Threshold2 = 800;
            Threshold3 = 800;
            Threshold4 = 800;
            Threshold5 = 800;
            Threshold6 = 800;
            Threshold7 = 800;
            Threshold8 = 800;

            Enable1 = true;
            Enable2 = true;
            Enable3 = true;
            Enable4 = true;
            Enable5 = true;
            Enable6 = true;
            Enable7 = true;
            Enable8 = true;

            LegacyFirmwareTimeRangeFix = false;

            VoltageAlarm = 10;
        }
    }
}
