using Microsoft.Xna.Framework.Graphics;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Timing.Aruco;
using Tools;

namespace UI.Video
{
    /// <summary>
    /// Burns ArUco detection overlays (polyline box, marker ID, size %) directly into the raw
    /// BGRA/RGBA frame buffer just before it is handed to both display and recording. Keeps the
    /// overlay visible in the live Channel view AND baked into the replay recording.
    /// </summary>
    internal static class ArucoFrameOverlay
    {
        public class Cached
        {
            public IReadOnlyList<MarkerDetection> Detections;
            public int DetectionWidth;
            public int DetectionHeight;

            /// <summary>Crop region on the full-resolution source, normalized [0,1].</summary>
            public float CropRelX;
            public float CropRelY;
            public float CropRelW;
            public float CropRelH;

            /// <summary>Vertical flip applied when drawing into the detection RT.</summary>
            public bool FlipY;
            /// <summary>Horizontal mirror applied when drawing into the detection RT.</summary>
            public bool MirrorX;

            /// <summary>
            /// Latest signal ratio for this channel (see ArucoMarkerDetector.SignalRatio).
            /// NaN when not measured (e.g. unassigned channel) — the overlay then draws nothing.
            /// </summary>
            public double SignalRatio = double.NaN;
        }

        /// <summary>
        /// How long (ms) a marker keeps being drawn after its last detection. Bridges 1-frame
        /// detection misses (avoids overlay flicker) while still erasing the box once the marker
        /// truly leaves the scene or the detection thread stalls.
        /// </summary>
        public const int OverlayHoldMs = 150;

        private struct HeldMarker
        {
            public MarkerDetection Detection;
            public DateTime SeenAt;
        }

        /// <summary>
        /// Per-channel state: latest geometry (crop/flip/mirror/sizes) plus a snapshot of held
        /// markers from the most recent detection update. The list is fully replaced on every
        /// update — no per-instance bridge across detection misses.
        /// </summary>
        private sealed class ChannelEntry
        {
            public Cached Geometry;
            public readonly List<HeldMarker> Markers = new List<HeldMarker>();
        }

        // Multiple channels can share the same FrameSource (e.g. a single 2x2 capture split
        // by RelativeSourceBounds into 4 pilot views). We therefore index first by FrameSource
        // then by an opaque channel key so each channel's crop is remembered independently.
        private static readonly ConcurrentDictionary<ImageServer.FrameSource, ConcurrentDictionary<object, ChannelEntry>> cache
            = new ConcurrentDictionary<ImageServer.FrameSource, ConcurrentDictionary<object, ChannelEntry>>();

        private static volatile bool registered;
        private static readonly object registerLock = new object();

        public static volatile bool ShowBox = true;
        public static volatile bool ShowId = true;
        public static volatile bool ShowSizePercent = true;
        public static volatile bool ShowFps = false;
        public static volatile bool ShowSignalRatio = false;
        public static volatile bool Enabled = false;

        /// <summary>
        /// Lost-signal threshold (shared, from the reference ArUco settings), drawn alongside each
        /// channel's measured signal ratio so "measured/threshold" reads like the FPS overlay.
        /// </summary>
        public static volatile float LostSignalThreshold = 0.4f;

        /// <summary>
        /// When true, overlay text (ID / size % / FPS) is rendered upside-down at the same on-screen
        /// position. Useful for cameras/displays mounted in a vertically-inverted orientation.
        /// </summary>
        public static volatile bool FlipTextVertical = true;

        /// <summary>
        /// Latest detection iterations per second, published by <see cref="UI.Video.ArucoTimingManager"/>
        /// once per ~1 s. Drawn in each channel's overlay when <see cref="ShowFps"/> is on.
        /// </summary>
        public static volatile float DetectionFps = 0f;

        public static void EnsureRegistered()
        {
            if (registered) return;
            lock (registerLock)
            {
                if (registered) return;
                ImageServer.FrameSource.BeforeFrameDispatch += OnBeforeFrame;
                ImageServer.FrameSource.BeforeFrameDispatchPtr += OnBeforeFramePtr;
                registered = true;
            }
        }

