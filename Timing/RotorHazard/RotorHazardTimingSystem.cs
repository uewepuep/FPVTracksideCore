﻿using Newtonsoft.Json;
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
using SocketIOClient.JsonSerializer;
using System.Text.Json;



namespace Timing.RotorHazard
{
    public struct PilotInfo
    {
        public Guid ID { get; private set; }
        public string Name { get; private  set; }

        public PilotInfo(Guid id, string name)
        {
            ID = id;
            Name = name;
        }
    }

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

        private double voltage;
        private double temperature;

        private bool detecting;

        private Heartbeat lastBeat;
        private DateTime lastBeatTime;

        private RotorHazardSettings settings;
        public TimingSystemSettings Settings { get { return settings; } set { settings = value as RotorHazardSettings; } }

        public event DetectionEventDelegate OnDetectionEvent;

        private SocketIO socket;

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
                return 8;
            }
        }

        public IEnumerable<StatusItem> Status
        {
            get
            {
                if (connected)
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

                    int len = 5;
                    if (ServerInfo.release_version.Length > len)
                    {
                        yield return new StatusItem() { StatusOK = true, Value = "V" + ServerInfo.release_version.Substring(0, len) };
                    }

                }
                else
                {
                    yield return new StatusItem() { StatusOK = false, Value = "Discon" };
                }
                
            }
        }

        private int connectionCount;


        public TimeSpan TimeOut { get; set; }
        public TimeSpan CommandTimeOut { get; set; }

        public ServerInfo ServerInfo { get; private set; }


        private const int MaxTimeSamples = 20;

        private DateTime rotorhazardStart;

        private RaceStartPilots raceStartPilots;

        public string Name
        {
            get
            {
                return "RH";
            }
        }

        public event MarshallEventDelegate OnMarshallEvent;

        public RotorHazardTimingSystem()
        {
            settings = new RotorHazardSettings();
            serverTimeSamples = new List<ServerTimeSample>();
            TimeOut = TimeSpan.FromSeconds(10);
            CommandTimeOut = TimeSpan.FromSeconds(10);
        }
        
        public void Dispose()
        {
            Disconnect();
        }

        public bool Connect()
        {
            try
            {
                bool result = false;
                using (Waiter reponseWaiter = new Waiter())
                {
                    socket = new SocketIO("http://" + settings.HostName + ":" + settings.Port);
                    socket.OnConnected += (object sender, EventArgs e) =>
                    {
                        if (reponseWaiter.IsDisposed)
                            return;

                        try
                        {
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
                            socket.On("ts_race_marshal", OnRaceMarshal);

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
                    };

                    lastBeatTime = DateTime.Now;
                    socket?.ConnectAsync();
                    result = reponseWaiter.WaitOne(TimeOut);
                }
                return result;
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }

            return false;
        }

        public bool Disconnect()
        {
            connected = false;

            if (socket == null)
                return true;

            socket?.Dispose();
            socket = null;

            return true;
        }

        private struct FrequencySetup
        {
            public char[] b { get; set; } //Band
            public int[] c { get; set; }//Channel (number)
            public int[] f { get; set; }//frequency
        }

        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> newFrequencies)
        {
            if (!Connected)
                return false;

            detecting = false;

            Logger.TimingLog.Log(this, "SetListeningFrequencies", string.Join(", ", newFrequencies.Select(f => f.ToString())));

            FrequencySetup frequencySetup = new FrequencySetup();
            frequencySetup.b = newFrequencies.Select(nf => nf.Band[0]).ToArray();
            frequencySetup.c = newFrequencies.Select(nf => nf.Channel).ToArray();
            frequencySetup.f = newFrequencies.Select(nf => nf.Frequency).ToArray();

            raceStartPilots = new RaceStartPilots()
            {
                p_id = newFrequencies.Select(r => r.PilotId.ToString()).ToArray(),
                p = newFrequencies.Select(r => r.Pilot).ToArray(),
                p_color = newFrequencies.Select(r => r.Color.ToHex()).ToArray()
               
            };

            try
            {
                using (Waiter responseWait = new Waiter())
                {
                    socket?.EmitAsync("ts_frequency_setup", (SocketIOResponse reponse) => 
                    {
                        if (responseWait.IsDisposed)
                            return;

                        responseWait.Set(); 

                    }, frequencySetup);
                    if (!responseWait.WaitOne(TimeOut))
                    {
                        Logger.TimingLog.Log(this, "Set Frequencies took too long");
                        return false;
                    }
                }

                if (!TimeSync())
                {
                    Logger.TimingLog.Log(this, "Time Sync Failure");
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                socket?.DisconnectAsync();
                Logger.TimingLog.LogException(this, e);
            }
            return false;
        }

        private void OnServerInfo(SocketIOResponse response)
        {
            ServerInfo = response.GetValue<ServerInfo>();
        }

        public bool TimeSync()
        {
            SocketIO socket = this.socket;
            if (socket == null)
                return false;

            Logger.TimingLog.Log(this, "Syncing Server time");

            lock (serverTimeSamples)
            {
                serverTimeSamples.Clear();
            }

            serverTimeStart = Monotonic;

            using (Waiter responseWait = new Waiter())
            {
                socket?.EmitAsync("ts_server_time", (response) => OnServerTime(response, responseWait));

                if (!responseWait.WaitOne(TimeOut))
                {
                    Logger.TimingLog.Log(this, "Time sync took too long");
                    return false;
                }
            }

            int count = 0;
            lock (serverTimeSamples)
            {
                count = serverTimeSamples.Count;
            }
            return count > 0;
        }

        private void OnServerTime(SocketIOResponse response, Waiter responseWait)
        {
            if (response.Count == 0 || responseWait.IsDisposed) return;

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
                        socket?.EmitAsync("ts_server_time", (response) => OnServerTime(response, responseWait));
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

        public bool StartDetection(ref DateTime time, StartMetaData startMetaData)
        {
            if (!Connected)
                return false;

            TimeSpan serverStartTime = time - serverEpoch;

            Logger.TimingLog.Log(this, "Start detection: Server time: " + serverStartTime.TotalSeconds);

            using (Waiter responseWait = new Waiter())
            {
                if (!settings.SyncPilotNames)
                {
                    raceStartPilots = new RaceStartPilots();
                }

                raceStartPilots.start_time_s = serverStartTime.TotalSeconds;
                raceStartPilots.race_id = startMetaData.RaceId;
                raceStartPilots.race_number = startMetaData.RaceNumber;
                raceStartPilots.round_number = startMetaData.RoundNumber;
                raceStartPilots.race_name = startMetaData.RaceName;
                raceStartPilots.bracket = startMetaData.Bracket;


                socket?.EmitAsync("ts_race_stage", (r) =>
                { 
                    if (responseWait.IsDisposed) 
                        return; 

                    responseWait.Set(); 
                }, raceStartPilots);

                if (!responseWait.WaitOne(CommandTimeOut))
                {
                    Logger.TimingLog.Log(this, "Start detection: Timed out, no response from RH.");
                    return false;
                }
            }

            // Do NOT overwrite 'time' with 'rotorhazardStart' here.
            // FPVTrackside's start time is authoritative for race records and YouTube timestamps.
            //
            // Bug fix: When a race is stopped and restarted, 'rotorhazardStart' contains the OLD
            // start time (from before the stop), causing the restarted race to have an incorrect
            // start time offset by the duration between stop and restart (typically ~30 seconds).
            // This breaks Race.Start, lap times, holeshot detection, and YouTube chapter markers.
            //
            // 'rotorhazardStart' is set by OnStageReady callback (which fires asynchronously after
            // this method returns) and is only used in OnLapData for lap time calculations.

            return detecting;
        }

        public bool EndDetection(EndDetectionType type)
        {
            if (!Connected)
                return false;

            detecting = false;

            SocketIO s = socket;
            if (s == null)
                return false;

            switch (type)
            {
                case EndDetectionType.Abort:

                    using (AutoResetEvent wait = new AutoResetEvent(false))
                    {
                        s.EmitAsync("ts_race_abort", (r) =>
                        {
                            wait.Set();
                            Logger.TimingLog.Log(this, "RH aborted Race");
                        });

                        // If we don't get an abort response, might be an old version, do ts_stop.
                        if (!wait.WaitOne(1000))
                        {
                            Logger.TimingLog.Log(this, "RH no abort fn..");

                            s.EmitAsync("ts_race_stop", (r) =>
                            {
                                Logger.TimingLog.Log(this, "RH stopped Race");
                            });
                        }
                    }
                    
                    break;
                case EndDetectionType.Normal:
                    s.EmitAsync("ts_race_stop", (r) =>
                    {
                        Logger.TimingLog.Log(this, "RH stopped Race");
                    });
                    break;
            }

            return true;
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

        private void OnRaceMarshal(SocketIOResponse response)
        {
            try
            {
                var text = response.ToString();
                List<RaceMarshalData> raceMarshalDataList = new List<RaceMarshalData>();

                using (JsonDocument doc = JsonDocument.Parse(text))
                {
                    JsonElement root = doc.RootElement;

                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        // Case 1: Response is an array of objects
                        foreach (JsonElement element in root.EnumerateArray())
                        {
                            raceMarshalDataList.Add(ParseRaceMarshalData(element));
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        // Case 2: Response is a single object
                        raceMarshalDataList.Add(ParseRaceMarshalData(root));
                    }
                    else
                    {
                        throw new InvalidOperationException("Unexpected JSON format.");
                    }
                }

                foreach (var rhmarshalData in raceMarshalDataList)
                {
                    Logger.TimingLog.Log(this, "Received race marshal data for pilot: " + rhmarshalData.callsign);

                    RaceMarshalLap[] orderedLaps = rhmarshalData.laps.OrderBy(l => l.lap_time_stamp).ToArray();

                    // Copy to generic data types.

                    MarshalData data = new MarshalData();
                    data.PilotName = rhmarshalData.callsign;
                    data.PilotID = Guid.Parse(rhmarshalData.ts_pilot_id);
                    data.RaceID = rhmarshalData.race_id;
                    data.Laps = new MarshalLap[rhmarshalData.laps.Count];

                    for (int i = 0; i < orderedLaps.Length; i++)
                    {
                        RaceMarshalLap raceMarshalLap = orderedLaps[i];

                        MarshalLap marshalLap = new MarshalLap();
                        marshalLap.LapNumber = i; // Account for non-Holeshots here?
                        marshalLap.Valid = !raceMarshalLap.deleted;
                        marshalLap.Length = TimeSpan.FromMilliseconds(raceMarshalLap.lap_time);
                        marshalLap.RaceTime = TimeSpan.FromMilliseconds(raceMarshalLap.lap_time_stamp);

                        data.Laps[i] = marshalLap;
                    }

                    Logger.TimingLog.Log(this, "Received race marshal data for pilot: " + rhmarshalData.callsign);
                    OnMarshallEvent?.Invoke(this, data);
                    break; 
                }
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }
        }

        private RaceMarshalData ParseRaceMarshalData(JsonElement element)
        {
            var raceData = new RaceMarshalData
            {
                race_id = element.GetProperty("race_id").GetGuid(),
                callsign = element.GetProperty("callsign").GetString(),
                ts_pilot_id = element.GetProperty("ts_pilot_id").GetString(),
                laps = new List<RaceMarshalLap>()
            };

            var lapsElement = element.GetProperty("laps");

            if (lapsElement.ValueKind == JsonValueKind.String)
            {
                // "laps" is a stringified JSON array -> Deserialize it
                raceData.laps = System.Text.Json.JsonSerializer.Deserialize<List<RaceMarshalLap>>(lapsElement.GetString());
            }
            else if (lapsElement.ValueKind == JsonValueKind.Array)
            {
                // "laps" is a proper JSON array -> Deserialize directly
                raceData.laps = System.Text.Json.JsonSerializer.Deserialize<List<RaceMarshalLap>>(lapsElement.GetRawText());
            }

            return raceData;
        }

    }
}
