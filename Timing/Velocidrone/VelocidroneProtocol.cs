using System.Collections.Generic;
using Newtonsoft.Json;

namespace Timing.Velocidrone
{
    /// <summary>
    /// DTOs for Velocidrone websocket protocol. Protocol inferred from rh-velocidrone reference.
    /// All assumptions about message structure are isolated here.
    /// </summary>
    public static class VelocidroneProtocol
    {
        public const string EndpointPath = "/velocidrone";
        public const int DefaultPort = 60003;

        #region Outbound commands

        public static string CommandStartRace() => JsonConvert.SerializeObject(new { command = "startrace" });
        public static string CommandAbortRace() => JsonConvert.SerializeObject(new { command = "abortrace" });
        /// <summary>Set all pilots in Velocidrone to spectate. Call before activate so only race pilots fly.</summary>
        public static string CommandAllSpectate() => JsonConvert.SerializeObject(new { command = "allspectate" });
        public static string CommandActivate(IEnumerable<string> pilotUids) =>
            JsonConvert.SerializeObject(new { command = "activate", pilots = pilotUids });
        public static string CommandGetPilots() => JsonConvert.SerializeObject(new { command = "getpilots" });

        #endregion

        #region Inbound message DTOs

        public class RacedataPilot
        {
            [JsonProperty("uid")]
            public string Uid { get; set; }

            [JsonProperty("lap")]
            public int Lap { get; set; }

            [JsonProperty("time")]
            public double Time { get; set; }

            [JsonProperty("gate")]
            public int Gate { get; set; }

            [JsonProperty("finished")]
            public bool Finished { get; set; }
        }

        public class RacedataMessage
        {
            [JsonProperty("racedata")]
            public RacedataPilot[] Racedata { get; set; }
        }

        public class RacestatusMessage
        {
            [JsonProperty("racestatus")]
            public string RaceStatus { get; set; }

            [JsonProperty("raceAction")]
            public string RaceAction { get; set; }
        }

        public class PilotlistMessage
        {
            [JsonProperty("pilotlist")]
            public PilotlistEntry[] PilotList { get; set; }
        }

        public class PilotlistEntry
        {
            [JsonProperty("uid")]
            public string Uid { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }
        }

        public class FinishGateMessage
        {
            [JsonProperty("FinishGate")]
            public object FinishGate { get; set; }
        }

        #endregion
    }
}
