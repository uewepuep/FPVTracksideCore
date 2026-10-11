using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Webb.Api
{
    // Builds the OpenAPI 3 document from the live route table and the [Doc]
    // attributes on the models, and serves a self-contained documentation page
    // that renders it (no CDN — a venue laptop is often offline).
    public static class ApiDocs
    {
        public static IEnumerable<string> EventTypeNames()
        {
            return Enum.GetValues(typeof(EventTypes)).Cast<EventTypes>().Where(t => t != EventTypes.Unknown).Distinct().Select(t => t.ToString());
        }

        public static string OpenApi(IEnumerable<ApiRoute> routes, string baseUrl)
        {
            Dictionary<string, JObject> schemas = new Dictionary<string, JObject>();
            JObject paths = new JObject();

            foreach (IGrouping<string, ApiRoute> group in routes.GroupBy(r => "/" + RestApi.Prefix + (r.Template.Length > 0 ? "/" + r.Template : "")))
            {
                JObject item = new JObject();
                foreach (ApiRoute route in group)
                {
                    JObject op = new JObject
                    {
                        ["tags"] = new JArray(route.Tag),
                        ["summary"] = route.Summary,
                        ["operationId"] = OperationId(route),
                    };

                    string description = route.Description ?? "";
                    if (route.Writes)
                        description = (description + "\n\nChanges data: accepted from the FPVTrackside machine itself, or with the API token.").Trim();
                    if (description.Length > 0)
                        op["description"] = description;

                    JArray parameters = new JArray();
                    foreach (string p in route.PathParams)
                    {
                        parameters.Add(new JObject { ["name"] = p, ["in"] = "path", ["required"] = true, ["schema"] = new JObject { ["type"] = "string" }, ["description"] = ParamDescription(p) });
                    }
                    foreach (string q in route.QueryParams)
                    {
                        parameters.Add(new JObject { ["name"] = q, ["in"] = "query", ["required"] = false, ["schema"] = new JObject { ["type"] = "string" }, ["description"] = QueryDescription(q) });
                    }
                    if (parameters.Count > 0)
                        op["parameters"] = parameters;

                    if (route.RequestType != null)
                    {
                        JObject schema = SchemaFor(route.RequestType, schemas);
                        if (route.RequestIsArray)
                            schema = new JObject { ["type"] = "array", ["items"] = schema };
                        JObject media = new JObject { ["schema"] = schema };
                        if (route.Example != null)
                            media["example"] = JToken.Parse(route.Example);
                        op["requestBody"] = new JObject
                        {
                            ["required"] = true,
                            ["content"] = new JObject { ["application/json"] = media },
                        };
                    }

                    JObject responses = new JObject();
                    JObject ok = new JObject { ["description"] = route.SuccessStatus == 201 ? "Created" : "OK" };
                    if (route.ResponseType != null)
                    {
                        JObject schema = SchemaFor(route.ResponseType, schemas);
                        if (route.ResponseIsArray)
                            schema = new JObject { ["type"] = "array", ["items"] = schema };
                        ok["content"] = new JObject { ["application/json"] = new JObject { ["schema"] = schema } };
                    }
                    responses[route.SuccessStatus.ToString()] = ok;
                    responses["default"] = new JObject
                    {
                        ["description"] = "Error",
                        ["content"] = new JObject { ["application/json"] = new JObject { ["schema"] = SchemaFor(typeof(ApiErrorDto), schemas) } },
                    };
                    op["responses"] = responses;

                    if (route.Writes)
                        op["security"] = new JArray(new JObject(), new JObject { ["bearer"] = new JArray() });

                    item[route.Method.ToLowerInvariant()] = op;
                }
                paths[group.Key] = item;
            }

            JObject components = new JObject
            {
                ["schemas"] = new JObject(schemas.OrderBy(k => k.Key).Select(k => new JProperty(k.Key, k.Value))),
                ["securitySchemes"] = new JObject
                {
                    ["bearer"] = new JObject
                    {
                        ["type"] = "http",
                        ["scheme"] = "bearer",
                        ["description"] = "The value of the " + ApiAuth.TokenVariable + " environment variable on the FPVTrackside machine. Only needed for changes made from another machine.",
                    },
                },
            };

            JObject doc = new JObject
            {
                ["openapi"] = "3.0.3",
                ["info"] = new JObject
                {
                    ["title"] = "FPVTrackside API",
                    ["version"] = RestApi.Version + ".0",
                    ["description"] = "Read and run the event open in FPVTrackside: pilots, rounds, races, laps, timing crossings, results and race control.\n\n"
                        + "Times are ISO 8601 in the FPVTrackside machine's local time with its UTC offset. IDs are GUIDs. "
                        + "Pilots may be referred to by ID or exact name, channels by ID, label (\"R1\") or frequency (\"5658\").\n\n"
                        + "Reads are open to anyone who can reach the web server. Changes are accepted from the FPVTrackside machine itself, or from another machine with `Authorization: Bearer <token>` where the token is the " + ApiAuth.TokenVariable + " environment variable.",
                },
                ["servers"] = new JArray(new JObject { ["url"] = baseUrl }),
                ["tags"] = new JArray(routes.Select(r => r.Tag).Distinct().Select(t => new JObject { ["name"] = t })),
                ["paths"] = paths,
                ["components"] = components,
            };
            return doc.ToString(Formatting.Indented);
        }

        private static string OperationId(ApiRoute route)
        {
            string name = string.Join("", route.Segments.Select(s => s.StartsWith("{") ? "By" + Cap(s.Trim('{', '}')) : Cap(s.Replace("-", " ")).Replace(" ", "")));
            return route.Method.ToLowerInvariant() + (name.Length > 0 ? name : "Info");
        }

        private static string Cap(string s)
        {
            return string.Join(" ", s.Split(' ').Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        }

        private static string ParamDescription(string p)
        {
            switch (p)
            {
                case "pilotId": return "Pilot ID, or the pilot's exact name.";
                case "raceId": return "Race ID.";
                case "roundId": return "Round ID.";
                case "detectionId": return "Detection (crossing) ID.";
                case "stageId": return "Stage ID.";
                case "stageRaceId": return "The race's ID within the stage, e.g. W1-2 or A:L2-1 (URL-encode it).";
                default: return p;
            }
        }

        private static string QueryDescription(string q)
        {
            switch (q)
            {
                case "state": return "pending, running or finished.";
                case "round": return "Round ID.";
                case "pilot": return "Pilot ID or exact name.";
                default: return q;
            }
        }

        // JSON schema for a model, registered under components/schemas and
        // returned as a $ref. Field names are camelCase, matching the wire.
        public static JObject SchemaFor(Type type, Dictionary<string, JObject> schemas)
        {
            JObject primitive = Primitive(type);
            if (primitive != null)
                return primitive;

            string name = type.Name;
            if (!schemas.ContainsKey(name))
            {
                JObject schema = new JObject { ["type"] = "object" };
                schemas[name] = schema; // before recursing, so self-references terminate

                DocAttribute typeDoc = type.GetCustomAttribute<DocAttribute>();
                if (typeDoc != null)
                    schema["description"] = typeDoc.Text;

                JObject props = new JObject();
                JArray required = new JArray();
                foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(Declared))
                {
                    string field = char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1);
                    JObject ps = PropertySchema(p.PropertyType, schemas);
                    DocAttribute doc = p.GetCustomAttribute<DocAttribute>();
                    if (doc != null)
                    {
                        if (ps["$ref"] != null)
                            ps = new JObject { ["allOf"] = new JArray(ps) };
                        ps["description"] = doc.Text;
                        if (doc.Example != null)
                            ps["example"] = doc.Example;
                        if (doc.Values != null)
                            ps["enum"] = new JArray(doc.Values);
                        if (doc.Required)
                            required.Add(field);
                    }
                    props[field] = ps;
                }
                schema["properties"] = props;
                if (required.Count > 0)
                    schema["required"] = required;
            }
            return new JObject { ["$ref"] = "#/components/schemas/" + name };
        }

        // Base-class fields first, then the type's own, each in declaration order.
        private static int Declared(PropertyInfo p)
        {
            int depth = 0;
            for (Type t = p.DeclaringType.BaseType; t != null && t != typeof(object); t = t.BaseType)
                depth++;
            return -depth * 100000 + p.MetadataToken % 100000;
        }

        private static JObject PropertySchema(Type t, Dictionary<string, JObject> schemas)
        {
            Type inner = Nullable.GetUnderlyingType(t);
            if (inner != null)
            {
                JObject s = PropertySchema(inner, schemas);
                s["nullable"] = true;
                return s;
            }
            if (t != typeof(string) && t.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(t))
            {
                return new JObject { ["type"] = "array", ["items"] = PropertySchema(t.GetGenericArguments()[0], schemas) };
            }
            return SchemaFor(t, schemas);
        }

        private static JObject Primitive(Type t)
        {
            if (t == typeof(string)) return new JObject { ["type"] = "string" };
            if (t == typeof(bool)) return new JObject { ["type"] = "boolean" };
            if (t == typeof(int)) return new JObject { ["type"] = "integer", ["format"] = "int32" };
            if (t == typeof(long)) return new JObject { ["type"] = "integer", ["format"] = "int64" };
            if (t == typeof(double) || t == typeof(float)) return new JObject { ["type"] = "number" };
            if (t == typeof(Guid)) return new JObject { ["type"] = "string", ["format"] = "uuid" };
            if (t == typeof(DateTime)) return new JObject { ["type"] = "string", ["format"] = "date-time" };
            return null;
        }

        // The console's files, embedded in this assembly (Api/Console/*).
        public static byte[] ConsoleFile(string name, out string contentType)
        {
            contentType = null;
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0)
                return null;

            using (System.IO.Stream s = typeof(ApiDocs).Assembly.GetManifestResourceStream("Webb.Api.Console." + name))
            {
                if (s == null)
                    return null;
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                {
                    s.CopyTo(ms);
                    switch (System.IO.Path.GetExtension(name).ToLowerInvariant())
                    {
                        case ".html": contentType = "text/html; charset=utf-8"; break;
                        case ".css": contentType = "text/css; charset=utf-8"; break;
                        case ".js": contentType = "application/javascript; charset=utf-8"; break;
                        default: contentType = "application/octet-stream"; break;
                    }
                    return ms.ToArray();
                }
            }
        }
    }
}
