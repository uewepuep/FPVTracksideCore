using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Timing;
using UI.Nodes;

namespace UI.Video
{
    public class VideoTimingManager : IDisposable
    {
        private TimingSystemManager timingSystemManager;
        private ChannelsGridNode channelsGridNode;

        private bool run;
        private Thread thread;

        public VideoTimingManager(TimingSystemManager timingSystemManager, ChannelsGridNode channelsGridNode)
        {
            this.timingSystemManager = timingSystemManager;
            this.channelsGridNode = channelsGridNode;

            Init();

            timingSystemManager.OnInitialise += Init;
        }

        public void Dispose()
        {
            CleanUp();
        }

        public void Init()
        {
            CleanUp();

            if (timingSystemManager.HasVideoTiming)
            {
                run = true;
                thread = new Thread(Process);
                thread.Name = "VideoTimingManager";
                thread.Start();
            }
        }

        public void CleanUp()
        {
            run = false;
            if (thread != null)
            {
                thread.Join();
                thread = null;
            }
        }

        private void Process()
        {
            long lastFrame = 0;

            while (run)
            {
                IEnumerable<VideoTimingSystem> videoTimingSystems = timingSystemManager.TimingSystems.OfType<VideoTimingSystem>();
                if (!videoTimingSystems.Any())
                {
                    Thread.Sleep(1000);
                    continue;
                }

                IEnumerable<ChannelVideoNode> videoNodes = channelsGridNode.ChannelNodes.OfType<ChannelVideoNode>();
                ChannelVideoNode first = videoNodes.FirstOrDefault();
                if (first == null)
                {
                    Thread.Sleep(1);
                    continue;
                }

                long frame = first.FrameNode.Source.FrameProcessNumber;
                if (lastFrame == frame)
                {
                    Thread.Sleep(1);
                    continue;
                }
                lastFrame = frame;

                foreach (ChannelVideoNode cvn in videoNodes)
                {
                    Color[] colorData = cvn.FrameNode.GetColorData();
                    int freq = cvn.Channel.Frequency;
                    Tools.Size size = cvn.FrameNode.Size;

                    foreach (VideoTimingSystem vts in videoTimingSystems)
                    {
                        VideoGateDetector videoGateDetector = vts.ProcessFrame(freq, colorData, size.Width, size.Height);

                        if (vts.VideoTimingSystemSettings.ShowInfo && videoGateDetector != null)
                        {
                            cvn.FrameNode.SetVideoTimingInfo((int)videoGateDetector.Current, (int)videoGateDetector.Max);
                        }
                    }
                }
            }
        }
    }
}
