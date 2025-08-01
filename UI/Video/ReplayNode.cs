using Composition;
using Composition.Input;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Media;
using RaceLib;
using RaceLib.Game;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;
using UI.Nodes;

namespace UI.Video
{
    public class ReplayNode : Node, IUpdateableNode
    {
        public ChannelsGridNode ChannelsGridNode { get; private set; }

        public EventManager EventManager { get; private set; }
        public VideoManager PlaybackVideoManager { get; private set; }

        public SeekNode SeekNode { get; private set; }

        private const float seekBarHeight = 0.05f;

        private IPlaybackFrameSource primary;
        private Race race;

        private DateTime minStart;
        private DateTime maxEnd;
        
        private DateTime? lastSeekTime;
        private DateTime lastSeekTimeSet;

        public bool Active
        {
            get
            {
                return race != null;
            }
        }

        public DateTime CurrentTime
        {
            get
            {
                if (race == null || SeekNode == null)
                    return DateTime.Now;

                return SeekNode.CurrentTime;
            }
        }

        public TimeSpan ElapsedTime 
        {
            get
            {
                if (race == null || SeekNode == null) 
                    return TimeSpan.Zero;

                return SeekNode.CurrentTime - race.Start;
            }
        }

        public TimeSpan RemainingTime
        {
            get
            {
                if (race == null || SeekNode == null)
                    return TimeSpan.Zero;

                return race.End - SeekNode.CurrentTime;
            }
        }

        private KeyboardShortcuts keyMapper;

        public ReplayNode(EventManager eventManager, KeyboardShortcuts keyMapper)
        {
            EventManager = eventManager;
            this.keyMapper = keyMapper;

            EventManager.RaceManager.OnLapDetected += OnChange;
            EventManager.RaceManager.OnLapsRecalculated += OnChange;
            EventManager.RaceManager.OnLapDisqualified += OnChange;
            EventManager.GameManager.OnGamePointChanged += OnChange;


            SeekNode = new SeekNode(eventManager, Theme.Current.Replay.Text.XNA);
            SeekNode.RelativeBounds = new RectangleF(0.1f, 1 - seekBarHeight, 0.8f, seekBarHeight);
            SeekNode.Seek += Seek;
            SeekNode.PlayButton.OnClick += (m) => { Play(); };
            SeekNode.StopButton.OnClick += (m) => { Stop(); };
            SeekNode.SlowCheck.Checkbox.ValueChanged += Checkbox_ValueChanged;
            SeekNode.ShowAll.OnClick += ShowAll_OnClick;

            AddChild(SeekNode);
        }

        private void OnChange(Race race)
        {
            OnChange();
        }

        private void OnChange(Lap lap)
        {
            OnChange();
        }

        private void OnChange(GamePoint gamePoint)
        {
            OnChange();
        }

        private void OnChange()
        {
            if (primary != null && race != null)
            {
                // Recalculate timeline to ensure consistency
                var frameSources = GetFileFrameSources();
                if (frameSources.Any())
                {
                    minStart = frameSources.Select(r => r.StartTime).Min();
                    
                    // Calculate maxEnd using the same XML frame timing method as video duration
                    var maxEndDuration = TimeSpan.Zero;
                    foreach (var frameSource in frameSources)
                    {
                        // If this frame source has frame timing data, use XML timing
                        if (frameSource is ICaptureFrameSource captureSource && captureSource.FrameTimes != null && captureSource.FrameTimes.Length > 0)
                        {
                            var sourceFrameTimes = captureSource.FrameTimes;
                            var firstFrame = sourceFrameTimes.First();
                            var lastFrame = sourceFrameTimes.Last();
                            var xmlDuration = lastFrame.Time - firstFrame.Time;
                            maxEndDuration = TimeSpan.FromSeconds(Math.Max(maxEndDuration.TotalSeconds, xmlDuration.TotalSeconds));
                        }
                        else
                        {
                            // Fallback to Length property
                            maxEndDuration = TimeSpan.FromSeconds(Math.Max(maxEndDuration.TotalSeconds, frameSource.Length.TotalSeconds));
                        }
                    }
                    maxEnd = minStart + maxEndDuration;
                }
                
                // Get frame times for timeline positioning
                var frameTimes = GetFrameTimesFromFrameSources();
                SeekNode.SetRace(race, minStart, maxEnd, frameTimes);
                ChannelsGridNode.SetPlaybackTime(race.Start);
            }
        }

