using Composition;
using Microsoft.VisualBasic;
using Spreadsheets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI
{
    public class TranslatorFactory
    {
        public static IEnumerable<Translator> Load()
        {
            string filename = "Translations.xlsx";
            FileInfo file = null;

            // Try multiple locations
            string[] searchPaths = new string[]
            {
                // Current directory
                filename,
                // Base directory (where executable is)
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename),
                // Working directory (user data on Mac: ~/Library/Application Support/FPVTrackside)
                Tools.IOTools.WorkingDirectory != null
                    ? Path.Combine(Tools.IOTools.WorkingDirectory.FullName, filename)
                    : null,
                // Parent of base directory (for .app bundles)
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", filename),
                // Resources folder in .app bundle
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Resources", filename),
            };

            foreach (string path in searchPaths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                file = new FileInfo(path);
                if (file.Exists)
                    break;
            }

            if (file == null || !file.Exists)
            {
                // Return empty if file not found
                return Enumerable.Empty<Translator>();
            }

            return Load(file);
        }

        public static IEnumerable<Translator> Load(FileInfo excelFile, string sheetname = "Sheet1")
        {
            List<string> translationItemNames = null;
            List<string> languages = null;

            using (OpenSheet openSheet = new OpenSheet())
            {
                openSheet.Open(excelFile, sheetname);
                translationItemNames = ReadColumn(openSheet, 1).Skip(1).ToList();
                languages = ReadRow(openSheet, 1).Skip(1).ToList();

                int languageIndex = 2;
                foreach (string language in languages)
                {
                    Translator translator = new Translator(language);
                    for (int i = 0; i < translationItemNames.Count; i++)
                    {
                        int row = i + 2;

                        string replacement = openSheet.GetText(row, languageIndex);

                        if (!string.IsNullOrEmpty(replacement))
                        {
                            string name = translationItemNames[i];
                            translator.Set(name, replacement);
                        }
                    }

                    yield return translator;
                    languageIndex++;
                }
            }
        }

        public static IEnumerable<string> ReadColumn(OpenSheet openSheet, int index)
        {
            int i = 1;
            string t;
            do
            {
                t = openSheet.GetText(i, index);

                if (!string.IsNullOrEmpty(t))
                {
                    yield return t;
                }
                i++;
            }
            while (!string.IsNullOrEmpty(t));
        }

        public static IEnumerable<string> ReadRow(OpenSheet openSheet, int index)
        {
            int i = 1;
            string t;
            do
            {
                t = openSheet.GetText(index, i);

                if (!string.IsNullOrEmpty(t))
                {
                    yield return t;
                }
                i++;
            }
            while (!string.IsNullOrEmpty(t));
        }

        public static bool Write(Translator[] translators, FileInfo excelFile, string sheetname = "Sheet1")
        {
            Translator english = translators.FirstOrDefault(t => t.Language.ToLower().ToString() == "english");

            using (OpenSheet openSheet = new OpenSheet())
            {
                openSheet.Open(excelFile, sheetname);
                string[] allNames = english.ItemNames.ToArray();

                openSheet.SetValue(1, 1, "Name");
                int c = 2;
                int r = 1;
                foreach (Translator translator in translators)
                {
                    openSheet.SetValue(r, c, translator.Language);
                    c++;
                }

                foreach (string name in allNames)
                {
                    r++;
                    openSheet.SetValue(r, 1, name);
                    c = 2;

                    foreach (Translator translator in translators)
                    {
                        string value = translator.GetTranslation(name, "");

                        openSheet.SetValue(r, c, value);
                        c++;
                    }
                }
                return openSheet.Save();
            }
        }
    }
}
