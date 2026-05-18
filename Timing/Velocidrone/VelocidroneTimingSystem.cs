using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using Tools;

namespace Timing.Velocidrone
{
    public class VelocidroneTimingSystem : ITimingSystem
    {
        public TimingSystemType Type => TimingSystemType.Velocidrone;

        public bool Connected => _client?.IsConnected ?? false;

        public event DetectionEventDelegate OnDetectionEvent;
        public event MarshallEventDelegate OnMarshallEvent;

        /// <summary>Fired for every racedata update with gate info. Use for UI display. (frequency, gate, lap, timeFromStartSec)</summary>
        public event Action<int, int, int, double> OnGatePassed;

        public VelocidroneSettings Settings { get; private set; }
        TimingSystemSettings ITimingSystem.Settings { get => Settings; set => Settings = value as VelocidroneSettings; }

        public int MaxPilots => 8;

        public string Name => "VD";

        private VelocidroneWebSocketClient _client;
        private readonly Dictionary<string, int> _uidToFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _lastLapByUid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly object _mappingLock = new object();
        private DateTime _raceStartTime;
        private bool _detecting;
        private Timer _keepaliveTimer;
        private System.Threading.Tasks.TaskCompletionSource<List<VelocidronePilotFetcher.PilotInfo>> _pilotListTcs;

        public VelocidroneTimingSystem()
        {
            Settings = new VelocidroneSettings();
        }

        public void Dispose()
        {
            Disconnect();
        }

        public bool Connect()
        {
            if (Settings == null || string.IsNullOrWhiteSpace(Settings.HostName))
                return false;

            _client = new VelocidroneWebSocketClient(Settings.HostName, Settings.Port);
            _client.OnMessageReceived += OnMessageReceived;
            _client.OnDisconnected += OnDisconnected;

            if (_client.Connect())
            {
                StartKeepalive();
                return true;
            }

            _client.Dispose();
            _client = null;
            return false;
        }

        public bool Disconnect()
        {
            StopKeepalive();
            _client?.Dispose();
            _client = null;
            return true;
        }

