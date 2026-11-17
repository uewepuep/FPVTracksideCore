using System.Collections.Generic;
using Timing.Aruco;
using Xunit;

namespace Timing.Tests.Aruco
{
    public class ArucoRolesTests
    {
        private static ArucoTimingSettings Instance(TimingSystemRole role, string ids)
        {
            return new ArucoTimingSettings { Role = role, MarkerIds = ids };
        }

        [Fact]
        public void Reference_IsThePrimaryWhereverItIsListed()
        {
            var split = Instance(TimingSystemRole.Split, "1");
            var primary = Instance(TimingSystemRole.Primary, "0");

            var instances = new[] { split, primary };

            Assert.Same(primary, ArucoRoles.Primary(instances));
            Assert.Same(primary, ArucoRoles.Reference(instances));
        }

        [Fact]
        public void Reference_IsTheFirstSplitWithoutAPrimary()
        {
            var first = Instance(TimingSystemRole.Split, "1");
            var second = Instance(TimingSystemRole.Split, "2");

            var instances = new[] { null, first, second };

            Assert.Null(ArucoRoles.Primary(instances));
            Assert.Same(first, ArucoRoles.Reference(instances));
        }

        [Fact]
        public void Reference_IsNullWithoutInstances()
        {
            Assert.Null(ArucoRoles.Reference(new ArucoTimingSettings[0]));
            Assert.Null(ArucoRoles.Reference(new ArucoTimingSettings[] { null }));
        }

        [Fact]
        public void KeepSinglePrimary_DemotesEveryLaterPrimary()
        {
            var first = Instance(TimingSystemRole.Primary, "0");
            var split = Instance(TimingSystemRole.Split, "1");
            var second = Instance(TimingSystemRole.Primary, "2");
            var third = Instance(TimingSystemRole.Primary, "3");

            List<ArucoTimingSettings> demoted = ArucoRoles.KeepSinglePrimary(new[] { first, split, null, second, third });

            Assert.Equal(new[] { second, third }, demoted);
            Assert.Equal(TimingSystemRole.Primary, first.Role);
            Assert.Equal(TimingSystemRole.Split, split.Role);
            Assert.Equal(TimingSystemRole.Split, second.Role);
            Assert.Equal(TimingSystemRole.Split, third.Role);
        }

        [Fact]
        public void KeepSinglePrimary_LeavesASinglePrimaryAlone()
        {
            var primary = Instance(TimingSystemRole.Primary, "0");
            var split = Instance(TimingSystemRole.Split, "1");

            Assert.Empty(ArucoRoles.KeepSinglePrimary(new[] { split, primary }));
            Assert.Equal(TimingSystemRole.Primary, primary.Role);
        }
    }
}
