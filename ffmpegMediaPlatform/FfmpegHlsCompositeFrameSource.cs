using ImageServer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tools;

namespace FfmpegMediaPlatform
{
    /// <summary>
    /// Composite frame source that maintains a persistent HLS live stream and uses
    /// a separate HLS recorder for recording functionality. This prevents interruption
    /// of the live stream when starting/stopping recordings.
    /// </summary>
    public class FfmpegHlsCompositeFrameSource : FrameSource, ICaptureFrameSource
    {
        private readonly FfmpegHlsLiveFrameSource liveFrameSource;
        private readonly HlsRecorderManager hlsRecorder;
        private bool disposed;
        private List<FrameTime> recordingFrameTimes;
        private DateTime recordingStartTime;
        private int recordingFrameCounter;

        // ICaptureFrameSource implementation
        public new bool IsVisible => liveFrameSource?.IsVisible ?? false;
        public FrameTime[] FrameTimes => recordingFrameTimes?.ToArray() ?? new FrameTime[0];
        public string Filename => hlsRecorder?.CurrentOutputPath ?? "";
        public new VideoConfig VideoConfig => liveFrameSource?.VideoConfig;
        public bool RecordNextFrameTime { get; set; }
        
        // Additional properties for compatibility
        public DateTime StartTime => DateTime.Now; // HLS streams don't have a fixed start time
        
        // Recording-related properties
        public new bool Recording => hlsRecorder?.IsRecording ?? false;
        public bool ManualRecording { get; set; }
        public bool Finalising => false; // HLS recording doesn't have a finalizing state
        public string HlsStreamUrl => liveFrameSource?.HlsStreamUrl;

        // Events for compatibility
        public event Action<RawTexture> NewRawTexture;
        public event Action<FrameSource.States> StateChanged;
        public event Action<bool> ConnectedChanged;

        public event Action<string> RecordingStarted;
        public event Action<string, bool> RecordingStopped;

        public FfmpegHlsCompositeFrameSource(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig, int httpPort = 8787)
            : base(videoConfig)
        {
            liveFrameSource = new FfmpegHlsLiveFrameSource(ffmpegMediaFramework, videoConfig, httpPort);
            hlsRecorder = new HlsRecorderManager(ffmpegMediaFramework);
            recordingFrameTimes = new List<FrameTime>();
            
            // Forward recording events
            hlsRecorder.RecordingStarted += (path) => RecordingStarted?.Invoke(path);
            hlsRecorder.RecordingStopped += (path, success) => RecordingStopped?.Invoke(path, success);
            
            // Forward frame events from live source to UI and track for recording
            liveFrameSource.OnFrameEvent += ForwardFrameEvent;
            
            Tools.Logger.VideoLog.LogCall(this, $"HLS Composite Frame Source initialized for device: {videoConfig.DeviceName} (Instance: {GetHashCode()})");
        }

        public override IEnumerable<Mode> GetModes()
        {
            return liveFrameSource.GetModes();
        }

        // FrameSource abstract method implementations
        public override bool UpdateTexture(Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice, int drawFrameId, ref Microsoft.Xna.Framework.Graphics.Texture2D texture)
        {
            bool result = liveFrameSource?.UpdateTexture(graphicsDevice, drawFrameId, ref texture) ?? false;
            
            // Log texture updates only occasionally to reduce spam
            if (drawFrameId % 120 == 0)
            {
                Tools.Logger.VideoLog.LogCall(this, $"UpdateTexture: drawFrameId={drawFrameId}, result={result}, texture={(texture != null ? $"{texture.Width}x{texture.Height}" : "null")}");
            }
            
            return result;
        }

        public override int FrameWidth => liveFrameSource?.FrameWidth ?? 640;
        public override int FrameHeight => liveFrameSource?.FrameHeight ?? 480;
        public override Microsoft.Xna.Framework.Graphics.SurfaceFormat FrameFormat => Microsoft.Xna.Framework.Graphics.SurfaceFormat.Color;
        
        // Forward state from live frame source
        public new FrameSource.States State => liveFrameSource?.State ?? FrameSource.States.Stopped;

