using ImageServer;
using Microsoft.Xna.Framework;
using RaceLib;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Timing;
using Timing.Aruco;
using Tools;
using UI.Nodes;

namespace UI.Video
{
    /// <summary>
    /// Feeds camera frames from each <see cref="ChannelVideoNode"/> to ArUco detectors and
    /// dispatches results to all active <see cref="ArucoTimingSystem"/> instances plus the
    /// <see cref="ArucoFrameOverlay"/>.
    ///
    /// Parallels <see cref="VideoTimingManager"/>: only spawns a detection thread when at least
    /// one <see cref="ArucoTimingSystem"/> is configured, so disabled = zero runtime cost.
    /// Runs per-channel detection in parallel via <see cref="Parallel.For"/> when the Primary
    /// has <c>UseMultiThreadDetection</c> enabled; falls back to a sequential loop otherwise.
    /// </summary>
    public class ArucoTimingManager : IDisposable
    {
        public const int FrameWidth = 480;
        public const int FrameHeight = 360;

        private readonly TimingSystemManager timingSystemManager;
        private readonly ChannelsGridNode channelsGridNode;
        private readonly RaceManager raceManager;
        private readonly ArucoFlickerLapCorrector flickerLapCorrector;

        private volatile bool run;
        private Thread thread;
        private volatile bool disposed;

        // FrameNodeThumb.renderTarget is private; we invalidate it via reflection after
        // enlarging Size, so PreProcess re-creates the RenderTarget at the new size.
        private static readonly FieldInfo renderTargetField =
            typeof(FrameNodeThumb).GetField("renderTarget", BindingFlags.NonPublic | BindingFlags.Instance);

        public ArucoTimingManager(TimingSystemManager tsm, ChannelsGridNode grid)
        {
            timingSystemManager = tsm;
            channelsGridNode = grid;

            Init();
            timingSystemManager.OnInitialise += Init;

            // Subscribed here rather than in Init, which runs again on every timing-system
            // rebuild - see the Dispose comment for what repeated subscription costs.
            raceManager = grid?.EventManager?.RaceManager;
            if (raceManager != null)
            {
                raceManager.OnRaceReset += RaceManager_OnRaceReset;
            }

            // Owned here so it lives and dies with the rest of the ArUco machinery. It checks
            // the ArUco settings itself, so it costs nothing when the setting is off.
            if (grid?.EventManager != null)
            {
                flickerLapCorrector = new ArucoFlickerLapCorrector(grid.EventManager, tsm);
            }
        }

        // A reset keeps the race's id and clears its laps, so the waveform captured for it is
        // now describing a run that no longer exists. Throw it away with the laps.
        private void RaceManager_OnRaceReset(Race race)
        {
            if (race == null)
                return;

            foreach (ArucoTimingSystem sys in timingSystemManager.TimingSystems.OfType<ArucoTimingSystem>())
            {
                sys.DiscardWaveform(race.ID);
            }
        }

        public void Dispose()
        {
            // Unsubscribe before cleaning up. ChannelsGridNode.InitVideoTimingSystems() disposes
            // this instance and creates a replacement every time the Video Input Settings dialog
            // closes (OK *and* Cancel both reach it via EventLayer's VideoSettingsExited handler).
            // Without this the disposed instance stayed subscribed to OnInitialise, so the next
            // InitialiseTimingSystems - e.g. pressing OK in Timing Settings - called Init() on it
            // and restarted its detection thread. Every extra Video Input Settings round trip
            // added another one, all driving the same ArucoTimingSystem.ChannelState from
            // separate threads with independently sampled captureTimes.
            disposed = true;
            if (timingSystemManager != null)
            {
                timingSystemManager.OnInitialise -= Init;
            }
            if (raceManager != null)
            {
                raceManager.OnRaceReset -= RaceManager_OnRaceReset;
            }
            flickerLapCorrector?.Dispose();
            CleanUp();
        }

