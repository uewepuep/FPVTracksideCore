using ImageServer;
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
    public class FfmpegVideoFileFrameSource : FfmpegFrameSource, IPlaybackFrameSource
    {
        private DateTime startTime; // Current playback start time (for timing calculations)
        private DateTime originalVideoStartTime; // Original video file start time (preserved for seeking)
        private TimeSpan length;
        private double frameRate;
        private PlaybackSpeed playbackSpeed;
        private float slowSpeedFactor = 0.1f; // Default slow speed
        private bool repeat;
        private bool bounceRepeat;
        private bool reversed;
        private bool isAtEnd;
        private TimeSpan mediaTime;
        private int currentFrameIndex;
        private long totalFrames;
        private bool isSeekOperation;
        private int seekStabilizationFrames;
        private string currentSeekArgs; // Temporary storage for seek FFmpeg arguments

        public DateTime StartTime => originalVideoStartTime != DateTime.MinValue ? originalVideoStartTime : startTime;
        public TimeSpan Length 
        { 
            get 
            { 
                Tools.Logger.VideoLog.LogDebugCall(this, $"Length property accessed: {length}");
                return length; 
            } 
        }
        public double FrameRate => frameRate;
        public PlaybackSpeed PlaybackSpeed 
        { 
            get => playbackSpeed; 
            set 
            {
                if (playbackSpeed != value)
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, $"PlaybackSpeed changed from {playbackSpeed} to {value}");
                    playbackSpeed = value;
                    
                    // If currently playing, restart with new speed settings
                    if (State == States.Running)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "Restarting video with new playback speed");
                        RestartWithSeek(mediaTime);
                    }
                }
            }
        }
        public bool Repeat 
        { 
            get => repeat; 
            set => repeat = value; 
        }
        public bool BounceRepeat 
        { 
            get => bounceRepeat; 
            set => bounceRepeat = value; 
        }
        public bool Reversed 
        { 
            get => reversed; 
            set => reversed = value; 
        }
        public bool IsAtEnd => isAtEnd;
        public float SlowSpeedFactor 
        { 
            get => slowSpeedFactor; 
            set => slowSpeedFactor = Math.Max(0.1f, Math.Min(1.0f, value)); 
        }

        public TimeSpan MediaTime
        {
            get => mediaTime;
            set
            {
                mediaTime = value;
                if (mediaTime >= length)
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, $"VIDEO END DETECTED (setter): mediaTime={mediaTime.TotalSeconds:F1}s >= length={length.TotalSeconds:F1}s, setting isAtEnd=true");
                    isAtEnd = true;
                    if (repeat)
                    {
                        mediaTime = TimeSpan.Zero;
                        isAtEnd = false;
                    }
                }
                else
                {
                    isAtEnd = false;
                }
            }
        }

        public DateTime CurrentTime 
        {
            get
            {
                // Return the absolute timeline position for the progress bar
                // This should be the original video start time + current media offset
                if (originalVideoStartTime != DateTime.MinValue)
                {
                    return originalVideoStartTime + mediaTime;
                }
                else
                {
                    // Fallback to old calculation if original start time not available
                    return startTime + mediaTime;
                }
            }
        }

        public FfmpegVideoFileFrameSource(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig)
            : base(ffmpegMediaFramework, videoConfig)
        {
            
            // Initialize playback properties
            startTime = DateTime.Now; // Default start time for playback timing
            originalVideoStartTime = DateTime.Now; // Initialize original video start time
            length = TimeSpan.Zero;
            frameRate = 30.0; // Default frame rate
            playbackSpeed = PlaybackSpeed.Normal;
            repeat = false;
            bounceRepeat = false;
            reversed = false;
            isAtEnd = false;
            mediaTime = TimeSpan.Zero;
            currentFrameIndex = 0;
            totalFrames = 0;
            
            // Load frame times from .recordinfo.xml file if it exists
            LoadFrameTimesFromRecordInfo();
        }

        // Helper method to build video filter string based on VideoConfig settings
        private string BuildVideoFilter()
        {
            List<string> filters = new List<string>();

            // Platform-specific flip handling
            bool isMac = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);

            // For video files, apply filters based on platform
            if (isMac)
            {
                // Mac: apply vflip only when explicitly requested (same as Windows)
                if (VideoConfig.Flipped)
                    filters.Add("vflip");
            }
            else
            {
                // Windows: only apply filters when explicitly requested
                if (VideoConfig.Flipped)
                    filters.Add("vflip");
            }

            if (VideoConfig.Mirrored)
                filters.Add("hflip");

            return filters.Any() ? string.Join(",", filters) : "";
        }

        protected override ProcessStartInfo GetProcessStartInfo()
        {
            // If we have seek args, use them instead of generating normal args
            if (!string.IsNullOrEmpty(currentSeekArgs))
            {
                Tools.Logger.VideoLog.LogDebugCall(this, $"Using seek FFmpeg args: {currentSeekArgs}");
                return ffmpegMediaFramework.GetProcessStartInfo(currentSeekArgs);
            }
            
            // Normal playback args
            string filePath = VideoConfig.FilePath;
            if (!Path.IsPathRooted(filePath))
            {
                filePath = Path.GetFullPath(filePath);
            }
            if (!File.Exists(filePath))
            {
                Tools.Logger.VideoLog.LogDebugCall(this, $"Video file does not exist: {filePath}");
                throw new FileNotFoundException($"Video file not found: {filePath}");
            }
            bool isWMV = filePath.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase);
            
            // Get just the filename for logging
            string fileName = Path.GetFileName(filePath);
            
            Tools.Logger.VideoLog.LogDebugCall(this, $"Video file playback: {fileName}");

            // Build FFmpeg command for video file playback with interactive seeking support
            // Use proper settings for smooth video file playback

            // Build video filter chain based on VideoConfig settings
            string videoFilter = BuildVideoFilter();
            string reFlag = "-re"; // Default: use -re for proper timing
            
            string ffmpegArgs = $"{(string.IsNullOrEmpty(reFlag) ? "" : reFlag + " ")}" +  // Read input at native frame rate (only for normal speed)
                               $"-i \"{filePath}\" " +
                               $"-fflags +genpts " +  // Generate presentation timestamps
                               $"-avoid_negative_ts make_zero " +  // Handle negative timestamps
                               $"-threads 4 " +
                               $"-an " +  // No audio
                               $"{(string.IsNullOrEmpty(videoFilter) ? "" : $"-vf \"{videoFilter}\" ")}" +  // Apply video filters if any
                               $"-pix_fmt rgba " +
                               $"-f rawvideo pipe:1";

            Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG Video File Playback ({fileName}): {ffmpegArgs}");
            return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
        }

        public override bool Start()
        {
            Tools.Logger.VideoLog.LogDebugCall(this, "PLAYBACK ENGINE: ffmpeg BINARY (external process)");
            
            // This is normal playback, not a seek operation
            isSeekOperation = false;
            
            // Initialize playback state
            InitializePlaybackState();
            
            // Try to get video file information first
            if (GetVideoFileInfo())
            {
                if (VideoConfig.VideoMode != null)
                {
                    width = VideoConfig.VideoMode.Width;
                    height = VideoConfig.VideoMode.Height;
                    buffer = new byte[width * height * 4];
                    rawTextures = new XBuffer<RawTexture>(5, width, height);
                    inited = true;
                }
            }
            
            // Always try to start, even if we couldn't get file info
            bool result = base.Start();
            
            if (result)
            {
                // Override the error data received handler for video file specific detection
                if (process != null)
                {
                    process.ErrorDataReceived += VideoFileErrorDataReceived;
                    try
                    {
                        process.EnableRaisingEvents = true;
                        process.Exited += (s, e) =>
                        {
                            // Clamp media time to exact length when process ends
                            Tools.Logger.VideoLog.LogDebugCall(this, $"VIDEO FILE: Process exited - clamping mediaTime to length {length.TotalSeconds:F3}s");
                            mediaTime = length;
                            isAtEnd = true;
                        };
                    }
                    catch { }
                    
                    // Set a timeout to check if initialization happened
                    Task.Delay(10000).ContinueWith(_ => 
                    {
                        if (!inited && run)
                        {
                            Tools.Logger.VideoLog.LogDebugCall(this, "Video file playback timeout - forcing initialization");
                            InitializeFrameProcessing();
                        }
                    });
                }
            }
            
            return result;
        }

        private void InitializePlaybackState()
        {
            startTime = DateTime.MinValue;
            mediaTime = TimeSpan.Zero;
            isAtEnd = false;
            FrameProcessNumber = 0;
        }

        private void VideoFileErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                if (!inited && (e.Data.Contains("frame=") || e.Data.Contains("Stream") || e.Data.Contains("Duration") || e.Data.Contains("Input")))
                {
                    InitializeFrameProcessing();
                }

                if (e.Data.Contains("error") || e.Data.Contains("Error") || e.Data.Contains("warning") || e.Data.Contains("Warning"))
                {
                    string prefix = isSeekOperation ? "SEEK: FFMPEG" : "FFMPEG Video File";
                    Tools.Logger.VideoLog.LogDebugCall(this, $"{prefix}: {e.Data}");
                }
            }
        }

        protected override void ProcessImage()
        {
            if (!inited)
                return;

            // Call base implementation for frame processing first
            base.ProcessImage();
            
            // For video file playback, let FFmpeg handle timing with -re flag
            // Only do minimal timing updates to support progress bar
            if (startTime == DateTime.MinValue)
            {
                // Initialize start time on first frame
                startTime = DateTime.Now;
                if (isSeekOperation)
                {
                    // For seek operations, adjust start time to account for seek position
                    startTime = DateTime.Now - mediaTime;
                    // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Video file playback started at: {startTime} (seek position: {mediaTime.TotalSeconds:F3}s)");
                }
                else
                {
                    // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"Video file playback started at: {startTime}");
                }
            }

            // Update timing - let FFmpeg handle most of the timing with -re flag
            if (startTime != DateTime.MinValue)
            {
                if (isSeekOperation)
                {
                    // For seek operations, allow time to stabilize first
                    seekStabilizationFrames++;
                    if (seekStabilizationFrames > 5) // Allow more frames for stabilization
                    {
                        isSeekOperation = false;
                        // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Timing stabilized, continuing from: {mediaTime.TotalSeconds:F3}s");
                    }
                    // During stabilization, keep mediaTime FROZEN at the seek position
                    // This prevents the progress bar from moving until frames are actually being processed
                    // mediaTime stays at the seek position until timing stabilizes
                }
                else
                {
                    // Normal playback - update mediaTime based on elapsed time since start
                    TimeSpan elapsedTime = DateTime.Now - startTime;
                    
                    // Only update mediaTime if it would advance (prevent going backwards)
                    if (elapsedTime > mediaTime)
                    {
                        mediaTime = elapsedTime;
                    }
                }
                
                // Check if we've reached the end of the video
                if (length > TimeSpan.Zero && mediaTime >= length)
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, $"VIDEO END DETECTED: mediaTime={mediaTime.TotalSeconds:F1}s >= length={length.TotalSeconds:F1}s, setting isAtEnd=true");
                    isAtEnd = true;
                    if (repeat)
                    {
                        // Restart from beginning
                        SetPosition(TimeSpan.Zero);
                    }
                }
            }
            else
            {
                // If startTime hasn't been set yet, don't update mediaTime at all
                // This prevents the progress bar from moving before the FFmpeg process starts
                if (isSeekOperation)
                {
                    // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Waiting for FFmpeg process to start before updating timing (mediaTime frozen at: {mediaTime.TotalSeconds:F3}s)");
                }
            }
        }

        private bool GetVideoFileInfo()
        {
            try
            {
                string filePath = VideoConfig.FilePath;
                if (!Path.IsPathRooted(filePath))
                {
                    filePath = Path.GetFullPath(filePath);
                }

                // Get just the filename for logging
                string fileName = Path.GetFileName(filePath);

                // Check if file exists
                if (!File.Exists(filePath))
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, $"Video file does not exist: {fileName}");
                    return false;
                }

                // Use FFprobe to get video file information
                string ffprobePath;
                if (ffmpegMediaFramework.ExecName.Contains(Path.DirectorySeparatorChar.ToString()))
                {
                    // If ffmpeg path contains directory separator, construct ffprobe path in same directory
                    string ffmpegDir = Path.GetDirectoryName(ffmpegMediaFramework.ExecName);
                    string ffmpegFile = Path.GetFileName(ffmpegMediaFramework.ExecName);
                    string ffprobeFile = ffmpegFile.Replace("ffmpeg", "ffprobe");
                    ffprobePath = Path.Combine(ffmpegDir, ffprobeFile);
                }
                else
                {
                    // Simple replacement for cases where ffmpeg is just "ffmpeg" or "ffmpeg.exe"
                    ffprobePath = ffmpegMediaFramework.ExecName.Replace("ffmpeg", "ffprobe");
                }
                
                if (!File.Exists(ffprobePath))
                {
                    string altPath1 = Path.Combine(Path.GetDirectoryName(ffmpegMediaFramework.ExecName), "ffprobe");
                    string altPath2 = Path.Combine(Path.GetDirectoryName(ffmpegMediaFramework.ExecName), "ffprobe.exe");

                    if (File.Exists(altPath1))
                        ffprobePath = altPath1;
                    else if (File.Exists(altPath2))
                        ffprobePath = altPath2;
                    else
                        return length > TimeSpan.Zero;
                }

                string ffprobeArgs = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"";

                ProcessStartInfo processStartInfo = new ProcessStartInfo()
                {
                    Arguments = ffprobeArgs,
                    FileName = ffprobePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (Process process = new Process())
                {
                    process.StartInfo = processStartInfo;
                    process.Start();

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        ParseVideoInfo(output);
                        return true;
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, $"FFprobe failed for {fileName} (exit code: {process.ExitCode}): {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }

            if (length == TimeSpan.Zero)
                length = TimeSpan.FromMinutes(5);
            
            if (VideoConfig.VideoMode == null)
            {
                VideoConfig.VideoMode = new Mode
                {
                    Width = 1280,
                    Height = 720,
                    FrameRate = 30.0f,
                    FrameWork = FrameWork.FFmpeg
                };
            }
            
            frameRate = VideoConfig.VideoMode.FrameRate;
            
            return false;
        }

        private void ParseVideoInfo(string jsonOutput)
        {
            try
            {
                TimeSpan parsedLength = TimeSpan.Zero;

                var durationMatch = Regex.Match(jsonOutput, @"""duration"":\s*""([^""]+)""");
                if (!durationMatch.Success)
                    durationMatch = Regex.Match(jsonOutput, @"""duration"":\s*""([0-9.]+)""");
                if (!durationMatch.Success)
                    durationMatch = Regex.Match(jsonOutput, @"""duration"":\s*([0-9.]+)");

                if (durationMatch.Success && double.TryParse(durationMatch.Groups[1].Value, out double durationSeconds))
                    parsedLength = TimeSpan.FromSeconds(durationSeconds);

                if (parsedLength > TimeSpan.Zero && (length == TimeSpan.Zero || parsedLength < length))
                    length = parsedLength;

                var frameRateMatch = Regex.Match(jsonOutput, @"""r_frame_rate"":\s*""([^""]+)""");
                if (frameRateMatch.Success)
                {
                    var parts = frameRateMatch.Groups[1].Value.Split('/');
                    if (parts.Length == 2 &&
                        double.TryParse(parts[0], out double num) &&
                        double.TryParse(parts[1], out double den) &&
                        den > 0)
                    {
                        frameRate = num / den;
                    }
                }

                var widthMatch = Regex.Match(jsonOutput, @"""width"":\s*(\d+)");
                var heightMatch = Regex.Match(jsonOutput, @"""height"":\s*(\d+)");
                if (widthMatch.Success && heightMatch.Success)
                {
                    int width = int.Parse(widthMatch.Groups[1].Value);
                    int height = int.Parse(heightMatch.Groups[1].Value);

                    if (VideoConfig.VideoMode == null)
                        VideoConfig.VideoMode = new Mode { Width = width, Height = height, FrameRate = (float)frameRate, FrameWork = FrameWork.FFmpeg };
                    else
                    {
                        VideoConfig.VideoMode.Width = width;
                        VideoConfig.VideoMode.Height = height;
                        VideoConfig.VideoMode.FrameRate = (float)frameRate;
                    }
                }

                startTime = DateTime.Now;
                Tools.Logger.VideoLog.LogDebugCall(this, $"Video info: {VideoConfig.VideoMode?.Width}x{VideoConfig.VideoMode?.Height}@{frameRate}fps, duration: {length}");
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
        }

        public void Play()
        {
            if (State != States.Running)
                RestartWithSeek(mediaTime);
        }

        public override bool Pause()
        {
            if (State == States.Running)
            {
                try
                {
                    if (process != null && !process.HasExited)
                    {
                        try { process.Kill(); process.WaitForExit(10000); }
                        catch (Exception killEx) { Tools.Logger.VideoLog.LogException(this, killEx); }
                    }
                    return base.Pause();
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    return false;
                }
            }
            return false;
        }
        
        public override bool Stop()
        {
            Tools.Logger.VideoLog.LogDebugCall(this, $"Stop() called, current state: {State}");
            
            if (State != States.Stopped)
            {
                try
                {
                    Tools.Logger.VideoLog.LogDebugCall(this, "Stopping video playback by killing FFmpeg process");
                    
                    // Kill the FFmpeg process immediately and synchronously BEFORE calling base.Stop()
                    // This prevents the base class's async cleanup from racing with our sync cleanup
                    if (process != null && !process.HasExited)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, $"Killing FFmpeg process {process.Id} for stop");
                        try
                        {
                            process.Kill();
                            // Give it a short time to exit gracefully, but don't wait too long
                            if (process.WaitForExit(10000))
                            {
                                Tools.Logger.VideoLog.LogDebugCall(this, "FFmpeg process stopped successfully");
                            }
                            else
                            {
                                Tools.Logger.VideoLog.LogDebugCall(this, "FFmpeg process didn't exit quickly, but continuing with stop");
                            }
                        }
                        catch (Exception killEx)
                        {
                            Tools.Logger.VideoLog.LogException(this, killEx);
                            Tools.Logger.VideoLog.LogDebugCall(this, "Error killing FFmpeg process during stop, but continuing");
                        }
                    }
                    else if (process != null)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "FFmpeg process already exited");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "No FFmpeg process to kill");
                    }
                    
                    // Now call base stop to set state and handle other cleanup
                    // Since we already killed the process, the base class's async cleanup won't find it
                    bool result = base.Stop();
                    Tools.Logger.VideoLog.LogDebugCall(this, $"Stop completed, state now: {State}");
                    return result;
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    return false;
                }
            }
            else
            {
                Tools.Logger.VideoLog.LogDebugCall(this, "Already stopped");
                return true;
            }
        }
        
        public override bool Unpause()
        {
            if (State == States.Paused)
            {
                try
                {
                    RestartWithSeek(mediaTime);
                    return base.Unpause();
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    return false;
                }
            }
            return false;
        }

        public void SetPosition(TimeSpan timeSpan)
        {
            if (timeSpan < TimeSpan.Zero) timeSpan = TimeSpan.Zero;
            if (length > TimeSpan.Zero && timeSpan > length) timeSpan = length;

            startTime = DateTime.Now - timeSpan;
            mediaTime = timeSpan;
            isAtEnd = false;
            SeekToPosition(timeSpan);
        }

        private void SeekToPosition(TimeSpan seekTime)
        {
            try { RestartWithSeek(seekTime); }
            catch (Exception ex) { Tools.Logger.VideoLog.LogException(this, ex); }
        }

        private void RestartWithSeek(TimeSpan seekTime)
        {
            try
            {
                if (process != null)
                {
                    run = false;
                    if (!process.HasExited)
                    {
                        try { process.Kill(); process.WaitForExit(10000); }
                        catch (Exception killEx) { Tools.Logger.VideoLog.LogException(this, killEx); }
                    }
                    try { process.Dispose(); } catch { }
                    process = null;
                }

                if (thread != null && thread.IsAlive)
                {
                    run = false;
                    thread.Join(10000);
                    thread = null;
                }

                System.Threading.Thread.Sleep(100);
                StartSeekProcess(seekTime);
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
        }
        
        private void StartSeekProcess(TimeSpan seekTime)
        {
            try
            {
                string fileName = Path.GetFileName(VideoConfig.FilePath);
                
                // Reset state for clean restart - same as initial Start()
                inited = false;
                Connected = false;
                
                // Reset timing state for seek operation
                startTime = DateTime.MinValue;  // Will be set to (Now - seekTime) when first frame arrives
                mediaTime = seekTime;  // Set to the seek position
                isSeekOperation = true;  // Mark as seek operation for timing stabilization
                seekStabilizationFrames = 0;  // Reset seek stabilization counter
                isAtEnd = false;  // Reset end-of-video flag
                FrameProcessNumber = 0;  // Reset frame counter for clean restart
                
                // Reinitialize buffers to prevent flickering (same as Start() method)
                if (VideoConfig.VideoMode != null)
                {
                    width = VideoConfig.VideoMode.Width;
                    height = VideoConfig.VideoMode.Height;
                    buffer = new byte[width * height * 4];  // RGBA = 4 bytes per pixel
                    rawTextures = new XBuffer<RawTexture>(5, width, height);
                    // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Buffers reinitialized: {width}x{height}");
                }
                
                // Update the FFmpeg command to include seek
                string filePath = VideoConfig.FilePath;
                // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Original file path: {filePath}");
                
                if (!Path.IsPathRooted(filePath))
                    filePath = Path.GetFullPath(filePath);
                
                // Verify file exists before attempting to start FFmpeg
                if (!File.Exists(filePath))
                {
                    // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: ERROR - Video file does not exist: {filePath}");
                    return; // Don't try to start FFmpeg with a non-existent file
                }
                
                // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: File verified to exist, size: {new FileInfo(filePath).Length} bytes");

                // Build FFmpeg command with seek parameter
                // Put -ss BEFORE -i for efficient input seeking (avoids decoding unnecessary frames)
                // Input options (-ss, -re) must come before -i, output options come after
                
                // Build video filter chain based on VideoConfig settings (same as GetProcessStartInfo)
                string videoFilter = BuildVideoFilter();
                string reFlag = "-re"; // Default: use -re for proper timing

                string ffmpegArgs = $"-ss {seekTime.TotalSeconds:F3} " +  // Seek to specific time (BEFORE input for fast seek)
                                   $"{(string.IsNullOrEmpty(reFlag) ? "" : reFlag + " ")}" +  // Read input at native frame rate (only for normal speed)
                                   $"-i \"{filePath}\" " +  // Input file
                                   $"-fflags +genpts " +  // Generate presentation timestamps (output option)
                                   $"-avoid_negative_ts make_zero " +  // Handle negative timestamps (output option)
                                   $"-threads 4 " +  // Multi-threaded processing (output option)
                                   $"-an " +  // No audio (output option)
                                   $"{(string.IsNullOrEmpty(videoFilter) ? "" : $"-vf \"{videoFilter}\" ")}" +  // Apply video filters if any
                                   $"-pix_fmt rgba " +  // RGBA pixel format (output option)
                                   $"-f rawvideo pipe:1";  // Raw video output to stdout (output option)

                // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: FFMPEG Video File Seek ({fileName}): {ffmpegArgs}");
                
                // Mark this as a seek operation for logging
                isSeekOperation = true;
                
                // Use the base class infrastructure for proper process management
                // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: About to start process using base infrastructure...");
                
                // Temporarily store the seek args in a field that GetProcessStartInfo can use
                currentSeekArgs = ffmpegArgs;
                
                // Use base.Start() which handles all the process setup correctly
                // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Calling base.Start() with currentSeekArgs set");
                bool result = base.Start();
                
                if (result)
                {
                    // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: FFmpeg seek process started successfully via base.Start()");
                    // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Process ID: {process?.Id}, HasExited: {process?.HasExited}");
                    
                    // For seek operations, we need to ensure initialization happens
                    if (process != null)
                    {
                        // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Adding VideoFileErrorDataReceived event handler");
                        process.ErrorDataReceived += VideoFileErrorDataReceived;
                        try
                        {
                            process.EnableRaisingEvents = true;
                            process.Exited += (s, e) => { mediaTime = length; isAtEnd = true; };
                        }
                        catch { }
                        
                        // For seek operations, force initialization after a short delay since FFmpeg 
                        // might not produce the exact patterns the base class expects
                        Task.Delay(500).ContinueWith(_ => 
                        {
                            if (!inited && run)
                            {
                                // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Force initializing frame processing for seek operation");
                                InitializeFrameProcessing();
                            }
                        });
                        
                        // Check if the process is actually running
                        if (process.HasExited)
                        {
                            // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: WARNING - Process exited immediately with exit code: {process.ExitCode}");
                        }
                    }
                    else
                    {
                        // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: WARNING - Process is null after successful base.Start()");
                    }
                }
                else
                {
                    // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Failed to start FFmpeg seek process via base.Start()");
                    // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: State after failed start: {State}, Connected: {Connected}");
                }
                
                // Clear the seek args
                currentSeekArgs = null;
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
        }

        public void SetPosition(DateTime dateTime)
        {
            // The dateTime comes from SeekNode and represents a position in the overall timeline
            // We need to convert this to a TimeSpan offset from THIS video file's beginning
            
            // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: SetPosition(DateTime): dateTime={dateTime:HH:mm:ss.fff}");
            // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Video file StartTime={StartTime:HH:mm:ss.fff}, Length={Length.TotalSeconds:F3}s");
            
            TimeSpan seekTimeSpan;
            
            // Calculate offset from this video file's start time
            if (StartTime != DateTime.MinValue)
            {
                // dateTime is absolute timeline position, StartTime is this video file's start
                seekTimeSpan = dateTime - StartTime;
                // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Calculated offset: {seekTimeSpan.TotalSeconds:F3}s (dateTime - StartTime)");
            }
            else
            {
                // Fallback: if StartTime not available, assume dateTime is already relative
                // This shouldn't normally happen, but provides a safety net
                seekTimeSpan = TimeSpan.Zero;
                // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: StartTime not available, defaulting to 0");
            }
            
            // Ensure we don't seek to negative time
            if (seekTimeSpan < TimeSpan.Zero)
            {
                // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Clamping negative offset {seekTimeSpan.TotalSeconds:F3}s to 0");
                seekTimeSpan = TimeSpan.Zero;
            }
            
            // Clamp to video length if known
            if (Length > TimeSpan.Zero && seekTimeSpan > Length)
            {
                // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Clamping offset {seekTimeSpan.TotalSeconds:F3}s to video length {Length.TotalSeconds:F3}s");
                seekTimeSpan = Length;
            }
            
            // Tools.Logger.VideoLog.LogCallDebugOnly(this, $"SEEK: Final seek offset: {seekTimeSpan.TotalSeconds:F3}s");
            SetPosition(seekTimeSpan);
        }

        public void NextFrame()
        {
            if (VideoConfig?.FrameTimes != null && VideoConfig.FrameTimes.Length > 0)
            {
                DateTime currentAbsoluteTime = StartTime + mediaTime;
                int currentIndex = -1;
                for (int i = 0; i < VideoConfig.FrameTimes.Length; i++)
                {
                    if (VideoConfig.FrameTimes[i].Time >= currentAbsoluteTime) { currentIndex = i; break; }
                }
                if (currentIndex >= 0 && currentIndex < VideoConfig.FrameTimes.Length - 1)
                {
                    SetPosition(VideoConfig.FrameTimes[currentIndex + 1].Time - StartTime);
                    return;
                }
            }
            SetPosition(mediaTime + TimeSpan.FromSeconds(1.0 / frameRate));
        }

        public void PrevFrame()
        {
            if (VideoConfig?.FrameTimes != null && VideoConfig.FrameTimes.Length > 0)
            {
                DateTime currentAbsoluteTime = StartTime + mediaTime;
                int currentIndex = -1;
                for (int i = 0; i < VideoConfig.FrameTimes.Length; i++)
                {
                    if (VideoConfig.FrameTimes[i].Time >= currentAbsoluteTime) { currentIndex = i; break; }
                }
                if (currentIndex > 0)
                {
                    SetPosition(VideoConfig.FrameTimes[currentIndex - 1].Time - StartTime);
                    return;
                }
            }
            SetPosition(mediaTime - TimeSpan.FromSeconds(1.0 / frameRate));
        }

        public override IEnumerable<Mode> GetModes()
        {
            // For video files, return a single mode based on the actual video properties
            var mode = new Mode
            {
                Width = VideoConfig.VideoMode?.Width ?? 640,
                Height = VideoConfig.VideoMode?.Height ?? 480,
                FrameRate = VideoConfig.VideoMode?.FrameRate ?? 30.0f,
                Format = "rgba",
                Index = 0,
                FrameWork = FrameWork.FFmpeg
            };
            
            return new List<Mode> { mode };
        }

        public void Mute(bool mute)
        {
            // Video files don't have audio in our implementation
        }

        public void Unmute()
        {
            // Video files don't have audio in our implementation
        }

        public void FixOrientation()
        {
            string filePath = VideoConfig.FilePath;
            if (!Path.IsPathRooted(filePath))
            {
                filePath = Path.GetFullPath(filePath);
            }

            string ffmpegArgs = $"-re " +  // Read input at native frame rate for proper timing
                               $"-i \"{filePath}\" " +
                               $"-fflags +genpts " +
                               $"-avoid_negative_ts make_zero " +
                               $"-threads 4 " +
                               $"-an " +
                               // Let FFmpeg preserve original video timing instead of forcing frame rate
                               $"-vf \"vflip,transpose=2,transpose=2\" " +  // Flip vertically and rotate 180 degrees
                               $"-pix_fmt rgba " +
                               $"-f rawvideo pipe:1";

            // Restart the process with orientation fix
            Stop();
            Thread.Sleep(500);
            
            var processStartInfo = ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);

            System.Diagnostics.Debug.Assert(process == null);
            process = new Process();
            process.StartInfo = processStartInfo;
            Start();
        }
        
        /// <summary>
        /// Load frame times from the .recordinfo.xml file associated with this video file
        /// </summary>
        private void LoadFrameTimesFromRecordInfo()
        {
            try
            {
                if (string.IsNullOrEmpty(VideoConfig.FilePath) || !File.Exists(VideoConfig.FilePath))
                {
                    return;
                }
                
                // Calculate the .recordinfo.xml file path
                string videoFilePath = VideoConfig.FilePath;
                string basePath = videoFilePath;
                if (basePath.EndsWith(".mp4"))
                {
                    basePath = basePath.Replace(".mp4", "");
                }
                else if (basePath.EndsWith(".wmv"))
                {
                    basePath = basePath.Replace(".wmv", "");
                }
                else if (basePath.EndsWith(".ts"))
                {
                    basePath = basePath.Replace(".ts", "");
                }
                else if (basePath.EndsWith(".mkv"))
                {
                    basePath = basePath.Replace(".mkv", "");
                }
                
                string recordInfoPath = basePath + ".recordinfo.xml";
                
                // Convert to absolute path for proper file checking and IOTools usage
                string absoluteRecordInfoPath = Path.GetFullPath(recordInfoPath);
                
                if (!File.Exists(absoluteRecordInfoPath))
                {
                    return;
                }

                if (VideoConfig != null && VideoConfig.FrameTimes != null && VideoConfig.FrameTimes.Length > 0)
                {
                    // Set the loaded frame times - need to access via reflection or create a protected setter
                    SetFrameTimes(VideoConfig.FrameTimes);
                    
                    // Update the start time based on the first frame
                    var firstFrame = VideoConfig.FrameTimes.OrderBy(f => f.Time).First();
                    var lastFrame = VideoConfig.FrameTimes.OrderBy(f => f.Time).Last();
                    startTime = firstFrame.Time;
                    originalVideoStartTime = firstFrame.Time; // Preserve the original video start time for seeking
                    
                    // Get ffprobe duration first to use as fallback
                    TimeSpan ffprobeDuration = DetermineActualDuration(videoFilePath);
                    
                    // Use unified duration calculation logic with ffprobe as fallback
                    var xmlDuration = UnifiedFrameTimingManager.CalculateVideoDuration(VideoConfig.FrameTimes, ffprobeDuration);
                    
                    // Duration selection: prefer the shorter of XML-derived duration and container duration
                    // This avoids progress bar overhang when one source slightly overestimates length
                    if (ffprobeDuration > TimeSpan.Zero && xmlDuration > TimeSpan.Zero)
                    {
                        double xmlS = xmlDuration.TotalSeconds;
                        double ffS = ffprobeDuration.TotalSeconds;
                        double diff = Math.Abs(xmlS - ffS);
                        Tools.Logger.VideoLog.LogDebugCall(this, $"Duration comparison: XML={xmlS:F3}s, ffprobe={ffS:F3}s, |diff|={diff:F3}s");
                        var chosen = TimeSpan.FromSeconds(Math.Min(xmlS, ffS));
                        length = chosen;
                        Tools.Logger.VideoLog.LogDebugCall(this, $"FINAL LENGTH SET (min of XML/ffprobe): {length.TotalSeconds:F3}s");
                    }
                    else if (xmlDuration > TimeSpan.Zero)
                    {
                        length = xmlDuration;
                        Tools.Logger.VideoLog.LogDebugCall(this, $"FINAL LENGTH SET: {length.TotalSeconds:F3}s (XML only)");
                    }
                    else if (ffprobeDuration > TimeSpan.Zero)
                    {
                        length = ffprobeDuration;
                        Tools.Logger.VideoLog.LogDebugCall(this, $"FINAL LENGTH SET: {length.TotalSeconds:F3}s (ffprobe only)");
                    }
                    
                    // Validate frame timing consistency to detect platform-specific issues
                    bool isConsistent = UnifiedFrameTimingManager.ValidateFrameTimingConsistency(
                        VideoConfig.FrameTimes, VideoConfig.VideoMode?.FrameRate ?? 30.0f);
                    
                    if (!isConsistent)
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "WARNING: Frame timing data appears inconsistent - this may cause playback issues");
                    }
                }
                else
                {
                    // No XML timing data - use actual MP4 duration as fallback
                    TimeSpan actualMp4Duration = DetermineActualDuration(videoFilePath);
                    if (actualMp4Duration > TimeSpan.Zero)
                    {
                        length = actualMp4Duration;
                        Tools.Logger.VideoLog.LogDebugCall(this, $"LENGTH DEBUG: No XML timing - using MP4 duration={actualMp4Duration.TotalSeconds:F1}s");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "LENGTH DEBUG: Could not determine video duration - using default");
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
        }

        /// <summary>
        /// Determine the actual duration of the MP4 file using ffprobe
        /// </summary>
        private TimeSpan DetermineActualDuration(string videoFilePath)
        {
            try
            {
                Tools.Logger.VideoLog.LogDebugCall(this, $"Determining actual MP4 duration for: {videoFilePath}");
                
                // Check if ffprobe is available first
                string ffprobePath;
                if (ffmpegMediaFramework.ExecName.Contains(Path.DirectorySeparatorChar.ToString()))
                {
                    // If ffmpeg path contains directory separator, construct ffprobe path in same directory
                    string ffmpegDir = Path.GetDirectoryName(ffmpegMediaFramework.ExecName);
                    string ffmpegFile = Path.GetFileName(ffmpegMediaFramework.ExecName);
                    string ffprobeFile = ffmpegFile.Replace("ffmpeg", "ffprobe");
                    ffprobePath = Path.Combine(ffmpegDir, ffprobeFile);
                }
                else
                {
                    // Simple replacement for cases where ffmpeg is just "ffmpeg" or "ffmpeg.exe"
                    ffprobePath = ffmpegMediaFramework.ExecName.Replace("ffmpeg", "ffprobe");
                }
                
                // Check if ffprobe exists, try alternatives if not
                if (!File.Exists(ffprobePath))
                {
                    string altPath1 = Path.Combine(Path.GetDirectoryName(ffmpegMediaFramework.ExecName), "ffprobe");
                    string altPath2 = Path.Combine(Path.GetDirectoryName(ffmpegMediaFramework.ExecName), "ffprobe.exe");
                    
                    if (File.Exists(altPath1))
                    {
                        ffprobePath = altPath1;
                    }
                    else if (File.Exists(altPath2))
                    {
                        ffprobePath = altPath2;
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, "FFprobe not found - cannot determine MP4 duration");
                        return TimeSpan.Zero; // Return zero if ffprobe not available
                    }
                }
                
                // Use ffprobe to get video information
                string ffprobeArgs = $"-v quiet -print_format json -show_format -show_streams \"{videoFilePath}\"";
                var processStartInfo = new ProcessStartInfo()
                {
                    Arguments = ffprobeArgs,
                    FileName = ffprobePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (Process process = new Process())
                {
                    process.StartInfo = processStartInfo;
                    process.Start();
                    
                    string jsonOutput = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(jsonOutput))
                    {
                        // Parse duration from JSON output - use existing parsing logic
                        var durationMatch = Regex.Match(jsonOutput, @"""duration"":\s*""([^""]+)""");
                        if (durationMatch.Success)
                        {
                            string durationStr = durationMatch.Groups[1].Value;
                            if (double.TryParse(durationStr, out double durationSeconds))
                            {
                                TimeSpan duration = TimeSpan.FromSeconds(durationSeconds);
                                Tools.Logger.VideoLog.LogDebugCall(this, $"MP4 duration determined: {duration.TotalSeconds:F1}s");
                                return duration;
                            }
                        }
                        
                        // Try alternative pattern without quotes
                        var altDurationMatch = Regex.Match(jsonOutput, @"""duration"":\s*([0-9.]+)");
                        if (altDurationMatch.Success)
                        {
                            string durationStr = altDurationMatch.Groups[1].Value;
                            if (double.TryParse(durationStr, out double durationSeconds))
                            {
                                TimeSpan duration = TimeSpan.FromSeconds(durationSeconds);
                                Tools.Logger.VideoLog.LogDebugCall(this, $"MP4 duration determined (alt pattern): {duration.TotalSeconds:F1}s");
                                return duration;
                            }
                        }
                    }
                }
                
                Tools.Logger.VideoLog.LogDebugCall(this, "Could not determine MP4 duration from ffprobe");
                return TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogException(this, "Error determining MP4 duration", ex);
                return TimeSpan.Zero;
            }
        }
        
        /// <summary>
        /// Set the frame times for this video file source
        /// </summary>
        private void SetFrameTimes(FrameTime[] newFrameTimes)
        {
            try
            {
                // Use reflection to access the private frameTimes field from the base class
                var frameTimesField = typeof(FfmpegFrameSource).GetField("frameTimes", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (frameTimesField != null)
                {
                    var frameTimesList = (List<FrameTime>)frameTimesField.GetValue(this);
                    if (frameTimesList != null)
                    {
                        frameTimesList.Clear();
                        frameTimesList.AddRange(newFrameTimes);
                    }
                    else
                    {
                    }
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
        }
    }
} 