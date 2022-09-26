using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timing.ImmersionRC
{
    public class LapRFSettingsEthernet : LapRFSettings
    {
        [Category("Network")]
        public string HostName { get; set; }
        [Category("Network")]
        public int Port { get; set; }

        public LapRFSettingsEthernet()
        {
            HostName = "192.168.1.9";
            Port = 5403;
        }

        public override string ToString()
        {
            return "LapRF 8-way";
        }
    }
}
