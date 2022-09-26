using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UI.Nodes;
using UI.Video;

namespace UI
{
    public class AutoCrashOut
    {
        private Dictionary<ChannelVideoNode, MotionDetector> toProcess;

        private Thread thread;
        private bool run;

        private EventManager eventManager;
        private ChannelsGridNode channelsGridNode;

        private bool needsClear;

        public TimeSpan RaceStartDelay { get { return TimeSpan.FromSeconds(GeneralSettings.Instance.StartDelaySeconds); } }

        private DateTime waitTill;

        public AutoCrashOut(EventManager eventManager, ChannelsGridNode channelsGridNode)
        {
            this.eventManager = eventManager;
            this.channelsGridNode = channelsGridNode;

            eventManager.RaceManager.OnRaceStart += RaceManager_OnRaceStart;

            run = true;
            toProcess = new Dictionary<ChannelVideoNode, MotionDetector>();
            thread = new Thread(Process);
            thread.Name = "Auto Crash Out";
        }
        public void Dispose()
        {
            eventManager.RaceManager.OnRaceStart -= RaceManager_OnRaceStart;

            run = false;

            if (thread != null && thread.IsAlive)
            {
                thread.Join();
                thread = null;
            }
        }

        private void RaceManager_OnRaceStart(Race race)
        {
            waitTill = DateTime.Now + RaceStartDelay;
            needsClear = true;
        }

        public void AddChannelNode(ChannelVideoNode channelNode)
        {
            if (!channelNode.FrameNode.ThumbnailEnabled || !GeneralSettings.Instance.VideoStaticDetector)
            {
                return;
            }

            lock (toProcess)
            {
                if (!toProcess.Any())
                {
                    thread.Start();
                }

                toProcess.Add(channelNode, new MotionDetector(channelNode.Channel));
            }
        }

        private void Process()
        {
            int lastFrame = 0;

            while (run)
            {
                if (!eventManager.RaceManager.RaceRunning)
                {
                    Thread.Sleep(100);
                    continue;
                }

                lock (toProcess)
                {
                    ChannelVideoNode first = toProcess.Keys.FirstOrDefault();
                    if (first == null)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    int frame = first.FrameNode.Source.FrameCount;
                    if (lastFrame == frame)
                    {
                        Thread.Sleep(1);
                        continue;
                    }
                    lastFrame = frame;

                    if (needsClear)
                    {
                        if (DateTime.Now > waitTill)
                        {
                            needsClear = false;
                            foreach (var kvp in toProcess)
                            {
                                MotionDetector motionDetector = kvp.Value;
                                motionDetector.Clear();
                            }
                        }
                        continue;
                    }

                    foreach (var kvp in toProcess)
                    {
                        if (!run)
                            break;

                        ChannelVideoNode channelNode = kvp.Key;
                        MotionDetector motionDetector = kvp.Value;

                        Color[] colors = channelNode.FrameNode.GetColorData();
                        motionDetector.AddFrame(colors);

                        if (eventManager.RaceManager.RaceRunning)
                        {
                            float motionValue;
                            bool motion;

                            motionDetector.DetectMotion(out motionValue, out motion);
                            if (motion == channelNode.CrashedOut)
                            {
                                channelsGridNode.AutomaticSetCrashed(channelNode, !motion);
                            }
                        }
                    }
                }

                if (!run)
                    break;
                Thread.Sleep(10);
            }
        }
    }
}
