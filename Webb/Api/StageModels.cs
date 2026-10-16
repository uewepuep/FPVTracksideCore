using System;
using System.Collections.Generic;
using Webb.Seeder;

namespace Webb.Api
{
    [Doc("A stage format FPVTrackside can run.")]
    public class StageFormatDto
    {
        [Doc("Value for `format` when creating a stage.", Example = "elimination")] public string Kind { get; set; }
        [Doc("Name for people.")] public string Name { get; set; }
        [Doc("What it is and when to use it.")] public string Description { get; set; }
        [Doc("Which `setup` fields it reads.")] public List<string> Uses { get; set; }
    }

    [Doc("A pilot's place in the qualifying order.")]
    public class SeedDto
    {
        [Doc("Seed (1 = fastest).")] public int Seed { get; set; }
        [Doc("Pilot ID.")] public Guid PilotId { get; set; }
        [Doc("Pilot name.")] public string Name { get; set; }
        [Doc("Best consecutive-laps time in seconds, or null with no time.")] public double? BestSeconds { get; set; }
        [Doc("Qualifying races the pilot flew.")] public int Races { get; set; }
    }

    [Doc("A stage in the list.")]
    public class StageSummaryDto
    {
        [Doc("Stage ID.")] public int Id { get; set; }
        [Doc("Name.")] public string Name { get; set; }
        [Doc("Format kind.", Values = new[] { "elimination", "qualifier", "mainsprogression", "ladder", "mains" })] public string Format { get; set; }
        [Doc("Format name for people.")] public string FormatName { get; set; }
        [Doc("Number of pilots.")] public int PilotCount { get; set; }
        [Doc("Races in the stage (void ones excluded).")] public int RaceCount { get; set; }
        [Doc("Races with a result.")] public int RacesDone { get; set; }
        [Doc("Winner, once decided (null before).")] public string Champion { get; set; }
        [Doc("True when the stage has finished.")] public bool IsComplete { get; set; }
        [Doc("Last change.")] public DateTime Updated { get; set; }
    }

    [Doc("A pilot in a stage race.")]
    public class StagePilotDto
    {
        [Doc("Pilot ID.")] public string PilotId { get; set; }
        [Doc("Pilot name.")] public string Name { get; set; }
        [Doc("Qualifying seed (1 = top).")] public int Seed { get; set; }
        [Doc("Channel in FPVTrackside, once the race has been sent.")] public string Channel { get; set; }
    }

    [Doc("One race of a stage.")]
    public class StageRaceDto
    {
        [Doc("Race ID within the stage (e.g. W1-2, L3-1, CTA-1, A:W2-1).")] public string Id { get; set; }
        [Doc("Round within the stage (column).")] public int Round { get; set; }
        [Doc("Lane / group, e.g. Winners, Losers, Chase the Ace, Bracket A.")] public string Group { get; set; }
        [Doc("FPVTrackside race bracket the race is created with.", Values = new[] { "None", "Winners", "Losers", "A", "B", "C" })] public string Bracket { get; set; }
        [Doc("Full heading, e.g. \"Winners Round 2 — Race 1\".")] public string Title { get; set; }
        [Doc("waiting = line-up not known yet or waiting on another race; ready = can be flown; sent = in FPVTrackside, not flown; running; flown = finished in FPVTrackside but the result isn't in the stage (see `note`); finished; void = not needed.",
            Values = new[] { "waiting", "ready", "sent", "running", "flown", "finished", "void" })]
        public string State { get; set; }
        [Doc("Why the race is waiting, or what stopped a result being applied.")] public string Note { get; set; }
        [Doc("Line-up, in seat order (in finishing order once finished, for formats that rank everyone).")] public List<StagePilotDto> Pilots { get; set; }
        [Doc("Pilots who advance.")] public List<string> Winners { get; set; }
        [Doc("Pilots who don't.")] public List<string> Losers { get; set; }
        [Doc("How many must be ranked for the result to count.")] public int ExpectedWinners { get; set; }
        [Doc("Whether a finished result may still be changed.")] public bool CanEdit { get; set; }
        [Doc("Result entered by hand: FPVTrackside's timing won't overwrite it.")] public bool ManualLock { get; set; }
        [Doc("The FPVTrackside race this race was sent to, or null.")] public Guid? FpvRaceId { get; set; }
        [Doc("Its name in FPVTrackside.")] public string FpvRaceName { get; set; }
    }

    [Doc("A row of the stage's running order.")]
    public class StageStandingDto
    {
        [Doc("Rank.")] public int Rank { get; set; }
        [Doc("Pilot ID.")] public string PilotId { get; set; }
        [Doc("Pilot name.")] public string Name { get; set; }
        [Doc("Format-specific detail, e.g. \"47 pts\".")] public string Detail { get; set; }
        [Doc("Rank not decided on the track (a tie, or inferred for knocked-out pilots).")] public bool Tied { get; set; }
    }

    [Doc("A race in the stage diagram.")]
    public class DiagramNodeDto
    {
        [Doc("Race ID.")] public string Id { get; set; }
        [Doc("Column.")] public int Column { get; set; }
        [Doc("Lane: winners, losers, grand-final, chase-the-ace.")] public string Lane { get; set; }
        [Doc("Short label, e.g. W2-1.")] public string Label { get; set; }
        [Doc("Seats: known pilots, or where a seat will come from.")] public List<DiagramSeatDto> Seats { get; set; }
    }

