using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Tools;

namespace Webb.Api
{
    // Settings groups as JSON: every property a settings dialog would show
    // (public, read/write, not [Browsable(false)] or [JsonIgnore]). Secrets go
    // out masked and a masked value coming back leaves the saved one alone.
    public static class AppSettings
    {
        public const string MaskedSecret = "********";

        private static IEnumerable<PropertyInfo> Editable(Type t)
        {
            return t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                .Where(p => p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
                .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null);
        }

        private static bool IsSecret(PropertyInfo p)
        {
            string n = p.Name.ToLowerInvariant();
            return p.PropertyType == typeof(string) && (n.Contains("password") || n.Contains("secret") || n.EndsWith("token") || n.EndsWith("apikey"));
        }

        private static string Camel(string s)
        {
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        private static bool NeedsRestart(AppSettingsGroup g, PropertyInfo p)
        {
            return g.NeedsRestart != null && g.NeedsRestart(p);
        }

        public static AppSettingsInfo Describe(AppSettingsGroup g)
        {
            return new AppSettingsInfo
            {
                Name = g.Name,
                Summary = g.Summary,
                Fields = Editable(g.Type).Select(p =>
                {
                    Type t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                    return new AppSettingField
                    {
                        Name = Camel(p.Name),
                        Label = p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? p.Name.CamelCaseToHuman(),
                        Category = p.GetCustomAttribute<CategoryAttribute>()?.Category,
                        Type = t.IsEnum ? "enum" : t.IsArray ? "array" : t.Name.ToLowerInvariant(),
                        Values = t.IsEnum ? Enum.GetNames(t).ToList() : null,
                        NeedsRestart = NeedsRestart(g, p),
                    };
                }).ToList(),
            };
        }

        public static JObject Get(AppSettingsGroup g)
        {
            object o = g.Load();
            JObject j = new JObject();
            foreach (PropertyInfo p in Editable(g.Type))
            {
                object v = p.GetValue(o);
                if (IsSecret(p))
                    j[Camel(p.Name)] = string.IsNullOrEmpty(v as string) ? "" : MaskedSecret;
                else
                    j[Camel(p.Name)] = v == null ? JValue.CreateNull() : JToken.FromObject(v, ApiJson.Serializer);
            }
            return j;
        }

        public static AppSettingsResult Set(AppSettingsGroup g, JObject values)
        {
            object o = g.Load();
            Dictionary<string, PropertyInfo> props = Editable(g.Type).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            // Check every value before changing anything.
            List<KeyValuePair<PropertyInfo, object>> sets = new List<KeyValuePair<PropertyInfo, object>>();
            foreach (JProperty jp in values?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                PropertyInfo p;
                if (!props.TryGetValue(jp.Name, out p))
                    throw ApiException.BadRequest("'" + jp.Name + "' is not a field of " + g.Name + " (see GET /settings)");
                if (IsSecret(p) && (string)jp.Value == MaskedSecret)
                    continue;
                object v;
                try
                {
                    v = jp.Value.ToObject(p.PropertyType, ApiJson.Serializer);
                }
                catch (Exception e)
                {
                    throw ApiException.BadRequest("'" + jp.Name + "': " + e.Message);
                }
                sets.Add(new KeyValuePair<PropertyInfo, object>(p, v));
            }

            List<string> changed = new List<string>();
            bool restart = false;
            foreach (var kv in sets)
            {
                PropertyInfo p = kv.Key;
                string before = JsonConvert.SerializeObject(p.GetValue(o));
                p.SetValue(o, kv.Value);
                if (before != JsonConvert.SerializeObject(p.GetValue(o)))
                {
                    changed.Add(Camel(p.Name));
                    restart |= NeedsRestart(g, p);
                }
            }

            if (changed.Count > 0)
                g.Save(o);

            return new AppSettingsResult { Settings = Get(g), Changed = changed, NeedsRestart = restart };
        }
    }
}
