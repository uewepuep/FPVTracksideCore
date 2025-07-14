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
        public IEnumerable<Translator> Load(FileInfo excelFile, string sheetname)
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
                    List<string> replacements = ReadColumn(openSheet, languageIndex).Skip(1).ToList();
                    languageIndex++;

                    if (!replacements.Any())
                        continue;

                    Translator translator = new Translator(language);

                    for (int i = 0; i < replacements.Count && i < translationItemNames.Count; i++)
                    {
                        translator.Add(translationItemNames[i], replacements[i]);
                    }

                    yield return translator;
                }
            }
        }

        public IEnumerable<string> ReadColumn(OpenSheet openSheet, int index)
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

        public IEnumerable<string> ReadRow(OpenSheet openSheet, int index)
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
