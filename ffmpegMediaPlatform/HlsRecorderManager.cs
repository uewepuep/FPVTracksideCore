using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tools;
using ImageServer;

namespace FfmpegMediaPlatform
{
    /// <summary>
    /// Manages recording from HLS streams to MP4 files.
    /// This allows starting and stopping recordings independently of the live stream.
    /// </summary>
    public class HlsRecorderManager : IDisposable
    {
        private readonly FfmpegMediaFramework ffmpegMediaFramework;
        private Process recordingProcess;
        private bool isRecording;
        private string currentOutputPath;
        private readonly object recordingLock = new object();
        private System.Threading.Timer delayedStopTimer;
        private bool delayedStopScheduled;
        private ICaptureFrameSource frameSourceForRecordInfo;

        public bool IsRecording => isRecording;
        public string CurrentOutputPath => currentOutputPath;

        public event Action<string> RecordingStarted;
        public event Action<string, bool> RecordingStopped; // path, success

        public HlsRecorderManager(FfmpegMediaFramework ffmpegMediaFramework)
        {
            this.ffmpegMediaFramework = ffmpegMediaFramework ?? throw new ArgumentNullException(nameof(ffmpegMediaFramework));
        }

        /// <summary>
        /// Start recording from an HLS stream to an MP4 file
        /// </summary>
        /// <param name="hlsStreamUrl">The HLS stream URL (e.g., http://localhost:8000/hls/stream.m3u8)</param>
        /// <param name="outputPath">The output MP4 file path</param>
        /// <param name="maxDurationSeconds">Maximum recording duration in seconds (0 = no limit)</param>
        /// <param name="captureFrameSource">The frame source to use for .recordinfo.xml generation (optional)</param>
        /// <returns>True if recording started successfully</returns>
        public bool StartRecording(string hlsStreamUrl, string outputPath, int maxDurationSeconds = 0, ICaptureFrameSource captureFrameSource = null)
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

