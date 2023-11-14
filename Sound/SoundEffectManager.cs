using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Audio;
using Tools;

namespace Sound
{
    public class SoundEffectManager : IDisposable
    {
        private Dictionary<string, SoundEffect> effects;

        private WorkQueue wavQueue;

        public bool Muted { get; set; }

        public SoundEffectManager()
        {
            effects = new Dictionary<string, SoundEffect>();

            wavQueue = new WorkQueue("Sound Manager WAV");
            Muted = false;
        }

        public void Dispose()
        {
            wavQueue?.Dispose();
            wavQueue = null;

            if (effects != null)
            {
                lock (effects)
                {
                    foreach (var effect in effects.Values)
                    {
                        if (effect != null)
                        {
                            effect.Dispose();
                        }
                    }
                    effects.Clear();
                }
            }
        }

        public void EnqueueSoundEffect(SoundEffectRequest request)
        {
            if (Muted)
            {
                if (request.OnFinish != null)
                {
                    wavQueue.Enqueue(request.OnFinish);
                }
            }
            else
            {
                SoundWorkItem soundWorkItem = new SoundWorkItem()
                {
                    Action = () => { PlaySound(request); },
                    Priority = request.Priority
                };
                wavQueue.Enqueue(soundWorkItem);
            }
        }

        private void PlaySound(SoundEffectRequest request)
        {
            SoundWorkItem[] soundWorkItems = wavQueue.WorkItems.OfType<SoundWorkItem>().ToArray();

            int maxPriority = 0;
            if (soundWorkItems.Any())
            {
                maxPriority = soundWorkItems.Select(r => r.Priority).Max();
            }

            if (request.Expiry > DateTime.Now && maxPriority <= request.Priority)
            {
                try
                {
                    if (File.Exists(request.Filename))
                    {
                        PlayWaveFile(request.Filename, request.Volume);
                    }
                }
                catch (Exception ex)
                {
                    Logger.SoundLog.LogException(this, ex);
                }
                request.OnFinish?.Invoke();
            }
        }


        private bool PlayWaveFile(string filename, int volume)
        {
            SoundEffect effect;

            lock (effects)
            {
                if (!effects.TryGetValue(filename, out effect))
                {
                    effect = LoadSound(filename);
                }
            }

            if (effect == null)
            {
                return false;
            }

            try
            {
                SoundEffectInstance instance = effect.CreateInstance();
                instance.Volume = Math.Clamp(volume / 100.0f, 0, 1);
                instance.Play();

                Logger.SoundLog.Log(this, "Play Sound", filename, Logger.LogType.Notice);

                while (instance.State == SoundState.Playing)
                {
                    System.Threading.Thread.Sleep(1);
                }
            }
            catch (Exception e)
            {
                Logger.SoundLog.LogException(this, e);
                return false;
            }
           

            return true;
        }

        public void LoadSounds(IEnumerable<string> filenames)
        {
            foreach (string filename in filenames)
            {
                LoadSound(filename);
            }
        }

        private SoundEffect LoadSound(string filename)
        {
            if (!File.Exists(filename))
            {
                filename = Directory.GetCurrentDirectory() + "\\" + filename;
            }

            if (File.Exists(filename) && !effects.ContainsKey(filename))
            {
                using (FileStream fileStream = new FileStream(filename, FileMode.Open))
                {
                    SoundEffect effect = SoundEffect.FromStream(fileStream);

                    Logger.SoundLog.Log(this, "Load Sound Success", filename, Logger.LogType.Notice);

                    lock (effects)
                    {
                        effects.Add(filename, effect);
                    }

                    return effect;
                }
            }

            Logger.SoundLog.Log(this, "Load Sound Failure", filename, Logger.LogType.Error);
            return null;
        }
    }

    public class SoundEffectRequest : SoundRequest
    {
        public string Filename { get; private set; }

        public SoundEffectRequest(string filename, int priority, int volume, DateTime expiry, Action onFinish)
            : base(priority, volume, expiry, onFinish)
        {
            Filename = filename;
        }
    }
}
