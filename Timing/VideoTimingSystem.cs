using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Timing
{
    public class VideoTimingSystem : ITimingSystem
    {
        public TimingSystemType Type { get { return TimingSystemType.Video; } }

        public bool Connected { get { return true; } }

        public VideoTimingSettings VideoTimingSystemSettings { get; private set; }
        public TimingSystemSettings Settings { get { return VideoTimingSystemSettings; } set { VideoTimingSystemSettings = (VideoTimingSettings)value; } }

        public event DetectionEventDelegate OnDetectionEvent;

        private VideoGateDetector[] detectors;

        private bool detecting;
        public int MaxPilots { get { return 256; } }

        public IEnumerable<StatusItem> Status { get { return new StatusItem[0]; } }

        public string Name
        {
            get
            {
                return "Video";
            }
        }

        public bool Connect()
        {
            return true;
        }

        public bool Disconnect()
        {
            return true;
        }

        public void Dispose()
        {
        }

        public bool StartDetection(ref DateTime time, Guid raceId)
        {

            detecting = true;
            return true;
        }

        public bool EndDetection()
        {
            detecting = false;
            return true;
        }

        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> newFrequencies)
        {
            detectors = newFrequencies.Select(i => new VideoGateDetector(i.Frequency, VideoTimingSystemSettings)).ToArray();
            return true;
        }

        public VideoGateDetector GetGateDetector(int freq)
        {
            foreach (VideoGateDetector vgd in detectors)
            {
                if (vgd.Frequency == freq)
                {
                    return vgd;
                }
            }

            return null;
        }

        public VideoGateDetector ProcessFrame(int freq, Color[] frame, int width, int height)
        {
            if (!detecting)
            {
                return null;
            }

            VideoGateDetector vgd = GetGateDetector(freq);
            if (vgd != null)
            {
                if (vgd.Frequency == freq)
                {
                    vgd.ProcessFrame(frame, width, height);

                    if (vgd.Detected)
                    {
                        OnDetectionEvent?.Invoke(this, freq, DateTime.Now, (ushort)vgd.Max);
                        vgd.ClearDetection();
                    }
                    return vgd;
                }
            }
            return null;
        }

        public bool GetVoltage(out float voltage)
        {
            voltage = 0.0f;
            return true;
        }
    }

    public class VideoGateDetector
    {
        public int Frequency { get; private set; }

        private Queue<float> dotProducts;
        private Queue<DateTime> times;

        private Vector3 colorVector;

        public float Current { get; private set; }

        public bool Detected { get; private set; }

        public float Max { get; private set; }

        private VideoTimingSettings settings;
        private DateTime lastDetection;

        private DateTime triggerBegins;

        public VideoGateDetector(int freq, VideoTimingSettings settings)
        {
            Color gateColor = new Color(settings.StartFinishGateColorR, settings.StartFinishGateColorG, settings.StartFinishGateColorB);
            colorVector = Vector3.Normalize(gateColor.ToVector3());
            dotProducts = new Queue<float>();
            times = new Queue<DateTime>();
            Frequency = freq;
            this.settings = settings;

            lastDetection = DateTime.MinValue;
        }

        public void ProcessFrame(Color[] frame, int width, int height)
        {
            float total = 0;

            int x = 0;
            int y = 0;

            int quaterWidth = width / 4;
            int quaterHeight = height / 4;

            foreach (Color c in frame)
            {
                float dot = Vector3.Dot(colorVector, Vector3.Normalize(c.ToVector3()));
                if (float.IsNaN(dot) || dot < settings.ColorSensitivity)
                {
                    continue;
                }

                // The nature of gates means we don't care about the middle of hte image..
                if (x < quaterWidth || x > 3 * quaterWidth ||
                    y < quaterHeight || y > 3 * quaterHeight)
                {
                    total += dot;
                }
               

                x++;
                if (x >= width)
                {
                    x = 0;
                    y++;
                }
            }

            Current = total;

            dotProducts.Enqueue(total);
            times.Enqueue(DateTime.Now);

            Max = dotProducts.Max();

            DateTime cutoff = DateTime.Now.AddSeconds(-settings.TimeWindowSeconds);
            while (times.Any() && times.First() < cutoff)
            {
                dotProducts.Dequeue();
                times.Dequeue();
            }

            if (Current < settings.TriggerThreshold 
             && Max > settings.ActivateThreshold
             && lastDetection.AddSeconds(settings.TimeWindowSeconds) < DateTime.Now
             && triggerBegins.AddSeconds(settings.TriggerWindowSeconds) < DateTime.Now)
            {
                Detected = true;
                lastDetection = DateTime.Now;
            }

            if (Current < settings.TriggerThreshold)
            {
                triggerBegins = DateTime.Now;
            }
        }

        public void ClearDetection()
        {
            Detected = false;
        }
    }

    public class VideoTimingSettings : TimingSystemSettings
    {
        public int StartFinishGateColorR { get; set; }
        public int StartFinishGateColorG { get; set; }
        public int StartFinishGateColorB { get; set; }

        public int ActivateThreshold { get; set; }
        public int TriggerThreshold { get; set; }
        public float TimeWindowSeconds { get; set; }
        public float TriggerWindowSeconds { get; set; }

        public float ColorSensitivity { get; set; }

        public bool ShowInfo { get; set; }

        public VideoTimingSettings()
        {
            StartFinishGateColorR = 255;
            StartFinishGateColorG = 0;
            StartFinishGateColorB = 255;

            ColorSensitivity = 0.9f;

            ActivateThreshold = 9;
            TriggerThreshold = 2;
            TimeWindowSeconds = 3;
            TriggerWindowSeconds = 0.1f;

            ShowInfo = true;
        }

        public override string ToString()
        {
            return "Video Timing";
        }
    }
}
