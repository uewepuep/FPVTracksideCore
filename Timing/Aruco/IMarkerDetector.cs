using System;
using System.Collections.Generic;

namespace Timing.Aruco
{
    /// <summary>
    /// Detector settings shared by all channels in one detection pass, taken from the reference
    /// settings (see <see cref="ArucoRoles.Reference"/>). Each detector uses the ones it needs.
    /// </summary>
    public sealed class MarkerDetectorOptions
    {
        public ArucoDetectMode Mode;
        public double ErrorCorrectionRate;
        public int HybridDistance;

        // Rejects sub-threshold candidates inside the detector (perimeter filter) so small
        // noise squares never reach decoding.
        public double MinMarkerPercent;
    }

    /// <summary>
    /// Finds gate markers of one type on a channel's frames. One instance per channel: a
    /// detector may keep state from the previous frame, which <see cref="Reset"/> clears when
    /// the channel's feed changes.
    /// </summary>
    public interface IMarkerDetector : IDisposable
    {
        /// <summary>Adds this frame's markers to <paramref name="into"/>.</summary>
        void Detect(DetectionFrame frame, MarkerDetectorOptions options, List<MarkerDetection> into);

        /// <summary>Forgets anything kept from earlier frames.</summary>
        void Reset();
    }
}