    [Doc("One seat in a diagram race.")]
    public class DiagramSeatDto
    {
        [Doc("Pilot ID when known.")] public string PilotId { get; set; }
        [Doc("Pilot name when known.")] public string Name { get; set; }
        [Doc("Finishing position in this race, once run.")] public int Position { get; set; }
        [Doc("Advanced from this race.")] public bool Advanced { get; set; }
        [Doc("For a seat not filled yet: the race it comes from.")] public string FromRace { get; set; }
        [Doc("…and the finishing place in that race (1 = winner).")] public int FromRank { get; set; }
    }

    [Doc("A line in the stage diagram: pilots flow from one race to another.")]
    public class DiagramEdgeDto
    {
        [Doc("Source race.")] public string From { get; set; }
        [Doc("Destination race.")] public string To { get; set; }
        [Doc("Finishing places in the source race that take this line, e.g. [1,2].")] public List<int> Ranks { get; set; }
    }

    [Doc("Stage diagram: races laid out by column and lane, and how pilots flow between them. Present for elimination-style formats (one per bracket for bracket progression).")]
    public class DiagramDto
    {
        [Doc("Diagram title (the bracket, for bracket progression).")] public string Title { get; set; }
        [Doc("Races.")] public List<DiagramNodeDto> Nodes { get; set; }
        [Doc("Flow between races.")] public List<DiagramEdgeDto> Edges { get; set; }
    }

    [Doc("A stage with its races, standings and diagram.")]
    public class StageDto : StageSummaryDto
    {
        [Doc("Setup the stage was created with.")] public StageSetup Setup { get; set; }
        [Doc("Pilots in seed order.")] public List<StagePilotDto> Pilots { get; set; }
        [Doc("Channel per seat (empty = FPVTrackside picks free channels).")] public List<string> SeatChannels { get; set; }
        [Doc("Apply results from FPVTrackside automatically when a sent race finishes.")] public bool AutoResults { get; set; }
        [Doc("Create each race in FPVTrackside automatically as soon as its line-up is known.")] public bool AutoSend { get; set; }
        [Doc("How the page lays it out.", Values = new[] { "bracket-grid", "ladder-grid", "mains-table", "mains-progression-tabs" })] public string Layout { get; set; }
        [Doc("Every race, in running order.")] public List<StageRaceDto> Races { get; set; }
        [Doc("Running order / final standings.")] public List<StageStandingDto> Standings { get; set; }
        [Doc("Stage diagrams (empty for ladder and points mains).")] public List<DiagramDto> Diagrams { get; set; }
    }

    [Doc("Outcome of pulling results from FPVTrackside.")]
    public class StageSyncDto
    {
        [Doc("Races whose result was recorded or updated.")] public List<string> Applied { get; set; }
        [Doc("Races whose FPVTrackside result differs but couldn't be applied, with why.")] public Dictionary<string, string> Problems { get; set; }
        [Doc("The stage after the sync.")] public StageDto Stage { get; set; }
    }

    [Doc("Races sent to FPVTrackside.")]
    public class StageSendDto
    {
        [Doc("Race IDs sent (or refreshed).")] public List<string> Sent { get; set; }
        [Doc("Races that couldn't be sent, with why.")] public Dictionary<string, string> Problems { get; set; }
        [Doc("The stage after sending.")] public StageDto Stage { get; set; }
    }

    // ───────────────────────── requests ─────────────────────────

    [Doc("A new stage.")]
    public class StageCreateRequest
    {
        [Doc("Name.", Required = true, Example = "Open Class Finals")] public string Name { get; set; }
        [Doc("Format kind (see GET /stages/formats).", Example = "elimination")] public string Format { get; set; }
        [Doc("Format settings. Leave out anything to use the default.")] public StageSetup Setup { get; set; }
        [Doc("Pilots in seed order, top seed first (IDs or names). Pilots from FPVTrackside's pilot database (GET /pilots/database) who aren't in the event are added to it; a new name creates a pilot. GET /stages/seeding gives the qualifying order.", Required = true)] public List<string> Pilots { get; set; }
        [Doc("Channel per seat, e.g. [\"R1\",\"R2\",\"R7\",\"R8\"]. Leave out to let FPVTrackside pick free channels.")] public List<string> SeatChannels { get; set; }
        [Doc("Create races in FPVTrackside automatically as their line-ups become known (round 1 straight away).")] public bool AutoSend { get; set; }
    }

    [Doc("Changes to a stage. Leave out anything you aren't changing.")]
    public class StagePatchRequest
    {
        [Doc("New name.")] public string Name { get; set; }
        [Doc("Channel per seat.")] public List<string> SeatChannels { get; set; }
        [Doc("Apply results from FPVTrackside automatically.")] public bool? AutoResults { get; set; }
        [Doc("Create races in FPVTrackside automatically as their line-ups become known. Switching it on sends the races that are ready now.")] public bool? AutoSend { get; set; }
    }

    [Doc("A race's result, entered by hand.")]
    public class StageRaceResultRequest
    {
        [Doc("Finishing order, winner first (pilot IDs or names). Give everyone, or at least the pilots who advance.", Required = true)] public List<string> Placement { get; set; }
        [Doc("Stop FPVTrackside's timing overwriting this result (default true).")] public bool? Lock { get; set; }
    }

    [Doc("Lock or unlock a race's result.")]
    public class StageRaceLockRequest
    {
        [Doc("True = timing won't overwrite the result; false = it may again.", Required = true)] public bool? Locked { get; set; }
    }

    [Doc("Options for sending races to FPVTrackside.")]
    public class StageRaceSendRequest
    {
        [Doc("Also select the (first) race on the race-director screen.")] public bool MakeCurrent { get; set; }
    }
}
