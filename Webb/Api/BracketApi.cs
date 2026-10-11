using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Webb.Bracketing;

namespace Webb.Api
{
    // /api/v1/brackets — race brackets run on FPVTrackside. See
    // Webb/Brackets/BracketService.cs for how they map onto FPVTrackside races.
    public partial class RestApi
    {
        private BracketService brackets;

        public BracketService Brackets { get { return brackets; } }

        public static readonly BracketFormatDto[] BracketFormats =
        {
            new BracketFormatDto
            {
                Kind = global::BracketEngine.FormatKinds.Elimination, Name = "Double elimination",
                Description = "Winners and redemption brackets: a pilot is out after a second loss. Finals are a chase-the-ace series (first to N heat wins) or a grand final. Single elimination is the same with no redemption side.",
                Uses = new List<string> { "eliminationType", "finalsFormat", "pilotsPerRace", "pilotsToAdvance", "aceWinsRequired" },
            },
            new BracketFormatDto
            {
                Kind = global::BracketEngine.FormatKinds.Qualifier, Name = "Double elimination — top 4 advance",
                Description = "MultiGP-style double elimination that ends in a final ranking the top four (or top N with wider heats) rather than one champion. 16 pilots at 4-up follows the MultiGP sheet exactly. Optional chase-the-ace final.",
                Uses = new List<string> { "pilotsPerRace", "pilotsToAdvance", "qualifierCta", "aceWinsRequired", "qualifierSeed" },
            },
            new BracketFormatDto
            {
                Kind = global::BracketEngine.FormatKinds.MainsProgression, Name = "Mains progression",
                Description = "A stack of top-4 qualifier brackets, one per main (A = championship). Racing starts in the lowest main; its top finishers bump up into the next main, seeded by original qualifying position, until the A Main crowns the champion.",
                Uses = new List<string> { "pilotsPerMain", "bumpUp", "pilotsPerRace", "pilotsToAdvance", "qualifierCta", "ctaMains", "aceWinsRequired", "qualifierSeed" },
            },
            new BracketFormatDto
            {
                Kind = global::BracketEngine.FormatKinds.Ladder, Name = "Seeded ladder",
                Description = "Lowest seeds race first; the top finishers carry up into the next bout against the next seeds, and the top seed enters last.",
                Uses = new List<string> { "pilotsPerRace", "pilotsToAdvance" },
            },
            new BracketFormatDto
            {
                Kind = global::BracketEngine.FormatKinds.Mains, Name = "Points mains",
                Description = "Pilots are split into A/B/C… mains by seed (or by hand) and every main races several rounds for points; best total wins, optionally dropping each pilot's worst rounds.",
                Uses = new List<string> { "mains", "rounds", "direction", "pointsTable", "dropWorst", "divisions", "mainsRosters" },
            },
        };

