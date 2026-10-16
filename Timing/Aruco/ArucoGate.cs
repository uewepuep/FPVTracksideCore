using System;

namespace Timing.Aruco
{
    /// <summary>
    /// One channel's view of the gate: in view while the marker count is at or above the
    /// threshold, and a crossing once it has stayed below for FlickerLengthMs.
    ///
    /// Split out so that the live timing path (ArucoTimingSystem.ReportMarkerCount) and any
    /// after-the-fact replay over a recorded waveform run the *same* code. A second
    /// implementation of this rule would eventually disagree with the first, and a marshal
    /// screen that disagrees with the timer is worse than no marshal screen.
    /// </summary>
    public class ArucoGate
    {
        private bool inGate;
        private DateTime flickerEndTime = DateTime.MinValue;

        /// <summary>
        /// Most markers seen on any one frame of the latest pass. Kept after the crossing, which
        /// is only reported once the count has dropped, so the caller can read it alongside the
        /// crossing; cleared when the next pass begins.
        /// </summary>
        public int LastPeak { get; private set; }

        /// <summary>
        /// Last crossing this gate reported. Times are derived from the caller's capture time,
        /// so anything that makes those non-monotonic - a second detection thread, or an OS
        /// clock step - would otherwise reach RaceLib as an out-of-order lap.
        /// </summary>
        public DateTime LastCrossing { get; private set; } = DateTime.MinValue;

        public void Reset()
        {
            inGate = false;
            flickerEndTime = DateTime.MinValue;
            LastPeak = 0;
            LastCrossing = DateTime.MinValue;
        }

        /// <summary>
        /// Feed one frame. Returns true, with the crossing time, on the frame that completes a
        /// gate pass.
        ///
        /// The crossing is reported at captureTime - flickerLengthMs, which is the moment the
        /// count first dropped below the threshold - when the marker left view - rounded up by
        /// at most one frame interval.
        /// </summary>
        public bool Update(int count, DateTime captureTime, int markerThreshold, int flickerLengthMs,
                           out DateTime crossing)
        {
            crossing = default;

            if (count >= markerThreshold)
            {
                LastPeak = inGate ? Math.Max(LastPeak, count) : count;
                inGate = true;
                flickerEndTime = DateTime.MinValue;
                return false;
            }

            if (!inGate)
                return false;

            if (flickerEndTime == DateTime.MinValue)
                flickerEndTime = captureTime.AddMilliseconds(flickerLengthMs);

            if (captureTime < flickerEndTime)
                return false;

            DateTime crossingTime = captureTime.AddMilliseconds(-flickerLengthMs);

            inGate = false;
            flickerEndTime = DateTime.MinValue;

            // Never report a time at or before the previous crossing on this channel.
            if (crossingTime <= LastCrossing)
                return false;

            LastCrossing = crossingTime;
            crossing = crossingTime;
            return true;
        }
    }
}