        private void StartKeepalive()
        {
            StopKeepalive();
            _keepaliveTimer = new Timer(_ =>
            {
                if (_client?.IsConnected == true)
                {
                    try
                    {
                        _client.Send("{\"command\":\"ping\"}");
                    }
                    catch { }
                }
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        private void StopKeepalive()
        {
            _keepaliveTimer?.Dispose();
            _keepaliveTimer = null;
        }

        private void OnDisconnected()
        {
            Logger.TimingLog.Log(this, "Velocidrone websocket disconnected", Logger.LogType.Notice);
        }

        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> newFrequencies)
        {
            lock (_mappingLock)
            {
                _uidToFrequency.Clear();
                foreach (var lf in newFrequencies ?? Enumerable.Empty<ListeningFrequency>())
                {
                    if (!string.IsNullOrEmpty(lf.SimulatorPilotId))
                    {
                        _uidToFrequency[lf.SimulatorPilotId] = lf.Frequency;
                    }
                }
            }

            if (Connected && newFrequencies != null)
            {
                // First set ALL pilots in Velocidrone to spectate
                _client?.Send(VelocidroneProtocol.CommandAllSpectate());

                // Then activate ONLY the pilots in this race who have a Velocidrone UID
                var uids = newFrequencies
                    .Where(lf => !string.IsNullOrEmpty(lf.SimulatorPilotId))
                    .Select(lf => lf.SimulatorPilotId)
                    .ToArray();
                if (uids.Length > 0)
                {
                    _client?.Send(VelocidroneProtocol.CommandActivate(uids));
                }
            }

            return true;
        }

        public bool StartDetection(ref DateTime time, StartMetaData startMetaData)
        {
            _raceStartTime = time;
            _detecting = true;
            lock (_mappingLock)
            {
                _lastLapByUid.Clear();
            }

            if (!Connected)
                return false;

            _client?.Send(VelocidroneProtocol.CommandStartRace());
            return true;
        }

        public bool EndDetection(EndDetectionType type)
        {
            _detecting = false;

            if (Connected)
            {
                _client?.Send(VelocidroneProtocol.CommandAbortRace());
            }

            return true;
        }

        public IEnumerable<StatusItem> Status
        {
            get
            {
                if (Connected)
                {
                    yield return new StatusItem { StatusOK = true, Value = "Connected" };
                }
                else
                {
                    yield return new StatusItem { StatusOK = false, Value = "Discon" };
                }
            }
        }

        private void OnMessageReceived(string json)
        {
            if (!VelocidroneWebSocketClient.TryParseMessage(json, out var obj))
            {
                Logger.TimingLog.Log(this, "Unknown message format: " + json?.Substring(0, Math.Min(50, json?.Length ?? 0)), Logger.LogType.Notice);
                return;
            }

            if (obj["racedata"] != null)
                HandleRacedata(obj);
            else if (obj["racestatus"] != null || obj["raceAction"] != null)
                HandleRacestatus(obj);
            else if (obj["pilotlist"] != null)
                HandlePilotlist(obj);
        }

        private void HandleRacedata(JObject obj)
        {
            var racedataToken = obj["racedata"];
            if (racedataToken == null) return;

            // racedata can be object (keys=pilot names) or array of {uid, lap, time, gate, finished}
            if (racedataToken is JObject racedataObj)
            {
                foreach (var kv in racedataObj.Properties())
                {
                    if (kv.Value is JObject pilotData)
                        ProcessRacedataPilot(pilotData);
                }
            }
            else if (racedataToken is JArray racedataArr)
            {
                foreach (var item in racedataArr)
                {
                    if (item is JObject pilotData)
                        ProcessRacedataPilot(pilotData);
                }
            }
        }

        private void ProcessRacedataPilot(JObject pilotData)
        {
            var uid = pilotData["uid"]?.ToString();
            if (string.IsNullOrEmpty(uid)) return;

            var timeSec = pilotData["time"]?.Value<double>() ?? 0.0;
            int frequency;
            int newLap;
            lock (_mappingLock)
            {
                if (!_uidToFrequency.TryGetValue(uid, out frequency))
                    return; // Not in our race - expected for spectating pilots

                newLap = pilotData["lap"]?.Value<int>() ?? 0;
                var finished = pilotData["finished"]?.Value<bool>() ?? false;

                // Gate display: try "gate" or "Gate" (JSON casing varies)
                var gate = pilotData["gate"]?.Value<int>() ?? pilotData["Gate"]?.Value<int>() ?? 0;
                if (OnGatePassed != null && _detecting)
                    OnGatePassed.Invoke(frequency, gate, newLap, timeSec);

                // Lap detection: only fire when lap number increases (new lap completion) or pilot finished
                _lastLapByUid.TryGetValue(uid, out int lastLap);
                if (newLap <= lastLap && !finished)
                    return;

                _lastLapByUid[uid] = newLap;
            }

            var detectionTime = _raceStartTime.AddSeconds(timeSec);

            if (!_detecting) return;

            OnDetectionEvent?.Invoke(this, frequency, detectionTime, 0);
        }

        private void HandleRacestatus(JObject obj)
        {
            var action = obj["raceAction"]?.ToString() ?? obj["racestatus"]?.ToString();
            Logger.TimingLog.Log(this, "Racestatus: " + action, Logger.LogType.Notice);
        }

        private void HandlePilotlist(JObject obj)
        {
            var pilotlist = obj["pilotlist"] ?? obj["PilotList"] ?? obj["pilotList"];
            var result = new List<VelocidronePilotFetcher.PilotInfo>();

            if (pilotlist is JArray arr)
            {
                foreach (var item in arr)
                {
                    AddPilotFromToken(item, result);
                }
            }
            else if (pilotlist is JObject pilotObj)
            {
                foreach (var kv in pilotObj.Properties())
                {
                    AddPilotFromToken(kv.Value, result);
                }
            }

            Logger.TimingLog.Log(this, "Pilotlist received, " + result.Count + " pilots", Logger.LogType.Notice);

            var tcs = System.Threading.Interlocked.Exchange(ref _pilotListTcs, null);
            tcs?.TrySetResult(result);
        }

        private static void AddPilotFromToken(JToken item, List<VelocidronePilotFetcher.PilotInfo> list)
        {
            var uid = item["uid"] ?? item["UID"] ?? item["Uid"];
            var name = item["name"] ?? item["Name"] ?? item["callsign"];
            var uidStr = uid?.ToString();
            var nameStr = name?.ToString();
            if (!string.IsNullOrEmpty(uidStr) && !string.IsNullOrEmpty(nameStr))
            {
                list.Add(new VelocidronePilotFetcher.PilotInfo { Name = nameStr.Trim(), Uid = uidStr.Trim() });
            }
        }

        /// <summary>
        /// Request pilot list over the existing connection. Use this when already connected to avoid a second connection.
        /// </summary>
        public List<VelocidronePilotFetcher.PilotInfo> RequestPilotList(int timeoutMs = 8000)
        {
            if (!Connected || _client == null)
                return null;

            var tcs = new System.Threading.Tasks.TaskCompletionSource<List<VelocidronePilotFetcher.PilotInfo>>();
            _pilotListTcs = tcs;

            try
            {
                _client.Send(VelocidroneProtocol.CommandGetPilots());
                if (tcs.Task.Wait(timeoutMs))
                    return tcs.Task.Result;
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _pilotListTcs, null);
            }

            return null;
        }
    }
}
