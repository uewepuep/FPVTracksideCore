using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace FPVMacsideCore
{
    public class MacSpeaker : ISpeaker
    {
        private string voice;

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
        }

        public void Speak(string text)
        {
            string cmdArgs = text;
            speechProcess = Process.Start("/usr/bin/say", cmdArgs);
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
