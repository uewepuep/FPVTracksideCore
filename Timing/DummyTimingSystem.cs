using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace Timing
{
    public class DummyTimingSystem : ITimingSystem
    {
        public TimingSystemType Type { get { return TimingSystemType.Dummy; } }

        public bool Connected { get; private set; }

        private bool running;
        private List<int> frequencies;
        private List<Thread> threads;

        public event System.Action OnDataReceived;
        public event System.Action OnDataSent;

        public event DetectionEventDelegate OnDetectionEvent;

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
            EndDetection();
        }

        public bool StartDetection(ref DateTime time, Guid raceId)
        {
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
                    EndDetection();
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

        public bool EndDetection()
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

        [Category("Virtual Hardware")]
        public int Receivers { get; set; }


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
            Receivers = 8;
            GenerateAlerts = false;
        }

        public override string ToString()
        {
            return "Dummy";
        }
    }
}
