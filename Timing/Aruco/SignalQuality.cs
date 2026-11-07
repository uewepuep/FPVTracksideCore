using System.Runtime.InteropServices;
using OpenCvSharp;

namespace Timing.Aruco
{
    /// <summary>
    /// Whether a frame carries video at all, independent of the marker type detected on it.
    /// </summary>
    public static class SignalQuality
    {
        /// <summary>
        /// Measures how much contrast survives a heavy blur — the raspi4eyes signal-quality metric.
        /// RF snow has no spatial correlation so blurring wipes out its contrast (ratio ~0),
        /// while real video keeps its large-scale light/dark structure (ratio high, ~0.6-0.85).
        /// A flat single-colour frame (black/blue screen) returns 0 (no signal).
        /// Returns 1.0 (treat as valid video) on any failure so detection is never silently lost.
        /// </summary>
        public static double SignalRatio(byte[] bgra, int width, int height)
        {
            if (bgra == null || bgra.Length < width * height * 4) return 1.0;
            try
            {
                using (Mat bgraMat = new Mat(height, width, MatType.CV_8UC4))
                using (Mat gray = new Mat())
                using (Mat small = new Mat())
                {
                    Marshal.Copy(bgra, 0, bgraMat.Data, bgra.Length);
                    Cv2.CvtColor(bgraMat, gray, ColorConversionCodes.BGRA2GRAY);
                    // Downscale to make the measurement cheap and to suppress per-pixel jitter.
                    Cv2.Resize(gray, small, new OpenCvSharp.Size(80, 60));

                    Cv2.MeanStdDev(small, out _, out Scalar stdRaw);
                    double valRaw = stdRaw.Val0;
                    // Almost no contrast at all => single colour (no signal / blue screen).
                    if (valRaw < 3.0) return 0.0;

                    using (Mat blurred = new Mat())
                    {
                        Cv2.GaussianBlur(small, blurred, new OpenCvSharp.Size(15, 15), 0);
                        Cv2.MeanStdDev(blurred, out _, out Scalar stdBlur);
                        return stdBlur.Val0 / valRaw;
                    }
                }
            }
            catch
            {
                return 1.0;
            }
        }

        /// <summary>
        /// Returns true when the BGRA frame looks like a lost signal — RF snow or a flat no-signal
        /// screen — i.e. its <see cref="SignalRatio"/> is below <paramref name="threshold"/>.
        /// </summary>
        public static bool IsLostSignal(byte[] bgra, int width, int height, double threshold)
        {
            return SignalRatio(bgra, width, height) < threshold;
        }
    }
}
