using Composition;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsPlatform
{
    public class Clipboard : IClipboard
    {
        public string[] GetLines()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                string[] result;
                WindowsPlatformTools.ReturnAsSTAThread(GetLines, out result);

                return result;
            }

            try
            {
                if (System.Windows.Forms.Clipboard.ContainsText(System.Windows.Forms.TextDataFormat.Text))
                {
                    string clipboardText = System.Windows.Forms.Clipboard.GetText(System.Windows.Forms.TextDataFormat.UnicodeText);

                    string[] lines = clipboardText.Split('\n');

                    for (int i = 0; i < lines.Length; i++)
                    {
                        lines[i] = lines[i].Replace("\r", "");
                    }

                    return lines;
                }
                return new string[0];
            }
            catch (Exception)
            {
                return new string[0];
            }
        }

        public string GetText()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                string result;
                WindowsPlatformTools.ReturnAsSTAThread(GetText, out result);
                return result;
            }

            try
            {
                if (System.Windows.Forms.Clipboard.ContainsText(System.Windows.Forms.TextDataFormat.Text))
                {
                    string clipboardText = System.Windows.Forms.Clipboard.GetText(System.Windows.Forms.TextDataFormat.UnicodeText);
                    return clipboardText;
                }
                return "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        public void SetLines(IEnumerable<string> items)
        {
            string all = string.Join("\r\n", items.ToArray());
            SetText(all);
        }

        public void SetText(string text)
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                WindowsPlatformTools.RunAsSTAThread(() => { SetText(text); });
                return;
            }

            try
            {
                System.Windows.Forms.Clipboard.SetText(text);
            }
            catch (Exception)
            {
            }
        }
    }
}
