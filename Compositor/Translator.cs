using Composition.Nodes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace Composition
{
    public class Translator
    {
        public static Translator Instance { get; private set; }

        private static string cachedListSeparator = null;

        // Translation sweep: when recording, every key asked for that the translations file
        // doesn't have is appended to missingFile, so it can be filled in later.
        private static HashSet<string> knownKeys;
        private static HashSet<string> recordedKeys;
        private static string missingFile;
        private static HashSet<string> formattedTexts;

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
            cachedListSeparator = null;
        }

        public static void ClearPrimary()
        {
            Instance = null;
            cachedListSeparator = null;
        }

        public void Clear()
        {
            lock (translations)
            {
                translations.Clear();
            }
            cachedListSeparator = null;
        }

        public void Set(string itemName, string translation)
        {
            itemName = NormaliseKey(itemName);
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
            if (itemName == "ListSeparator")
                cachedListSeparator = null;
        }


        public void Merge(Translator other)
        {
            KeyValuePair<string, string>[] items;
            lock (other.translations)
            {
                items = other.translations.ToArray();
            }

            foreach (var item in items)
            {
                Set(item.Key, item.Value);
            }
        }

        public string GetTranslation(string fullType, string englishName)
        {
            lock (translations)
            {
                fullType = NormaliseKey(fullType);

                if (translations.TryGetValue(fullType, out string value))
                {
                    return value;
                }
            }

            return englishName;
        }

        // A key is only its letters, digits and dots, so button text like "Channel\nSettings" finds Button.ChannelSettings
        // and "Save & Exit" finds Menu.SaveExit
        public static string NormaliseKey(string key)
        {
            key = key.Replace("\\n", "");

            StringBuilder sb = new StringBuilder(key.Length);
            foreach (char c in key)
            {
                if (char.IsLetterOrDigit(c) || c == '.')
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public static void RecordMissing(IEnumerable<string> keys, string filename)
        {
            knownKeys = new HashSet<string>(keys.Select(NormaliseKey));
            recordedKeys = new HashSet<string>();
            formattedTexts = new HashSet<string>();
            missingFile = filename;
        }

        private static void CheckMissing(string fullType, string englishName)
        {
            if (knownKeys == null || fullType == null)
                return;

            string key = NormaliseKey(fullType);
            lock (recordedKeys)
            {
                if (knownKeys.Contains(key) || formattedTexts.Contains(englishName) || !recordedKeys.Add(key))
                    return;

                try
                {
                    File.AppendAllText(missingFile, key + "\t" + (englishName ?? "").Replace("\n", "\\n") + Environment.NewLine);
                }
                catch
                {
                }
            }
        }

        public static string Get(string fullType, string englishName)
        {
            CheckMissing(fullType, englishName);

            if (Instance != null)
            {
                return Instance.GetTranslation(fullType, englishName);
            }

            return englishName;
        }

        // Text with a runtime part, e.g. Format("Menu.AddPilotName", "Add {0}", pilot.Name). The translation keeps the {0}.
        public static string Format(string fullType, string englishFormat, params object[] args)
        {
            string output;
            try
            {
                output = string.Format(Get(fullType, englishFormat), args);
            }
            catch (FormatException)
            {
                // A translation with broken placeholders shouldn't take the menu down
                output = string.Format(englishFormat, args);
            }

            // The result is usually passed on to a menu or button, which looks it up again - don't record that as missing
            if (knownKeys != null)
            {
                lock (recordedKeys)
                {
                    formattedTexts.Add(output);
                }
            }
            return output;
        }

        public static string ListSeparator
        {
            get
            {
                if (cachedListSeparator == null)
                    cachedListSeparator = Get("ListSeparator", ", ");
                return cachedListSeparator;
            }
        }

        public static string GetPropertyName<T>(string name, string defaultName)
        {
            if (name != null)
            {
                CheckMissing(typeof(T).Name + "." + name, defaultName);
            }

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
                return translations.ContainsKey(NormaliseKey(name));
            }
        }

        public override string ToString()
        {
            return Language;
        }
    }
}
