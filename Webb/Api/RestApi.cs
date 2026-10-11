using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Tools;
using Webb.Bracketing;

namespace Webb.Api
{
    // FPVTrackside's REST API, served by EventWebServer under /api/v1.
    // documentation/REST_API.md is the guide; /api/v1/docs and
    // /api/v1/openapi.json are generated from the route table below.
    //
    // Threading: the web server runs on its own thread, but RaceLib and the UI
    // are driven from the game loop. Every handler therefore runs on the UI
    // thread (through the invoke delegate the app passes in) and this thread
    // waits for it — reads included, since RaceLib's lists are not safe to walk
    // while the game loop edits them.
    public partial class RestApi : IDisposable
    {
        public const string Prefix = "api/v1";
        public const int Version = 1;

        private readonly EventManager eventManager;
        private readonly IRaceControl raceControl;
        private readonly Action<Action> invokeOnUIThread;
        private readonly ApiRouter router = new ApiRouter();

        public ApiRouter Router { get { return router; } }

        private RaceManager RaceManager { get { return eventManager.RaceManager; } }

        // How long a call waits for the UI thread before answering 503.
        public static TimeSpan UIThreadTimeout = TimeSpan.FromSeconds(15);

        // eventDirectory: the open event's folder (brackets are saved in it).
        public RestApi(EventManager eventManager, IRaceControl raceControl, Action<Action> invokeOnUIThread, Func<string> eventDirectory = null)
        {
            this.eventManager = eventManager;
            this.raceControl = raceControl;
            this.invokeOnUIThread = invokeOnUIThread;
            brackets = new BracketService(eventManager, eventDirectory ?? (() => null), invokeOnUIThread);
            Register();
        }

        public void Dispose()
        {
            brackets?.Dispose();
        }

        // ───────────────────────── dispatch ─────────────────────────

        // Handles /api/v1/... Returns false for any other path so the web server
        // serves it as before.
        public bool TryHandle(HttpListenerContext context, string[] path, string body, out byte[] response)
        {
            response = null;
            string[] prefix = Prefix.Split('/');
            if (path.Length < prefix.Length || !path.Take(prefix.Length).SequenceEqual(prefix, StringComparer.OrdinalIgnoreCase))
                return false;

            string[] rest = path.Skip(prefix.Length).ToArray();
            HttpListenerResponse res = context.Response;

            // The interactive console: /api/v1/docs and its files under /api/v1/console/.
            if ((rest.Length == 1 && (rest[0].Equals("docs", StringComparison.OrdinalIgnoreCase) || rest[0].Equals("console", StringComparison.OrdinalIgnoreCase)))
                || (rest.Length == 2 && rest[0].Equals("console", StringComparison.OrdinalIgnoreCase)))
            {
                string type;
                byte[] file = ApiDocs.ConsoleFile(rest.Length == 1 ? "index.html" : rest[1], out type);
                if (file == null)
                {
                    res.StatusCode = 404;
                    response = new byte[0];
                    return true;
                }
                res.ContentType = type;
                res.AddHeader("Cache-Control", "no-cache");
                response = file;
                return true;
            }
            if (rest.Length == 1 && rest[0].Equals("openapi.json", StringComparison.OrdinalIgnoreCase))
            {
                res.ContentType = "application/json";
                response = Encoding.UTF8.GetBytes(ApiDocs.OpenApi(router.Routes, BaseUrl(context)));
                return true;
            }

            ApiRequest req = new ApiRequest
            {
                Context = context,
                Method = context.Request.HttpMethod,
                Query = context.Request.QueryString,
                Body = body,
            };

            object result;
            try
            {
                ApiRoute route;
                Dictionary<string, string> captured;
                switch (router.Match(req.Method, rest, out route, out captured))
                {
                    case ApiRouter.MatchResult.NotFound:
                        throw new ApiException(404, "not_found", "no such endpoint: " + req.Method + " /" + string.Join("/", path) + " — see /" + Prefix + "/docs");
                    case ApiRouter.MatchResult.MethodNotAllowed:
                        res.AddHeader("Allow", string.Join(", ", router.AllowedMethods(rest)));
                        throw new ApiException(405, "method_not_allowed", req.Method + " is not supported here; use " + string.Join(", ", router.AllowedMethods(rest)));
                }

                req.Params = captured;
                req.Status = route.SuccessStatus;
                if (route.Writes)
                    ApiAuth.RequireWrite(context);

                result = OnUIThread(() => route.Handler(req));
                res.StatusCode = req.Status;
            }
            catch (ApiException e)
            {
                res.StatusCode = e.Status;
                result = new ApiErrorDto { Ok = false, Code = e.Code, Error = e.Message };
            }
            catch (Exception e)
            {
                Logger.HTTP.LogException(this, e);
                res.StatusCode = 500;
                result = new ApiErrorDto { Ok = false, Code = "internal", Error = e.Message };
            }

            res.ContentType = "application/json; charset=utf-8";
            res.AddHeader("Cache-Control", "no-store");
            response = Encoding.UTF8.GetBytes(ApiJson.Serialize(result));
            return true;
        }

