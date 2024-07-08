using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Timing.ImmersionRC;
using Timing.RotorHazard;
using Tools;

namespace Timing
{
    public delegate void TimingSystemDetectionEventDelegate(TimingSystemType type, int timingSystem, int frequency, DateTime time, bool isLapEnd, int peak);

    public class TimingSystemManager : IDisposable
    {
        protected bool detectionRunning;

        private Thread connector;
        private bool disposing;

        public event Action<int> OnConnected;
        public event System.Action OnDisconnected;

        public event System.Action<ITimingSystem> OnDataReceived;
        public event System.Action OnDataSent;

        public event System.Action OnInitialise;

        public event TimingSystemDetectionEventDelegate DetectionEvent;

        public ITimingSystem[] PrimeSystems { get; private set; }
        public ITimingSystem[] SplitSystems { get; private set; }

        public IEnumerable<ITimingSystem> TimingSystems
        {
            get
            {
                return PrimeSystems.Union(SplitSystems);
            }
        }

        public IEnumerable<ITimingSystem> TimingSystemsSectorOrder
        {
            get
            {
                foreach (ITimingSystem timingSystem in SplitSystems)
                    yield return timingSystem;

                foreach (ITimingSystem timingSystem in PrimeSystems)
                    yield return timingSystem;
            }
        }

        public bool Connected
        {
            get
            {
                if (!TimingSystems.Any()) return false;
                return TimingSystems.All(t => t.Connected);
            }
        }

        public int ConnectedCount
        {
            get
            {
                if (!TimingSystems.Any()) return 0;

                return TimingSystems.Where(t => t.Connected).Count();
            }
        }

        public TimingSystemSettings[] TimingSystemsSettings { get; set; }

        public int SplitsPerLap { get { return SplitSystems.Length + 1; } }

        public bool HasVideoTiming { get { return TimingSystems.OfType<VideoTimingSystem>().Any(); } }
        public bool HasDummyTiming { get { return TimingSystems.All(r => r is DummyTimingSystem) && ConnectedCount > 0; } }

        public int TimingSystemCount { get { return TimingSystems.Count(); } }

        public bool HasSpectrumAnalyser { get; private set; }
        public bool IsDetecting { get { return detectionRunning; } }

        public int MaxPilots
        {
            get
            {
                if (PrimeSystems != null && PrimeSystems.Any())
                {
                    return PrimeSystems.Sum(r => r.MaxPilots);
                }
                return 0;
            }
        }

        public ListeningFrequency[] LastListeningFrequencies { get; private set; }

        public TimingSystemManager(Profile profile)
        {
            PrimeSystems = new ITimingSystem[0];
            SplitSystems = new ITimingSystem[0];
            LastListeningFrequencies = new ListeningFrequency[0];

            InitialiseTimingSystems(profile);
        }


        public void ClearTimingSystems()
        {
            Logger.TimingLog.LogCall(this);

            foreach (ITimingSystem timingSystem in TimingSystems)
            {
                timingSystem.Dispose();
            }
            PrimeSystems = new ITimingSystem[0];
            SplitSystems = new ITimingSystem[0];

            HasSpectrumAnalyser = false;
        }

        public void InitialiseTimingSystems(Profile profile)
        {
            Logger.TimingLog.LogCall(this);

            if (TimingSystemCount > 0)
            {
                ClearTimingSystems();
            }

            HasSpectrumAnalyser = false;

            TimingSystemsSettings = TimingSystemSettings.Read(profile);

            ITimingSystem[] timingSystems = CreateTimingSystems().ToArray();

            foreach (ITimingSystem timingSystem in timingSystems)
            {
                timingSystem.OnDetectionEvent += OnDetectionEvent;

                if (timingSystem is ITimingSystemWithRSSI)
                {
                    HasSpectrumAnalyser = true;
                }
            }

            if (timingSystems.Length > 1)
            {
                PrimeSystems = timingSystems.Where(r => r.Settings.Role == TimingSystemRole.Primary).ToArray();
                SplitSystems = timingSystems.Where(r => r.Settings.Role == TimingSystemRole.Split).ToArray();
            }
            else
            {
                PrimeSystems = timingSystems.ToArray();
                SplitSystems = new ITimingSystem[0];
            }

            detectionRunning = false;
            connector = null;

            OnInitialise?.Invoke();
        }

