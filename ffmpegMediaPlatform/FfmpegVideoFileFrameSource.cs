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
using UI.Video;

namespace FfmpegMediaPlatform
{
    public class FfmpegVideoFileFrameSource : FfmpegFrameSource, IPlaybackFrameSource
    {
        private DateTime startTime;
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

        public DateTime StartTime => startTime;
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
            set => playbackSpeed = value; 
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

        public DateTime CurrentTime => startTime + mediaTime;

        public FfmpegVideoFileFrameSource(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig)
            : base(ffmpegMediaFramework, videoConfig)
        {
            Tools.Logger.VideoLog.LogCall(this, "PROGRESSBAR FfmpegVideoFileFrameSource constructor called");
            
            // Initialize playback properties
            startTime = DateTime.Now; // Default start time
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
            Tools.Logger.VideoLog.LogCall(this, "PROGRESSBAR About to call LoadFrameTimesFromRecordInfo");
            LoadFrameTimesFromRecordInfo();
            Tools.Logger.VideoLog.LogCall(this, "PROGRESSBAR LoadFrameTimesFromRecordInfo completed");
        }

        protected override ProcessStartInfo GetProcessStartInfo()
        {
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
            bool isMac = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
            
            // Get just the filename for logging
            string fileName = Path.GetFileName(filePath);
            
            Tools.Logger.VideoLog.LogCall(this, $"Video file playback: {fileName}");
            Tools.Logger.VideoLog.LogCall(this, $"File exists: {File.Exists(filePath)}");
            Tools.Logger.VideoLog.LogCall(this, $"File size: {new FileInfo(filePath).Length} bytes");
            Tools.Logger.VideoLog.LogCall(this, $"Is WMV: {isWMV}");
            Tools.Logger.VideoLog.LogCall(this, $"Is Mac: {isMac}");

            // Build FFmpeg command for video file playback with interactive seeking support
            // Use proper settings for smooth video file playback
            string ffmpegArgs = $"-re " +  // Read input at native frame rate for proper timing
                               $"-i \"{filePath}\" " +
                               $"-fflags +genpts " +  // Generate presentation timestamps
                               $"-avoid_negative_ts make_zero " +  // Handle negative timestamps
                               $"-threads 1 " +
                               $"-an " +  // No audio
                               $"-vf \"vflip\" " +  // Flip video vertically to fix upside down orientation
                               // Let FFmpeg preserve original video timing instead of forcing frame rate
                               $"-pix_fmt rgba " +
                               $"-f rawvideo pipe:1";

            // Add special handling for WMV files on Mac if needed
            if (isWMV && isMac)
            {
                Tools.Logger.VideoLog.LogCall(this, "WMV file detected on Mac - using standard FFmpeg WMV support");
                // FFmpeg handles WMV files well on Mac, no special parameters needed
            }

            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Video File Playback ({fileName}): {ffmpegArgs}");
            return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
        }

        public override bool Start()
        {
            Tools.Logger.VideoLog.LogCall(this, $"FfmpegVideoFileFrameSource.Start() called, current state: {State}");
            
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
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Video File: {e.Data}");
                
                // Initialize immediately when we see any FFmpeg output for video files
                if (!inited && (e.Data.Contains("frame=") || e.Data.Contains("Stream") || e.Data.Contains("Duration") || e.Data.Contains("Input")))
                {
                    Tools.Logger.VideoLog.LogCall(this, "Video file playback detected - initializing frame processing");
                    InitializeFrameProcessing();
                }
                
                // Special handling for WMV files
                bool isWMV = VideoConfig.FilePath?.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase) ?? false;
                if (isWMV)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"WMV file processing: {e.Data}");
                    
