using Composition;
using Composition.Input;
using Composition.Nodes;
using Composition.Text;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Tools;

namespace UI.Nodes
{
    public class TimeInputNode : Node
    {
        private TextNode timeDisplay;
        private TextEditNode secondsInput;
        private TextNode secondsLabel;

        public event Action<TimeSpan> OnTimeChanged;
        public event Action<TimeInputNode, TimeSpan> OnTimeChangedWithSender;

        private TimeSpan originalHeatOffset;  // Never changes
        private TimeSpan variableOffset;      // User editable offset

        public TimeInputNode(Color textColor)
        {
            // Time display (read-only, left side)
            timeDisplay = new TextNode("00:00:00", textColor);
            timeDisplay.RelativeBounds = new RectangleF(0, 0.1f, 0.4f, 0.8f); // Smaller vertical bounds to reduce text size
            AddChild(timeDisplay);

            // Background for seconds input field to make it more obvious it's editable
            ColorNode inputBackground = new ColorNode(Color.DarkGray);
            inputBackground.RelativeBounds = new RectangleF(0.4f, 0.1f, 0.35f, 0.8f);
            AddChild(inputBackground);

            // Seconds input field (middle) - right aligned with background
            secondsInput = new TextEditNode("0", textColor);
            secondsInput.RelativeBounds = new RectangleF(0.02f, 0.1f, 0.96f, 0.8f); // Relative to background
            secondsInput.Alignment = RectangleAlignment.CenterRight; // Right-align the text
            secondsInput.TextChanged += SecondsInput_OnTextChanged;
            inputBackground.AddChild(secondsInput);

            // "sec" label (right side)
            secondsLabel = new TextNode("sec", textColor);
            secondsLabel.RelativeBounds = new RectangleF(0.75f, 0.1f, 0.25f, 0.8f); // Smaller vertical bounds
            AddChild(secondsLabel);

            variableOffset = TimeSpan.Zero;
        }

        public void HideTimeDisplay()
        {
            // Hide the time display by setting its width to 0
            timeDisplay.RelativeBounds = new RectangleF(0, 0.1f, 0f, 0.8f);
            
            // Adjust the input background to start from the left
            var inputBackground = Children.OfType<ColorNode>().FirstOrDefault();
            if (inputBackground != null)
            {
                inputBackground.RelativeBounds = new RectangleF(0f, 0.1f, 0.75f, 0.8f);
            }
            
            // Adjust the "sec" label position
            secondsLabel.RelativeBounds = new RectangleF(0.75f, 0.1f, 0.25f, 0.8f);
        }

        public TimeSpan Time
        {
            get => variableOffset;
            set
            {
                variableOffset = value;
                secondsInput.Text = ((int)value.TotalSeconds).ToString();
                UpdateTimeDisplay();
            }
        }

        public TimeSpan AbsoluteTime
        {
            get => originalHeatOffset.Add(variableOffset);
            set
            {
                originalHeatOffset = value;
                UpdateTimeDisplay();
            }
        }

        public void SetOriginalHeatOffset(TimeSpan offset)
        {
            originalHeatOffset = offset;
            variableOffset = TimeSpan.Zero;
            UpdateTimeDisplay();
        }

        public void SetVariableOffset(TimeSpan offset)
        {
            variableOffset = offset;
            UpdateTimeDisplay();
        }

        private void UpdateTimeDisplay()
        {
            var totalTime = originalHeatOffset + variableOffset;
            
            // Format as HH:MM:SS
            int hours = (int)totalTime.TotalHours;
            int minutes = totalTime.Minutes;
            int seconds = totalTime.Seconds;
            
            // Ensure valid time values
            if (hours < 0 || hours > 23) hours = 0;
            if (minutes < 0 || minutes > 59) minutes = 0;
            if (seconds < 0 || seconds > 59) seconds = 0;
            
            timeDisplay.Text = $"{hours:D2}:{minutes:D2}:{seconds:D2}";
            
            // Update the seconds input to show the variable offset
            secondsInput.Text = ((int)variableOffset.TotalSeconds).ToString();
        }

        private int ParseOrDefault(string text, int defaultValue)
        {
            return int.TryParse(text, out int value) ? value : defaultValue;
        }

        private void SecondsInput_OnTextChanged(string text)
        {
            variableOffset = TimeSpan.FromSeconds(ParseOrDefault(text, 0));
            UpdateTimeDisplay();
            OnTimeChanged?.Invoke(variableOffset);
            OnTimeChangedWithSender?.Invoke(this, variableOffset);
        }
    }
} 