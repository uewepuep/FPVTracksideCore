using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tools;
using ImageServer;

namespace FfmpegMediaPlatform
{
    /// <summary>
    /// Manages recording by accepting RGBA frames and piping them to ffmpeg stdin for MP4 output.
    /// Collects frame timing data for .recordinfo.xml generation.
    /// </summary>
    public class RgbaRecorderManager : IDisposable
    {
        private readonly FfmpegMediaFramework ffmpegMediaFramework;
        private Process recordingProcess;
        private bool isRecording;
        private string currentOutputPath;
        private readonly object recordingLock = new object();
        private ICaptureFrameSource frameSourceForRecordInfo;
        
        // Frame timing collection
        private List<FrameTime> frameTimes;
        private DateTime recordingStartTime;
        private int frameWidth;
        private int frameHeight;
        private float frameRate;
        private int recordingFrameCounter;
        private DateTime lastFrameWriteTime;
        private float detectedFrameRate;
        private bool frameRateDetected;
        private double targetFrameInterval; // Target interval between frames in milliseconds

        public bool IsRecording => isRecording;
        public string CurrentOutputPath => currentOutputPath;
        public FrameTime[] FrameTimes => frameTimes?.ToArray() ?? new FrameTime[0];

        public event Action<string> RecordingStarted;
        public event Action<string, bool> RecordingStopped; // path, success

        public RgbaRecorderManager(FfmpegMediaFramework ffmpegMediaFramework)
        {
            this.ffmpegMediaFramework = ffmpegMediaFramework ?? throw new ArgumentNullException(nameof(ffmpegMediaFramework));
            this.frameTimes = new List<FrameTime>();
        }

