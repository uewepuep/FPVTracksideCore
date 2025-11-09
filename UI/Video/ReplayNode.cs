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
using FfmpegMediaPlatform;

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
            SeekNode.SlowSpeedChanged += OnSlowSpeedChanged;
            SeekNode.SyncDelayChanged += OnSyncDelayChanged;
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
                        // If this frame source has frame timing data, use unified duration calculation
                        if (frameSource is ICaptureFrameSource captureSource && captureSource.FrameTimes != null && captureSource.FrameTimes.Length > 0)
                        {
                            // Use unified duration calculation for consistency across platforms
                            var xmlDuration = UnifiedFrameTimingManager.CalculateVideoDuration(
                                captureSource.FrameTimes, frameSource.Length);
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

                // Get device latency from the first frame source
                // Default to 1.3 seconds to compensate for typical video encoding/processing delay
                // Increased by 0.3s based on observed timing difference
                float deviceLatency = 0.4f;

                // Get the actual FrameSource objects to access VideoConfig
                var actualFrameSources = GetFrameSources();
                var firstFrameSource = actualFrameSources.FirstOrDefault();
                if (firstFrameSource != null && firstFrameSource.VideoConfig != null)
                {
                    if (firstFrameSource.VideoConfig.DeviceLatency > 0)
                    {
                        deviceLatency = firstFrameSource.VideoConfig.DeviceLatency;
                        Tools.Logger.VideoLog.LogCall(this, $"OnChange: Using configured device latency: {deviceLatency:F3}s from VideoConfig");
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"OnChange: Using default device latency: {deviceLatency:F3}s (VideoConfig has no latency set)");
                    }
                }
                else
                {
                    Tools.Logger.VideoLog.LogCall(this, $"OnChange: Using default device latency: {deviceLatency:F3}s (no VideoConfig available)");
                }

                SeekNode.SetRace(race, minStart, maxEnd, frameTimes, deviceLatency);
                ChannelsGridNode.SetPlaybackTime(race.Start);
            }
        }

        private void Checkbox_ValueChanged(bool slowMotion)
        {
            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                frameSource.PlaybackSpeed = slowMotion ? PlaybackSpeed.Slow : PlaybackSpeed.Normal;
                if (slowMotion)
                {
                    // Set the custom slow speed from the SeekNode input
                    frameSource.SlowSpeedFactor = SeekNode.SlowSpeed;
                }
            }
        }

        private void OnSlowSpeedChanged(float newSpeed)
        {
            // Update the slow speed factor for all frame sources if slow motion is currently enabled
            if (SeekNode.SlowCheck.Checkbox.Value)
            {
                foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
                {
                    frameSource.SlowSpeedFactor = newSpeed;
                }
            }
        }

        private void OnSyncDelayChanged(float newDelay)
        {
            // Refresh the lap lines with the new sync delay
            Tools.Logger.VideoLog.LogCall(this, $"Sync delay changed to: {newDelay:F2}s");
            SeekNode.RefreshLapLines();
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
                // Pause the playback - maintains current position
                Tools.Logger.VideoLog.LogCall(this, $"ReplayNode.Stop: Pausing frameSource");
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
                // Simply call Play() - the frame source will handle resume correctly
                Tools.Logger.VideoLog.LogCall(this, $"ReplayNode.Play: Calling Play on frameSource");
                frameSource.Play();
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

            // Remember if we're currently playing before the seek
            bool wasPlaying = SeekNode.StopButton.Visible;

            foreach (IPlaybackFrameSource frameSource in GetFileFrameSources())
            {
                Tools.Logger.VideoLog.LogCall(this, $"ReplayNode.Seek calling SetPosition on frameSource: {frameSource.GetType().Name}");
                frameSource.SetPosition(seekTime);

                // Only resume playing if we were playing before the seek
                // The SetPosition should maintain the play/pause state internally
                if (wasPlaying)
                {
                    // Ensure we're playing after seek
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

        private IEnumerable<FrameSource> GetFrameSources()
        {
            if (PlaybackVideoManager != null)
            {
                return PlaybackVideoManager.GetFrameSources();
            }
            return new FrameSource[0];
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
                                // Use unified duration calculation for consistency across platforms
                                var xmlDuration = UnifiedFrameTimingManager.CalculateVideoDuration(
                                    captureSource.FrameTimes, frameSource.Length);
                                
                                // For playback timeline, prefer the shorter of XML and container-based Length
                                // to avoid progress bar extending beyond actual playable content
                                var playbackDuration = TimeSpan.FromSeconds(Math.Min(xmlDuration.TotalSeconds, frameSource.Length.TotalSeconds));
                                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Timeline: XML={xmlDuration.TotalSeconds:F3}s, Length={frameSource.Length.TotalSeconds:F3}s, Using(min)={playbackDuration.TotalSeconds:F3}s for {frameSource.GetType().Name}");
                                maxEndDuration = TimeSpan.FromSeconds(Math.Max(maxEndDuration.TotalSeconds, playbackDuration.TotalSeconds));
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

                    // Get device latency from the first frame source
                    // Default to 1.3 seconds to compensate for typical video encoding/processing delay
                    // Increased by 0.3s based on observed timing difference
                    float deviceLatency = 0.4f;

                    // frameSources here are IPlaybackFrameSource, need to cast to FrameSource
                    var firstFrameSource = frameSources.FirstOrDefault() as FrameSource;
                    if (firstFrameSource != null && firstFrameSource.VideoConfig != null)
                    {
                        if (firstFrameSource.VideoConfig.DeviceLatency > 0)
                        {
                            deviceLatency = firstFrameSource.VideoConfig.DeviceLatency;
                            Tools.Logger.VideoLog.LogCall(this, $"ReplayRace: Using configured device latency: {deviceLatency:F3}s from VideoConfig");
                        }
                        else
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"ReplayRace: Using default device latency: {deviceLatency:F3}s (VideoConfig has no latency set)");
                        }
                    }
                    else
                    {
                        Tools.Logger.VideoLog.LogCall(this, $"ReplayRace: Using default device latency: {deviceLatency:F3}s (no VideoConfig available)");
                    }

                    SeekNode.SetRace(race, minStart, maxEnd, frameTimes, deviceLatency);

                    ChannelsGridNode.SetProfileVisible(ChannelNodeBase.PilotProfileOptions.Small);

                    RequestLayout();
                });

                SeekNode.PlayButton.Visible = false;
                SeekNode.StopButton.Visible = true;
                SeekNode.SlowCheck.Visible = true;
                SeekNode.ShowAll.Visible = false;
                SeekNode.SlowCheck.Checkbox.Value = false;

                // Ensure sync delay controls are visible
                SeekNode.SyncDelayLabel.Visible = true;
                SeekNode.SyncDelayInput.Visible = true;
                SeekNode.SyncDelayUpButton.Visible = true;
                SeekNode.SyncDelayDownButton.Visible = true;


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
                    if (keyMapper.ReplayPlus2Seconds.Match(inputEvent))
                    {
                        double increment = 2.0;
                        // If slow motion is enabled, adjust the increment by the slow motion factor
                        if (SeekNode.SlowCheck.Checkbox.Value)
                        {
                            increment *= SeekNode.SlowSpeed;
                        }
                        Seek(primary.CurrentTime + TimeSpan.FromSeconds(increment));
                    }

                    if (keyMapper.ReplayMinus2Seconds.Match(inputEvent))
                    {
                        double increment = -2.0;
                        // If slow motion is enabled, adjust the increment by the slow motion factor
                        if (SeekNode.SlowCheck.Checkbox.Value)
                        {
                            increment *= SeekNode.SlowSpeed;
                        }
                        Seek(primary.CurrentTime + TimeSpan.FromSeconds(increment));
                    }

                    if (keyMapper.ReplaySpeedUp.Match(inputEvent))
                    {
                        // Increase slow motion speed by 0.1
                        SeekNode.AdjustSpeedPublic(0.1f);
                    }

                    if (keyMapper.ReplaySpeedDown.Match(inputEvent))
                    {
                        // Decrease slow motion speed by 0.1
                        SeekNode.AdjustSpeedPublic(-0.1f);
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
                    // Only update progress bar when playing (Stop button is visible)
                    bool isPlaying = SeekNode.StopButton.Visible;

                    if (isPlaying)
                    {
                        DateTime currentTime = primary.CurrentTime;

                        // Check if we recently performed a seek operation
                        bool recentSeek = lastSeekTime.HasValue && (DateTime.Now - lastSeekTimeSet).TotalMilliseconds < 1000; // 1 second grace period

                        if (recentSeek)
                        {
                            // During seek grace period, use the seek time if the primary time hasn't caught up yet
                            if (Math.Abs((lastSeekTime.Value - currentTime).TotalSeconds) > 0.5) // If primary time is still far from seek target
                            {
                                // Don't update the progress bar yet - wait for the video to catch up
                                return;
                            }
                            else
                            {
                                // Primary has caught up, clear the seek tracking
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

                    // First try to get frame times from IPlaybackFrameSource (for playback mode)
                    var playbackSources = allFrameSources.OfType<IPlaybackFrameSource>();
                    foreach (var frameSource in playbackSources)
                    {
                        var frameTimes = frameSource.FrameTimes;
                        if (frameTimes != null && frameTimes.Length > 0)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"Found frame times from IPlaybackFrameSource: {frameTimes.Length} frames");
                            return frameTimes;
                        }
                    }

                    // Fallback to ICaptureFrameSource (for recording mode)
                    var captureSources = allFrameSources.OfType<ICaptureFrameSource>();
                    foreach (var frameSource in captureSources)
                    {
                        var frameTimes = frameSource.FrameTimes;
                        if (frameTimes != null && frameTimes.Length > 0)
                        {
                            Tools.Logger.VideoLog.LogCall(this, $"Found frame times from ICaptureFrameSource: {frameTimes.Length} frames");
                            return frameTimes;
                        }
                    }
                }

                Tools.Logger.VideoLog.LogCall(this, "No frame times found from any frame source");
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
