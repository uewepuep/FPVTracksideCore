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

        IEnumerable<string> GetFileExtensions();

        bool NeedsInstall { get; }
        void Install(); 
    }

    public static class VideoFrameWorks
    {
        public static List<VideoFrameWork> Available = new List<VideoFrameWork>();

        public static VideoFrameWork GetFramework(FrameWork frameWork)
        {
            return Available.FirstOrDefault(f => f.FrameWork == frameWork);
        }

        public static int IndexOf(FrameWork frameWork)
        {
            int index = 0;
            foreach (VideoFrameWork a in Available)
            {
                if (a.FrameWork == frameWork)
                    return index;
                index++;
            }

            return int.MaxValue;
        }
    }
}