        /// <summary>
        /// Start recording RGBA frames to an MP4 file
        /// </summary>
        /// <param name="outputPath">The output MP4 file path</param>
        /// <param name="frameWidth">Width of RGBA frames</param>
        /// <param name="frameHeight">Height of RGBA frames</param>
        /// <param name="frameRate">Frame rate for recording</param>
        /// <param name="captureFrameSource">The frame source to use for .recordinfo.xml generation (optional)</param>
        /// <returns>True if recording started successfully</returns>
        public bool StartRecording(string outputPath, int frameWidth, int frameHeight, float frameRate, ICaptureFrameSource captureFrameSource = null)
        {
            lock (recordingLock)
            {
                if (isRecording)
                {
                    Tools.Logger.VideoLog.LogCall(this, "Recording already in progress, cannot start new recording");
                    return false;
                }

                try
                {
                    // Ensure output directory exists
                    string outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    currentOutputPath = outputPath;
                    frameSourceForRecordInfo = captureFrameSource;
                    this.frameWidth = frameWidth;
                    this.frameHeight = frameHeight;
                    this.frameRate = frameRate;
                    this.targetFrameInterval = 1000.0 / frameRate; // Calculate target frame interval
                    
                    // Reset frame timing collection using unified timing logic
                    frameTimes.Clear();
                    recordingStartTime = UnifiedFrameTimingManager.InitializeRecordingStartTime();
                    recordingFrameCounter = 0; // Reset frame counter for this recording session
                    lastFrameWriteTime = DateTime.MinValue;
                    detectedFrameRate = frameRate; // Start with configured rate
                    frameRateDetected = false;

                    // Build FFmpeg command to accept RGBA frames from stdin
                    string ffmpegArgs = BuildRecordingCommand(outputPath, frameWidth, frameHeight, frameRate);
                    
                    Tools.Logger.VideoLog.LogCall(this, $"Starting RGBA recording: {ffmpegArgs}");

                    var processStartInfo = ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
                    processStartInfo.RedirectStandardInput = true;
                    processStartInfo.UseShellExecute = false;
                    
                    recordingProcess = new Process();
                    recordingProcess.StartInfo = processStartInfo;
                    
                    // Set up error monitoring
                    recordingProcess.ErrorDataReceived += RecordingProcess_ErrorDataReceived;
                    recordingProcess.Exited += RecordingProcess_Exited;
                    recordingProcess.EnableRaisingEvents = true;

                    if (recordingProcess.Start())
                    {
                        recordingProcess.BeginErrorReadLine();
                        isRecording = true;
                        
                        Tools.Logger.VideoLog.LogCall(this, $"RGBA recording started successfully - PID: {recordingProcess.Id}, Output: {outputPath}");
                        RecordingStarted?.Invoke(outputPath);
                        
                        return true;
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "Failed to start RGBA recording process");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    Tools.Logger.VideoLog.LogCall(this, $"Exception while starting RGBA recording: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Write an RGBA frame to the recording ffmpeg process with precise timing control
        /// </summary>
        /// <param name="rgbaData">RGBA frame data</param>
        /// <param name="frameNumber">Frame number for XML timing</param>
        /// <returns>True if frame was written successfully</returns>
        public bool WriteFrame(byte[] rgbaData, int frameNumber)
        {
            lock (recordingLock)
            {
                if (!isRecording || recordingProcess == null || recordingProcess.HasExited)
                {
                    return false;
                }

                try
                {
                    var currentTime = UnifiedFrameTimingManager.GetHighPrecisionTimestamp();
                    
                    // Track actual frame timing for debugging and verification
                    if (recordingFrameCounter % 100 == 0) // Log every 100 frames to see timing precision
                    {
                        double intervalMs = lastFrameWriteTime != DateTime.MinValue ? (currentTime - lastFrameWriteTime).TotalMilliseconds : 0;
                        double actualFps = intervalMs > 0 ? (1000.0 / intervalMs) * 100 : 0; // 100 frames over the interval
                        double totalSeconds = (currentTime - recordingStartTime).TotalSeconds;
                        double avgFps = recordingFrameCounter > 0 ? recordingFrameCounter / totalSeconds : 0;
                        
                        Tools.Logger.VideoLog.LogCall(this, $"RECORDING TIMING: Frame {recordingFrameCounter}, Recent: {actualFps:F3}fps, Average: {avgFps:F3}fps, PerFrame: {intervalMs/100:F2}ms (wallclock-driven)");
                        lastFrameWriteTime = currentTime;
                    }
                    
                    // Write RGBA frame data to ffmpeg stdin with timing control
                    // The -use_wallclock_as_timestamps flag will use the real-time arrival of frames
                    // for PTS calculation, ensuring proper timing in the output video
                    recordingProcess.StandardInput.BaseStream.Write(rgbaData, 0, rgbaData.Length);
                    recordingProcess.StandardInput.BaseStream.Flush();

                    // Collect frame timing for XML file
                    recordingFrameCounter++; // Increment our internal frame counter (starts from 1)
                    
                    // Use unified frame timing logic for consistency across platforms
                    var frameTime = UnifiedFrameTimingManager.CreateFrameTime(
                        recordingFrameCounter, currentTime, recordingStartTime);
                    frameTimes.Add(frameTime);

                    return true;
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    Tools.Logger.VideoLog.LogCall(this, $"Error writing RGBA frame: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Stop the current recording
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds to wait for graceful shutdown</param>
        /// <returns>True if stopped successfully</returns>
        public bool StopRecording(int timeoutMs = 10000)
        {
            lock (recordingLock)
            {
                if (!isRecording || recordingProcess == null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "No recording in progress to stop");
                    return true;
                }

                try
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Stopping RGBA recording - PID: {recordingProcess.Id}");

                    bool success = false;
                    string outputPath = currentOutputPath;

                    if (!recordingProcess.HasExited)
                    {
                        // Close stdin to signal end of input to FFmpeg
                        try
                        {
                            recordingProcess.StandardInput.BaseStream.Close();
                            recordingProcess.StandardInput.Close();
                        }
                        catch (Exception ex)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"Could not close stdin to FFmpeg: {ex.Message}");
                        }

                        // Wait for graceful exit
                        if (recordingProcess.WaitForExit(timeoutMs))
                        {
                            success = recordingProcess.ExitCode == 0;
                            Tools.Logger.VideoLog.LogCall(this, $"RGBA recording stopped gracefully - Exit code: {recordingProcess.ExitCode}");
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, "RGBA recording did not stop gracefully, force killing");
                            recordingProcess.Kill();
                            recordingProcess.WaitForExit(5000);
                            success = false;
                        }
                    }
                    else
                    {
                        success = recordingProcess.ExitCode == 0;
                        Tools.Logger.VideoLog.LogCall(this, $"RGBA recording process already exited - Exit code: {recordingProcess.ExitCode}");
                    }

                    // Verify output file was created and has content
                    if (success && File.Exists(outputPath))
                    {
                        var fileInfo = new FileInfo(outputPath);
                        if (fileInfo.Length > 0)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"RGBA recording completed successfully - File size: {fileInfo.Length} bytes");
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, "RGBA recording file is empty, marking as failed");
                            success = false;
                        }
                    }
                    else if (success)
                    {
                        Tools.Logger.VideoLog.LogCall(this, "RGBA recording file does not exist, marking as failed");
                        success = false;
                    }

                    isRecording = false;
                    
                    // Generate XML metadata file from the camera loop timing data
                    // This ensures the XML metadata is generated with camera-native timing
                    if (success)
                    {
                        GenerateRecordInfoFile(outputPath);
                        
                        // Add a small delay to ensure file system has time to register the new file
                        // This prevents race conditions where the UI checks for recordings before
                        // the .recordinfo.xml file is fully written to disk
                        Task.Delay(100).Wait();
                    }
                    
                    RecordingStopped?.Invoke(outputPath, success);
                    
                    return success;
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    Tools.Logger.VideoLog.LogCall(this, $"Exception while stopping RGBA recording: {ex.Message}");
                    isRecording = false;
                    RecordingStopped?.Invoke(currentOutputPath, false);
                    return false;
                }
                finally
                {
                    CleanupRecordingProcess();
                }
            }
        }

