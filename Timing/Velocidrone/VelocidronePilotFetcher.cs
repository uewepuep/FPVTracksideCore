using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tools;

namespace Timing.Velocidrone
{
    /// <summary>
    /// One-shot fetch of pilot list from Velocidrone websocket for import.
    /// </summary>
    public static class VelocidronePilotFetcher
    {
        public struct PilotInfo
        {
            public string Name { get; set; }
            public string Uid { get; set; }
        }

        /// <summary>
        /// Connect to Velocidrone, send getpilots, wait for pilotlist, return pilots. Blocks on thread pool.
        /// </summary>
        /// <param name="host">Velocidrone host (e.g. 192.168.1.100)</param>
        /// <param name="port">Port (default 60003)</param>
        /// <param name="timeoutMs">Max wait for pilotlist response</param>
        /// <returns>List of (name, uid) or null on failure</returns>
        public static List<PilotInfo> FetchPilots(string host, int port = 0, int timeoutMs = 8000)
        {
            if (string.IsNullOrWhiteSpace(host))
                return null;

            if (port <= 0)
                port = VelocidroneProtocol.DefaultPort;

            var result = new List<PilotInfo>();
            var tcs = new TaskCompletionSource<bool>();
            VelocidroneWebSocketClient client = null;

            void OnMessage(string json)
            {
                if (string.IsNullOrWhiteSpace(json))
                    return;

                if (!VelocidroneWebSocketClient.TryParseMessage(json, out var obj))
                    return;

                var keys = string.Join(", ", obj.Properties().Select(p => p.Name));
                Logger.TimingLog.Log("VelocidronePilotFetcher", "Message keys: " + keys, Logger.LogType.Notice);

                // Try multiple key variations (pilotlist, PilotList, pilotList)
                var pilotlist = obj["pilotlist"] ?? obj["PilotList"] ?? obj["pilotList"];
                if (pilotlist == null)
                    return;

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

                Logger.TimingLog.Log("VelocidronePilotFetcher", "Parsed pilotlist: " + result.Count + " pilots", Logger.LogType.Notice);
                tcs.TrySetResult(true);
            }

            void AddPilotFromToken(JToken item, List<PilotInfo> list)
            {
                var uid = item["uid"] ?? item["UID"] ?? item["Uid"];
                var name = item["name"] ?? item["Name"] ?? item["callsign"];
                var uidStr = uid?.ToString();
                var nameStr = name?.ToString();
                if (!string.IsNullOrEmpty(uidStr) && !string.IsNullOrEmpty(nameStr))
                {
                    list.Add(new PilotInfo { Name = nameStr.Trim(), Uid = uidStr.Trim() });
                }
            }

            try
            {
                client = new VelocidroneWebSocketClient(host, port);
                client.OnMessageReceived += OnMessage;

                if (!client.Connect())
                {
                    Logger.TimingLog.Log("VelocidronePilotFetcher", "Failed to connect to " + host + ":" + port, Logger.LogType.Notice);
                    return null;
                }

                // Brief delay to let connection settle before requesting pilots
                System.Threading.Thread.Sleep(300);

                client.Send(VelocidroneProtocol.CommandGetPilots());

                if (!tcs.Task.Wait(timeoutMs))
                {
                    Logger.TimingLog.Log("VelocidronePilotFetcher", "Timeout waiting for pilotlist", Logger.LogType.Notice);
                    return null;
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException("VelocidronePilotFetcher", ex);
                return null;
            }
            finally
            {
                client?.Dispose();
            }
        }
    }
}
