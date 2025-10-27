using ImageServer;
using RaceLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Video
{

    public class RecodingInfo
    {
        public string FilePath { get; set; }
        public float ChannelCoveragePercent { get; set; }
        public FlipMirroreds FlipMirrored { get; set; }

        public VideoBounds[] ChannelBounds { get; set; }

        // Legacy conversions for single video frame times.
        public DateTime FirstFrame
        {
            get
            {
                var possibleFirstFrames = FrameTimes.Where(r => r.Frame == 1);
                if (possibleFirstFrames.Any())
                {
                    return possibleFirstFrames.First().Time;
                }

                return default(DateTime);
            }
            set
            {
                if (FrameTimes == null || !FrameTimes.Any())
                {
                    FrameTimes = new FrameTime[] { new FrameTime() { Frame = 1, Time = value, Seconds = 0.0 } };
                }
            }
        }

        public FrameTime[] FrameTimes { get; set; }

        public float DeviceLatency { get; set; }

        public FrameWork FrameWork { get; set; }


        public RecodingInfo()
        {
            FrameTimes = new FrameTime[0];
        }

        public RecodingInfo(ICaptureFrameSource captureFrameSource)
        {
            FilePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), captureFrameSource.Filename);
            ChannelCoveragePercent = captureFrameSource.VideoConfig.ChannelCoveragePercent;
            FrameTimes = captureFrameSource.FrameTimes.ToArray();
            ChannelBounds = captureFrameSource.VideoConfig.VideoBounds;
            FlipMirrored = captureFrameSource.VideoConfig.FlipMirrored;
            DeviceLatency = captureFrameSource.VideoConfig.DeviceLatency;
            FrameWork = captureFrameSource.VideoConfig.FrameWork;
        }

        public VideoConfig GetVideoConfig()
        {
            VideoConfig videoConfig = new VideoConfig();
            videoConfig.FilePath = FilePath;
            videoConfig.ChannelCoveragePercent = ChannelCoveragePercent;
            videoConfig.FrameTimes = FrameTimes;
            videoConfig.VideoBounds = ChannelBounds;
            videoConfig.FlipMirrored = FlipMirrored;
            videoConfig.Pauseable = false;
            videoConfig.DeviceLatency = DeviceLatency;
            videoConfig.FrameWork = FrameWork;
            return videoConfig;
        }
    }

    public class ChannelVideoInfo
    {
        public Channel Channel { get; set; }
        public FrameSource FrameSource { get; set; }
        public RectangleF ScaledRelativeSourceBounds { get; private set; }
        public VideoBounds VideoBounds { get; set; }

        public ChannelVideoInfo()
        {
            Channel = Channel.None;
        }

        public ChannelVideoInfo(VideoBounds videoBounds, Channel channel, FrameSource source)
        {
            Channel = channel;
            FrameSource = source;
            VideoBounds = videoBounds;

            // Scale the relative source bounds now...
            ScaledRelativeSourceBounds = VideoBounds.RelativeSourceBounds.Scale(FrameSource.VideoConfig.ChannelCoveragePercent / 100.0f);
        }

        public override string ToString()
        {
            return ScaledRelativeSourceBounds.ToString() + " " + Channel.ToString();
        }
    }
}