        private T OnUIThread<T>(Func<T> f)
        {
            if (invokeOnUIThread == null)
                return f();

            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            invokeOnUIThread(() =>
            {
                try
                {
                    tcs.SetResult(f());
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });

            try
            {
                if (!tcs.Task.Wait(UIThreadTimeout))
                    throw new ApiException(503, "busy", "FPVTrackside did not respond in time; try again");
                return tcs.Task.Result;
            }
            catch (AggregateException ae)
            {
                Exception inner = ae.Flatten().InnerExceptions.FirstOrDefault() ?? ae;
                if (inner is ApiException)
                    throw inner;
                throw new Exception(inner.Message, inner);
            }
        }

        private static string BaseUrl(HttpListenerContext context)
        {
            Uri u = context.Request.Url;
            return u.Scheme + "://" + u.Authority;
        }

        // ───────────────────────── routes ─────────────────────────

        private void Get(string template, string tag, string summary, Type response, Func<ApiRequest, object> handler, string description = null, bool array = false, params string[] query)
        {
            router.Add(new ApiRoute { Method = "GET", Template = template, Tag = tag, Summary = summary, Description = description, ResponseType = response, ResponseIsArray = array, QueryParams = query, Handler = handler });
        }

        private void Write(string method, string template, string tag, string summary, Type request, Type response, Func<ApiRequest, object> handler, string description = null, int status = 200, bool requestArray = false, bool responseArray = false)
        {
            router.Add(new ApiRoute { Method = method, Template = template, Tag = tag, Summary = summary, Description = description, RequestType = request, RequestIsArray = requestArray, ResponseType = response, ResponseIsArray = responseArray, SuccessStatus = status, Writes = true, Handler = handler });
        }

        private void Register()
        {
            // Info
            Get("", "Info", "Server and event information", typeof(ApiInfoDto), Info,
                "Start here: which event is open, whether this caller may make changes, and where the docs are.");

            // Event
            Get("event", "Event", "Get the open event", typeof(EventDto), r => ToDto(eventManager.Event));
            Write("PATCH", "event", "Event", "Change the event's name or rules", typeof(EventPatchRequest), typeof(EventDto), PatchEvent,
                "Laps, PB laps and race length are refused (409) while the event's rules are locked.");
            Get("channels", "Event", "List the event's channels", typeof(ChannelDto), r => eventManager.Channels.Select(ToDto).ToList(), array: true);

            // Pilots
            Get("pilots", "Pilots", "List the event's pilots", typeof(PilotDto), r => eventManager.Event.Pilots.OrderBy(p => p.Name).Select(ToDto).ToList(), array: true);
            Get("pilots/database", "Pilots", "List every pilot FPVTrackside knows (its pilot database), with whether each is in the event", typeof(PilotDto), PilotDatabase, array: true);
            Write("POST", "pilots", "Pilots", "Add a pilot to the event", typeof(PilotCreateRequest), typeof(PilotDto), CreatePilot, status: 201);
            Get("pilots/{pilotId}", "Pilots", "Get a pilot", typeof(PilotDto), r => ToDto(PilotRef(r.Param("pilotId"))),
                "pilotId may be the pilot's ID or exact name.");
            Write("PATCH", "pilots/{pilotId}", "Pilots", "Change a pilot", typeof(PilotPatchRequest), typeof(PilotDto), PatchPilot);
            Write("DELETE", "pilots/{pilotId}", "Pilots", "Remove a pilot from the event", null, typeof(OkDto), DeletePilot,
                "The pilot stays in races they have already flown; they are only taken off the event's pilot list.");
            Get("pilots/{pilotId}/races", "Pilots", "List a pilot's races", typeof(RaceSummaryDto), r => RaceManager.GetRaces(PilotRef(r.Param("pilotId"))).Where(x => x.Valid).OrderBy(x => x.Round.Order).ThenBy(x => x.RaceOrder).Select(ToSummary).ToList(), array: true);

            // Rounds
            Get("rounds", "Rounds", "List rounds in running order", typeof(RoundDto), r => Rounds().Select(ToDto).ToList(), array: true);
            Write("POST", "rounds", "Rounds", "Add an empty round", typeof(RoundCreateRequest), typeof(RoundDto), CreateRound, status: 201);
            Get("rounds/{roundId}", "Rounds", "Get a round", typeof(RoundDto), r => ToDto(RoundRef(r)));
            Write("PATCH", "rounds/{roundId}", "Rounds", "Rename a round", typeof(RoundPatchRequest), typeof(RoundDto), PatchRound);
            Write("DELETE", "rounds/{roundId}", "Rounds", "Delete a round", null, typeof(OkDto), DeleteRound,
                "Races in the round that haven't been run are deleted with it; finished races are kept.");
            Get("rounds/{roundId}/races", "Rounds", "List a round's races", typeof(RaceSummaryDto), r => RaceManager.GetRaces(RoundRef(r)).Select(ToSummary).ToList(), array: true);

            // Races
            Get("races", "Races", "List races in running order", typeof(RaceSummaryDto), ListRaces,
                "Filter with ?state=pending|running|finished, ?round=<round ID> and ?pilot=<pilot ID or name>.", array: true, query: new[] { "state", "round", "pilot" });
            Write("POST", "races", "Races", "Create a race", typeof(RaceCreateRequest), typeof(RaceDto), CreateRace, status: 201);
            Get("races/current", "Races", "Get the race selected in FPVTrackside", typeof(RaceDto), r => ToDto(CurrentRaceOr404()));
            Get("races/{raceId}", "Races", "Get a race with its laps and results", typeof(RaceDto), r => ToDto(RaceRef(r)));
            Write("DELETE", "races/{raceId}", "Races", "Delete a race", null, typeof(OkDto), DeleteRace,
                "Refused (409) while the race is running.");
            Write("POST", "races/{raceId}/reset", "Races", "Clear a race's laps and results so it can be re-run", null, typeof(RaceDto), ResetRace,
                "Refused (409) while the race is running.");

            // Race pilots
            Write("PUT", "races/{raceId}/pilots", "Race pilots", "Replace a race's line-up", typeof(RacePilotRequest), typeof(RaceDto), SetRacePilots,
                "The body is an array. Pilots not in the event are added to it. Refused (409) while the race is running.", requestArray: true);
            Write("POST", "races/{raceId}/pilots", "Race pilots", "Seat a pilot in a race", typeof(RacePilotRequest), typeof(RaceDto), AddRacePilot,
                "Moves the pilot to the given channel if they are already in the race. Refused (409) while the race is running.");
            Write("DELETE", "races/{raceId}/pilots/{pilotId}", "Race pilots", "Take a pilot out of a race", null, typeof(RaceDto), RemoveRacePilot,
                "On a finished race the pilot's laps are invalidated. Refused (409) while the race is running.");

            // Laps and detections
            Get("races/{raceId}/laps", "Laps", "List a race's laps", typeof(LapDto), r => Laps(RaceRef(r), PilotFilter(r)),
                "Filter with ?pilot=<pilot ID or name>.", array: true, query: new[] { "pilot" });
            Write("POST", "races/{raceId}/laps", "Laps", "Add a lap-line crossing", typeof(LapCreateRequest), typeof(PilotLapsDto), AddLap,
                "Works on any race, finished or not; laps, results and lap records are rebuilt. Adding a crossing within 250 ms of an existing valid one does nothing, so retries are safe.", status: 201);
            Get("races/{raceId}/detections", "Laps", "List a race's timing crossings (lap lines and split gates)", typeof(DetectionDto), r => Detections(RaceRef(r), PilotFilter(r)),
                "Filter with ?pilot=<pilot ID or name>. Includes invalid crossings.", array: true, query: new[] { "pilot" });
            Write("PATCH", "races/{raceId}/detections/{detectionId}", "Laps", "Count or discount a crossing", typeof(DetectionPatchRequest), typeof(PilotLapsDto), PatchDetection,
                "Invalidating a lap-line crossing merges the laps either side of it; validating one splits them again.");
            Write("POST", "races/{raceId}/lap-corrections", "Laps", "Apply a lap-timing correction", typeof(LapCorrectionRequest), typeof(LapCorrectionResultDto), LapCorrection,
                "Invalidates false lap lines and adds missed ones in one step. Everything is checked before anything changes. Missed lines are placed relative to another crossing of the same pilot, so clock differences between machines don't matter. Repeating a correction is harmless.");

            // Results
            Get("races/{raceId}/results", "Results", "Get a race's results", typeof(ResultDto), r => Results(RaceRef(r)), array: true);

            // Race control
            Get("race-control", "Race control", "What the race director's screen is doing", typeof(RaceControlDto), r => RaceControlState());
            Write("PUT", "race-control/current", "Race control", "Select a race", typeof(CurrentRaceRequest), typeof(RaceControlDto), SetCurrentRace,
                "Refused (409) while the selected race is running.");
            Write("POST", "race-control/next", "Race control", "Select the next race", null, typeof(RaceControlDto), r => Step(true));
            Write("POST", "race-control/previous", "Race control", "Select the previous race", null, typeof(RaceControlDto), r => Step(false));
            Write("POST", "race-control/start", "Race control", "Start the selected race", null, typeof(RaceControlDto), StartRace,
                "Same as pressing Start: runs the start countdown and sounds. Needs \"Race control over HTTP\" switched on in FPVTrackside's settings (403 race_control_disabled otherwise).");
            Write("POST", "race-control/stop", "Race control", "End the running race", null, typeof(RaceControlDto), r => RaceAction(() => RaceManager.EndRace(), "no race is running"));
            Write("POST", "race-control/cancel", "Race control", "Cancel a start countdown", null, typeof(RaceControlDto), r => RaceAction(() => RaceManager.CancelRaceStart(false), "no start is counting down"));

            RegisterBrackets();

            Examples(new Dictionary<string, string>
            {
                ["PATCH event"] = "{ \"name\": \"Club Champs Round 3\", \"laps\": 3 }",
                ["POST pilots"] = "{ \"name\": \"NewPilot\", \"channel\": \"R2\" }",
                ["PATCH pilots/{pilotId}"] = "{ \"aircraft\": \"5 inch\", \"channel\": \"R4\" }",
                ["POST rounds"] = "{ \"eventType\": \"Race\" }",
                ["PATCH rounds/{roundId}"] = "{ \"name\": \"Semi Finals\" }",
                ["POST races"] = "{\n  \"roundId\": \"{{round.id}}\",\n  \"pilots\": [\n    { \"pilot\": \"{{pilot.name}}\", \"channel\": \"R1\" }\n  ],\n  \"makeCurrent\": false\n}",
                ["PUT races/{raceId}/pilots"] = "[\n  { \"pilot\": \"{{pilot.name}}\", \"channel\": \"R1\" }\n]",
                ["POST races/{raceId}/pilots"] = "{ \"pilot\": \"{{pilot.name}}\", \"channel\": \"R8\" }",
                ["POST races/{raceId}/laps"] = "{\n  \"pilot\": \"{{pilot.id}}\",\n  \"anchorDetectionId\": \"{{split.id}}\",\n  \"offsetMs\": 12000\n}",
                ["PATCH races/{raceId}/detections/{detectionId}"] = "{ \"valid\": false, \"validityType\": \"Marshall\" }",
                ["POST races/{raceId}/lap-corrections"] = "{\n  \"pilotId\": \"{{pilot.id}}\",\n  \"invalidate\": [\"{{lapLine.id}}\"],\n  \"add\": [\n    { \"anchorDetectionId\": \"{{split.id}}\", \"offsetMs\": 12000 }\n  ]\n}",
                ["PUT race-control/current"] = "{ \"raceId\": \"{{race.id}}\" }",
            });
        }

        private void Examples(Dictionary<string, string> examples)
        {
            foreach (KeyValuePair<string, string> e in examples)
            {
                string[] parts = e.Key.Split(new[] { ' ' }, 2);
                ApiRoute route = router.Routes.First(r => r.Method == parts[0] && r.Template == parts[1]);
                route.Example = e.Value;
            }
        }

        // ───────────────────────── lookups ─────────────────────────

        private Pilot PilotRef(string reference, bool createIfMissing = false)
        {
            if (string.IsNullOrWhiteSpace(reference))
                throw ApiException.BadRequest("a pilot is required");

            Guid id;
            Pilot p = Guid.TryParse(reference, out id)
                ? eventManager.GetPilot(id)
                : eventManager.Event.Pilots.FirstOrDefault(x => string.Equals(x.Name, reference.Trim(), StringComparison.OrdinalIgnoreCase));

            if (p == null && createIfMissing && id != Guid.Empty)
            {
                // A pilot from FPVTrackside's pilot database who isn't in the
                // event yet: add them, as the pilot list's Add button does.
                p = AllPilots().FirstOrDefault(x => x.ID == id);
                if (p != null)
                    eventManager.AddPilot(p);
            }
            if (p == null && createIfMissing && id == Guid.Empty)
            {
                p = eventManager.GetCreatePilot(reference.Trim());
                eventManager.AddPilot(p);
            }
            if (p == null)
                throw ApiException.NotFound("pilot '" + reference + "'");
            return p;
        }

        // FPVTrackside's pilot database: every pilot from every event on this
        // machine, plus the open event's own (which may not be saved there yet).
        private List<Pilot> AllPilots()
        {
            Dictionary<Guid, Pilot> all = new Dictionary<Guid, Pilot>();
            using (IDatabase db = DatabaseFactory.Open(eventManager.EventId))
            {
                foreach (Pilot p in db.All<Pilot>())
                    if (p != null && !string.IsNullOrWhiteSpace(p.Name))
                        all[p.ID] = p;
            }
            foreach (Pilot p in eventManager.Event.Pilots)
                if (p != null)
                    all[p.ID] = p;
            return all.Values.ToList();
        }

        private object PilotDatabase(ApiRequest r)
        {
            HashSet<Guid> inEvent = new HashSet<Guid>(eventManager.Event.Pilots.Where(p => p != null).Select(p => p.ID));
            return AllPilots()
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p =>
                {
                    PilotDto dto = ToDto(p);
                    dto.InEvent = inEvent.Contains(p.ID);
                    if (dto.InEvent != true) dto.Channel = null;
                    return dto;
                })
                .ToList();
        }

