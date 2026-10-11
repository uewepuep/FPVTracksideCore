using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Webb.Api
{
    // For an external system that owns the meeting (the AUFPV platform, through
    // its dd-pits venue agent): link FPVTrackside's pilots, rounds and races to
    // its own IDs, push a whole meeting plan in one idempotent call, and pull
    // the whole meeting back in one read.
    public partial class RestApi
    {
        private void RegisterSync()
        {
            const string tag = "Meeting sync";

            Get("meeting", tag, "The whole meeting in one read", typeof(MeetingDto), Meeting,
                "Event, channels, pilots, rounds and every race with its line-up, laps and results, plus the selected race. `?laps=false` leaves laps and results out. Everything carries its externalId, so a caller can match it to its own records.",
                query: new[] { "laps" });
            Write("POST", "meeting/sync", tag, "Create or update the meeting from a plan", typeof(MeetingPlanRequest), typeof(MeetingSyncResultDto), Sync,
                "Idempotent: send the same plan again and nothing changes. Pilots, rounds and races are matched by `externalId` (a pilot also by name, which then takes the externalId). Missing ones are created, changed ones updated. A race that has started or finished is never touched — it is reported as `locked`. With `removeMissing: true`, races that have an externalId, haven't been run and aren't in the plan are deleted. With `dryRun: true` nothing changes and the result says what would.");
            Write("PATCH", "races/{raceId}", "Races", "Change a race's external ID or target laps", typeof(RacePatchRequest), typeof(RaceDto), PatchRace);
            Write("PUT", "event/channels", "Event", "Set the event's channels", typeof(ChannelsRequest), typeof(EventDto), SetChannels,
                "Labels (`R1`), frequencies (`5658`) or IDs, in the order to use. FPVTrackside reloads the event to apply new channels, so the API is unavailable for a few seconds afterwards. Refused (409) while a race is running.");

            Examples(new Dictionary<string, string>
            {
                ["POST meeting/sync"] = "{\n  \"pilots\": [\n    { \"externalId\": 101, \"name\": \"Wheelzfpv\", \"channel\": \"R1\" },\n    { \"externalId\": 102, \"name\": \"heepsy\", \"channel\": \"R3\" }\n  ],\n  \"rounds\": [\n    {\n      \"externalId\": 9001,\n      \"eventType\": \"Race\",\n      \"name\": \"Winners Round 1\",\n      \"races\": [\n        { \"externalId\": 5001, \"pilots\": [ { \"pilot\": \"ext:101\", \"channel\": \"R1\" }, { \"pilot\": \"ext:102\", \"channel\": \"R3\" } ] }\n      ]\n    }\n  ],\n  \"removeMissing\": false,\n  \"dryRun\": true\n}",
                ["PATCH races/{raceId}"] = "{ \"externalId\": 5001 }",
                ["PUT event/channels"] = "{ \"channels\": [\"R1\", \"R3\", \"R6\", \"R8\"] }",
            });
        }

        private static int? ExternalIdFilter(ApiRequest r)
        {
            string s = r.Query["externalId"];
            if (string.IsNullOrEmpty(s))
                return null;
            int v;
            if (!int.TryParse(s, out v))
                throw ApiException.BadRequest("externalId must be a number");
            return v;
        }

        private void ApplyRound(Round round, string name, int? externalId)
        {
            if (name != null && name.Trim() != (round.Name ?? ""))
                eventManager.RoundManager.RenameRound(round, name.Trim());
            if (externalId.HasValue && round.ExternalID != externalId.Value)
            {
                round.ExternalID = externalId.Value;
                using (RaceLib.IDatabase db = RaceLib.DatabaseFactory.Open(eventManager.EventId))
                    db.Update(round);
            }
        }

        private void ApplyRace(Race race, int? externalId, int? targetLaps)
        {
            if (targetLaps.HasValue && (targetLaps < 0 || targetLaps > 1000))
                throw ApiException.BadRequest("targetLaps must be 0-1000");
            bool changed = false;
            if (externalId.HasValue && race.ExternalID != externalId.Value)
            {
                race.ExternalID = externalId.Value;
                changed = true;
            }
            if (targetLaps.HasValue && race.TargetLaps != targetLaps.Value)
            {
                race.TargetLaps = targetLaps.Value;
                changed = true;
            }
            if (changed)
            {
                using (RaceLib.IDatabase db = RaceLib.DatabaseFactory.Open(eventManager.EventId))
                    db.Update(race);
            }
        }

        private object PatchRace(ApiRequest r)
        {
            Race race = RaceRef(r);
            RacePatchRequest body = r.BodyAs<RacePatchRequest>();
            if (body.ExternalId.HasValue && body.ExternalId != 0 && RaceManager.Races.Any(x => x != race && x.Valid && x.ExternalID == body.ExternalId))
                throw ApiException.Conflict("another race has externalId " + body.ExternalId);
            if (body.TargetLaps.HasValue)
                NotWhileRunning(race);
            ApplyRace(race, body.ExternalId, body.TargetLaps);
            Log("race changed " + race.RaceName);
            return ToDto(race);
        }

        private object SetChannels(ApiRequest r)
        {
            ChannelsRequest body = r.BodyAs<ChannelsRequest>();
            if (body.Channels == null || body.Channels.Count == 0)
                throw ApiException.BadRequest("'channels' needs at least one channel");
            if (RaceManager.RaceRunning)
                throw ApiException.Conflict("a race is running");

            List<Channel> channels = new List<Channel>();
            foreach (string c in body.Channels)
            {
                Channel ch = ResolveChannel(c, Channel.AllChannels);
                if (ch == null)
                    throw ApiException.BadRequest("unknown channel '" + c + "'");
                if (channels.Contains(ch))
                    throw ApiException.BadRequest(ch.GetBandChannelText() + " is listed twice");
                channels.Add(ch);
            }

            Event ev = eventManager.Event;
            bool same = ev.Channels != null && ev.Channels.SequenceEqual(channels);
            if (!same)
            {
                using (RaceLib.IDatabase db = RaceLib.DatabaseFactory.Open(eventManager.EventId))
                {
                    ev.Channels = channels.ToArray();
                    db.Update(ev);
                }
                Log("channels set " + string.Join(",", channels.Select(c => c.GetBandChannelText())));

                // The window builds its channel tiles at load, as after the
                // Channel Settings dialog.
                app?.Restart();
            }
            return ToDto(ev);
        }

        private object Meeting(ApiRequest r)
        {
            bool laps = !string.Equals(r.Query["laps"], "false", StringComparison.OrdinalIgnoreCase);
            List<Race> races = RaceManager.Races.Where(x => x.Valid).OrderBy(x => x.Round?.Order ?? 0).ThenBy(x => x.RaceOrder).ToList();
            return new MeetingDto
            {
                Event = ToDto(eventManager.Event),
                Pilots = eventManager.Event.Pilots.OrderBy(p => p.Name).Select(ToDto).ToList(),
                Rounds = Rounds().Select(ToDto).ToList(),
                Races = races.Select(x => laps ? ToDto(x) : Fill(new RaceDto(), x)).ToList(),
                CurrentRaceId = RaceManager.CurrentRace?.ID,
                RaceControl = RaceControlState(),
            };
        }

        // ───────────────────────── plan sync ─────────────────────────

        private object Sync(ApiRequest r)
        {
            MeetingPlanRequest plan = r.BodyAs<MeetingPlanRequest>();
            bool dry = plan.DryRun;
            MeetingSyncResultDto result = new MeetingSyncResultDto { DryRun = dry, Pilots = new List<SyncItemDto>(), Rounds = new List<SyncItemDto>(), Races = new List<SyncItemDto>() };

            ValidatePlan(plan);

            // Pilots first, so race line-ups can name them as ext:<id>.
            foreach (PlanPilot pp in plan.Pilots ?? new List<PlanPilot>())
                result.Pilots.Add(SyncPilot(pp, dry));

            HashSet<int> planned = new HashSet<int>();
            foreach (PlanRound pr in plan.Rounds ?? new List<PlanRound>())
            {
                Round round = Rounds().FirstOrDefault(x => x.ExternalID == pr.ExternalId);
                string action = "unchanged";
                if (round == null)
                {
                    action = "created";
                    if (!dry)
                    {
                        round = eventManager.RoundManager.CreateEmptyRound(ParseEventType(pr.EventType, eventManager.Event.EventType));
                        ApplyRound(round, pr.Name, pr.ExternalId);
                    }
                }
                else
                {
                    if (pr.Name != null && pr.Name.Trim() != (round.Name ?? ""))
                    {
                        action = "updated";
                        if (!dry)
                            ApplyRound(round, pr.Name, null);
                    }
                    if (pr.EventType != null && ParseEventType(pr.EventType, round.EventType) != round.EventType)
                        result.Warnings.Add("round " + pr.ExternalId + ": its race type can't be changed through sync (it is " + round.EventType + ")");
                }
                result.Rounds.Add(new SyncItemDto { ExternalId = pr.ExternalId, Id = round?.ID, Action = action });

                foreach (PlanRace race in pr.Races ?? new List<PlanRace>())
                {
                    planned.Add(race.ExternalId);
                    result.Races.Add(SyncRace(race, round, pr.ExternalId, dry));
                }
            }

            if (plan.RemoveMissing)
            {
                foreach (Race race in RaceManager.Races.Where(x => x.Valid && x.ExternalID != 0 && !planned.Contains(x.ExternalID)).ToArray())
                {
                    if (race.Started || race.Ended || race.Running)
                    {
                        result.Races.Add(new SyncItemDto { ExternalId = race.ExternalID, Id = race.ID, Action = "locked", Message = "not in the plan but already run, so kept" });
                        continue;
                    }
                    if (!dry)
                        RaceManager.RemoveRace(race, false);
                    result.Races.Add(new SyncItemDto { ExternalId = race.ExternalID, Id = race.ID, Action = "removed" });
                }
            }

            result.Changed = result.Pilots.Concat(result.Rounds).Concat(result.Races).Count(x => x.Action != "unchanged" && x.Action != "locked");
            Log("meeting sync" + (dry ? " (dry run)" : "") + ": " + result.Changed + " changes");
            return result;
        }

        // Everything checked before anything changes, so a bad plan changes nothing.
        private void ValidatePlan(MeetingPlanRequest plan)
        {
            HashSet<int> pilotIds = new HashSet<int>();
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlanPilot p in plan.Pilots ?? new List<PlanPilot>())
            {
                if (p == null || p.ExternalId == 0)
                    throw ApiException.BadRequest("every pilot needs a non-zero externalId");
                if (string.IsNullOrWhiteSpace(p.Name))
                    throw ApiException.BadRequest("pilot " + p.ExternalId + " needs a name");
                if (!pilotIds.Add(p.ExternalId))
                    throw ApiException.BadRequest("pilot externalId " + p.ExternalId + " is listed twice");
                if (!names.Add(p.Name.Trim()))
                    throw ApiException.BadRequest("two pilots are named '" + p.Name.Trim() + "'");
                if (p.Channel != null && ChannelRef(p.Channel) == null)
                    throw ApiException.BadRequest("pilot " + p.ExternalId + ": unknown channel '" + p.Channel + "'");
            }

            HashSet<int> roundIds = new HashSet<int>();
            HashSet<int> raceIds = new HashSet<int>();
            foreach (PlanRound round in plan.Rounds ?? new List<PlanRound>())
            {
                if (round == null || round.ExternalId == 0)
                    throw ApiException.BadRequest("every round needs a non-zero externalId");
                if (!roundIds.Add(round.ExternalId))
                    throw ApiException.BadRequest("round externalId " + round.ExternalId + " is listed twice");
                ParseEventType(round.EventType, eventManager.Event.EventType);

                foreach (PlanRace race in round.Races ?? new List<PlanRace>())
                {
                    if (race == null || race.ExternalId == 0)
                        throw ApiException.BadRequest("every race needs a non-zero externalId");
                    if (!raceIds.Add(race.ExternalId))
                        throw ApiException.BadRequest("race externalId " + race.ExternalId + " is listed twice");
                    if (race.TargetLaps.HasValue && (race.TargetLaps < 0 || race.TargetLaps > 1000))
                        throw ApiException.BadRequest("race " + race.ExternalId + ": targetLaps must be 0-1000");

                    HashSet<string> seated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    HashSet<Channel> channels = new HashSet<Channel>();
                    foreach (RacePilotRequest seat in race.Pilots ?? new List<RacePilotRequest>())
                    {
                        if (seat == null || string.IsNullOrWhiteSpace(seat.Pilot))
                            throw ApiException.BadRequest("race " + race.ExternalId + ": every seat needs a pilot");
                        int ext;
                        if (TryExternalRef(seat.Pilot, out ext) && !pilotIds.Contains(ext) && !eventManager.Event.Pilots.Any(p => p.ExternalID == ext))
                            throw ApiException.BadRequest("race " + race.ExternalId + ": pilot ext:" + ext + " isn't in the plan or the event");
                        if (!seated.Add(seat.Pilot.Trim()))
                            throw ApiException.BadRequest("race " + race.ExternalId + ": " + seat.Pilot + " is seated twice");
                        if (seat.Channel != null)
                        {
                            Channel c = ChannelRef(seat.Channel);
                            if (c == null)
                                throw ApiException.BadRequest("race " + race.ExternalId + ": unknown channel '" + seat.Channel + "'");
                            if (!channels.Add(c))
                                throw ApiException.BadRequest("race " + race.ExternalId + ": two pilots are on " + c.GetBandChannelText());
                        }
                    }
                }
            }
        }

        private SyncItemDto SyncPilot(PlanPilot pp, bool dry)
        {
            string name = pp.Name.Trim();
            Pilot pilot = eventManager.Event.Pilots.FirstOrDefault(p => p.ExternalID == pp.ExternalId)
                       ?? eventManager.Event.Pilots.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (pilot == null)
            {
                if (dry)
                    return new SyncItemDto { ExternalId = pp.ExternalId, Action = "created", Message = name };
                pilot = eventManager.GetCreatePilot(name);
                ApplyPilot(pilot, pp);
                Channel channel = ChannelRef(pp.Channel);
                if (channel != null) eventManager.AddPilot(pilot, channel); else eventManager.AddPilot(pilot);
                using (RaceLib.IDatabase db = RaceLib.DatabaseFactory.Open(eventManager.EventId))
                    db.Update(pilot);
                return new SyncItemDto { ExternalId = pp.ExternalId, Id = pilot.ID, Action = "created", Message = name };
            }

            if (!string.Equals(pilot.Name, name, StringComparison.Ordinal)
                && eventManager.Event.Pilots.Any(p => p != pilot && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                return new SyncItemDto { ExternalId = pp.ExternalId, Id = pilot.ID, Action = "conflict", Message = "another pilot is already named '" + name + "'" };

            Channel want = ChannelRef(pp.Channel);
            bool differs = pilot.ExternalID != pp.ExternalId
                || !string.Equals(pilot.Name, name, StringComparison.Ordinal)
                || (pp.FirstName != null && pp.FirstName != pilot.FirstName)
                || (pp.LastName != null && pp.LastName != pilot.LastName)
                || (pp.DiscordId != null && pp.DiscordId != pilot.DiscordID)
                || (pp.PracticePilot.HasValue && pp.PracticePilot != pilot.PracticePilot)
                || (want != null && want != eventManager.GetChannel(pilot));
            if (!differs)
                return new SyncItemDto { ExternalId = pp.ExternalId, Id = pilot.ID, Action = "unchanged", Message = pilot.Name };
            if (dry)
                return new SyncItemDto { ExternalId = pp.ExternalId, Id = pilot.ID, Action = "updated", Message = name };

            pilot.Name = name;
            ApplyPilot(pilot, pp);
            using (RaceLib.IDatabase db = RaceLib.DatabaseFactory.Open(eventManager.EventId))
                db.Update(pilot);
            eventManager.RefreshPilots(new[] { pilot });
            if (want != null && want != eventManager.GetChannel(pilot))
                eventManager.SetPilotChannel(pilot, want);
            return new SyncItemDto { ExternalId = pp.ExternalId, Id = pilot.ID, Action = "updated", Message = name };
        }

        private static void ApplyPilot(Pilot pilot, PlanPilot pp)
        {
            pilot.ExternalID = pp.ExternalId;
            if (pp.FirstName != null) pilot.FirstName = pp.FirstName;
            if (pp.LastName != null) pilot.LastName = pp.LastName;
            if (pp.DiscordId != null) pilot.DiscordID = pp.DiscordId;
            if (pp.PracticePilot.HasValue) pilot.PracticePilot = pp.PracticePilot.Value;
        }

        private SyncItemDto SyncRace(PlanRace pr, Round round, int roundExternalId, bool dry)
        {
            Race race = RaceManager.Races.FirstOrDefault(x => x.Valid && x.ExternalID == pr.ExternalId);

            if (race != null && (race.Started || race.Ended || race.Running || (race == RaceManager.CurrentRace && RaceManager.PreRaceStartDelay)))
                return new SyncItemDto { ExternalId = pr.ExternalId, Id = race.ID, Action = "locked", Message = "already run (" + RaceState(race) + "); left as it is" };

            if (race != null && round != null && race.Round != round)
                return new SyncItemDto { ExternalId = pr.ExternalId, Id = race.ID, Action = "conflict", Message = "it is in another round; delete it or move it in FPVTrackside first" };

            if (dry)
            {
                if (race == null)
                    return new SyncItemDto { ExternalId = pr.ExternalId, Action = "created" };
                return new SyncItemDto { ExternalId = pr.ExternalId, Id = race.ID, Action = LineupDiffers(race, pr) || (pr.TargetLaps.HasValue && pr.TargetLaps != race.TargetLaps) ? "updated" : "unchanged" };
            }

            List<Tuple<Pilot, Channel>> lineup = Lineup(pr.Pilots);
            if (race == null)
            {
                race = RaceManager.AddRaceToRound(round);
                if (lineup.Any())
                    RaceManager.SetRacePilots(race, lineup, false);
                ApplyRace(race, pr.ExternalId, pr.TargetLaps);
                return new SyncItemDto { ExternalId = pr.ExternalId, Id = race.ID, Action = "created", Message = race.RaceName };
            }

            bool changed = false;
            if (LineupDiffers(race, lineup))
            {
                if (IsSelectedAndUnrun(race))
                {
                    foreach (Pilot p in race.Pilots.Where(p => p != null).ToArray())
                        RaceManager.RemovePilotFromCurrentRace(p, false);
                    foreach (Tuple<Pilot, Channel> seat in lineup)
                        Seat(race, seat.Item1, seat.Item2);
                }
                else
                {
                    RaceManager.SetRacePilots(race, lineup, false);
                }
                changed = true;
            }
            if (pr.TargetLaps.HasValue && pr.TargetLaps != race.TargetLaps)
            {
                ApplyRace(race, null, pr.TargetLaps);
                changed = true;
            }
            return new SyncItemDto { ExternalId = pr.ExternalId, Id = race.ID, Action = changed ? "updated" : "unchanged", Message = race.RaceName };
        }

        private bool LineupDiffers(Race race, List<Tuple<Pilot, Channel>> lineup)
        {
            var have = race.PilotChannelsSafe.Where(pc => pc.Pilot != null).Select(pc => Tuple.Create(pc.Pilot.ID, pc.Channel?.ID ?? Guid.Empty)).OrderBy(t => t.Item1).ToList();
            var want = lineup.Select(s => Tuple.Create(s.Item1.ID, s.Item2?.ID ?? Guid.Empty)).OrderBy(t => t.Item1).ToList();
            return !have.SequenceEqual(want);
        }

        // Dry-run comparison without creating pilots: a seat whose pilot or
        // channel can't be resolved yet counts as a change.
        private bool LineupDiffers(Race race, PlanRace pr)
        {
            List<RacePilotRequest> seats = pr.Pilots ?? new List<RacePilotRequest>();
            PilotChannel[] have = race.PilotChannelsSafe.Where(pc => pc.Pilot != null).ToArray();
            if (have.Length != seats.Count)
                return true;
            foreach (RacePilotRequest seat in seats)
            {
                Pilot p;
                try { p = PilotRef(seat.Pilot); }
                catch (ApiException) { return true; }
                PilotChannel pc = have.FirstOrDefault(x => x.Pilot == p);
                if (pc == null)
                    return true;
                Channel c = ChannelRef(seat.Channel);
                if (c != null && c != pc.Channel)
                    return true;
            }
            return false;
        }
    }

    [Doc("Changes to a race.")]
    public class RacePatchRequest
    {
        [Doc("An external system's ID (0 clears it).")] public int? ExternalId { get; set; }
        [Doc("Laps to finish (refused while the race is running).")] public int? TargetLaps { get; set; }
    }

    [Doc("The event's channels, in order.")]
    public class ChannelsRequest
    {
        [Doc("Labels, frequencies or IDs.", Required = true)] public List<string> Channels { get; set; }
    }

    [Doc("The whole meeting.")]
    public class MeetingDto
    {
        public EventDto Event { get; set; }
        public List<PilotDto> Pilots { get; set; }
        public List<RoundDto> Rounds { get; set; }
        [Doc("Every race in running order; laps and results are left out with ?laps=false.")] public List<RaceDto> Races { get; set; }
        [Doc("The race selected in FPVTrackside, or null.")] public Guid? CurrentRaceId { get; set; }
        public RaceControlDto RaceControl { get; set; }
    }

    [Doc("A meeting plan from an external system. Every item is keyed by that system's own ID.")]
    public class MeetingPlanRequest
    {
        [Doc("Pilots to have in the event.")] public List<PlanPilot> Pilots { get; set; }
        [Doc("Rounds, each with its races in running order.")] public List<PlanRound> Rounds { get; set; }
        [Doc("Delete races that have an externalId, haven't been run and aren't in this plan.")] public bool RemoveMissing { get; set; }
        [Doc("Change nothing; report what would change.")] public bool DryRun { get; set; }
    }

    public class PlanPilot
    {
        [Doc("The external system's pilot ID.", Required = true)] public int ExternalId { get; set; }
        [Doc("Name (callsign).", Required = true)] public string Name { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string DiscordId { get; set; }
        public bool? PracticePilot { get; set; }
        [Doc("Default channel for the event.")] public string Channel { get; set; }
    }

    public class PlanRound
    {
        [Doc("The external system's round ID.", Required = true)] public int ExternalId { get; set; }
        [Doc("Race type, for a new round. Defaults to the event's.")] public string EventType { get; set; }
        [Doc("Custom name.")] public string Name { get; set; }
        public List<PlanRace> Races { get; set; }
    }

    public class PlanRace
    {
        [Doc("The external system's race ID.", Required = true)] public int ExternalId { get; set; }
        [Doc("Seats. A pilot is an ID, a name or ext:<externalId>.")] public List<RacePilotRequest> Pilots { get; set; }
        [Doc("Laps to finish, if not the event's.")] public int? TargetLaps { get; set; }
    }

    [Doc("What a meeting sync did (or, on a dry run, would do).")]
    public class MeetingSyncResultDto
    {
        public bool DryRun { get; set; }
        [Doc("How many items were created, updated or removed.")] public int Changed { get; set; }
        public List<SyncItemDto> Pilots { get; set; }
        public List<SyncItemDto> Rounds { get; set; }
        public List<SyncItemDto> Races { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class SyncItemDto
    {
        public int ExternalId { get; set; }
        [Doc("FPVTrackside's ID (null for something a dry run would create).")] public Guid? Id { get; set; }
        [Doc("What happened.", Values = new[] { "created", "updated", "unchanged", "removed", "locked", "conflict" })] public string Action { get; set; }
        public string Message { get; set; }
    }
}
