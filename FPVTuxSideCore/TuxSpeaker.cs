using System.Diagnostics;
using System.Globalization;
using Tools;

namespace FPVTuxsideCore
{
    public class TuxSpeaker : ISpeaker
    {
        // espeak-ng takes an absolute words-per-minute value, so map the -10 to
        // 10 scale onto a nominal 175wpm, doubling at 10 and halving at -10.
        private const double BaseWordsPerMinute = 175;

        // 0 means leave the rate alone and let espeak-ng use its own default.
        private int wordsPerMinute;

        // espeak-ng's default amplitude is 100, so a full-volume request needs
        // no flag at all.
        private int amplitude = 100;

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

        public void SetRate(int rate)
        {
            if (rate == 0)
            {
                wordsPerMinute = 0;
                return;
            }

            wordsPerMinute = (int)Math.Round(BaseWordsPerMinute * Math.Pow(2, rate / 10.0));
        }

        public void SetVolume(int volume)
        {
            amplitude = Math.Clamp(volume, 0, 100);
        }

        public void Speak(string text)
        {
            var psi = new ProcessStartInfo("espeak-ng");

            if (wordsPerMinute > 0)
            {
                psi.ArgumentList.Add("-s");
                psi.ArgumentList.Add(wordsPerMinute.ToString(CultureInfo.InvariantCulture));
            }

            if (amplitude < 100)
            {
                psi.ArgumentList.Add("-a");
                psi.ArgumentList.Add(amplitude.ToString(CultureInfo.InvariantCulture));
            }

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
