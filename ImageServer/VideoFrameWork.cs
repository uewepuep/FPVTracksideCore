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
        IEnumerable<string> GetAudioSources();

        FrameSource CreateFrameSource(VideoConfig vc);

        FrameSource CreateFrameSource(string filename);

        Mode PickMode(IEnumerable<Mode> modes);
    }

    public static class VideoFrameWorks
    {
        public static VideoFrameWork[] Available = new VideoFrameWork[0];

        public static VideoFrameWork GetFramework(params FrameWork[] frameWorks)
        {
            List<FrameWork> frameworkList = frameWorks.ToList();

            return Available.Where(f => frameworkList.Contains(f.FrameWork)).OrderBy(f => frameworkList.IndexOf(f.FrameWork)).FirstOrDefault();
        }

        public static string GetName(this VideoFrameWork videoFrameWork)
        {
            if (videoFrameWork == null)
                return "";

            return videoFrameWork.GetType().Name;
        }
    }
}
