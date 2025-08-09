using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Tools;
using ImageServer;

namespace FfmpegMediaPlatform
{
    /// <summary>
    /// Dual-stream video capture manager implementing the drone racing specification.
    /// Maintains an always-on capture process that outputs both RGBA and H.264 streams.
    /// Supports race-triggered recording without interrupting the live feed.
    /// </summary>
    public class DualStreamCaptureManager : IDisposable
    {
        private readonly FfmpegMediaFramework ffmpegMediaFramework;
        private readonly VideoConfig videoConfig;
        
        // Always-on capture process
        private Process captureProcess;
        private bool isCapturing;
        private readonly object captureLock = new object();
        
        // Named pipes for dual stream communication
        private string rgbaPipePath;
        private string h264PipePath;
        
        // Recording process (started/stopped with races)
        private Process recordingProcess;
        private bool isRecording;
        private string currentRecordingPath;
        private readonly object recordingLock = new object();
        
        // Platform-specific pipe handling
        private readonly bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        
        public bool IsCapturing => isCapturing;
        public bool IsRecording => isRecording;
        public string CurrentRecordingPath => currentRecordingPath;
        
        public event Action<string> RecordingStarted;
        public event Action<string, bool> RecordingStopped;
        public event Action<string> CaptureStarted;
        public event Action<string> CaptureStopped;

        public DualStreamCaptureManager(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig)
        {
            this.ffmpegMediaFramework = ffmpegMediaFramework ?? throw new ArgumentNullException(nameof(ffmpegMediaFramework));
            this.videoConfig = videoConfig ?? throw new ArgumentNullException(nameof(videoConfig));
            
            InitializePipePaths();
        }

        private void InitializePipePaths()
        {
            if (isWindows)
            {
                // Windows named pipes
                rgbaPipePath = $"\\\\.\\pipe\\fpv_rgba_{Guid.NewGuid():N}";
                h264PipePath = $"\\\\.\\pipe\\fpv_h264_{Guid.NewGuid():N}";
            }
            else
            {
                // Unix named pipes (macOS/Linux)
                string tempDir = Path.GetTempPath();
                rgbaPipePath = Path.Combine(tempDir, $"fpv_rgba_{Guid.NewGuid():N}");
                h264PipePath = Path.Combine(tempDir, $"fpv_h264_{Guid.NewGuid():N}");
            }
        }

        /// <summary>
        /// Start the always-on capture process that outputs dual streams
        /// </summary>
        public bool StartCapture()
        {
            lock (captureLock)
            {
                if (isCapturing)
                {
                    Tools.Logger.VideoLog.LogCall(this, "Capture already running");
                    return true;
                }

                try
                {
                    CreateNamedPipes();
                    
                    string ffmpegArgs = BuildCaptureCommand();
                    Tools.Logger.VideoLog.LogCall(this, $"Starting dual-stream capture: {ffmpegArgs}");

                    var processStartInfo = ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
                    processStartInfo.RedirectStandardError = true;
                    processStartInfo.UseShellExecute = false;
                    
                    captureProcess = new Process();
                    captureProcess.StartInfo = processStartInfo;
                    captureProcess.ErrorDataReceived += CaptureProcess_ErrorDataReceived;
                    captureProcess.Exited += CaptureProcess_Exited;
                    captureProcess.EnableRaisingEvents = true;

                    if (captureProcess.Start())
                    {
                        captureProcess.BeginErrorReadLine();
                        isCapturing = true;
                        
                        Tools.Logger.VideoLog.LogCall(this, $"Dual-stream capture started - PID: {captureProcess.Id}");
                        CaptureStarted?.Invoke($"PID: {captureProcess.Id}");
                        
                        return true;
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "Failed to start capture process");
                        CleanupNamedPipes();
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    CleanupNamedPipes();
                    return false;
                }
            }
        }

        /// <summary>
        /// Stop the always-on capture process
        /// </summary>
        public bool StopCapture(int timeoutMs = 10000)
        {
            lock (captureLock)
            {
                if (!isCapturing || captureProcess == null)
                {
                    Tools.Logger.VideoLog.LogCall(this, "No capture in progress to stop");
                    return true;
                }

                try
                {
                    // Stop recording first if running
                    if (isRecording)
                    {
                        StopRecording();
                    }

                    bool success = false;
                    if (!captureProcess.HasExited)
                    {
                        // Send quit command to FFmpeg
                        try
                        {
                            captureProcess.StandardInput?.WriteLine("q");
                            captureProcess.StandardInput?.Flush();
                        }
                        catch { }

                        if (captureProcess.WaitForExit(timeoutMs))
                        {
                            success = captureProcess.ExitCode == 0;
                        }
                        else
                        {
                            captureProcess.Kill();
                            captureProcess.WaitForExit(5000);
                        }
                    }

                    isCapturing = false;
                    CaptureStopped?.Invoke($"Exit code: {captureProcess?.ExitCode}");
                    
                    return success;
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    return false;
                }
                finally
                {
                    CleanupCaptureProcess();
                    CleanupNamedPipes();
                }
            }
        }

