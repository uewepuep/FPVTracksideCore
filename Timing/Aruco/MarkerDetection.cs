using OpenCvSharp;

namespace Timing.Aruco
{
    public enum DetectionSource
    {
        Original,    // detected on raw frame (TVPAS2: blue)
        Corrected,   // detected on undistorted frame only (TVPAS2: yellow)
        Both         // detected on both in Hybrid mode (TVPAS2: green)
    }

    /// <summary>One marker found on a frame, in the frame's coordinates, by any detector.</summary>
    public class MarkerDetection
    {
        public int Id;
        public Point2f[] Corners;
        public double AreaPx;
        public DetectionSource Source;
    }
}