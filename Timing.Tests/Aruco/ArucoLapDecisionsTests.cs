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
        private static ArucoLapEntry Provisional(double s) => new ArucoLapEntry(T(s), ArucoLapKind.Provisional);

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
            Assert.Empty(d.DisqualifyProvisional);
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

        // --- provisional laps ----------------------------------------------------------------------

        [Fact]
        public void ProvisionalThenCrossingWithinW_CrossingReplacesProvisional()
        {
            // M -> A
            ArucoLapEntry m = Provisional(30);
            var d = Decide(32, new[] { Crossing(10), m });
            Assert.Equal(ArucoCrossingAction.Accept, d.Action);
            Assert.Equal(new[] { m }, d.DisqualifyProvisional);
            Assert.Null(d.Replace);
        }

        [Fact]
        public void ProvisionalPressedAfterLateCrossing_IsReplaced()
        {
            // The crossing time is before the press, but it arrives after it.
            ArucoLapEntry m = Provisional(31);
            var d = Decide(30, new[] { Crossing(10), m });
            Assert.Equal(ArucoCrossingAction.Accept, d.Action);
            Assert.Equal(new[] { m }, d.DisqualifyProvisional);
            Assert.False(d.RecalculateAfter);
        }

        [Fact]
        public void ProvisionalLap_IsNotThePreviousLap()
        {
            // Measured from the crossing at 10, not the provisional lap at 30: 22s, not 2s.
            var d = Decide(32, new[] { Crossing(10), Provisional(30) }, correct: false);
            Assert.Equal(ArucoCrossingAction.Accept, d.Action);
        }

        [Fact]
        public void ProvisionalMoreThanWAway_IsKept()
        {
            var d = Decide(32, new[] { Crossing(10), Provisional(20) });
            Assert.Equal(ArucoCrossingAction.Accept, d.Action);
            Assert.Empty(d.DisqualifyProvisional);
        }

        [Fact]
        public void DroppedCrossing_LeavesProvisionalAlone()
        {
            var d = Decide(31, new[] { Crossing(28), Provisional(30) }, correct: false);
            Assert.Equal(ArucoCrossingAction.Drop, d.Action);
            Assert.Empty(d.DisqualifyProvisional);
        }

        [Fact]
        public void ProvisionalThenTwoCrossings_FlickerRuleAppliesToSecond()
        {
            // M -> A1 -> A2. After A1 has replaced M, only A1 is valid.
            ArucoLapEntry a1 = Crossing(31);

            var off = Decide(33, new[] { Crossing(10), a1 }, correct: false);
            Assert.Equal(ArucoCrossingAction.Drop, off.Action);

            var on = Decide(33, new[] { Crossing(10), a1 }, correct: true);
            Assert.Equal(ArucoCrossingAction.Accept, on.Action);
            Assert.Same(a1, on.Replace);
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

        // --- provisional laps being entered -------------------------------------------------------

        [Fact]
        public void Provisional_WithinWAfterCrossing_IsBlocked()
        {
            // A1 -> M
            ArucoLapEntry a = Crossing(30);
            Assert.Same(a, ArucoLapDecisions.FindProvisionalBlocker(T(32), new[] { a }, W));
        }

        [Fact]
        public void Provisional_RepeatedPress_IsBlocked()
        {
            ArucoLapEntry m = Provisional(30);
            Assert.Same(m, ArucoLapDecisions.FindProvisionalBlocker(T(31), new[] { Crossing(10), m }, W));
        }

        [Fact]
        public void Provisional_AfterStandardManual_IsRecorded()
        {
            Assert.Null(ArucoLapDecisions.FindProvisionalBlocker(T(31), new[] { Manual(30) }, W));
        }

        [Fact]
        public void Provisional_BeforeACrossing_IsRecorded()
        {
            Assert.Null(ArucoLapDecisions.FindProvisionalBlocker(T(29), new[] { Crossing(30) }, W));
        }

        [Fact]
        public void Provisional_ExactlyWAfterCrossing_IsRecorded()
        {
            Assert.Null(ArucoLapDecisions.FindProvisionalBlocker(T(35), new[] { Crossing(30) }, W));
        }

        [Fact]
        public void Provisional_NoValidationTime_IsRecorded()
        {
            Assert.Null(ArucoLapDecisions.FindProvisionalBlocker(T(31), new[] { Crossing(30) }, TimeSpan.Zero));
        }
    }
}
