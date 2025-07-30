using ImageServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics;

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
                
                IEnumerable<string> modes = ffmpegMediaFramework.GetFfmpegText(ffmpegListCommand, l => l.Contains("pixel_format"));

                int index = 0;
                //[dshow @ 000001ccc05aa180]   pixel_format=nv12  min s=1280x720 fps=30 max s=1280x720 fps=30
                foreach (string format in modes)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG OUTPUT: {format}");
                    
                    string pixelFormat = ffmpegMediaFramework.GetValue(format, "pixel_format");
                    string size = ffmpegMediaFramework.GetValue(format, "min s");
                    string fps = ffmpegMediaFramework.GetValue(format, "fps");

                    string[] sizes = size.Split("x");
                    if (int.TryParse(sizes[0], out int x) && int.TryParse(sizes[1], out int y) && float.TryParse(fps, out float ffps))
                    {
                        // Prefer uyvy422 if available (like Mac), otherwise use what camera supports
                        string formatToUse = pixelFormat == "uyvy422" ? "uyvy422" : pixelFormat;
                        var mode = new Mode { Format = formatToUse, Width = x, Height = y, FrameRate = ffps, FrameWork = FrameWork.ffmpeg, Index = index };
                        supportedModes.Add(mode);
                        Tools.Logger.VideoLog.LogCall(this, $"FFMPEG ✓ PARSED MODE: {x}x{y}@{ffps}fps ({formatToUse}) (Index {index})");
                        index++;
                    }
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
            
            // Use the working command structure without pixel_format specification (let camera use native format)
            string ffmpegArgs = $"-f dshow " +
                                $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                                $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                                $"-rtbufsize 10M " +                    // DirectShow buffer management
                                $"-i video=\"{name}\" " +
                                $"-fflags nobuffer " +                 // Don't buffer input
                                $"-flags low_delay " +                 // Enable low latency
                                $"-strict experimental " +             // Allow experimental features
                                $"-threads 1 " +                       // Single-threaded for deterministic low latency
                                $"-vsync passthrough " +              // Don't synchronize video frames — drop if needed
                                $"-an " +                              // Disable audio
                                $"-pix_fmt rgba " +                    // Output pixel format
                                $"-preset ultrafast " +                // If encoding later, reduce latency
                                $"-tune zerolatency " +                // Tune for latency if encoding
                                $"-f rawvideo -";                      // Output raw video
                                
            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Using direct ffmpeg with device name: {name}");
            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG CONVERSION PIPELINE: Camera native format → RGBA (rawvideo)");
            Tools.Logger.VideoLog.LogCall(this, $"FFMPEG COMMAND (start stream): ffmpeg {ffmpegArgs}");
            return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
        }
    }
}
