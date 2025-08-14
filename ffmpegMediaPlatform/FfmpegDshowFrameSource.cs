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
        public FfmpegDshowFrameSource(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig)
            : base(ffmpegMediaFramework, videoConfig)
        {
        }

        public override IEnumerable<Mode> GetModes()
        {
            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG GetModes() called - querying actual camera capabilities for '{VideoConfig.DeviceName}'");
            List<Mode> supportedModes = new List<Mode>();
            
            try
            {
                string ffmpegListCommand = "-list_options true -f dshow -i video=\"" + VideoConfig.DeviceName + "\"";
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG COMMAND (list camera modes): ffmpeg {ffmpegListCommand}");
                
                // Get all FFmpeg output lines for debugging
                IEnumerable<string> allLines = ffmpegMediaFramework.GetFfmpegText(ffmpegListCommand, null);
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG DEBUG: All output lines count: {allLines.Count()}");
                foreach (string line in allLines)
                {
                    if (line.Contains("vcodec=") || line.Contains("pixel_format="))
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"FFMPEG DEBUG: Found format line: {line}");
                    }
                }
                
                IEnumerable<string> modes = ffmpegMediaFramework.GetFfmpegText(ffmpegListCommand, l => l.Contains("pixel_format") || l.Contains("vcodec="));
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG DEBUG: Filtered modes count: {modes.Count()}");

                int index = 0;
                var parsedModes = new List<(string format, int width, int height, float fps, int priority)>();
                
                // Parse all modes and assign priorities
                foreach (string format in modes)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG OUTPUT: {format}");
                    
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
                    
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG DEBUG: Found {fpsMatches.Count} fps matches in: {format}");
                    for (int i = 0; i < fpsMatches.Count; i++)
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"FFMPEG DEBUG: Match {i}: {fpsMatches[i].Groups[1].Value}");
                    }
                    
                    if (fpsMatches.Count >= 2)
                    {
                        // New format with min and max fps (e.g., "min s=1920x1080 fps=25 max s=1920x1080 fps=60.0002")
                        float.TryParse(fpsMatches[0].Groups[1].Value, out minFps);
                        float.TryParse(fpsMatches[1].Groups[1].Value, out maxFps);
                        Tools.Logger.VideoLog.LogCall(this, $"FFMPEG DEBUG: Parsed minFps={minFps}, maxFps={maxFps}");
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
                        Tools.Logger.VideoLog.LogCall(this, $"FFMPEG DEBUG: Added minFps: {minFps}");
                        if (maxFps > minFps)
                        {
                            supportedFrameRates.Add(maxFps);
                            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG DEBUG: Added maxFps: {maxFps}");
                        }
                        
                        // Add common frame rates within the range
                        var commonRates = new float[] { 24, 25, 29.97f, 30, 50, 59.94f, 60 };
                        foreach (var rate in commonRates)
                        {
                            if (rate > minFps && rate < maxFps)
                            {
                                supportedFrameRates.Add(rate);
                                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG DEBUG: Added common rate: {rate}");
                            }
                        }
                        
                        // Remove duplicates and sort
                        supportedFrameRates = supportedFrameRates.Distinct().OrderBy(f => f).ToList();
                        Tools.Logger.VideoLog.LogCall(this, $"FFMPEG DEBUG: Final supported frame rates: [{string.Join(", ", supportedFrameRates)}]");
                        
                        // Add all supported frame rates as separate modes
                        foreach (var fps in supportedFrameRates)
                        {
                            parsedModes.Add((videoFormat, width, height, fps, priority));
                            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG ✓ PARSED MODE: {width}x{height}@{fps}fps ({videoFormat}) (Priority {priority}) [Range: {minFps}-{maxFps}]");
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
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG ✓ ADDED MODE: {mode.width}x{mode.height}@{mode.fps}fps ({mode.format}) (Index {index}, Priority {mode.priority})");
                    index++;
                }
                
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Camera capability detection complete: {supportedModes.Count} supported modes found");
                
                if (supportedModes.Count == 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, "FFMPEG WARNING: No supported modes detected for camera!");
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Final supported modes for '{VideoConfig.DeviceName}':");
                    foreach (var mode in supportedModes.OrderBy(m => m.Width * m.Height).ThenBy(m => m.FrameRate))
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"FFMPEG   - {mode.Width}x{mode.Height}@{mode.FrameRate}fps");
                    }
                }
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
                                $"-threads 1 " +
                                $"-fps_mode passthrough " +
                                $"-copyts " +
                                $"-an " +
                                $"-filter_complex \"split=2[out1][out2];[out1]format=rgba[outpipe];[out2]format=yuv420p[outfile]\" " +
                                $"-map \"[outpipe]\" -f rawvideo pipe:1 " +
                                $"-map \"[outfile]\" -c:v h264_nvenc -preset llhp -tune zerolatency -b:v 5M -f matroska -avoid_negative_ts make_zero \"{recordingPath}\"";
                
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Windows Recording Mode ({format}): {ffmpegArgs}");
            }
            else
            {
                // Live mode: Use dual stream approach like recording mode but only output RGBA pipe
                ffmpegArgs = $"-f dshow " +
                                $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                                $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                                $"{inputFormatArgs}" +
                                $"-rtbufsize 10M " +
                                $"-i video=\"{name}\" " +
                                $"-fflags nobuffer " +
                                $"-flags low_delay " +
                                $"-strict experimental " +
                                $"-threads 1 " +
                                $"-fps_mode passthrough " +
                                $"-copyts " +
                                $"-an " +
                                $"-filter_complex \"split=2[out1][out2];[out1]format=rgba[outpipe];[out2]null[outnull]\" " +
                                $"-map \"[outpipe]\" -f rawvideo pipe:1";
                
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Windows Live Mode ({format}): {ffmpegArgs}");
            }
            return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
        }
    }
}
