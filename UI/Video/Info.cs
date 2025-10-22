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
