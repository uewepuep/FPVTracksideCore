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

        [Category("Communication")]
        public bool UseTCP { get; set; }

        [Category("Communication")]
        public string IPAddress { get; set; }

        [Category("Communication")]
        public int Port { get; set; }

        public int MinLapTimeSeconds { get; set; }

        public int Threshold { get; set; }

        public ChorusSettings()
        {
            ComPort = "None";
            UseTCP = false;
            IPAddress = "192.168.4.1";
            Port = 9000;
            MinLapTimeSeconds = 0;
            Threshold = 190;
        }
        

        public override string ToString()
        {
            return "Chorus / Chorus32";
        }
    }
}
