using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Webb.Seeder;

namespace Webb.Api
{
    // /api/v1/stages — race stages run on FPVTrackside. See
    // Webb/Stages/StageService.cs for how they map onto FPVTrackside races.
    public partial class RestApi
    {
        private StageService stages;

        public StageService Stages { get { return stages; } }

        public static readonly StageFormatDto[] StageFormats =
        {
            new StageFormatDto
            {
                Kind = global::SeederEngine.FormatKinds.Elimination, Name = "Double elimination",
                Description = "Winners and Losers brackets: a pilot is out after a second loss. Finals are a chase-the-ace series (first to N race wins) or a grand final. Single elimination is the same with no Losers bracket.",
                Uses = new List<string> { "eliminationType", "finalsFormat", "pilotsPerRace", "pilotsToAdvance", "aceWinsRequired" },
            },
            new StageFormatDto
            {
                Kind = global::SeederEngine.FormatKinds.Qualifier, Name = "Double elimination — top 4 advance",
                Description = "MultiGP-style double elimination that ends in a final ranking the top four (or top N with wider races) rather than one champion. 16 pilots at 4-up follows the MultiGP sheet exactly. Optional chase-the-ace final.",
                Uses = new List<string> { "pilotsPerRace", "pilotsToAdvance", "qualifierCta", "aceWinsRequired", "qualifierSeed" },
            },
            new StageFormatDto
            {
                Kind = global::SeederEngine.FormatKinds.MainsProgression, Name = "Bracket progression",
                Description = "Brackets A, B, C…, each a top-4 double elimination (A = championship). Racing starts in the lowest bracket; its top finishers move up into the next bracket, seeded by original qualifying position, until Bracket A decides the champion.",
                Uses = new List<string> { "pilotsPerMain", "bumpUp", "pilotsPerRace", "pilotsToAdvance", "qualifierCta", "ctaMains", "aceWinsRequired", "qualifierSeed" },
            },
            new StageFormatDto
            {
                Kind = global::SeederEngine.FormatKinds.Ladder, Name = "Seeded ladder",
                Description = "Lowest seeds race first; the top finishers carry up into the next bout against the next seeds, and the top seed enters last.",
                Uses = new List<string> { "pilotsPerRace", "pilotsToAdvance" },
            },
            new StageFormatDto
            {
                Kind = global::SeederEngine.FormatKinds.Mains, Name = "Points brackets",
                Description = "Pilots are split into brackets A, B, C… by seed (or by hand) and every bracket races several rounds for points; best total wins, optionally dropping each pilot's worst rounds.",
                Uses = new List<string> { "mains", "rounds", "direction", "pointsTable", "dropWorst", "divisions", "mainsRosters" },
            },
        };

