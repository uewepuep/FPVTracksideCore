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
                
                IEnumerable<string> modes = ffmpegMediaFramework.GetFfmpegText(ffmpegListCommand, l => l.Contains("pixel_format") || l.Contains("vcodec="));

                int index = 0;
                //[dshow @ 000001ccc05aa180]   pixel_format=nv12  min s=1280x720 fps=30 max s=1280x720 fps=30
                foreach (string format in modes)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"FFMPEG OUTPUT: {format}");
                    
                    string pixelFormat = ffmpegMediaFramework.GetValue(format, "pixel_format");
                    if (string.IsNullOrEmpty(pixelFormat))
                    {
                        pixelFormat = ffmpegMediaFramework.GetValue(format, "vcodec");
                    }
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
            string ffmpegArgs;
            
            if (Recording && !string.IsNullOrEmpty(recordingFilename))
            {
                string recordingPath = Path.GetFullPath(recordingFilename);
                
                // Use hardware-accelerated H.264 encoding for Windows (try NVENC first, fallback to software)
                ffmpegArgs = $"-f dshow " +
                                $"-rtbufsize 2048M " +
                                $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                                $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
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
                
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Windows Recording Mode: {ffmpegArgs}");
            }
            else
            {
                // Live mode: Use dual stream approach like recording mode but only output RGBA pipe
                ffmpegArgs = $"-f dshow " +
                                $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                                $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
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
                
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG Windows Live Mode (dual stream): {ffmpegArgs}");
            }
            return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
        }
    }
}
