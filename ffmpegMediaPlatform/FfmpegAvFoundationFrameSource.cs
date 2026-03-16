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
        }

        public override IEnumerable<Mode> GetModes()
        {
            Tools.Logger.VideoLog.LogDebugCall(this, $"GetModes() called for '{VideoConfig.DeviceName}'");
            List<Mode> supportedModes = new List<Mode>();

            try
            {
                // Use invalid resolution to trigger ffmpeg to output supported modes
                string testArgs = $"-f avfoundation -framerate 30 -video_size 1234x5678 -i \"{VideoConfig.DeviceName}\"";

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
                                    FrameWork = FrameWork.FFmpeg,
                                    Index = index,
                                    Format = "uyvy422"
                                };
                                supportedModes.Add(mode);
                                index++;
                            }
                        }
                    }
                }
                
                Tools.Logger.VideoLog.LogDebugCall(this, $"GetModes() complete: {supportedModes.Count} modes for '{VideoConfig.DeviceName}'");
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

                // Use hardware-accelerated H.264 encoding for recording - let camera use its natural framerate
                ffmpegArgs = $"-f avfoundation " +
                                $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                                $"-pixel_format uyvy422 " +
                                $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                                $"-i \"{name}\" " +
                                $"-fflags nobuffer " +
                                $"-flags low_delay " +
                                $"-strict experimental " +
                                $"-threads 4 " +
                                $"-fps_mode passthrough " +
                                $"-copyts " +
                                $"-an " +
                                $"-filter_complex \"[0:v]{videoFilter}split=2[out1][out2];[out1]format=rgba[outpipe];[out2]format=yuv420p[outfile]\" " +
                                $"-map \"[outpipe]\" -f rawvideo pipe:1 " +
                                $"-map \"[outfile]\" -c:v h264_videotoolbox -preset ultrafast -tune zerolatency -b:v 5M -f matroska -avoid_negative_ts make_zero \"{recordingPath}\"";

                Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG macOS Recording Mode (filters: {videoFilter}): {ffmpegArgs}");
            }
            else
            {
                // Live mode: Use dual stream approach like recording mode but only output RGBA pipe
                // PERFORMANCE: Enhanced low-delay flags for 4K video to reduce 1-second startup delay
                ffmpegArgs = $"-f avfoundation " +
                                $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                                $"-pixel_format uyvy422 " +
                                $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                                $"-i \"{name}\" " +
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
                                $"-vf \"{videoFilter}format=rgba\" " +
                                $"-f rawvideo pipe:1";

                Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG macOS Live Mode (filters: {videoFilter}): {ffmpegArgs}");
            }
            return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
        }
    }
}
