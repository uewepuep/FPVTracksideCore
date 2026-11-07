using Timing.Aruco;
using Xunit;

namespace Timing.Tests.Aruco
{
    public class DetectionImageTests
    {
        private const int Width = 8;
        private const int Height = 6;

        /// <summary>A frame whose pixel (x, y) is (x, y, 100 + x, 200 + y) in byte order.</summary>
        private static byte[] Frame()
        {
            var pixels = new byte[Width * Height * 4];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int i = (y * Width + x) * 4;
                    pixels[i + 0] = (byte)x;
                    pixels[i + 1] = (byte)y;
                    pixels[i + 2] = (byte)(100 + x);
                    pixels[i + 3] = (byte)(200 + y);
                }
            }
            return pixels;
        }

        /// <summary>
        /// The right half of the shown frame, at its own size so no pixel is scaled: shown pixel
        /// (x, y) of the crop must be the captured pixel the overlay maps it to.
        /// </summary>
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void FromCapture_CropsTheShownFrame_AsTheOverlayMapsIt(bool flipY, bool mirrorX)
        {
            const int cropX = 4, cropY = 2, cropW = 4, cropH = 3;

            byte[] result = DetectionImage.FromCapture(Frame(), Width, Height, false,
                cropX / (double)Width, cropY / (double)Height, cropW / (double)Width, cropH / (double)Height,
                flipY, mirrorX, cropW, cropH);

            for (int y = 0; y < cropH; y++)
            {
                for (int x = 0; x < cropW; x++)
                {
                    int shownX = cropX + x;
                    int shownY = cropY + y;
                    int capturedX = mirrorX ? Width - 1 - shownX : shownX;
                    int capturedY = flipY ? Height - 1 - shownY : shownY;

                    int i = (y * cropW + x) * 4;
                    Assert.Equal(capturedX, result[i + 0]);
                    Assert.Equal(capturedY, result[i + 1]);
                }
            }
        }

        [Fact]
        public void FromCapture_RgbaBecomesBgra()
        {
            byte[] result = DetectionImage.FromCapture(Frame(), Width, Height, true,
                0, 0, 1, 1, false, false, Width, Height);

            // Captured pixel (3, 2) is R=3 G=2 B=103 A=202.
            int i = (2 * Width + 3) * 4;
            Assert.Equal(103, result[i + 0]);
            Assert.Equal(2, result[i + 1]);
            Assert.Equal(3, result[i + 2]);
            Assert.Equal(202, result[i + 3]);
        }

        [Fact]
        public void FromCapture_ScalesToTheDetectionSize()
        {
            byte[] result = DetectionImage.FromCapture(Frame(), Width, Height, false,
                0, 0, 1, 1, false, false, 4, 3);

            Assert.Equal(4 * 3 * 4, result.Length);
        }

        [Fact]
        public void FromCapture_EmptyCropOrShortFrame_IsNull()
        {
            Assert.Null(DetectionImage.FromCapture(Frame(), Width, Height, false,
                0, 0, 0, 1, false, false, 4, 3));
            Assert.Null(DetectionImage.FromCapture(new byte[10], Width, Height, false,
                0, 0, 1, 1, false, false, 4, 3));
        }
    }
}
