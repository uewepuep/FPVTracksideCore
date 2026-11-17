using System;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace Timing.Aruco
{
    /// <summary>
    /// One channel's frame as the marker detectors see it: the BGRA pixels, the gray image and,
    /// with a calibration, the undistorted gray image. The gray and corrected images are made
    /// on first use and shared, so detectors running on the same frame convert it only once,
    /// and a detector that reads the pixels itself never needs OpenCV for this frame.
    /// </summary>
    public sealed class DetectionFrame : IDisposable
    {
        private readonly UndistortMap undistort;
        private Mat gray;
        private Mat corrected;

        public byte[] Bgra { get; }
        public int Width { get; }
        public int Height { get; }

        public DetectionFrame(byte[] bgra, int width, int height, UndistortMap undistort)
        {
            Bgra = bgra;
            Width = width;
            Height = height;
            this.undistort = undistort;
        }

        /// <summary>Whether <see cref="Corrected"/> is available (calibration of this frame's size).</summary>
        public bool CanCorrect => undistort != null && undistort.Width == Width && undistort.Height == Height;

        /// <summary>The frame in gray (8-bit, one channel).</summary>
        public Mat Gray
        {
            get
            {
                if (gray == null)
                {
                    using (Mat bgraMat = new Mat(Height, Width, MatType.CV_8UC4))
                    {
                        Marshal.Copy(Bgra, 0, bgraMat.Data, Bgra.Length);
                        Mat g = new Mat();
                        Cv2.CvtColor(bgraMat, g, ColorConversionCodes.BGRA2GRAY);
                        gray = g;
                    }
                }
                return gray;
            }
        }

        /// <summary>The undistorted gray frame; null when <see cref="CanCorrect"/> is false.</summary>
        public Mat Corrected
        {
            get
            {
                if (corrected == null && CanCorrect)
                    corrected = undistort.Remap(Gray);
                return corrected;
            }
        }

        /// <summary>Moves points found in <see cref="Corrected"/> back to this frame's coordinates, in place.</summary>
        public void ToOriginal(Point2f[] points)
        {
            if (CanCorrect)
                undistort.ToOriginal(points);
        }

        public void Dispose()
        {
            corrected?.Dispose(); corrected = null;
            gray?.Dispose(); gray = null;
        }
    }
}