        private IEnumerable<ITimingSystem> CreateTimingSystems()
        {
            Logger.TimingLog.LogCall(this);
            foreach (TimingSystemSettings settings in TimingSystemsSettings)
            {
                ITimingSystem timingSystem = null;

                if (settings is LapRFSettingsEthernet)
                    timingSystem = new LapRFTimingEthernet();

                else if (settings is LapRFSettingsUSB)
                    timingSystem = new LapRFTimingUSB();

                else if (settings is DummySettings)
                    timingSystem = new DummyTimingSystem();

                else if (settings is VideoTimingSettings)
                    timingSystem = new VideoTimingSystem();

                else if (settings is RotorHazardSettings)
                    timingSystem = new RotorHazardTimingSystem();

                if (timingSystem != null)
                {
                    timingSystem.Settings = settings;
                }

                if (timingSystem != null)
                {
                    yield return timingSystem;
                }
            }
        }

        public virtual void Dispose()
        {
            disposing = true;
            if (connector != null)
            {
                connector.Join();
                connector = null;
            }

            if (detectionRunning)
            {
                EndDetection();
            }

            Disconnect();
        }

        private void MaintainConnection()
        {
            bool lastLoopState = false;
            while (!disposing)
            {
                try
                {
                    Thread.Sleep(1000);

                    if (PrimeSystems == null || !PrimeSystems.Any())
                        continue;

                    int newlyConnected = 0;

                    foreach (ITimingSystem timingSystem in TimingSystems)
                    {
                        if (!timingSystem.Connected && !disposing)
                        {
                            if (timingSystem.Connect())
                            {
                                newlyConnected++;
                                Logger.TimingLog.Log(this, "Connected", timingSystem, Logger.LogType.Notice);
                                lastLoopState = true;

                                if (!detectionRunning)
                                {
                                    timingSystem.EndDetection();
                                }
                            }
                        }
                    }

                    if (newlyConnected > 0)
                    {
                        OnConnected?.Invoke(newlyConnected);
                    }

                    bool primesConnected = PrimeSystems.All(t => t.Connected);

                    if (lastLoopState != primesConnected && !primesConnected)
                    {
                        foreach (ITimingSystem prime in PrimeSystems.Where(r => !r.Connected))
                        {
                            Logger.TimingLog.Log(this, "Disconnected", prime, Logger.LogType.Notice);
                        }
                        OnDisconnected?.Invoke();
                    }

                    lastLoopState = primesConnected;
                }
                catch (Exception e) 
                {
                    Logger.TimingLog.LogException(this, e);
                }
            }
        }

        public bool StartDetection(ref DateTime start)
        {
            return StartDetection(ref start, Guid.Empty);
        }

        public bool StartDetection(ref DateTime start, Guid raceId)
        {
            Logger.TimingLog.LogCall(this);

            if (PrimeSystems == null || !PrimeSystems.Any())
                return false;

            detectionRunning = true;
            OnDataSent?.Invoke();

            bool startedDetection = true;
            foreach (ITimingSystem prime in PrimeSystems)
            {
                try
                {
                    startedDetection &= prime.StartDetection(ref start, raceId);
                    if (startedDetection)
                    {
                        OnDataReceived?.Invoke(prime);
                    }
                }
                catch (Exception e) 
                {
                    Logger.TimingLog.LogException(this, e);
                    startedDetection = false;
                }
            }

            DateTime auxStart = start;

            if (startedDetection)
            {
                foreach (ITimingSystem aux in SplitSystems)
                {
                    try
                    {
                        if (aux.StartDetection(ref auxStart, raceId))
                        {
                            OnDataReceived?.Invoke(aux);
                        }
                    }
                    catch (Exception e) 
                    {
                        Logger.TimingLog.LogException(this, e);
                    }
                }
                return true;
            }
            else
            {
                foreach (ITimingSystem ts in TimingSystems)
                {
                    try
                    {
                        ts.EndDetection();
                    }
                    catch (Exception e)
                    {
                        Logger.TimingLog.LogException(this, e);
                        startedDetection = false;
                    }
                }

                detectionRunning = false;
                return false;
            }
        }

