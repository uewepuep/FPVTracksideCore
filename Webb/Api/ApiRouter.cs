using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;

namespace Webb.Api
{
    // An error the API reports to the caller as {"ok":false,"error":"...","code":"..."}
    // with the given HTTP status. Anything else thrown by a handler is a 500.
    public class ApiException : Exception
    {
        public int Status { get; private set; }
        public string Code { get; private set; }

        public ApiException(int status, string code, string message)
            : base(message)
        {
            Status = status;
            Code = code;
        }

        public static ApiException NotFound(string what) { return new ApiException(404, "not_found", what + " not found"); }
        public static ApiException BadRequest(string message) { return new ApiException(400, "bad_request", message); }
        public static ApiException Conflict(string message) { return new ApiException(409, "conflict", message); }
    }

    public class ApiRequest
    {
        public HttpListenerContext Context { get; set; }
        public string Method { get; set; }
        public Dictionary<string, string> Params { get; set; }
        public NameValueCollection Query { get; set; }
        public string Body { get; set; }

        // Response status for a successful call (200 unless the handler sets 201 etc).
        public int Status { get; set; } = 200;

        public string Param(string name)
        {
            string v;
            return Params != null && Params.TryGetValue(name, out v) ? v : null;
        }

        public Guid GuidParam(string name)
        {
            Guid g;
            if (!Guid.TryParse(Param(name), out g))
                throw ApiException.BadRequest("'" + name + "' must be a GUID");
            return g;
        }

        // The JSON body as T. An empty body is an empty object, so handlers whose
        // fields are all optional accept a bare POST.
        public T BodyAs<T>() where T : new()
        {
            if (string.IsNullOrWhiteSpace(Body))
                return new T();
            try
            {
                JToken token = JToken.Parse(Body);
                if (token.Type != JTokenType.Object)
                    throw ApiException.BadRequest("the request body must be a JSON object");
                return token.ToObject<T>(ApiJson.Serializer);
            }
            catch (ApiException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw ApiException.BadRequest("invalid JSON body: " + e.Message);
            }
        }

        public T BodyAsArray<T>()
        {
            try
            {
                JToken token = JToken.Parse(string.IsNullOrWhiteSpace(Body) ? "[]" : Body);
                if (token.Type != JTokenType.Array)
                    throw ApiException.BadRequest("the request body must be a JSON array");
                return token.ToObject<T>(ApiJson.Serializer);
            }
            catch (ApiException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw ApiException.BadRequest("invalid JSON body: " + e.Message);
            }
        }
    }

    // One endpoint. Everything the documentation says about it lives here, next
    // to the handler, so the OpenAPI document is generated from the same table
    // the router dispatches on and cannot describe an endpoint that isn't there.
    public class ApiRoute
    {
        public string Method { get; set; }
        public string Template { get; set; }
        public string Tag { get; set; }
        public string Summary { get; set; }
        public string Description { get; set; }
        public Type RequestType { get; set; }
        public bool RequestIsArray { get; set; }
        public Type ResponseType { get; set; }
        public bool ResponseIsArray { get; set; }
        public int SuccessStatus { get; set; } = 200;
        public string[] QueryParams { get; set; } = new string[0];

        // A ready-to-send request body for the docs and the console. Values
        // written {{like.this}} are filled from the live event by the console
        // (pilot.id, pilot.name, race.id, round.id, split.id, lapLine.id).
        public string Example { get; set; }

        // Changes something (needs the write permission, runs on the UI thread).
        public bool Writes { get; set; }

        public Func<ApiRequest, object> Handler { get; set; }

        private string[] segments;
        public string[] Segments
        {
            get
            {
                if (segments == null)
                    segments = Template.Split('/').Where(s => s.Length > 0).ToArray();
                return segments;
            }
        }

        public IEnumerable<string> PathParams
        {
            get { return Segments.Where(s => s.StartsWith("{")).Select(s => s.Trim('{', '}')); }
        }

        // Matches a path (already split into segments) against this route's
        // template. {name} segments capture; everything else is compared
        // case-insensitively.
        public bool TryMatch(string[] path, out Dictionary<string, string> captured)
        {
            captured = null;
            if (path.Length != Segments.Length)
                return false;

            Dictionary<string, string> values = new Dictionary<string, string>();
            for (int i = 0; i < path.Length; i++)
            {
                string t = Segments[i];
                if (t.StartsWith("{") && t.EndsWith("}"))
                {
                    if (path[i].Length == 0)
                        return false;
                    values[t.Substring(1, t.Length - 2)] = path[i];
                }
                else if (!string.Equals(t, path[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            captured = values;
            return true;
        }
    }

    public class ApiRouter
    {
        private readonly List<ApiRoute> routes = new List<ApiRoute>();

        public IReadOnlyList<ApiRoute> Routes { get { return routes; } }

        public ApiRoute Add(ApiRoute route)
        {
            routes.Add(route);
            return route;
        }

        public enum MatchResult { Matched, NotFound, MethodNotAllowed }

        // Literal segments win over {params} so /races/current never matches
        // /races/{raceId}, whatever order the routes were registered in.
        public MatchResult Match(string method, string[] path, out ApiRoute route, out Dictionary<string, string> captured)
        {
            route = null;
            captured = null;
            bool pathExists = false;

            foreach (ApiRoute r in routes.OrderByDescending(Specificity))
            {
                Dictionary<string, string> values;
                if (!r.TryMatch(path, out values))
                    continue;

                pathExists = true;
                if (string.Equals(r.Method, method, StringComparison.OrdinalIgnoreCase))
                {
                    route = r;
                    captured = values;
                    return MatchResult.Matched;
                }
            }
            return pathExists ? MatchResult.MethodNotAllowed : MatchResult.NotFound;
        }

        public IEnumerable<string> AllowedMethods(string[] path)
        {
            Dictionary<string, string> ignored;
            return routes.Where(r => r.TryMatch(path, out ignored)).Select(r => r.Method).Distinct();
        }

        private static int Specificity(ApiRoute r)
        {
            int score = 0;
            foreach (string s in r.Segments)
            {
                score = score * 2 + (s.StartsWith("{") ? 0 : 1);
            }
            return score;
        }
    }
}