        private Pilot PilotFilter(ApiRequest r)
        {
            string p = r.Query["pilot"];
            return string.IsNullOrEmpty(p) ? null : PilotRef(p);
        }

        // A channel by ID, label ("R1", "L3"), frequency ("5658") or label with
        // band ("Raceband 1"). The event's own channels are preferred, then
        // every channel FPVTrackside knows.
        public static Channel ResolveChannel(string reference, IEnumerable<Channel> eventChannels)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;

            string s = reference.Trim();
            foreach (IEnumerable<Channel> pool in new[] { eventChannels ?? Enumerable.Empty<Channel>(), Channel.AllChannels })
            {
                Guid id;
                if (Guid.TryParse(s, out id))
                {
                    Channel byId = pool.FirstOrDefault(c => c.ID == id);
                    if (byId != null) return byId;
                    continue;
                }

                Channel byLabel = pool.FirstOrDefault(c => string.Equals(c.GetBandChannelText(), s, StringComparison.OrdinalIgnoreCase)
                                                         || string.Equals(c.DisplayName, s, StringComparison.OrdinalIgnoreCase)
                                                         || string.Equals(c.UIDisplayName, s, StringComparison.OrdinalIgnoreCase)
                                                         || string.Equals(c.Band + " " + c.Number, s, StringComparison.OrdinalIgnoreCase));
                if (byLabel != null) return byLabel;

                int freq;
                if (int.TryParse(s.Replace("MHz", "").Replace("mhz", "").Trim(), out freq) && freq > 1000)
                {
                    Channel byFreq = pool.FirstOrDefault(c => c.Frequency == freq);
                    if (byFreq != null) return byFreq;
                }
            }
            throw ApiException.BadRequest("unknown channel '" + reference + "'");
        }

