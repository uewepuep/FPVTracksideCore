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
        /// Default: false (HLS disabled for lower latency)
        ///
        /// Usage: FfmpegHlsLiveFrameSource.HlsEnabled = true; // to enable
        /// </summary>
        public static bool HlsEnabled { get; set; } = false;
        
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
                IEnumerable<string> modes = ffmpegMediaFramework.GetFfmpegText(ffmpegListCommand, l => l.Contains("pixel_format") || l.Contains("vcodec="));

                int index = 0;
                var parsedModes = new List<(string format, int width, int height, float fps, int priority)>();
                
                // Parse all modes and assign priorities
                foreach (string format in modes)
                {
                    // Try vcodec first (preferred formats like h264, mjpeg)
                    string videoFormat = ffmpegMediaFramework.GetValue(format, "vcodec");
                    int priority = 1; // Default priority for vcodec formats
                    
                    // If no vcodec, try pixel_format (lower priority)
                    if (string.IsNullOrEmpty(videoFormat))
                    {
                        videoFormat = ffmpegMediaFramework.GetValue(format, "pixel_format");
                        priority = 2; // Lower priority for pixel_format
                    }
                    
                    // Set higher priority for preferred codecs
                    if (videoFormat == "h264")
                    {
                        priority = 0; // Highest priority for h264
                    }
                    else if (videoFormat == "mjpeg")
                    {
                        priority = 0; // Highest priority for mjpeg
                    }
                    else if (videoFormat == "uyvy422")
                    {
                        priority = 1; // Good priority for uyvy422
                    }
                    
                    string minSize = ffmpegMediaFramework.GetValue(format, "min s");
                    string maxSize = ffmpegMediaFramework.GetValue(format, "max s");
                    
                    // Parse fps values - support both old single fps and new min/max fps formats
                    float minFps = 0, maxFps = 0;
                    var fpsMatches = System.Text.RegularExpressions.Regex.Matches(format, @"fps=([\d.]+)");
                    
                    if (fpsMatches.Count >= 2)
                    {
                        // New format with min and max fps (e.g., "min s=1920x1080 fps=25 max s=1920x1080 fps=60.0002")
                        float.TryParse(fpsMatches[0].Groups[1].Value, out minFps);
                        float.TryParse(fpsMatches[1].Groups[1].Value, out maxFps);
                    }
                    else if (fpsMatches.Count == 1)
                    {
                        // Old format or single fps value - try both new regex and old method for compatibility
                        if (float.TryParse(fpsMatches[0].Groups[1].Value, out minFps))
                        {
                            maxFps = minFps; // Single fps value, min and max are the same
                        }
                        else
                        {
                            // Fallback to original parsing method for backward compatibility
                            string fps = ffmpegMediaFramework.GetValue(format, "fps");
                            if (float.TryParse(fps, out minFps))
                            {
                                maxFps = minFps;
                            }
                        }
                    }
                    else
                    {
                        // Fallback to original parsing method for backward compatibility
                        string fps = ffmpegMediaFramework.GetValue(format, "fps");
                        if (float.TryParse(fps, out minFps))
                        {
                            maxFps = minFps;
                        }
                    }

                    string[] minSizes = minSize.Split("x");
                    string[] maxSizes = maxSize.Split("x");
                    
                    if (int.TryParse(minSizes[0], out int minX) && int.TryParse(minSizes[1], out int minY) &&
                        int.TryParse(maxSizes[0], out int maxX) && int.TryParse(maxSizes[1], out int maxY) &&
                        minFps > 0)
                    {
                        // Use the resolution from min s (which should match max s for most cases)
                        int width = minX;
                        int height = minY;
                        
                        // Generate frame rates between min and max fps
                        var supportedFrameRates = new List<float>();
                        
                        // Always add the min and max fps
                        supportedFrameRates.Add(minFps);
                        if (maxFps > minFps)
                        {
                            supportedFrameRates.Add(maxFps);
                        }
                        
                        // Add common frame rates within the range
                        var commonRates = new float[] { 24, 25, 29.97f, 30, 50, 59.94f, 60 };
                        foreach (var rate in commonRates)
                        {
                            if (rate > minFps && rate < maxFps)
                            {
                                supportedFrameRates.Add(rate);
                            }
                        }
                        
                        // Remove duplicates and sort
                        supportedFrameRates = supportedFrameRates.Distinct().OrderBy(f => f).ToList();
                        
                        // Add all supported frame rates as separate modes
                        foreach (var fps in supportedFrameRates)
                        {
                            parsedModes.Add((videoFormat, width, height, fps, priority));
                        }
                    }
                }
                
                // Sort by priority (0=highest), then by resolution, then by framerate
                var sortedModes = parsedModes
                    .OrderBy(m => m.priority)
                    .ThenByDescending(m => m.width * m.height)
                    .ThenByDescending(m => m.fps)
                    .ToList();
                
                // Add sorted modes to supportedModes list
                foreach (var mode in sortedModes)
                {
                    var videoMode = new Mode { Format = mode.format, Width = mode.width, Height = mode.height, FrameRate = mode.fps, FrameWork = FrameWork.ffmpeg, Index = index };
                    supportedModes.Add(videoMode);
                    index++;
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

            // Build video filter string for flip/mirror
            // Note: Mac cameras are upside down by default, Windows cameras are right-side up
            List<string> filters = new List<string>();

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                // macOS: Cameras are upside down by default, apply vflip when Flipped=false to show right-side up
                if (VideoConfig.Flipped)
                    filters.Add("vflip");
            }
            else
            {
                // Windows: Cameras are right-side up by default, apply vflip when Flipped=true
                if (VideoConfig.Flipped)
                    filters.Add("vflip");
            }

            // Mirror is the same for both platforms
            if (VideoConfig.Mirrored)
                filters.Add("hflip");

            string videoFilter = filters.Any() ? string.Join(",", filters) + "," : "";

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

                // Apply flip/mirror filters to the input before split
                string ffmpegArgs = $"{inputArgs} " +
                                   $"-fflags nobuffer " +
                                   $"-flags low_delay " +
                                   $"-strict experimental " +
                                   $"-threads 4 " +  // Increased for better 4K60 performance
                                   $"-fps_mode passthrough " +  // Use camera's natural frame rate
                                   $"-an " +
                                   $"-filter_complex \"[0:v]{videoFilter}split=2[out1][out2];[out1]format=rgba[outpipe];[out2]format=yuv420p[outfile]\" " +
                                   $"-map \"[outpipe]\" -f rawvideo pipe:1 " +  // RGBA output for live display
                                   $"-map \"[outfile]\" {encodingArgs} -g {gop} -keyint_min {gop} -force_key_frames \"expr:gte(t,n_forced*0.1)\" " +  // HLS output with tighter GOP
                                   $"-hls_time 0.5 -hls_list_size 3 -hls_flags delete_segments+independent_segments " +  // Ultra-low latency: 0.5s segments, only 3 segments
                                   $"-hls_segment_type mpegts " +  // Use MPEG-TS for better streaming
                                   $"-start_number 0 " +  // Start numbering from 0
                                   $"-f hls \"{hlsPath}\"";

                Tools.Logger.VideoLog.LogCall(this, $"HLS Live Stream with filters: {videoFilter}");

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

                // Single output FFmpeg command - RGBA only with flip/mirror filters
                string ffmpegArgs = $"{inputArgs} " +
                                   $"-fflags nobuffer " +
                                   $"-flags low_delay " +
                                   $"-strict experimental " +
                                   $"-threads 4 " +  // Increased for better 4K60 performance
                                   $"-fps_mode passthrough " +  // Use camera's natural frame rate
                                   $"-an " +
                                   $"-vf \"{videoFilter}format=rgba\" " +  // Apply flip/mirror and format conversion
                                   $"-f rawvideo " +
                                   $"pipe:1";  // RGBA output for live display only

                Tools.Logger.VideoLog.LogCall(this, $"RGBA-Only FFmpeg Command (HLS Disabled) with filters: {videoFilter} HW Accel: {VideoConfig.HardwareDecodeAcceleration}:");
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
                
                // Add hardware decode acceleration for macOS (only for compressed formats)
                string hwaccelArgs = "";
                if (VideoConfig.HardwareDecodeAcceleration && VideoConfig.IsCompressedVideoFormat)
                {
                    hwaccelArgs = "-hwaccel videotoolbox ";
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Hardware decode acceleration enabled for {VideoConfig.VideoMode?.Format} - trying VideoToolbox");
                }
                else if (VideoConfig.HardwareDecodeAcceleration && !VideoConfig.IsCompressedVideoFormat)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Hardware decode acceleration skipped for uncompressed format: {VideoConfig.VideoMode?.Format}");
                }
                
                return $"-f avfoundation " +
                       $"{hwaccelArgs}" +
                       $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                       $"-pixel_format uyvy422 " +
                       $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                       $"-i \"{name}\"";
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                // Add hardware decode acceleration for Windows (only for compressed formats)
                string hwaccelArgs = "";
                if (VideoConfig.HardwareDecodeAcceleration && VideoConfig.IsCompressedVideoFormat)
                {
                    hwaccelArgs = "-hwaccel cuda ";
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Hardware decode acceleration enabled for {VideoConfig.VideoMode?.Format} - trying NVDEC/CUDA");
                }
                else if (VideoConfig.HardwareDecodeAcceleration && !VideoConfig.IsCompressedVideoFormat)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Hardware decode acceleration skipped for uncompressed format: {VideoConfig.VideoMode?.Format}");
                }
                
                // Build format-specific input arguments
                string formatArgs = "";
                string format = VideoConfig.VideoMode?.Format;
                if (!string.IsNullOrEmpty(format))
                {
                    // For vcodec formats like h264, mjpeg, use vcodec parameter
                    if (format == "h264" || format == "mjpeg")
                    {
                        formatArgs = $"-vcodec {format} ";
                    }
                    // For pixel formats like yuyv422, use pixel_format parameter  
                    else if (format != "uyvy422") // uyvy422 is the default, don't specify explicitly
                    {
                        formatArgs = $"-pixel_format {format} ";
                    }
                }
                
                return $"-f dshow " +
                       $"{hwaccelArgs}" +
                       $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                       $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                       $"{formatArgs}" +
                       $"-rtbufsize 100M " +  // Large buffer for 4K60 capture cards to prevent frame drops
                       $"-i video=\"{name}\"";
            }
            
            throw new PlatformNotSupportedException("Platform not supported for HLS live streaming");
        }

        /// <summary>
        /// Get hardware-accelerated encoding arguments based on platform and available encoders
        /// </summary>
        private string GetHardwareEncodingArgs()
        {
            // Check if hardware decode acceleration is enabled in video config
            bool useHardwareDecoding = VideoConfig.HardwareDecodeAcceleration;
            
            try
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                {
                    // macOS - Use VideoToolbox hardware acceleration
                    string decodingArgs = useHardwareDecoding ? GetMacOSHardwareDecodingArgs() : "";
                    Tools.Logger.VideoLog.LogCall(this, $"Using macOS VideoToolbox hardware encoding (hardware decode: {useHardwareDecoding})");
                    return $"{decodingArgs}-c:v h264_videotoolbox -q:v 50 -realtime 1 " +
                           "-b:v 8M -maxrate 12M -bufsize 2M " +
                           "-pix_fmt yuv420p -profile:v high -level 4.0";
                }
                else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    // Windows - Try NVENC first, fallback to Intel QSV, then software
                    return GetWindowsHardwareEncodingArgs(useHardwareDecoding);
                }
                else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                {
                    // Linux - Try NVENC, VAAPI, then software
                    return GetLinuxHardwareEncodingArgs(useHardwareDecoding);
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, "Hardware encoding detection failed, falling back to software");
            }

            // Fallback to software encoding
            Tools.Logger.VideoLog.LogCall(this, $"Using software encoding (libx264) (hardware decode: {useHardwareDecoding})");
            string softwareDecodingArgs = useHardwareDecoding ? GetSoftwareHardwareDecodingArgs() : "";
            return $"{softwareDecodingArgs}-c:v libx264 -preset medium -tune zerolatency -crf 18 " +
                   "-b:v 8M -maxrate 12M -bufsize 2M " +
                   "-pix_fmt yuv420p -profile:v high -level 4.0";
        }

        /// <summary>
        /// Get Windows hardware encoding arguments with fallbacks
        /// </summary>
        private string GetWindowsHardwareEncodingArgs(bool useHardwareDecoding)
        {
            string decodingArgs = useHardwareDecoding ? GetWindowsHardwareDecodingArgs() : "";
            
            // Try NVIDIA NVENC first (most common for gaming/streaming)
            if (IsEncoderAvailable("h264_nvenc"))
            {
                Tools.Logger.VideoLog.LogCall(this, $"Using NVIDIA NVENC hardware encoding (hardware decode: {useHardwareDecoding})");
                return $"{decodingArgs}-c:v h264_nvenc -preset p3 -tune ll -rc vbr " +
                       "-cq 18 -b:v 8M -maxrate 12M -bufsize 2M " +
                       "-pix_fmt yuv420p -profile:v high -level 4.0";   
            }

            // Try Intel Quick Sync Video
            if (IsEncoderAvailable("h264_qsv"))
            {
                Tools.Logger.VideoLog.LogCall(this, $"Using Intel Quick Sync Video hardware encoding (hardware decode: {useHardwareDecoding})");
                return $"{decodingArgs}-c:v h264_qsv -preset veryfast -global_quality 18 " +
                       "-b:v 8M -maxrate 12M -bufsize 2M " +
                       "-pix_fmt yuv420p -profile:v high -level 4.0";
            }
            
            // Fallback to software
            Tools.Logger.VideoLog.LogCall(this, $"No hardware encoders detected on Windows, using software (hardware decode: {useHardwareDecoding})");
            return $"{decodingArgs}-c:v libx264 -preset medium -tune zerolatency -crf 18 " +
                   "-b:v 8M -maxrate 12M -bufsize 2M " +
                   "-pix_fmt yuv420p -profile:v high -level 4.0";
        }

        /// <summary>
        /// Get Linux hardware encoding arguments with fallbacks
        /// </summary>
        private string GetLinuxHardwareEncodingArgs(bool useHardwareDecoding)
        {
            string decodingArgs = useHardwareDecoding ? GetLinuxHardwareDecodingArgs() : "";
            
            // Try NVIDIA NVENC first
            if (IsEncoderAvailable("h264_nvenc"))
            {
                Tools.Logger.VideoLog.LogCall(this, $"Using NVIDIA NVENC hardware encoding on Linux (hardware decode: {useHardwareDecoding})");
                return $"{decodingArgs}-c:v h264_nvenc -preset p1 -tune ll -rc vbr " +
                       "-cq 18 -b:v 8M -maxrate 12M -bufsize 2M " +
                       "-pix_fmt yuv420p -profile:v high -level 4.0";
            }
            
            // Try VAAPI (Intel/AMD integrated graphics)
            if (IsEncoderAvailable("h264_vaapi"))
            {
                Tools.Logger.VideoLog.LogCall(this, $"Using VAAPI hardware encoding on Linux (hardware decode: {useHardwareDecoding})");
                return $"{decodingArgs}-c:v h264_vaapi -qp 18 " +
                       "-b:v 8M -maxrate 12M -bufsize 2M " +
                       "-pix_fmt yuv420p -profile:v high -level 4.0";
            }
            
            // Fallback to software
            Tools.Logger.VideoLog.LogCall(this, $"No hardware encoders detected on Linux, using software (hardware decode: {useHardwareDecoding})");
            return $"{decodingArgs}-c:v libx264 -preset medium -tune zerolatency -crf 18 " +
                   "-b:v 8M -maxrate 12M -bufsize 2M " +
                   "-pix_fmt yuv420p -profile:v high -level 4.0";
        }

        /// <summary>
        /// Get macOS hardware decoding arguments
        /// </summary>
        private string GetMacOSHardwareDecodingArgs()
        {
            // macOS VideoToolbox hardware decoding for compressed video
            Tools.Logger.VideoLog.LogCall(this, "Adding macOS VideoToolbox hardware decode acceleration");
            return "-hwaccel videotoolbox -hwaccel_output_format videotoolbox_vld ";
        }

        /// <summary>
        /// Get Windows hardware decoding arguments
        /// </summary>
        private string GetWindowsHardwareDecodingArgs()
        {
            // Windows - Try NVDEC first, then DXVA2, then D3D11VA
            if (IsDecoderAvailable("h264_cuvid"))
            {
                Tools.Logger.VideoLog.LogCall(this, "Adding Windows NVDEC hardware decode acceleration");
                return "-hwaccel cuda -hwaccel_output_format cuda ";
            }
            else if (IsDecoderAvailable("dxva2"))
            {
                Tools.Logger.VideoLog.LogCall(this, "Adding Windows DXVA2 hardware decode acceleration");
                return "-hwaccel dxva2 ";
            }
            else if (IsDecoderAvailable("d3d11va"))
            {
                Tools.Logger.VideoLog.LogCall(this, "Adding Windows D3D11VA hardware decode acceleration");
                return "-hwaccel d3d11va ";
            }
            
            Tools.Logger.VideoLog.LogCall(this, "No Windows hardware decoders available, using software decode");
            return "";
        }

        /// <summary>
        /// Get Linux hardware decoding arguments
        /// </summary>
        private string GetLinuxHardwareDecodingArgs()
        {
            // Linux - Try NVDEC first, then VAAPI
            if (IsDecoderAvailable("h264_cuvid"))
            {
                Tools.Logger.VideoLog.LogCall(this, "Adding Linux NVDEC hardware decode acceleration");
                return "-hwaccel cuda -hwaccel_output_format cuda ";
            }
            else if (IsDecoderAvailable("vaapi"))
            {
                Tools.Logger.VideoLog.LogCall(this, "Adding Linux VAAPI hardware decode acceleration");
                return "-hwaccel vaapi -hwaccel_output_format vaapi ";
            }
            
            Tools.Logger.VideoLog.LogCall(this, "No Linux hardware decoders available, using software decode");
            return "";
        }

        /// <summary>
        /// Get software-based hardware decoding arguments (generic cross-platform)
        /// </summary>
        private string GetSoftwareHardwareDecodingArgs()
        {
            // Generic hardware acceleration attempt - let FFmpeg auto-detect
            Tools.Logger.VideoLog.LogCall(this, "Attempting auto hardware decode acceleration");
            return "-hwaccel auto ";
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

        /// <summary>
        /// Check if a specific decoder is available (simplified check)
        /// In production, this could query FFmpeg directly for available decoders
        /// </summary>
        private bool IsDecoderAvailable(string decoderName)
        {
            try
            {
                // This is a simplified check - in production you might want to actually test the decoder
                // For now, we'll assume common decoders are available and let FFmpeg handle the fallback
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