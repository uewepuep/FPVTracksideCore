using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Synthesis;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace WindowsPlatform
{
    public class WindowsSpeaker : ISpeaker
    {
        private SpeechSynthesizer speechSynthesizer;

        public WindowsSpeaker()
        {
            speechSynthesizer = new SpeechSynthesizer();
        }

        public void Dispose()
        {
            if (speechSynthesizer != null)
            {
                speechSynthesizer.Dispose();
                speechSynthesizer = null;
            }
        }

        public IEnumerable<string> GetVoices()
        {
            return speechSynthesizer.GetInstalledVoices().Select(r => r.VoiceInfo.Name);
        }

        public void SelectVoice(string voice)
        {
            var chosen = speechSynthesizer.GetInstalledVoices().FirstOrDefault(v => v.VoiceInfo.Name == voice);
            if (chosen != null)
            {
                speechSynthesizer.SelectVoice(chosen.VoiceInfo.Name);
            }
        }

        public void SetRate(int rate)
        {
            speechSynthesizer.Rate = rate;
        }

        public void SetVolume(int volume)
        {
            speechSynthesizer.Volume = volume;
        }

        public void Speak(string text)
        {
            speechSynthesizer.Speak(text);
        }

        public void Stop()
        {
            speechSynthesizer.SpeakAsyncCancelAll();
        }
    }
}
