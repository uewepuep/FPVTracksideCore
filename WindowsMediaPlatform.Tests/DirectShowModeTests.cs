using System;
using WindowsMediaPlatform.DirectShow;
using Xunit;

namespace WindowsMediaPlatform.Tests
{
    /// <summary>
    /// Mode rules of <see cref="DirectShowDeviceFrameSource"/>, checked against the subtypes and
    /// AvgTimePerFrame values a Blackmagic DeckLink Quad HDMI Recorder advertises through its WDM driver.
    /// </summary>
    public class DirectShowModeTests
    {
        private static readonly Guid NV12 = new Guid("3231564e-0000-0010-8000-00aa00389b71");
        private static readonly Guid HDYC = new Guid("43594448-0000-0010-8000-00aa00389b71");
        private static readonly Guid V210 = new Guid("30313276-0000-0010-8000-00aa00389b71");
        private static readonly Guid R210 = new Guid("30313272-0000-0010-8000-00aa00389b71");
        private static readonly Guid ARGB32 = new Guid("773c9ac0-3274-11d0-b724-00aa006c1a01");

        [Theory]
        [InlineData("59565955-0000-0010-8000-00aa00389b71")]
        [InlineData("32595559-0000-0010-8000-00aa00389b71")]
        public void AcceptsPackedYuvSubTypes(string subType)
        {
            Assert.True(DirectShowDeviceFrameSource.IsSupportedSubType(new Guid(subType)));
        }

        [Fact]
        public void RejectsSubTypesWithoutRgbConversion()
        {
            Assert.False(DirectShowDeviceFrameSource.IsSupportedSubType(NV12));
            Assert.False(DirectShowDeviceFrameSource.IsSupportedSubType(HDYC));
            Assert.False(DirectShowDeviceFrameSource.IsSupportedSubType(V210));
            Assert.False(DirectShowDeviceFrameSource.IsSupportedSubType(R210));
        }

        /// <summary>
        /// The Blackmagic WDM driver only offers HD and UHD modes as HDYC, v210, r210 or ARGB32.
        /// </summary>
        [Fact]
        public void AcceptsArgb32()
        {
            Assert.True(DirectShowDeviceFrameSource.IsSupportedSubType(ARGB32));
        }

        [Fact]
        public void NamesDirectShowArgb32()
        {
            Assert.Equal("ARGB32", MediaFoundation.MFHelper.GetFormat(ARGB32));
        }

        [Theory]
        [InlineData(400000L, 25f)]
        [InlineData(333333L, 30f)]
        [InlineData(200000L, 50f)]
        public void ComputesWholeFrameRates(long avgTimePerFrame, float expected)
        {
            Assert.Equal(expected, DirectShowDeviceFrameSource.GetFrameRate(avgTimePerFrame), 2);
        }

        [Theory]
        [InlineData(166667L, 60f)]
        [InlineData(333667L, 29.97f)]
        [InlineData(166833L, 59.94f)]
        [InlineData(417083L, 23.976f)]
        public void KeepsFractionalFrameRates(long avgTimePerFrame, float expected)
        {
            Assert.Equal(expected, DirectShowDeviceFrameSource.GetFrameRate(avgTimePerFrame), 2);
        }
    }
}