        private Channel ChannelRef(string reference)
        {
            return ResolveChannel(reference, eventManager.Channels);
        }

        private Race RaceRef(ApiRequest r)
        {
            Race race = RaceManager.GetRaceByRaceId(r.GuidParam("raceId"));
            if (race == null || !race.Valid)
                throw ApiException.NotFound("race");
            return race;
        }

        private Race CurrentRaceOr404()
        {
            Race race = RaceManager.CurrentRace;
            if (race == null)
                throw ApiException.NotFound("current race (none is selected)");
            return race;
        }

        private IEnumerable<Round> Rounds()
        {
            lock (eventManager.Event.Rounds)
            {
                return eventManager.Event.Rounds.Where(x => x.Valid).OrderBy(x => x.Order).ThenBy(x => x.RoundNumber).ToArray();
            }
        }

        private Round RoundRef(ApiRequest r)
        {
            Guid id = r.GuidParam("roundId");
            Round round = Rounds().FirstOrDefault(x => x.ID == id);
            if (round == null)
                throw ApiException.NotFound("round");
            return round;
        }

        private static EventTypes ParseEventType(string s, EventTypes fallback)
        {
            if (string.IsNullOrWhiteSpace(s))
                return fallback;
            EventTypes t;
            if (!Enum.TryParse(s.Trim(), true, out t) || t == EventTypes.Unknown || int.TryParse(s, out _))
                throw ApiException.BadRequest("unknown eventType '" + s + "'; use one of " + string.Join(", ", ApiDocs.EventTypeNames()));
            return t;
        }

        private void NotWhileRunning(Race race)
        {
            if (race.Running || (race == RaceManager.CurrentRace && RaceManager.PreRaceStartDelay))
                throw ApiException.Conflict("the race is running");
        }

        // ───────────────────────── handlers ─────────────────────────

        private object Info(ApiRequest r)
        {
            return new ApiInfoDto
            {
                Product = "FPVTrackside",
                Version = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version?.ToString(),
                ApiVersion = Version,
                EventId = eventManager.EventId,
                EventName = eventManager.Event?.Name,
                CanWrite = ApiAuth.CanWrite(r.Context),
                RaceControlEnabled = raceControl != null,
                Docs = "/" + Prefix + "/docs",
                OpenApi = "/" + Prefix + "/openapi.json",
            };
        }

        private object PatchEvent(ApiRequest r)
        {
            EventPatchRequest body = r.BodyAs<EventPatchRequest>();
            Event ev = eventManager.Event;

            if ((body.Laps.HasValue || body.PbLaps.HasValue || body.RaceLengthSeconds.HasValue) && ev.RulesLocked)
                throw ApiException.Conflict("the event's rules are locked");
            if (body.Laps.HasValue && (body.Laps < 0 || body.Laps > 1000))
                throw ApiException.BadRequest("laps must be 0-1000");
            if (body.PbLaps.HasValue && (body.PbLaps < 1 || body.PbLaps > 100))
                throw ApiException.BadRequest("pbLaps must be 1-100");
            if (body.RaceLengthSeconds.HasValue && (body.RaceLengthSeconds < 0 || body.RaceLengthSeconds > 86400))
                throw ApiException.BadRequest("raceLengthSeconds must be 0-86400");
            if (body.Name != null && body.Name.Trim().Length == 0)
                throw ApiException.BadRequest("name can't be empty");

            if (body.Name != null)
                eventManager.SetEventName(body.Name.Trim());
            if (body.PbLaps.HasValue)
                eventManager.SetPBLaps(body.PbLaps.Value);
            if (body.RaceLengthSeconds.HasValue)
                eventManager.SetRaceLength(body.RaceLengthSeconds.Value);
            if (body.Laps.HasValue)
                eventManager.SetEventLaps(body.Laps.Value);

            Log("event changed");
            return ToDto(ev);
        }

        private object CreatePilot(ApiRequest r)
        {
            PilotCreateRequest body = r.BodyAs<PilotCreateRequest>();
            if (string.IsNullOrWhiteSpace(body.Name))
                throw ApiException.BadRequest("name is required");