        public static void ClearAll()
        {
            cache.Clear();
        }

        public static void SetLatestDetections(ImageServer.FrameSource source, object channelKey, Cached cached)
        {
            if (source == null || channelKey == null || cached == null) return;
            var inner = cache.GetOrAdd(source, _ => new ConcurrentDictionary<object, ChannelEntry>());
            var entry = inner.GetOrAdd(channelKey, _ => new ChannelEntry());

            entry.Geometry = cached;

            DateTime now = DateTime.UtcNow;
            lock (entry.Markers)
            {
                // Snapshot mode: the detector's merged list is the source of truth for the frame.
                // Per-marker bridging across detection misses is intentionally not done — every
                // detection update fully replaces the held set, so a missed marker disappears for
                // one detection cycle instead of being held with a stale outline.
                entry.Markers.Clear();
                if (cached.Detections != null)
                {
                    foreach (var d in cached.Detections)
                    {
                        if (d == null || d.Corners == null || d.Corners.Length == 0) continue;
                        entry.Markers.Add(new HeldMarker { Detection = d, SeenAt = now });
                    }
                }
            }
        }

        /// <summary>
        /// Snapshot the held markers for a channel, dropping anything older than <see cref="OverlayHoldMs"/>.
        /// Returns null when nothing should be drawn.
        /// </summary>
        private static List<MarkerDetection> CollectActive(ChannelEntry entry, DateTime now)
        {
            List<MarkerDetection> active = null;
            lock (entry.Markers)
            {
                for (int i = entry.Markers.Count - 1; i >= 0; i--)
                {
                    double ageMs = (now - entry.Markers[i].SeenAt).TotalMilliseconds;
                    if (ageMs > OverlayHoldMs)
                    {
                        entry.Markers.RemoveAt(i);
                    }
                    else
                    {
                        if (active == null) active = new List<MarkerDetection>();
                        active.Add(entry.Markers[i].Detection);
                    }
                }
            }
            return active;
        }

        private static void OnBeforeFrame(ImageServer.FrameSource source, byte[] buffer)
        {
            if (!Enabled || buffer == null) return;
            if (!(ShowBox || ShowId || ShowSizePercent || ShowFps || ShowSignalRatio)) return;

            int w = source.FrameWidth;
            int h = source.FrameHeight;
            if (w <= 0 || h <= 0 || buffer.Length < w * h * 4) return;
            if (!cache.TryGetValue(source, out var inner) || inner.IsEmpty) return;

            DateTime now = DateTime.UtcNow;
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr data = handle.AddrOfPinnedObject();
                foreach (var entry in inner.Values)
                {
                    if (entry?.Geometry == null) continue;
                    var active = CollectActive(entry, now);
                    bool hasMarkers = active != null && active.Count > 0;
                    if (!hasMarkers && !ShowFps && !ShowSignalRatio) continue;
                    DrawInto(data, w, h, source.FrameFormat, entry.Geometry, active);
                }
            }
            finally
            {
                handle.Free();
            }
        }

        private static void OnBeforeFramePtr(ImageServer.FrameSource source, IntPtr buffer, int length)
        {
            if (!Enabled || buffer == IntPtr.Zero) return;
            if (!(ShowBox || ShowId || ShowSizePercent || ShowFps || ShowSignalRatio)) return;

            int w = source.FrameWidth;
            int h = source.FrameHeight;
            if (w <= 0 || h <= 0 || length < w * h * 4) return;
            if (!cache.TryGetValue(source, out var inner) || inner.IsEmpty) return;

            DateTime now = DateTime.UtcNow;
            foreach (var entry in inner.Values)
            {
                if (entry?.Geometry == null) continue;
                var active = CollectActive(entry, now);
                bool hasMarkers = active != null && active.Count > 0;
                if (!hasMarkers && !ShowFps && !ShowSignalRatio) continue;
                DrawInto(buffer, w, h, source.FrameFormat, entry.Geometry, active);
            }
        }

