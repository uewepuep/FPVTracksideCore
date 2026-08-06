using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace FPVMacsideCore
{
    public class MacSpeaker : ISpeaker
    {
        // 'say' takes an absolute words-per-minute value, so map the -10 to 10
        // scale onto a nominal 175wpm, doubling at 10 and halving at -10.
        // Note that macOS clamps slow speech - anything under about 150wpm gets
        // pulled back up - so negative rates have far less effect than positive
        // ones. That's the speech engine's doing, not this mapping.
        private const double BaseWordsPerMinute = 175;

        private string voice;

        // 0 means leave the rate alone and let the voice use its own default.
        private int wordsPerMinute;

        private Process speechProcess;

        public MacSpeaker()
        {
            voice = "Default";
        }

        public void Dispose()
        {
            Stop();
        }

        public IEnumerable<string> GetVoices()
        {
            return new string[] { voice };
        }

        public void SelectVoice(string voice)
        {
        }

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
        }

        public void Speak(string text)
        {
            ProcessStartInfo psi = new ProcessStartInfo("/usr/bin/say");

            if (wordsPerMinute > 0)
            {
                psi.ArgumentList.Add("-r");
                psi.ArgumentList.Add(wordsPerMinute.ToString(CultureInfo.InvariantCulture));
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
                process = null;
            }
        }
    }
}
