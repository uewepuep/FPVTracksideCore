using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timing.Chorus
{
    public class ChorusSettings : TimingSystemSettings
    {
        [Category("Communication")]
        public string ComPort { get; set; }

        public int MinLapTimeSeconds { get; set; }

        public int Threshold { get; set; }

        public ChorusSettings()
        {
            ComPort = "None";
            MinLapTimeSeconds = 0;
            Threshold = 190;
        }


        public override string ToString()
        {
            return "Chorus / Chorus32";
        }
    }
}