        /// <summary>
        /// Start recording H.264 stream to file (race triggered)
        /// </summary>
        public bool StartRecording(string outputPath)
        {
            lock (recordingLock)
            {
                if (isRecording)
                {
                    Tools.Logger.VideoLog.LogCall(this, "Recording already in progress");
                    return false;
                }

                if (!isCapturing)
                {
                    Tools.Logger.VideoLog.LogCall(this, "Cannot start recording - capture not running");
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

                    currentRecordingPath = outputPath;
                    
                    string ffmpegArgs = BuildRecordingCommand(outputPath);
                    Tools.Logger.VideoLog.LogCall(this, $"Starting H.264 recording: {ffmpegArgs}");

                    var processStartInfo = ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
                    processStartInfo.RedirectStandardError = true;
                    processStartInfo.UseShellExecute = false;
                    
                    recordingProcess = new Process();
                    recordingProcess.StartInfo = processStartInfo;
                    recordingProcess.ErrorDataReceived += RecordingProcess_ErrorDataReceived;
                    recordingProcess.Exited += RecordingProcess_Exited;
                    recordingProcess.EnableRaisingEvents = true;

                    if (recordingProcess.Start())
                    {
                        recordingProcess.BeginErrorReadLine();
                        isRecording = true;
                        
                        Tools.Logger.VideoLog.LogCall(this, $"H.264 recording started - PID: {recordingProcess.Id}, Output: {outputPath}");
                        RecordingStarted?.Invoke(outputPath);
                        
                        return true;
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, "Failed to start recording process");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    return false;
                }
            }
        }

        /// <summary>
        /// Stop the H.264 recording (race triggered)
        /// </summary>
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
                    bool success = false;
                    string outputPath = currentRecordingPath;

                    if (!recordingProcess.HasExited)
                    {
                        // Send quit command to FFmpeg
                        try
                        {
                            recordingProcess.StandardInput?.WriteLine("q");
                            recordingProcess.StandardInput?.Flush();
                        }
                        catch { }

                        if (recordingProcess.WaitForExit(timeoutMs))
                        {
                            success = recordingProcess.ExitCode == 0;
                        }
                        else
                        {
                            recordingProcess.Kill();
                            recordingProcess.WaitForExit(5000);
                        }
                    }
                    else
                    {
                        success = recordingProcess.ExitCode == 0;
                    }

                    // Verify output file was created
                    if (success && File.Exists(outputPath))
                    {
                        var fileInfo = new FileInfo(outputPath);
                        success = fileInfo.Length > 0;
                        Tools.Logger.VideoLog.LogCall(this, $"Recording file size: {fileInfo.Length} bytes");
                    }

                    isRecording = false;
                    RecordingStopped?.Invoke(outputPath, success);
                    