        private void RegisterStages()
        {
            Get("stages/formats", "Stages", "List the stage formats", typeof(StageFormatDto), r => StageFormats.ToList(), array: true);
            Get("stages/seeding", "Stages", "Qualifying order for seeding a stage", typeof(SeedDto), Seeding,
                "Each pilot's best consecutive laps (`?laps=`, default the event's PB laps) across the time-trial rounds (or `?round=` repeated), fastest first. Pilots without a time follow. `?pilots=all` includes pilots who didn't qualify.",
                array: true, query: new[] { "laps", "round", "pilots" });
            Get("stages", "Stages", "List stages", typeof(StageSummaryDto), r => stages.Store.All().Select(b => (object)Summary(b, stages.Format(b))).ToList(), array: true);
            Write("POST", "stages", "Stages", "Create a stage", typeof(StageCreateRequest), typeof(StageDto), CreateStage,
                "Generates the whole structure straight away. Pilots not in the event are added to it.", status: 201);
            Get("stages/{stageId}", "Stages", "Get a stage with its races, standings and diagram", typeof(StageDto), r => Full(StageRef(r)));
            Write("PATCH", "stages/{stageId}", "Stages", "Rename a stage or change its channels", typeof(StagePatchRequest), typeof(StageDto), PatchStage);
            Write("DELETE", "stages/{stageId}", "Stages", "Delete a stage", null, typeof(OkDto), DeleteStage,
                "Races already sent to FPVTrackside stay there.");

            Write("POST", "stages/{stageId}/send", "Stages", "Send every ready race to FPVTrackside", typeof(StageRaceSendRequest), typeof(StageSendDto), SendReady,
                "Creates an FPVTrackside race (in a round named after the stage) for every race whose line-up is known and that hasn't been flown, and refreshes the line-up of any already sent but not started.");
            Write("POST", "stages/{stageId}/sync", "Stages", "Pull results from FPVTrackside", null, typeof(StageSyncDto), SyncStage,
                "Applies the result of every sent race that has finished in FPVTrackside. This happens automatically as each race ends (unless autoResults is off); use it after switching that back on, or to see why a result didn't apply.");

            Write("POST", "stages/{stageId}/races/{stageRaceId}/send", "Stages", "Send one race to FPVTrackside", typeof(StageRaceSendRequest), typeof(StageDto), SendStageRace,
                "Creates the FPVTrackside race (or refreshes its line-up if it hasn't started). Pilots keep the channel they flew in their previous race when it's free.");
            Write("POST", "stages/{stageId}/races/{stageRaceId}/result", "Stages", "Enter a race's result by hand", typeof(StageRaceResultRequest), typeof(StageDto), StageRaceResult,
                "Records (or corrects) the finishing order and advances pilots. Correcting is refused (409) once a later race that received these pilots has been run. Locks the result against timing unless lock is false.");
            Write("POST", "stages/{stageId}/races/{stageRaceId}/reset", "Stages", "Clear a race's result", null, typeof(StageDto), StageRaceReset,
                "Un-runs the race and takes its pilots back out of later races. Refused (409) once a later race that received them has been run.");
            Write("POST", "stages/{stageId}/races/{stageRaceId}/lock", "Stages", "Lock or unlock a race's result against timing", typeof(StageRaceLockRequest), typeof(StageDto), StageRaceLock);

            Examples(new Dictionary<string, string>
            {
                ["POST stages"] = "{\n  \"name\": \"Open Class Finals\",\n  \"format\": \"elimination\",\n  \"setup\": { \"eliminationType\": \"double\", \"finalsFormat\": \"chase-the-ace\", \"pilotsPerRace\": 4, \"pilotsToAdvance\": 2, \"aceWinsRequired\": 2 },\n  \"pilots\": [\"{{pilot.name}}\"],\n  \"seatChannels\": [\"R1\", \"R3\", \"R5\", \"R7\"]\n}",
                ["PATCH stages/{stageId}"] = "{ \"seatChannels\": [\"R1\", \"R3\", \"R5\", \"R7\"], \"autoResults\": true, \"autoSend\": true }",
                ["POST stages/{stageId}/races/{stageRaceId}/result"] = "{ \"placement\": [\"{{pilot.name}}\"], \"lock\": true }",
                ["POST stages/{stageId}/races/{stageRaceId}/lock"] = "{ \"locked\": false }",
                ["POST stages/{stageId}/send"] = "{ \"makeCurrent\": false }",
                ["POST stages/{stageId}/races/{stageRaceId}/send"] = "{ \"makeCurrent\": true }",
            });
        }

        // ───────────────────────── lookups ─────────────────────────

        private StageRecord StageRef(ApiRequest r)
        {
            int id;
            if (!int.TryParse(r.Param("stageId"), out id))
                throw ApiException.BadRequest("stageId must be a number");
            StageRecord rec = stages.Store.Get(id);
            if (rec == null)
                throw ApiException.NotFound("stage");
            return rec;
        }

        // Race IDs of bracket progression contain a colon ("A:W1-1"); accept it
        // encoded or not.
        private static string StageRaceParam(ApiRequest r)
        {
            return Uri.UnescapeDataString(r.Param("stageRaceId") ?? "");
        }

        // Runs an engine call, turning its refusals into 409s with its own words.
        private static T EngineCall<T>(Func<T> f)
        {
            try
            {
                return f();
            }
            catch (global::SeederEngine.BracketException e)
            {
                throw ApiException.Conflict(Terms.Trackside(e.Message));
            }
        }

        // ───────────────────────── handlers ─────────────────────────

        private object Seeding(ApiRequest r)
        {
            int laps;
            int.TryParse(r.Query["laps"], out laps);
            string[] roundIds = r.Query.GetValues("round") ?? new string[0];
            List<Round> rounds = new List<Round>();
            foreach (string id in roundIds.SelectMany(x => x.Split(',')).Where(x => x.Length > 0))
            {
                Guid g;
                Round round = Guid.TryParse(id, out g) ? Rounds().FirstOrDefault(x => x.ID == g) : null;
                if (round == null)
                    throw ApiException.NotFound("round " + id);
                rounds.Add(round);
            }