                    // Check for WMV-specific issues
                    if (e.Data.Contains("Invalid data found") || e.Data.Contains("Error while decoding"))
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"WMV decoding issue detected: {e.Data}");
                    }
                    
                    // Check for successful WMV processing
                    if (e.Data.Contains("Stream") && e.Data.Contains("Video"))
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"WMV video stream detected: {e.Data}");
                    }
                }
                
                // Log any errors or warnings
                if (e.Data.Contains("error") || e.Data.Contains("Error") || e.Data.Contains("warning") || e.Data.Contains("Warning"))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Video File Error/Warning: {e.Data}");
                }
            }
        }

        protected override void ProcessImage()
        {
            if (!inited)
            {
                Tools.Logger.VideoLog.LogCall(this, "Video file frame processing: Not initialized yet");
                return;
            }

            // Initialize startTime if not set (first frame)
            if (startTime == DateTime.MinValue)
            {
                startTime = DateTime.Now;
                Tools.Logger.VideoLog.LogCall(this, $"Video file playback started at: {startTime}");
            }

            // Call base implementation for frame processing
            base.ProcessImage();
            
            // Update media time for video file playback
            if (startTime != DateTime.MinValue)
            {
                mediaTime = DateTime.Now - startTime;
                
                // Check if we've reached the end of the video
                if (length > TimeSpan.Zero && mediaTime >= length)
                {
                    isAtEnd = true;
                    if (repeat)
                    {
                        // Restart from beginning
                        SetPosition(TimeSpan.Zero);
                    }
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
                string ffprobePath = ffmpegMediaFramework.ExecName.Replace("ffmpeg", "ffprobe");
                
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
                
                if (durationMatch.Success)
                {
                    string durationStr = durationMatch.Groups[1].Value;
                    Tools.Logger.VideoLog.LogCall(this, $"Duration string found: '{durationStr}'");
                    if (double.TryParse(durationStr, out double durationSeconds))
                    {
                        length = TimeSpan.FromSeconds(durationSeconds);
                        Tools.Logger.VideoLog.LogCall(this, $"Video duration parsed successfully: {length}");
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
                        length = TimeSpan.FromSeconds(durationSeconds);
                        Tools.Logger.VideoLog.LogCall(this, $"Video duration parsed successfully via test pattern: {length}");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"Failed to parse duration from test pattern: '{durationStr}'");
                    }
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, "No duration found in FFprobe output using primary regex");
                    
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
                            length = TimeSpan.FromSeconds(durationSeconds);
                            Tools.Logger.VideoLog.LogCall(this, $"Video duration parsed from alternative pattern: {length}");
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
                
                // Look for frame rate in streams section
                var frameRateMatch = Regex.Match(jsonOutput, @"""r_frame_rate"":\s*""([^""]+)""");
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
                        Tools.Logger.VideoLog.LogCall(this, $"Video frame rate: {frameRate} fps");
                    }
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
            if (State == States.Paused)
            {
                Unpause();
            }
        }

        public override bool Pause()
        {
            if (State == States.Running && process != null && !process.HasExited)
            {
                try
                {
                    // Send pause signal to FFmpeg process
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    {
                        // Windows: Use space key to toggle pause
                        process.StandardInput.WriteLine(" ");
                        process.StandardInput.Flush();
                    }
                    else
                    {
                        // macOS/Linux: Use SIGSTOP to pause the process
                        var pauseProcess = new Process();
                        pauseProcess.StartInfo.FileName = "kill";
                        pauseProcess.StartInfo.Arguments = $"-STOP {process.Id}";
                        pauseProcess.StartInfo.UseShellExecute = false;
                        pauseProcess.Start();
                        pauseProcess.WaitForExit();
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
        
        public override bool Unpause()
        {
            if (State == States.Paused && process != null && !process.HasExited)
            {
                try
                {
                    // Send unpause signal to FFmpeg process
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    {
                        // Windows: Use space key to toggle pause
                        process.StandardInput.WriteLine(" ");
                        process.StandardInput.Flush();
                    }
                    else
                    {
                        // macOS/Linux: Use SIGCONT to resume the process
                        var resumeProcess = new Process();
                        resumeProcess.StartInfo.FileName = "kill";
                        resumeProcess.StartInfo.Arguments = $"-CONT {process.Id}";
                        resumeProcess.StartInfo.UseShellExecute = false;
                        resumeProcess.Start();
                        resumeProcess.WaitForExit();
                    }
                    
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
                if (process == null || process.HasExited)
                {
                    Tools.Logger.VideoLog.LogCall(this, "Process not running, using restart seek");
                    RestartWithSeek(seekTime);
                    return;
                }

                // For small seeks (within 5 seconds), try to continue without restart
                TimeSpan currentMediaTime = mediaTime;
                TimeSpan seekDifference = seekTime > currentMediaTime ? 
                    seekTime - currentMediaTime : currentMediaTime - seekTime;
                
                if (seekDifference.TotalSeconds <= 5.0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Small seek detected ({seekDifference.TotalSeconds:F1}s), continuing without restart");
                    
                    // Update timing state for small seeks
                    startTime = DateTime.Now - seekTime;
                    mediaTime = seekTime;
                    isAtEnd = false;
                    
                    // Skip frames until we reach the desired position
                    // This is handled by the timing logic in ProcessImage
                    return;
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Large seek detected ({seekDifference.TotalSeconds:F1}s), using restart seek");
                    RestartWithSeek(seekTime);
                }
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
                
                // Stop current process and wait for clean shutdown
                if (process != null && !process.HasExited)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Stopping current FFmpeg process for seek");
                    Stop();
                    Thread.Sleep(1000); // Give more time for clean shutdown
                }
                
                // Reset state for clean restart
                inited = false;
                Connected = false;
                
                // Update the FFmpeg command to include seek
                string filePath = VideoConfig.FilePath;
                if (!Path.IsPathRooted(filePath))
                {
                    filePath = Path.GetFullPath(filePath);
                }

                // Build FFmpeg command with seek parameter
                string ffmpegArgs = $"-ss {seekTime.TotalSeconds:F3} " +  // Seek to specific time
                                   $"-re " +  // Read input at native frame rate for proper timing
                                   $"-i \"{filePath}\" " +
                                   $"-fflags +genpts " +
                                   $"-avoid_negative_ts make_zero " +
                                   $"-threads 1 " +
                                   $"-an " +
                                   $"-vf \"vflip\" " +  // Flip video vertically to fix upside down orientation
                                   // Let FFmpeg preserve original video timing instead of forcing frame rate
                                   $"-pix_fmt rgba " +
                                   $"-f rawvideo pipe:1";

                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Video File Seek ({fileName}): {ffmpegArgs}");
                
                // Create and start new process with seek
                var processStartInfo = ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
                process = new Process();
                process.StartInfo = processStartInfo;
                
                // Start the process
                if (process.Start())
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFmpeg seek process started successfully (PID: {process.Id})");
                    
                    // Set up error data received handler
                    process.ErrorDataReceived += VideoFileErrorDataReceived;
                    process.BeginErrorReadLine();
                    
                    // Set up output stream
                    if (process.StandardOutput.BaseStream.CanTimeout)
                    {
                        process.StandardOutput.BaseStream.ReadTimeout = 10000;
                    }
                    
                    // Start the reading thread
                    run = true;
                    thread = new Thread(Run);
                    thread.Start();
                    
                    // Wait a bit for initialization
                    Thread.Sleep(2000);
                    
                    // Force initialization if needed
                    if (!inited)
                    {
                        Tools.Logger.VideoLog.LogCall(this, "Forcing initialization after seek restart");
                        InitializeFrameProcessing();
                    }
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, "Failed to start FFmpeg seek process");
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, "Error during seek restart");
            }
        }

        public void SetPosition(DateTime dateTime)
        {
            TimeSpan timeSpan = dateTime - startTime;
            SetPosition(timeSpan);
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
                    Tools.Logger.VideoLog.LogCall(this, "PROGRESSBAR No video file path available for frame times loading");
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
                
                string recordInfoPath = basePath + ".recordinfo.xml";
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Looking for record info file: {recordInfoPath}");
                
                // Convert to absolute path for proper file checking and IOTools usage
                string absoluteRecordInfoPath = Path.GetFullPath(recordInfoPath);
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Absolute record info path: {absoluteRecordInfoPath}");
                
                if (!File.Exists(absoluteRecordInfoPath))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR No .recordinfo.xml file found at {absoluteRecordInfoPath}");
                    return;
                }
                
                // Load the recording info using absolute paths
                var recordingInfo = IOTools.ReadSingle<RecodingInfo>(Path.GetDirectoryName(absoluteRecordInfoPath), Path.GetFileName(absoluteRecordInfoPath));
                if (recordingInfo != null && recordingInfo.FrameTimes != null && recordingInfo.FrameTimes.Length > 0)
                {
                    // Set the loaded frame times - need to access via reflection or create a protected setter
                    SetFrameTimes(recordingInfo.FrameTimes);
                    
                    Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Loaded {recordingInfo.FrameTimes.Length} frame times from {recordInfoPath}");
                    
                    // Update the start time based on the first frame
                    var firstFrame = recordingInfo.FrameTimes.OrderBy(f => f.Time).First();
                    var lastFrame = recordingInfo.FrameTimes.OrderBy(f => f.Time).Last();
                    startTime = firstFrame.Time;
                    length = lastFrame.Time - firstFrame.Time;
                    
                    Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Video timeline: start={startTime:HH:mm:ss.fff}, length={length.TotalSeconds:F1}s");
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR .recordinfo.xml file exists but contains no frame times");
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Failed to load frame times from .recordinfo.xml: {ex.Message}");
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
                        Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR SetFrameTimes: Set {newFrameTimes.Length} frame times via reflection");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "PROGRESSBAR SetFrameTimes: frameTimes field is null");
                    }
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, "PROGRESSBAR SetFrameTimes: Could not find frameTimes field via reflection");
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR SetFrameTimes failed: {ex.Message}");
            }
        }
    }
} 