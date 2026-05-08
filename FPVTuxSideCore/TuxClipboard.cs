using Composition;
using System.Diagnostics;

namespace FPVTuxsideCore
{
    public class TuxClipboard : IClipboard
    {
        private readonly bool useX11;

        public TuxClipboard()
        {
            useX11 = !TuxPlatformTools.IsCommandAvailable("wl-paste");
        }

        public string[] GetLines()
        {
            return GetText().Split('\n');
        }

        public string GetText()
        {
            if (useX11)
                return RunAndRead("xclip", "-selection clipboard -o") ?? "";
            return RunAndRead("wl-paste", "--no-newline") ?? "";
        }

        public void SetLines(IEnumerable<string> items)
        {
            SetText(string.Join("\r\n", items));
        }

        public void SetText(string text)
        {
            if (useX11)
                RunAndWrite("xclip", "-selection clipboard", text);
            else
                RunAndWrite("wl-copy", null, text);
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

        private static void RunAndWrite(string cmd, string args, string text)
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
            }
            catch { }
        }
    }
}
