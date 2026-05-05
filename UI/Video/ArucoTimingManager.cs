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

            if (!HasAruco()) return;

            EnforceSinglePrimary();

            run = true;
            thread = new Thread(Process)
            {
                Name = "ArucoTimingManager",
                IsBackground = true
            };
            thread.Start();
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
                    "ArUco: only one Primary is allowed; demoting Marker(s) " + ids + " to Split.");
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
                    Logger.TimingLog.Log(this, "ArUco: calibration loaded from " + calPath
                        + " (ref " + calibration.ReferenceWidth + "x" + calibration.ReferenceHeight + ")");
                else
                    Logger.TimingLog.Log(this, "ArUco: calibration NOT loaded (" + (calError ?? "unknown") + ") — Corrected/Hybrid passes will be skipped. Path: " + calPath);

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

                    // Overlay display flags track the Primary's settings.
                    if (primarySettings != null)
                    {
                        ArucoFrameOverlay.ShowBox = primarySettings.ShowMarkerBox;
                        ArucoFrameOverlay.ShowId = primarySettings.ShowMarkerId;
                        ArucoFrameOverlay.ShowSizePercent = primarySettings.ShowMarkerSizePercent;
                        ArucoFrameOverlay.ShowFps = primarySettings.ShowFps;
                    }
                    bool multiThread = primarySettings?.UseMultiThreadDetection ?? true;

                    // Phase 1: collect BGRA buffers from each FrameNodeThumb sequentially
                    // (GetColorData can race with the UI draw thread; do it once up front).
                    var inputs = new (ChannelVideoNode cvn, byte[] bgra)[channelNodes.Length];
                    for (int i = 0; i < channelNodes.Length; i++)
                    {
                        var cvn = channelNodes[i];
                        Color[] pixels = cvn.FrameNode.GetColorData();
                        if (pixels == null)
                        {
                            inputs[i] = (cvn, null);
                            continue;
                        }

                        Tools.Size size = cvn.FrameNode.Size;
                        if (size.Width != FrameWidth || size.Height != FrameHeight ||
                            pixels.Length != FrameWidth * FrameHeight)
                        {
                            inputs[i] = (cvn, null);
                            continue;
                        }

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

                            foreach (var d in detections)
                            {
                                if (!wantedIds.Contains(d.Id)) continue;
                                if (d.AreaPx < minAreaPx) continue;
                                matching++;
                                if (d.AreaPx > maxArea) maxArea = d.AreaPx;
                            }

                            sys.ReportMarkerCount(cvn.Channel.Frequency, matching, (int)maxArea, captureTime,
                                effective.MarkerThreshold, effective.FlickerLengthMs);
                        }
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