        private void RegisterBrackets()
        {
            Get("brackets/formats", "Brackets", "List the bracket formats", typeof(BracketFormatDto), r => BracketFormats.ToList(), array: true);
            Get("brackets/seeding", "Brackets", "Qualifying order for seeding a bracket", typeof(SeedDto), Seeding,
                "Each pilot's best consecutive laps (`?laps=`, default the event's PB laps) across the time-trial rounds (or `?round=` repeated), fastest first. Pilots without a time follow. `?pilots=all` includes pilots who didn't qualify.",
                array: true, query: new[] { "laps", "round", "pilots" });
            Get("brackets", "Brackets", "List brackets", typeof(BracketSummaryDto), r => brackets.Store.All().Select(b => (object)Summary(b, brackets.Format(b))).ToList(), array: true);
            Write("POST", "brackets", "Brackets", "Create a bracket", typeof(BracketCreateRequest), typeof(BracketDto), CreateBracket,
                "Generates the whole structure straight away. Pilots not in the event are added to it.", status: 201);
            Get("brackets/{bracketId}", "Brackets", "Get a bracket with its heats, standings and diagram", typeof(BracketDto), r => Full(BracketRef(r)));
            Write("PATCH", "brackets/{bracketId}", "Brackets", "Rename a bracket or change its channels", typeof(BracketPatchRequest), typeof(BracketDto), PatchBracket);
            Write("DELETE", "brackets/{bracketId}", "Brackets", "Delete a bracket", null, typeof(OkDto), DeleteBracket,
                "Races already sent to FPVTrackside stay there.");

            Write("POST", "brackets/{bracketId}/send", "Brackets", "Send every ready heat to FPVTrackside", typeof(HeatSendRequest), typeof(BracketSendDto), SendReady,
                "Creates an FPVTrackside race (in a round named after the bracket) for every heat whose line-up is known and that hasn't been flown, and refreshes the line-up of any already sent but not started.");
            Write("POST", "brackets/{bracketId}/sync", "Brackets", "Pull results from FPVTrackside", null, typeof(BracketSyncDto), SyncBracket,
                "Applies the result of every sent heat that has finished in FPVTrackside. This happens automatically as each race ends (unless autoResults is off); use it after switching that back on, or to see why a result didn't apply.");

            Write("POST", "brackets/{bracketId}/heats/{heatId}/send", "Brackets", "Send one heat to FPVTrackside", typeof(HeatSendRequest), typeof(BracketDto), SendHeat,
                "Creates the FPVTrackside race (or refreshes its line-up if it hasn't started). Pilots keep the channel they flew in their previous heat when it's free.");
            Write("POST", "brackets/{bracketId}/heats/{heatId}/result", "Brackets", "Enter a heat's result by hand", typeof(HeatResultRequest), typeof(BracketDto), HeatResult,
                "Records (or corrects) the finishing order and advances pilots. Correcting is refused (409) once a later heat that received these pilots has been run. Locks the result against timing unless lock is false.");
            Write("POST", "brackets/{bracketId}/heats/{heatId}/reset", "Brackets", "Clear a heat's result", null, typeof(BracketDto), HeatReset,
                "Un-runs the heat and takes its pilots back out of later heats. Refused (409) once a later heat that received them has been run.");
            Write("POST", "brackets/{bracketId}/heats/{heatId}/lock", "Brackets", "Lock or unlock a heat's result against timing", typeof(HeatLockRequest), typeof(BracketDto), HeatLock);

            Examples(new Dictionary<string, string>
            {
                ["POST brackets"] = "{\n  \"name\": \"Open Class Finals\",\n  \"format\": \"elimination\",\n  \"setup\": { \"eliminationType\": \"double\", \"finalsFormat\": \"chase-the-ace\", \"pilotsPerRace\": 4, \"pilotsToAdvance\": 2, \"aceWinsRequired\": 2 },\n  \"pilots\": [\"{{pilot.name}}\"],\n  \"seatChannels\": [\"R1\", \"R3\", \"R5\", \"R7\"]\n}",
                ["PATCH brackets/{bracketId}"] = "{ \"seatChannels\": [\"R1\", \"R3\", \"R5\", \"R7\"], \"autoResults\": true, \"autoSend\": true }",
                ["POST brackets/{bracketId}/heats/{heatId}/result"] = "{ \"placement\": [\"{{pilot.name}}\"], \"lock\": true }",
                ["POST brackets/{bracketId}/heats/{heatId}/lock"] = "{ \"locked\": false }",
                ["POST brackets/{bracketId}/send"] = "{ \"makeCurrent\": false }",
                ["POST brackets/{bracketId}/heats/{heatId}/send"] = "{ \"makeCurrent\": true }",
            });
        }

        // ───────────────────────── lookups ─────────────────────────

        private BracketRecord BracketRef(ApiRequest r)
        {
            int id;
            if (!int.TryParse(r.Param("bracketId"), out id))
                throw ApiException.BadRequest("bracketId must be a number");
            BracketRecord rec = brackets.Store.Get(id);
            if (rec == null)
                throw ApiException.NotFound("bracket");
            return rec;
        }

