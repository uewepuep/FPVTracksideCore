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

        public string Language { get; private set; }

        public Translator(string language)
        {
            Language = language;
            translations = new Dictionary<string, string>();
        }

        public void MakePrimary()
        {
            Instance = this;
        }

        public void Clear()
        {
            lock (translations)
            {
                translations.Clear();
            }
        }
        public void Add(string itemName, string translation)
        {
            lock (translations)
            {
                if (translations.ContainsKey(itemName))
                {
                    translations[itemName] = translation;
                }
                else
                {
                    translations.Add(itemName, translation);
                }
            }
        }

        public string GetTranslation(string type, string englishName)
        {
            lock (translations)
            {
                string fullType = type + "." + englishName;
                if (translations.TryGetValue(fullType, out string value))
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

        public static string GetPropertyName<T>(string englishName, string defaultName)
        {
            if (Instance != null)
            {
                string type = "Editor." + typeof(T).Name;

                string output = Instance.GetTranslation(type, englishName);
                if (output == englishName)
                {
                    return defaultName;
                }
                return output;
            }

            return englishName;
        }
    }
}
