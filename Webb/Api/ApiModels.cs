using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;

namespace Webb.Api
{
    // Documentation for a model or one of its fields. The OpenAPI document is
    // built from these by reflection (ApiDocs), so a field and its description
    // are always in the same place.
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
    public class DocAttribute : Attribute
    {
        public string Text { get; private set; }
        public string Example { get; set; }
        public bool Required { get; set; }
        public string[] Values { get; set; }

        public DocAttribute(string text) { Text = text; }
    }

    public static class ApiJson
    {
        // camelCase field names, enums as strings, times as ISO 8601 with the
        // venue machine's UTC offset (FPVTrackside records local time).
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Include,
            DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffzzz",
            Converters = new List<JsonConverter> { new StringEnumConverter() },
        };

        public static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static string Serialize(object o)
        {
            return JsonConvert.SerializeObject(o, Formatting.Indented, Settings);
        }
    }

    // ───────────────────────── responses ─────────────────────────

    [Doc("Every error response. HTTP status says what kind; `code` is stable for programs, `error` is for people.")]
    public class ApiErrorDto
    {
        [Doc("Always false.")] public bool Ok { get; set; }
        [Doc("Machine-readable code.", Values = new[] { "bad_request", "unauthorized", "forbidden", "too_many_attempts", "not_found", "method_not_allowed", "conflict", "race_control_disabled", "busy", "internal" })]
        public string Code { get; set; }
        [Doc("What went wrong, in words.")] public string Error { get; set; }
    }

    [Doc("Result of an action that returns nothing else.")]
    public class OkDto
    {
        [Doc("True when the action was carried out.")] public bool Ok { get; set; } = true;
        [Doc("Extra detail, when there is any.")] public string Message { get; set; }
    }

    [Doc("What this server is and what it is running.")]
    public class ApiInfoDto
    {
        [Doc("Always \"FPVTrackside\".")] public string Product { get; set; }
        [Doc("FPVTrackside build version.")] public string Version { get; set; }
        [Doc("API version. Breaking changes get a new /api/vN prefix.")] public int ApiVersion { get; set; }
        [Doc("ID of the event open in FPVTrackside.")] public Guid EventId { get; set; }
        [Doc("Name of the open event.")] public string EventName { get; set; }
        [Doc("Whether this caller may make any changes.")] public bool CanWrite { get; set; }
        [Doc("What this caller may change: none (read only), seeder, operator or admin. See /auth/me.", Values = new[] { "none", "seeder", "operator", "admin" })] public string Role { get; set; }
        [Doc("Who this caller is: a user's name, \"This computer\" or FPV_API_TOKEN. Empty when not signed in.")] public string SignedInAs { get; set; }
        [Doc("How this caller was recognised.", Values = new[] { "this-computer", "session", "key", "environment-token", "anonymous" })] public string SignedInVia { get; set; }
        [Doc("Whether race start/stop over HTTP is switched on in FPVTrackside's settings.")] public bool RaceControlEnabled { get; set; }
        [Doc("Human-readable documentation page.")] public string Docs { get; set; }
        [Doc("Machine-readable OpenAPI 3 document.")] public string OpenApi { get; set; }
    }

    [Doc("A video channel.")]
    public class ChannelDto
    {
        [Doc("Channel ID.")] public Guid Id { get; set; }
        [Doc("Short label as FPVTrackside shows it.", Example = "R1")] public string Label { get; set; }
        [Doc("Band.", Example = "Raceband")] public string Band { get; set; }
        [Doc("Channel number within the band.", Example = "1")] public int Number { get; set; }
        [Doc("Frequency in MHz.", Example = "5658")] public int Frequency { get; set; }
    }

    [Doc("The event open in FPVTrackside.")]
    public class EventDto
    {
        [Doc("Event ID.")] public Guid Id { get; set; }
        [Doc("Event name.")] public string Name { get; set; }
        [Doc("Default race type for new rounds.", Example = "Race")] public string EventType { get; set; }
        [Doc("Laps per race.")] public int Laps { get; set; }
        [Doc("Consecutive laps used for personal bests.")] public int PbLaps { get; set; }
        [Doc("Race length in seconds (for timed formats).")] public double RaceLengthSeconds { get; set; }
        [Doc("Laps shorter than this are ignored, in seconds.")] public double MinLapTimeSeconds { get; set; }
        [Doc("Packs a pilot may fly before FPVTrackside warns (0 = no limit).")] public int PackLimit { get; set; }
        [Doc("Where the lap line is relative to the start.", Example = "EndOfLap")] public string PrimaryTimingSystemLocation { get; set; }
        [Doc("When true, laps / race length can't be changed.")] public bool RulesLocked { get; set; }
        [Doc("Event start date.")] public DateTime Start { get; set; }
        [Doc("The event's channels, in order.")] public List<ChannelDto> Channels { get; set; }
        [Doc("Number of pilots in the event.")] public int PilotCount { get; set; }
    }

    [Doc("A pilot in the event.")]
    public class PilotDto
    {
        [Doc("Pilot ID.")] public Guid Id { get; set; }
        [Doc("Name (callsign) as shown in FPVTrackside.")] public string Name { get; set; }
        [Doc("First name.")] public string FirstName { get; set; }
        [Doc("Last name.")] public string LastName { get; set; }
        [Doc("Discord user ID.")] public string DiscordId { get; set; }
        [Doc("Aircraft.")] public string Aircraft { get; set; }
        [Doc("Catch phrase.")] public string CatchPhrase { get; set; }
        [Doc("Practice pilot (not scored).")] public bool PracticePilot { get; set; }
        [Doc("Velocidrone user ID.")] public string VelocidroneUid { get; set; }
        [Doc("The pilot's default channel for this event.")] public ChannelDto Channel { get; set; }
        [Doc("Only in GET /pilots/database: whether the pilot is in the open event.")] public bool? InEvent { get; set; }
        [Doc("An external system's ID for the pilot (0 = none). Shared with FPVTrackside's MultiGP ID field and the bracket paste. Address the pilot as `ext:<id>`.")] public int ExternalId { get; set; }
    }

    [Doc("A round: a group of races of one type.")]
    public class RoundDto
    {
        [Doc("Round ID.")] public Guid Id { get; set; }
        [Doc("Round number. Numbered per race type, so Practice round 1 and Race round 1 can both exist.")] public int Number { get; set; }
        [Doc("Custom name, if set.")] public string Name { get; set; }
        [Doc("Race type.", Example = "TimeTrial")] public string EventType { get; set; }
        [Doc("Running order of the round in the event (the order FPVTrackside shows).")] public int Order { get; set; }
        [Doc("Stage name, if the round belongs to one.")] public string Stage { get; set; }
        [Doc("Number of races in the round.")] public int RaceCount { get; set; }
        [Doc("An external system's ID for the round (0 = none). Address the round as `ext:<id>`.")] public int ExternalId { get; set; }
    }

    [Doc("A pilot's seat in a race.")]
    public class RacePilotDto
    {
        [Doc("Pilot ID.")] public Guid PilotId { get; set; }
        [Doc("Pilot name.")] public string Name { get; set; }
        [Doc("Channel the pilot flies in this race.")] public ChannelDto Channel { get; set; }
    }

    [Doc("A race (heat), without its laps.")]
    public class RaceSummaryDto
    {
        [Doc("Race ID.")] public Guid Id { get; set; }
        [Doc("Round ID.")] public Guid RoundId { get; set; }
        [Doc("Round number.")] public int RoundNumber { get; set; }
        [Doc("Race number within the round.")] public int RaceNumber { get; set; }
        [Doc("Name as FPVTrackside shows it.", Example = "Race 1-2")] public string Name { get; set; }
        [Doc("Race type.", Example = "Race")] public string EventType { get; set; }
        [Doc("pending = not run yet, running, finished.", Values = new[] { "pending", "running", "finished" })] public string State { get; set; }
        [Doc("True when this is the race selected in FPVTrackside.")] public bool IsCurrent { get; set; }
        [Doc("When the race started (null if it hasn't).")] public DateTime? Start { get; set; }
        [Doc("When the race ended (null if it hasn't).")] public DateTime? End { get; set; }
        [Doc("Laps to finish.")] public int TargetLaps { get; set; }
        [Doc("Bracket, for double-elimination formats.", Example = "None")] public string Bracket { get; set; }
        [Doc("An external system's ID for the race (0 = none), e.g. the AUFPV platform's bracket race. Address the race as `ext:<id>`.")] public int ExternalId { get; set; }
        [Doc("Pilots and channels.")] public List<RacePilotDto> Pilots { get; set; }
    }

    [Doc("A race (heat) with its laps and results.")]
    public class RaceDto : RaceSummaryDto
    {
        [Doc("Every lap, valid and invalid, ordered by time.")] public List<LapDto> Laps { get; set; }
        [Doc("Results, best first. Empty until the race has results.")] public List<ResultDto> Results { get; set; }
    }

    [Doc("A lap: the time between two lap-line crossings (or the race start and the first one).")]
    public class LapDto
    {
        [Doc("Lap ID.")] public Guid Id { get; set; }
        [Doc("ID of the lap-line crossing that ended the lap. Use it to change the lap's validity.")] public Guid DetectionId { get; set; }
        [Doc("Pilot ID.")] public Guid PilotId { get; set; }
        [Doc("Lap number. 0 is the holeshot (start to the first crossing).")] public int Number { get; set; }
        [Doc("Whether the lap counts.")] public bool Valid { get; set; }
        [Doc("When the lap started.")] public DateTime Start { get; set; }
        [Doc("When the lap ended.")] public DateTime End { get; set; }
        [Doc("Lap time in seconds.")] public double LengthSeconds { get; set; }
        [Doc("Time from the race start to the end of the lap, in seconds.")] public double RaceTimeSeconds { get; set; }
    }

    [Doc("One timing-system crossing: a lap line (isLapEnd) or a split gate.")]
    public class DetectionDto
    {
        [Doc("Detection ID.")] public Guid Id { get; set; }
        [Doc("Pilot ID.")] public Guid PilotId { get; set; }
        [Doc("When the crossing happened.")] public DateTime Time { get; set; }
        [Doc("Milliseconds after the race start (negative before it).")] public long RaceTimeMs { get; set; }
        [Doc("True for the lap line, false for a split gate.")] public bool IsLapEnd { get; set; }
        [Doc("0 = lap line, 1+ = split gate number.")] public int TimingSystemIndex { get; set; }
        [Doc("What recorded it.", Example = "RotorHazard")] public string TimingSystemType { get; set; }
        [Doc("Lap number the crossing belongs to.")] public int LapNumber { get; set; }
        [Doc("Whether it counts.")] public bool Valid { get; set; }
        [Doc("Who last set the validity.", Values = new[] { "Auto", "ManualOverride", "Marshall" })] public string ValidityType { get; set; }
        [Doc("Signal peak reported by the timer (0 if none).")] public int Peak { get; set; }
    }

    [Doc("A pilot's result in a race.")]
    public class ResultDto
    {
        [Doc("Pilot ID.")] public Guid PilotId { get; set; }
        [Doc("Pilot name.")] public string Name { get; set; }
        [Doc("Finishing position.")] public int Position { get; set; }
        [Doc("Points awarded.")] public int Points { get; set; }
        [Doc("Laps completed.")] public int LapsFinished { get; set; }
        [Doc("Total time in seconds.")] public double TimeSeconds { get; set; }
        [Doc("Did not finish.")] public bool Dnf { get; set; }
    }

    [Doc("The race director's state: which race is selected and what it is doing.")]
    public class RaceControlDto
    {
        [Doc("none = no race selected, ready = selected and not started, starting = start countdown, running, finished.", Values = new[] { "none", "ready", "starting", "running", "finished" })]
        public string State { get; set; }
        [Doc("Whether a start would be accepted now.")] public bool CanStart { get; set; }
        [Doc("Whether race start/stop over HTTP is switched on in FPVTrackside's settings.")] public bool RaceControlEnabled { get; set; }
        [Doc("Seconds the current race has been running (0 if it isn't).")] public double ElapsedSeconds { get; set; }
        [Doc("The selected race, or null.")] public RaceSummaryDto CurrentRace { get; set; }
        [Doc("The race after the selected one, or null.")] public RaceSummaryDto NextRace { get; set; }
    }

    [Doc("Result of a lap correction.")]
    public class LapCorrectionResultDto
    {
        [Doc("True when the correction was applied (or had already been).")] public bool Ok { get; set; }
        [Doc("Empty on success.")] public string Error { get; set; }
        [Doc("Lap-line crossings marked invalid.")] public int Invalidated { get; set; }
        [Doc("Lap-line crossings added. 0 for a repeat of a correction already applied.")] public int Added { get; set; }
        [Doc("The pilot's laps in the race after the change.")] public List<LapDto> Laps { get; set; }
    }

    [Doc("The pilot's laps in a race after a change.")]
    public class PilotLapsDto
    {
        [Doc("Race ID.")] public Guid RaceId { get; set; }
        [Doc("Pilot ID.")] public Guid PilotId { get; set; }
        [Doc("Crossings this call created.")] public List<DetectionDto> Created { get; set; }
        [Doc("All the pilot's laps in the race, ordered by time.")] public List<LapDto> Laps { get; set; }
    }

    // ───────────────────────── requests ─────────────────────────

    [Doc("Changes to the event. Leave out anything you aren't changing.")]
    public class EventPatchRequest
    {
        [Doc("New name.")] public string Name { get; set; }
        [Doc("Laps per race (refused when rules are locked).")] public int? Laps { get; set; }
        [Doc("Consecutive laps used for personal bests (refused when rules are locked).")] public int? PbLaps { get; set; }
        [Doc("Race length in seconds (refused when rules are locked).")] public int? RaceLengthSeconds { get; set; }
        [Doc("Default race type for new rounds.", Example = "Race")] public string EventType { get; set; }
        [Doc("Laps shorter than this are ignored, in seconds.")] public double? MinLapTimeSeconds { get; set; }
        [Doc("Packs a pilot may fly before FPVTrackside warns (0 = no limit).")] public int? PackLimit { get; set; }
        [Doc("Event start date.")] public DateTime? Start { get; set; }
    }

    [Doc("A pilot to add to the event. An existing pilot of the same name (in the event or FPVTrackside's pilot list) is reused.")]
    public class PilotCreateRequest
    {
        [Doc("Name (callsign).", Required = true, Example = "Wheelzfpv")] public string Name { get; set; }
        [Doc("Default channel (label like \"R1\", a frequency, or a channel ID). Leave out to use the least-used channel.")] public string Channel { get; set; }
        [Doc("First name.")] public string FirstName { get; set; }
        [Doc("Last name.")] public string LastName { get; set; }
        [Doc("An external system's ID for the pilot. Must be unique in the event.")] public int? ExternalId { get; set; }
    }

    [Doc("Changes to a pilot. Leave out anything you aren't changing.")]
    public class PilotPatchRequest
    {
        [Doc("Name (callsign).")] public string Name { get; set; }
        [Doc("First name.")] public string FirstName { get; set; }
        [Doc("Last name.")] public string LastName { get; set; }
        [Doc("Discord user ID.")] public string DiscordId { get; set; }
        [Doc("Aircraft.")] public string Aircraft { get; set; }
        [Doc("Catch phrase.")] public string CatchPhrase { get; set; }
        [Doc("Practice pilot (not scored).")] public bool? PracticePilot { get; set; }
        [Doc("Velocidrone user ID.")] public string VelocidroneUid { get; set; }
        [Doc("Default channel for the event (label, frequency or ID).")] public string Channel { get; set; }
        [Doc("An external system's ID (0 clears it).")] public int? ExternalId { get; set; }
    }

    [Doc("A new, empty round.")]
    public class RoundCreateRequest
    {
        [Doc("Race type. Defaults to the event's type.", Example = "Race")] public string EventType { get; set; }
        [Doc("Custom name.")] public string Name { get; set; }
        [Doc("An external system's ID for the round.")] public int? ExternalId { get; set; }
    }

    [Doc("Changes to a round.")]
    public class RoundPatchRequest
    {
        [Doc("Custom name (empty string clears it).")] public string Name { get; set; }
        [Doc("An external system's ID (0 clears it).")] public int? ExternalId { get; set; }
    }

    [Doc("A pilot to seat in a race.")]
    public class RacePilotRequest
    {
        [Doc("Pilot ID or name. A name not in the event adds the pilot to the event.", Required = true, Example = "Wheelzfpv")] public string Pilot { get; set; }
        [Doc("Channel (label like \"R1\", a frequency, or an ID). Leave out to use a free one.")] public string Channel { get; set; }
    }

    [Doc("A new race.")]
    public class RaceCreateRequest
    {
        [Doc("Round to add it to. Leave out to start a new round.")] public Guid? RoundId { get; set; }
        [Doc("Race type of the new round, when roundId is left out. Defaults to the event's type.")] public string EventType { get; set; }
        [Doc("Pilots to seat.")] public List<RacePilotRequest> Pilots { get; set; }
        [Doc("Select the race in FPVTrackside once it's created (refused while another race is running).")] public bool MakeCurrent { get; set; }
        [Doc("An external system's ID for the race, e.g. the AUFPV platform's bracket race (finished results are linked back by it).")] public int? ExternalId { get; set; }
        [Doc("Laps to finish, if not the event's.")] public int? TargetLaps { get; set; }
    }

    [Doc("A lap-line crossing to add. Give exactly one of: time; raceTimeMs; or anchorDetectionId with offsetMs.")]
    public class LapCreateRequest
    {
        [Doc("Pilot ID or name.", Required = true)] public string Pilot { get; set; }
        [Doc("When the pilot crossed the lap line (ISO 8601).")] public DateTime? Time { get; set; }
        [Doc("Milliseconds after the race start.")] public long? RaceTimeMs { get; set; }
        [Doc("A crossing of the same pilot to measure from (usually a split gate). Immune to clock differences between machines.")] public Guid? AnchorDetectionId { get; set; }
        [Doc("Milliseconds after the anchor (1 to 600000).")] public long? OffsetMs { get; set; }
    }

    [Doc("Whether a crossing counts.")]
    public class DetectionPatchRequest
    {
        [Doc("Count it (true) or not (false).", Required = true)] public bool? Valid { get; set; }
        [Doc("Recorded as who changed it. Defaults to ManualOverride.", Values = new[] { "ManualOverride", "Marshall" })] public string ValidityType { get; set; }
    }

    [Doc("A missed lap line to add, measured from another crossing of the same pilot.")]
    public class LapCorrectionAdd
    {
        [Doc("Detection to measure from (usually a split gate).", Required = true)] public Guid AnchorDetectionId { get; set; }
        [Doc("Milliseconds after the anchor (1 to 600000).", Required = true)] public long OffsetMs { get; set; }
    }

    [Doc("A lap-timing fix for one pilot in one race: false lap lines to invalidate and missed ones to add. Validated in full before anything changes; repeating it is harmless.")]
    public class LapCorrectionRequest
    {
        [Doc("Pilot ID.", Required = true)] public Guid PilotId { get; set; }
        [Doc("Lap-line detection IDs to mark invalid (recorded as Marshall).")] public List<Guid> Invalidate { get; set; }
        [Doc("Missed lap lines to add.")] public List<LapCorrectionAdd> Add { get; set; }
    }

    public class StartRaceRequest
    {
        [Doc("Show the video-check scene first, as the Start button does when \"Auto race start video check\" is on.")] public bool VideoCheck { get; set; }
    }

    [Doc("The race to select in FPVTrackside.")]
    public class CurrentRaceRequest
    {
        [Doc("Race ID, or null to clear the selection.")] public Guid? RaceId { get; set; }
    }
}
