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

        // Multiple channels can share the same FrameSource (e.g. a single 2x2 capture split
        // by RelativeSourceBounds into 4 pilot views). We therefore index first by FrameSource
        // then by an opaque channel key so each channel's crop is remembered independently.
        private static readonly ConcurrentDictionary<ImageServer.FrameSource, ConcurrentDictionary<object, Cached>> cache
            = new ConcurrentDictionary<ImageServer.FrameSource, ConcurrentDictionary<object, Cached>>();

        private static volatile bool registered;
        private static readonly object registerLock = new object();

        public static volatile bool ShowBox = true;
        public static volatile bool ShowId = true;
        public static volatile bool ShowSizePercent = true;
        public static volatile bool Enabled = false;

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
            var inner = cache.GetOrAdd(source, _ => new ConcurrentDictionary<object, Cached>());
            inner[channelKey] = cached;
        }

        private static void OnBeforeFrame(ImageServer.FrameSource source, byte[] buffer)
        {
            if (!Enabled || buffer == null) return;
            if (!(ShowBox || ShowId || ShowSizePercent)) return;

            int w = source.FrameWidth;
            int h = source.FrameHeight;
            if (w <= 0 || h <= 0 || buffer.Length < w * h * 4) return;
            if (!cache.TryGetValue(source, out var inner) || inner.IsEmpty) return;

            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr data = handle.AddrOfPinnedObject();
                foreach (var cached in inner.Values)
                {
                    if (cached?.Detections == null || cached.Detections.Count == 0) continue;
                    DrawInto(data, w, h, source.FrameFormat, cached);
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
            if (!(ShowBox || ShowId || ShowSizePercent)) return;

            int w = source.FrameWidth;
            int h = source.FrameHeight;
            if (w <= 0 || h <= 0 || length < w * h * 4) return;
            if (!cache.TryGetValue(source, out var inner) || inner.IsEmpty) return;

            foreach (var cached in inner.Values)
            {
                if (cached?.Detections == null || cached.Detections.Count == 0) continue;
                DrawInto(buffer, w, h, source.FrameFormat, cached);
            }
        }

        private static void DrawInto(IntPtr data, int w, int h, SurfaceFormat format, Cached cached)
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

                    foreach (var d in cached.Detections)
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
                            Cv2.PutText(mat, label, org, HersheyFonts.HersheySimplex, fontScale, color, textThickness, LineTypes.AntiAlias);
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
