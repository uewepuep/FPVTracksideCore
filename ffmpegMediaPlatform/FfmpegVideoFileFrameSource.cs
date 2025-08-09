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
                Tools.Logger.VideoLog.LogCall(this, $"Length property accessed: {length}");
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
                    Tools.Logger.VideoLog.LogCall(this, $"PlaybackSpeed changed from {playbackSpeed} to {value}");
                    playbackSpeed = value;
                    
                    // If currently playing, restart with new speed settings
                    if (State == States.Running)
                    {
                        Tools.Logger.VideoLog.LogCall(this, "Restarting video with new playback speed");
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

        public TimeSpan MediaTime
        {
            get => mediaTime;
            set
            {
                mediaTime = value;
                if (mediaTime >= length)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"VIDEO END DETECTED (setter): mediaTime={mediaTime.TotalSeconds:F1}s >= length={length.TotalSeconds:F1}s, setting isAtEnd=true");
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

        protected override ProcessStartInfo GetProcessStartInfo()
        {
            // If we have seek args, use them instead of generating normal args
            if (!string.IsNullOrEmpty(currentSeekArgs))
            {
                Tools.Logger.VideoLog.LogCall(this, $"Using seek FFmpeg args: {currentSeekArgs}");
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
                Tools.Logger.VideoLog.LogCall(this, $"Video file does not exist: {filePath}");
                throw new FileNotFoundException($"Video file not found: {filePath}");
            }
            bool isWMV = filePath.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase);
            
            // Get just the filename for logging
            string fileName = Path.GetFileName(filePath);
            
            Tools.Logger.VideoLog.LogCall(this, $"Video file playback: {fileName}");
            Tools.Logger.VideoLog.LogCall(this, $"File exists: {File.Exists(filePath)}");
            Tools.Logger.VideoLog.LogCall(this, $"File size: {new FileInfo(filePath).Length} bytes");
            Tools.Logger.VideoLog.LogCall(this, $"Is WMV: {isWMV}");

            // Build FFmpeg command for video file playback with interactive seeking support
            // Use proper settings for smooth video file playback
            
            // Build video filter chain based on playback speed
            string videoFilter = "vflip"; // Base filter to flip video vertically
            string reFlag = "-re"; // Default: use -re for proper timing
            
            // Add playback speed control if slow motion is enabled
            if (playbackSpeed == PlaybackSpeed.Slow)
            {
                // Use setpts filter to slow down video to 25% speed
                // setpts=4*PTS makes video 4x slower (25% speed)
                videoFilter += ",setpts=4*PTS";
                reFlag = ""; // Remove -re flag for slow motion to allow setpts to work properly
                Tools.Logger.VideoLog.LogCall(this, "Slow motion enabled - video will play at 25% speed using setpts filter (no -re)");
            }
            
            string ffmpegArgs = $"{(string.IsNullOrEmpty(reFlag) ? "" : reFlag + " ")}" +  // Read input at native frame rate (only for normal speed)
                               $"-i \"{filePath}\" " +
                               $"-fflags +genpts " +  // Generate presentation timestamps
                               $"-avoid_negative_ts make_zero " +  // Handle negative timestamps
                               $"-threads 1 " +
                               $"-an " +  // No audio
                               $"-vf \"{videoFilter}\" " +  // Apply video filters (vflip and optionally setpts for slow motion)
                               $"-pix_fmt rgba " +
                               $"-f rawvideo pipe:1";

            // Add special handling for WMV files if needed
            if (isWMV)
            {
                Tools.Logger.VideoLog.LogCall(this, "WMV file detected - using standard FFmpeg WMV support");
                // FFmpeg handles WMV files well across platforms, no special parameters needed
            }

            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Video File Playback ({fileName}): {ffmpegArgs}");
            return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
        }

        public override bool Start()
        {
            Tools.Logger.VideoLog.LogCall(this, $"FfmpegVideoFileFrameSource.Start() called, current state: {State}");
            Tools.Logger.VideoLog.LogCall(this, "PLAYBACK ENGINE: ffmpeg BINARY (external process)");
            
            // This is normal playback, not a seek operation
            isSeekOperation = false;
            
            // Initialize playback state
            InitializePlaybackState();
            
            // Try to get video file information first
            if (GetVideoFileInfo())
            {
                // If we successfully got video info, initialize with the correct dimensions
                Tools.Logger.VideoLog.LogCall(this, $"Video file info obtained: {VideoConfig.VideoMode?.Width}x{VideoConfig.VideoMode?.Height} @ {VideoConfig.VideoMode?.FrameRate}fps");
                
                // Initialize frame processing with the correct dimensions from the video file
                if (VideoConfig.VideoMode != null)
                {
                    // Override the dimensions with the actual video file dimensions
                    width = VideoConfig.VideoMode.Width;
                    height = VideoConfig.VideoMode.Height;
                    
                    // Recalculate buffer size with correct dimensions
                    buffer = new byte[width * height * 4];  // RGBA = 4 bytes per pixel
                    rawTextures = new XBuffer<RawTexture>(5, width, height);
                    
                    Tools.Logger.VideoLog.LogCall(this, $"Video file buffer initialized: {width}x{height}, buffer size: {buffer.Length} bytes");
                    inited = true;
                }
            }
            else
            {
                Tools.Logger.VideoLog.LogCall(this, "Could not get video file info, will use default dimensions");
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
                            Tools.Logger.VideoLog.LogCall(this, $"VIDEO FILE: Process exited - clamping mediaTime to length {length.TotalSeconds:F3}s");
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
                            Tools.Logger.VideoLog.LogCall(this, "Video file playback timeout - forcing initialization");
                            InitializeFrameProcessing();
                        }
                    });
                }
            }
            
            return result;
        }

        private void InitializePlaybackState()
        {
            // Reset playback state
            startTime = DateTime.MinValue;
            mediaTime = TimeSpan.Zero;
            isAtEnd = false;
            FrameProcessNumber = 0; // Reset frame counter for proper timeline calculation
            
            Tools.Logger.VideoLog.LogCall(this, "Video file playback state initialized");
        }

        private void VideoFileErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                string prefix = isSeekOperation ? "SEEK: FFMPEG" : "FFMPEG Video File";
                Tools.Logger.VideoLog.LogCall(this, $"{prefix}: {e.Data}");
                
                // Initialize immediately when we see any FFmpeg output for video files
                if (!inited && (e.Data.Contains("frame=") || e.Data.Contains("Stream") || e.Data.Contains("Duration") || e.Data.Contains("Input")))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"{prefix}: Video file playback detected - initializing frame processing");
                    Tools.Logger.VideoLog.LogCall(this, $"{prefix}: FFmpeg output that triggered init: {e.Data}");
                    
                    InitializeFrameProcessing();
                    
                    Tools.Logger.VideoLog.LogCall(this, $"{prefix}: Frame processing initialized - inited: {inited}, width: {width}, height: {height}");
                }
                
                // Special handling for WMV files
                bool isWMV = VideoConfig.FilePath?.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase) ?? false;
                if (isWMV)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"{prefix}: WMV file processing: {e.Data}");
                    
                    // Check for WMV-specific issues
                    if (e.Data.Contains("Invalid data found") || e.Data.Contains("Error while decoding"))
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"{prefix}: WMV decoding issue detected: {e.Data}");
                    }
                    
                    // Check for successful WMV processing
                    if (e.Data.Contains("Stream") && e.Data.Contains("Video"))
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"{prefix}: WMV video stream detected: {e.Data}");
                    }
                }
                
                // Log any errors or warnings
                if (e.Data.Contains("error") || e.Data.Contains("Error") || e.Data.Contains("warning") || e.Data.Contains("Warning"))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"{prefix}: Error/Warning: {e.Data}");
                }
            }
        }

        protected override void ProcessImage()
        {
            if (!inited)
            {
                // Log every 60 frames to avoid spam but still track the issue
                if (FrameProcessNumber % 60 == 0)
                {
                    string prefix = isSeekOperation ? "SEEK" : "NORMAL";  
                    Tools.Logger.VideoLog.LogCall(this, $"{prefix}: Video file frame processing: Not initialized yet (frame {FrameProcessNumber})");
                }
                return;
            }

            // Log first few frames and then every 120 frames to track processing
            if (FrameProcessNumber < 10 || FrameProcessNumber % 120 == 0)
            {
                string prefix = isSeekOperation ? "SEEK" : "NORMAL";
                Tools.Logger.VideoLog.LogCall(this, $"{prefix}: Processing video frame {FrameProcessNumber}, mediaTime: {mediaTime.TotalSeconds:F3}s");
            }

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
                    // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Video file playback started at: {startTime} (seek position: {mediaTime.TotalSeconds:F3}s)");
                }
                else
                {
                    // Tools.Logger.VideoLog.LogCall(this, $"Video file playback started at: {startTime}");
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
                        // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Timing stabilized, continuing from: {mediaTime.TotalSeconds:F3}s");
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
                    Tools.Logger.VideoLog.LogCall(this, $"VIDEO END DETECTED: mediaTime={mediaTime.TotalSeconds:F1}s >= length={length.TotalSeconds:F1}s, setting isAtEnd=true");
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
                    // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Waiting for FFmpeg process to start before updating timing (mediaTime frozen at: {mediaTime.TotalSeconds:F3}s)");
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
                    Tools.Logger.VideoLog.LogCall(this, $"Video file does not exist: {fileName}");
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
                
                Tools.Logger.VideoLog.LogCall(this, $"Looking for FFprobe at: {ffprobePath}");
                Tools.Logger.VideoLog.LogCall(this, $"FFmpeg framework exec name: {ffmpegMediaFramework.ExecName}");
                
                // Check if ffprobe exists
                if (!File.Exists(ffprobePath))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFprobe not found at: {ffprobePath}");
                    
                    // Try alternative paths
                    string altPath1 = Path.Combine(Path.GetDirectoryName(ffmpegMediaFramework.ExecName), "ffprobe");
                    string altPath2 = Path.Combine(Path.GetDirectoryName(ffmpegMediaFramework.ExecName), "ffprobe.exe");
                    
                    Tools.Logger.VideoLog.LogCall(this, $"Trying alternative path 1: {altPath1}");
                    if (File.Exists(altPath1))
                    {
                        ffprobePath = altPath1;
                        Tools.Logger.VideoLog.LogCall(this, $"Found FFprobe at alternative path: {altPath1}");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Trying alternative path 2: {altPath2}");
                        if (File.Exists(altPath2))
                        {
                            ffprobePath = altPath2;
                            Tools.Logger.VideoLog.LogCall(this, $"Found FFprobe at alternative path: {altPath2}");
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, "FFprobe not found at any expected location");
                            return false;
                        }
                    }
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFprobe found at: {ffprobePath}");
                }

                string ffprobeArgs = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"";
                Tools.Logger.VideoLog.LogCall(this, $"FFprobe command: {ffprobePath} {ffprobeArgs}");
                
                var processStartInfo = new ProcessStartInfo()
                {
                    Arguments = ffprobeArgs,
                    FileName = ffprobePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (var process = new Process())
                {
                    process.StartInfo = processStartInfo;
                    process.Start();
                    
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    Tools.Logger.VideoLog.LogCall(this, $"FFprobe completed with exit code: {process.ExitCode}");
                    Tools.Logger.VideoLog.LogCall(this, $"FFprobe output length: {output?.Length ?? 0} chars");
                    Tools.Logger.VideoLog.LogCall(this, $"FFprobe error output: '{error}'");
                    
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        // Parse JSON output to get video information
                        Tools.Logger.VideoLog.LogCall(this, $"FFprobe JSON output (first 800 chars): {output.Substring(0, Math.Min(800, output.Length))}");
                        if (output.Length > 800)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"FFprobe JSON output (chars 800-1600): {output.Substring(800, Math.Min(800, output.Length - 800))}");
                            if (output.Length > 1600)
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"FFprobe JSON output (last 400 chars): ...{output.Substring(Math.Max(0, output.Length - 400))}");
                            }
                        }
                        
                        // Log the complete JSON in chunks for debugging duration parsing issues
                        int chunkSize = 500;
                        for (int i = 0; i < output.Length; i += chunkSize)
                        {
                            int remainingLength = Math.Min(chunkSize, output.Length - i);
                            string chunk = output.Substring(i, remainingLength);
                            Tools.Logger.VideoLog.LogCall(this, $"FFprobe JSON chunk {i / chunkSize + 1}: {chunk}");
                        }
                        
                        // Look for duration field specifically before parsing
                        if (output.Contains("duration"))
                        {
                            Tools.Logger.VideoLog.LogCall(this, "Duration field found in JSON output");
                            
                            // Extract just the lines containing duration for focused debugging
                            var lines = output.Split('\n');
                            foreach (var line in lines.Where(l => l.Contains("duration")))
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"Duration line: {line.Trim()}");
                            }
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, "No duration field found in JSON output - this explains the parsing failure");
                        }
                        
                        ParseVideoInfo(output);
                        return true;
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"FFprobe failed for {fileName} (exit code: {process.ExitCode})");
                        
                        // Log detailed error information
                        if (!string.IsNullOrEmpty(error))
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"FFprobe stderr: {error}");
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, "FFprobe stderr was empty");
                        }
                        
                        if (string.IsNullOrEmpty(output))
                        {
                            Tools.Logger.VideoLog.LogCall(this, "FFprobe stdout was empty - no video info available");
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"FFprobe stdout (failed): {output}");
                        }
                        
                        // Log additional diagnostic information
                        Tools.Logger.VideoLog.LogCall(this, $"File size: {new FileInfo(filePath).Length} bytes");
                        Tools.Logger.VideoLog.LogCall(this, $"File extension: {Path.GetExtension(filePath)}");
                        Tools.Logger.VideoLog.LogCall(this, $"FFprobe path used: {ffprobePath}");
                        Tools.Logger.VideoLog.LogCall(this, $"FFprobe args used: {ffprobeArgs}");
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }

            // Fallback: use default values if we can't get file info
            Tools.Logger.VideoLog.LogCall(this, "Using default video file properties");
            if (VideoConfig.VideoMode == null)
            {
                VideoConfig.VideoMode = new Mode
                {
                    Width = 1280,
                    Height = 720,
                    FrameRate = 30.0f,
                    FrameWork = FrameWork.ffmpeg
                };
            }
            
            length = TimeSpan.FromMinutes(5); // Default 5 minutes
            frameRate = VideoConfig.VideoMode.FrameRate;
            
            return false;
        }

        private void ParseVideoInfo(string jsonOutput)
        {
            try
            {
                Tools.Logger.VideoLog.LogCall(this, "Parsing video file information from FFprobe output");
                
                // Simple JSON parsing for duration and frame rate
                // Look for duration in format section - try multiple patterns
                var durationMatch = Regex.Match(jsonOutput, @"""duration"":\s*""([^""]+)""");
                Tools.Logger.VideoLog.LogCall(this, $"Primary duration regex match: {durationMatch.Success}");
                
                // Let's also try to find any occurrence of "duration" in the JSON to debug
                var allDurationMatches = Regex.Matches(jsonOutput, @"""duration""[^,}]*");
                Tools.Logger.VideoLog.LogCall(this, $"All duration-related JSON entries found: {allDurationMatches.Count}");
                foreach (Match match in allDurationMatches)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Duration entry: {match.Value}");
                }
                
                // Test the exact pattern we expect based on the logs
                var testMatch = Regex.Match(jsonOutput, @"""duration"":\s*""([0-9.]+)""");
                Tools.Logger.VideoLog.LogCall(this, $"Test duration regex match: {testMatch.Success}");
                if (testMatch.Success)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Test duration value: '{testMatch.Groups[1].Value}'");
                }
                
                // We'll compute parsedLength locally to avoid overriding an already-determined XML-based length
                TimeSpan parsedLength = TimeSpan.Zero;

                if (durationMatch.Success)
                {
                    string durationStr = durationMatch.Groups[1].Value;
                    Tools.Logger.VideoLog.LogCall(this, $"Duration string found: '{durationStr}'");
                    if (double.TryParse(durationStr, out double durationSeconds))
                    {
                        parsedLength = TimeSpan.FromSeconds(durationSeconds);
                        Tools.Logger.VideoLog.LogCall(this, $"Video duration parsed successfully (ffprobe): {parsedLength}");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Failed to parse duration: '{durationStr}'");
                    }
                }
                else if (testMatch.Success)
                {
                    // Use the test pattern if the primary one failed
                    string durationStr = testMatch.Groups[1].Value;
                    Tools.Logger.VideoLog.LogCall(this, $"Duration string found via test pattern: '{durationStr}'");
                    if (double.TryParse(durationStr, out double durationSeconds))
                    {
                        parsedLength = TimeSpan.FromSeconds(durationSeconds);
                        Tools.Logger.VideoLog.LogCall(this, $"Video duration parsed successfully via test pattern (ffprobe): {parsedLength}");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Failed to parse duration from test pattern: '{durationStr}'");
                    }
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, "⚠ No duration found in FFprobe output using primary regex");
                    
                    // Try alternative patterns that might exist in the JSON
                    var altDurationMatch1 = Regex.Match(jsonOutput, @"""duration"":\s*([0-9.]+)");
                    var altDurationMatch2 = Regex.Match(jsonOutput, @"""duration"":\s*([0-9.]+),");
                    
                    Tools.Logger.VideoLog.LogCall(this, $"Alternative duration pattern 1 (no quotes): {altDurationMatch1.Success}");
                    Tools.Logger.VideoLog.LogCall(this, $"Alternative duration pattern 2 (with comma): {altDurationMatch2.Success}");
                    
                    if (altDurationMatch1.Success)
                    {
                        string durationStr = altDurationMatch1.Groups[1].Value;
                        Tools.Logger.VideoLog.LogCall(this, $"Alternative duration string found: '{durationStr}'");
                        if (double.TryParse(durationStr, out double durationSeconds))
                        {
                            parsedLength = TimeSpan.FromSeconds(durationSeconds);
                            Tools.Logger.VideoLog.LogCall(this, $"Video duration parsed from alternative pattern (ffprobe): {parsedLength}");
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"Failed to parse alternative duration string: '{durationStr}'");
                        }
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "No duration found in any pattern - video length will be unknown");
                        
                        // Try one more attempt to find any duration-like values for debugging
                        var debugMatches = Regex.Matches(jsonOutput, @"""[^""]*duration[^""]*"":\s*[^,}]+");
                        Tools.Logger.VideoLog.LogCall(this, $"Debug: Found {debugMatches.Count} duration-like entries");
                        foreach (Match match in debugMatches.Cast<Match>().Take(5))
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"Debug duration entry: {match.Value}");
                        }
                    }
                }
                
                // Apply parsedLength conservatively: if we already have a non-zero length (e.g., from XML timing),
                // do not extend it. Only set if length is zero or parsedLength is shorter.
                if (parsedLength > TimeSpan.Zero)
                {
                    if (length == TimeSpan.Zero || parsedLength < length)
                    {
                        length = parsedLength;
                        Tools.Logger.VideoLog.LogCall(this, $"FINAL LENGTH SET (from ffprobe, conservative): {length}");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Keeping existing length {length} (XML-derived) over longer ffprobe {parsedLength}");
                    }
                }
                
                // Look for frame rate in streams section
                var frameRateMatch = Regex.Match(jsonOutput, @"""r_frame_rate"":\s*""([^""]+)""");
                Tools.Logger.VideoLog.LogCall(this, $"FFprobe frame rate regex match: {frameRateMatch.Success}");
                if (frameRateMatch.Success)
                {
                    string frameRateStr = frameRateMatch.Groups[1].Value;
                    // Parse frame rate like "30/1" or "30000/1001"
                    var parts = frameRateStr.Split('/');
                    if (parts.Length == 2 && 
                        double.TryParse(parts[0], out double num) && 
                        double.TryParse(parts[1], out double den) && 
                        den > 0)
                    {
                        frameRate = num / den;
                        Tools.Logger.VideoLog.LogCall(this, $"✓ FFprobe detected frame rate: {frameRate} fps (from r_frame_rate: {frameRateStr})");
                    }
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, "⚠ FFprobe did NOT find r_frame_rate in JSON - will use default 30fps");
                }
                
                // Look for video dimensions
                var widthMatch = Regex.Match(jsonOutput, @"""width"":\s*(\d+)");
                var heightMatch = Regex.Match(jsonOutput, @"""height"":\s*(\d+)");
                if (widthMatch.Success && heightMatch.Success)
                {
                    int width = int.Parse(widthMatch.Groups[1].Value);
                    int height = int.Parse(heightMatch.Groups[1].Value);
                    
                    if (VideoConfig.VideoMode == null)
                    {
                        VideoConfig.VideoMode = new Mode
                        {
                            Width = width,
                            Height = height,
                            FrameRate = (float)frameRate,
                            FrameWork = FrameWork.ffmpeg
                        };
                    }
                    else
                    {
                        VideoConfig.VideoMode.Width = width;
                        VideoConfig.VideoMode.Height = height;
                        VideoConfig.VideoMode.FrameRate = (float)frameRate;
                    }
                    
                    Tools.Logger.VideoLog.LogCall(this, $"Video dimensions: {width}x{height}");
                }
                
                // Update start time for proper playback timing
                startTime = DateTime.Now;
                
                Tools.Logger.VideoLog.LogCall(this, $"Video file info parsed successfully: {VideoConfig.VideoMode?.Width}x{VideoConfig.VideoMode?.Height}@{frameRate}fps, duration: {length}");
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
        }

        public void Play()
        {
            Tools.Logger.VideoLog.LogCall(this, $"Play() called, current state: {State}, mediaTime: {mediaTime}");
            
            if (State == States.Paused)
            {
                // When paused, we stopped the FFmpeg process, so we need to restart from current position
                Tools.Logger.VideoLog.LogCall(this, "Resuming from pause by restarting video from current position");
                RestartWithSeek(mediaTime);
            }
            else if (State == States.Stopped)
            {
                // If stopped, restart from current position
                Tools.Logger.VideoLog.LogCall(this, "Video stopped, restarting from current position");
                RestartWithSeek(mediaTime);
            }
            else if (State == States.Running)
            {
                Tools.Logger.VideoLog.LogCall(this, "Video is already playing");
            }
            else
            {
                Tools.Logger.VideoLog.LogCall(this, $"Play() called from unexpected state: {State}");
                // Try to start from current position anyway
                RestartWithSeek(mediaTime);
            }
        }

        public override bool Pause()
        {
            Tools.Logger.VideoLog.LogCall(this, $"Pause() called, current state: {State}");
            
            if (State == States.Running)
            {
                try
                {
                    Tools.Logger.VideoLog.LogCall(this, "Pausing video playback by stopping FFmpeg process");
                    
                    // For video file playback, we can't easily pause FFmpeg, so we stop it instead
                    // The current mediaTime position is preserved so Play() can resume from there
                    
                    // Stop the FFmpeg process immediately and synchronously
                    if (process != null && !process.HasExited)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Killing FFmpeg process {process.Id} for pause");
                        try
                        {
                            process.Kill();
                            // Give it a short time to exit gracefully, but don't wait too long
                            if (process.WaitForExit(1000))
                            {
                                Tools.Logger.VideoLog.LogCall(this, "FFmpeg process stopped successfully for pause");
                            }
                            else
                            {
                                Tools.Logger.VideoLog.LogCall(this, "FFmpeg process didn't exit quickly, but continuing with pause");
                            }
                        }
                        catch (Exception killEx)
                        {
                            Tools.Logger.VideoLog.LogException(this, killEx);
                            Tools.Logger.VideoLog.LogCall(this, "Error killing FFmpeg process during pause, but continuing");
                        }
                    }
                    else if (process != null)
                    {
                        Tools.Logger.VideoLog.LogCall(this, "FFmpeg process already exited");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "No FFmpeg process to kill");
                    }
                    
                    // Set state to paused 
                    bool result = base.Pause();
                    Tools.Logger.VideoLog.LogCall(this, $"Pause completed, state now: {State}");
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
                Tools.Logger.VideoLog.LogCall(this, $"Cannot pause from state: {State}");
                return false;
            }
        }
        
        public override bool Stop()
        {
            Tools.Logger.VideoLog.LogCall(this, $"Stop() called, current state: {State}");
            
            if (State != States.Stopped)
            {
                try
                {
                    Tools.Logger.VideoLog.LogCall(this, "Stopping video playback by killing FFmpeg process");
                    
                    // Kill the FFmpeg process immediately and synchronously BEFORE calling base.Stop()
                    // This prevents the base class's async cleanup from racing with our sync cleanup
                    if (process != null && !process.HasExited)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Killing FFmpeg process {process.Id} for stop");
                        try
                        {
                            process.Kill();
                            // Give it a short time to exit gracefully, but don't wait too long
                            if (process.WaitForExit(1000))
                            {
                                Tools.Logger.VideoLog.LogCall(this, "FFmpeg process stopped successfully");
                            }
                            else
                            {
                                Tools.Logger.VideoLog.LogCall(this, "FFmpeg process didn't exit quickly, but continuing with stop");
                            }
                        }
                        catch (Exception killEx)
                        {
                            Tools.Logger.VideoLog.LogException(this, killEx);
                            Tools.Logger.VideoLog.LogCall(this, "Error killing FFmpeg process during stop, but continuing");
                        }
                    }
                    else if (process != null)
                    {
                        Tools.Logger.VideoLog.LogCall(this, "FFmpeg process already exited");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "No FFmpeg process to kill");
                    }
                    
                    // Now call base stop to set state and handle other cleanup
                    // Since we already killed the process, the base class's async cleanup won't find it
                    bool result = base.Stop();
                    Tools.Logger.VideoLog.LogCall(this, $"Stop completed, state now: {State}");
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
                Tools.Logger.VideoLog.LogCall(this, "Already stopped");
                return true;
            }
        }
        
        public override bool Unpause()
        {
            Tools.Logger.VideoLog.LogCall(this, $"Unpause() called, current state: {State}");
            
            if (State == States.Paused)
            {
                try
                {
                    Tools.Logger.VideoLog.LogCall(this, "Unpausing by restarting video from current position");
                    
                    // Since we stopped the FFmpeg process in Pause(), we need to restart it from the current position
                    // The mediaTime should be preserved from when we paused
                    RestartWithSeek(mediaTime);
                    
                    bool result = base.Unpause();
                    Tools.Logger.VideoLog.LogCall(this, $"Unpause completed, state now: {State}");
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
                Tools.Logger.VideoLog.LogCall(this, $"Cannot unpause from state: {State}");
                return false;
            }
        }

        public void SetPosition(TimeSpan timeSpan)
        {
            if (timeSpan < TimeSpan.Zero)
                timeSpan = TimeSpan.Zero;
            
            if (length > TimeSpan.Zero && timeSpan > length)
                timeSpan = length;

            Tools.Logger.VideoLog.LogCall(this, $"Setting video position to: {timeSpan}");
            
            // Update the start time to maintain correct media time
            startTime = DateTime.Now - timeSpan;
            mediaTime = timeSpan;
            isAtEnd = false;
            
            // Use efficient seeking without restarting FFmpeg
            SeekToPosition(timeSpan);
        }

        private void SeekToPosition(TimeSpan seekTime)
        {
            try
            {
                // For video files, always restart FFmpeg with the new position for accurate seeking
                // This is much more reliable than trying to skip frames
                Tools.Logger.VideoLog.LogCall(this, $"Seeking to {seekTime.TotalSeconds:F1}s - restarting FFmpeg");
                RestartWithSeek(seekTime);
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, "Seek failed, falling back to restart seek");
                RestartWithSeek(seekTime);
            }
        }

        private void RestartWithSeek(TimeSpan seekTime)
        {
            try
            {
                // Get filename for logging
                string fileName = Path.GetFileName(VideoConfig.FilePath);
                Tools.Logger.VideoLog.LogCall(this, $"Restarting video playback ({fileName}) with seek to: {seekTime}");
                
                // For seeking, immediately kill the current process synchronously - no graceful shutdown needed
                if (process != null)
                {
                    if (!process.HasExited)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Immediately killing FFmpeg process for seek (PID: {process.Id})");
                        
                        // Force kill the process immediately and wait for it to exit
                        run = false;
                        try
                        {
                            process.Kill();
                            // Wait for the process to actually exit (synchronous)
                            if (process.WaitForExit(1000))
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"FFmpeg process killed and exited successfully for seek");
                            }
                            else
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"FFmpeg process didn't exit quickly after kill, but continuing");
                            }
                        }
                        catch (Exception killEx)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"Process kill exception: {killEx.Message}");
                        }
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Process was already exited (Exit Code: {process.ExitCode})");
                    }
                    
                    // Dispose the process immediately
                    try
                    {
                        process.Dispose();
                        Tools.Logger.VideoLog.LogCall(this, "Process disposed successfully for seek");
                    }
                    catch (Exception disposeEx)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Process dispose exception: {disposeEx.Message}");
                    }
                    
                    process = null; // Set to null after disposal
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"No existing process to kill (process was null)");
                }
                
                // Ensure the reading thread has stopped before starting new process
                if (thread != null && thread.IsAlive)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Reading thread still alive, waiting for it to finish");
                    run = false; // Make sure the thread loop will exit
                    
                    if (!thread.Join(2000)) // Wait up to 2 seconds for thread to finish
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Reading thread didn't finish in 2 seconds, proceeding anyway");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Reading thread finished successfully");
                    }
                    thread = null;
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Reading thread was already stopped or null");
                }
                
                // Brief pause to ensure process cleanup completes before starting new process
                // This prevents resource conflicts that can cause flickering
                Tools.Logger.VideoLog.LogCall(this, $"Waiting 100ms for process cleanup to complete");
                System.Threading.Thread.Sleep(100); // Increased from 50ms to 100ms for more reliable cleanup
                
                StartSeekProcess(seekTime);
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, "Error during seek restart");
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
                    // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Buffers reinitialized: {width}x{height}");
                }
                
                // Update the FFmpeg command to include seek
                string filePath = VideoConfig.FilePath;
                // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Original file path: {filePath}");
                
                if (!Path.IsPathRooted(filePath))
                {
                    filePath = Path.GetFullPath(filePath);
                    Tools.Logger.VideoLog.LogCall(this, $"SEEK: Converted to absolute path: {filePath}");
                }
                
                // Verify file exists before attempting to start FFmpeg
                if (!File.Exists(filePath))
                {
                    // Tools.Logger.VideoLog.LogCall(this, $"SEEK: ERROR - Video file does not exist: {filePath}");
                    return; // Don't try to start FFmpeg with a non-existent file
                }
                
                // Tools.Logger.VideoLog.LogCall(this, $"SEEK: File verified to exist, size: {new FileInfo(filePath).Length} bytes");

                // Build FFmpeg command with seek parameter
                // Put -ss BEFORE -i for efficient input seeking (avoids decoding unnecessary frames)
                // Input options (-ss, -re) must come before -i, output options come after
                
                // Build video filter chain based on playback speed (same as GetProcessStartInfo)
                string videoFilter = "vflip"; // Base filter to flip video vertically
                string reFlag = "-re"; // Default: use -re for proper timing
                
                // Add playback speed control if slow motion is enabled
                if (playbackSpeed == PlaybackSpeed.Slow)
                {
                    // Use setpts filter to slow down video to 25% speed
                    // setpts=4*PTS makes video 4x slower (25% speed)
                    videoFilter += ",setpts=4*PTS";
                    reFlag = ""; // Remove -re flag for slow motion to allow setpts to work properly
                    // Tools.Logger.VideoLog.LogCall(this, "SEEK: Slow motion enabled - video will play at 25% speed using setpts filter (no -re)");
                }
                
                string ffmpegArgs = $"-ss {seekTime.TotalSeconds:F3} " +  // Seek to specific time (BEFORE input for fast seek)
                                   $"{(string.IsNullOrEmpty(reFlag) ? "" : reFlag + " ")}" +  // Read input at native frame rate (only for normal speed)
                                   $"-i \"{filePath}\" " +  // Input file
                                   $"-fflags +genpts " +  // Generate presentation timestamps (output option)
                                   $"-avoid_negative_ts make_zero " +  // Handle negative timestamps (output option)
                                   $"-threads 1 " +  // Single thread (output option)
                                   $"-an " +  // No audio (output option)
                                   $"-vf \"{videoFilter}\" " +  // Apply video filters (vflip and optionally setpts for slow motion)
                                   $"-pix_fmt rgba " +  // RGBA pixel format (output option)
                                   $"-f rawvideo pipe:1";  // Raw video output to stdout (output option)

                // Tools.Logger.VideoLog.LogCall(this, $"SEEK: FFMPEG Video File Seek ({fileName}): {ffmpegArgs}");
                
                // Mark this as a seek operation for logging
                isSeekOperation = true;
                
                // Use the base class infrastructure for proper process management
                // Tools.Logger.VideoLog.LogCall(this, $"SEEK: About to start process using base infrastructure...");
                
                // Temporarily store the seek args in a field that GetProcessStartInfo can use
                currentSeekArgs = ffmpegArgs;
                
                // Use base.Start() which handles all the process setup correctly
                // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Calling base.Start() with currentSeekArgs set");
                bool result = base.Start();
                
                if (result)
                {
                    // Tools.Logger.VideoLog.LogCall(this, $"SEEK: FFmpeg seek process started successfully via base.Start()");
                    // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Process ID: {process?.Id}, HasExited: {process?.HasExited}");
                    
                    // For seek operations, we need to ensure initialization happens
                    if (process != null)
                    {
                        // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Adding VideoFileErrorDataReceived event handler");
                        process.ErrorDataReceived += VideoFileErrorDataReceived;
                        try
                        {
                            process.EnableRaisingEvents = true;
                            process.Exited += (s, e) =>
                            {
                                Tools.Logger.VideoLog.LogCall(this, $"SEEK: Process exited - clamping mediaTime to length {length.TotalSeconds:F3}s");
                                mediaTime = length;
                                isAtEnd = true;
                            };
                        }
                        catch { }
                        
                        // For seek operations, force initialization after a short delay since FFmpeg 
                        // might not produce the exact patterns the base class expects
                        Task.Delay(500).ContinueWith(_ => 
                        {
                            if (!inited && run)
                            {
                                // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Force initializing frame processing for seek operation");
                                InitializeFrameProcessing();
                            }
                        });
                        
                        // Check if the process is actually running
                        if (process.HasExited)
                        {
                            // Tools.Logger.VideoLog.LogCall(this, $"SEEK: WARNING - Process exited immediately with exit code: {process.ExitCode}");
                        }
                    }
                    else
                    {
                        // Tools.Logger.VideoLog.LogCall(this, $"SEEK: WARNING - Process is null after successful base.Start()");
                    }
                }
                else
                {
                    // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Failed to start FFmpeg seek process via base.Start()");
                    // Tools.Logger.VideoLog.LogCall(this, $"SEEK: State after failed start: {State}, Connected: {Connected}");
                }
                
                // Clear the seek args
                currentSeekArgs = null;
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, "Error during seek restart");
            }
        }

        public void SetPosition(DateTime dateTime)
        {
            // The dateTime comes from SeekNode and represents a position in the overall timeline
            // We need to convert this to a TimeSpan offset from THIS video file's beginning
            
            // Tools.Logger.VideoLog.LogCall(this, $"SEEK: SetPosition(DateTime): dateTime={dateTime:HH:mm:ss.fff}");
            // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Video file StartTime={StartTime:HH:mm:ss.fff}, Length={Length.TotalSeconds:F3}s");
            
            TimeSpan seekTimeSpan;
            
            // Calculate offset from this video file's start time
            if (StartTime != DateTime.MinValue)
            {
                // dateTime is absolute timeline position, StartTime is this video file's start
                seekTimeSpan = dateTime - StartTime;
                // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Calculated offset: {seekTimeSpan.TotalSeconds:F3}s (dateTime - StartTime)");
            }
            else
            {
                // Fallback: if StartTime not available, assume dateTime is already relative
                // This shouldn't normally happen, but provides a safety net
                seekTimeSpan = TimeSpan.Zero;
                // Tools.Logger.VideoLog.LogCall(this, $"SEEK: StartTime not available, defaulting to 0");
            }
            
            // Ensure we don't seek to negative time
            if (seekTimeSpan < TimeSpan.Zero)
            {
                // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Clamping negative offset {seekTimeSpan.TotalSeconds:F3}s to 0");
                seekTimeSpan = TimeSpan.Zero;
            }
            
            // Clamp to video length if known
            if (Length > TimeSpan.Zero && seekTimeSpan > Length)
            {
                // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Clamping offset {seekTimeSpan.TotalSeconds:F3}s to video length {Length.TotalSeconds:F3}s");
                seekTimeSpan = Length;
            }
            
            // Tools.Logger.VideoLog.LogCall(this, $"SEEK: Final seek offset: {seekTimeSpan.TotalSeconds:F3}s");
            SetPosition(seekTimeSpan);
        }

        public void NextFrame()
        {
            TimeSpan frameDuration = TimeSpan.FromSeconds(1.0 / frameRate);
            SetPosition(mediaTime + frameDuration);
        }

        public void PrevFrame()
        {
            TimeSpan frameDuration = TimeSpan.FromSeconds(1.0 / frameRate);
            SetPosition(mediaTime - frameDuration);
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
                FrameWork = FrameWork.ffmpeg
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
            // Method to fix upside down video if needed
            Tools.Logger.VideoLog.LogCall(this, "Fixing video orientation (upside down)");
            
            // Restart with transpose filter
            string filePath = VideoConfig.FilePath;
            if (!Path.IsPathRooted(filePath))
            {
                filePath = Path.GetFullPath(filePath);
            }

            string ffmpegArgs = $"-re " +  // Read input at native frame rate for proper timing
                               $"-i \"{filePath}\" " +
                               $"-fflags +genpts " +
                               $"-avoid_negative_ts make_zero " +
                               $"-threads 1 " +
                               $"-an " +
                               // Let FFmpeg preserve original video timing instead of forcing frame rate
                               $"-vf \"vflip,transpose=2,transpose=2\" " +  // Flip vertically and rotate 180 degrees
                               $"-pix_fmt rgba " +
                               $"-f rawvideo pipe:1";

            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Video File with orientation fix: {ffmpegArgs}");
            
            // Restart the process with orientation fix
            Stop();
            Thread.Sleep(500);
            
            var processStartInfo = ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
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
                
                // Load the recording info using absolute paths
                var recordingInfo = IOTools.ReadSingle<RecodingInfo>(Path.GetDirectoryName(absoluteRecordInfoPath), Path.GetFileName(absoluteRecordInfoPath));
                if (recordingInfo != null && recordingInfo.FrameTimes != null && recordingInfo.FrameTimes.Length > 0)
                {
                    // Set the loaded frame times - need to access via reflection or create a protected setter
                    SetFrameTimes(recordingInfo.FrameTimes);
                    
                    // Update the start time based on the first frame
                    var firstFrame = recordingInfo.FrameTimes.OrderBy(f => f.Time).First();
                    var lastFrame = recordingInfo.FrameTimes.OrderBy(f => f.Time).Last();
                    startTime = firstFrame.Time;
                    originalVideoStartTime = firstFrame.Time; // Preserve the original video start time for seeking
                    
                    // Get ffprobe duration first to use as fallback
                    TimeSpan ffprobeDuration = DetermineActualDuration(videoFilePath);
                    
                    // Use unified duration calculation logic with ffprobe as fallback
                    var xmlDuration = UnifiedFrameTimingManager.CalculateVideoDuration(recordingInfo.FrameTimes, ffprobeDuration);
                    
                    // Duration selection: prefer the shorter of XML-derived duration and container duration
                    // This avoids progress bar overhang when one source slightly overestimates length
                    if (ffprobeDuration > TimeSpan.Zero && xmlDuration > TimeSpan.Zero)
                    {
                        double xmlS = xmlDuration.TotalSeconds;
                        double ffS = ffprobeDuration.TotalSeconds;
                        double diff = Math.Abs(xmlS - ffS);
                        Tools.Logger.VideoLog.LogCall(this, $"Duration comparison: XML={xmlS:F3}s, ffprobe={ffS:F3}s, |diff|={diff:F3}s");
                        var chosen = TimeSpan.FromSeconds(Math.Min(xmlS, ffS));
                        length = chosen;
                        Tools.Logger.VideoLog.LogCall(this, $"FINAL LENGTH SET (min of XML/ffprobe): {length.TotalSeconds:F3}s");
                    }
                    else if (xmlDuration > TimeSpan.Zero)
                    {
                        length = xmlDuration;
                        Tools.Logger.VideoLog.LogCall(this, $"FINAL LENGTH SET: {length.TotalSeconds:F3}s (XML only)");
                    }
                    else if (ffprobeDuration > TimeSpan.Zero)
                    {
                        length = ffprobeDuration;
                        Tools.Logger.VideoLog.LogCall(this, $"FINAL LENGTH SET: {length.TotalSeconds:F3}s (ffprobe only)");
                    }
                    
                    // Validate frame timing consistency to detect platform-specific issues
                    bool isConsistent = UnifiedFrameTimingManager.ValidateFrameTimingConsistency(
                        recordingInfo.FrameTimes, VideoConfig.VideoMode?.FrameRate ?? 30.0f);
                    
                    if (!isConsistent)
                    {
                        Tools.Logger.VideoLog.LogCall(this, "WARNING: Frame timing data appears inconsistent - this may cause playback issues");
                    }
                }
                else
                {
                    // No XML timing data - use actual MP4 duration as fallback
                    TimeSpan actualMp4Duration = DetermineActualDuration(videoFilePath);
                    if (actualMp4Duration > TimeSpan.Zero)
                    {
                        length = actualMp4Duration;
                        Tools.Logger.VideoLog.LogCall(this, $"LENGTH DEBUG: No XML timing - using MP4 duration={actualMp4Duration.TotalSeconds:F1}s");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "LENGTH DEBUG: Could not determine video duration - using default");
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
                Tools.Logger.VideoLog.LogCall(this, $"Determining actual MP4 duration for: {videoFilePath}");
                
                // Use ffprobe to get video information
                string ffprobeArgs = $"-v quiet -print_format json -show_format -show_streams \"{videoFilePath}\"";
                var processStartInfo = ffmpegMediaFramework.GetProcessStartInfo($"ffprobe {ffprobeArgs}");
                processStartInfo.RedirectStandardOutput = true;
                processStartInfo.UseShellExecute = false;
                processStartInfo.CreateNoWindow = true;

                using (var process = new Process())
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
                                Tools.Logger.VideoLog.LogCall(this, $"MP4 duration determined: {duration.TotalSeconds:F1}s");
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
                                Tools.Logger.VideoLog.LogCall(this, $"MP4 duration determined (alt pattern): {duration.TotalSeconds:F1}s");
                                return duration;
                            }
                        }
                    }
                }
                
                Tools.Logger.VideoLog.LogCall(this, "Could not determine MP4 duration from ffprobe");
                return TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, $"Error determining MP4 duration: {ex.Message}");
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