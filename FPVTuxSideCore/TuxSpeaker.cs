using System.Diagnostics;
using Tools;

namespace FPVTuxsideCore
{
    public class TuxSpeaker : ISpeaker
    {
        private Process speechProcess;

        public void Dispose()
        {
            Stop();
        }

        public IEnumerable<string> GetVoices()
        {
            return new string[] { "Default" };
        }

        public void SelectVoice(string voice) { }

        public void SetRate(int rate) { }

        public void SetVolume(int volume) { }

        public void Speak(string text)
        {
            var psi = new ProcessStartInfo("espeak-ng");
            psi.ArgumentList.Add(text);
            speechProcess = Process.Start(psi);
            speechProcess.WaitForExit();
            speechProcess = null;
        }

        public void Stop()
        {
            Process process = speechProcess;
            if (process != null)
            {
                process.Kill();
                speechProcess = null;
            }
        }
    }
}
