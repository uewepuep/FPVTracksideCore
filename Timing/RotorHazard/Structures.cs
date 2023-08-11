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

    //{[{"fdata":[{"band":null,"channel":null,"frequency":5658},{"band":null,"channel":null,"frequency":5695},{"band":null,"channel":null,"frequency":5760},{"band":null,"channel":null,"frequency":5800}]}]}
    public struct FrequencyDatas
    {
        public FrequencyData[] fdata { get; set; }
    }

    public class FrequencyData
    {
        public string band { get; set; }
        public string channel { get; set; }
        public int frequency { get; set; }

        public override string ToString()
        {
            string output = "";
            if (band != null) output += band;
            if (channel != null) output += channel + " ";

            output += frequency + "mhz";

            return output;
        }
    }

    public struct PiTime
    {
        public double pi_time_s { get; set; }
    }

    public struct ServerTimeSample
    {
        public TimeSpan Differential { get; set; }
        public TimeSpan Response { get; set; }
    }

    public class NodeData
    {
        //node_data {"node_peak_rssi":[122,51,51,53],"node_nadir_rssi":[40,39,43,47],"pass_peak_rssi":[115,0,0,0],"pass_nadir_rssi":[71,0,0,0],"debug_pass_count":[3,0,0,0]}
        public int[] node_peak_rssi { get; set; }
        public int[] node_nadir_rssi { get; set; }
        public int[] pass_peak_rssi { get; set; }
        public int[] pass_nadir_rssi { get; set; }
        public int[] debug_pass_count { get; set; }
    }

    public struct Heartbeat
    {
        //{[{"current_rssi":[57,57,49,41],"frequency":[5658,5695,5760,5800],"loop_time":[1020,1260,1092,1136],"crossing_flag":[false,false,false,false]}]}
        
        public int[] current_rssi { get; set; }
        public int[] frequency { get; set; }
        public int[] loop_time { get; set; }
        public bool[] crossing_flag { get; set; }

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

    public struct EnvironmentDataValue
    {
        public double value { get; set; }
        public string units { get; set; }
    }

    public struct EnvironmentDataSensor
    {
        public EnvironmentDataValue temperature { get; set; }
        public EnvironmentDataValue voltage { get; set; }
    }

    public struct EnvironmentData
    {
        public EnvironmentDataSensor Core { get; set; }
    }

    //{[{"release_version":"4.0.0-dev.9","server_api":41,"json_api":3,"node_api_match":true,"node_api_lowest":35,"node_api_levels":[35,35,35,35],"node_version_match":true,"node_fw_versions":["1.1.4","1.1.4","1.1.4","1.1.4"],"node_api_best":35,"prog_start_epoch":"1691557662399","prog_start_time":"2023-08-09 05:07:42.398829"}]}

    public struct ServerInfo
    {
        public string release_version { get; set; }
        public string[] node_fw_versions { get; set; }

        public string prog_start_epoch { get; set; }
        public string prog_start_time { get; set; }
    }

    public struct RaceStart
    {
        public double start_time_s { get; set; }
    }
}

