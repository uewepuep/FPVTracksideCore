using System;
using System.Collections.Generic;
using Timing.Aruco;
using Xunit;

namespace Timing.Tests.Aruco
{
    public class ArucoGateTests
    {
        private const int Threshold = 2;
        private const int FlickerMs = 150;
        private const int FrameMs = 50;

        private static readonly DateTime Origin = new DateTime(2026, 10, 21, 7, 0, 0);

        /// <summary>Feeds one count per frame from Origin and returns the crossings, in ms.</summary>
        private static List<double> Run(ArucoGate gate, params int[] counts)
        {
            var crossings = new List<double>();
            for (int i = 0; i < counts.Length; i++)
            {
                if (gate.Update(counts[i], Origin.AddMilliseconds(i * FrameMs), Threshold, FlickerMs, out DateTime crossing))
                    crossings.Add((crossing - Origin).TotalMilliseconds);
            }
            return crossings;
        }

        [Fact]
        public void Pass_IsReportedWhenTheMarkerLeftView()
        {
            // In view on frames 1-3, out from frame 4 (200ms).
            var crossings = Run(new ArucoGate(), 0, 2, 3, 2, 0, 0, 0, 0, 0);
            Assert.Equal(new[] { 200.0 }, crossings);
        }

        [Fact]
        public void BelowThreshold_IsNotAPass()
        {
            Assert.Empty(Run(new ArucoGate(), 0, 1, 1, 1, 0, 0, 0, 0));
        }

        [Fact]
        public void DropOutShorterThanFlicker_IsOnePass()
        {
            // Out for 100ms (frames 3-4) in the middle of the pass.
            var crossings = Run(new ArucoGate(), 2, 2, 2, 0, 0, 2, 2, 0, 0, 0, 0, 0);
            Assert.Equal(new[] { 350.0 }, crossings);
        }

        [Fact]
        public void DropOutAsLongAsFlicker_IsTwoPasses()
        {
            var crossings = Run(new ArucoGate(), 2, 2, 0, 0, 0, 0, 2, 2, 0, 0, 0, 0);
            Assert.Equal(new[] { 100.0, 400.0 }, crossings);
        }

        [Fact]
        public void LastPeak_IsTheMostMarkersInThePass()
        {
            var gate = new ArucoGate();
            Run(gate, 2, 4, 3, 0, 0, 0, 0);
            Assert.Equal(4, gate.LastPeak);
        }

        [Fact]
        public void CrossingAtOrBeforeThePrevious_IsNotReported()
        {
            var gate = new ArucoGate();
            Assert.Single(Run(gate, 2, 0, 0, 0, 0));

            // The same frames again: identical times, as a second detection thread would give.
            Assert.Empty(Run(gate, 2, 0, 0, 0, 0));
        }

        [Fact]
        public void Reset_ForgetsThePreviousCrossing()
        {
            var gate = new ArucoGate();
            Run(gate, 2, 0, 0, 0, 0);
            gate.Reset();
            Assert.Single(Run(gate, 2, 0, 0, 0, 0));
        }
    }
}
