using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace RaceLib
{
    // JSON clipboard format for pasting whole races into an event.
    //
    // The clipboard holds a bare JSON array of races. Each race carries its
    // external race id ONCE and a list of pilots (name + external pilot id),
    // so there is no need to repeat the race id on every pilot row and no need
    // to guess which row is authoritative ("the top runner"):
    //
    //   [
    //     {
    //       "externalRaceId": 12345,
    //       "pilots": [
    //         { "name": "Alice", "externalPilotId": 111 },
    //         { "name": "Bob",   "externalPilotId": 222 }
    //       ]
    //     },
    //     {
    //       "externalRaceId": 12346,
    //       "pilots": [ { "name": "Carol", "externalPilotId": 333 } ]
    //     }
    //   ]
    //
    // This format only exists to carry external ids. Plain-text / CSV paste is
    // deliberately name-only, so a stray spreadsheet paste can never set an
    // external id (or, worse, set one to a lap time / position) by accident.
    public class PastedRace
    {
        [JsonProperty("externalRaceId")]
        public int ExternalRaceID { get; set; }

        [JsonProperty("pilots")]
        public List<PastedPilot> Pilots { get; set; } = new List<PastedPilot>();
    }

    public class PastedPilot
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("externalPilotId")]
        public int ExternalPilotID { get; set; }
    }

    // A race from a paste after its pilots have been matched to event pilots and
    // assigned channels. Produced from either the JSON format above or the legacy
    // name-only line paste, so every paste site consumes one shape.
    public class ResolvedRace
    {
        public int ExternalRaceID { get; set; }

        public List<Tuple<Pilot, Channel>> PilotChannels { get; } = new List<Tuple<Pilot, Channel>>();
    }
}
