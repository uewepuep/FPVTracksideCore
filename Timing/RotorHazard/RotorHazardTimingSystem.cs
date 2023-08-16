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
using SocketIOClient;
using System.Reflection;
using System.Timers;

namespace Timing.RotorHazard
{
    public class RotorHazardTimingSystem : ITimingSystemWithRSSI
    {

        private bool connected;
        public bool Connected
        {
            get
            {
                if ((DateTime.Now - lastBeatTime) > TimeOut)
                {
                    Disconnect();
                    connected = false;
                }

                return connected;
            }
        }

        public TimingSystemType Type { get { return TimingSystemType.RotorHazard; } }

        public Version Version { get; private set; }

        private double voltage;
        private double temperature;

        private bool detecting;

        private Heartbeat lastBeat;
        private DateTime lastBeatTime;

        private RotorHazardSettings settings;
        public TimingSystemSettings Settings { get { return settings; } set { settings = value as RotorHazardSettings; } }

        public event DetectionEventDelegate OnDetectionEvent;

        private SocketIO socketIOClient;

        private TimeSpan serverTimeStart;
        private List<ServerTimeSample> serverTimeSamples;

        private TimeSpan serverDifferential;

        public TimeSpan Monotonic { get { return TimeSpan.FromMilliseconds(Environment.TickCount64); } }

        private DateTime serverEpoch
        {
            get
            {
                DateTime time = DateTime.Now;

                // Adjust by our monotonic clock and the difference to the servers monotonic clock
                time -= Monotonic + serverDifferential;

                return time;
            }
        }

        public int MaxPilots
        {
            get
            {
                if (lastBeat.frequency != null)
                {
                    return lastBeat.frequency.Length;
                }
                return 4;
            }
        }

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


        public TimeSpan TimeOut { get; set; }
        public TimeSpan CommandTimeOut { get; set; }

        public ServerInfo ServerInfo { get; private set; }

        private AutoResetEvent responseWait;

        private const int MaxTimeSamples = 20;

        private DateTime detectionStart;
        private DateTime rotorhazardStart;


        public RotorHazardTimingSystem()
        {
            settings = new RotorHazardSettings();
            serverTimeSamples = new List<ServerTimeSample>();
            TimeOut = TimeSpan.FromSeconds(10);
            CommandTimeOut = TimeSpan.FromSeconds(3);
            responseWait = new AutoResetEvent(false);
        }
        
        public void Dispose()
        {
            Disconnect();
        }

        public bool Connect()
        {
            try
            {
                socketIOClient = new SocketIO("http://" + settings.HostName + ":" + settings.Port);
                socketIOClient.OnConnected += Socket_OnConnected;

                lastBeatTime = DateTime.Now;
                connected = true;

                socketIOClient.ConnectAsync();
                return true;
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }

            return false;
        }

        private void Socket_OnConnected(object sender, EventArgs e)
        {
            try
            {
                SocketIO socket = (SocketIO)sender;

                socket.On("ts_lap_data", OnLapData);
                socket.On("stage_ready", OnStageReady);
                socket.On("frequency_set", (a) => { });
                socket.On("frequency_data", OnFrequencyData);
                socket.On("environmental_data", OnEnvironmentData);
                socket.On("node_data", OnNodeData);
                socket.On("heartbeat", HeartBeat);
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
                            "node_crossing_change",
                            "message",
                            "first_pass_registered",
                            "priority_message"
                };

                foreach (string tolog in toLog)
                {
                    socket.On(tolog, DebugLog);
                }

                connected = true;
                Logger.TimingLog.Log(this, "Connected");

                socket.EmitAsync("ts_server_info", OnServerInfo);

                connectionCount++;
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
                connected = false;
            }
        }

