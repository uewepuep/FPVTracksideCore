using System;
using System.Collections.Generic;
using System.IO;
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
                DateTime detectionTime = captureTime.AddMilliseconds(-flickerLengthMs);
                OnDetectionEvent?.Invoke(this, frequency, detectionTime, state.LastPeak);
                state.InGate = false;
                state.FlickerEndTime = DateTime.MinValue;
            }
        }
    }
}
