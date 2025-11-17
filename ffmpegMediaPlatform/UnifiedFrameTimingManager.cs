using System;
using System.Collections.Generic;
using System.Linq;
using ImageServer;
using Tools;

namespace FfmpegMediaPlatform
{
    /// <summary>
    /// Unified frame timing management for consistent recording and playback across all platforms.
    /// This ensures Windows and Mac generate identical timing data in .recordinfo.xml files.
    /// </summary>
    public static class UnifiedFrameTimingManager
    {
        /// <summary>
        /// Create a standardized frame time entry using consistent timing logic across platforms
        /// </summary>
        /// <param name="frameNumber">Frame number (0-based)</param>
        /// <param name="currentTime">Current timestamp</param>
        /// <param name="recordingStartTime">When recording started</param>
        /// <returns>Consistent FrameTime entry</returns>
        public static FrameTime CreateFrameTime(int frameNumber, DateTime currentTime, DateTime recordingStartTime)
        {
            // Use consistent timing calculation across all platforms
            var timeSinceStart = currentTime - recordingStartTime;
            
            return new FrameTime
            {
                Frame = frameNumber,
                Time = currentTime,
                Seconds = timeSinceStart.TotalSeconds // Use TotalSeconds for consistent precision
            };
        }

        /// <summary>
        /// Calculate video duration using unified logic across platforms
        /// Uses the longer of XML frame timing or fallback duration to handle buffered frames
        /// </summary>
        /// <param name="frameTimes">Frame timing data from recording</param>
        /// <param name="fallbackDuration">Fallback duration (typically from FFprobe)</param>
        /// <returns>Consistent duration calculation that accounts for buffering</returns>
        public static TimeSpan CalculateVideoDuration(FrameTime[] frameTimes, TimeSpan fallbackDuration)
        {
            if (frameTimes != null && frameTimes.Length > 1)
            {
                // Sort by time to ensure correct ordering
                var sortedFrames = frameTimes.OrderBy(f => f.Time).ToArray();
                var firstFrame = sortedFrames.First();
                var lastFrame = sortedFrames.Last();
                
                // Calculate XML duration from frame timing
                var xmlDuration = lastFrame.Time - firstFrame.Time;
                
                // Use the longer duration to account for buffered frames at the end
                var finalDuration = xmlDuration > fallbackDuration ? xmlDuration : fallbackDuration;
                
                Tools.Logger.VideoLog.LogCall(typeof(UnifiedFrameTimingManager),
                    $"[DIAG-PLAYBACK] Unified duration calculation: XML={xmlDuration.TotalSeconds:F3}s, Fallback={fallbackDuration.TotalSeconds:F3}s, Using={finalDuration.TotalSeconds:F3}s");
                
                return finalDuration;
            }
            
            Tools.Logger.VideoLog.LogCall(typeof(UnifiedFrameTimingManager), 
                $"Unified duration calculation: No frame times available, using fallback = {fallbackDuration.TotalSeconds:F3}s");
            
            return fallbackDuration;
        }

        /// <summary>
        /// Validate frame timing consistency to detect platform-specific issues
        /// </summary>
        /// <param name="frameTimes">Frame timing data to validate</param>
        /// <param name="expectedFrameRate">Expected frame rate</param>
        /// <returns>True if timing data appears consistent</returns>
        public static bool ValidateFrameTimingConsistency(FrameTime[] frameTimes, float expectedFrameRate)
        {
            if (frameTimes == null || frameTimes.Length < 10)
            {
                Tools.Logger.VideoLog.LogCall(typeof(UnifiedFrameTimingManager), 
                    "Cannot validate frame timing: insufficient frame data");
                return false;
            }

            var sortedFrames = frameTimes.OrderBy(f => f.Time).ToArray();
            var totalDuration = (sortedFrames.Last().Time - sortedFrames.First().Time).TotalSeconds;
            var averageFrameRate = (sortedFrames.Length - 1) / totalDuration;
            
            var frameRateDifference = Math.Abs(averageFrameRate - expectedFrameRate);
            var isConsistent = frameRateDifference < (expectedFrameRate * 0.1); // Within 10%
            
            Tools.Logger.VideoLog.LogCall(typeof(UnifiedFrameTimingManager), 
                $"Frame timing validation: Expected {expectedFrameRate:F1}fps, Measured {averageFrameRate:F1}fps, " +
                $"Difference {frameRateDifference:F1}fps, Consistent: {isConsistent}");
            
            return isConsistent;
        }

        /// <summary>
        /// Get consistent high-precision timestamp across platforms
        /// Uses the same timing source to ensure identical behavior
        /// </summary>
        /// <returns>High-precision timestamp</returns>
        public static DateTime GetHighPrecisionTimestamp()
        {
            // Use DateTime.UtcNow for consistent precision across platforms
            // Convert to local time to maintain compatibility with existing code
            return DateTime.UtcNow.ToLocalTime();
        }

        /// <summary>
        /// Initialize timing for a new recording session with consistent start time
        /// </summary>
        /// <returns>Standardized recording start time</returns>
        public static DateTime InitializeRecordingStartTime()
        {
            var startTime = GetHighPrecisionTimestamp();
            Tools.Logger.VideoLog.LogCall(typeof(UnifiedFrameTimingManager), 
                $"Unified recording start time initialized: {startTime:HH:mm:ss.fff}");
            return startTime;
        }
    }
}