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
        /// Per-channel state: latest geometry (crop/flip/mirror/sizes) plus a per-marker-ID
        /// dictionary of the most recent detection and when it was seen. Aging out markers by ID
        /// rather than by whole-frame lets a brief miss on one of N markers preserve the others.
        /// </summary>
        private sealed class ChannelEntry
        {
            public Cached Geometry;
            public readonly Dictionary<int, HeldMarker> Markers = new Dictionary<int, HeldMarker>();
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
        public static volatile bool Enabled = false;

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
                if (cached.Detections != null)
                {
                    foreach (var d in cached.Detections)
                    {
                        if (d == null || d.Corners == null) continue;
                        entry.Markers[d.Id] = new HeldMarker { Detection = d, SeenAt = now };
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
            List<int> expired = null;
            lock (entry.Markers)
            {
                foreach (var kv in entry.Markers)
                {
                    double ageMs = (now - kv.Value.SeenAt).TotalMilliseconds;
                    if (ageMs > OverlayHoldMs)
                    {
                        if (expired == null) expired = new List<int>();
                        expired.Add(kv.Key);
                    }
                    else
                    {
                        if (active == null) active = new List<MarkerDetection>();
                        active.Add(kv.Value.Detection);
                    }
                }
                if (expired != null)
                {
                    foreach (var k in expired) entry.Markers.Remove(k);
                }
            }
            return active;
        }

        private static void OnBeforeFrame(ImageServer.FrameSource source, byte[] buffer)
        {
            if (!Enabled || buffer == null) return;
            if (!(ShowBox || ShowId || ShowSizePercent || ShowFps)) return;

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
                    if (!hasMarkers && !ShowFps) continue;
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
            if (!(ShowBox || ShowId || ShowSizePercent || ShowFps)) return;

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
                if (!hasMarkers && !ShowFps) continue;
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
        /// in-place (when <see cref="FlipTextVertical"/> is true). Position of <paramref name="org"/>
        /// is preserved; only the glyph shape is inverted top-to-bottom.
        /// </summary>
        /// <remarks>
        /// Vertical flip is performed by drawing into a tight ROI on <paramref name="mat"/> and
        /// applying <see cref="Cv2.Flip"/> with <see cref="FlipMode.X"/>. Background pixels within
        /// that small ROI are also flipped — invisible for typical glyph-sized rectangles.
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

            // Use the wider of (outline, main) for bounding-rect calculation so the flipped
            // ROI covers both passes. Pad a few pixels for AA fringe.
            double measureScale = hasOutline ? outlineScale : scale;
            int measureThickness = hasOutline ? outlineThickness : thickness;
            OpenCvSharp.Size textSize = Cv2.GetTextSize(text, font, measureScale, measureThickness, out int baseline);
            const int padding = 2;
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

            Rect roi = new Rect(x0, y0, x1 - x0, y1 - y0);
            using (Mat sub = new Mat(mat, roi))
            {
                Point adjOrg = new Point(org.X - roi.X, org.Y - roi.Y);
                if (hasOutline)
                    Cv2.PutText(sub, text, adjOrg, font, outlineScale, Scalar.Black, outlineThickness, lineType);
                Cv2.PutText(sub, text, adjOrg, font, scale, color, thickness, lineType);
                Cv2.Flip(sub, sub, FlipMode.X);
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
