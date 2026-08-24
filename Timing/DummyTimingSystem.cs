using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace Timing
{
    public class DummyTimingSystem : ITimingSystem, IRemoteMarshalUpdatable
    {
        public TimingSystemType Type { get { return TimingSystemType.Dummy; } }

        public bool Connected { get; private set; }

        private volatile bool running;
        private List<int> frequencies;
        private List<Thread> threads;

        public event System.Action OnDataReceived;
        public event System.Action OnDataSent;

        public event DetectionEventDelegate OnDetectionEvent;
        public event MarshallEventDelegate OnMarshallEvent;

        public DummySettings DummingSettings { get; private set; }

        public TimingSystemSettings Settings { get { return DummingSettings; } set { DummingSettings = value as DummySettings; } }

        private Random random;

        public int MaxPilots { get { return DummingSettings.Receivers; } }
        public IEnumerable<StatusItem> Status
        {
            get
            {
                if (DummingSettings.GenerateRandomLaps)
                {
                    float voltage = random.Next(120, 180) / 10.0f;
                    float temperature = random.Next(10, 60);

                    bool alertOverride = !DummingSettings.GenerateAlerts;

                    yield return new StatusItem() { StatusOK = voltage > 14 || alertOverride, Value = voltage + "v" };
                    yield return new StatusItem() { StatusOK = temperature < 50 || alertOverride, Value = temperature + "c" };
                }
            }
        }
        public string Name
        {
            get
            {
                return "DMY";
            }
        }

        public DummyTimingSystem()
        {
            random = new Random();
            DummingSettings = new DummySettings();

            frequencies = new List<int>();
            threads = new List<Thread>();
        }

        public void Dispose()
        {
            EndDetection(EndDetectionType.Normal);
        }

        public bool StartDetection(ref DateTime time, StartMetaData startMetaData)
        {
            running = true;

            if (DummingSettings.GenerateFromKeyboardShortcuts)
            {
                Thread keyboardThread = new Thread(() =>
                {
                    KeyboardState lastState = Keyboard.GetState();

                    Keys[] keys = new Keys[] { Keys.NumPad0, Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad4, Keys.NumPad5, Keys.NumPad6, Keys.NumPad7, Keys.NumPad8, Keys.NumPad9 };

                    while (running)
                    {
                        Thread.Sleep(100);

                        KeyboardState keyboardState = Keyboard.GetState();

                        if (keyboardState.IsKeyDown(Keys.LeftAlt) || keyboardState.IsKeyDown(Keys.RightAlt))
                        {
                            int index = 0;
                            foreach (Keys key in keys)
                            {
                                if (index >= frequencies.Count)
                                    continue;

                                if (keyboardState.IsKeyDown(key) && lastState.IsKeyUp(key))
                                {
                                    if (running)
                                    {
                                        OnDetectionEvent?.Invoke(this, frequencies[index], DateTime.Now, 800);
                                    }
                                }
                                index++;
                            }
                        }

                        lastState = keyboardState;
                    }
                });
                keyboardThread.Name = "Dummy timing system (KB) ";
                keyboardThread.Start();
                threads.Add(keyboardThread);
            }

            if (!DummingSettings.GenerateRandomLaps)
            {
                return true;
            }

            lock (threads)
            {
                float randomPercent = (float)(random.NextDouble() * 100);
                if (randomPercent < DummingSettings.FakeFailureRatePercent)
                {
                    return false;
                }

                if (threads.Any())
                {
                    EndDetection(EndDetectionType.Normal);
                }
                
                running = true;

                int index = 1;
                foreach (int freq in frequencies)
                {
                    int thisFreq = freq;
                    Thread thread = new Thread(() =>
                    {
                        TimeSpan minTime = DummingSettings.TypicalLapTime - TimeSpan.FromSeconds(DummingSettings.Range.TotalSeconds / 2);

                        DateTime start = DateTime.Now.AddSeconds(DummingSettings.OffsetSeconds);
                        while (running && DateTime.Now < start)
                        {
                            Thread.Sleep(10);
                        }

                        IEnumerable<DateTime> triggers = GetTriggers(start, 1000);
                        foreach (DateTime next in triggers)
                        {
                            if (!running)
                                break;

                            while (running && DateTime.Now < next)
                            {
                                Thread.Sleep(10);
                            }
                            OnDataReceived?.Invoke();

                            Logger.TimingLog.Log(this, "Detection", string.Join(", ", Thread.CurrentThread.Name, DateTime.Now, next));

                            if (running)
                            {
                                OnDetectionEvent?.Invoke(this, freq, DateTime.Now, 800);
                            }

                        }
                    });

                    thread.Name = "Dummy timing system (" + index +") " + freq;
                    thread.Start();
                    threads.Add(thread);

                    index++;
                }


                return true;
            }
        }

        public IEnumerable<DateTime> GetTriggers(DateTime start, int count)
        {
            TimeSpan minTime = DummingSettings.TypicalLapTime - TimeSpan.FromSeconds(DummingSettings.Range.TotalSeconds / 2);

            DateTime current = start;
            for (int i = 0; i < count; i++)
            {
                bool falseRead = random.Next(100) < DummingSettings.FalseReadPercent;
                if (falseRead)
                {
                    double falseReadNext = random.NextDouble() * DummingSettings.TypicalLapTimeSeconds;
                    DateTime falseReadTime = current + TimeSpan.FromSeconds(falseReadNext);
                    yield return falseReadTime;
                }
                else
                {
                    double nextTime = random.NextDouble() * DummingSettings.Range.TotalSeconds;
                    DateTime next = current + minTime + TimeSpan.FromSeconds(nextTime);
                    yield return next;

                    current = next;
                }
            }
        }

        public bool EndDetection(EndDetectionType type)
        {
            lock (threads)
            {
                if (!threads.Any())
                {
                    return false;
                }

                running = false;

                foreach (Thread t in threads)
                {
                    if (t != Thread.CurrentThread)
                    {
                        t.Join();
                    }
                }
                threads.Clear();

                return true;
            }
        }
        public bool Connect()
        {
            OnDataSent?.Invoke();
            OnDataReceived?.Invoke();

            Connected = true;

            return true;
        }


        public bool Disconnect()
        {
            OnDataSent?.Invoke();
            OnDataReceived?.Invoke();

            Connected = false;

            return true;
        }

        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> newFrequencies)
        {
            frequencies.Clear();
            frequencies.AddRange(newFrequencies.Select(r => r.Frequency));

            System.Diagnostics.Debug.Assert(frequencies.Distinct().Count() == frequencies.Count);

            OnDataSent?.Invoke();
            OnDataReceived?.Invoke();

            return true;
        }

        protected bool AddListeningFrequencies(int newFrequency)
        {
            frequencies.Add(newFrequency);

            OnDataSent?.Invoke();
            OnDataReceived?.Invoke();

            return true;
        }

        // Last marshal correction accepted via PushMarshalUpdate, for tests/inspection - Dummy
        // has nowhere real to persist it.
        public MarshalData LastMarshalUpdate { get; private set; }

        // Dummy is a local-only test harness with no remote plugin/version concept, so it
        // always supports marshalling.
        public bool MarshalSupported { get { return true; } }

        public void PushMarshalUpdate(MarshalData marshalData)
        {
            LastMarshalUpdate = marshalData;
            Logger.TimingLog.Log(this, "Marshal update pushed for pilot: " + marshalData.PilotName + ", " + marshalData.Laps.Length + " laps");
        }

        // Fabricates a plausible RSSI trace, using the same lap-timing jitter GetTriggers()
        // already uses for live detection, so the native marshal UI can be exercised end to end
        // without needing a real RotorHazard connection.
        public RSSIWaveform GetWaveform(Guid raceId, Guid pilotId)
        {
            const int enterAt = 190;
            const int exitAt = 60;
            const int baseline = 30;
            const double sampleIntervalMs = 20;
            const double peakWidthMs = 300;

            List<DateTime> crossings = GetTriggers(DateTime.MinValue, 6).ToList();
            TimeSpan duration = (crossings.LastOrDefault() - DateTime.MinValue) + TimeSpan.FromSeconds(DummingSettings.TypicalLapTimeSeconds);

            List<TimeSpan> times = new List<TimeSpan>();
            List<int> values = new List<int>();

            for (double ms = 0; ms < duration.TotalMilliseconds; ms += sampleIntervalMs)
            {
                TimeSpan sampleTime = TimeSpan.FromMilliseconds(ms);

                double nearestDistanceMs = crossings
                    .Select(c => Math.Abs((c - DateTime.MinValue - sampleTime).TotalMilliseconds))
                    .DefaultIfEmpty(double.MaxValue)
                    .Min();

                int noise = random.Next(-5, 5);
                int peakBoost = (int)((enterAt - baseline + 40) * Math.Exp(-(nearestDistanceMs * nearestDistanceMs) / (2 * peakWidthMs * peakWidthMs)));

                int rssi = Math.Clamp(baseline + noise + peakBoost, 0, 255);

                times.Add(sampleTime);
                values.Add(rssi);
            }

            return new RSSIWaveform
            {
                Times = times.ToArray(),
                Values = values.ToArray(),
                EnterAt = enterAt,
                ExitAt = exitAt
            };
        }
    }

    public class DummySettings : TimingSystemSettings
    {
        [Category("Random number generation settings (for testing)")]

        public bool GenerateRandomLaps { get; set; }

        [Category("Random number generation settings (for testing)")]
        public double TypicalLapTimeSeconds { get; set; }
      
        [Category("Random number generation settings (for testing)")]
        public double RangeSeconds { get; set; }

        [Category("Random number generation settings (for testing)")]
        public double OffsetSeconds { get; set; }

        [Category("Random number generation settings (for testing)")]
        public double FakeFailureRatePercent { get; set; }

        [Category("Random number generation settings (for testing)")]
        public double FalseReadPercent { get; set; }

        [Category("Random number generation settings (for testing)")]
        public bool GenerateAlerts { get; set; }

        [Category("Random number generation settings (for testing)")]
        public double TestConnectionFailureRatePercent { get; set; }

        [Category("Virtual Hardware")]
        public int Receivers { get; set; }


        [Category("Keyboard shortcuts are ALT + Numpad 0-9")]
        public bool GenerateFromKeyboardShortcuts { get; set; }

        [Browsable(false)]
        public TimeSpan TypicalLapTime { get { return TimeSpan.FromSeconds(TypicalLapTimeSeconds); } }

        [Browsable(false)]
        public TimeSpan Range { get { return TimeSpan.FromSeconds(RangeSeconds); } }


        public DummySettings()
        {
            GenerateRandomLaps = false;
            OffsetSeconds = 5;
            TypicalLapTimeSeconds = 15;
            RangeSeconds = 5;
            FakeFailureRatePercent = 0;
            FalseReadPercent = 10;
            TestConnectionFailureRatePercent = 50;
            Receivers = 8;
            GenerateAlerts = false;
            GenerateFromKeyboardShortcuts = true;
        }

        public override string ToString()
        {
            return "Dummy";
        }
    }
}
