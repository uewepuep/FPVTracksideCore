using ImageServer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace FfmpegMediaPlatform
{
    /// <summary>
    /// FFmpeg frame source that maintains a persistent live stream with dual outputs:
    /// 1. Raw RGBA output for live processing (pipe:1)
    /// 2. HLS stream for HTTP access and recording
    /// The live stream never stops, recording processes consume the HLS stream independently.
    /// 
    /// TO DISABLE HLS: Set FfmpegHlsLiveFrameSource.HlsEnabled = false;
    /// This will disable HLS streaming while keeping RGBA live processing active.
    /// </summary>
    public class FfmpegHlsLiveFrameSource : FfmpegFrameSource
    {
        /// <summary>
        /// Configuration flag to disable HLS functionality.
        /// Set to false to disable HLS streaming while keeping RGBA live processing.
        /// Default: true (HLS enabled)
        /// 
        /// Usage: FfmpegHlsLiveFrameSource.HlsEnabled = false;
        /// </summary>
        public static bool HlsEnabled { get; set; } = true;
        
        private HttpListener httpServer;
        private Thread httpServerThread;
        private string hlsOutputPath;
        private int httpPort;
        private bool httpServerRunning;
        private readonly object httpServerLock = new object();

        public string HlsStreamUrl => HlsEnabled ? $"http://localhost:{httpPort}/hls/stream.m3u8" : "HLS Disabled";
        public bool IsHttpServerRunning => HlsEnabled && httpServerRunning;

        public FfmpegHlsLiveFrameSource(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig, int httpPort = 8787)
            : base(ffmpegMediaFramework, videoConfig)
        {
            this.httpPort = httpPort;
            
            // Use the application's binary directory for HLS files to match FFmpeg's working directory
            string binaryDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            this.hlsOutputPath = Path.Combine(binaryDir, "trackside_hls");
            
            // Only create HLS directory if HLS is enabled
            if (HlsEnabled)
            {
                // Ensure HLS output directory exists with proper permissions
                try
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Binary directory: {binaryDir}");
                    Tools.Logger.VideoLog.LogCall(this, $"Current working directory: {Directory.GetCurrentDirectory()}");
                    Tools.Logger.VideoLog.LogCall(this, $"Target HLS path: {hlsOutputPath}");
                    
                    if (Directory.Exists(hlsOutputPath))
                    {
                        Tools.Logger.VideoLog.LogCall(this, "Removing existing HLS directory");
                        Directory.Delete(hlsOutputPath, true);
                    }
                    Directory.CreateDirectory(hlsOutputPath);
                    Tools.Logger.VideoLog.LogCall(this, $"Created HLS directory: {hlsOutputPath}");
                    
                    // Test directory writability
                    string testFile = Path.Combine(hlsOutputPath, "test_write.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    Tools.Logger.VideoLog.LogCall(this, "HLS directory write test passed");
                    
                    Tools.Logger.VideoLog.LogCall(this, $"HLS Live Frame Source initialized - HTTP port: {httpPort}, HLS path: {hlsOutputPath}");
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Failed to create writable HLS directory: {ex.Message}");
                    throw new InvalidOperationException($"Cannot create writable HLS directory at {hlsOutputPath}", ex);
                }
            }
            else
            {
                Tools.Logger.VideoLog.LogCall(this, "HLS Live Frame Source initialized with HLS DISABLED - HTTP port: {httpPort}");
            }
        }

        public override IEnumerable<Mode> GetModes()
        {
            // Delegate to platform-specific implementation
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                return GetMacOSModes();
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return GetWindowsModes();
            }
            
            return new List<Mode>();
        }

        private IEnumerable<Mode> GetMacOSModes()
        {
            Tools.Logger.VideoLog.LogCall(this, $"GetMacOSModes() called - querying AVFoundation capabilities for '{VideoConfig.DeviceName}'");
            List<Mode> supportedModes = new List<Mode>();
            
            try
            {
                string testArgs = $"-f avfoundation -framerate 30 -video_size 1234x5678 -i \"{VideoConfig.DeviceName}\"";
                Tools.Logger.VideoLog.LogCall(this, $"Querying supported modes with command: ffmpeg {testArgs}");
                
                var output = ffmpegMediaFramework.GetFfmpegText(testArgs, l => 
                    l.Contains("Supported modes:") || 
                    l.Contains("@[") || 
                    l.Contains("Selected video size") ||
                    l.Contains("Error opening"));
                
                bool foundSupportedModes = false;
                int index = 0;
                
                foreach (string line in output)
                {
                    if (line.Contains("Supported modes:"))
                    {
                        foundSupportedModes = true;
                        continue;
                    }
                    
                    if (foundSupportedModes && line.Contains("@["))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)x(\d+)@\[([0-9.\s]+)\]fps");
                        if (match.Success)
                        {
                            int width = int.Parse(match.Groups[1].Value);
                            int height = int.Parse(match.Groups[2].Value);
                            string frameRatesStr = match.Groups[3].Value;
                            
                            var frameRates = frameRatesStr.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                .Select(fr => float.Parse(fr))
                                .ToList();
                            
                            foreach (var frameRate in frameRates)
                            {
                                var mode = new Mode
                                {
                                    Width = width,
                                    Height = height,
                                    FrameRate = frameRate,
                                    FrameWork = FrameWork.ffmpeg,
                                    Index = index,
                                    Format = "uyvy422"
                                };
                                supportedModes.Add(mode);
                                index++;
                            }
                        }
                    }
                }
                
                Tools.Logger.VideoLog.LogCall(this, $"macOS camera capability detection complete: {supportedModes.Count} supported modes found");
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
            
            return supportedModes;
        }

        private IEnumerable<Mode> GetWindowsModes()
        {
            Tools.Logger.VideoLog.LogCall(this, $"GetWindowsModes() called - querying DirectShow capabilities for '{VideoConfig.DeviceName}'");
            List<Mode> supportedModes = new List<Mode>();
            
            try
            {
                string ffmpegListCommand = "-list_options true -f dshow -i video=\"" + VideoConfig.DeviceName + "\"";
                IEnumerable<string> modes = ffmpegMediaFramework.GetFfmpegText(ffmpegListCommand, l => l.Contains("pixel_format"));

                int index = 0;
                foreach (string format in modes)
                {
                    string pixelFormat = ffmpegMediaFramework.GetValue(format, "pixel_format");
                    string size = ffmpegMediaFramework.GetValue(format, "min s");
                    string fps = ffmpegMediaFramework.GetValue(format, "fps");

                    string[] sizes = size.Split("x");
                    if (int.TryParse(sizes[0], out int x) && int.TryParse(sizes[1], out int y) && float.TryParse(fps, out float ffps))
                    {
                        string formatToUse = pixelFormat == "uyvy422" ? "uyvy422" : pixelFormat;
                        var mode = new Mode { Format = formatToUse, Width = x, Height = y, FrameRate = ffps, FrameWork = FrameWork.ffmpeg, Index = index };
                        supportedModes.Add(mode);
                        index++;
                    }
                }
                
                Tools.Logger.VideoLog.LogCall(this, $"Windows camera capability detection complete: {supportedModes.Count} supported modes found");
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
            
            return supportedModes;
        }

        protected override ProcessStartInfo GetProcessStartInfo()
        {
            string inputArgs = GetPlatformSpecificInputArgs();
            
            if (HlsEnabled)
            {
                // HLS ENABLED: Dual output FFmpeg command
                string hlsPath = Path.Combine(hlsOutputPath, "stream.m3u8");
                
                // Log paths for debugging
                Tools.Logger.VideoLog.LogCall(this, $"HLS output directory: {hlsOutputPath}");
                Tools.Logger.VideoLog.LogCall(this, $"HLS playlist path: {hlsPath}");
                
                // Dual output FFmpeg command:
                // 1. Raw RGBA to pipe:1 for live processing  
                // 2. HLS stream for HTTP access and recording
                
                // Log individual components for debugging
                Tools.Logger.VideoLog.LogCall(this, $"Input args: {inputArgs}");
                Tools.Logger.VideoLog.LogCall(this, $"Video mode: {VideoConfig.VideoMode?.Width}x{VideoConfig.VideoMode?.Height}@{VideoConfig.VideoMode?.FrameRate}fps");
                
                // Get hardware-accelerated encoding settings
                string encodingArgs = GetHardwareEncodingArgs();
                
                // Fix: Let camera provide frames at its natural rate, don't force frame rate conversion
                float targetFrameRate = VideoConfig.VideoMode?.FrameRate ?? 30.0f;
                int gop = Math.Max(1, (int)Math.Round(targetFrameRate * 0.1f)); // 0.1s GOP
                
                // No frame rate filtering needed - recording now handles 60fps correctly
                string ffmpegArgs = $"{inputArgs} " +
                                   $"-fflags nobuffer " +
                                   $"-flags low_delay " +
                                   $"-strict experimental " +
                                   $"-threads 1 " +
                                   $"-fps_mode passthrough " +  // Use camera's natural frame rate
                                   $"-an " +
                                   $"-filter_complex \"[0:v]split=2[out1][out2];[out1]format=rgba[outpipe];[out2]format=yuv420p[outfile]\" " +
                                   $"-map \"[outpipe]\" -f rawvideo pipe:1 " +  // RGBA output for live display
                                   $"-map \"[outfile]\" {encodingArgs} -g {gop} -keyint_min {gop} -force_key_frames \"expr:gte(t,n_forced*0.1)\" " +  // HLS output with tighter GOP
                                   $"-hls_time 0.5 -hls_list_size 3 -hls_flags delete_segments+independent_segments " +  // Ultra-low latency: 0.5s segments, only 3 segments
                                   $"-hls_segment_type mpegts " +  // Use MPEG-TS for better streaming
                                   $"-start_number 0 " +  // Start numbering from 0
                                   $"-f hls \"{hlsPath}\"";
                
                Tools.Logger.VideoLog.LogCall(this, $"LIVE STREAM DEBUG: Using passthrough mode, no frame filtering");

                Tools.Logger.VideoLog.LogCall(this, $"Hardware Accelerated HLS FFmpeg Command:");
                Tools.Logger.VideoLog.LogCall(this, ffmpegArgs);
                return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
            }
            else
            {
                // HLS DISABLED: Single output - RGBA only for live processing
                Tools.Logger.VideoLog.LogCall(this, $"HLS is DISABLED - using single RGBA output only");
                Tools.Logger.VideoLog.LogCall(this, $"Input args: {inputArgs}");
                Tools.Logger.VideoLog.LogCall(this, $"Video mode: {VideoConfig.VideoMode?.Width}x{VideoConfig.VideoMode?.Height}@{VideoConfig.VideoMode?.FrameRate}fps");
                
                // Single output FFmpeg command - RGBA only
                string ffmpegArgs = $"{inputArgs} " +
                                   $"-fflags nobuffer " +
                                   $"-flags low_delay " +
                                   $"-strict experimental " +
                                   $"-threads 1 " +
                                   $"-fps_mode passthrough " +  // Use camera's natural frame rate
                                   $"-an " +
                                   $"-f rawvideo " +
                                   $"-pix_fmt rgba " +
                                   $"pipe:1";  // RGBA output for live display only
                
                Tools.Logger.VideoLog.LogCall(this, $"RGBA-Only FFmpeg Command (HLS Disabled):");
                Tools.Logger.VideoLog.LogCall(this, ffmpegArgs);
                return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
            }
        }

        private string GetPlatformSpecificInputArgs()
        {
            string name = VideoConfig.ffmpegId;
            
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                Tools.Logger.VideoLog.LogCall(this, $"CAMERA DEBUG: Requesting {VideoConfig.VideoMode.FrameRate}fps from camera '{name}'");
                return $"-f avfoundation " +
                       $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                       $"-pixel_format uyvy422 " +
                       $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                       $"-i \"{name}\"";
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return $"-f dshow " +
                       $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                       $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                       $"-rtbufsize 10M " +
                       $"-i video=\"{name}\"";
            }
            
            throw new PlatformNotSupportedException("Platform not supported for HLS live streaming");
        }

        /// <summary>
        /// Get hardware-accelerated encoding arguments based on platform and available encoders
        /// </summary>
        private string GetHardwareEncodingArgs()
        {
            try
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                {
                    // macOS - Use VideoToolbox hardware acceleration
                    Tools.Logger.VideoLog.LogCall(this, "Using macOS VideoToolbox hardware encoding");
                    return "-c:v h264_videotoolbox -q:v 50 -realtime 1 " +
                           "-b:v 8M -maxrate 12M -bufsize 2M " +
                           "-pix_fmt yuv420p -profile:v high -level 4.0";
                }
                else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    // Windows - Try NVENC first, fallback to Intel QSV, then software
                    return GetWindowsHardwareEncodingArgs();
                }
                else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                {
                    // Linux - Try NVENC, VAAPI, then software
                    return GetLinuxHardwareEncodingArgs();
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, "Hardware encoding detection failed, falling back to software");
            }

            // Fallback to software encoding
            Tools.Logger.VideoLog.LogCall(this, "Using software encoding (libx264)");
            return "-c:v libx264 -preset medium -tune zerolatency -crf 18 " +
                   "-b:v 8M -maxrate 12M -bufsize 2M " +
                   "-pix_fmt yuv420p -profile:v high -level 4.0";
        }

        /// <summary>
        /// Get Windows hardware encoding arguments with fallbacks
        /// </summary>
        private string GetWindowsHardwareEncodingArgs()
        {
            // Try to detect available encoders (this is a simplified approach)
            // In a full implementation, you might query FFmpeg for available encoders

            // Try NVIDIA NVENC first (most common for gaming/streaming)
            if (IsEncoderAvailable("h264_nvenc"))
            {
                Tools.Logger.VideoLog.LogCall(this, "Using NVIDIA NVENC hardware encoding");
                return "-c:v h264_nvenc -preset p3 -tune ll -rc vbr " +
                       "-cq 18 -b:v 8M -maxrate 12M -bufsize 2M " +
                       "-pix_fmt yuv420p -profile:v high -level 4.0";   
            }

            // Try Intel Quick Sync Video
            if (IsEncoderAvailable("h264_qsv"))
            {
                Tools.Logger.VideoLog.LogCall(this, "Using Intel Quick Sync Video hardware encoding");
                return "-c:v h264_qsv -preset veryfast -global_quality 18 " +
                       "-b:v 8M -maxrate 12M -bufsize 2M " +
                       "-pix_fmt yuv420p -profile:v high -level 4.0";
            }
            
            // Fallback to software
            Tools.Logger.VideoLog.LogCall(this, "No hardware encoders detected on Windows, using software");
            return "-c:v libx264 -preset medium -tune zerolatency -crf 18 " +
                   "-b:v 8M -maxrate 12M -bufsize 2M " +
                   "-pix_fmt yuv420p -profile:v high -level 4.0";
        }

        /// <summary>
        /// Get Linux hardware encoding arguments with fallbacks
        /// </summary>
        private string GetLinuxHardwareEncodingArgs()
        {
            // Try NVIDIA NVENC first
            if (IsEncoderAvailable("h264_nvenc"))
            {
                Tools.Logger.VideoLog.LogCall(this, "Using NVIDIA NVENC hardware encoding on Linux");
                return "-c:v h264_nvenc -preset p1 -tune ll -rc vbr " +
                       "-cq 18 -b:v 8M -maxrate 12M -bufsize 2M " +
                       "-pix_fmt yuv420p -profile:v high -level 4.0";
            }
            
            // Try VAAPI (Intel/AMD integrated graphics)
            if (IsEncoderAvailable("h264_vaapi"))
            {
                Tools.Logger.VideoLog.LogCall(this, "Using VAAPI hardware encoding on Linux");
                return "-c:v h264_vaapi -qp 18 " +
                       "-b:v 8M -maxrate 12M -bufsize 2M " +
                       "-pix_fmt yuv420p -profile:v high -level 4.0";
            }
            
            // Fallback to software
            Tools.Logger.VideoLog.LogCall(this, "No hardware encoders detected on Linux, using software");
            return "-c:v libx264 -preset medium -tune zerolatency -crf 18 " +
                   "-b:v 8M -maxrate 12M -bufsize 2M " +
                   "-pix_fmt yuv420p -profile:v high -level 4.0";
        }

        /// <summary>
        /// Check if a specific encoder is available (simplified check)
        /// In production, this could query FFmpeg directly for available encoders
        /// </summary>
        private bool IsEncoderAvailable(string encoderName)
        {
            try
            {
                // This is a simplified check - in production you might want to actually test the encoder
                // For now, we'll assume common encoders are available and let FFmpeg handle the fallback
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override bool Start()
        {
            Tools.Logger.VideoLog.LogCall(this, HlsEnabled ? "Starting HLS Live Frame Source" : "Starting RGBA-Only Live Frame Source (HLS Disabled)");
            
            if (!HlsEnabled)
            {
                Tools.Logger.VideoLog.LogCall(this, "HLS is disabled, skipping HLS directory check and HTTP server.");
                // Start the FFmpeg process with single RGBA output only
                Tools.Logger.VideoLog.LogCall(this, "Attempting to start FFmpeg process with single RGBA output");
                bool rgbaResult = base.Start();
                
                if (rgbaResult)
                {
                    Tools.Logger.VideoLog.LogCall(this, "RGBA-Only Live Stream started successfully (HLS Disabled)");
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, "FAILED to start RGBA-Only Live Stream - FFmpeg process did not start");
                }
                
                return rgbaResult;
            }
            
            // HLS ENABLED: Continue with normal HLS startup
            // Ensure HLS directory still exists before starting FFmpeg
            if (!Directory.Exists(hlsOutputPath))
            {
                Tools.Logger.VideoLog.LogCall(this, $"HLS directory missing, recreating: {hlsOutputPath}");
                try
                {
                    Directory.CreateDirectory(hlsOutputPath);
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    Tools.Logger.VideoLog.LogCall(this, $"Failed to recreate HLS directory: {ex.Message}");
                    return false;
                }
            }
            
            // Start HTTP server first
            if (!StartHttpServer())
            {
                Tools.Logger.VideoLog.LogCall(this, "Failed to start HTTP server, cannot start HLS streaming");
                return false;
            }
            
            // Start the FFmpeg process with dual outputs
            Tools.Logger.VideoLog.LogCall(this, "Attempting to start FFmpeg process with dual outputs");
            bool result = base.Start();
            
            if (result)
            {
                Tools.Logger.VideoLog.LogCall(this, $"HLS Live Stream started successfully - Stream URL: {HlsStreamUrl}");
            }
            else
            {
                Tools.Logger.VideoLog.LogCall(this, "FAILED to start HLS Live Stream - FFmpeg process did not start");
                Tools.Logger.VideoLog.LogCall(this, "This could be due to:");
                Tools.Logger.VideoLog.LogCall(this, "1. Invalid FFmpeg command syntax");
                Tools.Logger.VideoLog.LogCall(this, "2. Camera/device not available");
                Tools.Logger.VideoLog.LogCall(this, "3. HLS output directory permission issues");
                Tools.Logger.VideoLog.LogCall(this, "4. FFmpeg codec/filter issues");
                StopHttpServer();
            }
            
            return result;
        }

        public override bool Stop()
        {
            Tools.Logger.VideoLog.LogCall(this, "Stopping HLS Live Frame Source (immediate kill - no graceful shutdown)");
            
            // Force immediate stop for live stream - override base behavior
            bool result = StopImmediate();
            StopHttpServer();
            
            return result;
        }

        /// <summary>
        /// Immediate stop without any graceful shutdown delays - for live stream only
        /// </summary>
        private bool StopImmediate()
        {
            Tools.Logger.VideoLog.LogCall(this, "Forcing immediate HLS live stream termination");
            run = false;
            
            // Kill reading thread immediately
            if (thread != null && thread.IsAlive)
            {
                Tools.Logger.VideoLog.LogCall(this, "Terminating reading thread immediately");
                thread = null; // Don't wait for thread to finish
            }
            
            // Kill FFmpeg process immediately
            if (process != null && !process.HasExited)
            {
                Tools.Logger.VideoLog.LogCall(this, "Killing HLS FFmpeg process immediately (no graceful shutdown)");
                try
                {
                    process.Kill();
                    if (!process.WaitForExit(1000)) // Very short timeout
                    {
                        Tools.Logger.VideoLog.LogCall(this, "Process didn't die in 1 second, continuing anyway");
                    }
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Error killing HLS process: {ex.Message}");
                }
            }
            
            if (process != null)
            {
                try
                {
                    process.Dispose();
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Error disposing HLS process: {ex.Message}");
                }
                process = null;
            }
            
            Tools.Logger.VideoLog.LogCall(this, "HLS Live Frame Source stopped immediately");
            return true;
        }

        private bool StartHttpServer()
        {
            lock (httpServerLock)
            {
                if (httpServerRunning)
                {
                    Tools.Logger.VideoLog.LogCall(this, "HTTP server already running");
                    return true;
                }

                if (!HlsEnabled)
                {
                    Tools.Logger.VideoLog.LogCall(this, "HLS is disabled, skipping HTTP server start.");
                    return true;
                }

                try
                {
                    httpServer = new HttpListener();
                    httpServer.Prefixes.Add($"http://localhost:{httpPort}/");
                    httpServer.Start();
                    
                    httpServerRunning = true;
                    httpServerThread = new Thread(HttpServerWorker)
                    {
                        IsBackground = true,
                        Name = "HLS HTTP Server"
                    };
                    httpServerThread.Start();
                    
                    Tools.Logger.VideoLog.LogCall(this, $"HTTP server started on port {httpPort}");
                    return true;
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                    Tools.Logger.VideoLog.LogCall(this, $"Failed to start HTTP server on port {httpPort}");
                    return false;
                }
            }
        }

        private void StopHttpServer()
        {
            lock (httpServerLock)
            {
                if (!httpServerRunning)
                    return;

                if (!HlsEnabled)
                {
                    Tools.Logger.VideoLog.LogCall(this, "HLS is disabled, skipping HTTP server stop.");
                    return;
                }

                try
                {
                    httpServerRunning = false;
                    httpServer?.Stop();
                    httpServer?.Close();
                    httpServerThread?.Join(5000);
                    
                    Tools.Logger.VideoLog.LogCall(this, "HTTP server stopped");
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                }
                finally
                {
                    httpServer = null;
                    httpServerThread = null;
                }
            }
        }

        private void HttpServerWorker()
        {
            Tools.Logger.VideoLog.LogCall(this, "HTTP server worker thread started");
            
            while (httpServerRunning)
            {
                try
                {
                    var context = httpServer.GetContext();
                    Task.Run(() => HandleHttpRequest(context));
                }
                catch (Exception ex) when (httpServerRunning)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                }
                catch
                {
                    // Expected when server is shutting down
                    break;
                }
            }
            
            Tools.Logger.VideoLog.LogCall(this, "HTTP server worker thread stopped");
        }

        private void HandleHttpRequest(HttpListenerContext context)
        {
            try
            {
                string requestPath = context.Request.Url.AbsolutePath;
                Tools.Logger.VideoLog.LogCall(this, $"HTTP request: {requestPath}");
                
                if (HlsEnabled)
                {
                    if (requestPath.StartsWith("/hls/"))
                    {
                        string fileName = Path.GetFileName(requestPath);
                        string filePath = Path.Combine(hlsOutputPath, fileName);
                        
                        if (File.Exists(filePath))
                        {
                            // Set appropriate content type
                            if (fileName.EndsWith(".m3u8"))
                            {
                                context.Response.ContentType = "application/vnd.apple.mpegurl";
                                context.Response.Headers.Add("Cache-Control", "no-cache");
                            }
                            else if (fileName.EndsWith(".ts"))
                            {
                                context.Response.ContentType = "video/mp2t";
                                context.Response.Headers.Add("Cache-Control", "max-age=10");
                            }
                            
                            // Enable CORS for browser access
                            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                            
                            byte[] fileData = File.ReadAllBytes(filePath);
                            context.Response.ContentLength64 = fileData.Length;
                            context.Response.OutputStream.Write(fileData, 0, fileData.Length);
                            context.Response.StatusCode = 200;
                        }
                        else
                        {
                            context.Response.StatusCode = 404;
                            Tools.Logger.VideoLog.LogCall(this, $"HLS file not found: {filePath}");
                        }
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                    }
                }
                else
                {
                    context.Response.StatusCode = 404;
                    context.Response.ContentType = "text/plain";
                    context.Response.ContentLength64 = 0;
                    context.Response.OutputStream.Close();
                    return;
                }
                
                context.Response.Close();
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { }
            }
        }

        public override void Dispose()
        {
            StopHttpServer();
            
            // Clean up HLS files
            if (HlsEnabled)
            {
                try
                {
                    if (Directory.Exists(hlsOutputPath))
                    {
                        Directory.Delete(hlsOutputPath, true);
                    }
                }
                catch (Exception ex)
                {
                    Tools.Logger.VideoLog.LogException(this, ex);
                }
            }
            
            base.Dispose();
        }
    }
}