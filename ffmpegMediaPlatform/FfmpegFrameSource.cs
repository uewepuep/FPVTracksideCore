using ImageServer;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace FfmpegMediaPlatform
{
    public abstract class FfmpegFrameSource : TextureFrameSource, ICaptureFrameSource
    {
        protected int width;
        protected int height;

        public override int FrameWidth
        {
            get
            {
                return width > 0 ? width : 640;
            }
        }

        public override int FrameHeight
        {
            get
            {
                return height > 0 ? height : 480;
            }
        }

        public override SurfaceFormat FrameFormat
        {
            get
            {
                return SurfaceFormat;
            }
        }

        protected FfmpegMediaFramework ffmpegMediaFramework;

        protected Process process;

        protected byte[] buffer;

        protected Thread thread;
        protected bool run;
        protected bool inited;
        
        // PERFORMANCE: Frame queue for parallel processing
        private readonly System.Collections.Concurrent.ConcurrentQueue<(byte[] data, DateTime timestamp, long frameNumber)> frameProcessingQueue;
        private readonly System.Threading.SemaphoreSlim frameProcessingSemaphore;
        private Task frameProcessingTask;
        private readonly CancellationTokenSource frameProcessingCancellation;

        // Recording implementation
        protected string recordingFilename;
        private List<FrameTime> frameTimes;
        private bool recordNextFrameTime;
        private bool manualRecording;
        private bool finalising;
        private DateTime recordingStartTime;
        private long frameCount;
        
        // Frame timing tracking for camera loop
        private DateTime lastFrameTime = DateTime.MinValue;
        private float measuredFrameRate = 0f;
        private bool frameRateMeasured = false;
        
        // REAL-TIME: Smart frame dropping for immediate responsiveness
        // This solves the "1-second delay when moving hand in front of camera" problem by:
        // 1. Dropping first 15 frames on startup to reach real-time quickly 
        // 2. Dynamically dropping frames when display processing falls behind
        // 3. NEVER dropping recording frames - only display frames are dropped
        private const int STARTUP_FRAMES_TO_DROP = 15; // Skip first 15 frames to get to real-time quickly
        private const double MAX_DISPLAY_LATENCY_MS = 500; // Drop frames if more than 500ms behind
        private readonly Queue<DateTime> displayFrameTimestamps = new Queue<DateTime>();
        private int framesDroppedForRealtime = 0;
        
        // Frame recording queue for async processing
        private readonly System.Collections.Concurrent.ConcurrentQueue<(byte[] frameData, int frameNumber)> recordingQueue = new System.Collections.Concurrent.ConcurrentQueue<(byte[], int)>();
        private readonly System.Threading.SemaphoreSlim recordingSemaphore = new System.Threading.SemaphoreSlim(0);
        private Task recordingWorkerTask;
        private CancellationTokenSource recordingCancellationSource;
        
        // RGBA recording using separate ffmpeg process
        protected RgbaRecorderManager rgbaRecorderManager;

        public FrameTime[] FrameTimes 
        {
            get
            {
                // If RGBA recorder exists and has frame times, use those for accuracy
                if (rgbaRecorderManager != null && rgbaRecorderManager.FrameTimes.Length > 0)
                {
                    return rgbaRecorderManager.FrameTimes;
                }
                // Otherwise use the base class frame times
                return frameTimes?.ToArray() ?? new FrameTime[0];
            }
        }
        public string Filename => recordingFilename;
        public bool RecordNextFrameTime 
        { 
            set 
            { 
                recordNextFrameTime = value; 
            } 
        }
        public bool ManualRecording 
        { 
            get => manualRecording; 
            set => manualRecording = value; 
        }
        public bool Finalising => finalising;

        public void StartRecording(string filename)
        {
            filename += ".mp4";

            if (Recording)
            {
                Tools.Logger.VideoLog.LogDebugCall(this, $"Already recording to {recordingFilename}");
                return;
            }

            recordingFilename = filename;
            recordingStartTime = DateTime.MinValue;  // Don't set timer yet - wait for first frame
            frameCount = 0;
            frameTimes.Clear();
            Recording = true;
            finalising = false;

            // Start RGBA recording with separate ffmpeg process
            // Use measured frame rate if available, otherwise fall back to configured rate
            float recordingFrameRate = frameRateMeasured ? measuredFrameRate : (VideoConfig.VideoMode?.FrameRate ?? 30.0f);
            
            // Debug logging to track frame rate on different platforms
            string platform = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "Windows" : "Mac";
            string rateSource = frameRateMeasured ? "MEASURED" : "CONFIGURED";
            Tools.Logger.VideoLog.LogDebugCall(this, $"RECORDING START [{platform}]: Using {rateSource} frame rate: {recordingFrameRate:F1}fps for recording");
            Tools.Logger.VideoLog.LogDebugCall(this, $"RECORDING START [{platform}]: VideoConfig details - Width: {VideoConfig.VideoMode?.Width}, Height: {VideoConfig.VideoMode?.Height}, ConfiguredRate: {VideoConfig.VideoMode?.FrameRate}fps, MeasuredRate: {measuredFrameRate:F1}fps");
            
            bool started = rgbaRecorderManager.StartRecording(filename, width, height, recordingFrameRate, this);
            if (!started)
            {
                Tools.Logger.VideoLog.LogCall(this, $"Failed to start RGBA recording to {filename}");
                Recording = false;
                return;
            }

            Tools.Logger.VideoLog.LogCall(this, $"Started RGBA recording to {filename}");
        }

        public void StopRecording()
        {
            if (!Recording)
            {
                Tools.Logger.VideoLog.LogCall(this, "Not currently recording");
                return;
            }

            Recording = false;
            finalising = true;

            // Stop RGBA recording
            bool stopped = rgbaRecorderManager.StopRecording();
            if (!stopped)
            {
                Tools.Logger.VideoLog.LogDebugCall(this, $"Warning: RGBA recording may not have stopped cleanly");
            }

            finalising = false;
            Tools.Logger.VideoLog.LogCall(this, $"Stopped RGBA recording to {recordingFilename}");
        }

        protected void InitializeFrameProcessing()
        {
            // Use the dimensions from VideoConfig since ffmpeg is successfully processing
            width = VideoConfig.VideoMode?.Width ?? 640;
            height = VideoConfig.VideoMode?.Height ?? 480;
            
            buffer = new byte[width * height * 4];  // RGBA = 4 bytes per pixel
            rawTextures = new XBuffer<RawTexture>(5, width, height);

            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Initialized with {width}x{height}, buffer size: {buffer.Length} bytes");
            inited = true;
        }

        private void ForceReinitialize()
        {
            Tools.Logger.VideoLog.LogDebugCall(this, "Force re-initializing frame processing");
            inited = false;
            InitializeFrameProcessing();
        }

        private void RestartForRecording()
        {
            // No longer needed - RGBA recording uses separate ffmpeg process
            // Live stream continues unchanged
            Tools.Logger.VideoLog.LogDebugCall(this, "RestartForRecording called - no action needed with RGBA recording");
        }

        public FfmpegFrameSource(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig)
            : base(videoConfig)
        {
            this.ffmpegMediaFramework = ffmpegMediaFramework;

            // Initialize recording fields
            frameTimes = new List<FrameTime>();
            recordingFilename = null;
            recordNextFrameTime = false;
            
            // Initialize RGBA recorder manager
            rgbaRecorderManager = new RgbaRecorderManager(ffmpegMediaFramework);
            manualRecording = false;
            finalising = false;
            recordingStartTime = DateTime.MinValue;
            frameCount = 0;
            
            // PERFORMANCE: Initialize frame processing queue
            frameProcessingQueue = new System.Collections.Concurrent.ConcurrentQueue<(byte[], DateTime, long)>();
            frameProcessingSemaphore = new System.Threading.SemaphoreSlim(0);
            frameProcessingCancellation = new CancellationTokenSource();

            // Set surface format - both platforms output RGBA from ffmpeg
            SurfaceFormat = SurfaceFormat.Color; // RGBA format for consistent color channels across platforms

            if (videoConfig.VideoMode == null || videoConfig.VideoMode.Index == -1)
            {
                videoConfig.VideoMode = ffmpegMediaFramework.DetectOptimalMode(GetModes());    
            }

            // Calculate buffer size based on video mode
            if (videoConfig.VideoMode != null)
            {
                width = videoConfig.VideoMode.Width;
                height = videoConfig.VideoMode.Height;
                
                // For RGBA format: 4 bytes per pixel (ESSENTIAL for pipe output)
                int bufferSize = width * height * 4;
                buffer = new byte[bufferSize];
                
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Initialized buffer: {width}x{height} RGBA = {bufferSize} bytes");
            }
            else
            {
                // Default buffer size if no video mode is set
                buffer = new byte[1280 * 720 * 4]; // Default 1280x720 RGBA
                Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG Using default buffer size: {buffer.Length} bytes");
            }
            
            if (width <= 0 || height <= 0)
            {
                width = 640;
                height = 480;
                Tools.Logger.VideoLog.LogCall(this, $"VideoConfig had invalid dimensions, using fallback 640x480");
            }
            
            buffer = new byte[width * height * 4];
            rawTextures = new XBuffer<RawTexture>(5, width, height);

            // Initialize recording fields
            frameTimes = new List<FrameTime>();
            IsVisible = true;

            Direction = Directions.BottomUp;
        }

        public override void Dispose()
        {
            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Disposing frame source for '{VideoConfig.DeviceName}'");
            
            // Stop recording worker task
            try
            {
                recordingCancellationSource?.Cancel();
                recordingWorkerTask?.Wait(TimeSpan.FromSeconds(5));
                recordingCancellationSource?.Dispose();
                recordingSemaphore?.Dispose();
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, "Error stopping recording worker task", ex);
            }
            
            // Stop and dispose RGBA recorder
            rgbaRecorderManager?.Dispose();
            
            // PERFORMANCE: Stop parallel processing
            frameProcessingCancellation?.Cancel();
            try
            {
                frameProcessingTask?.Wait(1000); // Wait up to 1 second for clean shutdown
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, $"Exception waiting for frame processing task", ex);
            }
            frameProcessingCancellation?.Dispose();
            frameProcessingSemaphore?.Dispose();
            
            Stop();
            
            // Kill ALL ffmpeg processes that might be using this camera - aggressive cleanup (Windows only)
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                KillAllFfmpegProcessesForCamera();
            }
            
            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG Process still running, killing immediately");
                        process.Kill();
                        process.WaitForExit(1000); // Shorter wait in dispose
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG Process already exited");
                    }
                    process.Dispose();
                    Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG Process disposed successfully");
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, "FFMPEG Error disposing process", ex);
                }
                process = null;
            }
            
            base.Dispose();
        }

        private void KillAllFfmpegProcessesForCamera()
        {
            try
            {
                Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG (Windows) Killing ALL ffmpeg processes to ensure camera '{VideoConfig.DeviceName}' is freed");
                
                var ffmpegProcesses = System.Diagnostics.Process.GetProcessesByName("ffmpeg");
                int killedCount = 0;
                
                foreach (var proc in ffmpegProcesses)
                {
                    try
                    {
                        proc.Kill();
                        //if (!proc.HasExited)
                        //{
                        //    //Tools.Logger.VideoLog.LogCallDebugOnly(this, $"FFMPEG (Windows) Killing ffmpeg process {proc.Id}");
                        //    proc.Kill();
                        //    killedCount++;
                        //}
                    }
                    catch (Exception ex)
                    {
                        Tools.Logger.VideoLog.LogException(this, "FFMPEG (Windows) Error killing process {proc.Id}", ex);
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
                
                Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG (Windows) Killed {killedCount} ffmpeg processes for camera cleanup");

                // Small delay to ensure processes are fully terminated (reduced from 200ms to 50ms for faster restarts)
                if (killedCount > 0)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, "FFMPEG (Windows) Error in aggressive cleanup", ex);
            }
        }

        public override bool Start()
        {
            Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG Starting frame source for '{VideoConfig.DeviceName}' at {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height}@{VideoConfig.VideoMode.FrameRate}fps");
            
            // PERFORMANCE: Early hardware decoder warmup to reduce 1-second delay
            Task.Run(() => WarmupHardwareDecoder());
            
            // Ensure we're completely stopped before starting
            if (run)
            {
                Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG Frame source already running, stopping first");
                Stop();
            }
            
            // Reset state for fresh start
            inited = false;
            width = VideoConfig.VideoMode?.Width ?? 640;
            height = VideoConfig.VideoMode?.Height ?? 480;
            buffer = new byte[width * height * 4];
            rawTextures = new XBuffer<RawTexture>(5, width, height);

            ProcessStartInfo processStartInfo = GetProcessStartInfo();

            System.Diagnostics.Debug.Assert(process == null);
            process = new Process();
            process.StartInfo = processStartInfo;
            process.ErrorDataReceived += (s, e) =>
            {
                // Filter out spammy HLS logs to reduce noise
                if (e.Data != null)
                {
                    bool shouldLog = true;
                    
                    // Skip HLS file creation/writing logs
                    if (e.Data.Contains("Opening") && e.Data.Contains("trackside_hls"))
                        shouldLog = false;
                    if (e.Data.Contains("hls @") && e.Data.Contains("stream"))
                        shouldLog = false;
                        
                    // Only log frame progress every 10 seconds (at time ending in 0)
                    if (e.Data.Contains("frame=") && e.Data.Contains("fps=") && e.Data.Contains("time="))
                    {
                        try
                        {
                            var timePart = e.Data.Split(new[] { "time=" }, StringSplitOptions.None)[1].Split(' ')[0];
                            if (!timePart.EndsWith(":00"))
                                shouldLog = false;
                        }
                        catch
                        {
                            // If parsing fails, log it anyway
                        }
                    }
                    
                    if (shouldLog)
                    {
                        Logger.VideoLog.LogDebugCall(this, e.Data);
                    }
                }

                // Initialize immediately when we see the first frame progress update
                // This means ffmpeg is successfully reading from camera and outputting frames
                if (!inited && e.Data != null && (e.Data.Contains("frame=") && e.Data.Contains("fps=")))
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG Frame output detected - initializing frame processing");
                    InitializeFrameProcessing();
                }
                
                // Also try to detect stream lines if they appear (fallback method)
                if (!inited && e.Data != null && e.Data.Contains("Stream") && e.Data.Contains("Video:"))
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, $"Found Stream line: {e.Data}");
                    
                    // Look for resolution pattern like "640x480" 
                    Regex resolutionRegex = new Regex(@"(\d{3,4})x(\d{3,4})");
                    Match resMatch = resolutionRegex.Match(e.Data);
                    
                    if (resMatch.Success)
                    {
                        if (int.TryParse(resMatch.Groups[1].Value, out int w) && int.TryParse(resMatch.Groups[2].Value, out int h) && w > 0 && h > 0)
                        {
                            width = w;
                            height = h;
                            
                            buffer = new byte[width * height * 4];
                            rawTextures = new XBuffer<RawTexture>(5, width, height);

                            Tools.Logger.VideoLog.LogDebugCall(this, $"Stream parsing: Initialized with {width}x{height}, buffer size: {buffer.Length}");
                            inited = true;
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogDebugCall(this, $"Failed to parse resolution: '{resMatch.Groups[1].Value}' x '{resMatch.Groups[2].Value}'");
                        }
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "No resolution pattern found in Stream line");
                    }
                }
                
                // Additional detection for filter_complex output or other FFmpeg messages
                if (!inited && e.Data != null && (e.Data.Contains("Output") || e.Data.Contains("Input") || e.Data.Contains("Duration") || e.Data.Contains("filter_complex")))
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, $"FFmpeg message detected: {e.Data}");
                    // If we see any FFmpeg output, assume it's working and initialize after a short delay
                    Task.Delay(1500).ContinueWith(_ => 
                    {
                        if (!inited && run)
                        {
                            Tools.Logger.VideoLog.LogDebugCall(this, "Fallback initialization after FFmpeg message detection");
                            InitializeFrameProcessing();
                        }
                    });
                }
                
                // Detection for successful filter_complex setup
                if (!inited && e.Data != null && (e.Data.Contains("split") || e.Data.Contains("format=")))
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, $"Filter complex setup detected: {e.Data}");
                    Task.Delay(1000).ContinueWith(_ => 
                    {
                        if (!inited && run)
                        {
                            Tools.Logger.VideoLog.LogDebugCall(this, "Initialization after filter complex setup");
                            InitializeFrameProcessing();
                        }
                    });
                }
            };

            if (process.Start())
            {
                run = true;
                Connected = true;

                // Start recording worker task for async frame processing
                StartRecordingWorkerTask();

                thread = new Thread(Run);
                thread.Name = "ffmpeg - " + VideoConfig.DeviceName;
                thread.Start();

                // PERFORMANCE: Start parallel frame processing task
                frameProcessingTask = Task.Run(async () => await FrameProcessingLoop(frameProcessingCancellation.Token));

                process.BeginErrorReadLine();

                // Windows-specific: Initialize immediately for DirectShow cameras
                // DirectShow takes time to output FFmpeg messages, causing 5-10 second delays
                bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                if (isWindows)
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, "Windows: Scheduling immediate initialization (500ms) for fast camera startup");
                    Task.Delay(500).ContinueWith(_ =>
                    {
                        if (!inited && run)
                        {
                            Tools.Logger.VideoLog.LogDebugCall(this, "Windows: Immediate initialization executing");
                            InitializeFrameProcessing();
                        }
                    });
                }

                // Fallback initialization - reduced timeout for Windows (2s), keep 5s for Mac
                int timeoutMs = isWindows ? 2000 : 5000;
                Task.Delay(timeoutMs).ContinueWith(_ =>
                {
                    if (!inited && run)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, $"Timeout-based fallback initialization ({timeoutMs}ms)");
                        InitializeFrameProcessing();
                    }
                });

                return base.Start();
            }

            return false;
        }

        public override bool Stop()
        {
            Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG Stopping frame source for '{VideoConfig.DeviceName}'");
            run = false;

            // On Windows, when restarting streams, we need to wait for cleanup to complete
            // to avoid the new FFmpeg process failing to access the camera device
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

            if (isWindows)
            {
                // Windows: Synchronous stop to ensure camera is released before restart
                Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG (Windows) Performing synchronous stop for '{VideoConfig.DeviceName}'");
                StopAsync();
                return true;
            }
            else
            {
                // Mac/Linux: Async stop for UI responsiveness
                Task.Run(() => StopAsync());
                return true;
            }
        }

        private void StopAsync()
        {
            try
            {
                // Wait for reading thread to finish
                if (thread != null && thread.IsAlive)
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG Waiting for reading thread to finish");
                    if (!thread.Join(10000))
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG Reading thread didn't finish in time, continuing with cleanup");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG Reading thread finished");
                    }
                    thread = null;
                }

                StopProcessAsync();
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
        }

        private void StopProcessAsync()
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    // Check if we're recording video files (needs graceful shutdown)
                    bool isRecordingWMV = Recording && !string.IsNullOrEmpty(recordingFilename) && recordingFilename.EndsWith(".wmv");
                    bool isRecordingMP4 = Recording && !string.IsNullOrEmpty(recordingFilename) && recordingFilename.EndsWith(".mp4");
                    bool isRecordingMKV = Recording && !string.IsNullOrEmpty(recordingFilename) && recordingFilename.EndsWith(".mkv");
                    
                    if (isRecordingWMV || isRecordingMP4 || isRecordingMKV)
                    {
                        string format = isRecordingWMV ? "WMV" : (isRecordingMP4 ? "MP4" : "MKV");
                        Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG Graceful shutdown for {format} recording (sending SIGINT)");
                        try
                        {
                            // Send SIGINT for graceful shutdown (allows FFmpeg to finalize the file)
                            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                            {
                                // Windows: Use CTRL+C equivalent
                                process.StandardInput.WriteLine("q");
                                process.StandardInput.Flush();
                            }
                            else
                            {
                                // Unix/Linux/macOS: Send SIGINT
                                using (Process killProcess = new Process())
                                {
                                    killProcess.StartInfo.FileName = "kill";
                                    killProcess.StartInfo.Arguments = $"-INT {process.Id}";
                                    killProcess.StartInfo.UseShellExecute = false;
                                    killProcess.StartInfo.CreateNoWindow = true;
                                    killProcess.Start();
                                    killProcess.WaitForExit();
                                }
                            }
                            
                            // Wait for graceful shutdown
                            if (!process.WaitForExit(10000))
                            {
                                Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG Graceful shutdown timeout, forcing kill");
                                process.Kill();
                                process.WaitForExit(10000);
                            }
                            else
                            {
                                Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG Graceful shutdown completed successfully");
                            }
                        }
                        catch (Exception ex)
                        {
                            Tools.Logger.VideoLog.LogException(this, $"FFMPEG Error during graceful shutdown falling back to kill", ex);
                            try
                            {
                                process.Kill();
                                process.WaitForExit(10000);
                            }
                            catch (Exception killEx)
                            {
                                Tools.Logger.VideoLog.LogException(this, $"FFMPEG Error killing process", killEx);
                            }
                        }
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG Immediate kill (not recording WMV or MP4)");
                        try
                        {
                            process.Kill();
                            // Shorter wait for non-recording scenarios (1 second instead of 3)
                            // This speeds up camera restarts significantly on Windows
                            if (!process.WaitForExit(10000))
                            {
                                Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG Process didn't exit after kill - this is unusual");
                            }
                            else
                            {
                                Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG Process killed successfully");
                            }
                        }
                        catch (Exception ex)
                        {
                            Tools.Logger.VideoLog.LogException(this, "FFMPEG Error killing process", ex);
                        }
                    }

                    try
                    {
                        process.CancelOutputRead();
                    }
                    catch (InvalidOperationException)
                    {
                        // No async read operation is in progress, ignore
                    }
                    catch (Exception ex)
                    {
                        Tools.Logger.VideoLog.LogException(this, "FFMPEG Error canceling output read", ex);
                    }
                }
                else if (process != null)
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG Process already exited");
                }
                else
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, "FFMPEG No process to stop");
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
        }

        protected abstract ProcessStartInfo GetProcessStartInfo();

        protected void Run()
        {
            Tools.Logger.VideoLog.LogDebugCall(this, "Camera reading thread started");
            bool loggedInit = false;
            int consecutiveErrors = 0;
            const int maxConsecutiveErrors = 5;
            
            while(run)
            {
                try
                {
                    if (!inited)
                    {
                        System.Threading.Thread.Sleep(10); // Prevent busy waiting
                        continue;
                    }
                    
                    if (!loggedInit)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "Camera reading thread initialized, running at native camera frame rate");
                        loggedInit = true;
                        consecutiveErrors = 0; // Reset error counter on successful init
                    }
                    
                    // Check if process is still running
                    if (process == null || process.HasExited)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "FFmpeg process has exited, stopping camera reading thread");
                        Connected = false;
                        break;
                    }
                    
                    Stream stream = process.StandardOutput.BaseStream;
                    if (stream == null)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "StandardOutput stream is null, waiting...");
                        Thread.Sleep(100);
                        continue;
                    }
                    
                    int totalBytesRead = 0;
                    int bytesToRead = buffer.Length;
                    
                    // Keep reading until we have a complete frame
                    while (totalBytesRead < bytesToRead && run && !process.HasExited)
                    {
                        int bytesRead = stream.Read(buffer, totalBytesRead, bytesToRead - totalBytesRead);
                        if (bytesRead == 0)
                        {
                            // End of stream or process ended
                            if (process == null || process.HasExited)
                            {
                                Tools.Logger.VideoLog.LogDebugCall(this, "FFmpeg process ended during read");
                                break;
                            }
                            Thread.Sleep(10); // Brief pause before retry
                            continue;
                        }
                        totalBytesRead += bytesRead;
                    }
                    
                    if (totalBytesRead == bytesToRead)
                    {
                        // Log only every 1800 frames to reduce spam (every 30 seconds at 60fps)
                        if (FrameProcessNumber % 1800 == 0)
                        {
                            Tools.Logger.VideoLog.LogDebugCall(this, $"CAMERA LOOP: Complete frame read: {totalBytesRead} bytes, frame {FrameProcessNumber}");
                        }
                        
                        // PERFORMANCE: Queue frame for parallel processing to avoid blocking camera read
                        DateTime frameTimestamp = DateTime.UtcNow;
                        byte[] frameDataCopy = new byte[buffer.Length];
                        
                        // Use fast buffer copy for the queue
                        if (buffer.Length > 2 * 1024 * 1024) // 2MB threshold for 4K
                        {
                            Buffer.BlockCopy(buffer, 0, frameDataCopy, 0, buffer.Length);
                        }
                        else
                        {
                            Buffer.BlockCopy(buffer, 0, frameDataCopy, 0, buffer.Length);
                        }
                        
                        frameProcessingQueue.Enqueue((frameDataCopy, frameTimestamp, FrameProcessNumber + 1));
                        frameProcessingSemaphore.Release();
                        
                        // Also call direct processing for immediate display responsiveness
                        ProcessCameraFrame();
                        
                        consecutiveErrors = 0; // Reset error counter on successful frame
                    }
                    else if (totalBytesRead > 0)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, $"Incomplete frame read: {totalBytesRead}/{bytesToRead} bytes");
                        consecutiveErrors++;
                    }
                    else
                    {
                        // No bytes read, might be a temporary issue
                        consecutiveErrors++;
                        Thread.Sleep(50);
                    }
                    
                    // If we're getting too many consecutive errors, log a warning
                    if (consecutiveErrors >= maxConsecutiveErrors)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, $"Warning: {consecutiveErrors} consecutive read errors, process may be having issues");
                        consecutiveErrors = 0; // Reset to avoid spam
                    }
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    Tools.Logger.VideoLog.LogException(this, ex);
                    Tools.Logger.VideoLog.LogDebugCall(this, $"Error in camera reading thread (error #{consecutiveErrors})");
                    
                    if (consecutiveErrors >= maxConsecutiveErrors)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "Too many consecutive errors, stopping camera reading thread");
                        Connected = false;
                        break;
                    }
                    
                    Thread.Sleep(100); // Brief pause before retry
                }
            }
            
            Tools.Logger.VideoLog.LogDebugCall(this, "Camera reading thread finished");
        }

        /// <summary>
        /// PERFORMANCE: Warm up hardware decoder to reduce initial startup delay
        /// </summary>
        private void WarmupHardwareDecoder()
        {
            try
            {
                // Small delay to avoid interfering with main initialization
                Task.Delay(100).Wait();
                
                // Only warmup for hardware accelerated sources
                if (VideoConfig.HardwareDecodeAcceleration || 
                    VideoConfig.VideoMode?.Format == "h264" || 
                    VideoConfig.VideoMode?.Format == "mjpeg")
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, "PERFORMANCE: Pre-warming hardware decoder for faster startup");
                    
                    // Platform-specific hardware decoder initialization
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                    {
                        // macOS VideoToolbox warmup
                        var warmupArgs = "-f avfoundation -list_devices true -i \"\"";
                        try
                        {
                            var warmupOutput = ffmpegMediaFramework.GetFfmpegText(warmupArgs, null);
                            Tools.Logger.VideoLog.LogDebugCall(this, "VideoToolbox warmed up");
                        }
                        catch
                        {
                            // Warmup failed, not critical
                        }
                    }
                    else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    {
                        // Windows hardware decoder warmup (NVDEC/DXVA2)
                        var warmupArgs = "-f dshow -list_devices true -i dummy";
                        try
                        {
                            var warmupOutput = ffmpegMediaFramework.GetFfmpegText(warmupArgs, null);
                            Tools.Logger.VideoLog.LogDebugCall(this, "Windows hardware decoder warmed up");
                        }
                        catch
                        {
                            // Warmup failed, not critical
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Hardware warmup is optional, don't fail startup if it doesn't work
                Tools.Logger.VideoLog.LogException(this, "Hardware decoder warmup failed (non-critical)", ex);
            }
        }

        /// <summary>
        /// PERFORMANCE: Parallel frame processing loop for heavy operations
        /// </summary>
        private async Task FrameProcessingLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && run)
                {
                    // Wait for a frame to be available
                    await frameProcessingSemaphore.WaitAsync(cancellationToken);
                    
                    if (frameProcessingQueue.TryDequeue(out var frameData))
                    {
                        // Process heavy operations in parallel (future extensions)
                        // Currently this just ensures the queue doesn't grow unbounded
                        // In the future, we could add:
                        // - Frame preprocessing
                        // - Quality analysis
                        // - Motion detection
                        // - Other CPU-intensive operations
                        
                        await Task.Yield(); // Yield control to prevent blocking
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
        }

        /// <summary>
        /// Start the recording worker task for async frame processing
        /// </summary>
        private void StartRecordingWorkerTask()
        {
            recordingCancellationSource = new CancellationTokenSource();
            recordingWorkerTask = Task.Run(async () =>
            {
                var cancellationToken = recordingCancellationSource.Token;
                
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Wait for frames to be queued
                        await recordingSemaphore.WaitAsync(cancellationToken);
                        
                        // Process all queued frames
                        while (recordingQueue.TryDequeue(out var frameInfo))
                        {
                            try
                            {
                                rgbaRecorderManager?.WriteFrame(frameInfo.frameData, frameInfo.frameNumber);
                                
                                // Log every 300 frames during recording
                                if (frameInfo.frameNumber % 300 == 0)
                                {
                                    string platform = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "Windows" : "Mac";
                                    Tools.Logger.VideoLog.LogDebugCall(this, $"ASYNC RECORDING [{platform}]: Frame {frameInfo.frameNumber} written to recording");
                                }
                            }
                            catch (Exception ex)
                            {
                                Tools.Logger.VideoLog.LogException(this, ex);
                                Tools.Logger.VideoLog.LogDebugCall(this, $"ASYNC RECORDING: Error writing frame {frameInfo.frameNumber}");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break; // Expected when cancellation is requested
                    }
                    catch (Exception ex)
                    {
                        Tools.Logger.VideoLog.LogException(this, ex);
                    }
                }
                
                Tools.Logger.VideoLog.LogDebugCall(this, "Recording worker task completed");
            }, recordingCancellationSource.Token);
        }

        /// <summary>
        /// RECORDING: Queue frame for async recording (never dropped)
        /// </summary>
        private void QueueFrameForRecording()
        {
            // PERFORMANCE: Use parallel buffer copy for 4K frames to reduce blocking
            byte[] frameData = new byte[buffer.Length];
            
            //// For large 4K frames (>2MB), use parallel copying to improve performance
            //if (buffer.Length > 2 * 1024 * 1024) // 2MB threshold for 4K
            //{
            //    // Use unsafe parallel copy for maximum performance on large buffers
            //    unsafe
            //    {
            //        fixed (byte* src = buffer)
            //        fixed (byte* dst = frameData)
            //        {
            //            // Store pointers in local variables to avoid capture issues
            //            IntPtr srcPtr = new IntPtr(src);
            //            IntPtr dstPtr = new IntPtr(dst);
            //            int totalLength = buffer.Length;
                        
            //            System.Threading.Tasks.Parallel.For(0, Environment.ProcessorCount, i =>
            //            {
            //                int chunkSize = totalLength / Environment.ProcessorCount;
            //                int start = i * chunkSize;
            //                int length = (i == Environment.ProcessorCount - 1) ? 
            //                    totalLength - start : chunkSize;
                            
            //                if (length > 0)
            //                {
            //                    Buffer.MemoryCopy((void*)(srcPtr + start), (void*)(dstPtr + start), length, length);
            //                }
            //            });
            //        }
            //    }
            //}
            //else
            {
                // For smaller frames, use regular Buffer.BlockCopy (faster than Array.Copy)
                Buffer.BlockCopy(buffer, 0, frameData, 0, buffer.Length);
            }
            
            // Queue the frame for async processing
            recordingQueue.Enqueue((frameData, (int)FrameProcessNumber));
            recordingSemaphore.Release(); // Signal worker task
        }

        /// <summary>
        /// REAL-TIME: Check if we should drop this frame for real-time responsiveness
        /// </summary>
        private bool ShouldDropFrameForRealTime()
        {
            // STARTUP FRAME DROPPING: Skip initial frames to get to real-time quickly
            if (FrameProcessNumber <= STARTUP_FRAMES_TO_DROP)
            {
                framesDroppedForRealtime++;
                Tools.Logger.VideoLog.LogDebugCall(this, $"REAL-TIME: Dropping startup frame {FrameProcessNumber}/{STARTUP_FRAMES_TO_DROP} to reach real-time faster");
                return true;
            }
            
            // DYNAMIC FRAME DROPPING: Check if we're falling behind real-time
            DateTime now = DateTime.UtcNow;
            displayFrameTimestamps.Enqueue(now);
            
            // Keep only last 5 frame timestamps for latency calculation
            while (displayFrameTimestamps.Count > 5)
                displayFrameTimestamps.Dequeue();
            
            // If we have enough samples, check if we're lagging behind
            if (displayFrameTimestamps.Count >= 5)
            {
                var oldestFrameTime = displayFrameTimestamps.Peek();
                var currentLatency = (now - oldestFrameTime).TotalMilliseconds;
                
                // If display latency exceeds threshold, drop this frame to catch up
                if (currentLatency > MAX_DISPLAY_LATENCY_MS)
                {
                    framesDroppedForRealtime++;
                    
                    // Log every 30 dropped frames to avoid spam
                    if (framesDroppedForRealtime % 30 == 0)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, $"REAL-TIME: Display latency {currentLatency:F0}ms > {MAX_DISPLAY_LATENCY_MS}ms, dropping frame {FrameProcessNumber} (total dropped: {framesDroppedForRealtime})");
                    }
                    
                    return true;
                }
            }
            
            return false; // Don't drop this frame
        }

        /// <summary>
        /// Update frame timing statistics (for dropped frames)
        /// </summary>
        private void UpdateFrameTimingStats()
        {
            DateTime currentFrameTime = DateTime.UtcNow;
            
            // Only update timing stats, don't do full processing
            if (FrameProcessNumber % 60 == 0) // Check every 60 frames
            {
                if (lastFrameTime != DateTime.MinValue)
                {
                    double actualInterval = (currentFrameTime - lastFrameTime).TotalMilliseconds / 60.0;
                    double actualFps = actualInterval > 0 ? 1000.0 / actualInterval : 0;
                    double configuredFps = VideoConfig.VideoMode?.FrameRate ?? 30.0f;
                    
                    string platform = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "Windows" : "Mac";
                    Tools.Logger.VideoLog.LogDebugCall(this, $"FRAME TIMING [{platform}]: Frame {FrameProcessNumber} - Configured: {configuredFps:F1}fps, Actual: {actualFps:F2}fps (some frames dropped for real-time)");
                }
                lastFrameTime = currentFrameTime;
            }
        }

        /// <summary>
        /// DISPLAY: Process frame for display (after frame dropping check)
        /// </summary>
        private void ProcessDisplayFrame()
        {
            // Track actual frame timing to detect frame rate discrepancies
            DateTime currentFrameTime = DateTime.UtcNow;
            
            if (FrameProcessNumber % 60 == 0) // Check every 60 frames (about 2 seconds)
            {
                double actualInterval = lastFrameTime != DateTime.MinValue ? (currentFrameTime - lastFrameTime).TotalMilliseconds / 60.0 : 0;
                double actualFps = actualInterval > 0 ? 1000.0 / actualInterval : 0;
                double configuredFps = VideoConfig.VideoMode?.FrameRate ?? 30.0f;
                
                string platform = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "Windows" : "Mac";
                Tools.Logger.VideoLog.LogDebugCall(this, $"CAMERA TIMING [{platform}]: Frame {FrameProcessNumber} - Configured: {configuredFps:F1}fps, Actual: {actualFps:F2}fps, Real-time optimized");
                
                // Update measured frame rate with higher precision after initial stabilization
                if (FrameProcessNumber >= 180 && actualFps > 0) // Wait longer for more accurate measurement (6 seconds)
                {
                    // Use rolling average for more stable measurement
                    if (!frameRateMeasured)
                    {
                        measuredFrameRate = (float)actualFps;
                        frameRateMeasured = true;
                        Tools.Logger.VideoLog.LogDebugCall(this, $"CAMERA MEASUREMENT [{platform}]: Initial measured frame rate: {measuredFrameRate:F3}fps (after {FrameProcessNumber} frames)");
                    }
                    else
                    {
                        // Use exponential smoothing for ongoing measurement refinement
                        float alpha = 0.1f; // Smoothing factor
                        measuredFrameRate = alpha * (float)actualFps + (1 - alpha) * measuredFrameRate;
                        
                        // Log every 300 frames (10 seconds) to show ongoing refinement
                        if (FrameProcessNumber % 300 == 0)
                        {
                            Tools.Logger.VideoLog.LogDebugCall(this, $"CAMERA MEASUREMENT [{platform}]: Refined measured frame rate: {measuredFrameRate:F3}fps (frame {FrameProcessNumber})");
                        }
                    }
                }
                
                lastFrameTime = currentFrameTime;
            }
            
            // Prepare frame for game engine display
            PrepareFrameForDisplay();
            
            // Notify the game engine that a new frame is available
            NotifyReceivedFrame();
        }

        /// <summary>
        /// CAMERA LOOP: Process frame at camera's native rate - independent from game loop
        /// This method runs in the camera reading thread and handles recording directly
        /// </summary>
        protected virtual void ProcessCameraFrame()
        {
            FrameProcessNumber++;
            
            // REAL-TIME OPTIMIZATION: Recording runs async, display processes immediately for minimum latency
            // Recording gets every frame for accurate capture, regardless of display frame dropping
            bool isRecording = Recording && rgbaRecorderManager.IsRecording;
            
            // DISPLAY OPTIMIZATION: Check if we should drop this frame for real-time display
            bool shouldDropForDisplay = ShouldDropFrameForRealTime();
            
            if (isRecording)
            {
                // QUEUED RECORDING: Queue frame for async processing without blocking
                // This ensures the camera loop moves forward as quickly as possible
                QueueFrameForRecording();
            }
            
            if (!shouldDropForDisplay)
            {
                // REAL-TIME DISPLAY: Process display immediately for minimum latency
                ProcessDisplayFrame();
            }
            else
            {
                // DROP FRAME: Skip display processing but update frame timing statistics
                UpdateFrameTimingStats();
            }
        }

        /// <summary>
        /// Prepare frame data for game engine display (runs in camera loop)
        /// </summary>
        private void PrepareFrameForDisplay()
        {
            var currentRawTextures = rawTextures;
            if (currentRawTextures != null)
            {
                RawTexture frame;
                if (currentRawTextures.GetWritable(out frame))
                {
                    // Calculate SampleTime for game engine
                    if (this is FfmpegVideoFileFrameSource videoFileSource && VideoConfig?.VideoMode?.FrameRate > 0)
                    {
                        // Use frame-based calculation for accurate timeline positioning
                        double frameBasedTime = FrameProcessNumber / VideoConfig.VideoMode.FrameRate;
                        SampleTime = (long)(frameBasedTime * 10000000); // Convert seconds to ticks
                        
                        // Log every 600 frames for video files
                        if (FrameProcessNumber % 600 == 0)
                        {
                            double actualMediaTime = videoFileSource.MediaTime.TotalSeconds;
                            double videoLength = videoFileSource.Length.TotalSeconds;
                            Tools.Logger.VideoLog.LogDebugCall(this, $"CAMERA LOOP: Video SampleTime - Frame {FrameProcessNumber}, FrameTime={frameBasedTime:F2}s, MediaTime={actualMediaTime:F2}s, VideoLength={videoLength:F2}s");
                        }
                    }
                    
                    // Convert byte[] to IntPtr for SetData call
                    System.Runtime.InteropServices.GCHandle handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try
                    {
                        IntPtr bufferPtr = handle.AddrOfPinnedObject();
                        frame.SetData(bufferPtr, SampleTime, FrameProcessNumber);
                    }
                    finally
                    {
                        handle.Free();
                    }
                    
                    // Make frame available for game engine display
                    currentRawTextures.WriteOne(frame);
                    
                    // Log every 600 frames to track display frame preparation
                    if (FrameProcessNumber % 600 == 0)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, $"CAMERA LOOP: Frame {FrameProcessNumber} prepared for game engine display");
                    }
                }
            }
        }

        /// <summary>
        /// GAME LOOP: Process frame for display only - called by game engine when it needs frames
        /// This is now decoupled from camera timing and recording
        /// </summary>
        protected override void ProcessImage()
        {
            // Legacy frame timing collection - only for non-RGBA recording sources
            if (recordNextFrameTime && !VideoConfig.FilePath.Contains("hls"))
            {
                // For non-RGBA recording sources, maintain legacy behavior
                if (!Recording || !rgbaRecorderManager.IsRecording)
                {
                    if (frameTimes == null)
                    {
                        frameTimes = new List<FrameTime>();
                    }
                    
                    DateTime frameTime = UnifiedFrameTimingManager.GetHighPrecisionTimestamp();
                    
                    // Start recording timer on first actual frame to eliminate FFmpeg initialization delay
                    if (recordingStartTime == DateTime.MinValue)
                    {
                        recordingStartTime = frameTime;
                        Tools.Logger.VideoLog.LogDebugCall(this, $"GAME LOOP: Recording timer started on first frame: {recordingStartTime:HH:mm:ss.fff}");
                    }
                    
                    // Use unified frame timing logic for consistency across platforms
                    var frameTimeEntry = UnifiedFrameTimingManager.CreateFrameTime(
                        (int)FrameProcessNumber, frameTime, recordingStartTime);
                    frameTimes.Add(frameTimeEntry);
                    
                    frameCount++;
                    
                    // Log frame timing only every 120 frames during recording
                    if (frameCount % 120 == 0)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, $"GAME LOOP: Legacy recorded frame {FrameProcessNumber} at {frameTime:HH:mm:ss.fff}, offset: {(frameTime - recordingStartTime).TotalSeconds:F3}s");
                    }
                }
                
                recordNextFrameTime = false;
            }
            else if (recordNextFrameTime)
            {
                // Reset the flag for HLS composite sources
                recordNextFrameTime = false;
            }
            
            // Fire the game engine frame event (this is what the game engine waits for)
            base.ProcessImage();
        }
    }
}