                    // Check if HLS stream is available before starting recording
                    if (!WaitForHlsStream(hlsStreamUrl, timeoutSeconds: 5))
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"HLS stream not available at {hlsStreamUrl} - cannot start recording");
                        return false;
                    }

                    currentOutputPath = outputPath;
                    frameSourceForRecordInfo = captureFrameSource;

                    // Build FFmpeg command to record from HLS stream
                    string ffmpegArgs = BuildRecordingCommand(hlsStreamUrl, outputPath, maxDurationSeconds);
                    
                    Tools.Logger.VideoLog.LogCall(this, $"Starting HLS recording: {ffmpegArgs}");

                    var processStartInfo = ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
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
                        
                        Tools.Logger.VideoLog.LogCall(this, $"HLS recording started successfully - PID: {recordingProcess.Id}, Output: {outputPath}");
                        RecordingStarted?.Invoke(outputPath);
                        
                        return true;
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "Failed to start HLS recording process");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    Tools.Logger.VideoLog.LogCall(this, $"Exception while starting HLS recording: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Stop the current recording with optional buffer delay to capture remaining HLS content
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds to wait for graceful shutdown</param>
        /// <param name="bufferDelaySeconds">Additional seconds to record to capture HLS stream buffer (default: 0 seconds for immediate stop)</param>
        /// <returns>True if stopped successfully</returns>
        public bool StopRecording(int timeoutMs = 10000, int bufferDelaySeconds = 0)
        {
            if (bufferDelaySeconds > 0)
            {
                // Use delayed stop to capture remaining HLS buffer content
                return StopRecordingWithDelay(bufferDelaySeconds, timeoutMs);
            }
            else
            {
                // Immediate stop
                return StopRecordingImmediate(timeoutMs);
            }
        }

        /// <summary>
        /// Stop recording immediately without buffer delay
        /// </summary>
        private bool StopRecordingImmediate(int timeoutMs = 10000)
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
                    Tools.Logger.VideoLog.LogCall(this, $"Stopping HLS recording immediately - PID: {recordingProcess.Id}");

                    bool success = false;
                    string outputPath = currentOutputPath;

                    if (!recordingProcess.HasExited)
                    {
                        // Send 'q' command to FFmpeg for graceful shutdown
                        try
                        {
                            recordingProcess.StandardInput.WriteLine("q");
                            recordingProcess.StandardInput.Flush();
                        }
                        catch (Exception ex)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"Could not send quit command to FFmpeg: {ex.Message}");
                        }

                        // Wait for graceful exit
                        if (recordingProcess.WaitForExit(timeoutMs))
                        {
                            success = recordingProcess.ExitCode == 0;
                            Tools.Logger.VideoLog.LogCall(this, $"HLS recording stopped gracefully - Exit code: {recordingProcess.ExitCode}");
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, "HLS recording did not stop gracefully, force killing");
                            recordingProcess.Kill();
                            recordingProcess.WaitForExit(5000);
                            success = false;
                        }
                    }
                    else
                    {
                        success = recordingProcess.ExitCode == 0;
                        Tools.Logger.VideoLog.LogCall(this, $"HLS recording process already exited - Exit code: {recordingProcess.ExitCode}");
                    }

                    // Verify output file was created and has content
                    if (success && File.Exists(outputPath))
                    {
                        var fileInfo = new FileInfo(outputPath);
                        if (fileInfo.Length > 0)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"HLS recording completed successfully - File size: {fileInfo.Length} bytes");
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, "HLS recording file is empty, marking as failed");
                            success = false;
                        }
                    }
                    else if (success)
                    {
                        Tools.Logger.VideoLog.LogCall(this, "HLS recording file does not exist, marking as failed");
                        success = false;
                    }

                    isRecording = false;
                    delayedStopScheduled = false; // Reset the delayed stop flag
                    
                    // Generate .recordinfo.xml file if we have a frame source
                    if (success && frameSourceForRecordInfo != null)
                    {
                        GenerateRecordInfoFile(outputPath);
                    }
                    
                    RecordingStopped?.Invoke(outputPath, success);
                    
                    return success;
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    Tools.Logger.VideoLog.LogCall(this, $"Exception while stopping HLS recording: {ex.Message}");
                    isRecording = false;
                    delayedStopScheduled = false; // Reset the delayed stop flag
                    RecordingStopped?.Invoke(currentOutputPath, false);
                    return false;
                }
                finally
                {
                    CleanupRecordingProcess();
                }
            }
        }

        /// <summary>
        /// Stop recording with a delay to capture remaining HLS buffer content
        /// </summary>
        private bool StopRecordingWithDelay(int bufferDelaySeconds, int timeoutMs)
        {
            lock (recordingLock)
            {
                if (!isRecording || recordingProcess == null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "No recording in progress to stop");
                    return true;
                }

                // Check if delayed stop is already scheduled
                if (delayedStopScheduled)
                {
                    Tools.Logger.VideoLog.LogCall(this, "Delayed stop already scheduled - ignoring duplicate request");
                    return true;
                }

                Tools.Logger.VideoLog.LogCall(this, $"Scheduling delayed stop in {bufferDelaySeconds} seconds to capture HLS buffer - PID: {recordingProcess.Id}");
                
                // Cancel any existing delayed stop timer
                delayedStopTimer?.Dispose();
                delayedStopScheduled = true;
                
                // Create a timer to stop recording after the buffer delay
                delayedStopTimer = new System.Threading.Timer(
                    callback: _ => {
                        try
                        {
                            Tools.Logger.VideoLog.LogCall(this, "Executing delayed HLS recording stop");
                            StopRecordingImmediate(timeoutMs);
                        }
                        catch (Exception ex)
                        {
                            Tools.Logger.VideoLog.LogException(this, ex);
                            Tools.Logger.VideoLog.LogCall(this, $"Exception during delayed stop: {ex.Message}");
                        }
                        finally
                        {
                            lock (recordingLock)
                            {
                                delayedStopTimer?.Dispose();
                                delayedStopTimer = null;
                                delayedStopScheduled = false;
                            }
                        }
                    },
                    state: null,
                    dueTime: TimeSpan.FromSeconds(bufferDelaySeconds),
                    period: Timeout.InfiniteTimeSpan
                );

                Tools.Logger.VideoLog.LogCall(this, $"HLS recording will stop in {bufferDelaySeconds} seconds to capture remaining buffer content");
                return true; // Return true immediately - the actual stop happens later
            }
        }

        /// <summary>
        /// Wait for HLS stream to become available before starting recording
        /// </summary>
        private bool WaitForHlsStream(string hlsStreamUrl, int timeoutSeconds)
        {
            try
            {
                Tools.Logger.VideoLog.LogCall(this, $"Waiting up to {timeoutSeconds} seconds for HLS stream at {hlsStreamUrl}");
                
                var timeout = DateTime.Now.AddSeconds(timeoutSeconds);
                
                while (DateTime.Now < timeout)
                {
                    try
                    {
                        using (var httpClient = new System.Net.Http.HttpClient())
                        {
                            httpClient.Timeout = TimeSpan.FromSeconds(2);
                            var response = httpClient.GetAsync(hlsStreamUrl).Result;
                            
                            if (response.IsSuccessStatusCode)
                            {
                                var content = response.Content.ReadAsStringAsync().Result;
                                // Basic validation that this looks like an M3U8 playlist
                                if (content.Contains("#EXTM3U") || content.Contains("#EXT-X-VERSION"))
                                {
                                    Tools.Logger.VideoLog.LogCall(this, "HLS stream is available and valid");
                                    return true;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore individual request failures, keep trying
                    }
                    
                    System.Threading.Thread.Sleep(500); // Wait 500ms between attempts
                }
                
                Tools.Logger.VideoLog.LogCall(this, $"Timeout waiting for HLS stream after {timeoutSeconds} seconds");
                return false;
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, $"Exception while waiting for HLS stream: {ex.Message}");
                return false;
            }
        }

        private string BuildRecordingCommand(string hlsStreamUrl, string outputPath, int maxDurationSeconds)
        {
            string durationArg = maxDurationSeconds > 0 ? $"-t {maxDurationSeconds} " : "";
            
            // Ultra-low latency recording settings
            string ffmpegArgs = $"-fflags nobuffer " +  // Disable input buffering
                               $"-flags low_delay " +  // Low delay mode
                               $"-probesize 32 " +  // Minimal probe size
                               $"-analyzeduration 0 " +  // Skip analysis for faster start
                               $"-i \"{hlsStreamUrl}\" " +
                               $"{durationArg}" +
                               $"-c copy " +  // Copy streams without re-encoding
                               $"-avoid_negative_ts make_zero " +
                               $"-bsf:a aac_adtstoasc " +  // Fix AAC if present
                               $"-movflags +faststart " +  // Optimize for streaming/quick playback
                               $"-flush_packets 1 " +  // Flush packets immediately
                               $"-y " +  // Overwrite output file
                               $"\"{outputPath}\"";

            return ffmpegArgs;
        }

        private void RecordingProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                Tools.Logger.VideoLog.LogCall(this, $"HLS Recording FFmpeg: {e.Data}");
                
                // Log any errors or warnings
                if (e.Data.Contains("error") || e.Data.Contains("Error") || 
                    e.Data.Contains("warning") || e.Data.Contains("Warning"))
                {
                    Tools.Logger.VideoLog.LogCall(this, $"HLS Recording Issue: {e.Data}");
                }
            }
        }

        private void RecordingProcess_Exited(object sender, EventArgs e)
        {
            lock (recordingLock)
            {
                if (isRecording)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"HLS recording process exited unexpectedly - Exit code: {recordingProcess?.ExitCode}");
                    
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
        /// Generate .recordinfo.xml file for the recorded video to enable replay functionality
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

                // Create RecodingInfo from the frame source
                var recordingInfo = new RecodingInfo(frameSourceForRecordInfo);
                
                // Use relative path for the recording info
                recordingInfo.FilePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), videoFilePath);
                
                // Generate .recordinfo.xml filename alongside the video file
                string basePath = videoFilePath;
                if (basePath.EndsWith(".mp4"))
                {
                    basePath = basePath.Replace(".mp4", "");
                }
                
                FileInfo recordInfoFile = new FileInfo(basePath + ".recordinfo.xml");
                
                // Write the .recordinfo.xml file
                IOTools.Write(recordInfoFile.Directory.FullName, recordInfoFile.Name, recordingInfo);
                
                Tools.Logger.VideoLog.LogCall(this, $"Generated .recordinfo.xml file: {recordInfoFile.FullName}");
                Tools.Logger.VideoLog.LogCall(this, $"Frame times count: {recordingInfo.FrameTimes?.Length ?? 0}");
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
                StopRecording(bufferDelaySeconds: 0); // Immediate stop during disposal
            }
            
            // Clean up the delayed stop timer
            delayedStopTimer?.Dispose();
            delayedStopTimer = null;
            delayedStopScheduled = false;
            
            CleanupRecordingProcess();
        }
    }
}