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
        private Dictionary<Channel, bool> channelHasMotion;

        private Thread thread;
        private bool run;

        private EventManager eventManager;
        private ChannelsGridNode channelsGridNode;

        private bool needsClear;

        public TimeSpan RaceStartDelay { get { return TimeSpan.FromSeconds(ApplicationProfileSettings.Instance.StartDelaySeconds); } }

        private DateTime waitTill;

        public bool Enabled { get { return ApplicationProfileSettings.Instance.VideoStaticDetector; } }

        public AutoCrashOut(EventManager eventManager, ChannelsGridNode channelsGridNode)
        {
            this.eventManager = eventManager;
            this.channelsGridNode = channelsGridNode;

            eventManager.RaceManager.OnRaceChanged += RaceManager_OnRaceChanged;
            eventManager.RaceManager.OnRaceStart += RaceManager_OnRaceStart;

            run = true;
            channelHasMotion = new Dictionary<Channel, bool>();
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

        public bool HasMotion(Channel channel)
        {
            lock (channelHasMotion)
            {
                bool motion;
                if (channelHasMotion.TryGetValue(channel, out motion))
                {
                    return motion;
                }
            }
            return true;
        }


        private void RaceManager_OnRaceChanged(Race race)
        {
            waitTill = DateTime.MaxValue;
            needsClear = true;
        }

        private void RaceManager_OnRaceStart(Race race)
        {
            waitTill = DateTime.Now + RaceStartDelay;
        }

        public void AddChannelNode(ChannelVideoNode channelNode)
        {
            if (!channelNode.FrameNode.ThumbnailEnabled || !Enabled)
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
            try
            {
                int lastFrame = 0;

                while (run)
                {
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
                            needsClear = false;
                            foreach (var kvp in toProcess)
                            {
                                MotionDetector motionDetector = kvp.Value;
                                motionDetector.Clear();
                            }
                            lock (channelHasMotion)
                            {
                                channelHasMotion.Clear();
                            }
                        }

                        foreach (var kvp in toProcess)
                        {
                            if (!run)
                                break;

                            ChannelVideoNode channelNode = kvp.Key;
                            MotionDetector motionDetector = kvp.Value;

                            Color[] colors = channelNode.FrameNode.GetColorData();
                            if (colors == null)
                                continue;

                            motionDetector.AddFrame(colors);

                            float motionValue;
                            bool motion;
                            motionDetector.DetectMotion(out motionValue, out motion);

                            lock (channelHasMotion)
                            {
                                if (channelHasMotion.ContainsKey(channelNode.Channel))
                                {
                                    channelHasMotion[channelNode.Channel] = motion;
                                }
                                else
                                {
                                    channelHasMotion.Add(channelNode.Channel, motion);
                                }
                            }

                            if (eventManager.RaceManager.RaceRunning && DateTime.Now > waitTill)
                            {
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
            catch (Exception e) 
            {
                Tools.Logger.VideoLog.LogException(this, e);
            }
        }
    }
}
