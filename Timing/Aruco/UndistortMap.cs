using System;
using OpenCvSharp;
using Tools;

namespace Timing.Aruco
{
    /// <summary>
    /// Lens undistortion for one detection frame size, built from the camera calibration.
    /// Remaps a gray frame to the corrected view and maps points found there back to the
    /// original frame, so a detector can search the corrected view and still report original
    /// coordinates. Not tied to any one marker type.
    /// </summary>
    public class UndistortMap : IDisposable
    {
        private Mat mapX;
        private Mat mapY;

        public int Width { get; }
        public int Height { get; }

        private UndistortMap(Mat mapX, Mat mapY, int width, int height)
        {
            this.mapX = mapX;
            this.mapY = mapY;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// The map for frames of targetWidth x targetHeight, or null without a calibration or
        /// when building it fails (detection then simply runs without undistortion).
        /// </summary>
        public static UndistortMap Create(ArucoCalibration cal, int targetWidth, int targetHeight)
        {
            if (cal == null) return null;

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
                        return new UndistortMap(mx, my, targetWidth, targetHeight);
                    }
                }
            }
            catch (Exception ex)
            {
                // If calibration fails we want detection to continue without undistort,
                // not take down the whole Process thread.
                Logger.TimingLog.LogException(typeof(UndistortMap), ex);
                return null;
            }
        }

        /// <summary>The corrected view of a gray frame of this map's size. The caller disposes it.</summary>
        public Mat Remap(Mat gray)
        {
            Mat undist = new Mat();
            Cv2.Remap(gray, undist, mapX, mapY, InterpolationFlags.Linear);
            return undist;
        }

        /// <summary>
        /// Moves points found in the corrected view back to original frame coordinates, in place,
        /// via the map lookup. Points outside the frame are left as they are.
        /// </summary>
        public void ToOriginal(Point2f[] points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                int ux = (int)points[i].X;
                int uy = (int)points[i].Y;
                if (ux >= 0 && ux < Width && uy >= 0 && uy < Height)
                {
                    points[i].X = mapX.Get<float>(uy, ux);
                    points[i].Y = mapY.Get<float>(uy, ux);
                }
            }
        }

        public void Dispose()
        {
            mapX?.Dispose(); mapX = null;
            mapY?.Dispose(); mapY = null;
        }
    }
}
