using Composition;
using Composition.Input;
using Composition.Nodes;
using ImageServer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RaceLib;
using RaceLib.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Video
{
    public class SeekNode : Node
    {
        public event Action<DateTime> Seek;
        public event Action<float> SlowSpeedChanged;
        public event Action<float> SyncDelayChanged;

        private ProgressBarNode progressBar;
        private Node progressBarLineContainer;

        public DateTime CurrentTime
        {
            get
            {
                return FactorToTime(progressBar.Progress);
            }
            set
            {
                progressBar.Progress = TimeToFactor(value);
            }
        }

        public ImageButtonNode PlayButton { get; private set; }
        public TextCheckBoxNode SlowCheck { get; private set; }
        public TextEditNode SlowSpeedInput { get; private set; }
        public TextButtonNode SpeedUpButton { get; private set; }
        public TextButtonNode SpeedDownButton { get; private set; }
        public ImageButtonNode StopButton { get; private set; }

        public TextButtonNode ShowAll { get; private set; }

        // Sync delay controls
        public TextNode SyncDelayLabel { get; private set; }
        public TextEditNode SyncDelayInput { get; private set; }
        public TextButtonNode SyncDelayUpButton { get; private set; }
        public TextButtonNode SyncDelayDownButton { get; private set; }

        public float SlowSpeed { get; private set; } = 0.1f; // Default to 0.1 (10% speed)
        public float SyncDelay { get; private set; } = 0.4f; // Default to 0.4 seconds (configurable)

        private Node buttonsNode;

        private Node flagLabels;

        private EventManager eventManager;

        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        public SeekNode(EventManager eventManager, Color color)
        {
            this.eventManager = eventManager;

            // Load saved values from settings
            if (GeneralSettings.Instance != null)
            {
                SlowSpeed = GeneralSettings.Instance.SlowPlaybackSpeed;
                SyncDelay = GeneralSettings.Instance.VideoSyncDelay;
            }

            Node container = new Node();
            container.RelativeBounds = new RectangleF(0, 0.0f, 1, 0.6f);
            AddChild(container);

            buttonsNode = new Node();
            buttonsNode.RelativeBounds = new RectangleF(0, 0, 0.02f, 1);
            container.AddChild(buttonsNode);

            PlayButton = new ImageButtonNode(@"img\start.png", Color.Transparent, Theme.Current.Hover.XNA, color);
            buttonsNode.AddChild(PlayButton);

            StopButton = new ImageButtonNode(@"img\stop.png", Color.Transparent, Theme.Current.Hover.XNA, color);
            buttonsNode.AddChild(StopButton);

            float slowWidth = 0.05f;
            float speedInputWidth = 0.03f;
            float arrowButtonWidth = 0.035f; // Increased width for better visibility
            float syncDelayLabelWidth = 0.06f;
            float syncDelayInputWidth = 0.035f;
            float showAllWidth = 0.05f;
            float totalRightWidth = showAllWidth + slowWidth + speedInputWidth + arrowButtonWidth + syncDelayLabelWidth + syncDelayInputWidth + arrowButtonWidth + 0.02f;

            // Calculate position for speed buttons (to the right of speed input)
            float speedButtonsX = 1 - (showAllWidth + syncDelayLabelWidth + syncDelayInputWidth + arrowButtonWidth + arrowButtonWidth + 0.01f);

            SlowCheck = new TextCheckBoxNode("Slow", color, false);
            SlowCheck.RelativeBounds = new RectangleF(speedButtonsX - speedInputWidth - slowWidth - 0.005f, 0, slowWidth, 1);
            SlowCheck.SetRatio(0.6f, 0.05f);
            SlowCheck.Scale(1, 0.8f);
            container.AddChild(SlowCheck);

            SlowSpeedInput = new TextEditNode(SlowSpeed.ToString("0.00"), color);
            SlowSpeedInput.RelativeBounds = new RectangleF(speedButtonsX - speedInputWidth - 0.002f, 0, speedInputWidth, 1);
            SlowSpeedInput.Scale(1, 0.8f);
            SlowSpeedInput.TextChanged += OnSlowSpeedChanged;
            SlowSpeedInput.CanEdit = true; // Ensure it can be edited
            container.AddChild(SlowSpeedInput);

            // Speed up button (+)
            Color buttonBg = new Color(60, 60, 60, 200); // Dark gray with transparency
            SpeedUpButton = new TextButtonNode("+", buttonBg, Theme.Current.Hover.XNA, color);
            SpeedUpButton.RelativeBounds = new RectangleF(speedButtonsX, 0.1f, arrowButtonWidth, 0.4f);
            SpeedUpButton.TextNode.RelativeBounds = new RectangleF(0.05f, 0.05f, 0.9f, 0.9f); // Larger text area
            SpeedUpButton.TextNode.Scale(1.5f, 1.5f); // Make text 50% larger
            SpeedUpButton.OnClick += (m) => { AdjustSpeedPublic(0.1f); };
            container.AddChild(SpeedUpButton);

            // Speed down button (-)
            SpeedDownButton = new TextButtonNode("-", buttonBg, Theme.Current.Hover.XNA, color);
            SpeedDownButton.RelativeBounds = new RectangleF(speedButtonsX, 0.5f, arrowButtonWidth, 0.4f);
            SpeedDownButton.TextNode.RelativeBounds = new RectangleF(0.05f, 0.05f, 0.9f, 0.9f); // Larger text area
            SpeedDownButton.TextNode.Scale(1.5f, 1.5f); // Make text 50% larger
            SpeedDownButton.OnClick += (m) => { AdjustSpeedPublic(-0.1f); };
            container.AddChild(SpeedDownButton);

            // Sync Delay label
            SyncDelayLabel = new TextNode("Sync Delay:", color);
            SyncDelayLabel.RelativeBounds = new RectangleF(1 - (showAllWidth + syncDelayLabelWidth + syncDelayInputWidth + arrowButtonWidth + 0.005f), 0, syncDelayLabelWidth, 1);
            SyncDelayLabel.Scale(1, 0.8f);
            container.AddChild(SyncDelayLabel);

            // Sync Delay input
            SyncDelayInput = new TextEditNode(SyncDelay.ToString("0.00"), color);
            SyncDelayInput.RelativeBounds = new RectangleF(1 - (showAllWidth + syncDelayInputWidth + arrowButtonWidth + 0.005f), 0, syncDelayInputWidth, 1);
            SyncDelayInput.Scale(1, 0.8f);
            SyncDelayInput.TextChanged += OnSyncDelayChanged;
            SyncDelayInput.CanEdit = true;
            container.AddChild(SyncDelayInput);

            // Sync delay up button (+)
            SyncDelayUpButton = new TextButtonNode("+", buttonBg, Theme.Current.Hover.XNA, color);
            SyncDelayUpButton.RelativeBounds = new RectangleF(1 - (showAllWidth + arrowButtonWidth + 0.002f), 0.1f, arrowButtonWidth, 0.4f);
            SyncDelayUpButton.TextNode.RelativeBounds = new RectangleF(0.05f, 0.05f, 0.9f, 0.9f); // Larger text area
            SyncDelayUpButton.TextNode.Scale(1.5f, 1.5f); // Make text 50% larger
            SyncDelayUpButton.OnClick += (m) => { AdjustSyncDelay(0.01f); };
            SyncDelayUpButton.Visible = true;
            container.AddChild(SyncDelayUpButton);

            // Sync delay down button (-)
            SyncDelayDownButton = new TextButtonNode("-", buttonBg, Theme.Current.Hover.XNA, color);
            SyncDelayDownButton.RelativeBounds = new RectangleF(1 - (showAllWidth + arrowButtonWidth + 0.002f), 0.5f, arrowButtonWidth, 0.4f);
            SyncDelayDownButton.TextNode.RelativeBounds = new RectangleF(0.05f, 0.05f, 0.9f, 0.9f); // Larger text area
            SyncDelayDownButton.TextNode.Scale(1.5f, 1.5f); // Make text 50% larger
            SyncDelayDownButton.OnClick += (m) => { AdjustSyncDelay(-0.01f); };
            SyncDelayDownButton.Visible = true;
            container.AddChild(SyncDelayDownButton);

            ShowAll = new TextButtonNode("Show All", Color.Transparent, Theme.Current.Hover.XNA, color);
            ShowAll.RelativeBounds = new RectangleF(1 - showAllWidth, 0, showAllWidth, 1);
            ShowAll.Scale(1, 0.8f);
            ShowAll.TextNode.RelativeBounds = new RectangleF(0, 0, 1, 1);
            container.AddChild(ShowAll);

            progressBar = new ProgressBarNode(color);
            progressBar.RelativeBounds = new RectangleF(buttonsNode.RelativeBounds.Right, 0, 1 - (buttonsNode.RelativeBounds.Right + totalRightWidth), 1);
            container.AddChild(progressBar);

            progressBarLineContainer = new Node();
            progressBar.AddChild(progressBarLineContainer);

            flagLabels = new Node();
            flagLabels.RelativeBounds = new RectangleF(progressBar.RelativeBounds.X, container.RelativeBounds.Bottom, progressBar.RelativeBounds.Width, 1 - container.RelativeBounds.Bottom);
            AddChild(flagLabels);
        }

        public void ClearFlags()
        {
            flagLabels.ClearDisposeChildren();
            progressBarLineContainer.ClearDisposeChildren();
        }

        private DateTime FactorToTime(float factor)
        {
            // Clamp factor and guard against invalid/zero timeline length
            if (float.IsNaN(factor) || float.IsInfinity(factor))
            {
                factor = 0f;
            }
            factor = Math.Clamp(factor, 0f, 1f);

            TimeSpan len = End - Start;
            double lenSec = len.TotalSeconds;
            if (double.IsNaN(lenSec) || double.IsInfinity(lenSec) || lenSec <= 0)
            {
                Tools.Logger.VideoLog.LogCall(this, "PROGRESSBAR FactorToTime: invalid or zero length timeline, returning Start");
                return Start;
            }

            double seconds = lenSec * factor;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                Tools.Logger.VideoLog.LogCall(this, "PROGRESSBAR FactorToTime: computed seconds invalid, returning Start");
                return Start;
            }
            return Start + TimeSpan.FromSeconds(seconds);
        }

        private float TimeToFactor(DateTime dateTime)
        {
            TimeSpan len = End - Start;
            double lenSec = len.TotalSeconds;
            if (double.IsNaN(lenSec) || double.IsInfinity(lenSec) || lenSec <= 0)
            {
                Tools.Logger.VideoLog.LogCall(this, "PROGRESSBAR TimeToFactor: invalid or zero length timeline, returning 0");
                return 0f;
            }

            double timeSec = (dateTime - Start).TotalSeconds;
            if (double.IsNaN(timeSec) || double.IsInfinity(timeSec))
            {
                Tools.Logger.VideoLog.LogCall(this, "PROGRESSBAR TimeToFactor: invalid time, returning 0");
                return 0f;
            }

            double factor = timeSec / lenSec;
            if (double.IsNaN(factor) || double.IsInfinity(factor))
            {
                Tools.Logger.VideoLog.LogCall(this, "PROGRESSBAR TimeToFactor: invalid factor, returning 0");
                factor = 0.0;
            }
            return (float)Math.Clamp(factor, 0.0, 1.0);
        }

        public void SetRace(Race race, DateTime mediaStart, DateTime mediaEnd, FrameTime[] frameTimes = null, float deviceLatency = 0, bool updateLapLines = true)
        {
            ClearFlags();

            // Store values for refreshing lap lines when sync delay changes
            currentRace = race;
            currentMediaStart = mediaStart;
            currentMediaEnd = mediaEnd;
            currentFrameTimes = frameTimes;

            // Use the full video file timeline for the progress bar
            // This ensures the white progress bar represents the entire video file
            Start = mediaStart;
            End = mediaEnd;

            // Debug logging for frame times
            Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR SetRace called: mediaStart={mediaStart:HH:mm:ss.fff}, mediaEnd={mediaEnd:HH:mm:ss.fff}");
            Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR FrameTimes provided: {frameTimes?.Length ?? 0} frame times");
            Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Race Start: {race.Start:HH:mm:ss.fff}, Race End: {race.End:HH:mm:ss.fff}");
            Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR DeviceLatency (ignored, using SyncDelay): {deviceLatency:F3} seconds");

            // Use the user-configurable SyncDelay instead of passed deviceLatency
            if (deviceLatency > 0 && Math.Abs(SyncDelay - 0.4f) < 0.001f)
            {
                // If a device latency was passed and SyncDelay is still at default, use the device latency
                SyncDelay = deviceLatency;
                SyncDelayInput.Text = SyncDelay.ToString("0.00");
            }
            
            if (frameTimes != null && frameTimes.Length > 0)
            {
                var firstFrame = frameTimes.First();
                var lastFrame = frameTimes.Last();
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR First FrameTime: Frame={firstFrame.Frame}, Time={firstFrame.Time:HH:mm:ss.fff}, Seconds={firstFrame.Seconds:F3}");
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Last FrameTime: Frame={lastFrame.Frame}, Time={lastFrame.Time:HH:mm:ss.fff}, Seconds={lastFrame.Seconds:F3}");
            }

            foreach (PilotChannel pilotChannel in race.PilotChannelsSafe)
            {
                Color tint = eventManager.GetRaceChannelColor(race, pilotChannel.Channel);

                Lap[] laps = race.GetValidLaps(pilotChannel.Pilot, true);
                foreach (Lap l in laps)
                {
                    // Use the lap's EndTime for accurate positioning on the progress bar
                    DateTime lapEndTime = l.End;
                    DateTime videoTime = ConvertDetectionTimeToVideoTime(lapEndTime, frameTimes, mediaStart, mediaEnd, SyncDelay);

                    string lapNumber = "L" + l.Number.ToString();
                    if (l.Number == 0)
                    {
                        lapNumber = "HS";
                    }

                    AddTimeMarker(videoTime, tint, lapNumber);
                }
            }

            // Convert race start/end times to video timeline as well
            DateTime raceStartVideoTime = ConvertDetectionTimeToVideoTime(race.Start, frameTimes, mediaStart, mediaEnd, SyncDelay);
            DateTime raceEndVideoTime = ConvertDetectionTimeToVideoTime(race.End, frameTimes, mediaStart, mediaEnd, SyncDelay);
            
            AddFlagAtTime(raceStartVideoTime, Color.Green);
            AddFlagAtTime(raceEndVideoTime, Color.White);

            if (race.Event.Flags != null)
            {
                IEnumerable<DateTime> flags = race.Event.Flags.Where(f => race.Start <= f && race.End >= f);
                foreach (DateTime flag in flags)
                {
                    AddFlagAtTime(flag, Color.Yellow);
                }
            }

            if (race.GamePoints != null)
            {
                foreach (GamePoint gamePoint in race.GamePoints)
                {
                    Color tint = eventManager.GetRaceChannelColor(race, gamePoint.Channel);

                    AddTimeMarker(gamePoint.Time, tint, gamePoint.Channel.DisplayName);
                }
            }

            RequestLayout();
        }


        private void AddFlagAtTime(DateTime time, Color tint)
        {
            float factor = AddLineAtTime(time, tint);
            ImageNode flag = new ImageNode(@"img/raceflag.png", tint);

            flag.RelativeBounds = new RectangleF(factor, 0, 0.02f, 1f);
            flag.KeepAspectRatio = false;
            flag.CanScale = false;
            flag.Alignment = RectangleAlignment.CenterLeft;
            flagLabels.AddChild(flag);
        }

        private void AddTimeMarker(DateTime time, Color tint, string text)
        {
            Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR AddTimeMarker: {text} at {time:HH:mm:ss.fff}");
            float factor = AddLineAtTime(time, tint);
            Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR AddTimeMarker: {text} factor = {factor:F4} (Start={Start:HH:mm:ss.fff}, End={End:HH:mm:ss.fff})");
            TextNode textNode = new TextNode(text, tint);
            textNode.RelativeBounds = new RectangleF(factor, 0, 1, 1.2f);
            textNode.Alignment = RectangleAlignment.BottomLeft;
            flagLabels.AddChild(textNode);
        }

        private float AddLineAtTime(DateTime time, Color tint)
        {
            float factor = TimeToFactor(time);
            ImageNode flag = new ImageNode(@"img/flag.png", tint);
            flag.RelativeBounds = new RectangleF(factor, 0, 1, 1f);
            flag.Alignment = RectangleAlignment.CenterLeft;
            flag.CanScale = false;
            progressBarLineContainer.AddChild(flag);

            return factor;
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (Mouse.GetState().LeftButton == ButtonState.Pressed && Seek != null)
            {
                if (progressBar.Contains(mouseInputEvent.Position))
                {
                    int x = mouseInputEvent.Position.X - progressBar.Bounds.X;
                    float factor = x / (float)progressBar.Bounds.Width;

                    DateTime time = FactorToTime(factor);
                    Seek(time);
                }
            }

            return base.OnMouseInput(mouseInputEvent);
        }


        /// <summary>
        /// Convert a detection time (real-world timestamp) to video timeline position
        /// </summary>
        private DateTime ConvertDetectionTimeToVideoTime(DateTime detectionTime, FrameTime[] frameTimes, DateTime mediaStart, DateTime mediaEnd, float deviceLatency = 0)
        {
            try
            {
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Converting detection time: {detectionTime:HH:mm:ss.fff} with latency: {deviceLatency:F3}s");

                if (frameTimes == null || frameTimes.Length == 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR No frame times available, applying latency directly: {deviceLatency:F3}s");
                    // No frame times available, but we still need to apply latency
                    // Assume detection time maps directly to video time, just add the latency
                    DateTime adjustedTime = detectionTime.AddSeconds(deviceLatency);
                    Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Adjusted time: {adjustedTime:HH:mm:ss.fff} (detection + latency)");
                    return adjustedTime;
                }

                // Use the FrameTime extension method to convert detection time to video timeline
                // The latency represents delay between detection and when it appears in video
                // Positive latency will move the lines later on the timeline
                TimeSpan latencySpan = TimeSpan.FromSeconds(deviceLatency);

                // Log the conversion process for debugging
                var firstFrame = frameTimes.OrderBy(f => f.Time).First();
                TimeSpan rawOffset = detectionTime - firstFrame.Time;
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Raw offset from first frame: {rawOffset.TotalSeconds:F3}s");
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Adding latency: {deviceLatency:F3}s");

                TimeSpan mediaTimeSpan = frameTimes.GetMediaTime(detectionTime, latencySpan);
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR GetMediaTime returned: {mediaTimeSpan.TotalSeconds:F3} seconds (should be raw + latency)");

                // Convert the media time offset to the progress bar timeline
                // The progress bar uses mediaStart to mediaEnd, so we add the offset to mediaStart
                DateTime videoTime = mediaStart.Add(mediaTimeSpan);
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Final video time: {videoTime:HH:mm:ss.fff} (mediaStart: {mediaStart:HH:mm:ss.fff} + offset: {mediaTimeSpan.TotalSeconds:F3}s)");
                
                // Clamp to video bounds
                if (videoTime < mediaStart) 
                {
                    Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Clamping to mediaStart: {mediaStart:HH:mm:ss.fff}");
                    videoTime = mediaStart;
                }
                if (videoTime > mediaEnd) 
                {
                    Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Clamping to mediaEnd: {mediaEnd:HH:mm:ss.fff}");
                    videoTime = mediaEnd;
                }
                
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Final video time: {videoTime:HH:mm:ss.fff}");
                return videoTime;
            }
            catch (Exception ex)
            {
                // Log error and fall back to original detection time
                Tools.Logger.VideoLog.LogException(this, ex);
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Exception occurred, returning original detection time: {detectionTime:HH:mm:ss.fff}");
                return detectionTime;
            }
        }

        private void OnSlowSpeedChanged(string speedText)
        {
            if (float.TryParse(speedText, out float speed))
            {
                // Clamp the speed between 0.05 and 1.0
                speed = Math.Max(0.05f, Math.Min(1.0f, speed));
                SlowSpeed = speed;
                
                // Update the text field to show the clamped value
                if (Math.Abs(speed - float.Parse(speedText)) > 0.001f)
                {
                    SlowSpeedInput.Text = speed.ToString("F2");
                }
                
                // Notify listeners of the speed change
                SlowSpeedChanged?.Invoke(speed);

                // Save to settings
                if (GeneralSettings.Instance != null)
                {
                    GeneralSettings.Instance.SlowPlaybackSpeed = speed;
                    GeneralSettings.Write();
                }
            }
            else
            {
                // Invalid input, reset to current value
                SlowSpeedInput.Text = SlowSpeed.ToString("F2");
            }
        }

        public void AdjustSpeedPublic(float delta)
        {
            float newSpeed;
            
            if (delta > 0) // Increasing speed
            {
                if (SlowSpeed < 0.1f)
                {
                    // From 0.05 to 0.1
                    newSpeed = 0.1f;
                }
                else
                {
                    // Normal increment of 0.1
                    newSpeed = SlowSpeed + 0.1f;
                }
            }
            else // Decreasing speed
            {
                if (SlowSpeed <= 0.1f)
                {
                    // From 0.1 to 0.05
                    newSpeed = 0.05f;
                }
                else
                {
                    // Normal decrement of 0.1
                    newSpeed = SlowSpeed - 0.1f;
                }
            }
            
            // Clamp between 0.05 and 1.0
            newSpeed = Math.Max(0.05f, Math.Min(1.0f, newSpeed));
            
            SlowSpeed = newSpeed;
            SlowSpeedInput.Text = newSpeed.ToString("F2");

            // Notify listeners of the speed change
            SlowSpeedChanged?.Invoke(newSpeed);

            // Save to settings
            if (GeneralSettings.Instance != null)
            {
                GeneralSettings.Instance.SlowPlaybackSpeed = newSpeed;
                GeneralSettings.Write();
            }
        }

        private void OnSyncDelayChanged(string text)
        {
            if (float.TryParse(text, out float delay))
            {
                // Clamp between 0 and 2 seconds
                delay = Math.Max(0.0f, Math.Min(2.0f, delay));

                if (Math.Abs(SyncDelay - delay) > 0.001f)
                {
                    SyncDelay = delay;
                    SyncDelayChanged?.Invoke(SyncDelay);

                    // Save to settings
                    if (GeneralSettings.Instance != null)
                    {
                        GeneralSettings.Instance.VideoSyncDelay = delay;
                        GeneralSettings.Write();
                    }
                }
            }
        }

        private void AdjustSyncDelay(float delta)
        {
            float newDelay = SyncDelay + delta;

            // Clamp between 0 and 2 seconds
            newDelay = Math.Max(0.0f, Math.Min(2.0f, newDelay));

            if (Math.Abs(SyncDelay - newDelay) > 0.001f)
            {
                SyncDelay = newDelay;
                SyncDelayInput.Text = SyncDelay.ToString("0.00");
                SyncDelayChanged?.Invoke(SyncDelay);

                // Save to settings
                if (GeneralSettings.Instance != null)
                {
                    GeneralSettings.Instance.VideoSyncDelay = newDelay;
                    GeneralSettings.Write();
                }
            }
        }

        private Race currentRace;
        private DateTime currentMediaStart;
        private DateTime currentMediaEnd;
        private FrameTime[] currentFrameTimes;

        public void RefreshLapLines()
        {
            if (currentRace != null)
            {
                // Re-call SetRace with current values to refresh lap lines with new sync delay
                SetRace(currentRace, currentMediaStart, currentMediaEnd, currentFrameTimes, 0, true);
            }
        }

    }
}
