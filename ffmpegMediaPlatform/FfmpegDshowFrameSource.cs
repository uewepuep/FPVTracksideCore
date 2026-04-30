using ImageServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics;
using System.IO;

namespace FfmpegMediaPlatform
{
    public class FfmpegDshowFrameSource : FfmpegFrameSource
    {
        private bool hwAccelFailed;

        private static bool IsCompressedFormat(string format)
        {
            if (string.IsNullOrEmpty(format))
                return false;
            string f = format.ToLower();
            return f == "h264" || f == "h265" || f == "hevc" || f == "mjpeg";
        }

        public FfmpegDshowFrameSource(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig)
            : base(ffmpegMediaFramework, videoConfig)
        {
        }

        public override bool Start()
        {
            bool result = base.Start();
            if (result && process != null && VideoConfig.HardwareAcceleration && IsCompressedFormat(VideoConfig.VideoMode?.Format) && !hwAccelFailed)
            {
                process.ErrorDataReceived += DetectHardwareAccelError;
            }
            return result;
        }

        private void DetectHardwareAccelError(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null || inited || hwAccelFailed)
                return;

            string line = e.Data;
            bool isCudaLine = line.IndexOf("cuda", StringComparison.OrdinalIgnoreCase) >= 0
                           || line.IndexOf("cuvid", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isError = line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                        || line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                        || line.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isCudaLine && isError)
            {
                hwAccelFailed = true;
                Tools.Logger.VideoLog.LogCall(this, "Hardware acceleration failed, falling back to software decode");
                Task.Run(() => { Stop(); Start(); });
            }
        }

        public override IEnumerable<Mode> GetModes()
        {
            Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG GetModes() called for '{VideoConfig.DeviceName}'");
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
                    var videoMode = new Mode { Format = mode.format, Width = mode.width, Height = mode.height, FrameRate = mode.fps, FrameWork = FrameWork.FFmpeg, Index = index };
                    supportedModes.Add(videoMode);
                    index++;
                }

                Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG GetModes() complete: {supportedModes.Count} modes for '{VideoConfig.DeviceName}'");
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }
            
            return supportedModes;
        }

        protected override ProcessStartInfo GetProcessStartInfo()
        {
            string name = VideoConfig.ffmpegId;
            string format = VideoConfig.VideoMode.Format;
            string ffmpegArgs;

            // Build format-specific input arguments
            string inputFormatArgs = "";
            if (!string.IsNullOrEmpty(format))
            {
                // For vcodec formats like h264, mjpeg, use vcodec parameter
                if (format == "h264" || format == "mjpeg")
                {
                    inputFormatArgs = $"-vcodec {format} ";
                }
                // For pixel formats like yuyv422, use pixel_format parameter
                else if (format != "uyvy422") // uyvy422 is the default, don't specify explicitly
                {
                    inputFormatArgs = $"-pixel_format {format} ";
                }
            }

            // Build video filter string for flip/mirror
            List<string> filters = new List<string>();
            if (VideoConfig.Flipped)
                filters.Add("vflip");
            if (VideoConfig.Mirrored)
                filters.Add("hflip");

            string videoFilter = filters.Any() ? string.Join(",", filters) + "," : "";

            if (Recording && !string.IsNullOrEmpty(recordingFilename))
            {
                string recordingPath = Path.GetFullPath(recordingFilename);

                // Use hardware-accelerated H.264 encoding for Windows (try NVENC first, fallback to software)
                ffmpegArgs = $"-f dshow " +
                                $"-rtbufsize 2048M " +
                                $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                                $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                                $"{inputFormatArgs}" +
                                $"-i video=\"{name}\" " +
                                $"-fflags nobuffer " +
                                $"-flags low_delay " +
                                $"-strict experimental " +
                                $"-threads 4 " +
                                $"-fps_mode passthrough " +
                                $"-copyts " +
                                $"-an " +
                                $"-filter_complex \"[0:v]{videoFilter}split=2[out1][out2];[out1]format=rgba[outpipe];[out2]format=yuv420p[outfile]\" " +
                                $"-map \"[outpipe]\" -f rawvideo pipe:1 " +
                                $"-map \"[outfile]\" -c:v {(VideoConfig.HardwareAcceleration ? "h264_nvenc -preset llhp" : "libx264 -preset ultrafast")} -tune zerolatency -b:v 5M -f matroska -avoid_negative_ts make_zero \"{recordingPath}\"";

                Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG Windows Recording Mode ({format}, filters: {videoFilter}): {ffmpegArgs}");
            }
            else
            {
                // Live mode: Use hardware decode acceleration when available (only for compressed formats)
                string hwaccelArgs = "";
                string decoderCodec = "";

                if (VideoConfig.HardwareAcceleration && IsCompressedFormat(VideoConfig.VideoMode?.Format) && !hwAccelFailed)
                {
                    if (format == "h264")
                    {
                        decoderCodec = "-c:v h264_cuvid ";
                        hwaccelArgs = "-hwaccel cuda -hwaccel_output_format cuda ";
                        Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG Hardware decode: Using h264_cuvid decoder for H264 capture card");
                    }
                    else if (format == "mjpeg")
                    {
                        decoderCodec = "-c:v mjpeg_cuvid ";
                        hwaccelArgs = "-hwaccel cuda -hwaccel_output_format cuda ";
                        Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG Hardware decode: Using mjpeg_cuvid decoder for MJPEG capture card");
                    }
                    else
                    {
                        hwaccelArgs = "-hwaccel cuda ";
                        Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG Hardware decode: Using generic CUDA hwaccel for {format}");
                    }
                }

                // PERFORMANCE: Enhanced low-delay flags for 4K video to reduce 1-second startup delay
                // Note: When using CUVID decoder, filters must handle CUDA frames or upload/download from GPU
                // mjpeg_cuvid produces NV12 frames tagged with bogus colorspace metadata (csp:gbr prim:reserved trc:reserved),
                // which swscaler refuses to convert to rgba. setparams overrides the metadata to a valid colorspace.
                string filterPrefix = (hwaccelArgs.Contains("cuda") && decoderCodec != "") ? "hwdownload,format=nv12,setparams=colorspace=bt709:color_primaries=bt709:color_trc=bt709," : "";

                ffmpegArgs = $"-f dshow " +
                                $"{hwaccelArgs}" +
                                $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                                $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                                $"{inputFormatArgs}" +
                                $"{decoderCodec}" +
                                $"-rtbufsize 512M " +
                                $"-i video=\"{name}\" " +
                                $"-fflags nobuffer+fastseek+flush_packets " +
                                $"-flags low_delay " +
                                $"-avioflags direct " +
                                $"-flush_packets 1 " +
                                $"-max_delay 0 " +
                                $"-strict experimental " +
                                $"-threads 4 " +
                                $"-fps_mode passthrough " +
                                $"-copyts " +
                                $"-probesize 32 " +
                                $"-analyzeduration 0 " +
                                $"-an " +
                                $"-filter_complex \"[0:v]{filterPrefix}{videoFilter}format=rgba[outpipe]\" " +
                                $"-map \"[outpipe]\" -f rawvideo pipe:1";

                Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG Windows Live Mode ({format}, filters: {videoFilter}) HW Accel: {VideoConfig.HardwareAcceleration}: {ffmpegArgs}");
            }
            return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
        }
    }
}
