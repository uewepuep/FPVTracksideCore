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
            return RunAndRead("wl-paste", "--no-newline")
                ?? RunAndRead("xclip", "-selection clipboard -o")
                ?? "";
        }

        private static string RunAndRead(string cmd, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(cmd, args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                string text = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                return proc.ExitCode == 0 ? text : null;
            }
            catch
            {
                return null;
            }
        }

        public void SetLines(IEnumerable<string> items)
        {
            SetText(string.Join("\r\n", items));
        }

        public void SetText(string text)
        {
            if (!RunAndWrite("wl-copy", null, text))
                RunAndWrite("xclip", "-selection clipboard", text);
        }

        private static bool RunAndWrite(string cmd, string args, string text)
        {
            try
            {
                var psi = new ProcessStartInfo(cmd, args ?? "")
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc.StandardInput.Write(text);
                proc.StandardInput.Close();
                proc.WaitForExit();
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
