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
        private Dictionary<Channel, MotionState> channelHasMotion;
        private Dictionary<Channel, Queue<(DateTime Time, MotionState State)>> channelMotionHistory;
        private DateTime nextHistorySample;
        private static readonly TimeSpan HistoryLength = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan HistorySampleRate = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan CrashDuration = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RecoverDuration = TimeSpan.FromSeconds(3);

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

            eventManager.RaceManager.OnRaceStart += RaceManager_OnRaceStart;
            eventManager.RaceManager.OnRaceEnd += RaceManager_OnRaceEnd;

            run = true;
            channelHasMotion = new Dictionary<Channel, MotionState>();
            channelMotionHistory = new Dictionary<Channel, Queue<(DateTime, MotionState)>>();
            nextHistorySample = DateTime.Now + HistorySampleRate;
            toProcess = new Dictionary<ChannelVideoNode, MotionDetector>();
            thread = new Thread(Process);
            thread.Name = "Auto Crash Out";
        }

        public void Dispose()
        {
            eventManager.RaceManager.OnRaceEnd -= RaceManager_OnRaceEnd;
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
            MotionState state = GetMotionState(channel);
            return state == MotionState.ActiveMotion || state == MotionState.InactiveMotion || state == MotionState.Unknown;
        }

        public bool HasMotionFor(Channel channel, TimeSpan duration)
        {
            IEnumerable<MotionState> states = GetMotionStatesFor(channel, duration);
            return states.Any() && states.All(s => s == MotionState.ActiveMotion || s == MotionState.InactiveMotion);
        }

        public bool HasNoMotionFor(Channel channel, TimeSpan duration)
        {
            IEnumerable<MotionState> states = GetMotionStatesFor(channel, duration);
            return states.Any() && states.All(s => s == MotionState.ActiveNoMotion || s == MotionState.InactiveNoMotion);
        }

        public bool IsActive(Channel channel)
        {
            MotionState state = GetMotionState(channel);
            return state == MotionState.ActiveMotion || state == MotionState.ActiveNoMotion || state == MotionState.Unknown;
        }

        public bool IsActiveFor(Channel channel, TimeSpan duration)
        {
            IEnumerable<MotionState> states = GetMotionStatesFor(channel, duration);
            return states.Any() && states.All(s => s == MotionState.ActiveMotion || s == MotionState.ActiveNoMotion);
        }

        public bool IsInactiveFor(Channel channel, TimeSpan duration)
        {
            IEnumerable<MotionState> states = GetMotionStatesFor(channel, duration);
            return states.Any() && states.All(s => s == MotionState.InactiveMotion || s == MotionState.InactiveNoMotion);
        }

        public void Update()
        {
            if (!eventManager.RaceManager.RaceRunning)
                return;

            bool inStartDelay = DateTime.Now <= waitTill;

            KeyValuePair<ChannelVideoNode, MotionDetector>[] temp;
            lock (toProcess)
            {
                temp = toProcess.ToArray();
            }

            foreach (KeyValuePair<ChannelVideoNode, MotionDetector> kvp in temp)
            {
                ChannelVideoNode channelNode = kvp.Key;
                Channel channel = channelNode.Channel;

                if (channelNode.CrashedOut)
                {
                    if (IsActive(channel))
                    {
                        if (HasMotionFor(channel, RecoverDuration))
                        {
                            channelsGridNode.AutomaticSetCrashed(channelNode, false);
                        }
                    }
                }
                else if (!inStartDelay)
                {
                    if (HasNoMotionFor(channel, CrashDuration))
                    {
                        channelsGridNode.AutomaticSetCrashed(channelNode, true);
                    }
                }
            }
        }

        private IEnumerable<MotionState> GetMotionStatesFor(Channel channel, TimeSpan duration)
        {
            DateTime cutoff = DateTime.Now - duration;
            lock (channelMotionHistory)
            {
                if (!channelMotionHistory.TryGetValue(channel, out Queue<(DateTime Time, MotionState State)> queue) || !queue.Any() || queue.Peek().Time > cutoff)
                    return Enumerable.Empty<MotionState>();

                return queue.Where(s => s.Time >= cutoff).Select(s => s.State).ToList();
            }
        }



        private void RaceManager_OnRaceEnd(Race race)
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
            needsClear = true;
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
                        nextHistorySample = DateTime.Now + HistorySampleRate;
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
                        nextHistorySample = DateTime.Now + HistorySampleRate;
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

                        MotionState state = motionDetector.DetectMotion(out float saturationValue, out float diffValue);
                        if (state != MotionState.Unknown)
                        {
                            lock (channelHasMotion)
                            {
                                channelHasMotion[channelNode.Channel] = state;
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
