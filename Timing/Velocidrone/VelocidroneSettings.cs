using System;
using System.ComponentModel;

namespace Timing.Velocidrone
{
    public class VelocidroneSettings : TimingSystemSettings
    {
        [Category("Network")]
        [DisplayName("Host / IP")]
        public string HostName { get; set; }

        [Category("Network")]
        public int Port { get; set; }

        [Category("Network")]
        [DisplayName("Auto Reconnect")]
        public bool AutoReconnect { get; set; }

        public VelocidroneSettings()
        {
            HostName = "localhost";
            Port = VelocidroneProtocol.DefaultPort;
            AutoReconnect = true;
        }

        public override string ToString()
        {
            return "Velocidrone";
        }
    }
}