        private static void DrawInto(IntPtr data, int w, int h, SurfaceFormat format, Cached cached, List<MarkerDetection> detections)
        {
            // Map from detection (480x360) → source-pixel coords within the crop rectangle.
            double cropX = cached.CropRelX * w;
            double cropY = cached.CropRelY * h;
            double cropW = cached.CropRelW * w;
            double cropH = cached.CropRelH * h;
            if (cropW <= 0 || cropH <= 0) return;

            double sxPerDet = cropW / cached.DetectionWidth;
            double syPerDet = cropH / cached.DetectionHeight;

            int thickness = h >= 720 ? 6 : 4;
            double fontScale = h >= 720 ? 0.6 : 0.45;
            int textThickness = h >= 720 ? 2 : 1;

            try
            {
                using (Mat mat = Mat.FromPixelData(h, w, MatType.CV_8UC4, data))
                {
                    double detArea = (double)cached.DetectionWidth * cached.DetectionHeight;

                    if (ShowFps)
                    {
                        // Bottom-left of the channel's crop. Yellow with black outline for
                        // legibility against any feed colour. Mirror/flip applied so the text
                        // stays at the channel's visual bottom-left after burn-in.
                        string fpsText = DetectionFps.ToString("F0") + " fps";
                        double fxBase = cropX + 6;
                        double fyBase = cropY + cropH - 6;  // baseline near crop bottom
                        // Pull the baseline one text-height inward (toward the channel's
                        // centre) so the FPS label doesn't sit flush against the crop edge.
                        // Computed in pre-flip visual coords so the offset always moves
                        // toward centre regardless of MirrorX / FlipY.
                        OpenCvSharp.Size fpsTextSize = Cv2.GetTextSize(
                            fpsText, HersheyFonts.HersheySimplex,
                            fontScale * 1.2, textThickness + 2, out _);
                        fyBase -= fpsTextSize.Height;
                        if (cached.MirrorX) fxBase = w - 1 - fxBase;
                        if (cached.FlipY)   fyBase = h - 1 - fyBase;
                        Scalar fpsColor = format == SurfaceFormat.Color
                            ? new Scalar(255, 255, 0, 255)   // RGBA: yellow
                            : new Scalar(0, 255, 255, 255);  // BGRA: yellow
                        var fpsOrg = new Point((int)fxBase, (int)fyBase);
                        DrawOverlayText(mat, w, h, fpsText, fpsOrg,
                                        fontScale * 1.2, fpsColor, textThickness,
                                        outlineScale: fontScale * 1.2, outlineThickness: textThickness + 2);
                    }

                    // Signal-ratio readout: "measured/threshold" at the channel's centre-left,
                    // mirroring the FPS overlay style. Red when below threshold (frame treated as
                    // lost signal), else green. Skipped when this channel has no measurement (NaN).
                    if (ShowSignalRatio && !double.IsNaN(cached.SignalRatio))
                    {
                        double thr = LostSignalThreshold;
                        bool lostSignal = cached.SignalRatio < thr;
                        string signalText = "S " + cached.SignalRatio.ToString("F2") + "/" + thr.ToString("F2");
                        double nScale = fontScale * 1.2;
                        int nThick = textThickness;

                        OpenCvSharp.Size signalTextSize = Cv2.GetTextSize(
                            signalText, HersheyFonts.HersheySimplex, nScale, nThick + 2, out _);
                        double nxBase = cropX + 6;
                        double nyBase = cropY + cropH / 2 + signalTextSize.Height / 2.0; // vertical centre, left side
                        if (cached.MirrorX) nxBase = w - 1 - nxBase;
                        if (cached.FlipY)   nyBase = h - 1 - nyBase;

                        // red when lost signal, green when valid (channel-order aware).
                        Scalar signalColor = format == SurfaceFormat.Color
                            ? (lostSignal ? new Scalar(255, 60, 60, 255) : new Scalar(60, 255, 60, 255))   // RGBA
                            : (lostSignal ? new Scalar(60, 60, 255, 255) : new Scalar(60, 255, 60, 255));  // BGRA
                        var signalOrg = new Point((int)nxBase, (int)nyBase);
                        DrawOverlayText(mat, w, h, signalText, signalOrg,
                                        nScale, signalColor, nThick,
                                        outlineScale: nScale, outlineThickness: nThick + 2);
                    }

                    if (detections == null) return;

                    foreach (var d in detections)
                    {
                        if (d.Corners == null || d.Corners.Length < 3) continue;

                        var pts = new Point[d.Corners.Length];
                        double acx = 0, acy = 0;
                        for (int i = 0; i < d.Corners.Length; i++)
                        {
                            double dx = d.Corners[i].X;
                            double dy = d.Corners[i].Y;

                            double px = cropX + dx * sxPerDet;
                            double py = cropY + dy * syPerDet;

                            if (cached.MirrorX) px = w - 1 - px;
                            if (cached.FlipY)   py = h - 1 - py;

                            int ipx = (int)Math.Round(px);
                            int ipy = (int)Math.Round(py);
                            pts[i] = new Point(ipx, ipy);
                            acx += ipx;
                            acy += ipy;
                        }
                        acx /= d.Corners.Length;
                        acy /= d.Corners.Length;

                        Scalar color = ColorFor(d.Source, format);

                        if (ShowBox)
                        {
                            Cv2.Polylines(mat, new[] { pts }, true, color, thickness, LineTypes.Link8);
                        }

                        string label = null;
                        if (ShowId && ShowSizePercent)
                        {
                            double pct = detArea > 0 ? (d.AreaPx / detArea) * 100.0 : 0;
                            label = "ID:" + d.Id + "  (" + pct.ToString("F1") + "%)";
                        }
                        else if (ShowId)
                        {
                            label = "ID:" + d.Id;
                        }
                        else if (ShowSizePercent)
                        {
                            double pct = detArea > 0 ? (d.AreaPx / detArea) * 100.0 : 0;
                            label = pct.ToString("F1") + "%";
                        }

                        if (label != null)
                        {
                            var org = new Point((int)acx, (int)acy - 10);
                            DrawOverlayText(mat, w, h, label, org, fontScale, color, textThickness);
                        }
                    }
                }
            }
            catch
            {
                // Never let overlay errors take down the capture thread.
            }
        }

