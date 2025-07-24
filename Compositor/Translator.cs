﻿using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace Composition
{
    public class Translator
    {
        public static Translator Instance { get; private set; }

        private Dictionary<string, string> translations;

        public string Language { get; private set; }

        public IEnumerable<string> ItemNames
        {
            get
            {
                return translations.Keys;
            }
        }

        public Translator(string language)
        {
            Language = language;
            translations = new Dictionary<string, string>();
        }

        public void MakePrimary()
        {
            Instance = this;
        }

        public static void ClearPrimary()
        {
            Instance = null;
        }

        public void Clear()
        {
            lock (translations)
            {
                translations.Clear();
            }
        }
        public void Set(string itemName, string translation)
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


        public string GetTranslation(string fullType, string englishName)
        {
            lock (translations)
            {
                // Ignore spaces.
                fullType = fullType.Replace(" ", "");
                fullType = fullType.Replace("-", "");

                if (translations.TryGetValue(fullType, out string value))
                {
                    return value;
                }
            }

            return englishName;
        }

        public static string Get(string fullType, string englishName)
        {
            if (Instance != null)
            {
                return Instance.GetTranslation(fullType, englishName);
            }

            return englishName;
        }

        public static string GetPropertyName<T>(string name, string defaultName)
        {
            if (Instance != null)
            {
                string type = typeof(T).Name + "." + name;

                string output = Instance.GetTranslation(type, defaultName);
                if (output == defaultName)
                {
                    return defaultName;
                }
                return output;
            }

            return defaultName;
        }

        public bool HasTranslation(string name)
        {
            lock (translations)
            {
                return translations.ContainsKey(name);
            }
        }

        public override string ToString()
        {
            return Language;
        }
    }
}
