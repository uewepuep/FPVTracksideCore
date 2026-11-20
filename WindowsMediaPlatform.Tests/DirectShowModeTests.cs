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

        [Theory]
        [InlineData(400000L, 25f)]
        [InlineData(333333L, 30f)]
        [InlineData(200000L, 50f)]
        public void ComputesWholeFrameRates(long avgTimePerFrame, float expected)
        {
            Assert.Equal(expected, DirectShowDeviceFrameSource.GetFrameRate(avgTimePerFrame), 2);
        }
    }
}