        /// <summary>
        /// Draws antialiased text on <paramref name="mat"/>, optionally with a black outline
        /// (when <paramref name="outlineThickness"/> &gt; 0) and optionally flipped vertically
        /// (when <see cref="FlipTextVertical"/> is true). Position of <paramref name="org"/>
        /// is preserved; only the glyph shape is inverted top-to-bottom.
        /// </summary>
        /// <remarks>
        /// Flip path: glyphs are rendered into an isolated transparent temp Mat, flipped, then
        /// alpha-composited onto the main mat. This keeps any pre-existing pixels under the
        /// text's bounding rect — marker outlines, underlying video — untouched, and avoids the
        /// AA-fringe ghosting that an in-place ROI flip would leave outside the tight rect.
        /// </remarks>
        private static void DrawOverlayText(Mat mat, int w, int h, string text, Point org,
                                            double scale, Scalar color, int thickness,
                                            double outlineScale = 0, int outlineThickness = 0)
        {
            const HersheyFonts font = HersheyFonts.HersheySimplex;
            const LineTypes lineType = LineTypes.AntiAlias;
            bool hasOutline = outlineThickness > 0;

            if (!FlipTextVertical)
            {
                if (hasOutline)
                    Cv2.PutText(mat, text, org, font, outlineScale, Scalar.Black, outlineThickness, lineType);
                Cv2.PutText(mat, text, org, font, scale, color, thickness, lineType);
                return;
            }

            // Measure with the wider of (outline, main) so the temp Mat covers both passes.
            // Padding generous enough to hold the AA fringe of the heaviest stroke we draw.
            double measureScale = hasOutline ? outlineScale : scale;
            int measureThickness = hasOutline ? outlineThickness : thickness;
            OpenCvSharp.Size textSize = Cv2.GetTextSize(text, font, measureScale, measureThickness, out int baseline);
            int padding = Math.Max(4, measureThickness + 2);
            int rectX = org.X - padding;
            int rectY = org.Y - textSize.Height - padding;
            int rectW = textSize.Width + padding * 2;
            int rectH = textSize.Height + baseline + padding * 2;

            // Clip to image bounds. Fall back to a non-flipped draw if the ROI vanishes.
            int x0 = Math.Max(0, rectX);
            int y0 = Math.Max(0, rectY);
            int x1 = Math.Min(w, rectX + rectW);
            int y1 = Math.Min(h, rectY + rectH);
            if (x1 - x0 <= 0 || y1 - y0 <= 0)
            {
                if (hasOutline)
                    Cv2.PutText(mat, text, org, font, outlineScale, Scalar.Black, outlineThickness, lineType);
                Cv2.PutText(mat, text, org, font, scale, color, thickness, lineType);
                return;
            }

            Rect rect = new Rect(x0, y0, x1 - x0, y1 - y0);
            // Adjust the glyph origin into the temp's local coords. When the rect was clipped at
            // the image edge, rect.X / rect.Y may differ from the unclipped rectX / rectY — using
            // rect.* here keeps the glyph centred correctly within whatever portion survived.
            Point adjOrg = new Point(org.X - rect.X, org.Y - rect.Y);
            using (Mat temp = new Mat(rect.Height, rect.Width, MatType.CV_8UC4, Scalar.All(0)))
            {
                if (hasOutline)
                    Cv2.PutText(temp, text, adjOrg, font, outlineScale, Scalar.Black, outlineThickness, lineType);
                Cv2.PutText(temp, text, adjOrg, font, scale, color, thickness, lineType);
                Cv2.Flip(temp, temp, FlipMode.X);
                AlphaCompositeOnto(mat, rect, temp);
            }
        }

