using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools
{
    public interface ISpeaker : IDisposable
    {
        IEnumerable<string> GetVoices();
        void SelectVoice(string voice);
        void SetRate(int rate);
        void Speak(string text);

        void Stop();

        void SetVolume(int volume);
    }
}