        private void Checkbox_ValueChanged(bool slowMotion)
        {
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                frameSource.PlaybackSpeed = slowMotion ? PlaybackSpeed.Slow : PlaybackSpeed.Normal;
            }
        }

        public override void Dispose()
        {
            EventManager.RaceManager.OnLapDetected -= OnChange;
            EventManager.RaceManager.OnLapsRecalculated -= OnChange;
            EventManager.RaceManager.OnLapDisqualified -= OnChange;

            base.Dispose();
            CleanUp();
        }

        private void Stop()
        {
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                frameSource.Pause();
            }

            SeekNode.PlayButton.Visible = true;
            SeekNode.StopButton.Visible = false;

            RequestLayout();
        }

        private void Play()
        {
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                // Check if we can access the state by casting to FrameSource
                if (frameSource is FrameSource fs && fs.State == FrameSource.States.Paused)
                {
                    frameSource.Play();
                }
                else
                {
                    // If not paused or can't check state, restart from current position
                    DateTime currentTime = SeekNode.CurrentTime;
                    frameSource.SetPosition(currentTime);
                    frameSource.Play();
                }
            }

            SeekNode.PlayButton.Visible = false;
            SeekNode.StopButton.Visible = true;

            RequestLayout();
        }

        public void Seek(DateTime seekTime)
        {
            Tools.Logger.VideoLog.LogCall(this, $"ReplayNode.Seek called with seekTime: {seekTime:HH:mm:ss.fff}");
            
            // Track the seek operation to prevent Update from overriding the position immediately
            lastSeekTime = seekTime;
            lastSeekTimeSet = DateTime.Now;
            
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                Tools.Logger.VideoLog.LogCall(this, $"ReplayNode.Seek calling SetPosition on frameSource: {frameSource.GetType().Name}");
                frameSource.SetPosition(seekTime);
                
                // If the video is currently playing (stop button visible), continue playing after seek
                if (SeekNode.StopButton.Visible)
                {
                    frameSource.Play();
                }
            }
            
            // Update the seek node's current time to reflect the new position
            SeekNode.CurrentTime = seekTime;
            SeekNode.RequestLayout();
            Tools.Logger.VideoLog.LogCall(this, $"ReplayNode.Seek updated SeekNode.CurrentTime to: {seekTime:HH:mm:ss.fff}");
        }

        public void PrevFrame()
        {
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                frameSource.PrevFrame();
            }
        }

        public void NextFrame()
        {
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                frameSource.NextFrame();
            }
        }

        public void Step(int steps)
        {
            float frameRate = (float)Math.Max(1, Math.Min(120, primary.FrameRate));
            double step = 1.0 / frameRate;

            TimeSpan seekTime = primary.MediaTime + TimeSpan.FromSeconds(step * steps);
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                frameSource.SetPosition(seekTime);
            }
        }

        private IEnumerable<IPlaybackFrameSource> GetFileFrameSources()
        {
            if (PlaybackVideoManager != null)
            {
                return PlaybackVideoManager.GetFrameSources().OfType<IPlaybackFrameSource>();
            }
            return new IPlaybackFrameSource[0];
        }

        public void CleanUp()
        {
            if (ChannelsGridNode != null)
            {
                ChannelsGridNode.Dispose();
                ChannelsGridNode = null;
            }

            if (PlaybackVideoManager != null)
            {
                PlaybackVideoManager.Dispose();
                PlaybackVideoManager = null;
            }
            primary = null;
            race = null;
        }

        public bool ReplayRace(Race race)
        {
            try
            {
                CleanUp();
                SeekNode.ClearFlags();

                this.race = race;

                PlaybackVideoManager = VideoManagerFactory.CreateVideoManager();
                PlaybackVideoManager.OnStart += PlaybackVideoManager_OnStart;

                ChannelsGridNode = new ChannelsGridNode(EventManager, PlaybackVideoManager);
                ChannelsGridNode.RelativeBounds = new RectangleF(0, 0, 1, 1 - seekBarHeight);
                AddChild(ChannelsGridNode);

                PlaybackVideoManager.LoadRecordings(race, (fs) =>
                {
                    IEnumerable<IPlaybackFrameSource> frameSources = fs.OfType<IPlaybackFrameSource>();

                    if (frameSources.Any())
                    {
                        minStart = frameSources.Select(r => r.StartTime).Min();
                        
                        // Calculate maxEnd using the same XML frame timing method as video duration
                        // This ensures progress bar timeline matches video duration calculation
                        var maxEndDuration = TimeSpan.Zero;
                        foreach (var frameSource in frameSources)
                        {
                            // If this frame source has frame timing data, use XML timing
                            if (frameSource is ICaptureFrameSource captureSource && captureSource.FrameTimes != null && captureSource.FrameTimes.Length > 0)
                            {
                                var sourceFrameTimes = captureSource.FrameTimes;
                                var firstFrame = sourceFrameTimes.First();
                                var lastFrame = sourceFrameTimes.Last();
                                var xmlDuration = lastFrame.Time - firstFrame.Time;
                                
                                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Timeline: Using XML frame timing duration {xmlDuration.TotalSeconds:F1}s for {frameSource.GetType().Name}");
                                maxEndDuration = TimeSpan.FromSeconds(Math.Max(maxEndDuration.TotalSeconds, xmlDuration.TotalSeconds));
                            }
                            else
                            {
                                // Fallback to Length property
                                maxEndDuration = TimeSpan.FromSeconds(Math.Max(maxEndDuration.TotalSeconds, frameSource.Length.TotalSeconds));
                            }
                        }
                        
                        maxEnd = minStart + maxEndDuration;
                        Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Timeline: minStart={minStart:HH:mm:ss.fff}, maxEnd={maxEnd:HH:mm:ss.fff}, duration={maxEndDuration.TotalSeconds:F1}s");
                    }
                    else
                    {
                        minStart = DateTime.MaxValue;
                        maxEnd = DateTime.MinValue;
                    }

                    foreach (IPlaybackFrameSource fileFrameSource in frameSources)
                    {
                        fileFrameSource.Mute();
                    }

                    // show everything in video playback.
                    foreach (FrameSource frameSource in frameSources)
                    {
                        foreach(VideoBounds videoBounds in frameSource.VideoConfig.VideoBounds)
                        {
                            if (videoBounds.SourceType == SourceTypes.Commentators 
                             || videoBounds.SourceType == SourceTypes.Launch 
                             || videoBounds.SourceType == SourceTypes.FinishLine)
                            {
                                videoBounds.ShowInGrid = true;
                            }
                        }
                    }

                    ChannelsGridNode.FillChannelNodes();
                    ChannelsGridNode.MakeExtrasVisible(true);

                    foreach (PilotChannel pilotChannel in race.PilotChannelsSafe)
                    {
                        ChannelNodeBase cbn = ChannelsGridNode.AddPilot(pilotChannel);
                        cbn.OnCloseClick += () => { Hide(cbn); };
                        cbn.OnCrashedOutClick += () => { Hide(cbn); };
                    }


                    // Get frame times for timeline positioning
                    var frameTimes = GetFrameTimesFromFrameSources();
                    SeekNode.SetRace(race, minStart, maxEnd, frameTimes);

                    RequestLayout();
                });

                SeekNode.PlayButton.Visible = false;
                SeekNode.StopButton.Visible = true;
                SeekNode.SlowCheck.Visible = true;
                SeekNode.ShowAll.Visible = false;
                SeekNode.SlowCheck.Checkbox.Value = false;


                return true;
            }
            catch (Exception e)
            {
                Logger.VideoLog.LogException(this, e);
                CleanUp();

                return false;
            }
        }

        private void PlaybackVideoManager_OnStart(FrameSource obj)
        {
            if (primary == null)
            {
                primary = (IPlaybackFrameSource)obj;
            }
            PlaybackVideoManager.OnStart -= PlaybackVideoManager_OnStart;
        }

        private void Hide(ChannelNodeBase cbn)
        {
            cbn.Visible = false;
            SeekNode.ShowAll.Visible = true;
            ChannelsGridNode.Reorder(true);
        }

        private void ShowAll_OnClick(MouseInputEvent mie)
        {
            SeekNode.ShowAll.Visible = false;

            foreach (ChannelNodeBase cbn in ChannelsGridNode.ChannelNodes)
            {
                if (cbn.Pilot != null)
                {
                    cbn.Visible = true;
                    cbn.SetCrashedOutType(ChannelNodeBase.CrashState.ManualUp);
                }
            }
            ChannelsGridNode.Reorder();
        }

        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            if (inputEvent.ButtonState == ButtonStates.Pressed)
            {
                if (keyMapper.ReplayPlayStop.Match(inputEvent))
                {
                    if (SeekNode.PlayButton.Visible)
                    {
                        Play();
                    }
                    else
                    {
                        Stop();
                    }
                }

                if (SeekNode.PlayButton.Visible)
                {
                    if (keyMapper.ReplayNextFrame.Match(inputEvent))
                    {
                        NextFrame();
                    }
                    if (keyMapper.ReplayPrevFrame.Match(inputEvent))
                    {
                        PrevFrame();
                    }
                }

                if (primary != null)
                {
                    if (keyMapper.ReplayPlus5Seconds.Match(inputEvent))
                    {
                        Seek(primary.CurrentTime + TimeSpan.FromSeconds(5));
                    }

                    if (keyMapper.ReplayMinus5Seconds.Match(inputEvent))
                    {
                        Seek(primary.CurrentTime + TimeSpan.FromSeconds(-5));
                    }
                }
                
            }
            return base.OnKeyboardInput(inputEvent);
        }

        public void Update(GameTime gameTime)
        {
            if (PlaybackVideoManager != null && ChannelsGridNode != null)
            {
                HasFocus = true;

                if (primary != null)
                {
                    DateTime currentTime = primary.CurrentTime;

                    // Check if we recently performed a seek operation
                    bool recentSeek = lastSeekTime.HasValue && (DateTime.Now - lastSeekTimeSet).TotalMilliseconds < 3000; // 3 second grace period
                    
                    if (recentSeek)
                    {
                        // During seek grace period, use the seek time if the primary time hasn't caught up yet
                        if (Math.Abs((lastSeekTime.Value - currentTime).TotalSeconds) > 2.0) // If primary time is still far from seek target
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"Update: Using seek time {lastSeekTime.Value:HH:mm:ss.fff} instead of primary time {currentTime:HH:mm:ss.fff}");
                            currentTime = lastSeekTime.Value;
                        }
                        else
                        {
                            // Primary has caught up, clear the seek tracking
                            Tools.Logger.VideoLog.LogCall(this, $"Update: Primary time caught up, clearing seek tracking");
                            lastSeekTime = null;
                        }
                    }

                    if (Math.Abs((SeekNode.CurrentTime - currentTime).TotalMilliseconds) > 10)
                    {
                        SeekNode.CurrentTime = currentTime;
                        SeekNode.RequestLayout();

                        ChannelsGridNode.SetPlaybackTime(currentTime);
                        ChannelsGridNode.SetReorderType(ApplicationProfileSettings.Instance.PilotOrderMidRace == ApplicationProfileSettings.OrderTypes.Channel ? ChannelsGridNode.ReOrderTypes.ChannelOrder : ChannelsGridNode.ReOrderTypes.PositionOrder);
                        ChannelsGridNode.Reorder();
                    }
                }
            }
        }

        /// <summary>
        /// Get frame times from the frame sources for timeline positioning
        /// </summary>
        private FrameTime[] GetFrameTimesFromFrameSources()
        {
            try
            {
                
                if (PlaybackVideoManager != null)
                {
                    var allFrameSources = PlaybackVideoManager.GetFrameSources();
                    
                    var frameSources = allFrameSources.OfType<ICaptureFrameSource>();
                    
                    foreach (var frameSource in frameSources)
                    {
                        var frameTimes = frameSource.FrameTimes;
                        if (frameTimes != null && frameTimes.Length > 0)
                        {
                            return frameTimes;
                        }
                    }
                }
                
                return new FrameTime[0];
            }
            catch (Exception ex)
            {
                // Log error but don't crash the UI
                Tools.Logger.VideoLog.LogException(this, ex);
                return new FrameTime[0];
            }
        }


    }
}