            List<StageService.SeedRow> rows = stages.Seeding(rounds, laps, eventManager.Event.Pilots.Where(p => p != null && !p.PracticePilot));
            if (r.Query["pilots"] != "all")
                rows = rows.Where(x => x.Races > 0).ToList();

            return rows.Select((x, i) => new SeedDto
            {
                Seed = i + 1,
                PilotId = x.Pilot.ID,
                Name = x.Pilot.Name,
                BestSeconds = x.Best.HasValue ? Math.Round(x.Best.Value.TotalSeconds, 3) : (double?)null,
                Races = x.Races,
            }).ToList();
        }

        private object CreateStage(ApiRequest r)
        {
            StageCreateRequest body = r.BodyAs<StageCreateRequest>();
            if (string.IsNullOrWhiteSpace(body.Name))
                throw ApiException.BadRequest("name is required");
            string format = string.IsNullOrWhiteSpace(body.Format) ? global::SeederEngine.FormatKinds.Elimination : body.Format.Trim().ToLowerInvariant();
            if (!StageFormats.Any(f => f.Kind == format))
                throw ApiException.BadRequest("unknown format '" + body.Format + "'; use one of " + string.Join(", ", StageFormats.Select(f => f.Kind)));
            if (body.Pilots == null || body.Pilots.Count < 2)
                throw ApiException.BadRequest("a stage needs at least 2 pilots");

            List<Pilot> pilots = new List<Pilot>();
            foreach (string reference in body.Pilots)
            {
                Pilot p = PilotRef(reference, true);
                if (pilots.Contains(p))
                    throw ApiException.BadRequest(p.Name + " is listed twice");
                pilots.Add(p);
            }
            foreach (string seat in body.SeatChannels ?? new List<string>())
                ChannelRef(seat);

            StageSetup setup = body.Setup ?? new StageSetup();
            if (setup.MainsRosters != null)
            {
                // Rosters may name pilots; the engine works on IDs.
                setup.MainsRosters = setup.MainsRosters.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Select(x => PilotRef(x).ID.ToString()).ToList());
            }

            StageRecord rec = EngineCall(() => stages.Create(body.Name.Trim(), format, setup, pilots, body.SeatChannels));
            if (body.AutoSend)
                stages.SetAutoSend(rec, true);
            Log("stage created " + rec.Name);
            return Full(rec);
        }

        private object PatchStage(ApiRequest r)
        {
            StageRecord rec = StageRef(r);
            StagePatchRequest body = r.BodyAs<StagePatchRequest>();
            if (body.Name != null)
            {
                if (body.Name.Trim().Length == 0)
                    throw ApiException.BadRequest("name can't be empty");
                stages.Rename(rec, body.Name.Trim());
            }
            if (body.SeatChannels != null)
            {
                foreach (string seat in body.SeatChannels)
                    ChannelRef(seat);
                stages.SetSeatChannels(rec, body.SeatChannels);
            }
            if (body.AutoSend.HasValue)
                stages.SetAutoSend(rec, body.AutoSend.Value);
            if (body.AutoResults.HasValue)
            {
                stages.SetAutoResults(rec, body.AutoResults.Value);
                if (body.AutoResults.Value)
                    stages.Sync(rec);
            }
            return Full(stages.Store.Get(rec.Id));
        }

        private object DeleteStage(ApiRequest r)
        {
            StageRecord rec = StageRef(r);
            stages.Delete(rec);
            Log("stage deleted " + rec.Name);
            return new OkDto { Message = rec.Name + " deleted" };
        }

        private object SendReady(ApiRequest r)
        {
            StageRecord rec = StageRef(r);
            StageRaceSendRequest body = r.BodyAs<StageRaceSendRequest>();

            Dictionary<string, string> problems;
            List<Race> races = stages.SendReady(rec, false, out problems);
            Race first = races.FirstOrDefault();
            List<string> sent = races.Select(x => rec.Links.First(l => l.Value == x.ID).Key).ToList();
            if (body.MakeCurrent && first != null)
                SelectRace(first);

            Log("stage " + rec.Name + " sent " + sent.Count + " races");
            return new StageSendDto { Sent = sent, Problems = problems, Stage = Full(stages.Store.Get(rec.Id)) };
        }

        private object SendStageRace(ApiRequest r)
        {
            StageRecord rec = StageRef(r);
            StageRaceSendRequest body = r.BodyAs<StageRaceSendRequest>();
            Race race = EngineCall(() => stages.Send(rec, StageRaceParam(r)));
            if (body.MakeCurrent)
                SelectRace(race);
            return Full(stages.Store.Get(rec.Id));
        }

