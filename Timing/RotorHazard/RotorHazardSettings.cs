using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timing.Delta5;

namespace Timing.RotorHazard
{
    public class RotorHazardSettings : TimingSystemSettings
    {
        [Category("Network")]
        public string HostName { get; set; }
        [Category("Network")]
        public int Port { get; set; }

        public int VoltageWarning { get; set; }
        public int TemperatureWarning { get; set; }

        public RotorHazardSettings()
        {
            HostName = "10.1.1.207";
            Port = 5000;

            VoltageWarning = 11;
            TemperatureWarning = 80;
        }
    }
}