        public void Init()
        {
            // A disposed instance must never resurrect its thread, even if something still holds
            // a reference and re-raises OnInitialise.
            if (disposed)
                return;

            CleanUp();

            // Probe (and log) native availability up front so we always see the result, even when
            // there are zero ArUco systems configured (which is why the menu would have been
            // hidden in TimingSystemEditor).
            bool nativeOk = ArucoTimingSystem.IsNativeAvailable();
            Logger.TimingLog.Log(this, "[ArUco-Debug] Init: IsNativeAvailable=" + nativeOk);

            try
            {
                var systems = timingSystemManager?.TimingSystems?.OfType<ArucoTimingSystem>().ToArray();
                int arucoCount = systems?.Length ?? 0;
                Logger.TimingLog.Log(this, "[ArUco-Debug] Init: ArucoTimingSystem count=" + arucoCount);
                if (systems != null)
                {
                    foreach (var sys in systems)
                    {
                        var s = sys.ArucoSettings;
                        Logger.TimingLog.Log(this, "[ArUco-Debug] Init:  - Role="
                            + (sys.Settings?.Role.ToString() ?? "null")
                            + " MarkerIds=" + (s?.MarkerIds ?? "null")
                            + " DetectMode=" + (s?.DetectMode.ToString() ?? "null")
                            + " MarkerThreshold=" + (s?.MarkerThreshold.ToString() ?? "null")
                            + " MinMarkerPercent=" + (s?.MinMarkerPercent.ToString() ?? "null")
                            + " FlickerLengthMs=" + (s?.FlickerLengthMs.ToString() ?? "null"));
                    }
                }
            }
            catch (Exception ex) { Logger.TimingLog.LogException(this, ex); }

            if (!HasAruco())
            {
                Logger.TimingLog.Log(this, "[ArUco-Debug] Init: no ArucoTimingSystem configured — detection thread will NOT start.");
                return;
            }

            EnforceSinglePrimary();
            SetWaveformDirectory();

            run = true;
            thread = new Thread(Process)
            {
                Name = "ArucoTimingManager",
                IsBackground = true
            };
            thread.Start();
            Logger.TimingLog.Log(this, "[ArUco-Debug] Init: detection thread started.");
        }

        /// <summary>
        /// Tells each ArUco system where to keep its marshal waveform. Only the UI knows the
        /// event's folder, and this runs on every InitialiseTimingSystems (see the OnInitialise
        /// subscription), so a settings change that rebuilds the timing systems re-supplies it.
        ///
        /// Deliberately done here rather than through EventMetaData/IEventAware: that path fans
        /// out to every timing system, including RotorHazard, and nothing about ArUco's storage
        /// should reach it.
        /// </summary>
        private void SetWaveformDirectory()
        {
            try
            {
                Guid eventId = channelsGridNode?.EventManager?.EventId ?? Guid.Empty;
                if (eventId == Guid.Empty)
                    return;

                string directory = Path.Combine(
                    ApplicationProfileSettings.Instance.EventStorageDirectory.FullName,
                    eventId.ToString(),
                    "aruco_waveform");

                foreach (ArucoTimingSystem sys in timingSystemManager.TimingSystems.OfType<ArucoTimingSystem>())
                {
                    sys.WaveformDirectory = directory;
                }

                Logger.TimingLog.Log(this, "[ArUco-Debug] Init: waveform directory=" + directory);
            }
            catch (Exception ex)
            {
                // Without a directory the waveform simply stays in memory - never a reason to
                // stop detection starting.
                Logger.TimingLog.LogException(this, ex);
            }
        }

        /// <summary>
        /// Keeps at most one ArUco timing system as Primary (the first in iteration order).
        /// </summary>
        private void EnforceSinglePrimary()
        {
            var demoted = ArucoRoles.KeepSinglePrimary(timingSystemManager.TimingSystems
                .OfType<ArucoTimingSystem>()
                .Select(sys => sys.ArucoSettings));

            foreach (var s in demoted)
            {
                string ids = s.MarkerIds ?? "-";
                Logger.TimingLog.Log(this,
                    "[ArUco-Debug] only one Primary is allowed; demoting Marker(s) " + ids + " to Split.");
            }
        }

