using Composition;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace FPVMacsideCore
{
    public class Maclipboard : IClipboard
    {

        public Maclipboard()
        {
        }

        public string[] GetLines()
        {
            string[] lines = GetText().Split('\n');
            return lines;
        }

        public string GetText()
        {
            return OsxClipboard.GetText();
        }

        public void SetLines(IEnumerable<string> items)
        {
            string all = string.Join("\r\n", items.ToArray());
            SetText(all);
        }

        public void SetText(string text)
        {
            OsxClipboard.SetText(text);
        }
    }
}