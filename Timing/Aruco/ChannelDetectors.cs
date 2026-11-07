using System;
using System.Collections.Generic;

namespace Timing.Aruco
{
    /// <summary>
    /// The detectors of one channel and its undistortion map. Runs every detector on the same
    /// <see cref="DetectionFrame"/>, so the gray and corrected images are made once per frame,
    /// and resets them when the channel's feed (video source or frequency) changes, so state a
    /// detector keeps from earlier frames never carries over to a different picture.
    /// </summary>
    public sealed class ChannelDetectors : IDisposable
    {
        private readonly UndistortMap undistort;
        private readonly List<IMarkerDetector> detectors;
        private object feedSource;
        private int feedFrequency;

        public ChannelDetectors(UndistortMap undistort, IEnumerable<IMarkerDetector> detectors)
        {
            this.undistort = undistort;
            this.detectors = new List<IMarkerDetector>(detectors);
        }

        /// <summary>
        /// Tells the detectors which feed the next frames come from. A change from the previous
        /// feed resets them; the first call only records it.
        /// </summary>
        public void SetFeed(object source, int frequency)
        {
            bool changed = feedSource != null && (!ReferenceEquals(feedSource, source) || feedFrequency != frequency);
            feedSource = source;
            feedFrequency = frequency;
            if (changed)
                Reset();
        }

        /// <summary>All detectors' markers on this BGRA frame, in detector order.</summary>
        public List<MarkerDetection> Detect(byte[] bgra, int width, int height, MarkerDetectorOptions options)
        {
            var results = new List<MarkerDetection>();
            using (var frame = new DetectionFrame(bgra, width, height, undistort))
            {
                foreach (var detector in detectors)
                    detector.Detect(frame, options, results);
            }
            return results;
        }

        public void Reset()
        {
            foreach (var detector in detectors)
                detector.Reset();
        }

        public void Dispose()
        {
            foreach (var detector in detectors)
                detector.Dispose();
            detectors.Clear();
            undistort?.Dispose();
        }
    }
}
