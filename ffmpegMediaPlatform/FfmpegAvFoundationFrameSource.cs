using ImageServer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace FfmpegMediaPlatform
{
    public class FfmpegAvFoundationFrameSource : FfmpegFrameSource
    {
        public FfmpegAvFoundationFrameSource(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig)
            : base(ffmpegMediaFramework, videoConfig)
        {
            Direction = Directions.BottomUp;
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
            string ffmpegArgs;
            
            if (Recording && !string.IsNullOrEmpty(recordingFilename))
            {
                string recordingPath = Path.GetFullPath(recordingFilename);
                
                // Use hardware-accelerated H.264 encoding for recording - let camera use its natural framerate
                ffmpegArgs = $"-f avfoundation " +
                                $"-pixel_format uyvy422 " +
                                $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                                $"-i \"{name}\" " +
                                $"-fflags nobuffer " +
                                $"-flags low_delay " +
                                $"-strict experimental " +
                                $"-threads 1 " +
                                $"-fps_mode passthrough " +
                                $"-copyts " +
                                $"-an " +
                                $"-filter_complex \"split=2[out1][out2];[out1]format=rgba[outpipe];[out2]format=yuv420p[outfile]\" " +
                                $"-map \"[outpipe]\" -f rawvideo pipe:1 " +
                                $"-map \"[outfile]\" -c:v h264_videotoolbox -preset ultrafast -tune zerolatency -b:v 5M -f matroska -avoid_negative_ts make_zero \"{recordingPath}\"";
                
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG macOS Recording Mode: {ffmpegArgs}");
            }
            else
            {
                // Live mode: Use dual stream approach like recording mode but only output RGBA pipe
                // PERFORMANCE: Enhanced low-delay flags for 4K video to reduce 1-second startup delay
                ffmpegArgs = $"-f avfoundation " +
                                $"-pixel_format uyvy422 " +
                                $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                                $"-i \"{name}\" " +
                                $"-fflags nobuffer+fastseek+flush_packets " +
                                $"-flags low_delay " +
                                $"-avioflags direct " +
                                $"-flush_packets 1 " +
                                $"-max_delay 0 " +
                                $"-strict experimental " +
                                $"-threads 1 " +
                                $"-fps_mode passthrough " +
                                $"-copyts " +
                                $"-probesize 32 " +
                                $"-analyzeduration 0 " +
                                $"-an " +
                                $"-filter_complex \"split=2[out1][out2];[out1]format=rgba[outpipe];[out2]null[outnull]\" " +
                                $"-map \"[outpipe]\" -f rawvideo pipe:1";
                
                Tools.Logger.VideoLog.LogCall(this, $"FFMPEG macOS Live Mode (dual stream): {ffmpegArgs}");
            }
            return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
        }
    }
}