        public bool Disconnect()
        {
            connected = false;

            if (socketIOClient == null)
                return true;

            if (socketIOClient.Connected)
            {
                socketIOClient.DisconnectAsync();
            }
            return true;
        }

        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> newFrequencies)
        {
            if (!Connected)
                return false;

            detecting = false;

            try
            {
                Logger.TimingLog.Log(this, "SetListeningFrequencies", string.Join(", ", newFrequencies.Select(f => f.ToString())));
                int node = 0;
                foreach (ListeningFrequency freqSense in newFrequencies)
                {
                    SetFrequency sf = new SetFrequency() { node = node, frequency = freqSense.Frequency };
                    socketIOClient.EmitAsync("set_frequency", OnSetFrequency, sf);
                    if (!responseWait.WaitOne(CommandTimeOut))
                    {
                        return false;
                    }

                    node++;
                }

                if (!TimeSync())
                    return false;

                return true;
            }
            catch (Exception e)
            {
                socketIOClient.DisconnectAsync();
                Logger.TimingLog.LogException(this, e);
            }
            return false;
        }

        protected void OnSetFrequency(SocketIOResponse reponse)
        {
            responseWait.Set();
        }

        private void OnServerInfo(SocketIOResponse response)
        {
            ServerInfo = response.GetValue<ServerInfo>();
        }

        public bool TimeSync()
        {
            SocketIO socket = socketIOClient;
            if (socket == null)
                return false;

            Logger.TimingLog.Log(this, "Syncing Server time");

            lock (serverTimeSamples)
            {
                serverTimeSamples.Clear();
            }

            serverTimeStart = Monotonic;
            socketIOClient.EmitAsync("ts_server_time", OnServerTime);

            if (!responseWait.WaitOne(TimeOut))
            {
                Logger.TimingLog.Log(this, "Time sync took too long");
                return false;
            }

            int count = 0;
            lock (serverTimeSamples)
            {
                count = serverTimeSamples.Count;
            }

            Logger.TimingLog.Log(this, "Server Time Samples " + count);

            return count > 0;
        }

        private void OnServerTime(SocketIOResponse response)
        {
            if (response.Count == 0) return;

            TimeSpan responseTime = Monotonic;
            try
            {
                double time = response.GetValue<double>();

                TimeSpan delay = Monotonic - serverTimeStart;
                TimeSpan oneway = delay / 2;

                ServerTimeSample piTimeSample = new ServerTimeSample()
                {
                    Differential = TimeSpan.FromSeconds(time) - responseTime - oneway,
                    Response = delay
                };

                lock (serverTimeSamples)
                {
                    serverTimeSamples.Add(piTimeSample);

                    if (serverTimeSamples.Count < MaxTimeSamples)
                    {
                        Logger.TimingLog.Log(this, "Server Time Sample " + serverTimeSamples.Count);
                        serverTimeStart = Monotonic;
                        socketIOClient.EmitAsync("ts_server_time", OnServerTime);
                    }
                    else
                    {
                        IEnumerable<ServerTimeSample> ordered = serverTimeSamples.OrderBy(x => x.Response);
                        IEnumerable<double> orderedSeconds = ordered.Select(x => x.Differential.TotalSeconds);

                        double median = orderedSeconds.Skip(orderedSeconds.Count() / 2).First();

                        serverDifferential = TimeSpan.FromSeconds(median);
                        Logger.TimingLog.Log(this, "Server Differential " + serverDifferential.TotalSeconds + " Epoch " + serverEpoch);

                        responseWait.Set();
                    }
                }
            }
            catch (Exception ex) 
            {
                Logger.TimingLog.LogException(this, ex);
            }
        }

        private void OnEnvironmentData(SocketIOResponse response)
        {
            //{[[{"Core":{"temperature":{"value":47.774,"units":"\u00b0C"}}}]]}

            try
            {
                var result = response.GetValue<EnvironmentData[]>();

                if (result.Length >= 1)
                {
                    EnvironmentData value = result.First();

                    this.voltage = value.Core.voltage.value;
                    this.temperature = value.Core.temperature.value;
                }
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }
        }

        private void HeartBeat(SocketIOResponse response)
        {
            //{[{"current_rssi":[57,57,49,41],"frequency":[5658,5695,5760,5800],"loop_time":[1020,1260,1092,1136],"crossing_flag":[false,false,false,false]}]}

            lastBeat = response.GetValue<Heartbeat>();
            connected = true;
            lastBeatTime = DateTime.Now;
        }

        private void OnNodeData(SocketIOResponse response)
        {
            try
            {
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }
        }

        private void DebugLog(SocketIOResponse response)
        {
#if DEBUG
            //Logger.TimingLog.Log(this, "Debug Log: " + response.ToString());
#endif
        }
        private void OnStageReady(SocketIOResponse response)
        {
            Logger.TimingLog.Log(this, "Device started Race");
            detecting = true;

            StageReady stageReady = response.GetValue<StageReady>();
            TimeSpan time = TimeSpan.FromSeconds(stageReady.pi_starts_at_s); ;
            rotorhazardStart = serverEpoch + time;

            responseWait.Set();
        }

        private void OnLapData(SocketIOResponse response)
        {
            LapData lapData = response.GetValue<LapData>();
            
            Logger.TimingLog.Log(this, "LapData", lapData);

            DateTime passingTime = rotorhazardStart + TimeSpan.FromSeconds(lapData.lap_time);
            OnDetectionEvent?.Invoke(this, lapData.frequency, passingTime, lapData.peak_rssi);
        }

        public void OnHeartBeat(SocketIOResponse response)
        {
            try
            {
                lastBeat = response.GetValue<Heartbeat>();
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }
        }

        private void OnFrequencyData(SocketIOResponse response)
        {
            //{[{"fdata":[{"band":null,"channel":null,"frequency":5658},{"band":null,"channel":null,"frequency":5695},{"band":"R","channel":6,"frequency":5843},{"band":"R","channel":7,"frequency":5880}]}]}
            try
            {
                FrequencyDatas frequencyDatas = response.GetValue<FrequencyDatas>();
                Logger.TimingLog.Log(this, "Device listening on " + string.Join(", ", frequencyDatas.fdata.Select(r => r.ToString())));
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }
        }
        public bool StartDetection(ref DateTime time)
        {
            if (!Connected)
                return false;

            detectionStart = time;

            TimeSpan serverStartTime = time - serverEpoch;

            Logger.TimingLog.Log(this, "Start detection: Server time: " + serverStartTime.TotalSeconds);
            socketIOClient.EmitAsync("ts_race_stage", new RaceStart { start_time_s = serverStartTime.TotalSeconds }); ;

            if (!responseWait.WaitOne(CommandTimeOut))
                return false;

            time = rotorhazardStart;

            return detecting;
        }

        public bool EndDetection()
        {
            if (!Connected)
                return false;

            if (!detecting)
                return false;

            detecting = false;

            socketIOClient.EmitAsync("ts_race_stop", GotRaceStop);

            return true;
        }

        protected void GotRaceStop(SocketIOResponse response)
        {
            Logger.TimingLog.Log(this, "Device stopped Race");
        }

        public IEnumerable<RSSI> GetRSSI()
        {
            if (lastBeat.current_rssi == null ||
                lastBeat.frequency == null ||
                lastBeat.crossing_flag == null)
            {
                yield break;
            }


            int length = (new int[] {
                lastBeat.current_rssi.Length,
                lastBeat.frequency.Length,
                lastBeat.crossing_flag.Length
            }).Min();

            for (int i = 0; i < length; i++)
            {
                RSSI rssi = new RSSI()
                {
                    CurrentRSSI = lastBeat.current_rssi[i],
                    Frequency = lastBeat.frequency[i],
                    Detected = lastBeat.crossing_flag[i],
                    ScaleMax = 200,
                    ScaleMin = 20,
                    TimingSystem = this
                };

                yield return rssi;
            }
        }
    }


}
