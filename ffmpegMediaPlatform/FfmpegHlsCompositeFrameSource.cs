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
        private bool disposed;

        // ICaptureFrameSource implementation
        public new bool IsVisible => liveFrameSource?.IsVisible ?? false;
        public FrameTime[] FrameTimes => liveFrameSource?.FrameTimes ?? new FrameTime[0];
        public string Filename => liveFrameSource?.Filename ?? "";
        public new VideoConfig VideoConfig => liveFrameSource?.VideoConfig;
        public bool RecordNextFrameTime { get; set; }
        
        // Additional properties for compatibility
        public DateTime StartTime => DateTime.Now; // HLS streams don't have a fixed start time
        
        // Recording-related properties
        public new bool Recording => liveFrameSource?.Recording ?? false;
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
            
            // Recording events will be handled by the live frame source's RGBA recorder
            // No need to forward events from rgbaRecorder since we're delegating to liveFrameSource
            
            // Forward frame events from live source to UI and track for recording
            liveFrameSource.OnFrameEvent += ForwardFrameEvent;
            
            Tools.Logger.VideoLog.LogCall(this, $"HLS Composite Frame Source initialized for device: {videoConfig.DeviceName} (Instance: {GetHashCode()})");

            Direction = Directions.BottomUp;
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
            // Simply forward the frame event to any UI subscribers
            // Frame timing for recording is now handled by the live frame source's RGBA recorder
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
        /// Check if the live stream is actually healthy by verifying RGBA frames are flowing
        /// </summary>
        private bool IsLiveStreamHealthy()
        {
            try
            {
                if (liveFrameSource == null) return false;
                
                // Check if RGBA frames are flowing from pipe:1 for live display
                bool framesFlowing = liveFrameSource.Connected;
                
                Tools.Logger.VideoLog.LogCall(this, $"Live Stream Health Check - RGBA frames flowing: {framesFlowing}");
                
                return framesFlowing;
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
        /// Start recording using RGBA frames from the live stream (internal implementation)
        /// </summary>
        /// <param name="filename">Output filename for the recording</param>
        public void StartRecording(string filename)
        {
            if (Recording)
            {
                Tools.Logger.VideoLog.LogCall(this, $"Already recording to {liveFrameSource.Filename}");
                return;
            }

            if (liveFrameSource.State != FrameSource.States.Running)
            {
                Tools.Logger.VideoLog.LogCall(this, "Cannot start recording - live stream is not running");
                return;
            }

            // Frame timing is now handled by the live frame source's RGBA recorder

            Tools.Logger.VideoLog.LogCall(this, $"Starting RGBA recording to {filename}");
            
            // Delegate to the live frame source's RGBA recording capability
            liveFrameSource.StartRecording(filename);
            
            Tools.Logger.VideoLog.LogCall(this, $"RGBA recording started via live frame source to {filename}");
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

            Tools.Logger.VideoLog.LogCall(this, "Stopping RGBA recording");
            
            // Delegate to the live frame source's RGBA recording capability
            liveFrameSource.StopRecording();
            
            Tools.Logger.VideoLog.LogCall(this, "RGBA recording stopped via live frame source");
        }

        /// <summary>
        /// Start recording with a maximum duration
        /// Note: Maximum duration is not currently supported with RGBA recording
        /// </summary>
        /// <param name="filename">Output filename</param>
        /// <param name="maxDurationSeconds">Maximum recording duration in seconds (not implemented for RGBA recording)</param>
        public void StartRecording(string filename, int maxDurationSeconds)
        {
            if (maxDurationSeconds > 0)
            {
                Tools.Logger.VideoLog.LogCall(this, $"Warning: Maximum duration ({maxDurationSeconds}s) is not supported with RGBA recording - starting unlimited recording");
            }
            
            // Delegate to the regular StartRecording method
            StartRecording(filename);
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

                // Dispose components - rgbaRecorder is handled by liveFrameSource
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