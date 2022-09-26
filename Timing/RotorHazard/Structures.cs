using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timing.RotorHazard
{
    public struct Version
    {
        public string Major { get; set; }
        public string Minor { get; set; }
    }

    public struct FrequencyDatas
    {
        public FrequencyData[] fdata { get; set; }
    }

    public class FrequencyData
    {
        public string Band { get; set; }
        public string Channel { get; set; }
        public int Frequency { get; set; }

        public override string ToString()
        {
            string output = "";
            if (Band != null) output += Band;
            if (Channel != null) output += Channel + " ";

            output += Frequency + "mhz";

            return output;
        }
    }

    public struct Sensor
    {
        public float Value { get; set; }
        public string Units { get; set; }
    }

    public class NodeData
    {
        //node_data {"node_peak_rssi":[122,51,51,53],"node_nadir_rssi":[40,39,43,47],"pass_peak_rssi":[115,0,0,0],"pass_nadir_rssi":[71,0,0,0],"debug_pass_count":[3,0,0,0]}
        public int[] Node_Peak_RSSI { get; set; }
        public int[] Node_Nadir_RSSI { get; set; }
        public int[] Pass_Peak_RSSI { get; set; }
        public int[] Pass_Nadir_RSSI { get; set; }
        public int[] Debug_Pass_Count { get; set; }
    }

    public struct Heartbeat
    {
        //{"current_rssi":[46,46,49,52],"frequency":[5658,5695,5760,5800],"loop_time":[972,1028,1024,1068],"crossing_flag":[false,false,false,false]
        public int[] Current_RSSI { get; set; }
        public int[] Frequency { get; set; }
        public int[] Loop_Time { get; set; }
        public bool[] Crossing_Flag { get; set; }

    }

    public struct SetFrequency
    {
        public int node { get; set; }
        public int frequency { get; set; }
    }

    public struct PassRecord
    {
        public int node { get; set; }
        public int frequency { get; set; }
        public double timestamp { get; set; }
    }

    public struct SetSettings
    {
        public int calibration_threshold { get; set; }
        public int calibration_offset { get; set; }
        public int trigger_threshold { get; set; }
        public int filter_ratio { get; set; }
    }

    public class TimeStamp
    {
        public double timestamp { get; set; }
    }
}
