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

        // Recording implementation
        protected string recordingFilename;
        private List<FrameTime> frameTimes;
        private bool recordNextFrameTime;
        private bool manualRecording;
        private bool finalising;
        private DateTime recordingStartTime;
        private long frameCount;
        
        // RGBA recording using separate ffmpeg process
        protected RgbaRecorderManager rgbaRecorderManager;

        public new bool IsVisible { get; set; }
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
        public new bool Recording { get; private set; }
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
            if (Recording)
            {
                Tools.Logger.VideoLog.LogCall(this, $"Already recording to {recordingFilename}");
                return;
            }

            recordingFilename = filename;
            recordingStartTime = DateTime.MinValue;  // Don't set timer yet - wait for first frame
            frameCount = 0;
            frameTimes.Clear();
            Recording = true;
            finalising = false;

            // Start RGBA recording with separate ffmpeg process
            // Use a reasonable default frame rate, the recorder will detect the actual rate
            float initialFrameRate = VideoConfig.VideoMode?.FrameRate ?? 30.0f;
            bool started = rgbaRecorderManager.StartRecording(filename, width, height, initialFrameRate, this);
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

            Tools.Logger.VideoLog.LogCall(this, $"Stopping RGBA recording to {recordingFilename}");
            Recording = false;
            finalising = true;

            // Stop RGBA recording
            bool stopped = rgbaRecorderManager.StopRecording();
            if (!stopped)
            {
                Tools.Logger.VideoLog.LogCall(this, $"Warning: RGBA recording may not have stopped cleanly");
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
            Tools.Logger.VideoLog.LogCall(this, "Force re-initializing frame processing");
            inited = false;
            InitializeFrameProcessing();
        }

        private void RestartForRecording()
        {
            // No longer needed - RGBA recording uses separate ffmpeg process
            // Live stream continues unchanged
            Tools.Logger.VideoLog.LogCall(this, "RestartForRecording called - no action needed with RGBA recording");
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

            // Set surface format based on platform
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                SurfaceFormat = SurfaceFormat.Color; // More widely supported on macOS
            }
            else
            {
                SurfaceFormat = SurfaceFormat.Bgr32; // Original Windows format
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
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Using default buffer size: {buffer.Length} bytes");
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
        }

        public override void Dispose()
        {
            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Disposing frame source for '{VideoConfig.DeviceName}'");
            
            // Stop and dispose RGBA recorder
            rgbaRecorderManager?.Dispose();
            
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
                        Tools.Logger.VideoLog.LogCall(this, "FFMPEG Process still running, killing immediately");
                        process.Kill();
                        process.WaitForExit(1000); // Shorter wait in dispose
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "FFMPEG Process already exited");
                    }
                    process.Dispose();
                    Tools.Logger.VideoLog.LogCall(this, "FFMPEG Process disposed successfully");
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Error disposing process: {ex.Message}");
                }
                process = null;
            }
            
            base.Dispose();
        }

        private void KillAllFfmpegProcessesForCamera()
        {
            try
            {
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG (Windows) Killing ALL ffmpeg processes to ensure camera '{VideoConfig.DeviceName}' is freed");
                
                var ffmpegProcesses = System.Diagnostics.Process.GetProcessesByName("ffmpeg");
                int killedCount = 0;
                
                foreach (var proc in ffmpegProcesses)
                {
                    try
                    {
                        proc.Kill();
                        //if (!proc.HasExited)
                        //{
                        //    //Tools.Logger.VideoLog.LogCall(this, $"FFMPEG (Windows) Killing ffmpeg process {proc.Id}");
                        //    proc.Kill();
                        //    killedCount++;
                        //}
                    }
                    catch (Exception ex)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"FFMPEG (Windows) Error killing process {proc.Id}: {ex.Message}");
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
                
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG (Windows) Killed {killedCount} ffmpeg processes for camera cleanup");
                
                // Small delay to ensure processes are fully terminated
                if (killedCount > 0)
                {
                    System.Threading.Thread.Sleep(200);
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG (Windows) Error in aggressive cleanup: {ex.Message}");
            }
        }

        public override bool Start()
        {
            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Starting frame source for '{VideoConfig.DeviceName}' at {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height}@{VideoConfig.VideoMode.FrameRate}fps");
            
            // Ensure we're completely stopped before starting
            if (run)
            {
                Tools.Logger.VideoLog.LogCall(this, "FFMPEG Frame source already running, stopping first");
                Stop();
            }
            
            // Reset state for fresh start
            inited = false;
            width = VideoConfig.VideoMode?.Width ?? 640;
            height = VideoConfig.VideoMode?.Height ?? 480;
            buffer = new byte[width * height * 4];
            rawTextures = new XBuffer<RawTexture>(5, width, height);

            ProcessStartInfo processStartInfo = GetProcessStartInfo();

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
                        Logger.VideoLog.LogCall(this, e.Data);
                    }
                }

                // Initialize immediately when we see the first frame progress update
                // This means ffmpeg is successfully reading from camera and outputting frames
                if (!inited && e.Data != null && (e.Data.Contains("frame=") && e.Data.Contains("fps=")))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Frame output detected - initializing frame processing");
                    InitializeFrameProcessing();
                }
                
                // Also try to detect stream lines if they appear (fallback method)
                if (!inited && e.Data != null && e.Data.Contains("Stream") && e.Data.Contains("Video:"))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Found Stream line: {e.Data}");
                    
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

                            Tools.Logger.VideoLog.LogCall(this, $"Stream parsing: Initialized with {width}x{height}, buffer size: {buffer.Length}");
                            inited = true;
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"Failed to parse resolution: '{resMatch.Groups[1].Value}' x '{resMatch.Groups[2].Value}'");
                        }
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "No resolution pattern found in Stream line");
                    }
                }
                
                // Additional detection for filter_complex output or other FFmpeg messages
                if (!inited && e.Data != null && (e.Data.Contains("Output") || e.Data.Contains("Input") || e.Data.Contains("Duration") || e.Data.Contains("filter_complex")))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFmpeg message detected: {e.Data}");
                    // If we see any FFmpeg output, assume it's working and initialize after a short delay
                    Task.Delay(1500).ContinueWith(_ => 
                    {
                        if (!inited && run)
                        {
                            Tools.Logger.VideoLog.LogCall(this, "Fallback initialization after FFmpeg message detection");
                            InitializeFrameProcessing();
                        }
                    });
                }
                
                // Detection for successful filter_complex setup
                if (!inited && e.Data != null && (e.Data.Contains("split") || e.Data.Contains("format=")))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Filter complex setup detected: {e.Data}");
                    Task.Delay(1000).ContinueWith(_ => 
                    {
                        if (!inited && run)
                        {
                            Tools.Logger.VideoLog.LogCall(this, "Initialization after filter complex setup");
                            InitializeFrameProcessing();
                        }
                    });
                }
            };

            if (process.Start())
            {
                run = true;
                Connected = true;

                thread = new Thread(Run);
                thread.Name = "ffmpeg - " + VideoConfig.DeviceName;
                thread.Start();

                process.BeginErrorReadLine();
                
                // Fallback initialization after 5 seconds if not already initialized
                Task.Delay(5000).ContinueWith(_ => 
                {
                    if (!inited && run)
                    {
                        Tools.Logger.VideoLog.LogCall(this, "Timeout-based fallback initialization");
                        InitializeFrameProcessing();
                    }
                });
                
                return base.Start();
            }

            return false;
        }

        public override bool Stop()
        {
            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Stopping frame source for '{VideoConfig.DeviceName}'");
            run = false;
            
            // For UI responsiveness, perform stop operations asynchronously
            Task.Run(() => StopAsync());
            
            // Return immediately to prevent UI lockup
            return true;
        }

        private void StopAsync()
        {
            try
            {
                // Wait for reading thread to finish
                if (thread != null && thread.IsAlive)
                {
                    Tools.Logger.VideoLog.LogCall(this, "FFMPEG Waiting for reading thread to finish");
                    if (!thread.Join(3000))
                    {
                        Tools.Logger.VideoLog.LogCall(this, "FFMPEG Reading thread didn't finish in time, continuing with cleanup");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "FFMPEG Reading thread finished");
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
                    // Check if we're recording WMV or MP4 (needs graceful shutdown)
                    bool isRecordingWMV = Recording && !string.IsNullOrEmpty(recordingFilename) && recordingFilename.EndsWith(".wmv");
                    bool isRecordingMP4 = Recording && !string.IsNullOrEmpty(recordingFilename) && recordingFilename.EndsWith(".mp4");
                    
                    if (isRecordingWMV || isRecordingMP4)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Graceful shutdown for {(isRecordingWMV ? "WMV" : "MP4")} recording (sending SIGINT)");
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
                                var killProcess = new Process();
                                killProcess.StartInfo.FileName = "kill";
                                killProcess.StartInfo.Arguments = $"-INT {process.Id}";
                                killProcess.StartInfo.UseShellExecute = false;
                                killProcess.StartInfo.CreateNoWindow = true;
                                killProcess.Start();
                                killProcess.WaitForExit();
                            }
                            
                            // Wait for graceful shutdown
                            if (!process.WaitForExit(5000))
                            {
                                Tools.Logger.VideoLog.LogCall(this, "FFMPEG Graceful shutdown timeout, forcing kill");
                                process.Kill();
                                process.WaitForExit(3000);
                            }
                            else
                            {
                                Tools.Logger.VideoLog.LogCall(this, "FFMPEG Graceful shutdown completed successfully");
                            }
                        }
                        catch (Exception ex)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Error during graceful shutdown: {ex.Message}, falling back to kill");
                            try
                            {
                                process.Kill();
                                process.WaitForExit(3000);
                            }
                            catch (Exception killEx)
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Error killing process: {killEx.Message}");
                            }
                        }
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "FFMPEG Immediate kill (not recording WMV or MP4)");
                        try
                        {
                            process.Kill();
                            if (!process.WaitForExit(3000))
                            {
                                Tools.Logger.VideoLog.LogCall(this, "FFMPEG Process didn't exit after kill - this is unusual");
                            }
                            else
                            {
                                Tools.Logger.VideoLog.LogCall(this, "FFMPEG Process killed successfully");
                            }
                        }
                        catch (Exception ex)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Error killing process: {ex.Message}");
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
                        Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Error canceling output read: {ex.Message}");
                    }
                }
                else if (process != null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "FFMPEG Process already exited");
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, "FFMPEG No process to stop");
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
            Tools.Logger.VideoLog.LogCall(this, "Reading thread started");
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
                        Tools.Logger.VideoLog.LogCall(this, "Reading thread initialized, starting to read frames");
                        loggedInit = true;
                        consecutiveErrors = 0; // Reset error counter on successful init
                    }
                    
                    // Check if process is still running
                    if (process == null || process.HasExited)
                    {
                        Tools.Logger.VideoLog.LogCall(this, "FFmpeg process has exited, stopping reading thread");
                        break;
                    }
                    
                    Stream stream = process.StandardOutput.BaseStream;
                    if (stream == null)
                    {
                        Tools.Logger.VideoLog.LogCall(this, "StandardOutput stream is null, waiting...");
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
                            if (process.HasExited)
                            {
                                Tools.Logger.VideoLog.LogCall(this, "FFmpeg process ended during read");
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
                            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Complete frame read: {totalBytesRead} bytes, processing frame {FrameProcessNumber}");
                        }
                        ProcessImage();
                        NotifyReceivedFrame();
                        consecutiveErrors = 0; // Reset error counter on successful frame
                    }
                    else if (totalBytesRead > 0)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Incomplete frame read: {totalBytesRead}/{bytesToRead} bytes");
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
                        Tools.Logger.VideoLog.LogCall(this, $"Warning: {consecutiveErrors} consecutive read errors, process may be having issues");
                        consecutiveErrors = 0; // Reset to avoid spam
                    }
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    Tools.Logger.VideoLog.LogException(this, ex);
                    Tools.Logger.VideoLog.LogCall(this, $"Error in reading thread (error #{consecutiveErrors})");
                    
                    if (consecutiveErrors >= maxConsecutiveErrors)
                    {
                        Tools.Logger.VideoLog.LogCall(this, "Too many consecutive errors, stopping reading thread");
                        break;
                    }
                    
                    Thread.Sleep(100); // Brief pause before retry
                }
            }
            
            Tools.Logger.VideoLog.LogCall(this, "Reading thread finished");
        }

        protected override void ProcessImage()
        {
            var currentRawTextures = rawTextures;
            if (currentRawTextures != null)
            {
                RawTexture frame;
                if (currentRawTextures.GetWritable(out frame))
                {
                    FrameProcessNumber++;
                    
                    // Log only every 600 frames to reduce spam (every 10 seconds at 60fps)
                    if (FrameProcessNumber % 600 == 0)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Processing frame {FrameProcessNumber}, buffer size: {buffer.Length} bytes");
                    }
                    
                    // Legacy frame timing collection - DISABLED for RGBA recording
                    // The RGBA recorder now handles ALL frame timing collection internally
                    // This ensures complete coverage from recording start to stop button
                    if (recordNextFrameTime && !VideoConfig.FilePath.Contains("hls"))
                    {
                        // For non-RGBA recording sources, maintain legacy behavior
                        if (!Recording || !rgbaRecorderManager.IsRecording)
                        {
                            if (frameTimes == null)
                            {
                                frameTimes = new List<FrameTime>();
                            }
                            
                            DateTime frameTime = DateTime.Now;
                            
                            // Start recording timer on first actual frame to eliminate FFmpeg initialization delay
                            if (recordingStartTime == DateTime.MinValue)
                            {
                                recordingStartTime = frameTime;
                                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Recording timer started on first frame: {recordingStartTime:HH:mm:ss.fff}");
                            }
                            
                            frameTimes.Add(new FrameTime
                            {
                                Frame = (int)FrameProcessNumber,
                                Time = frameTime,
                                Seconds = (float)(frameTime - recordingStartTime).TotalSeconds
                            });
                            
                            frameCount++;
                            
                            // Log frame timing only every 120 frames during recording
                            if (frameCount % 120 == 0)
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Recorded frame {FrameProcessNumber} at {frameTime:HH:mm:ss.fff}, offset: {(frameTime - recordingStartTime).TotalSeconds:F3}s");
                            }
                        }
                        else
                        {
                            // RGBA recording active - frame timing handled by RgbaRecorderManager
                            // Log every 120 frames to confirm RGBA timing collection
                            if (FrameProcessNumber % 120 == 0)
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Frame {FrameProcessNumber} - RGBA recorder handling timing collection");
                            }
                        }
                        
                        recordNextFrameTime = false;
                    }
                    else if (recordNextFrameTime)
                    {
                        // Reset the flag for HLS composite sources
                        recordNextFrameTime = false;
                    }
                    
                    // Copy buffer to texture
                    // For video file playback, calculate SampleTime based on frame rate and current position
                    if (this is FfmpegVideoFileFrameSource videoFileSource && VideoConfig?.VideoMode?.FrameRate > 0)
                    {
                        // Use frame-based calculation for accurate timeline positioning
                        double frameBasedTime = FrameProcessNumber / VideoConfig.VideoMode.FrameRate;
                        double actualMediaTime = videoFileSource.MediaTime.TotalSeconds;
                        
                        // Use the frame-based time for timeline markers
                        SampleTime = (long)(frameBasedTime * 10000000); // Convert seconds to ticks
                        
                        // Log every 120 frames to see what's happening
                        if (FrameProcessNumber % 120 == 0)
                        {
                            double videoLength = videoFileSource.Length.TotalSeconds;
                            Tools.Logger.VideoLog.LogCall(this, $"SampleTime Debug: Frame {FrameProcessNumber}, FrameTime={frameBasedTime:F2}s, MediaTime={actualMediaTime:F2}s, VideoLength={videoLength:F2}s, SampleTime={SampleTime} (using FrameTime)");
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
                    
                    currentRawTextures.WriteOne(frame);
                    
                    // Write RGBA frame to recording if active
                    if (Recording && rgbaRecorderManager.IsRecording)
                    {
                        // Write the RGBA frame data to the recorder
                        // The RGBA recorder handles ALL frame timing collection internally
                        // This ensures XML timing covers the complete recording session
                        byte[] frameData = new byte[buffer.Length];
                        Array.Copy(buffer, frameData, buffer.Length);
                        
                        // RGBA recorder collects timing for ALL frames from start to stop
                        rgbaRecorderManager.WriteFrame(frameData, (int)FrameProcessNumber);
                    }
                    
                    // Enhanced logging for video file sources to track frame writing
                    bool isVideoFile = this.GetType().Name.Contains("VideoFile");
                    if (isVideoFile && FrameProcessNumber % 10 == 0)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"VIDEO WRITE: Wrote frame {FrameProcessNumber} to rawTextures buffer, SampleTime: {SampleTime}");
                    }
                }
            }
            base.ProcessImage();
        }
    }
}
