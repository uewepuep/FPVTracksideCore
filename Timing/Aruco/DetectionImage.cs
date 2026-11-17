using System;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace Timing.Aruco
{
    /// <summary>
    /// Makes one channel's detection frame from a captured video frame: the channel's part of
    /// it, turned the way it is shown, scaled to the detection size, as BGRA.
    ///
    /// The geometry is the one the marker overlay maps detections back with: the crop is in
    /// shown coordinates, and a shown pixel (x, y) is the captured pixel
    /// (mirrorX ? w - 1 - x : x, flipY ? h - 1 - y : y).
    /// </summary>
    public static class DetectionImage
    {
        /// <param name="pixels">The captured frame, width x height pixels of 4 bytes, packed.</param>
        /// <param name="rgba">The frame is RGBA; otherwise BGRA (or BGR with a padding byte).</param>
        /// <param name="cropX">The channel's part of the shown frame, relative (0-1).</param>
        /// <returns>outWidth x outHeight BGRA pixels; null when the crop is empty.</returns>
        public static byte[] FromCapture(byte[] pixels, int width, int height, bool rgba,
            double cropX, double cropY, double cropW, double cropH,
            bool flipY, bool mirrorX, int outWidth, int outHeight)
        {
            if (pixels == null || pixels.Length < width * height * 4)
                return null;

            // Crop in shown pixels, rounded as the display rounds its source rectangle.
            int x = Math.Clamp((int)(width * cropX), 0, width);
            int y = Math.Clamp((int)(height * cropY), 0, height);
            int w = Math.Min((int)(width * cropW), width - x);
            int h = Math.Min((int)(height * cropH), height - y);
            if (w <= 0 || h <= 0)
                return null;

            // The same rows and columns of the captured frame, so only the crop is turned.
            if (mirrorX) x = width - x - w;
            if (flipY) y = height - y - h;

            byte[] result = new byte[outWidth * outHeight * 4];
            GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                using (Mat frame = Mat.FromPixelData(height, width, MatType.CV_8UC4, handle.AddrOfPinnedObject()))
                using (Mat crop = new Mat(frame, new Rect(x, y, w, h)))
                using (Mat scaled = new Mat())
                using (Mat turned = new Mat())
                using (Mat bgra = Mat.FromPixelData(outHeight, outWidth, MatType.CV_8UC4, result))
                {
                    Cv2.Resize(crop, scaled, new Size(outWidth, outHeight), 0, 0, InterpolationFlags.Linear);

                    Mat shown = scaled;
                    if (flipY || mirrorX)
                    {
                        FlipMode mode = flipY && mirrorX ? FlipMode.XY : flipY ? FlipMode.X : FlipMode.Y;
                        Cv2.Flip(scaled, turned, mode);
                        shown = turned;
                    }

                    if (rgba)
                        Cv2.CvtColor(shown, bgra, ColorConversionCodes.RGBA2BGRA);
                    else
                        shown.CopyTo(bgra);
                }
            }
            finally
            {
                handle.Free();
            }
            return result;
        }
    }
}