        private string BuildRecordingCommand(string outputPath, int frameWidth, int frameHeight, float frameRate)
        {
            // FFmpeg command to read RGBA frames from stdin preserving source timing
            // Use wallclock timestamps to ensure recording framerate matches source exactly
            
            int gop = Math.Max(1, (int)Math.Round(frameRate * 0.1f)); // 0.1s GOP at the configured/measured fps
            
            string ffmpegArgs = $"-f rawvideo " +                          // Input format: raw video
                               $"-pix_fmt rgba " +                         // Pixel format: RGBA  
                               $"-s {frameWidth}x{frameHeight} " +         // Frame size
                               $"-use_wallclock_as_timestamps 1 " +       // Use wallclock time for PTS (critical for timing accuracy)
                               $"-fflags +genpts " +                       // Generate presentation timestamps
                               $"-i pipe:0 " +                             // Input from stdin
                               $"-c:v libx264 " +                          // H264 codec
                               $"-preset medium " +                        // Balanced preset for quality
                               $"-crf 18 " +                               // Higher quality
                               $"-g {gop} -keyint_min {gop} " +            // Tighter keyframe interval to improve seeking
                               $"-force_key_frames \"expr:gte(t,n_forced*0.1)\" " + // Keyframe at least every 0.1s
                               $"-pix_fmt yuv420p " +                      // Output pixel format
                               $"-fps_mode passthrough " +                 // Preserve original frame timing (VFR)
                               $"-video_track_timescale 90000 " +          // Standard video timescale for precise timing
                               $"-avoid_negative_ts make_zero " +          // Handle timing issues
                               $"-movflags +faststart " +                  // Optimize for streaming
                               $"-y " +                                    // Overwrite output file
                               $"\"{outputPath}\"";

            Tools.Logger.VideoLog.LogCall(this, $"RGBA Recording ffmpeg command (preserving source timing - no framerate forcing): {ffmpegArgs}");
            return ffmpegArgs;
        }

        private void RecordingProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                Tools.Logger.VideoLog.LogCall(this, $"RGBA Recording FFmpeg: {e.Data}");
                
                // Log any errors or warnings
                if (e.Data.Contains("error") || e.Data.Contains("Error") || 
                    e.Data.Contains("warning") || e.Data.Contains("Warning"))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"RGBA Recording Issue: {e.Data}");
                }
            }
        }

        private void RecordingProcess_Exited(object sender, EventArgs e)
        {
            lock (recordingLock)
            {
                if (isRecording)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"RGBA recording process exited unexpectedly - Exit code: {recordingProcess?.ExitCode}");
                    
                    bool success = recordingProcess?.ExitCode == 0;
                    string outputPath = currentOutputPath;
                    
                    isRecording = false;
                    RecordingStopped?.Invoke(outputPath, success);
                }
            }
        }

        private void CleanupRecordingProcess()
        {
            try
            {
                if (recordingProcess != null)
                {
                    recordingProcess.ErrorDataReceived -= RecordingProcess_ErrorDataReceived;
                    recordingProcess.Exited -= RecordingProcess_Exited;
                    
                    if (!recordingProcess.HasExited)
                    {
                        recordingProcess.Kill();
                    }
                    
                    recordingProcess.Dispose();
                    recordingProcess = null;
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
        }

        /// <summary>
        /// Generate .recordinfo.xml file for the recorded video using collected frame timing data
        /// </summary>
        /// <param name="videoFilePath">Path to the recorded video file</param>
        private void GenerateRecordInfoFile(string videoFilePath)
        {
            try
            {
                if (frameSourceForRecordInfo == null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "No frame source available for .recordinfo.xml generation");
                    return;
                }

                // Create RecodingInfo using collected frame timing data
                var recordingInfo = new RecodingInfo(frameSourceForRecordInfo);
                
                // Override frame times with our collected data
                recordingInfo.FrameTimes = frameTimes.ToArray();
                
                // Use relative path for the recording info
                recordingInfo.FilePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), videoFilePath);
                
                // Generate .recordinfo.xml filename alongside the video file
                string basePath = videoFilePath;
                if (basePath.EndsWith(".mp4"))
                {
                    basePath = basePath.Replace(".mp4", "");
                }
                else if (basePath.EndsWith(".mkv"))
                {
                    basePath = basePath.Replace(".mkv", "");
                }
                
                FileInfo recordInfoFile = new FileInfo(basePath + ".recordinfo.xml");
                
                // Write the .recordinfo.xml file
                IOTools.Write(recordInfoFile.Directory.FullName, recordInfoFile.Name, recordingInfo);
                
                // Verify the file was written successfully
                if (recordInfoFile.Exists)
                {
                    var fileInfo = new FileInfo(recordInfoFile.FullName);
                    Tools.Logger.VideoLog.LogCall(this, $"Generated .recordinfo.xml file: {recordInfoFile.FullName} ({fileInfo.Length} bytes)");
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"WARNING: .recordinfo.xml file was not created: {recordInfoFile.FullName}");
                }
                
                Tools.Logger.VideoLog.LogCall(this, $"Frame times count: {frameTimes.Count}");
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, $"Failed to generate .recordinfo.xml file for {videoFilePath}: {ex.Message}");
            }
        }


        public void Dispose()
        {
            if (isRecording)
            {
                StopRecording();
            }
            
            CleanupRecordingProcess();
        }
    }
}