using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageServer
{
    public interface VideoFrameWork
    {
        FrameWork FrameWork { get; }

        IEnumerable<VideoConfig> GetVideoConfigs();

        FrameSource CreateFrameSource(VideoConfig vc);

        Mode PickMode(IEnumerable<Mode> modes);
    }

    public static class VideoFrameworks
    {
        public static VideoFrameWork[] Available = new VideoFrameWork[0];
    }
}
