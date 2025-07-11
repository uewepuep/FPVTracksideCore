using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition
{
    public class Translator
    {
        public static Translator Instance { get; private set; }

        private Dictionary<string, string> translations;

        public Translator()
        {
            translations = new Dictionary<string, string>();
            Instance = this;
        }

        public void Clear()
        {
            lock (translations)
            {
                translations.Clear();
            }
        }
        public void Add(string englishName, string translation)
        {
            lock (translations)
            {
                if (translations.ContainsKey(englishName))
                {
                    translations[englishName] = translation;
                }
                else
                {
                    translations.Add(englishName, translation);
                }
            }
        }

        public string GetTranslation(string type, string englishName)
        {
            lock (translations)
            {
                if (translations.TryGetValue(type, out string value))
                {
                    return value;
                }
            }

            return englishName;
        }

        public string GetTranslation<T>(string englishName) where T : Node
        {
            string type = typeof(T).Name.Replace("Node", "");
            return Get(type, englishName);
        }

        public static string Get(string type, string englishName)
        {
            if (Instance != null)
            {
                return Instance.GetTranslation(type, englishName);
            }

            return englishName;
        }

        public static string Get<T>(string englishName) where T : Node
        {
            if (Instance != null)
            {
                return Instance.GetTranslation<T>(englishName);
            }

            return englishName;
        }
    }
}
