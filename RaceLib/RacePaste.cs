using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

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
    //       "ExternalRaceId": 12345,
    //       "Pilots": [
    //         { "Name": "Alice", "ExternalPilotId": 111 },
    //         { "Name": "Bob",   "ExternalPilotId": 222 }
    //       ]
    //     },
    //     {
    //       "ExternalRaceId": 12346,
    //       "Pilots": [ { "Name": "Carol", "ExternalPilotId": 333 } ]
    //     }
    //   ]
    //
    // This format only exists to carry external ids. Plain-text / CSV paste is
    // deliberately name-only, so a stray spreadsheet paste can never set an
    // external id (or, worse, set one to a lap time / position) by accident.
    public class PastedRace
    {
        public int ExternalRaceID { get; set; }

        public List<PastedPilot> Pilots { get; set; } = new List<PastedPilot>();

        // Returns true and the parsed races only when the clipboard genuinely
        // holds the JSON race array; any other text (including unrelated JSON)
        // falls through to the name-only paste.
        public static bool TryParsePastedRaces(string clipboardText, out List<PastedRace> races)
        {
            races = null;

            if (string.IsNullOrWhiteSpace(clipboardText))
                return false;

            // The format is a bare array; bail early on anything else so a normal
            // pilot-name paste never hits the JSON parser.
            if (!clipboardText.TrimStart().StartsWith("["))
                return false;

            try
            {
                races = JsonConvert.DeserializeObject<List<PastedRace>>(clipboardText);
            }
            catch
            {
                races = null;
                return false;
            }

            // Require it to actually look like races (at least one pilot somewhere)
            // so a random array of strings/numbers isn't treated as a paste.
            if (races == null || races.Any(r => r == null) ||
                !races.Any(r => r.Pilots != null && r.Pilots.Count > 0))
            {
                races = null;
                return false;
            }

            return true;
        }
    }

    public class PastedPilot
    {
        public string Name { get; set; }

        public int ExternalPilotID { get; set; }

        // Optional VTX channel label for this seat, e.g. "R1", "F3", "L2", or a
        // band+number like "Raceband 1". When present (and resolvable against the
        // event's channels), the pilot is assigned to that channel instead of the
        // auto-cycled one. Empty/unresolvable falls back to auto-assignment.
        public string Channel { get; set; }
    }
}
