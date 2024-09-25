#if !MAC

using Composition;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace Sound
{
    public class SpeechManager : IDisposable
    {

        private WorkQueue ttsQueue;

        private ISpeaker speaker;

        private bool mute;
        public bool Muted
        {
            get
            {
                return mute;
            }
            set
            {
                mute = value;
                if (mute)
                {
                    StopSpeech();
                }
            }
        }

        private PlatformTools platformTools;

        public string Voice { get; private set; }

        public int Volume { get; set; }

        public SpeechManager(PlatformTools platformTools, string voice, int volume)
        {
            Voice = voice;
            this.platformTools = platformTools;
            speaker = platformTools.CreateSpeaker(voice);

            if (speaker != null)
            {
                ttsQueue = new WorkQueue("Speecher TTS");
                Muted = false;
            }
            else
            {
                mute = true;
            }

            Volume = volume;
        }

        public void Dispose()
        {
            DisposeSpeech();
            ttsQueue?.Dispose();
            ttsQueue = null;
        }

        private void DisposeSpeech()
        {
            StopSpeech();

            if (speaker != null)
            {
                lock (speaker)
                {
                    if (speaker != null)
                    {
                        speaker.Dispose();
                        speaker = null;
                    }
                }
            }
        }
        public bool HasSpeech()
        {
            return ttsQueue != null;
        }

        public void StopSpeech()
        {
            ttsQueue.Clear();
            if (speaker != null)
            {
                speaker.Stop();
            }
        }

        public void EnqueueSpeech(SpeechRequest speech)
        {
            if (ttsQueue == null)
            {
                speech.OnFinish?.Invoke();
                return;
            }

            if (Muted)
            {
                if (speech.OnFinish != null)
                {
                    ttsQueue.Enqueue(speech.OnFinish);
                }
            }
            else
            {
                SoundWorkItem soundWorkItem = new SoundWorkItem()
                {
                    Action = () => { Speak(speech); },
                    Priority = speech.Priority
                };
                ttsQueue.Enqueue(soundWorkItem);
            }
        }

        private void Speak(SpeechRequest request)
        {
            if (speaker == null)
            {
                speaker = platformTools.CreateSpeaker(Voice);
            }

            if (speaker == null)
                return;

            speaker.SetRate(Math.Max(-10, Math.Min(10, request.Rate)));

            float factorVolume = (request.Volume / 100.0f) * (Volume / 100.0f);

            speaker.SetVolume(Math.Clamp((int)(factorVolume * 100), 0, 100));

            SoundWorkItem[] soundWorkItems = ttsQueue.WorkItems.OfType<SoundWorkItem>().ToArray();

            int maxPriority = 0;
            if (soundWorkItems.Any())
            {
                maxPriority = soundWorkItems.Select(r => r.Priority).Max();
            }

            if (request.Expiry > DateTime.Now && maxPriority <= request.Priority)
            {
                try
                {
                    string text = SpeechParameters.CreateTextToSpeech(request.RawText, request.Parameters);
                    Logger.SoundLog.Log(this, "TTS", text, Logger.LogType.Notice);
                    if (!Muted)
                    {
                        if (!string.IsNullOrEmpty(text))
                        {
                            speaker.Speak(text);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Logger.SoundLog.LogException(this, ex);
                }
                request.OnFinish?.Invoke();
            }
        }
    }

    public class SpeechRequest : SoundRequest
    {
        public int Rate { get; set; }

        public string RawText { get; set; }

        public SpeechParameters Parameters { get; private set; }

        public SpeechRequest(string rawText, int rate, int volume, SpeechParameters parameters, DateTime expiry,Action onFinish)
             : base(parameters.Priority, volume, expiry, onFinish)
        {
            Rate = rate;
            RawText = rawText;
            Parameters = parameters;
        }
    }
}
#endif