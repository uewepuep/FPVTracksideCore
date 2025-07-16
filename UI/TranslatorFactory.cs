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
            return Load(new FileInfo("Translations.xlsx"), "Sheet1");
        }

        public static IEnumerable<Translator> Load(FileInfo excelFile, string sheetname)
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
                            translator.Add(name, replacement);
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
    }
}
