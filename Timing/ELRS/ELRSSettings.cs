using System.ComponentModel;

namespace Timing.ELRS
{
    public class ELRSSettings : TimingSystemSettings
    {
        [Category("VRXC Connection")]
        [Description("Serial port name (e.g., COM3, /dev/ttyUSB0) connected to ESP32 running VRXC/ELRS Backpack firmware")]
        [DisplayName("Serial Port")]
        public string ComPort { get; set; }

        [Category("VRXC Connection")]
        [Description("Serial baud rate (default: 460800 for ELRS/MSP)")]
        public int BaudRate { get; set; }

        [Category("Race Control")]
        [Description("Minimum time between race start/stop commands in milliseconds (debounce)")]
        public int DebounceMs { get; set; }

        public ELRSSettings()
        {
            Role = TimingSystemRole.Split;
            ComPort = "None";
            BaudRate = 460800;
            DebounceMs = 500;
        }

        public override string ToString()
        {
            return $"ELRS Backpack ({ComPort})";
        }
    }
}