        private void ForwardFrameEvent(long sampleTime, long frameNumber)
        {
            // Track frame times for recording ONLY if we're currently recording
            // Since live stream runs at 60fps but HLS records at 30fps, only track every other frame
            if (Recording)
            {
                // Skip every other frame to match 30fps recording rate when live stream is 60fps
                if (frameNumber % 2 != 0)
                {
                    // Forward the frame event to any UI subscribers but skip FrameTime tracking
                    OnFrame(sampleTime, frameNumber);
                    return;
                }
                
                DateTime currentTime = DateTime.Now;
                double secondsFromStart = (currentTime - recordingStartTime).TotalSeconds;
                
                // Ensure the first frame starts at exactly 0 seconds and frame numbering starts from 1
                lock (recordingFrameTimes)
                {
                    if (recordingFrameTimes.Count == 0)
                    {
                        secondsFromStart = 0.0;
                        recordingStartTime = currentTime; // Adjust start time to match first frame
                        recordingFrameCounter = 1; // Start frame counter at 1
                    }
                    else
                    {
                        recordingFrameCounter++; // Increment frame counter for each recorded frame
                    }
                }
                
                var frameTime = new FrameTime()
                {
                    Frame = recordingFrameCounter, // Use our own counter starting from 1
                    Time = currentTime,
                    Seconds = secondsFromStart
                };
                
                lock (recordingFrameTimes)
                {
                    recordingFrameTimes.Add(frameTime);
                    
                    // Log first few frame times for debugging
                    if (recordingFrameTimes.Count <= 3)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Recording Frame {recordingFrameTimes.Count}: Frame={frameTime.Frame}, Seconds={frameTime.Seconds:F3}, Time={frameTime.Time:HH:mm:ss.fff} (Live frame #{frameNumber})");
                    }
                }
                
                // Only reset the flag if it was set (maintains compatibility with video manager)
                if (RecordNextFrameTime)
                {
                    RecordNextFrameTime = false;
                }
            }
            
            // Forward the frame event to any UI subscribers
            OnFrame(sampleTime, frameNumber);
        }

        public override bool Start()
        {
            Tools.Logger.VideoLog.LogCall(this, $"Starting HLS Composite Frame Source (Instance: {GetHashCode()})");
            
            // Check if the live frame source is actually healthy (not just "Running" state)
            bool isHealthy = IsLiveStreamHealthy();
            
            if (liveFrameSource.State == FrameSource.States.Running && isHealthy)
            {
                Tools.Logger.VideoLog.LogCall(this, "HLS Live Frame Source already running and healthy - no restart needed");
                return true;
            }
            else if (liveFrameSource.State == FrameSource.States.Running && !isHealthy)
            {
                Tools.Logger.VideoLog.LogCall(this, "HLS Live Frame Source reports running but is unhealthy - restarting");
                liveFrameSource.Stop();
                System.Threading.Thread.Sleep(1000); // Brief pause for cleanup
            }
            
            bool result = liveFrameSource.Start();
            
            if (result)
            {
                Tools.Logger.VideoLog.LogCall(this, $"HLS Composite Frame Source started - Stream URL: {HlsStreamUrl}");
            }
            else
            {
                Tools.Logger.VideoLog.LogCall(this, "Failed to start HLS Composite Frame Source");
            }
            
            return result;
        }

        /// <summary>
        /// Check if the HLS live stream is actually healthy by verifying the playlist file exists
        /// </summary>
        private bool IsLiveStreamHealthy()
        {
            try
            {
                if (liveFrameSource == null) return false;
                
                // Check if HLS playlist file exists (indicates stream is generating content)
                string hlsDir = Path.Combine(Directory.GetCurrentDirectory(), "trackside_hls");
                string playlistPath = Path.Combine(hlsDir, "stream.m3u8");
                
                bool playlistExists = File.Exists(playlistPath);
                bool httpServerRunning = liveFrameSource.IsHttpServerRunning;
                
                Tools.Logger.VideoLog.LogCall(this, $"HLS Health Check - Playlist exists: {playlistExists}, HTTP server: {httpServerRunning}");
                
                return playlistExists && httpServerRunning;
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                return false;
            }
        }

        public override bool Stop()
        {
            Tools.Logger.VideoLog.LogCall(this, "Stopping HLS Composite Frame Source");
            
            // Stop recording first if active
            if (Recording)
            {
                StopRecording();
            }
            
            bool result = liveFrameSource.Stop();
            
            Tools.Logger.VideoLog.LogCall(this, "HLS Composite Frame Source stopped");
            return result;
        }

        public override bool Pause()
        {
            // HLS streams don't support pause/unpause in the traditional sense
            // The live stream continues, but we could potentially pause the display
            Tools.Logger.VideoLog.LogCall(this, "Pause not supported for HLS Composite Frame Source");
            return false;
        }

        public override bool Unpause()
        {
            Tools.Logger.VideoLog.LogCall(this, "Unpause not supported for HLS Composite Frame Source");
            return false;
        }

        /// <summary>
        /// Start recording using the HLS stream as source
        /// </summary>
        /// <param name="filename">Output filename for the recording</param>
        void ICaptureFrameSource.StartRecording(string filename)
        {
            StartRecording(filename);
        }

        /// <summary>
        /// Start recording using the HLS stream as source (internal implementation)
        /// </summary>
        /// <param name="filename">Output filename for the recording</param>
        public void StartRecording(string filename)
        {
            if (Recording)
            {
                Tools.Logger.VideoLog.LogCall(this, $"Already recording to {hlsRecorder.CurrentOutputPath}");
                return;
            }

            if (liveFrameSource.State != FrameSource.States.Running)
            {
                Tools.Logger.VideoLog.LogCall(this, "Cannot start recording - live stream is not running");
                return;
            }

            if (string.IsNullOrEmpty(HlsStreamUrl))
            {
                Tools.Logger.VideoLog.LogCall(this, "Cannot start recording - HLS stream URL is not available");
                return;
            }

            // Initialize recording frame times
            lock (recordingFrameTimes)
            {
                recordingFrameTimes.Clear();
                recordingStartTime = DateTime.Now;
                recordingFrameCounter = 0; // Reset frame counter (will be set to 1 on first frame)
                Tools.Logger.VideoLog.LogCall(this, $"Recording frame times initialized at {recordingStartTime:HH:mm:ss.fff}");
            }

            Tools.Logger.VideoLog.LogCall(this, $"Starting HLS recording to {filename}");
            
            bool success = hlsRecorder.StartRecording(HlsStreamUrl, filename, maxDurationSeconds: 0, captureFrameSource: this);
            
            if (success)
            {
                Tools.Logger.VideoLog.LogCall(this, $"HLS recording started successfully to {filename}");
            }
            else
            {
                Tools.Logger.VideoLog.LogCall(this, $"Failed to start HLS recording to {filename}");
            }
        }

