using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timing.ImmersionRC
{
    public class LapRFSettingsUSB : LapRFSettings
    {
        [Category("USB")]
        public string ComPort { get; set; }

        public LapRFSettingsUSB()
        {
            ComPort = "None";
        }


        public override string ToString()
        {
            return "LapRF Puck";
        }
    }
}