        public void CleanUp()
        {
            run = false;
            if (thread != null)
            {
                thread.Join();
                thread = null;
            }
        }

        private bool HasAruco()
        {
            var systems = timingSystemManager?.TimingSystems;
            if (systems == null) return false;
            return systems.OfType<ArucoTimingSystem>().Any();
        }

        private void Process()
        {
            var channelDetectors = new Dictionary<ChannelVideoNode, ChannelDetectors>();
            try
            {
                ArucoCalibration calibration = LoadCalibration();

                ArucoFrameOverlay.EnsureRegistered();
                ArucoFrameOverlay.Enabled = true;

                var sizedNodes = new HashSet<ChannelVideoNode>();
                long lastFrame = -1;

                // Detection FPS publishing for the optional overlay readout. Recomputed every ~1s
                // from the iteration count; the overlay reads the latest value via ArucoFrameOverlay.
                var fpsSw = Stopwatch.StartNew();
                int fpsIter = 0;

                while (run)
                {
                    // Re-assert each iteration so that another ArucoTimingManager instance's
                    // shutdown (e.g. the one ReplayNode spawns via its own ChannelsGridNode)
                    // cannot leave this thread running with the global overlay disabled.
                    ArucoFrameOverlay.Enabled = true;

                    var systems = timingSystemManager.TimingSystems
                        .OfType<ArucoTimingSystem>()
                        .ToArray();
                    if (systems.Length == 0)
                    {
                        Thread.Sleep(500);
                        continue;
                    }

                    var channelNodes = channelsGridNode.ChannelNodes
                        .OfType<ChannelVideoNode>()
                        .ToArray();
                    if (channelNodes.Length == 0)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    PrepareChannels(channelNodes, sizedNodes, channelDetectors, calibration);

                    var src = channelNodes[0].FrameNode.Source;
                    if (src == null)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    long frame = src.FrameProcessNumber;
                    if (frame == lastFrame)
                    {
                        Thread.Sleep(1);
                        continue;
                    }
                    lastFrame = frame;

                    DateTime captureTime = DateTime.Now;

                    ArucoTimingSettings reference = GetReferenceSettings(systems);
                    DetectionSettings settings = new DetectionSettings(reference);
                    ApplyOverlaySettings(reference);

                    ChannelInput[] inputs = CollectInputs(channelNodes, settings);
                    List<MarkerDetection>[] results = DetectAll(inputs, channelDetectors, settings);

                    for (int ch = 0; ch < inputs.Length; ch++)
                    {
                        if (results[ch] == null) continue;

                        PublishOverlay(inputs[ch], results[ch]);
                        ReportCounts(systems, reference, inputs[ch].Node, results[ch], captureTime);
                    }

                    fpsIter++;
                    if (fpsSw.ElapsedMilliseconds >= 1000)
                    {
                        ArucoFrameOverlay.DetectionFps = (float)(fpsIter * 1000.0 / fpsSw.Elapsed.TotalMilliseconds);
                        fpsIter = 0;
                        fpsSw.Restart();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.TimingLog.LogException(this, ex);
            }
            finally
            {
                ArucoFrameOverlay.Enabled = false;
                ArucoFrameOverlay.ClearAll();
                foreach (var det in channelDetectors.Values)
                    det?.Dispose();
                channelDetectors.Clear();
            }
        }

        private ArucoCalibration LoadCalibration()
        {
            string calPath = Path.Combine(AppContext.BaseDirectory, "Aruco", "camera_calibration.json");
            ArucoCalibration calibration = ArucoCalibration.TryLoad(calPath, out string calError);
            if (calibration != null)
                Logger.TimingLog.Log(this, "[ArUco-Debug] calibration loaded from " + calPath
                    + " (ref " + calibration.ReferenceWidth + "x" + calibration.ReferenceHeight + ")");
            else
                Logger.TimingLog.Log(this, "[ArUco-Debug] calibration NOT loaded (" + (calError ?? "unknown") + ") — Corrected/Hybrid passes will be skipped. Path: " + calPath);
            return calibration;
        }

        /// <summary>
        /// Sizes each new channel's FrameNodeThumb for detection (480x360, not the default 8x8)
        /// and gives it its own detectors and undistortion map, so per-channel calibration maps,
        /// DetectorParameters and any state a detector keeps are not shared across worker threads.
        /// </summary>
        private static void PrepareChannels(ChannelVideoNode[] channelNodes, HashSet<ChannelVideoNode> sizedNodes,
            Dictionary<ChannelVideoNode, ChannelDetectors> channelDetectors, ArucoCalibration calibration)
        {
            foreach (var cvn in channelNodes)
            {
                if (sizedNodes.Contains(cvn)) continue;
                EnsureDetectionFrameSize(cvn.FrameNode);
                sizedNodes.Add(cvn);
            }

            foreach (var cvn in channelNodes)
            {
                if (channelDetectors.ContainsKey(cvn)) continue;
                channelDetectors[cvn] = new ChannelDetectors(
                    UndistortMap.Create(calibration, FrameWidth, FrameHeight),
                    new IMarkerDetector[] { new ArucoMarkerDetector() });
            }
        }

        /// <summary>
        /// The reference settings (see <see cref="ArucoRoles.Reference"/>). Null when no
        /// instance has settings.
        /// </summary>
        private static ArucoTimingSettings GetReferenceSettings(ArucoTimingSystem[] systems)
        {
            return ArucoRoles.Reference(systems.Select(sys => sys.ArucoSettings));
        }

        /// <summary>
        /// What one detection pass shares across all channels, taken from the reference settings.
        /// Splits do not expose DetectMode in their UI, so their stored value (default Hybrid)
        /// must not influence the single detection pass - otherwise changing the Primary's mode
        /// away from Hybrid would have no effect.
        /// </summary>
        private sealed class DetectionSettings
        {
            public readonly MarkerDetectorOptions Detector;

            // Ignore lost-signal frames on racing pilots' channels.
            public readonly bool IgnoreLostSignal;
            public readonly double LostSignalThreshold;

            // Measure the ratio whenever we either act on it (ignore) or display it (overlay).
            public readonly bool MeasureSignal;

            public readonly bool MultiThread;

            public DetectionSettings(ArucoTimingSettings reference)
            {
                Detector = new MarkerDetectorOptions
                {
                    Mode = reference?.DetectMode ?? ArucoDetectMode.Original,
                    ErrorCorrectionRate = reference?.ErrorCorrectionRate ?? 0.0,
                    HybridDistance = reference?.HybridDistanceThreshold ?? 0,
                    MinMarkerPercent = reference?.MinMarkerPercent ?? 0.0,
                };
                IgnoreLostSignal = reference?.IgnoreLostSignal ?? false;
                LostSignalThreshold = reference?.LostSignalThreshold ?? 0.4;
                MeasureSignal = IgnoreLostSignal || (reference?.ShowSignalRatio ?? false);
                MultiThread = reference?.UseMultiThreadDetection ?? true;
            }
        }

        /// <summary>Overlay display flags track the reference settings.</summary>
        private static void ApplyOverlaySettings(ArucoTimingSettings reference)
        {
            if (reference == null)
                return;

            ArucoFrameOverlay.ShowBox = reference.ShowMarkerBox;
            ArucoFrameOverlay.ShowId = reference.ShowMarkerId;
            ArucoFrameOverlay.ShowSizePercent = reference.ShowMarkerSizePercent;
            ArucoFrameOverlay.ShowFps = reference.ShowFps;
            ArucoFrameOverlay.ShowSignalRatio = reference.ShowSignalRatio;
            ArucoFrameOverlay.LostSignalThreshold = reference.LostSignalThreshold;
            ArucoFrameOverlay.FlipTextVertical = reference.CharacterFlipVertical;
        }

        private struct ChannelInput
        {
            public ChannelVideoNode Node;

            /// <summary>The frame to detect on, or null to skip this channel.</summary>
            public byte[] Bgra;

            /// <summary>Signal ratio for the overlay; NaN when not measured.</summary>
            public double SignalRatio;
        }

        /// <summary>
        /// Collects each channel's frame as BGRA, sequentially (GetColorData can race with the UI
        /// draw thread, so it is done once up front).
        ///
        /// While a race is running (start -> end, including abort), only channels of pilots
        /// actually racing are detected on. Unassigned channels typically show RF static, which
        /// is extremely expensive for ArUco (huge contour counts), so they get a black frame
        /// instead. Outside a running race, all channels are detected on.
        /// </summary>
        private static ChannelInput[] CollectInputs(ChannelVideoNode[] channelNodes, DetectionSettings settings)
        {
            var raceManager = channelNodes[0].EventManager?.RaceManager;
            bool raceRunning = raceManager != null && raceManager.RaceRunning;
            var currentRace = raceManager?.CurrentRace;

            var inputs = new ChannelInput[channelNodes.Length];
            for (int i = 0; i < channelNodes.Length; i++)
            {
                var cvn = channelNodes[i];
                inputs[i] = new ChannelInput { Node = cvn, SignalRatio = double.NaN };

                // Black frame (zero buffer) for non-racing / unassigned channels during a race.
                if (raceRunning &&
                    !(cvn.Pilot != null && currentRace != null && currentRace.HasPilot(cvn.Pilot)))
                {
                    inputs[i].Bgra = new byte[FrameWidth * FrameHeight * 4];
                    continue;
                }

                Color[] pixels = cvn.FrameNode.GetColorData();
                if (pixels == null)
                    continue;

                Tools.Size size = cvn.FrameNode.Size;
                if (size.Width != FrameWidth || size.Height != FrameHeight ||
                    pixels.Length != FrameWidth * FrameHeight)
                    continue;

                byte[] bgra = new byte[pixels.Length * 4];
                for (int k = 0; k < pixels.Length; k++)
                {
                    var p = pixels[k];
                    bgra[k * 4 + 0] = p.B;
                    bgra[k * 4 + 1] = p.G;
                    bgra[k * 4 + 2] = p.R;
                    bgra[k * 4 + 3] = p.A;
                }

                // Of the channels we still detect on (a racing pilot is registered), measure the
                // signal ratio when we either act on it (ignore) or display it (overlay).
                // Blank out any lost-signal frame so the detector never chews on snow. We
                // zero the buffer rather than skip detection so the timing system still receives a
                // "0 markers" report, consistent with the unassigned-channel path above.
                if (settings.MeasureSignal)
                {
                    double ratio = SignalQuality.SignalRatio(bgra, FrameWidth, FrameHeight);
                    inputs[i].SignalRatio = ratio;
                    if (settings.IgnoreLostSignal && ratio < settings.LostSignalThreshold)
                        Array.Clear(bgra, 0, bgra.Length);
                }

                inputs[i].Bgra = bgra;
            }
            return inputs;
        }

        /// <summary>
        /// Runs detection on every channel with a frame, in parallel or sequentially. A channel
        /// with no frame, or no detector, has a null result.
        /// </summary>
        private static List<MarkerDetection>[] DetectAll(ChannelInput[] inputs,
            Dictionary<ChannelVideoNode, ChannelDetectors> channelDetectors, DetectionSettings settings)
        {
            var results = new List<MarkerDetection>[inputs.Length];

            void DetectOne(int ch)
            {
                var input = inputs[ch];
                if (input.Bgra == null) return;
                if (!channelDetectors.TryGetValue(input.Node, out var det)) return;
                det.SetFeed(input.Node.FrameNode.Source, input.Node.Channel?.Frequency ?? 0);
                results[ch] = det.Detect(input.Bgra, FrameWidth, FrameHeight, settings.Detector);
            }

            if (settings.MultiThread && inputs.Length > 1)
            {
                Parallel.For(0, inputs.Length, DetectOne);
            }
            else
            {
                for (int ch = 0; ch < inputs.Length; ch++)
                    DetectOne(ch);
            }

            return results;
        }

        /// <summary>
        /// Caches a channel's detections against its full-resolution FrameSource, keyed per
        /// channel so multiple channels sharing one FrameSource (e.g. 2x2 capture) each keep their
        /// own crop. The overlay hook finds them there.
        /// </summary>
        private static void PublishOverlay(ChannelInput input, List<MarkerDetection> detections)
        {
            ChannelVideoNode cvn = input.Node;
            FrameSource source = cvn.FrameNode.Source;
            if (source == null)
                return;

            var rsb = cvn.FrameNode.RelativeSourceBounds;
            bool flipY = source.Direction == FrameSource.Directions.TopDown;
            bool mirrorX = false;
            // If the source pre-applied flip/mirror, detection coords are already
            // in the rendered orientation — don't remap a second time.
            if (!source.AppliesUserFlipMirror)
            {
                if (source.VideoConfig != null && source.VideoConfig.Flipped)
                    flipY = !flipY;
                if (source.VideoConfig != null && source.VideoConfig.Mirrored)
                    mirrorX = true;
            }

            var cached = new ArucoFrameOverlay.Cached
            {
                Detections = detections,
                DetectionWidth = FrameWidth,
                DetectionHeight = FrameHeight,
                CropRelX = rsb.X,
                CropRelY = rsb.Y,
                CropRelW = rsb.Width,
                CropRelH = rsb.Height,
                FlipY = flipY,
                MirrorX = mirrorX,
                SignalRatio = input.SignalRatio,
            };
            ArucoFrameOverlay.SetLatestDetections(source, cvn, cached);
        }

        /// <summary>
        /// Hands each ArUco instance the number of its own markers in this channel's detections.
        /// Every instance inherits shared thresholds from the reference (Primary, or lowest-index
        /// Split when no Primary exists).
        /// </summary>
        private static void ReportCounts(ArucoTimingSystem[] systems, ArucoTimingSettings reference,
            ChannelVideoNode cvn, List<MarkerDetection> detections, DateTime captureTime)
        {
            foreach (var sys in systems)
            {
                var s = sys.ArucoSettings;
                if (s == null) continue;

                ArucoTimingSettings effective = reference ?? s;
                int matching = CountMatching(detections, s.EffectiveMarkerIds, effective.MinMarkerPercent);

                sys.ReportMarkerCount(cvn.Channel.Frequency, matching, captureTime,
                    effective.MarkerThreshold, effective.FlickerLengthMs);
            }
        }

        /// <summary>Detections with one of the wanted IDs and at least the minimum area.</summary>
        private static int CountMatching(List<MarkerDetection> detections, IReadOnlyList<int> wantedIds, double minMarkerPercent)
        {
            double minAreaPx = (double)FrameWidth * FrameHeight * (minMarkerPercent / 100.0);

            int matching = 0;
            foreach (var d in detections)
            {
                if (!wantedIds.Contains(d.Id)) continue;
                if (d.AreaPx < minAreaPx) continue;
                matching++;
            }
            return matching;
        }

        /// <summary>
        /// Resizes the <see cref="FrameNodeThumb"/> to 480x360 and invalidates its existing
        /// RenderTarget so the next PreProcess recreates it at the new resolution. Safe to call
        /// repeatedly; no-op if already correctly sized.
        /// </summary>
        private static void EnsureDetectionFrameSize(FrameNodeThumb fn)
        {
            if (fn == null) return;

            bool sizeOk = fn.Size.Width == FrameWidth && fn.Size.Height == FrameHeight;
            var existingRt = renderTargetField?.GetValue(fn) as RenderTarget2D;
            bool rtOk = existingRt != null
                        && existingRt.Width == FrameWidth
                        && existingRt.Height == FrameHeight;
            if (sizeOk && (existingRt == null || rtOk)) return;

            fn.Size = new Tools.Size(FrameWidth, FrameHeight);

            if (existingRt != null && !rtOk)
            {
                try
                {
                    if (fn.CompositorLayer != null)
                        fn.CompositorLayer.CleanUp(existingRt);
                    else
                        existingRt.Dispose();
                }
                catch { }
                renderTargetField?.SetValue(fn, null);
            }
        }
    }
}