        /// <summary>
        /// Stop the current recording (ICaptureFrameSource interface implementation)
        /// </summary>
        void ICaptureFrameSource.StopRecording()
        {
            StopRecording();
        }

        /// <summary>
        /// Stop the current recording (internal implementation)
        /// </summary>
        public void StopRecording()
        {
            if (!Recording)
            {
                Tools.Logger.VideoLog.LogCall(this, "Not currently recording");
                return;
            }

            Tools.Logger.VideoLog.LogCall(this, "Stopping HLS recording");
            
            // Log frame time statistics for debugging
            lock (recordingFrameTimes)
            {
                Tools.Logger.VideoLog.LogCall(this, $"Recording captured {recordingFrameTimes.Count} frame times during session");
                if (recordingFrameTimes.Count > 0)
                {
                    var firstFrame = recordingFrameTimes.First();
                    var lastFrame = recordingFrameTimes.Last();
                    var duration = lastFrame.Time - firstFrame.Time;
                    var avgFps = recordingFrameTimes.Count / duration.TotalSeconds;
                    Tools.Logger.VideoLog.LogCall(this, $"Frame time span: {firstFrame.Time:HH:mm:ss.fff} to {lastFrame.Time:HH:mm:ss.fff} ({duration.TotalSeconds:F1}s)");
                    Tools.Logger.VideoLog.LogCall(this, $"Average FPS during recording: {avgFps:F1}");
                    Tools.Logger.VideoLog.LogCall(this, $"First frame seconds: {firstFrame.Seconds:F3}, Last frame seconds: {lastFrame.Seconds:F3}");
                }
            }
            
            bool success = hlsRecorder.StopRecording();
            
            if (success)
            {
                Tools.Logger.VideoLog.LogCall(this, "HLS recording stopped successfully");
            }
            else
            {
                Tools.Logger.VideoLog.LogCall(this, "HLS recording stop completed with issues");
            }
        }

        /// <summary>
        /// Start recording with a maximum duration
        /// </summary>
        /// <param name="filename">Output filename</param>
        /// <param name="maxDurationSeconds">Maximum recording duration in seconds</param>
        public void StartRecording(string filename, int maxDurationSeconds)
        {
            if (Recording)
            {
                Tools.Logger.VideoLog.LogCall(this, $"Already recording to {hlsRecorder.CurrentOutputPath}");
                return;
            }

            if (liveFrameSource.State != FrameSource.States.Running)
            {
                Tools.Logger.VideoLog.LogCall(this, "Cannot start recording - live stream is not running");
                return;
            }

            if (string.IsNullOrEmpty(HlsStreamUrl))
            {
                Tools.Logger.VideoLog.LogCall(this, "Cannot start recording - HLS stream URL is not available");
                return;
            }

            Tools.Logger.VideoLog.LogCall(this, $"Starting HLS recording to {filename} with max duration {maxDurationSeconds}s");
            
            bool success = hlsRecorder.StartRecording(HlsStreamUrl, filename, maxDurationSeconds, captureFrameSource: this);
            
            if (success)
            {
                Tools.Logger.VideoLog.LogCall(this, $"HLS recording started successfully to {filename} (max {maxDurationSeconds}s)");
            }
            else
            {
                Tools.Logger.VideoLog.LogCall(this, $"Failed to start HLS recording to {filename}");
            }
        }

        /// <summary>
        /// Get the current HLS stream URL for external access
        /// </summary>
        /// <returns>The HLS stream URL or null if not available</returns>
        public string GetHlsStreamUrl()
        {
            return HlsStreamUrl;
        }

        /// <summary>
        /// Check if the HTTP server is running and serving HLS content
        /// </summary>
        /// <returns>True if the HTTP server is running</returns>
        public bool IsHttpServerRunning()
        {
            return liveFrameSource?.IsHttpServerRunning ?? false;
        }

        public override void Dispose()
        {
            if (disposed)
                return;

            Tools.Logger.VideoLog.LogCall(this, "Disposing HLS Composite Frame Source");

            try
            {
                // Stop recording if active
                if (Recording)
                {
                    StopRecording();
                }

                // Dispose components
                hlsRecorder?.Dispose();
                liveFrameSource?.Dispose();
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
            finally
            {
                disposed = true;
                base.Dispose();
            }
        }
    }
}