        private void SelectRace(Race race)
        {
            Race current = RaceManager.CurrentRace;
            if (current != null && current != race && (current.Running || RaceManager.PreRaceStartDelay))
                throw ApiException.Conflict("the race was sent, but another race is running so it wasn't selected");
            RaceManager.SetRace(race);
        }

        private object SyncStage(ApiRequest r)
        {
            StageRecord rec = StageRef(r);
            Dictionary<string, StageService.TimingOutcome> outcome = stages.Sync(rec);
            rec = stages.Store.Get(rec.Id);

            Dictionary<string, string> problems = new Dictionary<string, string>();
            foreach (string stageRace in outcome.Where(o => o.Value == StageService.TimingOutcome.Blocked || o.Value == StageService.TimingOutcome.Failed).Select(o => o.Key))
            {
                string detail;
                stages.ApplyFromTiming(rec, stageRace, out detail);
                problems[stageRace] = detail;
            }
            return new StageSyncDto
            {
                Applied = outcome.Where(o => o.Value == StageService.TimingOutcome.Applied || o.Value == StageService.TimingOutcome.Updated).Select(o => o.Key).ToList(),
                Problems = problems,
                Stage = Full(rec),
            };
        }

        private object StageRaceResult(ApiRequest r)
        {
            StageRecord rec = StageRef(r);
            string stageRace = StageRaceParam(r);
            StageRaceResultRequest body = r.BodyAs<StageRaceResultRequest>();
            if (body.Placement == null || body.Placement.Count == 0)
                throw ApiException.BadRequest("placement is required");

            List<string> placement = body.Placement.Select(x => StagePilotId(rec, x)).ToList();
            EngineCall(() => { stages.RecordResult(rec, stageRace, placement, body.Lock ?? true); return true; });
            Log("stage " + rec.Name + " race " + stageRace + " result entered");
            return Full(stages.Store.Get(rec.Id));
        }

        private object StageRaceReset(ApiRequest r)
        {
            StageRecord rec = StageRef(r);
            string stageRace = StageRaceParam(r);
            EngineCall(() => { stages.ResetResult(rec, stageRace); return true; });
            Log("stage " + rec.Name + " race " + stageRace + " reset");
            return Full(stages.Store.Get(rec.Id));
        }

        private object StageRaceLock(ApiRequest r)
        {
            StageRecord rec = StageRef(r);
            StageRaceLockRequest body = r.BodyAs<StageRaceLockRequest>();
            if (!body.Locked.HasValue)
                throw ApiException.BadRequest("locked is required");
            EngineCall(() => { stages.Lock(rec, StageRaceParam(r), body.Locked.Value); return true; });
            rec = stages.Store.Get(rec.Id);
            if (!body.Locked.Value && rec.AutoResults)
            {
                string detail;
                stages.ApplyFromTiming(rec, StageRaceParam(r), out detail);
            }
            return Full(stages.Store.Get(rec.Id));
        }

