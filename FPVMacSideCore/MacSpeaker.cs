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

        // 1 is the voice's normal level, and emitting nothing at all produces
        // byte-identical audio, so only attenuate when asked to.
        private double volumeScale = 1;

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

        // 'say' has no volume flag, so attenuation goes through an embedded
        // [[volm]] command instead. Its scale is logarithmic - amplitude halves
        // every 0.25 - which puts a straight percentage close to linear in
        // perceived loudness.
        public void SetVolume(int volume)
        {
            volumeScale = Math.Clamp(volume, 0, 100) / 100.0;
        }

        public void Speak(string text)
        {
            ProcessStartInfo psi = new ProcessStartInfo("/usr/bin/say");

            if (wordsPerMinute > 0)
            {
                psi.ArgumentList.Add("-r");
                psi.ArgumentList.Add(wordsPerMinute.ToString(CultureInfo.InvariantCulture));
            }

            if (volumeScale < 1)
            {
                text = string.Format(CultureInfo.InvariantCulture, "[[volm {0:0.###}]] {1}", volumeScale, text);
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
