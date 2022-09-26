using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace Timing.RotorHazard
{
    public class RotorHazardTimingSystem : ITimingSystemWithRSSI
    {
        public bool Connected { get { return socket.Connected; } }

        public TimingSystemType Type { get { return TimingSystemType.RotorHazard; } }

        public Version Version { get; private set; }

        private float voltage;
        private Regex voltageRegex;

        private float temperature;
        private Regex tempRegex;

        private bool detecting;

        private Heartbeat lastBeat;
        private DateTime epoch;

        private RotorHazardSettings settings;
        public TimingSystemSettings Settings { get { return settings; } set { settings = value as RotorHazardSettings; } }

        public event DetectionEventDelegate OnDetectionEvent;

        private SocketIOHeartbeat socket;

        public int MaxPilots 
        { 
            get 
            {
                if (lastBeat.Frequency != null)
                {
                    return lastBeat.Frequency.Length;
                }
                return int.MaxValue;
            } 
        }

        private List<PassRecord> passRecords;

        public IEnumerable<StatusItem> Status
        {
            get
            {
                if (voltage != 0)
                {
                    yield return new StatusItem() { StatusOK = voltage > settings.VoltageWarning, Value = voltage.ToString("0.0") + "v" };
                }

                if (temperature != 0)
                {
                    yield return new StatusItem() { StatusOK = temperature < settings.TemperatureWarning, Value = temperature.ToString("0.0") + "c" };
                }

                if (connectionCount > 10)
                    yield return new StatusItem() { StatusOK = false, Value = connectionCount.ToString("0") + " disc" };
            }
        }

        private int connectionCount;


        public RotorHazardTimingSystem()
        {
            voltageRegex = new Regex("\"voltage\":{\"value\":([0-9.]*)", RegexOptions.Compiled);
            tempRegex = new Regex("\"temperature\":{\"value\":([0-9.]*)", RegexOptions.Compiled);

            settings = new RotorHazardSettings();
            socket = new SocketIOHeartbeat(this);
            socket.OnHeartBeat += OnHeartBeat;

            passRecords = new List<PassRecord>();
        }
        public void Dispose()
        {
            Disconnect();
        }
        public bool Connect()
        {
            if (socket.Connect("http://" + settings.HostName + ":" + settings.Port))
            {
                socket.On("pass_record", OnPassRecord);
                socket.On("frequency_set", (a) => { });
                socket.On("frequency_data", OnFrequencyData);
                socket.On("environmental_data", OnEnvironmentData);
                socket.On("node_data", OnNodeData);
                socket.On("load_all", e =>
                {
                    Logger.TimingLog.Log(this, "Load All");
                });

                string[] toLog = new string[]
                {
                        "cluster_status",
                        "heat_data",
                        "current_laps",
                        "leaderboard",
                        "race_status",
                        "race_format",
                        "stop_timer",
                        "stage_ready",
                        "node_crossing_change",
                        "message",
                        "first_pass_registered",
                        "priority_message"
                };

                foreach (string tolog in toLog)
                {
                    socket.On(tolog, DebugLog);
                }

                connectionCount++;
                return true;
            }

            return false;
        }

        public bool Disconnect()
        {
            return socket.Disconnect();
        }

        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> newFrequencies)
        {
            try
            {
                SetSettings setSettings = new SetSettings()
                {
                    calibration_offset = settings.CalibrationOffset,
                    calibration_threshold = settings.CalibrationThreshold,
                    trigger_threshold = settings.TriggerThreshold,
                };

                Logger.TimingLog.Log(this, "Setting Triggers");
                socket.Emit("set_calibration_threshold", setSettings);
                socket.Emit("set_calibration_offset", setSettings);
                socket.Emit("set_trigger_threshold", setSettings);

                Logger.TimingLog.Log(this, "SetListeningFrequencies", string.Join(", ", newFrequencies.Select(f => f.ToString())));
                int node = 0;
                foreach (ListeningFrequency freqSense in newFrequencies)
                {
                    SetFrequency sf = new SetFrequency() { node = node, frequency = freqSense.Frequency };
                    socket.Emit("set_frequency", sf);
                    node++;
                }
                return true;
            }
            catch (Exception e)
            {
                socket.Disconnect();
                Logger.TimingLog.LogException(this, e);
            }
            return false;
        }


        private void OnEnvironmentData(string text)
        {
            //[{"Core":{"temperature":{"value":42.932,"units":"\u00b0C"}}}]

            try
            {
                Match match = voltageRegex.Match(text);
                if (match != null)
                {
                    string voltage = match.Groups[1].Value;
                    float v;
                    if (float.TryParse(voltage, out v))
                    {
                        this.voltage = v;
                    }
                }

                match = tempRegex.Match(text);
                if (match != null)
                {
                    string temp = match.Groups[1].Value;
                    float t;
                    if (float.TryParse(temp, out t))
                    {
                        this.temperature = t;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }
        }

        private void OnNodeData(string text)
        {
            try
            {
                NodeData nodeData = JsonConvert.DeserializeObject<NodeData>(text);

                PassRecord[] temp;
                lock (passRecords)
                {
                    temp = passRecords.ToArray();
                    passRecords.Clear();
                }


                foreach (PassRecord record in temp)
                {
                    DateTime time = epoch.AddMilliseconds(record.timestamp);

                    int rssi = 0;

                    if (nodeData.Pass_Peak_RSSI.Length > record.node)
                    {
                        rssi = nodeData.Pass_Peak_RSSI[record.node];
                    }

                    OnDetectionEvent?.Invoke(this, record.frequency, time, rssi);
                }
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }
        }

        private void DebugLog(string text)
        {
#if DEBUG
            Logger.TimingLog.Log(this, "Debug Log: " + text);
#endif
        }

        private void OnPassRecord(string text)
        {
            PassRecord response = JsonConvert.DeserializeObject<PassRecord>(text);

            lock (passRecords)
            {
                passRecords.Add(response);
            }
        }

        public void OnHeartBeat(string text)
        {
            try
            {
                lastBeat = JsonConvert.DeserializeObject<Heartbeat>(text);
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }
        }

        private void SecondaryResponse(string args)
        {
            Logger.TimingLog.Log(this, "Secondary Race Format");
        }

        private void VersionResponse(string text)
        {
            //{"major":"3","minor":"1"}

            try
            {
                Version = JsonConvert.DeserializeObject<Version>(text);
                Logger.TimingLog.Log(this, "Version " + Version.Major + "." + Version.Minor);

            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }
        }

        private void OnFrequencyData(string text)
        {
            //frequency_data {"fdata":[{"band":null,"channel":null,"frequency":5658},{"band":null,"channel":null,"frequency":5695},{"band":null,"channel":null,"frequency":5760},{"band":null,"channel":null,"frequency":5880}]})
            try
            {
                FrequencyDatas frequencyData = JsonConvert.DeserializeObject<FrequencyDatas>(text);
                Logger.TimingLog.Log(this, "Device listening on " + string.Join(", ", frequencyData.fdata.Select(r => r.ToString())));

            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }
        }
        public bool StartDetection()
        {
            lock (passRecords)
            {
                passRecords.Clear();
            }

            if (!socket.Emit("get_version", VersionResponse))
            {
                Logger.TimingLog.Log(this, "get_version failed", Logger.LogType.Error);
                return false;
            }

            if (!socket.Emit("join_cluster", SecondaryResponse))
            {
                Logger.TimingLog.Log(this, "join_cluster failed", Logger.LogType.Error);
                return false;
            }

            if (!socket.Emit("get_timestamp", GotTimestamp))
            {
                Logger.TimingLog.Log(this, "get_timestamp failed", Logger.LogType.Error);
                return false;
            }

            if (!socket.Emit("stage_race", GotRaceStart))
            {
                Logger.TimingLog.Log(this, "stage_race failed", Logger.LogType.Error);
                return false;
            }

            return true;
        }

        protected void GotTimestamp(string text)
        {
            TimeStamp response = JsonConvert.DeserializeObject<TimeStamp>(text);
            epoch = DateTime.Now.AddMilliseconds(-response.timestamp);

            Logger.TimingLog.Log(this, "Timestamp", response.timestamp.ToString("N"));
            Logger.TimingLog.Log(this, "Epoch", epoch.ToLongTimeString());

        }

        protected void GotRaceStart(string args)
        {
            Logger.TimingLog.Log(this, "Device started Race");
            detecting = true;
        }

        public bool EndDetection()
        {
            if (!detecting)
                return false;

            detecting = false;

            return socket.Emit("stop_race", GotRaceStop);
        }

        protected void GotRaceStop(string args)
        {
            Logger.TimingLog.Log(this, "Device stopped Race");
        }

        public IEnumerable<RSSI> GetRSSI()
        {
            if (lastBeat.Current_RSSI == null ||
                lastBeat.Frequency == null ||
                lastBeat.Crossing_Flag == null)
            {
                yield break;
            }


            int length = (new int[] {
                lastBeat.Current_RSSI.Length,
                lastBeat.Frequency.Length,
                lastBeat.Crossing_Flag.Length
            }).Min();

            for (int i = 0; i < length; i++)
            {
                RSSI rssi = new RSSI()
                {
                    CurrentRSSI = lastBeat.Current_RSSI[i],
                    Frequency = lastBeat.Frequency[i],
                    Detected = lastBeat.Crossing_Flag[i],
                    ScaleMax = 200,
                    ScaleMin = 20,
                    TimingSystem = this
                };

                yield return rssi;
            }
        }
    }


}