        // A pilot reference (ID or name) resolved against the stage's own pilots.
        private string StagePilotId(StageRecord rec, string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                throw ApiException.BadRequest("empty pilot in placement");
            string r = reference.Trim();
            string byId = rec.Pilots.FirstOrDefault(p => string.Equals(p, r, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return byId;
            string byName = rec.Pilots.FirstOrDefault(p => string.Equals(stages.PilotName(rec, p), r, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
                return byName;
            throw ApiException.BadRequest("'" + reference + "' isn't in this stage");
        }

        // ───────────────────────── mapping ─────────────────────────

        private StageSummaryDto Summary(StageRecord rec, global::SeederEngine.IRaceFormat f)
        {
            return Fill(new StageSummaryDto(), rec, f);
        }

        private T Fill<T>(T dto, StageRecord rec, global::SeederEngine.IRaceFormat f) where T : StageSummaryDto
        {
            List<global::SeederEngine.RaceView> races = StageService.Views(f).Where(x => !x.IsVoid).ToList();
            string champion = f.Champion();
            dto.Id = rec.Id;
            dto.Name = rec.Name;
            dto.Format = rec.Format;
            dto.FormatName = StageFormats.FirstOrDefault(x => x.Kind == rec.Format)?.Name ?? rec.Format;
            dto.PilotCount = rec.Pilots.Count;
            dto.RaceCount = races.Count;
            dto.RacesDone = races.Count(x => x.IsComplete);
            dto.Champion = string.IsNullOrEmpty(champion) ? null : stages.PilotName(rec, champion);
            dto.IsComplete = f.IsComplete();
            dto.Updated = rec.Updated;
            return dto;
        }

        private StageDto Full(StageRecord rec)
        {
            global::SeederEngine.IRaceFormat f = stages.Format(rec);
            Dictionary<string, int> seed = rec.Pilots.Select((p, i) => new { p, i }).ToDictionary(x => x.p, x => x.i + 1);
            Func<string, string> name = id => stages.PilotName(rec, id);

            StageDto dto = Fill(new StageDto(), rec, f);
            dto.Setup = rec.Setup;
            dto.SeatChannels = rec.SeatChannels;
            dto.AutoResults = rec.AutoResults;
            dto.AutoSend = rec.AutoSend;
            dto.Layout = f.LayoutKind;
            dto.Pilots = rec.Pilots.Select(p => new StagePilotDto { PilotId = p, Name = name(p), Seed = seed[p] }).ToList();
            Dictionary<string, string> waiting = StageService.WaitingOn(f);
            dto.Races = StageService.Views(f).Select(rv => StageRace(rec, f, rv, seed, name, waiting)).ToList();
            dto.Standings = global::SeederEngine.RaceFormats.FinalOrdering(f, seed).Select(s => new StageStandingDto
            {
                Rank = s.Rank,
                PilotId = s.Pilot,
                Name = name(s.Pilot),
                Detail = Terms.Trackside(s.Detail),
                Tied = s.Tied,
            }).ToList();
            dto.Diagrams = StageDiagrams.Build(f, name);
            return dto;
        }

        private StageRaceDto StageRace(StageRecord rec, global::SeederEngine.IRaceFormat f, global::SeederEngine.RaceView rv, Dictionary<string, int> seed, Func<string, string> name, Dictionary<string, string> waiting)
        {
            Race race = stages.LinkedRace(rec, rv.Id);
            Dictionary<string, string> channels = new Dictionary<string, string>();
            if (race != null)
            {
                foreach (RacePilotChannel pc in race.PilotChannelsSafe)
                {
                    if (pc.Pilot != null && pc.Channel != null)
                        channels[pc.Pilot.ID.ToString()] = pc.Channel.GetBandChannelText();
                }
            }

            List<string> pilots = rv.Pilots, winners = rv.Winners, losers = rv.Losers;

            string state, note = null;
            if (rv.IsVoid) state = "void";
            else if (rv.IsComplete) state = "finished";
            else if (race != null && race.Running) state = "running";
            else if (race != null && race.Ended)
            {
                state = "flown";
                note = rec.Locked.Contains(rv.Id) ? "result locked — unlock it to take FPVTrackside's result" : "finished in FPVTrackside but the result wasn't applied — use Sync to see why";
            }
            else if (race != null) state = "sent";
            else if (rv.Locked) { state = "waiting"; note = Terms.Trackside(rv.LockedReason); }
            else if (pilots.Count == 0) { state = "waiting"; note = "line-up not known yet"; }
            else if (waiting.ContainsKey(rv.Id)) { state = "waiting"; note = "waiting on " + waiting[rv.Id] + " for the rest of the line-up"; }
            else state = "ready";

            string why;
            bool canEdit = f.CanEditRace(rv.Id, out why);

            return new StageRaceDto
            {
                Id = rv.Id,
                Round = rv.Round,
                Group = Terms.Trackside(rv.GroupLabel),
                Bracket = StageService.TracksideBracket(rv).ToString(),
                Title = Terms.Trackside(rv.Title),
                State = state,
                Note = note ?? (rv.IsComplete && !canEdit ? why : null),
                Pilots = pilots.Select(p => new StagePilotDto
                {
                    PilotId = p,
                    Name = name(p),
                    Seed = seed.TryGetValue(p, out int s) ? s : 0,
                    Channel = channels.TryGetValue(p, out string c) ? c : null,
                }).ToList(),
                Winners = winners.Select(name).ToList(),
                Losers = losers.Select(name).ToList(),
                ExpectedWinners = rv.ExpectedWinners,
                CanEdit = rv.IsComplete && canEdit,
                ManualLock = rec.Locked.Contains(rv.Id),
                FpvRaceId = race?.ID,
                FpvRaceName = race?.RaceName,
            };
        }
    }
}
