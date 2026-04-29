using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace Timing.Aruco
{
    public class ArucoTimingSystem : ITimingSystem
    {
        public static bool IsNativeAvailable()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return true;

            try
            {
                Cv2.GetVersionString();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public TimingSystemType Type => TimingSystemType.Other;
        public bool Connected => true;
        public int MaxPilots => 32;
        public string Name => "ArUco " + (settings?.MarkerIds ?? "-");

        public ArucoTimingSettings ArucoSettings => settings;

        public TimingSystemSettings Settings
        {
            get => settings;
            set => settings = value as ArucoTimingSettings;
        }

        public event DetectionEventDelegate OnDetectionEvent;
        public event MarshallEventDelegate OnMarshallEvent;

        public IEnumerable<StatusItem> Status
        {
            get
            {
                yield return new StatusItem() { StatusOK = true, Value = "Marker " + (settings?.MarkerIds ?? "-") };
                if (detecting)
                    yield return new StatusItem() { StatusOK = true, Value = "Listen" };
            }
        }

        private ArucoTimingSettings settings;
        private volatile bool detecting;
        private readonly Dictionary<int, ChannelState> stateByFreq = new Dictionary<int, ChannelState>();
        private readonly object stateLock = new object();

        private class ChannelState
        {
            public bool InGate;
            public DateTime FlickerEndTime = DateTime.MinValue;
            public int LastPeak;
        }

        public bool Connect() => true;
        public bool Disconnect() => true;

        public void Dispose()
        {
            lock (stateLock) { stateByFreq.Clear(); }
        }

        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> frequencies)
        {
            lock (stateLock)
            {
                stateByFreq.Clear();
                foreach (var f in frequencies)
                {
                    if (!stateByFreq.ContainsKey(f.Frequency))
                        stateByFreq[f.Frequency] = new ChannelState();
                }
            }
            return true;
        }

        public bool StartDetection(ref DateTime time, StartMetaData startMetaData)
        {
            lock (stateLock)
            {
                foreach (var s in stateByFreq.Values)
                {
                    s.InGate = false;
                    s.FlickerEndTime = DateTime.MinValue;
                    s.LastPeak = 0;
                }
            }
            detecting = true;
            return true;
        }

        public bool EndDetection(EndDetectionType type)
        {
            detecting = false;
            return true;
        }

        /// <summary>
        /// Fed from <see cref="UI.Video.ArucoTimingManager"/> each camera frame with count of
        /// matching markers (after MarkerIds filter and area threshold) for this channel frequency.
        /// markerThreshold / flickerLengthMs are supplied by the manager so Split instances can
        /// inherit the Primary's values (keeping the Split UI limited to MarkerIds only).
        /// </summary>
        public void ReportMarkerCount(int frequency, int count, int peak, DateTime captureTime,
            int markerThreshold, int flickerLengthMs)
        {
            if (!detecting || settings == null) return;

            ChannelState state;
            lock (stateLock)
            {
                if (!stateByFreq.TryGetValue(frequency, out state))
                    return;
            }

            if (count >= markerThreshold)
            {
                state.InGate = true;
                state.LastPeak = peak;
                state.FlickerEndTime = DateTime.MinValue;
                return;
            }

            if (!state.InGate) return;

            if (state.FlickerEndTime == DateTime.MinValue)
                state.FlickerEndTime = captureTime.AddMilliseconds(flickerLengthMs);

            if (captureTime >= state.FlickerEndTime)
            {
                OnDetectionEvent?.Invoke(this, frequency, captureTime, state.LastPeak);
                state.InGate = false;
                state.FlickerEndTime = DateTime.MinValue;
            }
        }
    }
}
