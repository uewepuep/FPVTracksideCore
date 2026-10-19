using Timing;

namespace Timing.Aruco
{
    /// <summary>
    /// An <see cref="RSSIWaveform"/> that is not an RSSI trace at all: ArUco's per-frame marker
    /// count for one pilot's channel.
    ///
    /// Everything that makes it read differently from a receiver trace lives here rather than on
    /// RSSIWaveform, so nothing about ArUco reaches the type RotorHazard shares. The marshal UI
    /// tests for this type and reads the extras; code that only knows RSSIWaveform sees exactly
    /// what it always saw.
    /// </summary>
    public class ArucoWaveform : RSSIWaveform
    {
        /// <summary>
        /// Marker counts are published multiplied by this, purely so the trace and its integer
        /// threshold line have vertical room. The graph divides by it again to label the value
        /// axis in whole markers and to grid it at each one.
        /// </summary>
        public int ValueScale { get; set; }

        /// <summary>Unit name shown beside the threshold line, e.g. "markers".</summary>
        public string ValueLabel { get; set; }

        /// <summary>
        /// EnterAt and ExitAt are one and the same number: the gate is in view while the count
        /// is at or above MarkerThreshold and out of view below it, and it is FlickerLengthMs -
        /// not a second, lower threshold - that decides when the crossing is emitted. The graph
        /// draws one line rather than two coincident ones.
        ///
        /// The same fact is why the thresholds are not draggable here. RSSICrossingCalculator
        /// only reproduces RotorHazard's peak-midpoint rule; ArUco emits a crossing when the
        /// marker left view, so recalculating that way would silently move every lap.
        /// </summary>
        public int MarkerThreshold { get; set; }
    }
}
