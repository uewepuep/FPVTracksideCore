using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timing.RotorHazard
{
    public class RotorHazardSettings : TimingSystemSettings
    {
        [Category("Network")]
        public string HostName { get; set; }
        [Category("Network")]
        public int Port { get; set; }

        [Category("Admin Authentication (optional)")]
        public string AdminUsername { get; set; }
        [Category("Admin Authentication (optional)")]
        public string AdminPassword { get; set; }

        public int VoltageWarning { get; set; }
        public int TemperatureWarning { get; set; }

        public bool SyncPilotNames { get; set; }

        public RotorHazardSettings()
        {
            HostName = "10.1.1.207";
            Port = 5000;

            AdminUsername = "";
            AdminPassword = "";

            VoltageWarning = 11;
            TemperatureWarning = 80;
            SyncPilotNames = true;
        }
    }
}
