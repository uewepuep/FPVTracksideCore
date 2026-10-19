using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SeederEngine
{
    /// <summary>
    /// A string-keyed map that serializes its keys in byte (ordinal) order, the way
    /// Go's encoding/json writes a map — so a state blob written here is byte-
    /// comparable with one written by the AUFPV platform.
    /// </summary>
    public class GoMap<TValue> : SortedDictionary<string, TValue>
    {
        public GoMap()
            : base(StringComparer.Ordinal)
        {
        }

        public GoMap(IDictionary<string, TValue> src)
            : base(StringComparer.Ordinal)
        {
            if (src != null)
            {
                foreach (KeyValuePair<string, TValue> kv in src)
                {
                    this[kv.Key] = kv.Value;
                }
            }
        }

        /// <summary>Go's m[k] read: the zero value when absent.</summary>
        public TValue Get(string key)
        {
            TValue v;
            if (key != null && TryGetValue(key, out v))
            {
                return v;
            }
            return default(TValue);
        }
    }

    /// <summary>Serializer settings shared by every state blob.</summary>
    public static class BracketJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
            Formatting = Formatting.None,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        public static string Serialize(object o)
        {
            return JsonConvert.SerializeObject(o, Settings);
        }

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }
    }
}