        /// <summary>
        /// Premultiplied-alpha composite of <paramref name="src"/> (8UC4, glyph pixels pre-multiplied
        /// by AA coverage thanks to PutText into an all-zero buffer) onto the <paramref name="rect"/>
        /// portion of <paramref name="dst"/>. Equivalent to: dst_roi = src + dst_roi * (255 - src_alpha) / 255.
        /// </summary>
        private static void AlphaCompositeOnto(Mat dst, Rect rect, Mat src)
        {
            using (Mat alpha = new Mat())
            using (Mat invAlpha = new Mat())
            using (Mat invAlphaBgra = new Mat())
            using (Mat scaledBg = new Mat())
            using (Mat dstRoi = new Mat(dst, rect))
            {
                Cv2.ExtractChannel(src, alpha, 3);
                Cv2.BitwiseNot(alpha, invAlpha);  // 255 - alpha for 8-bit
                Cv2.Merge(new[] { invAlpha, invAlpha, invAlpha, invAlpha }, invAlphaBgra);
                Cv2.Multiply(dstRoi, invAlphaBgra, scaledBg, 1.0 / 255.0);
                Cv2.Add(scaledBg, src, dstRoi);
            }
        }

        /// <summary>
        /// Returns a Scalar whose channel order matches the buffer's <paramref name="format"/>.
        /// OpenCV treats a 4-channel Mat as generic bytes; channel 0 is the first byte in memory.
        /// <list type="bullet">
        /// <item>SurfaceFormat.Color (RGBA): byte[0]=R</item>
        /// <item>SurfaceFormat.Bgr32 (BGRX): byte[0]=B</item>
        /// </list>
        /// </summary>
        private static Scalar ColorFor(DetectionSource source, SurfaceFormat format)
        {
            byte r, g, b;
            switch (source)
            {
                case DetectionSource.Corrected: r = 255; g = 255; b = 0; break;   // yellow
                case DetectionSource.Both:      r = 0;   g = 255; b = 0; break;   // green
                case DetectionSource.Original:
                default:                         r = 0;   g = 0;   b = 255; break; // blue
            }

            if (format == SurfaceFormat.Color)
                return new Scalar(r, g, b, 255); // RGBA buffer: byte order R,G,B,A
            return new Scalar(b, g, r, 255);     // BGRA/BGRX buffer: byte order B,G,R,(X|A)
        }
    }
}
