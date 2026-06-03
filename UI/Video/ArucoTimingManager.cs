using ImageServer;
using Microsoft.Xna.Framework;
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

        private volatile bool run;
        private Thread thread;

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
        }

        public void Dispose()
        {
            CleanUp();
        }

        public void Init()
        {
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
        /// Keeps at most one ArUco timing system as Primary (the first in iteration order).
        /// Any additional Primary instances are demoted to Split so that only the start/finish
        /// sector closes laps; the others record split times.
        /// </summary>
        private void EnforceSinglePrimary()
        {
            var arucoSystems = timingSystemManager.TimingSystems
                .OfType<ArucoTimingSystem>()
                .ToArray();

            bool seenPrimary = false;
            foreach (var sys in arucoSystems)
            {
                if (sys.Settings == null) continue;
                if (sys.Settings.Role != TimingSystemRole.Primary) continue;

                if (!seenPrimary)
                {
                    seenPrimary = true;
                    continue;
                }

                string ids = sys.ArucoSettings?.MarkerIds ?? "-";
                Logger.TimingLog.Log(this,
                    "[ArUco-Debug] only one Primary is allowed; demoting Marker(s) " + ids + " to Split.");
                sys.Settings.Role = TimingSystemRole.Split;
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
            var channelDetectors = new Dictionary<ChannelVideoNode, ArucoMarkerDetector>();
            try
            {
                string calPath = Path.Combine(AppContext.BaseDirectory, "Aruco", "camera_calibration.json");
                ArucoCalibration calibration = ArucoCalibration.TryLoad(calPath, out string calError);
                if (calibration != null)
                    Logger.TimingLog.Log(this, "[ArUco-Debug] calibration loaded from " + calPath
                        + " (ref " + calibration.ReferenceWidth + "x" + calibration.ReferenceHeight + ")");
                else
                    Logger.TimingLog.Log(this, "[ArUco-Debug] calibration NOT loaded (" + (calError ?? "unknown") + ") — Corrected/Hybrid passes will be skipped. Path: " + calPath);

                ArucoFrameOverlay.EnsureRegistered();
                ArucoFrameOverlay.Enabled = true;

                var sizedNodes = new HashSet<ChannelVideoNode>();
                long lastFrame = -1;

                // Detection FPS publishing for the optional overlay readout. Recomputed every ~1s
                // from the iteration count; the overlay reads the latest value via ArucoFrameOverlay.
                var fpsSw = Stopwatch.StartNew();
                int fpsIter = 0;

                // [ArUco-Debug] per-second aggregates so we can see why detection isn't firing
                // without spamming the log every frame.
                int dbgLoopIter = 0;
                int dbgSkipNoSystems = 0, dbgSkipNoChannels = 0, dbgSkipNullSource = 0, dbgSkipSameFrame = 0;
                int dbgBgraOk = 0, dbgBgraNullPixels = 0, dbgBgraBadSize = 0;
                int dbgTotalDetections = 0;
                int[] dbgPerIdCount = new int[4];
                int dbgMatchingPositive = 0;
                int dbgPrimaryAbsent = 0;
                ArucoDetectMode dbgLastMode = ArucoDetectMode.Original;
                double dbgLastMinAreaPx = 0;
                int dbgLastChannelCount = 0;
                long dbgLastFrameSeen = -1;

                while (run)
                {
                    // Re-assert each iteration so that another ArucoTimingManager instance's
                    // shutdown (e.g. the one ReplayNode spawns via its own ChannelsGridNode)
                    // cannot leave this thread running with the global overlay disabled.
                    ArucoFrameOverlay.Enabled = true;
                    dbgLoopIter++;

                    var systems = timingSystemManager.TimingSystems
                        .OfType<ArucoTimingSystem>()
                        .ToArray();
                    if (systems.Length == 0)
                    {
                        dbgSkipNoSystems++;
                        Thread.Sleep(500);
                        goto SecondTick;
                    }

                    var channelNodes = channelsGridNode.ChannelNodes
                        .OfType<ChannelVideoNode>()
                        .ToArray();
                    dbgLastChannelCount = channelNodes.Length;
                    if (channelNodes.Length == 0)
                    {
                        dbgSkipNoChannels++;
                        Thread.Sleep(100);
                        goto SecondTick;
                    }

                    // Ensure each FrameNodeThumb is sized for detection (480x360, not the default 8x8).
                    foreach (var cvn in channelNodes)
                    {
                        if (sizedNodes.Contains(cvn)) continue;
                        EnsureDetectionFrameSize(cvn.FrameNode);
                        sizedNodes.Add(cvn);
                    }

                    // Lazily allocate one detector per channel so per-channel calibration maps and
                    // DetectorParameters state don't get shared across worker threads.
                    foreach (var cvn in channelNodes)
                    {
                        if (channelDetectors.ContainsKey(cvn)) continue;
                        var det = new ArucoMarkerDetector();
                        det.ApplyCalibration(calibration, FrameWidth, FrameHeight);
                        channelDetectors[cvn] = det;
                    }

                    var first = channelNodes[0];
                    var src = first.FrameNode.Source;
                    if (src == null)
                    {
                        dbgSkipNullSource++;
                        Thread.Sleep(10);
                        goto SecondTick;
                    }

                    long frame = src.FrameProcessNumber;
                    if (frame == lastFrame)
                    {
                        dbgSkipSameFrame++;
                        Thread.Sleep(1);
                        goto SecondTick;
                    }
                    lastFrame = frame;
                    dbgLastFrameSeen = frame;

                    DateTime captureTime = DateTime.Now;

                    // Reference settings: Primary's if one exists, otherwise the first Split.
                    // All other instances inherit shared thresholds from the reference so the
                    // per-instance UI can stay limited to MarkerIds.
                    ArucoTimingSettings primarySettings = systems
                        .Where(sys => sys.Settings != null && sys.Settings.Role == TimingSystemRole.Primary)
                        .Select(sys => sys.ArucoSettings)
                        .FirstOrDefault()
                        ?? systems
                            .Where(sys => sys.Settings != null)
                            .Select(sys => sys.ArucoSettings)
                            .FirstOrDefault();

                    // Detector parameters are shared from the reference (Primary). Splits do not
                    // expose DetectMode in their UI, so their stored value (default Hybrid) must
                    // not influence the single detection pass — otherwise changing the Primary's
                    // mode away from Hybrid would have no effect.
                    ArucoDetectMode sharedMode = primarySettings?.DetectMode ?? ArucoDetectMode.Original;
                    double sharedEcRate = primarySettings?.ErrorCorrectionRate ?? 0.0;
                    int sharedHybridDist = primarySettings?.HybridDistanceThreshold ?? 0;
                    dbgLastMode = sharedMode;
                    if (primarySettings == null) dbgPrimaryAbsent++;

                    // Overlay display flags track the Primary's settings.
                    if (primarySettings != null)
                    {
                        ArucoFrameOverlay.ShowBox = primarySettings.ShowMarkerBox;
                        ArucoFrameOverlay.ShowId = primarySettings.ShowMarkerId;
                        ArucoFrameOverlay.ShowSizePercent = primarySettings.ShowMarkerSizePercent;
                        ArucoFrameOverlay.ShowFps = primarySettings.ShowFps;
                        ArucoFrameOverlay.FlipTextVertical = primarySettings.CharacterFlipVertical;
                    }
                    bool multiThread = primarySettings?.UseMultiThreadDetection ?? true;

                    // Only detect on channels of pilots actually racing while a race is running
                    // (start -> end, including abort). Unassigned channels typically show RF
                    // static, which is extremely expensive for ArUco (huge contour counts), so
                    // feed those a black frame instead. Outside a running race, detect on all.
                    var raceManager = channelNodes[0].EventManager?.RaceManager;
                    bool raceRunning = raceManager != null && raceManager.RaceRunning;
                    var currentRace = raceManager?.CurrentRace;

                    // Phase 1: collect BGRA buffers from each FrameNodeThumb sequentially
                    // (GetColorData can race with the UI draw thread; do it once up front).
                    var inputs = new (ChannelVideoNode cvn, byte[] bgra)[channelNodes.Length];
                    for (int i = 0; i < channelNodes.Length; i++)
                    {
                        var cvn = channelNodes[i];

                        // Black frame (zero buffer) for non-racing / unassigned channels during a race.
                        if (raceRunning &&
                            !(cvn.Pilot != null && currentRace != null && currentRace.HasPilot(cvn.Pilot)))
                        {
                            inputs[i] = (cvn, new byte[FrameWidth * FrameHeight * 4]);
                            continue;
                        }

                        Color[] pixels = cvn.FrameNode.GetColorData();
                        if (pixels == null)
                        {
                            dbgBgraNullPixels++;
                            inputs[i] = (cvn, null);
                            continue;
                        }

                        Tools.Size size = cvn.FrameNode.Size;
                        if (size.Width != FrameWidth || size.Height != FrameHeight ||
                            pixels.Length != FrameWidth * FrameHeight)
                        {
                            dbgBgraBadSize++;
                            inputs[i] = (cvn, null);
                            continue;
                        }

                        dbgBgraOk++;
                        byte[] bgra = new byte[pixels.Length * 4];
                        for (int k = 0; k < pixels.Length; k++)
                        {
                            var p = pixels[k];
                            bgra[k * 4 + 0] = p.B;
                            bgra[k * 4 + 1] = p.G;
                            bgra[k * 4 + 2] = p.R;
                            bgra[k * 4 + 3] = p.A;
                        }
                        inputs[i] = (cvn, bgra);
                    }

                    // Phase 2: run detection in parallel (or sequentially) across channels.
                    var channelResults = new List<MarkerDetection>[channelNodes.Length];

                    void DetectOne(int ch)
                    {
                        var input = inputs[ch];
                        if (input.bgra == null) return;
                        if (!channelDetectors.TryGetValue(input.cvn, out var det)) return;
                        channelResults[ch] = det.Detect(input.bgra, FrameWidth, FrameHeight,
                            sharedMode, sharedEcRate, sharedHybridDist);
                    }

                    if (multiThread && channelNodes.Length > 1)
                    {
                        Parallel.For(0, channelNodes.Length, DetectOne);
                    }
                    else
                    {
                        for (int ch = 0; ch < channelNodes.Length; ch++)
                            DetectOne(ch);
                    }

                    // Phase 3: publish overlay + dispatch detections to timing systems.
                    for (int ch = 0; ch < channelNodes.Length; ch++)
                    {
                        var cvn = inputs[ch].cvn;
                        var detections = channelResults[ch];
                        if (detections == null) continue;

                        dbgTotalDetections += detections.Count;
                        foreach (var d in detections)
                        {
                            if (d.Id >= 0 && d.Id < dbgPerIdCount.Length)
                                dbgPerIdCount[d.Id]++;
                        }

                        // Cache per full-resolution FrameSource so the overlay hook can find them.
                        FrameSource source = cvn.FrameNode.Source;
                        if (source != null)
                        {
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
                            };
                            // Key per-channel so multiple channels sharing one FrameSource
                            // (e.g. 2x2 capture) each keep their own crop cached.
                            ArucoFrameOverlay.SetLatestDetections(source, cvn, cached);
                        }

                        foreach (var sys in systems)
                        {
                            var s = sys.ArucoSettings;
                            if (s == null) continue;

                            // Every instance inherits shared thresholds from the reference
                            // (Primary, or lowest-index Split when no Primary exists).
                            ArucoTimingSettings effective = primarySettings ?? s;

                            var wantedIds = s.EffectiveMarkerIds;
                            int matching = 0;
                            double maxArea = 0;
                            double minAreaPx = (double)FrameWidth * FrameHeight * (effective.MinMarkerPercent / 100.0);
                            dbgLastMinAreaPx = minAreaPx;

                            foreach (var d in detections)
                            {
                                if (!wantedIds.Contains(d.Id)) continue;
                                if (d.AreaPx < minAreaPx) continue;
                                matching++;
                                if (d.AreaPx > maxArea) maxArea = d.AreaPx;
                            }

                            if (matching > 0) dbgMatchingPositive++;

                            sys.ReportMarkerCount(cvn.Channel.Frequency, matching, (int)maxArea, captureTime,
                                effective.MarkerThreshold, effective.FlickerLengthMs);
                        }
                    }

                    fpsIter++;

                SecondTick:
                    if (fpsSw.ElapsedMilliseconds >= 1000)
                    {
                        ArucoFrameOverlay.DetectionFps = (float)(fpsIter * 1000.0 / fpsSw.Elapsed.TotalMilliseconds);

                        // [ArUco-Debug] one-line summary every ~1s so we can answer:
                        //   * is the loop even running? (loopIter)
                        //   * what's gating it? (skip*)
                        //   * are frames flowing? (bgraOk vs bgraNullPixels/bgraBadSize)
                        //   * are markers being seen? (totalDet, perId)
                        //   * are matches surviving the wantedIds + minAreaPx filter? (matching>0)
                        Logger.TimingLog.Log(this,
                            "[ArUco-Debug] 1s: loop=" + dbgLoopIter
                            + " fps=" + fpsIter
                            + " channels=" + dbgLastChannelCount
                            + " lastFrame=" + dbgLastFrameSeen
                            + " skip[noSys=" + dbgSkipNoSystems
                            + ",noCh=" + dbgSkipNoChannels
                            + ",nullSrc=" + dbgSkipNullSource
                            + ",sameFrame=" + dbgSkipSameFrame + "]"
                            + " bgra[ok=" + dbgBgraOk
                            + ",nullPx=" + dbgBgraNullPixels
                            + ",badSize=" + dbgBgraBadSize + "]"
                            + " mode=" + dbgLastMode
                            + " primaryAbsent=" + dbgPrimaryAbsent
                            + " minAreaPx=" + dbgLastMinAreaPx.ToString("F1")
                            + " totalDet=" + dbgTotalDetections
                            + " perId=[" + dbgPerIdCount[0] + "," + dbgPerIdCount[1] + "," + dbgPerIdCount[2] + "," + dbgPerIdCount[3] + "]"
                            + " matching>0=" + dbgMatchingPositive);

                        fpsIter = 0;
                        fpsSw.Restart();

                        // reset 1-second aggregates
                        dbgLoopIter = 0;
                        dbgSkipNoSystems = dbgSkipNoChannels = dbgSkipNullSource = dbgSkipSameFrame = 0;
                        dbgBgraOk = dbgBgraNullPixels = dbgBgraBadSize = 0;
                        dbgTotalDetections = 0;
                        for (int i = 0; i < dbgPerIdCount.Length; i++) dbgPerIdCount[i] = 0;
                        dbgMatchingPositive = 0;
                        dbgPrimaryAbsent = 0;
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
