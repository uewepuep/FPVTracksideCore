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

        public float SlowSpeed { get; private set; } = 0.1f; // Default to 0.1 (10% speed)

        private Node buttonsNode;

        private Node flagLabels;

        private EventManager eventManager;

        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        public SeekNode(EventManager eventManager, Color color)
        {
            this.eventManager = eventManager;

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
            float arrowButtonWidth = 0.015f; // Width for each arrow button
            float showAllWidth = 0.05f;
            float totalRightWidth = showAllWidth + slowWidth + speedInputWidth + (arrowButtonWidth * 2);

            SlowCheck = new TextCheckBoxNode("Slow", color, false);
            SlowCheck.RelativeBounds = new RectangleF(1 - totalRightWidth, 0, slowWidth, 1);
            SlowCheck.SetRatio(0.6f, 0.05f);
            SlowCheck.Scale(1, 0.8f);
            container.AddChild(SlowCheck);

            SlowSpeedInput = new TextEditNode("0.1", color);
            SlowSpeedInput.RelativeBounds = new RectangleF(1 - (showAllWidth + speedInputWidth + (arrowButtonWidth * 2)), 0, speedInputWidth, 1);
            SlowSpeedInput.Scale(1, 0.8f);
            SlowSpeedInput.TextChanged += OnSlowSpeedChanged;
            SlowSpeedInput.CanEdit = true; // Ensure it can be edited
            container.AddChild(SlowSpeedInput);

            // Speed up button (↑)
            SpeedUpButton = new TextButtonNode("▲", Color.Transparent, Theme.Current.Hover.XNA, color);
            SpeedUpButton.RelativeBounds = new RectangleF(1 - (showAllWidth + arrowButtonWidth * 2), 0, arrowButtonWidth, 0.5f);
            SpeedUpButton.Scale(1, 0.8f);
            SpeedUpButton.OnClick += (m) => { AdjustSpeedPublic(0.1f); };
            container.AddChild(SpeedUpButton);

            // Speed down button (↓)
            SpeedDownButton = new TextButtonNode("▼", Color.Transparent, Theme.Current.Hover.XNA, color);
            SpeedDownButton.RelativeBounds = new RectangleF(1 - (showAllWidth + arrowButtonWidth), 0.5f, arrowButtonWidth, 0.5f);
            SpeedDownButton.Scale(1, 0.8f);
            SpeedDownButton.OnClick += (m) => { AdjustSpeedPublic(-0.1f); };
            container.AddChild(SpeedDownButton);

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

        public void SetRace(Race race, DateTime mediaStart, DateTime mediaEnd, FrameTime[] frameTimes = null)
        {
            ClearFlags();

            // Use the full video file timeline for the progress bar
            // This ensures the white progress bar represents the entire video file
            Start = mediaStart;
            End = mediaEnd;
            
            // Debug logging for frame times
            Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR SetRace called: mediaStart={mediaStart:HH:mm:ss.fff}, mediaEnd={mediaEnd:HH:mm:ss.fff}");
            Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR FrameTimes provided: {frameTimes?.Length ?? 0} frame times");
            Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Race Start: {race.Start:HH:mm:ss.fff}, Race End: {race.End:HH:mm:ss.fff}");
            
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
                    DateTime videoTime = ConvertDetectionTimeToVideoTime(lapEndTime, frameTimes, mediaStart, mediaEnd);

                    string lapNumber = "L" + l.Number.ToString();
                    if (l.Number == 0)
                    {
                        lapNumber = "HS";
                    }

                    AddTimeMarker(videoTime, tint, lapNumber);
                }
            }

            // Convert race start/end times to video timeline as well
            DateTime raceStartVideoTime = ConvertDetectionTimeToVideoTime(race.Start, frameTimes, mediaStart, mediaEnd);
            DateTime raceEndVideoTime = ConvertDetectionTimeToVideoTime(race.End, frameTimes, mediaStart, mediaEnd);
            
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
            float factor = AddLineAtTime(time, tint);
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
        private DateTime ConvertDetectionTimeToVideoTime(DateTime detectionTime, FrameTime[] frameTimes, DateTime mediaStart, DateTime mediaEnd)
        {
            try
            {
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Converting detection time: {detectionTime:HH:mm:ss.fff}");
                
                if (frameTimes == null || frameTimes.Length == 0)
                {
                    Tools.Logger.VideoLog.LogCall(this, "PROGRESSBAR No frame times available, returning original detection time");
                    return detectionTime;
                }

                // Use the FrameTime extension method to convert detection time to video timeline  
                TimeSpan mediaTimeSpan = frameTimes.GetMediaTime(detectionTime, TimeSpan.Zero);
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR GetMediaTime returned: {mediaTimeSpan.TotalSeconds:F3} seconds");
                
                // Convert the media time offset to the progress bar timeline
                // The progress bar uses mediaStart to mediaEnd, so we add the offset to mediaStart
                DateTime videoTime = mediaStart.Add(mediaTimeSpan);
                Tools.Logger.VideoLog.LogCall(this, $"PROGRESSBAR Calculated video time (mediaStart + offset): {videoTime:HH:mm:ss.fff}");
                
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
                // Clamp the speed between 0.1 and 1.0
                speed = Math.Max(0.1f, Math.Min(1.0f, speed));
                SlowSpeed = speed;
                
                // Update the text field to show the clamped value
                if (Math.Abs(speed - float.Parse(speedText)) > 0.001f)
                {
                    SlowSpeedInput.Text = speed.ToString("F1");
                }
                
                // Notify listeners of the speed change
                SlowSpeedChanged?.Invoke(speed);
            }
            else
            {
                // Invalid input, reset to current value
                SlowSpeedInput.Text = SlowSpeed.ToString("F1");
            }
        }

        public void AdjustSpeedPublic(float delta)
        {
            float newSpeed = SlowSpeed + delta;
            newSpeed = Math.Max(0.1f, Math.Min(1.0f, newSpeed)); // Clamp between 0.1 and 1.0
            
            SlowSpeed = newSpeed;
            SlowSpeedInput.Text = newSpeed.ToString("F1");
            
            // Notify listeners of the speed change
            SlowSpeedChanged?.Invoke(newSpeed);
        }

    }
}