                    return success;
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    isRecording = false;
                    RecordingStopped?.Invoke(currentRecordingPath, false);
                    return false;
                }
                finally
                {
                    CleanupRecordingProcess();
                }
            }
        }

        /// <summary>
        /// Get RGBA stream path for interface consumption
        /// </summary>
        public string GetRgbaStreamPath()
        {
            return rgbaPipePath;
        }

        private string BuildCaptureCommand()
        {
            string args;
            string hardwareEncoder = GetHardwareEncoder();
            
            // Use 0.1s GOP for better seeking
            int gop = Math.Max(1, (int)Math.Round(videoConfig.VideoMode.FrameRate * 0.1f));
            string gopArgs = $"-g {gop} -keyint_min {gop} -force_key_frames \"expr:gte(t,n_forced*0.1)\" ";
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS with AVFoundation
                args = $"-f avfoundation " +
                       $"-framerate {videoConfig.VideoMode.FrameRate} " +
                       $"-video_size {videoConfig.VideoMode.Width}x{videoConfig.VideoMode.Height} " +
                       $"-i \"{videoConfig.ffmpegId}\" " +
                       $"-fflags nobuffer -flags low_delay " +
                       $"-vsync passthrough -copyts " +
                       $"-filter_complex \"[0:v]split=2[out1][out2]\" " +
                       $"-map \"[out1]\" -pix_fmt rgba -f rawvideo \"{rgbaPipePath}\" " +
                       $"-map \"[out2]\" -c:v {hardwareEncoder} -preset ultrafast -tune zerolatency {gopArgs}-f mpegts \"{h264PipePath}\"";
            }
            else
            {
                // Windows with DirectShow
                args = $"-f dshow " +
                       $"-framerate {videoConfig.VideoMode.FrameRate} " +
                       $"-video_size {videoConfig.VideoMode.Width}x{videoConfig.VideoMode.Height} " +
                       $"-i video=\"{videoConfig.DeviceName}\" " +
                       $"-fflags nobuffer -flags low_delay " +
                       $"-vsync passthrough -copyts " +
                       $"-filter_complex \"[0:v]split=2[out1][out2]\" " +
                       $"-map \"[out1]\" -pix_fmt rgba -f rawvideo \"{rgbaPipePath}\" " +
                       $"-map \"[out2]\" -c:v {hardwareEncoder} -preset llhp -tune zerolatency {gopArgs}-f mpegts \"{h264PipePath}\"";
            }

            return args;
        }

        private string BuildRecordingCommand(string outputPath)
        {
            bool useMatroska = outputPath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase);
            string containerFormat = useMatroska ? "matroska" : "mp4";
            
            // Recording process reads from H.264 pipe and remuxes to seekable file
            return $"-i \"{h264PipePath}\" " +
                   $"-copyts -vsync passthrough -avoid_negative_ts make_zero " +
                   $"-c copy " +
                   $"-f {containerFormat} " +
                   $"-y \"{outputPath}\"";
        }

        private string GetHardwareEncoder()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "h264_videotoolbox";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Try to detect the best available encoder
                // In practice, you might want to probe for available encoders
                return "h264_nvenc"; // Default to NVIDIA, could fallback to h264_qsv or libx264
            }
            else
            {
                return "libx264"; // Fallback for Linux
            }
        }

        private void CreateNamedPipes()
        {
            if (!isWindows)
            {
                // Create Unix named pipes (FIFOs)
                CreateUnixNamedPipe(rgbaPipePath);
                CreateUnixNamedPipe(h264PipePath);
            }
            // Windows named pipes are created automatically by FFmpeg
        }

        private void CreateUnixNamedPipe(string pipePath)
        {
            try
            {
                if (File.Exists(pipePath))
                {
                    File.Delete(pipePath);
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "mkfifo",
                        Arguments = $"\"{pipePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Failed to create named pipe: {pipePath}");
                }

                Tools.Logger.VideoLog.LogCall(this, $"Created named pipe: {pipePath}");
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                throw;
            }
        }

        private void CleanupNamedPipes()
        {
            if (!isWindows)
            {
                try
                {
                    if (File.Exists(rgbaPipePath))
                    {
                        File.Delete(rgbaPipePath);
                    }
                    if (File.Exists(h264PipePath))
                    {
                        File.Delete(h264PipePath);
                    }
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                }
            }
        }

        private void CaptureProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                Tools.Logger.VideoLog.LogCall(this, $"Capture FFmpeg: {e.Data}");
            }
        }

        private void CaptureProcess_Exited(object sender, EventArgs e)
        {
            lock (captureLock)
            {
                if (isCapturing)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Capture process exited unexpectedly - Exit code: {captureProcess?.ExitCode}");
                    isCapturing = false;
                    CaptureStopped?.Invoke($"Unexpected exit: {captureProcess?.ExitCode}");
                }
            }
        }

        private void RecordingProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                Tools.Logger.VideoLog.LogCall(this, $"Recording FFmpeg: {e.Data}");
            }
        }

        private void RecordingProcess_Exited(object sender, EventArgs e)
        {
            lock (recordingLock)
            {
                if (isRecording)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Recording process exited unexpectedly - Exit code: {recordingProcess?.ExitCode}");
                    bool success = recordingProcess?.ExitCode == 0;
                    isRecording = false;
                    RecordingStopped?.Invoke(currentRecordingPath, success);
                }
            }
        }

        private void CleanupCaptureProcess()
        {
            try
            {
                if (captureProcess != null)
                {
                    captureProcess.ErrorDataReceived -= CaptureProcess_ErrorDataReceived;
                    captureProcess.Exited -= CaptureProcess_Exited;
                    
                    if (!captureProcess.HasExited)
                    {
                        captureProcess.Kill();
                    }
                    
                    captureProcess.Dispose();
                    captureProcess = null;
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
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

        public void Dispose()
        {
            StopCapture();
        }
    }
}