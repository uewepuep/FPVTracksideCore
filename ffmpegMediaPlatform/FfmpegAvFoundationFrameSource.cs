using ImageServer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FfmpegMediaPlatform
{
    public class FfmpegAvFoundationFrameSource : FfmpegFrameSource
    {
        public FfmpegAvFoundationFrameSource(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig)
            : base(ffmpegMediaFramework, videoConfig)
        {
        }

        public override IEnumerable<Mode> GetModes()
        {
            Tools.Logger.VideoLog.LogCall(this, $"GetModes() called - querying actual camera capabilities for '{VideoConfig.DeviceName}'");
            List<Mode> supportedModes = new List<Mode>();
            
            try
            {
                // Use invalid resolution to trigger ffmpeg to output supported modes
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
                    Tools.Logger.VideoLog.LogCall(this, $"FFmpeg output: {line}");
                    
                    if (line.Contains("Supported modes:"))
                    {
                        foundSupportedModes = true;
                        continue;
                    }
                    
                    if (foundSupportedModes && line.Contains("@["))
                    {
                        // Parse lines like: "   640x480@[15.000000 30.000000]fps"
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)x(\d+)@\[([0-9.\s]+)\]fps");
                        if (match.Success)
                        {
                            int width = int.Parse(match.Groups[1].Value);
                            int height = int.Parse(match.Groups[2].Value);
                            string frameRatesStr = match.Groups[3].Value;
                            
                            // Parse frame rates like "15.000000 30.000000"
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
                                Tools.Logger.VideoLog.LogCall(this, $"✓ PARSED MODE: {width}x{height}@{frameRate}fps (Index {index})");
                                index++;
                            }
                        }
                    }
                }
                
                Tools.Logger.VideoLog.LogCall(this, $"Camera capability detection complete: {supportedModes.Count} supported modes found");
                
                if (supportedModes.Count == 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, "WARNING: No supported modes detected for camera!");
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"Final supported modes for '{VideoConfig.DeviceName}':");
                    foreach (var mode in supportedModes.OrderBy(m => m.Width * m.Height).ThenBy(m => m.FrameRate))
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"  - {mode.Width}x{mode.Height}@{mode.FrameRate}fps");
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
            // string ffmpegArgs = $"-f avfoundation  -framerate {VideoConfig.VideoMode.FrameRate} -pixel_format uyvy422 -video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} -i \"{name}\" -pix_fmt rgba -f rawvideo -";
            
            string ffmpegArgs = $"-f avfoundation " +
                                $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                                $"-pixel_format uyvy422 " +
                                $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                                $"-i \"{name}\" " +
                                $"-fflags nobuffer " +                 // Don't buffer input
                                $"-flags low_delay " +                 // Enable low latency
                                $"-strict experimental " +             // Allow experimental features
                                $"-threads 1 " +                       // Single-threaded for deterministic low latency
                                $"-vsync passthrough " +              // Don’t synchronize video frames — drop if needed
                                $"-an " +                              // Disable audio
                                $"-pix_fmt rgba " +                    // Output pixel format
                                $"-preset ultrafast " +                // If encoding later, reduce latency
                                $"-tune zerolatency " +                // Tune for latency if encoding
                                    $"-f rawvideo -";                      // Output raw video
            Tools.Logger.VideoLog.LogCall(this, $"Using direct ffmpeg with device name: {name}");
            Tools.Logger.VideoLog.LogCall(this, $"FFmpeg args: {ffmpegArgs}");
            return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
        }
    }
}