        public void EndDetection()
        {
            Logger.TimingLog.LogCall(this);

            detectionRunning = false;
            OnDataSent?.Invoke();

            foreach (ITimingSystem timingSystem in TimingSystems)
            {
                try
                {
                    if (timingSystem.EndDetection())
                    {
                        OnDataReceived?.Invoke(timingSystem);
                    }
                }
                catch (Exception e)
                {
                    Logger.TimingLog.LogException(this, e);
                }
            }
        }

        public void Connect()
        {
            if (connector == null)
            {
                connector = new Thread(MaintainConnection);
                connector.Name = "Maintain Connection";
                connector.Start();
            }
        }

        public void Disconnect()
        {
            OnDataSent?.Invoke();
            foreach (ITimingSystem timingSystem in TimingSystems)
            {
                try
                {
                    if (timingSystem.Disconnect())
                    {
                        OnDataReceived?.Invoke(timingSystem);
                    }
                }
                catch (Exception e)
                {
                    Logger.TimingLog.LogException(this, e);
                }
            }
        }

        protected void OnDetectionEvent(ITimingSystem timingSystem, int frequency, DateTime time, int peak)
        {
            Logger.TimingLog.LogCall(this, frequency, time.ToLogFormat(), peak);

            OnDataReceived?.Invoke(timingSystem);

            int index = GetIndex(timingSystem);

            bool isLapEnd = timingSystem.Settings.Role == TimingSystemRole.Primary;
            DetectionEvent?.Invoke(timingSystem.Type, index, frequency, time, isLapEnd, peak);
        }


        // 0 Primary, 1,2,3 etc secondary. 
        public int GetIndex(ITimingSystem timingSystem)
        {
            int index = 0;

            foreach (ITimingSystem t in PrimeSystems)
            {
                if (t == timingSystem)
                {
                    return index;
                }
                index++;
            }

            foreach (ITimingSystem t in SplitSystems)
            {
                if (t == timingSystem)
                {
                    return index;
                }
                index++;
            }
            return index;
        }
        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> newFrequencies)
        {
            if (PrimeSystems.Length == 0)
                return false;

            try
            {
                ListeningFrequency[] ordered = newFrequencies.Distinct().OrderBy(i => i.Frequency).ToArray();

                LastListeningFrequencies = ordered;

                Logger.TimingLog.LogCall(this, string.Join(", ", ordered.Select(r => r.ToString())));

                // Split the listening frequencies into as many groups as there are primary systems...
                IEnumerable<IEnumerable<ListeningFrequency>> splits = ordered.Split(PrimeSystems.Length);

                bool setOk = true;

                int index = 0;
                foreach (IEnumerable<ListeningFrequency> split in splits)
                {
                    IEnumerable<ListeningFrequency> orderedSplit = split.OrderBy(i => i.Frequency);

                    ITimingSystem prime = PrimeSystems[index];
                    if (prime == null)
                        return false;

                    OnDataSent?.Invoke();

                    Logger.TimingLog.Log(this, prime.ToString() + ".SetFrequencies ", string.Join(", ", orderedSplit.Select(r => r.ToString())));
                    setOk &= prime.SetListeningFrequencies(orderedSplit);
                    
                    if (setOk)
                    {
                        OnDataReceived?.Invoke(prime);
                    }

                    index++;
                }

                if (setOk)
                {
                    foreach (ITimingSystem aux in SplitSystems)
                    {
                        Logger.TimingLog.Log(this, aux.ToString() + ".SetFrequencies ", string.Join(", ", ordered.Select(r => r.ToString())));
                        if (aux.SetListeningFrequencies(ordered))
                        {
                            OnDataReceived?.Invoke(aux);
                        }
                    }

                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                Logger.TimingLog.LogException(this, e);
                return false;
            }
        }

        public IEnumerable<RSSI> GetRSSI()
        {
            foreach (ITimingSystemWithRSSI timingSystem in TimingSystems.OfType<ITimingSystemWithRSSI>())
            {
                foreach (RSSI rssi in timingSystem.GetRSSI())
                {
                    yield return rssi;
                }
            }
        }
    }
}