            string name = body.Name.Trim();
            if (eventManager.Event.Pilots.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw ApiException.Conflict("a pilot named '" + name + "' is already in the event");

            Channel channel = ChannelRef(body.Channel);
            Pilot pilot = eventManager.GetCreatePilot(name);
            if (body.FirstName != null) pilot.FirstName = body.FirstName;
            if (body.LastName != null) pilot.LastName = body.LastName;

            bool added = channel != null ? eventManager.AddPilot(pilot, channel) : eventManager.AddPilot(pilot);
            if (!added)
                throw ApiException.Conflict("the pilot is already in the event");

            Log("pilot added " + name);
            return ToDto(pilot);
        }

        private object PatchPilot(ApiRequest r)
        {
            Pilot pilot = PilotRef(r.Param("pilotId"));
            PilotPatchRequest body = r.BodyAs<PilotPatchRequest>();

            if (body.Name != null)
            {
                string name = body.Name.Trim();
                if (name.Length == 0)
                    throw ApiException.BadRequest("name can't be empty");
                if (eventManager.Event.Pilots.Any(p => p != pilot && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                    throw ApiException.Conflict("another pilot is already named '" + name + "'");
                pilot.Name = name;
            }
            Channel channel = ChannelRef(body.Channel);

            if (body.FirstName != null) pilot.FirstName = body.FirstName;
            if (body.LastName != null) pilot.LastName = body.LastName;
            if (body.DiscordId != null) pilot.DiscordID = body.DiscordId;
            if (body.Aircraft != null) pilot.Aircraft = body.Aircraft;
            if (body.CatchPhrase != null) pilot.CatchPhrase = body.CatchPhrase;
            if (body.PracticePilot.HasValue) pilot.PracticePilot = body.PracticePilot.Value;
            if (body.VelocidroneUid != null) pilot.VelocidroneUID = body.VelocidroneUid;

            using (RaceLib.IDatabase db = RaceLib.DatabaseFactory.Open(eventManager.EventId))
            {
                db.Update(pilot);
            }
            eventManager.RefreshPilots(new[] { pilot });

            if (channel != null)
                eventManager.SetPilotChannel(pilot, channel);

            Log("pilot changed " + pilot.Name);
            return ToDto(pilot);
        }

        private object DeletePilot(ApiRequest r)
        {
            Pilot pilot = PilotRef(r.Param("pilotId"));
            Race current = RaceManager.CurrentRace;
            if (current != null && current.HasPilot(pilot) && (current.Running || RaceManager.PreRaceStartDelay))
                throw ApiException.Conflict("the pilot is in the running race");

            if (!eventManager.RemovePilot(pilot))
                throw ApiException.NotFound("pilot in the event");

            Log("pilot removed " + pilot.Name);
            return new OkDto { Message = pilot.Name + " removed from the event" };
        }

        private object CreateRound(ApiRequest r)
        {
            RoundCreateRequest body = r.BodyAs<RoundCreateRequest>();
            EventTypes type = ParseEventType(body.EventType, eventManager.Event.EventType);
            Round round = eventManager.RoundManager.CreateEmptyRound(type);
            Log("round added " + round);
            return ToDto(round);
        }

        private object PatchRound(ApiRequest r)
        {
            Round round = RoundRef(r);
            RoundPatchRequest body = r.BodyAs<RoundPatchRequest>();
            if (body.Name != null)
                eventManager.RoundManager.RenameRound(round, body.Name.Trim());
            return ToDto(round);
        }

        private object DeleteRound(ApiRequest r)
        {
            Round round = RoundRef(r);
            Race current = RaceManager.CurrentRace;
            if (current != null && current.Round == round && (current.Running || RaceManager.PreRaceStartDelay))
                throw ApiException.Conflict("a race in the round is running");

            eventManager.RoundManager.RemoveRound(round);
            Log("round removed " + round);
            return new OkDto { Message = "round deleted" };
        }

        private object ListRaces(ApiRequest r)
        {
            IEnumerable<Race> races = RaceManager.Races;

            string state = r.Query["state"];
            if (!string.IsNullOrEmpty(state))
            {
                if (!new[] { "pending", "running", "finished" }.Contains(state))
                    throw ApiException.BadRequest("state must be pending, running or finished");
                races = races.Where(x => RaceState(x) == state);
            }

            string round = r.Query["round"];
            if (!string.IsNullOrEmpty(round))
            {
                Guid id;
                if (!Guid.TryParse(round, out id))
                    throw ApiException.BadRequest("round must be a round ID");
                races = races.Where(x => x.Round != null && x.Round.ID == id);
            }

            Pilot pilot = PilotFilter(r);
            if (pilot != null)
                races = races.Where(x => x.HasPilot(pilot));

            return races.OrderBy(x => x.Round?.Order ?? 0).ThenBy(x => x.RaceOrder).Select(ToSummary).ToList();
        }

        private object CreateRace(ApiRequest r)
        {
            RaceCreateRequest body = r.BodyAs<RaceCreateRequest>();

            Round round;
            if (body.RoundId.HasValue)
            {
                round = Rounds().FirstOrDefault(x => x.ID == body.RoundId.Value);
                if (round == null)
                    throw ApiException.NotFound("round");
            }
            else
            {
                round = eventManager.RoundManager.CreateEmptyRound(ParseEventType(body.EventType, eventManager.Event.EventType));
            }

            if (body.MakeCurrent && RaceManager.CurrentRace != null && RaceManager.CurrentRace.Running)
                throw ApiException.Conflict("another race is running, so the new race can't be selected");

            List<Tuple<Pilot, Channel>> lineup = Lineup(body.Pilots);
            Race race = RaceManager.AddRaceToRound(round);
            if (lineup.Any())
                RaceManager.SetRacePilots(race, lineup, false);

            if (body.MakeCurrent)
                RaceManager.SetRace(race);

            Log("race added " + race.RaceName);
            return ToDto(race);
        }

        private List<Tuple<Pilot, Channel>> Lineup(IEnumerable<RacePilotRequest> seats)
        {
            List<Tuple<Pilot, Channel>> lineup = new List<Tuple<Pilot, Channel>>();
            foreach (RacePilotRequest seat in seats ?? Enumerable.Empty<RacePilotRequest>())
            {
                Pilot pilot = PilotRef(seat?.Pilot, true);
                if (lineup.Any(x => x.Item1 == pilot))
                    throw ApiException.BadRequest(pilot.Name + " is listed twice");

                Func<Channel, bool> taken = c => lineup.Any(x => x.Item2 == c || x.Item2.InterferesWith(c));

                Channel channel = ChannelRef(seat.Channel);
                if (channel != null)
                {
                    // An explicit channel is what the caller wants; refuse rather than move anyone.
                    if (taken(channel))
                        throw ApiException.BadRequest("two pilots are on " + channel.GetBandChannelText());
                }
                else
                {
                    // The pilot's usual channel if it's free, else the first free event channel.
                    channel = eventManager.GetChannel(pilot);
                    if (channel == null || taken(channel))
                        channel = eventManager.Channels.FirstOrDefault(c => !taken(c));
                    if (channel == null)
                        throw ApiException.BadRequest("no free channel for " + pilot.Name);
                }

                lineup.Add(Tuple.Create(pilot, channel));
            }
            return lineup;
        }

        private object DeleteRace(ApiRequest r)
        {
            Race race = RaceRef(r);
            NotWhileRunning(race);
            RaceManager.RemoveRace(race, false);
            Log("race removed " + race.RaceName);
            return new OkDto { Message = "race deleted" };
        }

        private object ResetRace(ApiRequest r)
        {
            Race race = RaceRef(r);
            NotWhileRunning(race);
            RaceManager.ResetRace(race);
            Log("race reset " + race.RaceName);
            return ToDto(race);
        }

        private object SetRacePilots(ApiRequest r)
        {
            Race race = RaceRef(r);
            NotWhileRunning(race);
            List<Tuple<Pilot, Channel>> lineup = Lineup(r.BodyAsArray<List<RacePilotRequest>>());
            if (IsSelectedAndUnrun(race))
            {
                // The race-director screen's own path, so the live channel tiles follow.
                foreach (Pilot p in race.Pilots.Where(p => p != null).ToArray())
                    RaceManager.RemovePilotFromCurrentRace(p, false);
                foreach (Tuple<Pilot, Channel> seat in lineup)
                    Seat(race, seat.Item1, seat.Item2);
            }
            else
            {
                RaceManager.SetRacePilots(race, lineup, false);
            }
            Log("line-up set " + race.RaceName);
            return ToDto(race);
        }

        private object AddRacePilot(ApiRequest r)
        {
            Race race = RaceRef(r);
            NotWhileRunning(race);
            RacePilotRequest body = r.BodyAs<RacePilotRequest>();
            Pilot pilot = PilotRef(body.Pilot, true);

            Channel channel = ChannelRef(body.Channel);
            if (channel == null)
            {
                channel = race.HasPilot(pilot) ? race.GetChannel(pilot) : null;
                if (channel == null)
                {
                    Channel preferred = eventManager.GetChannel(pilot);
                    channel = preferred != null && race.IsFrequencyFree(preferred) ? preferred : RaceManager.GetFreeChannel(race);
                }
            }
            if (channel == null)
                throw ApiException.Conflict("the race has no free channel");

            Pilot sitting = race.GetPilot(channel);
            if (sitting != null && sitting != pilot)
                throw ApiException.Conflict(sitting.Name + " is already on " + channel.GetBandChannelText());

            Seat(race, pilot, channel);
            Log("pilot seated " + pilot.Name + " " + race.RaceName);
            return ToDto(race);
        }

        private object RemoveRacePilot(ApiRequest r)
        {
            Race race = RaceRef(r);
            NotWhileRunning(race);
            Pilot pilot = race.Pilots.FirstOrDefault(p => p != null && (p.ID.ToString().Equals(r.Param("pilotId"), StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, r.Param("pilotId"), StringComparison.OrdinalIgnoreCase)));
            if (pilot == null)
                throw ApiException.NotFound("pilot in that race");

            if (IsSelectedAndUnrun(race))
                RaceManager.RemovePilotFromCurrentRace(pilot, false);
            else
                RaceManager.RemovePilot(race, pilot, true);
            Log("pilot unseated " + pilot.Name + " " + race.RaceName);
            return ToDto(race);
        }

        // The race selected on the race-director screen that hasn't started:
        // seat changes go through the same calls the screen makes, so its live
        // channel tiles update. Any other race is edited directly, which
        // refreshes the rounds view.
        private bool IsSelectedAndUnrun(Race race)
        {
            return race == RaceManager.CurrentRace && !race.Started;
        }

        private void Seat(Race race, Pilot pilot, Channel channel)
        {
            bool ok;
            if (IsSelectedAndUnrun(race))
            {
                if (race.HasPilot(pilot))
                    RaceManager.RemovePilotFromCurrentRace(pilot, false);
                ok = RaceManager.AddPilot(channel, pilot);
            }
            else
            {
                ok = RaceManager.SetPilot(race, channel, pilot);
            }
            if (!ok)
                throw ApiException.Conflict("FPVTrackside refused to seat " + pilot.Name + " on " + channel.GetBandChannelText() + " (is that channel in the event and free?)");
        }

        private Pilot RacePilot(Race race, string reference)
        {
            Pilot pilot = PilotRef(reference);
            if (!race.HasPilot(pilot))
                throw ApiException.BadRequest(pilot.Name + " is not in that race");
            return pilot;
        }

        private object AddLap(ApiRequest r)
        {
            Race race = RaceRef(r);
            LapCreateRequest body = r.BodyAs<LapCreateRequest>();
            Pilot pilot = RacePilot(race, body.Pilot);

            int modes = (body.Time.HasValue ? 1 : 0) + (body.RaceTimeMs.HasValue ? 1 : 0) + (body.AnchorDetectionId.HasValue ? 1 : 0);
            if (modes != 1)
                throw ApiException.BadRequest("give exactly one of time, raceTimeMs, or anchorDetectionId + offsetMs");

            DateTime time;
            if (body.Time.HasValue)
            {
                time = body.Time.Value.Kind == DateTimeKind.Utc ? body.Time.Value.ToLocalTime() : body.Time.Value;
            }
            else if (body.RaceTimeMs.HasValue)
            {
                if (!race.Started)
                    throw ApiException.Conflict("the race hasn't started, so raceTimeMs has nothing to count from");
                if (body.RaceTimeMs < 0 || body.RaceTimeMs > 86400000)
                    throw ApiException.BadRequest("raceTimeMs must be 0-86400000");
                time = race.Start.AddMilliseconds(body.RaceTimeMs.Value);
            }
            else
            {
                if (!body.OffsetMs.HasValue || body.OffsetMs <= 0 || body.OffsetMs > 600000)
                    throw ApiException.BadRequest("offsetMs must be 1-600000");
                Detection anchor = DetectionsOf(race).FirstOrDefault(d => d.ID == body.AnchorDetectionId.Value);
                if (anchor == null || anchor.Pilot != pilot)
                    throw ApiException.NotFound("anchor detection for that pilot");
                time = anchor.Time.AddMilliseconds(body.OffsetMs.Value);
            }

            if (race.Started && time < race.Start.AddMinutes(-5))
                throw ApiException.BadRequest("that time is before the race started");

            List<Detection> created = RaceManager.AddLapsToRace(race, pilot, new[] { time });
            Log("lap added " + pilot.Name + " " + race.RaceName + " " + time.ToString("HH:mm:ss.fff"));
            return new PilotLapsDto { RaceId = race.ID, PilotId = pilot.ID, Created = created.Select(d => ToDto(race, d)).ToList(), Laps = Laps(race, pilot) };
        }

        private object PatchDetection(ApiRequest r)
        {
            Race race = RaceRef(r);
            Guid id = r.GuidParam("detectionId");
            Detection detection = DetectionsOf(race).FirstOrDefault(d => d.ID == id);
            if (detection == null)
                throw ApiException.NotFound("detection");

            DetectionPatchRequest body = r.BodyAs<DetectionPatchRequest>();
            if (!body.Valid.HasValue)
                throw ApiException.BadRequest("valid is required");

            Detection.ValidityTypes type = Detection.ValidityTypes.ManualOverride;
            if (!string.IsNullOrEmpty(body.ValidityType))
            {
                if (!Enum.TryParse(body.ValidityType, true, out type) || type == Detection.ValidityTypes.Auto)
                    throw ApiException.BadRequest("validityType must be ManualOverride or Marshall");
            }

            if (detection.Valid != body.Valid.Value)
            {
                RaceManager.SetDetectionValidity(race, detection, body.Valid.Value, type);
                RaceManager.RefreshPilotRecords(race, detection.Pilot);
            }

            Log("detection " + (body.Valid.Value ? "validated " : "invalidated ") + detection.Pilot?.Name + " " + race.RaceName);
            return new PilotLapsDto { RaceId = race.ID, PilotId = detection.Pilot?.ID ?? Guid.Empty, Created = new List<DetectionDto>(), Laps = Laps(race, detection.Pilot) };
        }

        private object LapCorrection(ApiRequest r)
        {
            Race race = RaceRef(r);
            LapCorrectionRequest body = r.BodyAs<LapCorrectionRequest>();
            if (body.PilotId == Guid.Empty)
                throw ApiException.BadRequest("pilotId is required");

            List<Guid> invalidate = body.Invalidate ?? new List<Guid>();
            List<KeyValuePair<Guid, long>> add = (body.Add ?? new List<LapCorrectionAdd>()).Select(a => new KeyValuePair<Guid, long>(a.AnchorDetectionId, a.OffsetMs)).ToList();
            if (!invalidate.Any() && !add.Any())
                throw ApiException.BadRequest("nothing to change: give invalidate and/or add");

            int before = DetectionsOf(race).Count(d => d.Pilot != null && d.Pilot.ID == body.PilotId);

            string error;
            if (!RaceManager.ApplyLapCorrection(race.ID, body.PilotId, invalidate, add, out error))
                throw ApiException.BadRequest(error);

            Pilot pilot = race.Pilots.FirstOrDefault(p => p != null && p.ID == body.PilotId);
            int after = DetectionsOf(race).Count(d => d.Pilot != null && d.Pilot.ID == body.PilotId);
            Log("lap correction " + pilot?.Name + " " + race.RaceName);
            return new LapCorrectionResultDto { Ok = true, Error = "", Invalidated = invalidate.Count, Added = after - before, Laps = Laps(race, pilot) };
        }

        private RaceControlDto RaceControlState()
        {
            Race current = RaceManager.CurrentRace;
            string state = "none";
            if (current != null)
            {
                if (RaceManager.PreRaceStartDelay) state = "starting";
                else if (current.Running) state = "running";
                else if (current.Ended) state = "finished";
                else state = "ready";
            }

            Race next = current != null ? RaceManager.GetNextRace(current) : RaceManager.GetNextRace(true);
            return new RaceControlDto
            {
                State = state,
                CanStart = state == "ready" && RaceManager.CanRunRace,
                RaceControlEnabled = raceControl != null,
                ElapsedSeconds = state == "running" ? Math.Round(RaceManager.ElapsedTime.TotalSeconds, 3) : 0,
                CurrentRace = current != null ? ToSummary(current) : null,
                NextRace = next != null ? ToSummary(next) : null,
            };
        }

        private void RequireRaceControl()
        {
            if (raceControl == null)
                throw new ApiException(403, "race_control_disabled", "race control over HTTP is switched off in FPVTrackside's settings");
        }

        private object SetCurrentRace(ApiRequest r)
        {
            RequireRaceControl();
            CurrentRaceRequest body = r.BodyAs<CurrentRaceRequest>();
            Race current = RaceManager.CurrentRace;
            if (current != null && (current.Running || RaceManager.PreRaceStartDelay))
                throw ApiException.Conflict("the selected race is running");

            if (!body.RaceId.HasValue)
            {
                RaceManager.ClearRace();
                return RaceControlState();
            }

            Race race = RaceManager.GetRaceByRaceId(body.RaceId.Value);
            if (race == null || !race.Valid)
                throw ApiException.NotFound("race");
            if (!RaceManager.SetRace(race))
                throw ApiException.Conflict("FPVTrackside refused to select the race");
            Log("race selected " + race.RaceName);
            return RaceControlState();
        }

        private object Step(bool forward)
        {
            RequireRaceControl();
            Race current = RaceManager.CurrentRace;
            if (current != null && (current.Running || RaceManager.PreRaceStartDelay))
                throw ApiException.Conflict("the selected race is running");

            Race target = forward ? RaceManager.GetNextRace(false, false) : RaceManager.GetPrevRace();
            if (target == null)
                throw ApiException.Conflict(forward ? "there is no next race" : "there is no previous race");
            RaceManager.SetRace(target);
            return RaceControlState();
        }

        private object StartRace(ApiRequest r)
        {
            RequireRaceControl();
            Race current = RaceManager.CurrentRace;
            if (current == null)
                throw ApiException.Conflict("no race is selected");
            if (current.Running || RaceManager.PreRaceStartDelay)
                throw ApiException.Conflict("the race is already running");
            if (current.Ended)
                throw ApiException.Conflict("the selected race has already been run; reset it first");
            if (!raceControl.StartRace())
                throw ApiException.Conflict("FPVTrackside refused to start the race");
            Log("race started " + current.RaceName);
            return RaceControlState();
        }

        private object RaceAction(Func<bool> action, string refused)
        {
            RequireRaceControl();
            if (!action())
                throw ApiException.Conflict(refused);
            return RaceControlState();
        }

        private void Log(string what)
        {
            Logger.HTTP.Log(this, "API " + what);
        }

        // ───────────────────────── mapping ─────────────────────────

        public static string RaceState(Race race)
        {
            if (race.Running) return "running";
            if (race.Ended) return "finished";
            return "pending";
        }

        private static DateTime? Time(DateTime t)
        {
            return t == default(DateTime) ? (DateTime?)null : t;
        }

        public static ChannelDto ToDto(Channel c)
        {
            if (c == null)
                return null;
            return new ChannelDto { Id = c.ID, Label = c.GetBandChannelText(), Band = c.Band.ToString(), Number = c.Number, Frequency = c.Frequency };
        }

        private EventDto ToDto(Event ev)
        {
            return new EventDto
            {
                Id = ev.ID,
                Name = ev.Name,
                EventType = ev.EventType.ToString(),
                Laps = ev.Laps,
                PbLaps = ev.PBLaps,
                RaceLengthSeconds = ev.RaceLength.TotalSeconds,
                MinLapTimeSeconds = ev.MinLapTime.TotalSeconds,
                PackLimit = ev.PackLimit,
                PrimaryTimingSystemLocation = ev.PrimaryTimingSystemLocation.ToString(),
                RulesLocked = ev.RulesLocked,
                Start = ev.Start,
                Channels = (ev.Channels ?? new Channel[0]).Select(ToDto).ToList(),
                PilotCount = ev.Pilots.Count(),
            };
        }

        private PilotDto ToDto(Pilot p)
        {
            return new PilotDto
            {
                Id = p.ID,
                Name = p.Name,
                FirstName = p.FirstName,
                LastName = p.LastName,
                DiscordId = p.DiscordID,
                Aircraft = p.Aircraft,
                CatchPhrase = p.CatchPhrase,
                PracticePilot = p.PracticePilot,
                VelocidroneUid = p.VelocidroneUID,
                Channel = ToDto(eventManager.GetChannel(p)),
            };
        }

        private RoundDto ToDto(Round round)
        {
            return new RoundDto
            {
                Id = round.ID,
                Number = round.RoundNumber,
                Name = round.Name,
                EventType = round.EventType.ToString(),
                Order = round.Order,
                Stage = round.Stage?.Name,
                RaceCount = RaceManager.GetRaces(round).Length,
            };
        }

        private T Fill<T>(T dto, Race race) where T : RaceSummaryDto
        {
            dto.Id = race.ID;
            dto.RoundId = race.Round?.ID ?? Guid.Empty;
            dto.RoundNumber = race.RoundNumber;
            dto.RaceNumber = race.RaceNumber;
            dto.Name = race.RaceName;
            dto.EventType = race.Type.ToString();
            dto.State = RaceState(race);
            dto.IsCurrent = race == RaceManager.CurrentRace;
            dto.Start = Time(race.Start);
            dto.End = Time(race.End);
            dto.TargetLaps = race.TargetLaps;
            dto.Bracket = race.Bracket.ToString();
            dto.Pilots = race.PilotChannelsSafe.Where(pc => pc.Pilot != null)
                .Select(pc => new RacePilotDto { PilotId = pc.Pilot.ID, Name = pc.Pilot.Name, Channel = ToDto(pc.Channel) })
                .ToList();
            return dto;
        }

        private RaceSummaryDto ToSummary(Race race)
        {
            return Fill(new RaceSummaryDto(), race);
        }

        private RaceDto ToDto(Race race)
        {
            RaceDto dto = Fill(new RaceDto(), race);
            dto.Laps = Laps(race, null);
            dto.Results = Results(race);
            return dto;
        }

        private static Detection[] DetectionsOf(Race race)
        {
            lock (race.Detections)
            {
                return race.Detections.ToArray();
            }
        }

        private List<LapDto> Laps(Race race, Pilot pilot)
        {
            Lap[] laps;
            lock (race.Laps)
            {
                laps = race.Laps.Where(l => l.Detection != null && (pilot == null || l.Pilot == pilot)).ToArray();
            }
            return laps.OrderBy(l => l.End).Select(l => new LapDto
            {
                Id = l.ID,
                DetectionId = l.Detection.ID,
                PilotId = l.Pilot?.ID ?? Guid.Empty,
                Number = l.Number,
                Valid = l.Detection.Valid,
                Start = l.Start,
                End = l.End,
                LengthSeconds = Math.Round(l.Length.TotalSeconds, 3),
                RaceTimeSeconds = race.Started ? Math.Round((l.End - race.Start).TotalSeconds, 3) : 0,
            }).ToList();
        }

        private DetectionDto ToDto(Race race, Detection d)
        {
            return new DetectionDto
            {
                Id = d.ID,
                PilotId = d.Pilot?.ID ?? Guid.Empty,
                Time = d.Time,
                RaceTimeMs = race.Started ? (long)Math.Round((d.Time - race.Start).TotalMilliseconds) : 0,
                IsLapEnd = d.IsLapEnd,
                TimingSystemIndex = d.TimingSystemIndex,
                TimingSystemType = d.TimingSystemType.ToString(),
                LapNumber = d.LapNumber,
                Valid = d.Valid,
                ValidityType = d.ValidityType.ToString(),
                Peak = d.Peak,
            };
        }

        private List<DetectionDto> Detections(Race race, Pilot pilot)
        {
            return DetectionsOf(race).Where(d => pilot == null || d.Pilot == pilot).OrderBy(d => d.Time).Select(d => ToDto(race, d)).ToList();
        }

        private List<ResultDto> Results(Race race)
        {
            return eventManager.ResultManager.GetOrderedResults(race).Where(x => x.Pilot != null).Select(x => new ResultDto
            {
                PilotId = x.Pilot.ID,
                Name = x.Pilot.Name,
                Position = x.Position,
                Points = x.Points,
                LapsFinished = x.LapsFinished,
                TimeSeconds = Math.Round(x.Time.TotalSeconds, 3),
                Dnf = x.DNF,
            }).ToList();
        }
    }
}