        // Heat IDs of mains progression contain a colon ("A:W1-1"); accept it
        // encoded or not.
        private static string HeatParam(ApiRequest r)
        {
            return Uri.UnescapeDataString(r.Param("heatId") ?? "");
        }

        // Runs an engine call, turning its refusals into 409s with its own words.
        private static T EngineCall<T>(Func<T> f)
        {
            try
            {
                return f();
            }
            catch (global::BracketEngine.BracketException e)
            {
                throw ApiException.Conflict(e.Message);
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

            List<BracketService.SeedRow> rows = brackets.Seeding(rounds, laps, eventManager.Event.Pilots.Where(p => p != null && !p.PracticePilot));
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

        private object CreateBracket(ApiRequest r)
        {
            BracketCreateRequest body = r.BodyAs<BracketCreateRequest>();
            if (string.IsNullOrWhiteSpace(body.Name))
                throw ApiException.BadRequest("name is required");
            string format = string.IsNullOrWhiteSpace(body.Format) ? global::BracketEngine.FormatKinds.Elimination : body.Format.Trim().ToLowerInvariant();
            if (!BracketFormats.Any(f => f.Kind == format))
                throw ApiException.BadRequest("unknown format '" + body.Format + "'; use one of " + string.Join(", ", BracketFormats.Select(f => f.Kind)));
            if (body.Pilots == null || body.Pilots.Count < 2)
                throw ApiException.BadRequest("a bracket needs at least 2 pilots");

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

            BracketSetup setup = body.Setup ?? new BracketSetup();
            if (setup.MainsRosters != null)
            {
                // Rosters may name pilots; the engine works on IDs.
                setup.MainsRosters = setup.MainsRosters.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Select(x => PilotRef(x).ID.ToString()).ToList());
            }

            BracketRecord rec = EngineCall(() => brackets.Create(body.Name.Trim(), format, setup, pilots, body.SeatChannels));
            if (body.AutoSend)
                brackets.SetAutoSend(rec, true);
            Log("bracket created " + rec.Name);
            return Full(rec);
        }

        private object PatchBracket(ApiRequest r)
        {
            BracketRecord rec = BracketRef(r);
            BracketPatchRequest body = r.BodyAs<BracketPatchRequest>();
            if (body.Name != null)
            {
                if (body.Name.Trim().Length == 0)
                    throw ApiException.BadRequest("name can't be empty");
                brackets.Rename(rec, body.Name.Trim());
            }
            if (body.SeatChannels != null)
            {
                foreach (string seat in body.SeatChannels)
                    ChannelRef(seat);
                brackets.SetSeatChannels(rec, body.SeatChannels);
            }
            if (body.AutoSend.HasValue)
                brackets.SetAutoSend(rec, body.AutoSend.Value);
            if (body.AutoResults.HasValue)
            {
                brackets.SetAutoResults(rec, body.AutoResults.Value);
                if (body.AutoResults.Value)
                    brackets.Sync(rec);
            }
            return Full(brackets.Store.Get(rec.Id));
        }

        private object DeleteBracket(ApiRequest r)
        {
            BracketRecord rec = BracketRef(r);
            brackets.Delete(rec);
            Log("bracket deleted " + rec.Name);
            return new OkDto { Message = rec.Name + " deleted" };
        }

        private object SendReady(ApiRequest r)
        {
            BracketRecord rec = BracketRef(r);
            HeatSendRequest body = r.BodyAs<HeatSendRequest>();

            Dictionary<string, string> problems;
            List<Race> races = brackets.SendReady(rec, false, out problems);
            Race first = races.FirstOrDefault();
            List<string> sent = races.Select(x => rec.Links.First(l => l.Value == x.ID).Key).ToList();
            if (body.MakeCurrent && first != null)
                SelectRace(first);

            Log("bracket " + rec.Name + " sent " + sent.Count + " heats");
            return new BracketSendDto { Sent = sent, Problems = problems, Bracket = Full(brackets.Store.Get(rec.Id)) };
        }

        private object SendHeat(ApiRequest r)
        {
            BracketRecord rec = BracketRef(r);
            HeatSendRequest body = r.BodyAs<HeatSendRequest>();
            Race race = EngineCall(() => brackets.Send(rec, HeatParam(r)));
            if (body.MakeCurrent)
                SelectRace(race);
            return Full(brackets.Store.Get(rec.Id));
        }

        private void SelectRace(Race race)
        {
            Race current = RaceManager.CurrentRace;
            if (current != null && current != race && (current.Running || RaceManager.PreRaceStartDelay))
                throw ApiException.Conflict("the heat was sent, but another race is running so it wasn't selected");
            RaceManager.SetRace(race);
        }

        private object SyncBracket(ApiRequest r)
        {
            BracketRecord rec = BracketRef(r);
            Dictionary<string, BracketService.TimingOutcome> outcome = brackets.Sync(rec);
            rec = brackets.Store.Get(rec.Id);

            Dictionary<string, string> problems = new Dictionary<string, string>();
            foreach (string heat in outcome.Where(o => o.Value == BracketService.TimingOutcome.Blocked || o.Value == BracketService.TimingOutcome.Failed).Select(o => o.Key))
            {
                string detail;
                brackets.ApplyFromTiming(rec, heat, out detail);
                problems[heat] = detail;
            }
            return new BracketSyncDto
            {
                Applied = outcome.Where(o => o.Value == BracketService.TimingOutcome.Applied || o.Value == BracketService.TimingOutcome.Updated).Select(o => o.Key).ToList(),
                Problems = problems,
                Bracket = Full(rec),
            };
        }

        private object HeatResult(ApiRequest r)
        {
            BracketRecord rec = BracketRef(r);
            string heat = HeatParam(r);
            HeatResultRequest body = r.BodyAs<HeatResultRequest>();
            if (body.Placement == null || body.Placement.Count == 0)
                throw ApiException.BadRequest("placement is required");

            List<string> placement = body.Placement.Select(x => BracketPilotId(rec, x)).ToList();
            EngineCall(() => { brackets.RecordResult(rec, heat, placement, body.Lock ?? true); return true; });
            Log("bracket " + rec.Name + " heat " + heat + " result entered");
            return Full(brackets.Store.Get(rec.Id));
        }

        private object HeatReset(ApiRequest r)
        {
            BracketRecord rec = BracketRef(r);
            string heat = HeatParam(r);
            EngineCall(() => { brackets.ResetResult(rec, heat); return true; });
            Log("bracket " + rec.Name + " heat " + heat + " reset");
            return Full(brackets.Store.Get(rec.Id));
        }

        private object HeatLock(ApiRequest r)
        {
            BracketRecord rec = BracketRef(r);
            HeatLockRequest body = r.BodyAs<HeatLockRequest>();
            if (!body.Locked.HasValue)
                throw ApiException.BadRequest("locked is required");
            EngineCall(() => { brackets.Lock(rec, HeatParam(r), body.Locked.Value); return true; });
            rec = brackets.Store.Get(rec.Id);
            if (!body.Locked.Value && rec.AutoResults)
            {
                string detail;
                brackets.ApplyFromTiming(rec, HeatParam(r), out detail);
            }
            return Full(brackets.Store.Get(rec.Id));
        }

        // A pilot reference (ID or name) resolved against the bracket's own pilots.
        private string BracketPilotId(BracketRecord rec, string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                throw ApiException.BadRequest("empty pilot in placement");
            string r = reference.Trim();
            string byId = rec.Pilots.FirstOrDefault(p => string.Equals(p, r, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return byId;
            string byName = rec.Pilots.FirstOrDefault(p => string.Equals(brackets.PilotName(rec, p), r, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
                return byName;
            throw ApiException.BadRequest("'" + reference + "' isn't in this bracket");
        }

        // ───────────────────────── mapping ─────────────────────────

        private BracketSummaryDto Summary(BracketRecord rec, global::BracketEngine.IRaceFormat f)
        {
            return Fill(new BracketSummaryDto(), rec, f);
        }

        private T Fill<T>(T dto, BracketRecord rec, global::BracketEngine.IRaceFormat f) where T : BracketSummaryDto
        {
            List<global::BracketEngine.RaceView> races = BracketService.Views(f).Where(x => !x.IsVoid).ToList();
            string champion = f.Champion();
            dto.Id = rec.Id;
            dto.Name = rec.Name;
            dto.Format = rec.Format;
            dto.FormatName = BracketFormats.FirstOrDefault(x => x.Kind == rec.Format)?.Name ?? rec.Format;
            dto.PilotCount = rec.Pilots.Count;
            dto.HeatCount = races.Count;
            dto.HeatsDone = races.Count(x => x.IsComplete);
            dto.Champion = string.IsNullOrEmpty(champion) ? null : brackets.PilotName(rec, champion);
            dto.IsComplete = f.IsComplete();
            dto.Updated = rec.Updated;
            return dto;
        }

        private BracketDto Full(BracketRecord rec)
        {
            global::BracketEngine.IRaceFormat f = brackets.Format(rec);
            Dictionary<string, int> seed = rec.Pilots.Select((p, i) => new { p, i }).ToDictionary(x => x.p, x => x.i + 1);
            Func<string, string> name = id => brackets.PilotName(rec, id);

            BracketDto dto = Fill(new BracketDto(), rec, f);
            dto.Setup = rec.Setup;
            dto.SeatChannels = rec.SeatChannels;
            dto.AutoResults = rec.AutoResults;
            dto.AutoSend = rec.AutoSend;
            dto.Layout = f.LayoutKind;
            dto.Pilots = rec.Pilots.Select(p => new HeatPilotDto { PilotId = p, Name = name(p), Seed = seed[p] }).ToList();
            Dictionary<string, string> waiting = BracketService.WaitingOn(f);
            dto.Heats = BracketService.Views(f).Select(rv => Heat(rec, f, rv, seed, name, waiting)).ToList();
            dto.Standings = global::BracketEngine.RaceFormats.FinalOrdering(f, seed).Select(s => new BracketStandingDto
            {
                Rank = s.Rank,
                PilotId = s.Pilot,
                Name = name(s.Pilot),
                Detail = s.Detail,
                Tied = s.Tied,
            }).ToList();
            dto.Diagrams = BracketDiagrams.Build(f, name);
            return dto;
        }

        private HeatDto Heat(BracketRecord rec, global::BracketEngine.IRaceFormat f, global::BracketEngine.RaceView rv, Dictionary<string, int> seed, Func<string, string> name, Dictionary<string, string> waiting)
        {
            Race race = brackets.LinkedRace(rec, rv.Id);
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
            else if (rv.Locked) { state = "waiting"; note = rv.LockedReason; }
            else if (pilots.Count == 0) { state = "waiting"; note = "line-up not known yet"; }
            else if (waiting.ContainsKey(rv.Id)) { state = "waiting"; note = "waiting on " + waiting[rv.Id] + " for the rest of the line-up"; }
            else state = "ready";

            string why;
            bool canEdit = f.CanEditRace(rv.Id, out why);

            return new HeatDto
            {
                Id = rv.Id,
                Round = rv.Round,
                Group = rv.GroupLabel,
                Title = rv.Title,
                State = state,
                Note = note ?? (rv.IsComplete && !canEdit ? why : null),
                Pilots = pilots.Select(p => new HeatPilotDto
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
