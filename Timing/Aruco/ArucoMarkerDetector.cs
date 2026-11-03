using System;
using System.Collections.Generic;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using Tools;

namespace Timing.Aruco
{
    public enum DetectionSource
    {
        Original,    // detected on raw frame (TVPAS2: blue)
        Corrected,   // detected on undistorted frame only (TVPAS2: yellow)
        Both         // detected on both in Hybrid mode (TVPAS2: green)
    }

    public class MarkerDetection
    {
        public int Id;
        public Point2f[] Corners;
        public double AreaPx;
        public DetectionSource Source;
    }

    public class ArucoMarkerDetector : IDisposable
    {
        private readonly Dictionary dict;
        private DetectorParameters parameters;
        private double cachedEcRate;
        private double cachedMinMarkerPercent = -1;
        private readonly double defaultMinMarkerPerimeterRate;

        public ArucoMarkerDetector()
        {
            // Use the standard Dict4X4_50 dictionary (IDs 0..49); TVPAS2-compatible setups
            // using IDs 0..3 keep working, but any ID in the dictionary is usable.
            dict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);
            parameters = new DetectorParameters();
            cachedEcRate = parameters.ErrorCorrectionRate;
            defaultMinMarkerPerimeterRate = parameters.MinMarkerPerimeterRate;
        }

        public List<MarkerDetection> Detect(DetectionFrame frame,
            ArucoDetectMode mode, double ecRate, int hybridDistPx, double minMarkerPercent)
        {
            int width = frame.Width;
            int height = frame.Height;

            if (Math.Abs(ecRate - cachedEcRate) > 0.001)
            {
                parameters.ErrorCorrectionRate = ecRate;
                cachedEcRate = ecRate;
            }

            // Reject small marker candidates before decoding so RF-snow contours don't cost CPU.
            // MinMarkerPercent is a marker-area threshold (% of frame); OpenCV's
            // MinMarkerPerimeterRate is a perimeter ratio to the larger image dimension. For a
            // square marker of area A, side = sqrt(A) and perimeter = 4*sqrt(A), so:
            //   rate = 4 * sqrt(width*height * pct/100) / max(width, height)
            // A threshold of 0 (filter off) restores OpenCV's default.
            if (Math.Abs(minMarkerPercent - cachedMinMarkerPercent) > 0.0001)
            {
                double rate = minMarkerPercent > 0
                    ? 4.0 * Math.Sqrt((double)width * height * (minMarkerPercent / 100.0)) / Math.Max(width, height)
                    : defaultMinMarkerPerimeterRate;
                parameters.MinMarkerPerimeterRate = rate;
                cachedMinMarkerPercent = minMarkerPercent;
            }

            List<MarkerDetection> results = new List<MarkerDetection>();

            Mat gray = frame.Gray;

            if (mode == ArucoDetectMode.Original || mode == ArucoDetectMode.Hybrid)
            {
                try { DetectInto(gray, results, DetectionSource.Original); }
                catch (Exception ex) { /* keep going; Corrected pass may still succeed */ Logger.TimingLog.LogException(this, ex); }
            }

            if (frame.CanCorrect && (mode == ArucoDetectMode.Corrected || mode == ArucoDetectMode.Hybrid))
            {
                try
                {
                    List<MarkerDetection> corrected = new List<MarkerDetection>();
                    DetectInto(frame.Corrected, corrected, DetectionSource.Corrected);

                    foreach (var c in corrected)
                    {
                        // Warp corners back to original image coords via inverse map lookup
                        frame.ToOriginal(c.Corners);
                        // Recompute area to match the warped-back corners so size % reflects
                        // the marker's actual footprint in the detection frame.
                        c.AreaPx = ContourArea(c.Corners);

                        if (mode == ArucoDetectMode.Hybrid)
                        {
                            var cc = Centroid(c.Corners);
                            MarkerDetection match = null;
                            foreach (var r in results)
                            {
                                if (r.Id != c.Id) continue;
                                if (Distance(Centroid(r.Corners), cc) < hybridDistPx)
                                {
                                    match = r;
                                    break;
                                }
                            }
                            if (match != null)
                            {
                                // Promote to Both; prefer corrected corners (more accurate geometry).
                                match.Source = DetectionSource.Both;
                                match.Corners = c.Corners;
                                match.AreaPx = c.AreaPx;
                            }
                            else
                            {
                                results.Add(c); // Source already = Corrected
                            }
                        }
                        else
                        {
                            results.Add(c);
                        }
                    }
                }
                catch (Exception ex) { /* undistort pipeline failed; return whatever Original found */ Logger.TimingLog.LogException(this, ex); }
            }

            return results;
        }

        private void DetectInto(Mat gray, List<MarkerDetection> into, DetectionSource source)
        {
            Point2f[][] corners;
            int[] ids;
            Point2f[][] rejected;
            CvAruco.DetectMarkers(gray, dict, out corners, out ids, parameters, out rejected);

            if (ids == null || corners == null) return;

            for (int i = 0; i < ids.Length; i++)
            {
                int id = ids[i];
                // Dict4X4_50 contains 50 markers, IDs 0..49.
                if (id < 0 || id > 49) continue;

                into.Add(new MarkerDetection
                {
                    Id = id,
                    Corners = corners[i],
                    AreaPx = ContourArea(corners[i]),
                    Source = source
                });
            }
        }

        private static double ContourArea(Point2f[] pts)
        {
            if (pts == null || pts.Length < 3) return 0;
            double area = 0;
            int n = pts.Length;
            for (int i = 0; i < n; i++)
            {
                var p1 = pts[i];
                var p2 = pts[(i + 1) % n];
                area += (p1.X * p2.Y) - (p2.X * p1.Y);
            }
            return Math.Abs(area) * 0.5;
        }

        private static Point2f Centroid(Point2f[] pts)
        {
            if (pts == null || pts.Length == 0) return new Point2f(0, 0);
            float sx = 0, sy = 0;
            foreach (var p in pts) { sx += p.X; sy += p.Y; }
            return new Point2f(sx / pts.Length, sy / pts.Length);
        }

        private static double Distance(Point2f a, Point2f b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public void Dispose()
        {
        }
    }
}
