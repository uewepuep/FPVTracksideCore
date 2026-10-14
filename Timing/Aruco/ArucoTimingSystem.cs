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
    public class ArucoTimingSystem : ITimingSystem, IRemoteMarshalUpdatable
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

        // The gate rule itself lives in ArucoGate, so that recalculating laps from a recorded
        // waveform runs the same code the timer ran live rather than a second copy of it.
        private class ChannelState
        {
            public readonly ArucoGate Gate = new ArucoGate();
        }

        // --- Marshal waveform ----------------------------------------------------------------
        // Everything below is ArUco's own: the marker-count trace behind the marshal graph, which
        // ArucoWaveformRecorder owns end to end. RotorHazard's marshalling is untouched by any of
        // it - it answers GetWaveform from its own server and shares no state with this.

        private readonly ArucoWaveformRecorder waveforms = new ArucoWaveformRecorder();

        /// <summary>
        /// events/&lt;EventID&gt;/aruco_waveform, set by ArucoTimingManager - the ArUco-only code
        /// that already re-runs on every timing-system rebuild. Null means memory only.
        /// </summary>
        public string WaveformDirectory
        {
            get => waveforms.Directory;
            set => waveforms.Directory = value;
        }

        // Only the reference instance records. Splits see the same frames but time a different
        // gate, and the marshal UI takes the first non-null waveform it is offered, so letting a
        // Split answer would put the wrong gate's trace behind the lap times.
        // ArucoTimingManager.EnforceSinglePrimary guarantees there is at most one.
        private bool IsPrimary => settings != null && settings.Role == TimingSystemRole.Primary;

        // Opt-in, from the ArUco settings. Off, nothing is captured and nothing is written, so
        // the marshal graph has nothing to draw for races run that way - reading a file an
        // earlier race left behind still works, which is why GetWaveform does not test this.
        private bool RecordsWaveform => IsPrimary && settings.RecordMarkerCountLog;

        // Frequency -> pilot for the race being set up. SetListeningFrequencies runs once per
        // race before its start, so StartDetection can hand this to the recorder.
        private readonly Dictionary<int, Guid> pilotByFrequency = new Dictionary<int, Guid>();

        public bool Connect() => true;
        public bool Disconnect() => true;

        public void Dispose()
        {
            lock (stateLock) { stateByFreq.Clear(); pilotByFrequency.Clear(); }
            waveforms.Clear();
        }

        public bool SetListeningFrequencies(IEnumerable<ListeningFrequency> frequencies)
        {
            lock (stateLock)
            {
                stateByFreq.Clear();
                pilotByFrequency.Clear();
                foreach (var f in frequencies)
                {
                    if (!stateByFreq.ContainsKey(f.Frequency))
                        stateByFreq[f.Frequency] = new ChannelState();

                    // Channels with no pilot in this race come through with an empty id.
                    if (f.PilotId != Guid.Empty)
                        pilotByFrequency[f.Frequency] = f.PilotId;
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
                    s.Gate.Reset();
                }
            }
            if (RecordsWaveform)
            {
                Dictionary<int, Guid> pilots;
                lock (stateLock)
                {
                    pilots = new Dictionary<int, Guid>(pilotByFrequency);
                }

                waveforms.Start(startMetaData?.RaceId ?? Guid.Empty, time, pilots);
            }
            else if (IsPrimary)
            {
                Logger.TimingLog?.Log(this, "[ArUco] waveform: not recording, Record Marker Count Log is off");
            }

            detecting = true;
            return true;
        }

        public bool EndDetection(EndDetectionType type)
        {
            detecting = false;

            if (RecordsWaveform)
            {
                waveforms.Finish(settings?.MarkerThreshold ?? 0);
            }

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

            if (RecordsWaveform)
            {
                waveforms.Record(frequency, count, captureTime);
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
                if (!state.Gate.Update(count, captureTime, markerThreshold, flickerLengthMs,
                                       out DateTime detectionTime))
                    return;

                // Peak is reported as the largest marker area seen while the gate was in view.
                OnDetectionEvent?.Invoke(this, frequency, detectionTime, peak);
            }
        }

        // --- IRemoteMarshalUpdatable ---------------------------------------------------------

        public bool MarshalSupported => IsPrimary;

        /// <summary>
        /// Drop the capture for a race that has been reset, so a re-run starts from nothing
        /// rather than showing the previous run's trace. Driven by ArucoTimingManager.
        /// </summary>
        public void DiscardWaveform(Guid raceId)
        {
            if (IsPrimary)
            {
                waveforms.Discard(raceId);
            }
        }

        public RSSIWaveform GetWaveform(Guid raceId, Guid pilotId)
        {
            return IsPrimary ? waveforms.Get(raceId, pilotId, settings?.MarkerThreshold ?? 0) : null;
        }

        // ArUco keeps its laps in FPVTrackside's own database - by the time the marshal editor
        // calls this, it has already written them. Nothing to push anywhere; the interface is
        // implemented so the editor, and its waveform graph, is offered for ArUco at all.
        public void PushMarshalUpdate(MarshalData marshalData)
        {
            Logger.TimingLog?.Log(this, "[ArUco] marshal update (local only) for "
                + marshalData.PilotName + ", " + marshalData.Laps.Length + " laps");
        }
    }
}
