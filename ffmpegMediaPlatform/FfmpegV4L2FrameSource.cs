using ImageServer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FfmpegMediaPlatform
{
    public class FfmpegV4L2FrameSource : FfmpegFrameSource
    {
        public FfmpegV4L2FrameSource(FfmpegMediaFramework ffmpegMediaFramework, VideoConfig videoConfig)
            : base(ffmpegMediaFramework, videoConfig)
        {
        }

        private static readonly int[] CommonFrameRates = { 24, 25, 30, 60 };

        public override IEnumerable<Mode> GetModes()
        {
            Tools.Logger.VideoLog.LogDebugCall(this, $"GetModes() called for '{VideoConfig.DeviceName}'");
            List<Mode> supportedModes = new List<Mode>();

            try
            {
                string testArgs = $"-f v4l2 -list_formats all -i \"{VideoConfig.ffmpegId}\"";

                var output = ffmpegMediaFramework.GetFfmpegText(testArgs, l =>
                    l.Contains("Compressed") || l.Contains("Raw"));

                // Output format: "[video4linux2...] Raw : yuyv422 : YUYV 4:2:2 : 1280x720 1920x1080 640x480"
                // or:            "[video4linux2...] Compressed : mjpeg : Motion-JPEG : 1280x720 1920x1080"
                var formatResolutions = new Dictionary<string, List<(int w, int h)>>();

                foreach (string line in output)
                {
                    var formatMatch = Regex.Match(line, @"(?:Raw|Compressed)\s*:\s*(\w+)\s*:.*:\s*(.+)$");
                    if (!formatMatch.Success)
                        continue;

                    string format = formatMatch.Groups[1].Value.Trim();
                    string resolutionPart = formatMatch.Groups[2].Value;

                    if (!formatResolutions.ContainsKey(format))
                        formatResolutions[format] = new List<(int, int)>();

                    foreach (Match resMatch in Regex.Matches(resolutionPart, @"(\d+)x(\d+)"))
                    {
                        int width = int.Parse(resMatch.Groups[1].Value);
                        int height = int.Parse(resMatch.Groups[2].Value);
                        formatResolutions[format].Add((width, height));
                    }
                }

                int index = 0;

                foreach (var kvp in formatResolutions)
                {
                    string format = kvp.Key;
                    var resolutions = kvp.Value;

                    if (resolutions.Count == 0)
                        continue;

                    // Probe the smallest resolution to discover what the driver caps fps to
                    var smallest = resolutions.OrderBy(r => r.w * r.h).First();
                    int maxFps = ProbeMaxFrameRate(format, smallest.w, smallest.h);

                    int[] validFps = CommonFrameRates.Where(f => f <= maxFps).ToArray();
                    if (validFps.Length == 0)
                        validFps = new[] { maxFps };

                    foreach (var (w, h) in resolutions)
                    {
                        foreach (int fps in validFps)
                        {
                            supportedModes.Add(new Mode
                            {
                                Format = format,
                                Width = w,
                                Height = h,
                                FrameRate = fps,
                                FrameWork = FrameWork.FFmpeg,
                                Index = index++
                            });
                        }
                    }
                }

                // mjpeg first, then by resolution, then by framerate
                supportedModes = supportedModes
                    .OrderBy(m => m.Format == "mjpeg" ? 0 : 1)
                    .ThenBy(m => m.Width * m.Height)
                    .ThenBy(m => m.FrameRate)
                    .ToList();

                Tools.Logger.VideoLog.LogDebugCall(this, $"GetModes() complete: {supportedModes.Count} modes for '{VideoConfig.DeviceName}'");
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }

            return supportedModes;
        }

        private int ProbeMaxFrameRate(string format, int width, int height)
        {
            try
            {
                string probeArgs = $"-f v4l2 -framerate 10000 -video_size {width}x{height} -input_format {format} -i \"{VideoConfig.ffmpegId}\"";

                var lines = ffmpegMediaFramework.GetFfmpegText(probeArgs, l =>
                    l.Contains("time per frame"));

                foreach (string line in lines)
                {
                    // "The driver changed the time per frame from 1/10000 to 1/30"
                    var match = Regex.Match(line, @"to\s+1/(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int fps))
                    {
                        Tools.Logger.VideoLog.LogDebugCall(this, $"ProbeMaxFrameRate({format} {width}x{height}): max={fps}fps");
                        return fps;
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Logger.VideoLog.LogException(this, ex);
            }

            Tools.Logger.VideoLog.LogDebugCall(this, $"ProbeMaxFrameRate({format} {width}x{height}): no driver message, falling back to 30fps");
            return 30;
        }

        protected override ProcessStartInfo GetProcessStartInfo()
        {
            string name = VideoConfig.ffmpegId;
            string format = VideoConfig.VideoMode?.Format ?? "yuyv422";

            List<string> filters = new List<string>();
            if (VideoConfig.Flipped)
                filters.Add("vflip");
            if (VideoConfig.Mirrored)
                filters.Add("hflip");

            string videoFilter = filters.Any() ? string.Join(",", filters) + "," : "";
            string ffmpegArgs;

            if (Recording && !string.IsNullOrEmpty(recordingFilename))
            {
                string recordingPath = Path.GetFullPath(recordingFilename);

                ffmpegArgs = $"-f v4l2 " +
                             $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                             $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                             $"-input_format {format} " +
                             $"-i \"{name}\" " +
                             $"-fflags nobuffer " +
                             $"-flags low_delay " +
                             $"-threads 4 " +
                             $"-fps_mode passthrough " +
                             $"-copyts " +
                             $"-an " +
                             $"-filter_complex \"[0:v]{videoFilter}split=2[out1][out2];[out1]format=rgba[outpipe];[out2]format=yuv420p[outfile]\" " +
                             $"-map \"[outpipe]\" -f rawvideo pipe:1 " +
                             $"-map \"[outfile]\" -c:v libx264 -preset ultrafast -tune zerolatency -b:v 5M -f matroska -avoid_negative_ts make_zero \"{recordingPath}\"";

                Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG Linux Recording Mode (filters: {videoFilter}): {ffmpegArgs}");
            }
            else
            {
                ffmpegArgs = $"-f v4l2 " +
                             $"-framerate {VideoConfig.VideoMode.FrameRate} " +
                             $"-video_size {VideoConfig.VideoMode.Width}x{VideoConfig.VideoMode.Height} " +
                             $"-input_format {format} " +
                             $"-i \"{name}\" " +
                             $"-fflags nobuffer+fastseek+flush_packets " +
                             $"-flags low_delay " +
                             $"-avioflags direct " +
                             $"-flush_packets 1 " +
                             $"-max_delay 0 " +
                             $"-threads 4 " +
                             $"-fps_mode passthrough " +
                             $"-copyts " +
                             $"-probesize 32 " +
                             $"-analyzeduration 0 " +
                             $"-an " +
                             $"-vf \"{videoFilter}format=rgba\" " +
                             $"-f rawvideo pipe:1";

                Tools.Logger.VideoLog.LogDebugCall(this, $"FFMPEG Linux Live Mode (filters: {videoFilter}): {ffmpegArgs}");
            }

            return ffmpegMediaFramework.GetProcessStartInfo(ffmpegArgs);
        }
    }
}
