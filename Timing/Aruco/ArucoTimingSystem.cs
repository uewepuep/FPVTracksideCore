using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using Tools;

namespace Timing.Aruco
{
    public class ArucoTimingSystem : ITimingSystem
    {
        private static bool nativeProbed;
        private static bool nativeAvailable;
        private static readonly object nativeProbeLock = new object();

        public static bool IsNativeAvailable()
        {
            lock (nativeProbeLock)
            {
                if (nativeProbed) return nativeAvailable;
                nativeProbed = true;
                nativeAvailable = ProbeNative();
                return nativeAvailable;
            }
        }

        private static bool ProbeNative()
        {
            // Always log the host environment so we can correlate failures with arch / OS.
            try
            {
                Logger.TimingLog?.Log(null,
                    "[ArUco-Debug] env: OS=" + RuntimeInformation.OSDescription
                    + ", FrameworkDesc=" + RuntimeInformation.FrameworkDescription
                    + ", ProcessArch=" + RuntimeInformation.ProcessArchitecture
                    + ", OSArch=" + RuntimeInformation.OSArchitecture
                    + ", RID=" + RuntimeInformation.RuntimeIdentifier);
            }
            catch { /* logging must never abort the probe */ }

            // Surface where the loader will search and whether the bundled dylib actually exists.
            try
            {
                string baseDir = AppContext.BaseDirectory;
                Logger.TimingLog?.Log(null, "[ArUco-Debug] AppContext.BaseDirectory=" + baseDir);

                string[] candidates = new[]
                {
                    Path.Combine(baseDir, "runtimes", "osx-arm64", "native", "libOpenCvSharpExtern.dylib"),
                    Path.Combine(baseDir, "runtimes", "osx-x64",   "native", "libOpenCvSharpExtern.dylib"),
                    Path.Combine(baseDir, "libOpenCvSharpExtern.dylib"),
                    Path.Combine(baseDir, "runtimes", "linux-x64", "native", "libOpenCvSharpExtern.so"),
                    Path.Combine(baseDir, "libOpenCvSharpExtern.so"),
                    Path.Combine(baseDir, "runtimes", "win-x64",   "native", "OpenCvSharpExtern.dll"),
                    Path.Combine(baseDir, "OpenCvSharpExtern.dll"),
                };
                foreach (string c in candidates)
                {
                    bool exists = File.Exists(c);
                    Logger.TimingLog?.Log(null, "[ArUco-Debug] native-probe: "
                        + (exists ? "FOUND   " : "missing ") + c
                        + (exists ? (" size=" + new FileInfo(c).Length) : ""));
                }
            }
            catch (Exception ex)
            {
                Logger.TimingLog?.LogException(null, ex);
            }

            // On Windows the OpenCvSharp4.runtime.win NuGet ships the native DLL, so just trust it.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Logger.TimingLog?.Log(null, "[ArUco-Debug] Windows host: skipping Cv2.GetVersionString probe (assumed available).");
                return true;
            }

            // Non-Windows: actually call into the native library so dlopen failures surface.
            // Log the concrete exception type/message — the original code swallowed it silently.
            try
            {
                string ver = Cv2.GetVersionString();
                Logger.TimingLog?.Log(null, "[ArUco-Debug] Cv2.GetVersionString() OK, OpenCV " + ver);

                try
                {
                    using (var d = CvAruco.GetPredefinedDictionary(OpenCvSharp.Aruco.PredefinedDictionaryName.Dict4X4_50))
                    {
                        Logger.TimingLog?.Log(null, "[ArUco-Debug] CvAruco.GetPredefinedDictionary(Dict4X4_50) OK.");
                    }
                }
                catch (Exception arEx)
                {
                    // Cv2 loaded but ArUco entry points missing — common when the dylib was built
                    // without opencv_contrib.
                    Logger.TimingLog?.LogException(null, arEx);
                    Logger.TimingLog?.Log(null, "[ArUco-Debug] Cv2 loaded but ArUco failed: "
                        + arEx.GetType().FullName + ": " + arEx.Message);
                    return false;
                }

                return true;
            }
            catch (DllNotFoundException dnf)
            {
                Logger.TimingLog?.Log(null, "[ArUco-Debug] DllNotFoundException loading OpenCvSharpExtern: " + dnf.Message);
                return false;
            }
            catch (TypeInitializationException tie)
            {
                Logger.TimingLog?.LogException(null, tie);
                Logger.TimingLog?.Log(null, "[ArUco-Debug] TypeInitializationException: "
                    + (tie.InnerException?.GetType().FullName ?? "no-inner")
                    + ": " + (tie.InnerException?.Message ?? ""));
                return false;
            }
            catch (Exception ex)
            {
                Logger.TimingLog?.LogException(null, ex);
                Logger.TimingLog?.Log(null, "[ArUco-Debug] Cv2 probe threw "
                    + ex.GetType().FullName + ": " + ex.Message);
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

            // Time of the last crossing reported for this channel. Used to guarantee the times we
            // hand to RaceLib never repeat or go backwards - see ReportMarkerCount.
            public DateTime LastDetectionTime = DateTime.MinValue;
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
            // Snapshot under stateLock, then take each channel lock separately. Never hold both at
            // once: ReportMarkerCount raises OnDetectionEvent while holding a channel lock, so a
            // stateLock -> channel lock nesting here could deadlock against it.
            ChannelState[] states;
            lock (stateLock)
            {
                states = stateByFreq.Values.ToArray();
            }

            foreach (var s in states)
            {
                lock (s)
                {
                    s.InGate = false;
                    s.FlickerEndTime = DateTime.MinValue;
                    s.LastPeak = 0;
                    s.LastDetectionTime = DateTime.MinValue;
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

            // The whole state machine runs under the channel's own lock, not just the dictionary
            // lookup above. InGate / FlickerEndTime are read-modify-written here, so an unlocked
            // version lets two callers both pass the FlickerEndTime check before either clears
            // InGate, emitting two crossings for a single gate pass. RaceLib cannot recover from
            // that: Race.RecordLap looks for the previous lap with a strict "Detection.Time <"
            // filter, so a duplicate carrying an identical or slightly earlier timestamp skips the
            // lap it duplicates and gets measured from the one before it. The result is a
            // full-length copy of a real lap that passes both RecordLap's <1ms guard and
            // Lapalyser's MinLapTime check, handing the pilot a lap they never flew.
            //
            // The event is raised inside the lock deliberately: it is already raised synchronously
            // on the caller's thread, and releasing first would let a preempted caller deliver an
            // older detectionTime after a newer one - reintroducing the inversion this guards
            // against. Contention is limited to callers reporting the same channel.
            lock (state)
            {
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

                if (captureTime < state.FlickerEndTime) return;

                DateTime detectionTime = captureTime.AddMilliseconds(-flickerLengthMs);

                state.InGate = false;
                state.FlickerEndTime = DateTime.MinValue;

                // Never report a time at or before the previous crossing on this channel. Detection
                // times are derived from the caller's captureTime, so anything that makes those
                // non-monotonic - a second detection thread, or an OS clock step - would otherwise
                // reach RaceLib as an out-of-order lap.
                if (detectionTime <= state.LastDetectionTime) return;
                state.LastDetectionTime = detectionTime;

                OnDetectionEvent?.Invoke(this, frequency, detectionTime, state.LastPeak);
            }
        }
    }
}
