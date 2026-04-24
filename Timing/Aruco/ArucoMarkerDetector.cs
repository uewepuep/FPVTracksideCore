using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Aruco;

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

        private Mat mapX;
        private Mat mapY;
        private int mapWidth;
        private int mapHeight;

        public ArucoMarkerDetector()
        {
            // Use the standard Dict4X4_50 dictionary; DetectInto restricts results to IDs 0..3
            // so that only the four markers used by TVPAS2-compatible setups are reported.
            dict = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);
            parameters = new DetectorParameters();
            cachedEcRate = parameters.ErrorCorrectionRate;
        }

        public bool HasCalibration => mapX != null && mapY != null;

        public void ApplyCalibration(ArucoCalibration cal, int targetWidth, int targetHeight)
        {
            DisposeMaps();
            if (cal == null) return;

            try
            {
                double sx = (double)targetWidth / cal.ReferenceWidth;
                double sy = (double)targetHeight / cal.ReferenceHeight;

                using (Mat cameraMatrix = new Mat(3, 3, MatType.CV_64FC1))
                using (Mat distMat = new Mat(1, cal.Dist.Length, MatType.CV_64FC1))
                using (Mat identity = new Mat())
                {
                    cameraMatrix.Set(0, 0, cal.Mtx[0, 0] * sx);
                    cameraMatrix.Set(0, 1, cal.Mtx[0, 1]);
                    cameraMatrix.Set(0, 2, cal.Mtx[0, 2] * sx);
                    cameraMatrix.Set(1, 0, cal.Mtx[1, 0]);
                    cameraMatrix.Set(1, 1, cal.Mtx[1, 1] * sy);
                    cameraMatrix.Set(1, 2, cal.Mtx[1, 2] * sy);
                    cameraMatrix.Set(2, 0, cal.Mtx[2, 0]);
                    cameraMatrix.Set(2, 1, cal.Mtx[2, 1]);
                    cameraMatrix.Set(2, 2, cal.Mtx[2, 2]);

                    for (int i = 0; i < cal.Dist.Length; i++)
                        distMat.Set(0, i, cal.Dist[i]);

                    OpenCvSharp.Size size = new OpenCvSharp.Size(targetWidth, targetHeight);
                    Rect validRoi;
                    using (Mat newMtx = Cv2.GetOptimalNewCameraMatrix(cameraMatrix, distMat, size, 1.0, size, out validRoi))
                    {
                        Mat mx = new Mat();
                        Mat my = new Mat();
                        // InitUndistortRectifyMap expects an identity/empty R — pass empty Mat, not null.
                        Cv2.InitUndistortRectifyMap(cameraMatrix, distMat, identity, newMtx, size, MatType.CV_32FC1, mx, my);

                        mapX = mx;
                        mapY = my;
                        mapWidth = targetWidth;
                        mapHeight = targetHeight;
                    }
                }
            }
            catch
            {
                // If calibration fails we want detection to continue without undistort,
                // not take down the whole Process thread.
                DisposeMaps();
            }
        }

        public List<MarkerDetection> Detect(byte[] bgra, int width, int height,
            ArucoDetectMode mode, double ecRate, int hybridDistPx)
        {

            if (Math.Abs(ecRate - cachedEcRate) > 0.001)
            {
                parameters.ErrorCorrectionRate = ecRate;
                cachedEcRate = ecRate;
            }

            List<MarkerDetection> results = new List<MarkerDetection>();

            using (Mat bgraMat = new Mat(height, width, MatType.CV_8UC4))
            using (Mat gray = new Mat())
            {
                Marshal.Copy(bgra, 0, bgraMat.Data, bgra.Length);
                Cv2.CvtColor(bgraMat, gray, ColorConversionCodes.BGRA2GRAY);

                if (mode == ArucoDetectMode.Original || mode == ArucoDetectMode.Hybrid)
                {
                    try { DetectInto(gray, results, DetectionSource.Original); }
                    catch { /* keep going; Corrected pass may still succeed */ }
                }

                bool canCorrect = HasCalibration && mapWidth == width && mapHeight == height;
                if (canCorrect && (mode == ArucoDetectMode.Corrected || mode == ArucoDetectMode.Hybrid))
                {
                    try
                    {
                        using (Mat undist = new Mat())
                        {
                            Cv2.Remap(gray, undist, mapX, mapY, InterpolationFlags.Linear);

                            List<MarkerDetection> corrected = new List<MarkerDetection>();
                            DetectInto(undist, corrected, DetectionSource.Corrected);

                            foreach (var c in corrected)
                            {
                                // Warp corners back to original image coords via inverse map lookup
                                for (int i = 0; i < c.Corners.Length; i++)
                                {
                                    int ux = (int)c.Corners[i].X;
                                    int uy = (int)c.Corners[i].Y;
                                    if (ux >= 0 && ux < width && uy >= 0 && uy < height)
                                    {
                                        c.Corners[i].X = mapX.Get<float>(uy, ux);
                                        c.Corners[i].Y = mapY.Get<float>(uy, ux);
                                    }
                                }
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
                    }
                    catch { /* undistort pipeline failed; return whatever Original found */ }
                }
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
                // Accept only IDs 0..3 (defensive even if the custom dictionary subset is active).
                if (id < 0 || id > 3) continue;

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

        private void DisposeMaps()
        {
            mapX?.Dispose(); mapX = null;
            mapY?.Dispose(); mapY = null;
        }

        public void Dispose()
        {
            DisposeMaps();
        }
    }
}
