using System;
using System.Collections.Generic;
using Timing.Aruco;
using Xunit;

namespace Timing.Tests.Aruco
{
    public class ArucoLapDecisionsTests
    {
        // W, the ArUco Validation Time.
        private static readonly TimeSpan W = TimeSpan.FromSeconds(5);
        private static readonly DateTime Start = new DateTime(2026, 10, 21, 7, 0, 0);

        private static DateTime T(double seconds) => Start.AddSeconds(seconds);

        private static ArucoLapEntry Crossing(double s, bool setByHand = false) => new ArucoLapEntry(T(s), ArucoLapKind.Crossing, setByHand);
        private static ArucoLapEntry Manual(double s) => new ArucoLapEntry(T(s), ArucoLapKind.Manual);

        private static ArucoCrossingDecision Decide(double s, IEnumerable<ArucoLapEntry> laps,
            bool correct = false, TimeSpan? validationTime = null, TimeSpan? ignore = null)
        {
            return ArucoLapDecisions.DecideCrossing(T(s), laps, Start, ignore ?? TimeSpan.Zero,
                validationTime ?? W, correct);
        }

        // --- pass-through ---------------------------------------------------------------------

        [Fact]
        public void NoValidationTime_PassesThrough()
        {
            var d = Decide(11, new[] { Crossing(10) }, validationTime: TimeSpan.Zero);
            Assert.Equal(ArucoCrossingAction.PassThrough, d.Action);
        }

        [Fact]
        public void InsideIgnoreWindow_PassesThrough()
        {
            var d = Decide(2, new ArucoLapEntry[0], ignore: TimeSpan.FromSeconds(3));
            Assert.Equal(ArucoCrossingAction.PassThrough, d.Action);
        }

        [Fact]
        public void BeforeStart_PassesThrough()
        {
            var d = Decide(-1, new ArucoLapEntry[0]);
            Assert.Equal(ArucoCrossingAction.PassThrough, d.Action);
        }

        // --- ordinary laps ----------------------------------------------------------------------

        [Fact]
        public void LapLongerThanW_IsAccepted()
        {
            var d = Decide(30, new[] { Crossing(10) });
            Assert.Equal(ArucoCrossingAction.Accept, d.Action);
            Assert.Null(d.Replace);
            Assert.False(d.RecalculateAfter);
        }

        [Fact]
        public void LapOfExactlyW_IsAccepted()
        {
            var d = Decide(15, new[] { Crossing(10) });
            Assert.Equal(ArucoCrossingAction.Accept, d.Action);
        }

        // --- after an ArUco lap -----------------------------------------------------------------

        [Fact]
        public void WithinWOfCrossing_CorrectionOff_IsDropped()
        {
            var d = Decide(12, new[] { Crossing(10) }, correct: false);
            Assert.Equal(ArucoCrossingAction.Drop, d.Action);
            Assert.Equal("the previous crossing", d.DropReason);
            Assert.Equal(T(10), d.Reference);
        }

        [Fact]
        public void WithinWOfCrossing_CorrectionOn_ReplacesPrevious()
        {
            ArucoLapEntry previous = Crossing(10);
            var d = Decide(12, new[] { Crossing(0.5), previous }, correct: true);
            Assert.Equal(ArucoCrossingAction.Accept, d.Action);
            Assert.Same(previous, d.Replace);
        }

        [Fact]
        public void WithinWOfCrossingSetByHand_IsDroppedEvenWithCorrection()
        {
            var d = Decide(12, new[] { Crossing(10, setByHand: true) }, correct: true);
            Assert.Equal(ArucoCrossingAction.Drop, d.Action);
            Assert.Equal("a lap set by hand", d.DropReason);
        }

        // --- after a standard manual lap -------------------------------------------------------

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void WithinWOfStandardManual_IsDropped(bool correct)
        {
            var d = Decide(12, new[] { Crossing(1), Manual(10) }, correct: correct);
            Assert.Equal(ArucoCrossingAction.Drop, d.Action);
            Assert.Equal("a manual lap", d.DropReason);
        }

        [Fact]
        public void BeforeAStandardManualLap_IsAcceptedAndRecalculates()
        {
            // The crossing was on its way while the operator entered the lap.
            var d = Decide(30, new[] { Crossing(10), Manual(31) });
            Assert.Equal(ArucoCrossingAction.Accept, d.Action);
            Assert.True(d.RecalculateAfter);
        }

        // --- first lap --------------------------------------------------------------------------

        [Fact]
        public void FirstCrossingWithinWOfStart_IsAccepted()
        {
            // Only the event's ignore window applies after the start.
            var d = Decide(3, new ArucoLapEntry[0], ignore: TimeSpan.FromSeconds(0.5));
            Assert.Equal(ArucoCrossingAction.Accept, d.Action);
        }

        [Fact]
        public void FirstCrossingJustAfterIgnoreWindow_IsAccepted()
        {
            var d = Decide(0.5, new ArucoLapEntry[0], ignore: TimeSpan.FromSeconds(0.5));
            Assert.Equal(ArucoCrossingAction.Accept, d.Action);
        }
    }
}
