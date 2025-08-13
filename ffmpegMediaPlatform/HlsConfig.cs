using System;

namespace FfmpegMediaPlatform
{
    /// <summary>
    /// Configuration class for HLS functionality in FPVTracksideCore.
    /// This provides a centralized way to control HLS features.
    /// </summary>
    public static class HlsConfig
    {
        /// <summary>
        /// Master switch to enable/disable all HLS functionality.
        /// When disabled:
        /// - HLS streaming is disabled
        /// - HTTP server is not started
        /// - HLS file generation is skipped
        /// - Only RGBA live processing remains active
        /// 
        /// Default: true (HLS enabled)
        /// </summary>
        public static bool HlsEnabled
        {
            get => FfmpegHlsLiveFrameSource.HlsEnabled;
            set => FfmpegHlsLiveFrameSource.HlsEnabled = value;
        }

        /// <summary>
        /// HTTP port for HLS streaming (only used when HLS is enabled).
        /// Default: 8787
        /// </summary>
        public static int HttpPort { get; set; } = 8787;

        /// <summary>
        /// HLS segment duration in seconds (only used when HLS is enabled).
        /// Lower values = lower latency but more segments.
        /// Default: 0.5 seconds
        /// </summary>
        public static double HlsSegmentDuration { get; set; } = 0.5;

        /// <summary>
        /// Maximum number of HLS segments to keep in memory (only used when HLS is enabled).
        /// Lower values = lower memory usage but less buffering.
        /// Default: 3 segments
        /// </summary>
        public static int HlsMaxSegments { get; set; } = 3;

        /// <summary>
        /// Disable HLS functionality completely.
        /// This is equivalent to setting HlsEnabled = false.
        /// </summary>
        public static void DisableHls()
        {
            HlsEnabled = false;
        }

        /// <summary>
        /// Enable HLS functionality.
        /// This is equivalent to setting HlsEnabled = true.
        /// </summary>
        public static void EnableHls()
        {
            HlsEnabled = true;
        }

        /// <summary>
        /// Get the current HLS status as a human-readable string.
        /// </summary>
        public static string GetStatus()
        {
            return HlsEnabled ? "HLS Enabled" : "HLS Disabled";
        }
    }
}
