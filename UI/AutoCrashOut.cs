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
    public enum MotionState
    {
        Unknown,
        Static,
        Active
    }

    public class AutoCrashOut
    {
        private Dictionary<ChannelVideoNode, MotionDetector> toProcess;
        private Dictionary<Channel, MotionState> channelHasMotion;
        private Dictionary<Channel, Queue<(DateTime Time, MotionState State)>> channelMotionHistory;
        private DateTime nextHistorySample;
        private static readonly TimeSpan HistoryLength = TimeSpan.FromSeconds(30);

        private Thread thread;
        private volatile bool run;

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
            channelHasMotion = new Dictionary<Channel, MotionState>();
            channelMotionHistory = new Dictionary<Channel, Queue<(DateTime, MotionState)>>();
            nextHistorySample = DateTime.Now + TimeSpan.FromSeconds(1);
            toProcess = new Dictionary<ChannelVideoNode, MotionDetector>();
            thread = new Thread(Process);
            thread.Name = "Auto Crash Out";
        }

        public void Dispose()
        {
            eventManager.RaceManager.OnRaceChanged -= RaceManager_OnRaceChanged;
            eventManager.RaceManager.OnRaceStart -= RaceManager_OnRaceStart;

            run = false;

            if (thread != null && thread.IsAlive)
            {
                thread.Join();
                thread = null;
            }
        }

        public MotionState GetMotionState(Channel channel)
        {
            lock (channelHasMotion)
            {
                if (channelHasMotion.TryGetValue(channel, out MotionState state))
                    return state;
            }
            return MotionState.Unknown;
        }

        public bool HasMotion(Channel channel)
        {
            return GetMotionState(channel) != MotionState.Static;
        }

        public bool HasMotionFor(Channel channel, TimeSpan duration)
        {
            return GetMotionStateFor(channel, duration) == MotionState.Active;
        }

        public bool IsStaticFor(Channel channel, TimeSpan duration)
        {
            return GetMotionStateFor(channel, duration) == MotionState.Static;
        }

        public MotionState GetMotionStateFor(Channel channel, TimeSpan duration)
        {
            DateTime cutoff = DateTime.Now - duration;
            lock (channelMotionHistory)
            {
                if (!channelMotionHistory.TryGetValue(channel, out var queue) || !queue.Any() || queue.Peek().Time > cutoff)
                    return MotionState.Unknown;

                var relevant = queue.Where(s => s.Time >= cutoff).ToList();
                if (relevant.All(s => s.State == MotionState.Active))
                    return MotionState.Active;

                if (relevant.All(s => s.State == MotionState.Static))
                    return MotionState.Static;
                return MotionState.Unknown;
            }
        }

        private void RaceManager_OnRaceChanged(Race race)
        {
            waitTill = DateTime.MaxValue;

            if (race == null || !race.Running)
            {
                needsClear = true;
            }
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
                long lastFrame = 0;

                while (run)
                {
                    KeyValuePair<ChannelVideoNode, MotionDetector>[] temp;
                    lock (toProcess)
                    {
                        temp = toProcess.ToArray();
                    }

                    if (!temp.Any())
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    if (DateTime.Now >= nextHistorySample)
                    {
                        nextHistorySample = DateTime.Now + TimeSpan.FromSeconds(1);
                        DateTime now = DateTime.Now;
                        DateTime cutoff = now - HistoryLength;

                        Dictionary<Channel, MotionState> snapshot;
                        lock (channelHasMotion)
                        {
                            snapshot = new Dictionary<Channel, MotionState>(channelHasMotion);
                        }

                        lock (channelMotionHistory)
                        {
                            foreach (var kvp in snapshot)
                            {
                                if (!channelMotionHistory.TryGetValue(kvp.Key, out var queue))
                                {
                                    queue = new Queue<(DateTime, MotionState)>();
                                    channelMotionHistory[kvp.Key] = queue;
                                }
                                queue.Enqueue((now, kvp.Value));

                                while (queue.Count > 0 && queue.Peek().Time < cutoff)
                                    queue.Dequeue();
                            }
                        }
                    }

                    ChannelVideoNode first = temp.FirstOrDefault().Key;

                    long frame = first.FrameNode.Source?.FrameProcessNumber ?? 0;
                    if (lastFrame == frame)
                    {
                        Thread.Sleep(1);
                        continue;
                    }
                    lastFrame = frame;

                    if (needsClear)
                    {
                        needsClear = false;
                        foreach (KeyValuePair<ChannelVideoNode, MotionDetector> kvp in temp)
                        {
                            kvp.Value.Clear();
                        }
                        lock (channelHasMotion)
                        {
                            channelHasMotion.Clear();
                        }
                        lock (channelMotionHistory)
                        {
                            channelMotionHistory.Clear();
                        }
                        nextHistorySample = DateTime.Now + TimeSpan.FromSeconds(1);
                    }

                    foreach (KeyValuePair<ChannelVideoNode, MotionDetector> kvp in temp)
                    {
                        if (!run)
                            break;

                        ChannelVideoNode channelNode = kvp.Key;
                        MotionDetector motionDetector = kvp.Value;

                        Color[] colors = channelNode.FrameNode.GetColorData();
                        if (colors == null)
                            continue;

                        motionDetector.AddFrame(colors);

                        if (motionDetector.DetectMotion(out float motionValue, out bool motion))
                        {
                            MotionState state = motion ? MotionState.Active : MotionState.Static;
                            lock (channelHasMotion)
                            {
                                channelHasMotion[channelNode.Channel] = state;
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
