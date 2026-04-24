using Composition;
using System.Diagnostics;

namespace FPVTuxsideCore
{
    public class TuxClipboard : IClipboard
    {
        public string[] GetLines()
        {
            return GetText().Split('\n');
        }

        public string GetText()
        {
            try
            {
                var psi = new ProcessStartInfo("xclip", "-selection clipboard -o")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                string text = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                return text ?? "";
            }
            catch
            {
                return "";
            }
        }

        public void SetLines(IEnumerable<string> items)
        {
            SetText(string.Join("\r\n", items));
        }

        public void SetText(string text)
        {
            try
            {
                var psi = new ProcessStartInfo("xclip", "-selection clipboard")
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc.StandardInput.Write(text);
                proc.StandardInput.Close();
                proc.WaitForExit();
            }
            catch { }
        }
    }
}
