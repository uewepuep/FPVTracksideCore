using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Composition.Text;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;
using UI.Nodes;

namespace UI.Nodes
{
    // Wrapper class to customize how races are displayed in the dropdown
    public class RaceDisplayWrapper
    {
        public Race Race { get; set; }
        
        public RaceDisplayWrapper(Race race)
        {
            Race = race;
        }
        
        public override string ToString()
        {
            if (Race == null)
                return "All Races";

            string roundText = Race.Round != null ? $"Round {Race.Round.RoundNumber} - " : "";
            return $"{roundText}{Race.Type} {Race.RaceNumber}";
        }
    }

    public class ClickableTextNode : TextNode
    {
        public event Action OnClick;
        
        public ClickableTextNode(string text, Color textColor) : base(text, textColor)
        {
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.ButtonState == ButtonStates.Pressed && mouseInputEvent.Button == MouseButtons.Left)
            {
                OnClick?.Invoke();
                return true;
            }
            return base.OnMouseInput(mouseInputEvent);
        }
    }

    public class TextEditWithHintNode : Node
    {
        private TextEditNode textEditNode;
        private TextNode hintTextNode;
        private string hintText;
        
        public event Action<string> TextChanged;
        public event Action<string> LostFocus;
        public event System.Action OnReturn;
        public event System.Action OnTab;
        
        public string Text 
        { 
            get => textEditNode.Text; 
            set => textEditNode.Text = value; 
        }
        
        public string HintText 
        { 
            get => hintText; 
            set 
            { 
                hintText = value; 
                hintTextNode.Text = value;
                UpdateHintVisibility();
            } 
        }
        
        public RectangleAlignment Alignment 
        { 
            get => textEditNode.Alignment; 
            set => textEditNode.Alignment = value; 
        }
        
        public bool CanEdit 
        { 
            get => textEditNode.CanEdit; 
            set => textEditNode.CanEdit = value; 
        }

        public TextEditWithHintNode(string text, Color textColor, string hintText = "", Color? hintColor = null)
        {
            this.hintText = hintText;
            
            // Create the actual text input
            textEditNode = new TextEditNode(text, textColor);
            textEditNode.RelativeBounds = new RectangleF(0, 0, 1, 1);
            AddChild(textEditNode);
            
            // Create the hint text (slightly transparent)
            Color actualHintColor = hintColor ?? Color.FromNonPremultiplied(textColor.R, textColor.G, textColor.B, 128);
            hintTextNode = new TextNode(hintText, actualHintColor);
            hintTextNode.RelativeBounds = new RectangleF(0, 0, 1, 1);
            hintTextNode.Alignment = textEditNode.Alignment;
            AddChild(hintTextNode);
            
            // Wire up events
            textEditNode.TextChanged += (newText) => 
            {
                UpdateHintVisibility();
                TextChanged?.Invoke(newText);
            };
            
            textEditNode.LostFocus += (text) => 
            {
                UpdateHintVisibility();
                LostFocus?.Invoke(text);
            };
            
            textEditNode.OnFocusChanged += (hasFocus) => 
            {
                UpdateHintVisibility();
            };
            
            textEditNode.OnReturn += () => OnReturn?.Invoke();
            textEditNode.OnTab += () => OnTab?.Invoke();
            
            UpdateHintVisibility();
        }
        
        private void UpdateHintVisibility()
        {
            // Show hint text only when the text field is empty and not focused
            bool showHint = string.IsNullOrEmpty(textEditNode.Text) && !textEditNode.HasFocus;
            hintTextNode.Visible = showHint;
        }
        
        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            // Forward mouse input to the text edit node
            return textEditNode.OnMouseInput(mouseInputEvent);
        }
        
        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            // Forward keyboard input to the text edit node
            return textEditNode.OnKeyboardInput(inputEvent);
        }
        
        public bool HasFocus 
        { 
            get => textEditNode.HasFocus; 
            set => textEditNode.HasFocus = value; 
        }
    }

    public class IntegerEditNode : TextEditNode
    {
        public IntegerEditNode(string text, Color textColor) : base(text, textColor)
        {
        }

        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            // Handle keyboard input but filter out non-integer characters
            if (inputEvent.ButtonState == ButtonStates.Pressed || inputEvent.ButtonState == ButtonStates.Repeat)
            {
                char c = inputEvent.GetChar();
                
                // If it's a character input, only allow digits, minus sign, and plus sign
                if (c != 0)
                {
                    // Only allow digits, minus sign (for negative numbers), and plus sign
                    if (!char.IsDigit(c) && c != '-' && c != '+')
                    {
                        // Block non-integer characters
                        return true; // Consume the event but don't process it
                    }
                }
            }
            
            // For all other input (navigation keys, backspace, etc.) or valid integer characters, use base behavior
            return base.OnKeyboardInput(inputEvent);
        }
    }

    public class HMSTimeInputNode : Node
    {
        private TextEditNode textValue;
        private ColorNode backgroundNode;
        private TimeSpan timeValue;
        private bool suppressEvents = false; // Flag to prevent unwanted event firing

        public event Action<TimeSpan> OnTimeChanged;
        public event Action<HMSTimeInputNode, TimeSpan> OnTimeChangedWithSender;

        public TimeSpan Value
        {
            get => timeValue;
            set 
            { 
                // Normalize to remove fractional seconds to prevent rounding issues
                var normalizedValue = new TimeSpan(0, 0, (int)value.TotalSeconds);
                if (Math.Abs((normalizedValue - timeValue).TotalSeconds) > 0.001)
                {
                    timeValue = normalizedValue;
                    suppressEvents = true;
                    textValue.Text = FormatTimeSpan(timeValue);
                    suppressEvents = false;
                }
            }
        }

        public HMSTimeInputNode(TimeSpan initialValue, Color background, Color textColor)
        {
            // Normalize to remove fractional seconds to prevent rounding issues
            timeValue = new TimeSpan(0, 0, (int)initialValue.TotalSeconds);

            // Background similar to other input nodes
            backgroundNode = new ColorNode(background);
            backgroundNode.RelativeBounds = new RectangleF(0, 0, 1, 1);
            AddChild(backgroundNode);

            // Text input for HH:MM:SS format
            textValue = new TextEditNode(FormatTimeSpan(initialValue), textColor);
            textValue.RelativeBounds = new RectangleF(0.02f, 0.1f, 0.96f, 0.8f);
            textValue.Alignment = RectangleAlignment.BottomLeft;
            
            // Handle text changes for real-time updates
            textValue.TextChanged += (text) => 
            {
                if (!suppressEvents && TryParseTimeSpan(text, out TimeSpan newTime))
                {
                    // Normalize to remove fractional seconds
                    var normalizedTime = new TimeSpan(0, 0, (int)newTime.TotalSeconds);
                    if (Math.Abs((normalizedTime - timeValue).TotalSeconds) > 0.001) // 1ms tolerance
                    {
                        timeValue = normalizedTime;
                        OnTimeChanged?.Invoke(timeValue);
                        OnTimeChangedWithSender?.Invoke(this, timeValue);
                    }
                }
            };
            
            // Handle focus changes for validation
            textValue.OnFocusChanged += (hasFocus) => 
            {
                if (!hasFocus)
                {
                    // Only validate if the text has changed from what we expect
                    var expectedText = FormatTimeSpan(timeValue);
                    if (textValue.Text != expectedText)
                    {
                        ValidateAndSetTime(textValue.Text);
                    }
                }
            };
            
            // Handle Enter key to validate immediately
            textValue.OnReturn += () => 
            {
                ValidateAndSetTime(textValue.Text);
            };

            AddChild(textValue);
        }

        public override bool OnKeyboardInput(KeyboardInputEvent inputEvent)
        {
            // Handle Up/Down arrows for second increment/decrement
            if (inputEvent.ButtonState == ButtonStates.Pressed || inputEvent.ButtonState == ButtonStates.Repeat)
            {
                if (inputEvent.Key == Microsoft.Xna.Framework.Input.Keys.Up)
                {
                    // Increase by 1 second
                    Value = timeValue.Add(TimeSpan.FromSeconds(1));
                    OnTimeChanged?.Invoke(timeValue);
                    OnTimeChangedWithSender?.Invoke(this, timeValue);
                    return true;
                }
                else if (inputEvent.Key == Microsoft.Xna.Framework.Input.Keys.Down)
                {
                    // Decrease by 1 second (don't go below 0)
                    var newTime = timeValue.Subtract(TimeSpan.FromSeconds(1));
                    if (newTime >= TimeSpan.Zero)
                    {
                        Value = newTime;
                        OnTimeChanged?.Invoke(timeValue);
                        OnTimeChangedWithSender?.Invoke(this, timeValue);
                    }
                    return true;
                }
            }
            
            return base.OnKeyboardInput(inputEvent);
        }

        private string FormatTimeSpan(TimeSpan time)
        {
            int totalSeconds = (int)time.TotalSeconds;
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;
            return $"{hours}:{minutes:D2}:{seconds:D2}";
        }

        private bool TryParseTimeSpan(string text, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var parts = text.Split(':');
            if (parts.Length != 3)
                return false;

            if (int.TryParse(parts[0], out int hours) &&
                int.TryParse(parts[1], out int minutes) &&
                int.TryParse(parts[2], out int seconds))
            {
                if (hours >= 0 && minutes >= 0 && minutes < 60 && seconds >= 0 && seconds < 60)
                {
                    result = new TimeSpan(hours, minutes, seconds);
                    return true;
                }
            }

            return false;
        }

        private void ValidateAndSetTime(string text)
        {
            if (suppressEvents) return; // Skip validation if events are suppressed
            
            if (string.IsNullOrWhiteSpace(text))
            {
                // Reset to zero if empty
                if (Math.Abs(timeValue.TotalSeconds) > 0.001)
                {
                    timeValue = TimeSpan.Zero;
                    suppressEvents = true;
                    textValue.Text = FormatTimeSpan(timeValue);
                    suppressEvents = false;
                    OnTimeChanged?.Invoke(timeValue);
                    OnTimeChangedWithSender?.Invoke(this, timeValue);
                }
                else
                {
                    suppressEvents = true;
                    textValue.Text = FormatTimeSpan(timeValue);
                    suppressEvents = false;
                }
                return;
            }
            
            if (TryParseTimeSpan(text, out TimeSpan newTime))
            {
                // Normalize to remove fractional seconds
                var normalizedTime = new TimeSpan(0, 0, (int)newTime.TotalSeconds);
                if (Math.Abs((normalizedTime - timeValue).TotalSeconds) > 0.001) // 1ms tolerance
                {
                    timeValue = normalizedTime;
                    suppressEvents = true;
                    textValue.Text = FormatTimeSpan(timeValue);
                    suppressEvents = false;
                    OnTimeChanged?.Invoke(timeValue);
                    OnTimeChangedWithSender?.Invoke(this, timeValue);
                }
            }
            else
            {
                // Reset to current value if invalid
                suppressEvents = true;
                textValue.Text = FormatTimeSpan(timeValue);
                suppressEvents = false;
            }
        }

        public bool Focus()
        {
            textValue.HasFocus = true;
            return true;
        }

        public override void Dispose()
        {
            textValue?.Dispose();
            backgroundNode?.Dispose();
            base.Dispose();
        }
    }

    public class IntegerInputNode : Node
    {
        private IntegerEditNode textValue;
        private ColorNode backgroundNode;
        private int value;
        private bool suppressEvents = false; // Flag to prevent unwanted event firing

        public event Action<int> OnValueChanged;
        public event Action<IntegerInputNode, int> OnValueChangedWithSender;

        public int Value
        {
            get => value;
            set
            {
                // Only update if the value has actually changed
                if (this.value != value)
                {
                    suppressEvents = true;
                    this.value = value;
                    textValue.Text = value.ToString();
                    suppressEvents = false;
                }
            }
        }

        public IntegerInputNode(int initialValue, Color background, Color textColor)
        {
            value = initialValue;

            // Background similar to NumberPropertyNode
            backgroundNode = new ColorNode(background);
            backgroundNode.RelativeBounds = new RectangleF(0, 0, 1, 1);
            AddChild(backgroundNode);

            // Text input using our custom IntegerEditNode that filters input
            textValue = new IntegerEditNode(value.ToString(), textColor);
            textValue.RelativeBounds = new RectangleF(0.02f, 0.1f, 0.96f, 0.8f);
            textValue.Alignment = RectangleAlignment.BottomLeft; // Use BottomLeft like NumberPropertyNode
            
            // Handle immediate text changes for real-time updates
            textValue.TextChanged += (text) => 
            {
                // Try to parse immediately but don't reset text if invalid
                if (!suppressEvents && int.TryParse(text, out int newValue))
                {
                    if (value != newValue)
                    {
                        value = newValue;
                        OnValueChanged?.Invoke(value);
                        OnValueChangedWithSender?.Invoke(this, value);
                    }
                }
                // Don't reset text here - let user continue typing
            };
            
            // Handle focus changes for validation, like NumberPropertyNode
            textValue.OnFocusChanged += (hasFocus) => 
            {
                if (!hasFocus)
                {
                    // Only validate if the text has changed from what we expect
                    var expectedText = value.ToString();
                    if (textValue.Text != expectedText)
                    {
                        // Validate and clean up when focus is lost
                        ValidateAndSetValue(textValue.Text);
                    }
                }
            };
            
            // Handle Enter key to validate immediately
            textValue.OnReturn += () => 
            {
                ValidateAndSetValue(textValue.Text);
            };

            AddChild(textValue);
        }

        private void ValidateAndSetValue(string text)
        {
            if (suppressEvents) return; // Skip validation if events are suppressed
            
            // Handle empty or whitespace-only text by setting to 0
            if (string.IsNullOrWhiteSpace(text))
            {
                if (value != 0)
                {
                    value = 0;
                    suppressEvents = true;
                    textValue.Text = "0";
                    suppressEvents = false;
                    OnValueChanged?.Invoke(value);
                    OnValueChangedWithSender?.Invoke(this, value);
                }
                else
                {
                    // Ensure text shows "0" even if value was already 0
                    suppressEvents = true;
                    textValue.Text = "0";
                    suppressEvents = false;
                }
                return;
            }
            
            if (int.TryParse(text, out int newValue))
            {
                if (value != newValue)
                {
                    value = newValue;
                    suppressEvents = true;
                    textValue.Text = value.ToString(); // Ensure consistent formatting
                    suppressEvents = false;
                    OnValueChanged?.Invoke(value);
                    OnValueChangedWithSender?.Invoke(this, value);
                }
            }
            else
            {
                // Reset to current value if invalid
                suppressEvents = true;
                textValue.Text = value.ToString();
                suppressEvents = false;
            }
        }

        public bool Focus()
        {
            textValue.HasFocus = true;
            return true;
        }

        public override void Dispose()
        {
            textValue?.Dispose();
            backgroundNode?.Dispose();
            base.Dispose();
        }
    }

    public class ChapterMarkerDialog : DialogNode
    {
        private Dictionary<Race, IntegerInputNode> raceOffsets = new Dictionary<Race, IntegerInputNode>();
        private Dictionary<Race, HMSTimeInputNode> raceTimeInputs = new Dictionary<Race, HMSTimeInputNode>(); // Add time input tracking
        private Dictionary<Race, TimeSpan> originalHeatOffsets = new Dictionary<Race, TimeSpan>(); // Original start times from races
        private Dictionary<Race, TimeSpan> variableOffsets = new Dictionary<Race, TimeSpan>(); // Current offset adjustments
        private Dictionary<Race, ClickableTextNode> raceNameDisplays = new Dictionary<Race, ClickableTextNode>();
        private Dictionary<Race, ClickableTextNode> youtubeUrlDisplays = new Dictionary<Race, ClickableTextNode>();

        private ListNode<TextNode> chapterList;
        private TextButtonNode copyButton;
        private ComboBoxNode<RaceDisplayWrapper> raceSelector;
        private TextEditWithHintNode youtubeUrlInput; // Add YouTube URL input field
        
        // Race record customization checkboxes
        private TextCheckBoxNode timeTrialPilotNamesCheckbox;
        private TextCheckBoxNode timeTrialTimesCheckbox;
        private TextCheckBoxNode racePilotNamesCheckbox;
        private TextCheckBoxNode racePositionsCheckbox;
        
        // Race type abbreviation checkboxes
        private TextCheckBoxNode timeTrialAbbreviationCheckbox;
        private TextCheckBoxNode raceAbbreviationCheckbox;
        private TextCheckBoxNode pilotSummaryCheckbox;
        
        // Character count display
        private TextNode characterCountLabel;
        
        private List<Race> availableRaces;
        private PlatformTools platformTools;
        private EventManager eventManager; // Store EventManager reference
        private Race selectedRace; // Track the selected race
        private ListNode<Node> raceList; // Store reference to race list
        private Node textContainer; // Store reference to text container
        private Node buttonContainer; // Store reference to button container

        public ChapterMarkerDialog(EventManager eventManager, PlatformTools platformTools)
        {
            try
            {
                // Override the default dialog width to be 95% of screen width (maximum practical width)
                RelativeBounds = new RectangleF(0.025f, 0.1f, 0.95f, 0.8f);
                
                // Override the absolute size to be much larger to accommodate wider dialog
                Size = new Size(1400, 800);
                
                this.platformTools = platformTools;
                this.eventManager = eventManager; // Store the EventManager reference

            // Debug: Check if we have any races at all
            var allRaces = eventManager.RaceManager.Races;
            System.Diagnostics.Debug.WriteLine($"Total races found: {allRaces?.Count() ?? 0}");
            
            if (allRaces != null)
            {
                // Show all race types for debugging
                var raceTypes = allRaces.GroupBy(r => r.Type).ToList();
                foreach (var group in raceTypes)
                {
                    System.Diagnostics.Debug.WriteLine($"Race Type: {group.Key}, Count: {group.Count()}");
                }
                
                foreach (var race in allRaces.Take(5)) // Show first 5 races for debugging
                {
                    System.Diagnostics.Debug.WriteLine($"Race: {race?.RaceNumber}, Type: {race?.Type}, Start: {race?.Start}, Valid: {race?.Valid}");
                }
            }

            // Filter for races that are suitable for chapter markers
            // Include Race, TimeTrial, and other race types that have meaningful start times
            var races = eventManager.RaceManager.Races
                .Where(r => r != null && r.Valid && r.Started) // Only include valid races that have started
                .OrderBy(r => r.Start)
                .ToList();
            
            System.Diagnostics.Debug.WriteLine($"Filtered races found: {races?.Count ?? 0}");
            
            // If no races match our filter, show a helpful message
            if (races == null || !races.Any())
            {
                System.Diagnostics.Debug.WriteLine("No suitable races found for chapter markers");
                
                // Try a more lenient filter for debugging
                var allValidRaces = eventManager.RaceManager.Races?.Where(r => r != null && r.Valid)?.ToList();
                System.Diagnostics.Debug.WriteLine($"All valid races: {allValidRaces?.Count ?? 0}");
                
                if (allValidRaces != null && allValidRaces.Any())
                {
                    races = allValidRaces.OrderBy(r => r.Start).ToList();
                    System.Diagnostics.Debug.WriteLine($"Using all valid races instead: {races.Count}");
                }
            }
            
            availableRaces = races;

            var container = new Node();
            container.RelativeBounds = new RectangleF(0.01f, 0.01f, 0.98f, 0.98f);
            AddChild(container);

            // Create a background
            var background = new ColorNode(Color.Black);
            background.Alpha = 0.8f;
            container.AddChild(background);

            // YouTube URL input section (0-6%)
            var urlContainer = new Node();
            urlContainer.RelativeBounds = new RectangleF(0, 0, 1, 0.06f);
            container.AddChild(urlContainer);

            // Label for YouTube URL
            var urlLabel = new TextNode("YouTube live stream URL:", Theme.Current.InfoPanel.HeadingText.XNA);
            urlLabel.RelativeBounds = new RectangleF(0, 0, 0.25f, 1);
            urlLabel.Alignment = RectangleAlignment.CenterLeft;
            urlContainer.AddChild(urlLabel);

            // Container for URL input (same structure as Event Settings Name field)
            var urlInputContainer = new Node();
            urlInputContainer.RelativeBounds = new RectangleF(0.26f, 0.1f, 0.74f, 0.8f);
            urlContainer.AddChild(urlInputContainer);

            // Background for URL input (same as Event Settings Name field background)
            var urlBackground = new ColorNode(Theme.Current.Editor.Foreground.XNA);
            urlInputContainer.AddChild(urlBackground);

            // YouTube URL input textbox (same styling as Event Settings Name field)
            youtubeUrlInput = new TextEditWithHintNode("", Theme.Current.Editor.Text.XNA, "(e.g., https://www.youtube.com/watch?v=XFODx1hxC5o), allowing testing links");
            youtubeUrlInput.Alignment = RectangleAlignment.BottomLeft; // Same alignment as Event Settings Name field
            youtubeUrlInput.TextChanged += (newUrl) => 
            {
                // Clean the URL by removing any &t= timestamp parameters
                string cleanedUrl = CleanYouTubeUrl(newUrl);
                
                // If the URL was changed during cleaning, update the input field
                if (cleanedUrl != newUrl)
                {
                    youtubeUrlInput.Text = cleanedUrl;
                    // Note: Don't return here - we still want to update displays with the cleaned URL
                }
                
                // Always update displays when URL changes (using the cleaned URL if it was cleaned)
                UpdateAllYouTubeUrlDisplays();
            };
            urlInputContainer.AddChild(youtubeUrlInput);

            // Race record customization section (6-11%) - increased height for better checkbox spacing
            var customizationContainer = new Node();
            customizationContainer.RelativeBounds = new RectangleF(0, 0.06f, 1, 0.05f);
            container.AddChild(customizationContainer);

            // Label for customization section with character count
            characterCountLabel = new TextNode("0/5000", Theme.Current.PilotViewTheme.PilotOverlayText.XNA);
            characterCountLabel.RelativeBounds = new RectangleF(0, 0, 0.25f, 1);
            characterCountLabel.Alignment = RectangleAlignment.CenterLeft;
            customizationContainer.AddChild(characterCountLabel);

            // Time Trial customization container
            var timeTrialContainer = new Node();
            timeTrialContainer.RelativeBounds = new RectangleF(0.26f, 0, 0.36f, 1);
            customizationContainer.AddChild(timeTrialContainer);

            var timeTrialLabel = new TextNode("TT:", Theme.Current.PilotViewTheme.PilotOverlayText.XNA);
            timeTrialLabel.RelativeBounds = new RectangleF(0, 0, 0.15f, 1);
            timeTrialLabel.Alignment = RectangleAlignment.CenterLeft;
            timeTrialContainer.AddChild(timeTrialLabel);

            timeTrialPilotNamesCheckbox = new TextCheckBoxNode("Pilot", Theme.Current.PilotViewTheme.PilotOverlayText.XNA, true);
            timeTrialPilotNamesCheckbox.RelativeBounds = new RectangleF(0.17f, 0.1f, 0.35f, 0.35f);
            timeTrialPilotNamesCheckbox.SetRatio(0.7f, 0.05f);
            timeTrialContainer.AddChild(timeTrialPilotNamesCheckbox);

            timeTrialTimesCheckbox = new TextCheckBoxNode("Times", Theme.Current.PilotViewTheme.PilotOverlayText.XNA, true);
            timeTrialTimesCheckbox.RelativeBounds = new RectangleF(0.54f, 0.1f, 0.44f, 0.35f);
            timeTrialTimesCheckbox.SetRatio(0.7f, 0.05f);
            timeTrialContainer.AddChild(timeTrialTimesCheckbox);

            // Add TT abbreviation checkbox below the existing ones
            timeTrialAbbreviationCheckbox = new TextCheckBoxNode("Time Trial/TT", Theme.Current.PilotViewTheme.PilotOverlayText.XNA, true);
            timeTrialAbbreviationCheckbox.RelativeBounds = new RectangleF(0.17f, 0.55f, 0.35f, 0.35f);
            timeTrialAbbreviationCheckbox.SetRatio(0.7f, 0.05f);
            timeTrialContainer.AddChild(timeTrialAbbreviationCheckbox);

            // Add Pilot Summary checkbox below the times checkbox
            pilotSummaryCheckbox = new TextCheckBoxNode("Pilot Summary", Theme.Current.PilotViewTheme.PilotOverlayText.XNA, true);
            pilotSummaryCheckbox.RelativeBounds = new RectangleF(0.54f, 0.55f, 0.44f, 0.35f);
            pilotSummaryCheckbox.SetRatio(0.7f, 0.05f);
            timeTrialContainer.AddChild(pilotSummaryCheckbox);

            // Race customization container
            var raceContainer = new Node();
            raceContainer.RelativeBounds = new RectangleF(0.63f, 0, 0.36f, 1);
            customizationContainer.AddChild(raceContainer);

            var raceLabel = new TextNode("Race:", Theme.Current.PilotViewTheme.PilotOverlayText.XNA);
            raceLabel.RelativeBounds = new RectangleF(0, 0, 0.25f, 1);
            raceLabel.Alignment = RectangleAlignment.CenterLeft;
            raceContainer.AddChild(raceLabel);

            racePilotNamesCheckbox = new TextCheckBoxNode("Pilot", Theme.Current.PilotViewTheme.PilotOverlayText.XNA, true);
            racePilotNamesCheckbox.RelativeBounds = new RectangleF(0.27f, 0.1f, 0.35f, 0.35f);
            racePilotNamesCheckbox.SetRatio(0.7f, 0.05f);
            raceContainer.AddChild(racePilotNamesCheckbox);

            racePositionsCheckbox = new TextCheckBoxNode("Positions", Theme.Current.PilotViewTheme.PilotOverlayText.XNA, true);
            racePositionsCheckbox.RelativeBounds = new RectangleF(0.64f, 0.1f, 0.35f, 0.35f);
            racePositionsCheckbox.SetRatio(0.7f, 0.05f);
            raceContainer.AddChild(racePositionsCheckbox);

            // Add R abbreviation checkbox below the existing ones
            raceAbbreviationCheckbox = new TextCheckBoxNode("Race/R", Theme.Current.PilotViewTheme.PilotOverlayText.XNA, true);
            raceAbbreviationCheckbox.RelativeBounds = new RectangleF(0.27f, 0.55f, 0.35f, 0.35f);
            raceAbbreviationCheckbox.SetRatio(0.7f, 0.05f);
            raceContainer.AddChild(raceAbbreviationCheckbox);

            // Add event handlers for customization checkboxes to update chapter markers
            timeTrialPilotNamesCheckbox.Checkbox.ValueChanged += (value) => GenerateChapterMarkers();
            timeTrialTimesCheckbox.Checkbox.ValueChanged += (value) => GenerateChapterMarkers();
            racePilotNamesCheckbox.Checkbox.ValueChanged += (value) => GenerateChapterMarkers();
            racePositionsCheckbox.Checkbox.ValueChanged += (value) => GenerateChapterMarkers();
            timeTrialAbbreviationCheckbox.Checkbox.ValueChanged += (value) => GenerateChapterMarkers();
            raceAbbreviationCheckbox.Checkbox.ValueChanged += (value) => GenerateChapterMarkers();
            pilotSummaryCheckbox.Checkbox.ValueChanged += (value) => GenerateChapterMarkers();

            // Heat selector dropdown (12-18%) - adjusted for taller customization section
            raceSelector = new ComboBoxNode<RaceDisplayWrapper>("Click to Select First Heat In Stream", 
                Theme.Current.InfoPanel.Heading.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.HeadingText.XNA);
            raceSelector.RelativeBounds = new RectangleF(0, 0.12f, 1, 0.06f);
            
            // Add "All Races" option first, then sorted races
            var raceWrappers = new List<RaceDisplayWrapper>();
            raceWrappers.Add(new RaceDisplayWrapper(null)); // "All Races" option
            
            if (races != null && races.Any())
            {
                raceWrappers.AddRange(races.Select(r => new RaceDisplayWrapper(r)));
            }
            
            System.Diagnostics.Debug.WriteLine($"RaceWrappers created: {raceWrappers?.Count ?? 0}");
            
            raceSelector.Items = raceWrappers;
            raceSelector.OnSelectionChanged += (wrapper) => RaceSelector_OnSelectionChanged(wrapper.Race);
            container.AddChild(raceSelector);

            // Race list in the middle with scrolling (19-59%) - adjusted for taller customization section
            var raceListContainer = new Node();
            raceListContainer.RelativeBounds = new RectangleF(0, 0.19f, 1, 0.40f); // Adjusted for taller customization section
            container.AddChild(raceListContainer);

            raceList = new ListNode<Node>(Color.Gray);
            raceList.RelativeBounds = new RectangleF(0, 0, 1, 1);
            raceListContainer.AddChild(raceList);

            // Initialize race offset data
            // The reference time is determined dynamically by GetFilteredRaces() based on selection
            
            foreach (var race in races)
            {
                originalHeatOffsets[race] = race.Start.TimeOfDay;
                variableOffsets[race] = TimeSpan.Zero;
                CreateRaceOffsetNode(race);
            }

            // Build initial race list (all races)
            RebuildRaceList();
            
            // Refresh all displays to ensure correct initial values are shown
            foreach (var race in availableRaces)
            {
                if (raceTimeInputs.ContainsKey(race) && raceOffsets.ContainsKey(race))
                {
                    var cumulativeTimeFromStart = CalculateRaceOffsetTime(race);
                    raceTimeInputs[race].Value = cumulativeTimeFromStart;
                    raceOffsets[race].Value = (int)variableOffsets[race].TotalSeconds;
                }
            }

            // Generated text area with fixed height and proper scrolling (52% to button area)
            textContainer = new Node();
            textContainer.RelativeBounds = new RectangleF(0, 0.52f, 1, 0.35f); // Fixed height for consistent layout
            container.AddChild(textContainer);

            // Create a bordered container for the text area
            var textBackground = new ColorNode(Color.Black);
            textBackground.RelativeBounds = new RectangleF(0, 0, 1, 1);
            textContainer.AddChild(textBackground);

            // Create scrollable list for chapter markers
            chapterList = new ListNode<TextNode>(Color.Gray);
            chapterList.RelativeBounds = new RectangleF(0.01f, 0.01f, 0.98f, 0.98f);
            chapterList.ItemHeight = 18; // Smaller height per line (75% of 25 = ~18)
            chapterList.ItemPadding = 1; // Smaller padding between lines
            textContainer.AddChild(chapterList);

            // Add initial placeholder text
            var placeholderText = new TextNode("Generated chapter markers will appear here...", Color.White);
            placeholderText.Alignment = RectangleAlignment.CenterLeft; // Left align the placeholder text
            chapterList.AddChild(placeholderText);

            // Button container at bottom (90-98%)
            buttonContainer = new Node();
            buttonContainer.RelativeBounds = new RectangleF(0, 0.9f, 1, 0.08f); // Bottom 8%
            container.AddChild(buttonContainer);

            // Copy to Clipboard button - using same theme as Learn More button
            copyButton = new TextButtonNode("Copy to Clipboard", Theme.Current.InfoPanel.Heading.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.HeadingText.XNA);
            copyButton.RelativeBounds = new RectangleF(0, 0, 0.5f, 1);
            copyButton.OnClick += CopyButton_OnClick;
            buttonContainer.AddChild(copyButton);

            // Close button - using same theme as Learn More button
            var closeButton = new TextButtonNode("Close", Theme.Current.InfoPanel.Heading.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.HeadingText.XNA);
            closeButton.RelativeBounds = new RectangleF(0.5f, 0, 0.5f, 1);
            closeButton.OnClick += (mie) => Dispose();
            buttonContainer.AddChild(closeButton);

            // Generate initial chapter markers
            GenerateChapterMarkers();
            }
            catch (Exception ex)
            {
                // If constructor fails, dispose any created nodes to prevent memory leaks
                System.Diagnostics.Debug.WriteLine($"ChapterMarkerDialog constructor failed: {ex.Message}");
                Dispose();
                throw;
            }
            
            // Update YouTube URLs since stream reference time has changed
            UpdateAllYouTubeUrlDisplays();
        }

        private void RebuildRaceList()
        {
            if (raceList == null) return;

            Logger.UI.Log(this, $"RebuildRaceList: Starting with {raceList.Children.Count()} existing children", Logger.LogType.Notice);

            // Clear existing race list and dispose old nodes properly
            var existingChildren = raceList.Children.ToArray(); // Make a copy first
            raceList.RemoveChild(existingChildren);
            
            // Clear the display dictionaries (time inputs are synchronized automatically now)
            raceNameDisplays.Clear();
            
            // Dispose the old race nodes and their children to prevent memory leaks
            foreach (var child in existingChildren)
            {
                // Remove TimeInputNode and IntegerInputNode objects from the child before disposing it
                var inputNodes = child.Children.OfType<IntegerInputNode>().ToArray();
                foreach (var inputNode in inputNodes)
                {
                    inputNode.Remove(); // Remove from parent but don't dispose
                }
                
                var timeInputNodes = child.Children.OfType<HMSTimeInputNode>().ToArray();
                foreach (var timeInputNode in timeInputNodes)
                {
                    timeInputNode.Remove(); // Remove from parent but don't dispose
                }
                
                // Now dispose the child and its remaining children (TextNodes, etc.)
                child.Dispose();
            }

            // Get races to display based on current filter
            var racesToShow = GetFilteredRaces();
            
            Logger.UI.Log(this, $"RebuildRaceList: Got {racesToShow.Count} races to show", Logger.LogType.Notice);

            foreach (var race in racesToShow)
            {
                var raceNode = new Node();
                raceNode.RelativeBounds = new RectangleF(0, 0, 1, 0.06f); // Height for each row

                // Create race display name
                var raceDisplayName = $"Round {race.RoundNumber} - {race.Type} {race.RoundNumber}-{race.RaceNumber}";

                // Editable time input (left side, 20% width) - replaces the clickable offset display
                var timeInput = raceTimeInputs[race];
                timeInput.RelativeBounds = new RectangleF(0, 0, 0.2f, 1); // 20% width for time input
                
                // Remove from previous parent if it has one
                if (timeInput.Parent != null)
                {
                    timeInput.Remove();
                }
                
                // Set up the time input with cumulative time from stream start
                var cumulativeTimeFromStart = CalculateRaceOffsetTime(race);
                timeInput.Value = cumulativeTimeFromStart;
                
                raceNode.AddChild(timeInput);
                
                // Race name display (clickable, first part of remaining space) - adjusted for new layout
                var raceNameDisplay = new ClickableTextNode(raceDisplayName, Color.LightBlue); // Use light blue to indicate it's clickable
                raceNameDisplay.RelativeBounds = new RectangleF(0.21f, 0.2f, 0.34f, 0.6f); // Start at 21%, take 34% width (55% - 21%)
                raceNameDisplay.Alignment = RectangleAlignment.CenterLeft; // Left align the text
                raceNameDisplay.OnClick += () => OpenYouTubeUrl(race); // Add click handler for hyperlink
                raceNode.AddChild(raceNameDisplay);
                
                // Store references to update displays when offsets change (time input will be updated by synchronization)
                raceNameDisplays[race] = raceNameDisplay;

                // YouTube URL display (clickable, second part of remaining space) - new column
                var offsetTime = CalculateRaceOffsetTime(race);
                int offsetSeconds = (int)offsetTime.TotalSeconds;
                string urlWithTimestamp = GenerateYouTubeUrl(offsetSeconds);
                var youtubeUrlDisplay = new ClickableTextNode(urlWithTimestamp, Color.LightGreen); // Use light green to differentiate from race name
                youtubeUrlDisplay.RelativeBounds = new RectangleF(0.56f, 0.2f, 0.33f, 0.6f); // Start at 56%, take 33% width (89% - 56%)
                youtubeUrlDisplay.Alignment = RectangleAlignment.CenterLeft; // Left align the text
                youtubeUrlDisplay.OnClick += () => OpenYouTubeUrl(race); // Same click handler as race name
                raceNode.AddChild(youtubeUrlDisplay);
                
                // Store reference for the YouTube URL display
                youtubeUrlDisplays[race] = youtubeUrlDisplay;

                // Add offset input (right-aligned, 10% width) - adjusted for new layout
                var raceOffset = raceOffsets[race];
                raceOffset.RelativeBounds = new RectangleF(0.9f, 0, 0.1f, 1); // Start at 90%, take 10% width (right-aligned)
                
                // Remove from previous parent if it has one
                if (raceOffset.Parent != null)
                {
                    raceOffset.Remove();
                }
                
                // Calculate the correct offset input value (change from original start time)
                // Same logic as CreateRaceOffsetNode
                var firstRaceInView = racesToShow.OrderBy(r => r.Start).First();
                var originalTimeFromStreamStart = (originalHeatOffsets[race] - firstRaceInView.Start.TimeOfDay).TotalSeconds;
                var currentTimeFromStreamStart = cumulativeTimeFromStart.TotalSeconds;
                var actualOffsetFromOriginal = currentTimeFromStreamStart - originalTimeFromStreamStart;
                
                // Set up the offset node with the actual offset from original (not cumulative time)
                raceOffset.Value = (int)Math.Round(actualOffsetFromOriginal);
                
                raceNode.AddChild(raceOffset);

                raceList.AddChild(raceNode);
            }
            
            Logger.UI.Log(this, $"RebuildRaceList: Finished, raceList now has {raceList.Children.Count()} children", Logger.LogType.Notice);
            
            // Force layout and redraw updates like in LogNode
            raceList.RequestLayout();
            raceList.RequestRedraw();
            RequestRedraw(); // Also request redraw on the dialog itself
            
            Logger.UI.Log(this, "RebuildRaceList: Requested layout and redraw updates", Logger.LogType.Notice);
        }

        private List<Race> GetFilteredRaces()
        {
            if (selectedRace == null)
            {
                // Show all races
                var allRaces = availableRaces.OrderBy(r => r.Start).ToList();
                Logger.UI.Log(this, $"GetFilteredRaces: No selection, returning all {allRaces.Count} races", Logger.LogType.Notice);
                return allRaces;
            }
            else
            {
                // Show selected race and races after it
                var races = availableRaces.OrderBy(r => r.Start).ToList();
                
                // Find the race using reference equality (should work now since we use matching reference)
                var selectedIndex = races.IndexOf(selectedRace);
                
                Logger.UI.Log(this, $"GetFilteredRaces: selectedRace={selectedRace?.RaceName} (#{selectedRace?.RaceNumber}), selectedIndex={selectedIndex}, totalRaces={races.Count}", Logger.LogType.Notice);
                
                if (selectedIndex >= 0)
                {
                    var filteredRaces = races.Skip(selectedIndex).ToList();
                    Logger.UI.Log(this, $"GetFilteredRaces: returning {filteredRaces.Count} races starting from index {selectedIndex}", Logger.LogType.Notice);
                    return filteredRaces;
                }
                
                Logger.UI.Log(this, "GetFilteredRaces: selectedRace not found, returning all races as fallback", Logger.LogType.Notice);
                return races; // Return all races as fallback instead of empty list
            }
        }

        private void RaceSelector_OnSelectionChanged(Race selectedRace)
        {
            Logger.UI.Log(this, $"RaceSelector_OnSelectionChanged: selectedRace={selectedRace?.RaceName}, RaceNumber={selectedRace?.RaceNumber}", Logger.LogType.Notice);
            
            // If selectedRace is not null, find the matching race in availableRaces by properties
            if (selectedRace != null)
            {
                var matchingRace = availableRaces.FirstOrDefault(r => 
                    r.RaceNumber == selectedRace.RaceNumber && 
                    r.Start == selectedRace.Start &&
                    string.Equals(r.RaceName, selectedRace.RaceName, StringComparison.OrdinalIgnoreCase));
                
                if (matchingRace != null)
                {
                    this.selectedRace = matchingRace; // Use the reference from availableRaces
                    Logger.UI.Log(this, $"Found matching race in availableRaces: {matchingRace.RaceName}", Logger.LogType.Notice);
                }
                else
                {
                    Logger.UI.Log(this, $"No matching race found in availableRaces for {selectedRace.RaceName}", Logger.LogType.Notice);
                    this.selectedRace = null; // Fall back to showing all races
                }
            }
            else
            {
                this.selectedRace = null; // "All Races" selected
                Logger.UI.Log(this, "All Races selected", Logger.LogType.Notice);
            }
            
            try
            {
                // Update the stream reference time based on the selected race
                UpdateStreamReferenceTime(this.selectedRace);
                
                // Rebuild the race list with filtered races
                RebuildRaceList();
                
                // Scroll race list to top after rebuilding
                if (raceList?.Scroller != null)
                {
                    raceList.Scroller.ScrollTo(0);
                    Logger.UI.Log(this, "Scrolled race list to top", Logger.LogType.Notice);
                }
                
                // Regenerate chapter markers with the new filter
                GenerateChapterMarkers();
                
                // Update YouTube URL displays to reflect new offsets
                UpdateAllYouTubeUrlDisplays();
                
                // Scroll chapter markers (generated text) to top after regenerating
                if (chapterList?.Scroller != null)
                {
                    chapterList.Scroller.ScrollTo(0);
                    Logger.UI.Log(this, "Scrolled chapter markers to top", Logger.LogType.Notice);
                }
            }
            catch (Exception ex)
            {
                Logger.UI.Log(this, $"Error in race selector changed: {ex.Message}", Logger.LogType.Error);
            }
        }

        private void CopyButton_OnClick(MouseInputEvent mouseInputEvent)
        {
            var chapterTexts = new List<string>();
            
            foreach (var child in chapterList.Children)
            {
                if (child is TextNode textNode && !string.IsNullOrEmpty(textNode.Text))
                {
                    chapterTexts.Add(textNode.Text);
                }
            }
            
            if (chapterTexts.Any())
            {
                var combinedText = string.Join("\n", chapterTexts);
                PlatformTools.Clipboard.SetText(combinedText);
            }
        }

        private void GenerateChapterMarkers()
        {
            Logger.UI.Log(this, $"GenerateChapterMarkers: Starting with {chapterList.Children.Count()} existing nodes", Logger.LogType.Notice);
            
            // Clear existing chapter markers and dispose them properly
            var existingNodes = chapterList.Children.ToArray();
            chapterList.RemoveChild(existingNodes);
            foreach (var node in existingNodes)
            {
                node.Dispose();
            }
            
            Logger.UI.Log(this, $"GenerateChapterMarkers: Cleared existing nodes, now have {chapterList.Children.Count()} nodes", Logger.LogType.Notice);

            if (availableRaces == null || !availableRaces.Any())
            {
                var noRacesText = new TextNode("No races available.", Color.White);
                chapterList.AddChild(noRacesText);
                Logger.UI.Log(this, "GenerateChapterMarkers: No races available", Logger.LogType.Notice);
                return;
            }

            var racesToProcess = GetFilteredRaces();

            if (!racesToProcess.Any())
            {
                var noDisplayText = new TextNode("No races to display.", Color.White);
                chapterList.AddChild(noDisplayText);
                Logger.UI.Log(this, "GenerateChapterMarkers: No races to display", Logger.LogType.Notice);
                return;
            }

            // IMPORTANT: When a specific race is selected, that race becomes the reference point (0:00)
            // Use the filtered races to find the first race in the current view
            var firstRaceInView = racesToProcess.OrderBy(r => r.Start).First();
            var streamStartTime = firstRaceInView.Start;

            // Add the required "Stream Start" entry at 00:00:00
            var streamStartText = "00:00:00 Stream Start";
            var streamStartNode = new TextNode(streamStartText, Color.White);
            streamStartNode.Scale(0.5625f); // Same scale as other chapters
            streamStartNode.Alignment = RectangleAlignment.CenterLeft;
            chapterList.AddChild(streamStartNode);
            Logger.UI.Log(this, $"GenerateChapterMarkers: Added stream start: {streamStartText}", Logger.LogType.Notice);

            // Calculate cumulative offsets
            var allRacesInOrder = availableRaces.OrderBy(r => r.Start).ToList();

            foreach (var race in racesToProcess)
            {
                // Calculate time elapsed from the view start to this race
                var raceStartTime = race.Start;
                var elapsedFromStreamStart = raceStartTime - streamStartTime;
                
                // Calculate cumulative offset up to this race, starting from the first race in view
                var firstRaceIndex = allRacesInOrder.IndexOf(firstRaceInView);
                var currentRaceIndex = allRacesInOrder.IndexOf(race);
                var cumulativeOffsetForThisRace = TimeSpan.Zero;
                
                // Add up all offsets from races starting with the first race in view up to this race
                for (int i = firstRaceIndex; i <= currentRaceIndex; i++)
                {
                    var raceInSequence = allRacesInOrder[i];
                    cumulativeOffsetForThisRace += variableOffsets[raceInSequence];
                }
                
                var totalOffset = elapsedFromStreamStart + cumulativeOffsetForThisRace;
                
                Logger.UI.Log(this, $"GenerateChapterMarkers: Race {race.RaceName ?? race.RaceNumber.ToString()} - elapsed: {elapsedFromStreamStart.TotalSeconds}s, cumulative offset: {cumulativeOffsetForThisRace.TotalSeconds}s, total: {totalOffset.TotalSeconds}s", Logger.LogType.Notice);
                
                // Ensure we don't go negative
                if (totalOffset < TimeSpan.Zero)
                    totalOffset = TimeSpan.Zero;
                
                var timeString = FormatTimeForChapter(totalOffset);
                
                var raceName = race.RaceName ?? $"Round {race.RoundNumber} - Heat {race.RaceNumber}";
                
                // Replace race type names based on checkbox settings (reversed logic)
                if (race.Type == EventTypes.TimeTrial)
                {
                    if (!timeTrialAbbreviationCheckbox.Value)
                    {
                        raceName = raceName.Replace("Time Trial", "TT");
                    }
                    // If checked, leave "Time Trial" as is
                }
                else if (race.Type == EventTypes.Race)
                {
                    if (!raceAbbreviationCheckbox.Value)
                    {
                        raceName = raceName.Replace("Race", "R");
                    }
                    // If checked, leave "Race" as is
                }
                
                // Get pilot results for this race
                var pilotResults = GetPilotResults(race);
                
                var chapterText = $"{timeString} {raceName} | {pilotResults}";
                
                // Remove trailing pipe if pilot results are empty or only whitespace
                if (string.IsNullOrWhiteSpace(pilotResults))
                {
                    chapterText = $"{timeString} {raceName}";
                }
                // Also remove trailing " | " if it exists
                else if (chapterText.EndsWith(" | "))
                {
                    chapterText = chapterText.TrimEnd(' ', '|');
                }
                
                // Create a text node for this chapter marker with smaller scale and left alignment
                var chapterNode = new TextNode(chapterText, Color.White);
                chapterNode.Scale(0.5625f); // Shrink text to 75% of current size (0.75 * 0.75)
                chapterNode.Alignment = RectangleAlignment.CenterLeft; // Left align the text
                chapterList.AddChild(chapterNode);
                Logger.UI.Log(this, $"GenerateChapterMarkers: Added chapter node: {chapterText}", Logger.LogType.Notice);
            }
            
            // Force layout update and log final state
            Logger.UI.Log(this, $"GenerateChapterMarkers: Final chapterList children count: {chapterList.Children.Count()}", Logger.LogType.Notice);
            Logger.UI.Log(this, $"GenerateChapterMarkers: chapterList bounds: {chapterList.RelativeBounds}", Logger.LogType.Notice);
            Logger.UI.Log(this, $"GenerateChapterMarkers: chapterList ItemHeight: {chapterList.ItemHeight}, ItemPadding: {chapterList.ItemPadding}", Logger.LogType.Notice);
            
            // Force layout and redraw updates like in LogNode
            chapterList.RequestLayout();
            chapterList.RequestRedraw();
            RequestRedraw(); // Also request redraw on the dialog itself
            
            Logger.UI.Log(this, "GenerateChapterMarkers: Requested layout and redraw updates", Logger.LogType.Notice);
            
            // Generate pilot performance leaderboard if enabled
            GeneratePilotPerformanceLeaderboard();
            
            // Update character count
            UpdateCharacterCount();
        }

        private void UpdateCharacterCount()
        {
            var chapterTexts = new List<string>();
            
            foreach (var child in chapterList.Children)
            {
                if (child is TextNode textNode && !string.IsNullOrEmpty(textNode.Text))
                {
                    chapterTexts.Add(textNode.Text);
                }
            }
            
            var combinedText = string.Join("\n", chapterTexts);
            var characterCount = combinedText.Length;
            
            if (characterCountLabel != null)
            {
                characterCountLabel.Text = $"{characterCount}/5000";
            }
        }

        private void UpdateYouTubeUrlDisplay(Race race)
        {
            if (youtubeUrlDisplays.ContainsKey(race))
            {
                var offsetTime = CalculateRaceOffsetTime(race);
                int offsetSeconds = (int)offsetTime.TotalSeconds;
                string urlWithTimestamp = GenerateYouTubeUrl(offsetSeconds);
                youtubeUrlDisplays[race].Text = urlWithTimestamp;
            }
        }

        private void UpdateAllYouTubeUrlDisplays()
        {
            foreach (var race in youtubeUrlDisplays.Keys.ToList())
            {
                UpdateYouTubeUrlDisplay(race);
            }
        }

        private void GeneratePilotPerformanceLeaderboard()
        {
            if (!pilotSummaryCheckbox.Value)
                return;

            // Get all Time Trial races
            var timeTrialRaces = availableRaces.Where(r => r.Type == EventTypes.TimeTrial).ToList();
            
            if (!timeTrialRaces.Any())
                return;

            // Dictionary to store each pilot's best performance
            var pilotBestTimes = new Dictionary<Pilot, (TimeSpan bestTime, Race bestRace)>();

            // Process each Time Trial race to find best times per pilot
            foreach (var race in timeTrialRaces)
            {
                // Get all pilots who participated in this race
                var pilots = race.Pilots.Where(p => p != null).ToList();
                
                foreach (var pilot in pilots)
                {
                    // Get all laps for this pilot in this race
                    var allLaps = race.GetValidLaps(pilot, false);
                    
                    if (allLaps == null || !allLaps.Any())
                        continue;
                    
                    // Get best consecutive laps (same logic as used in chapter markers)
                    var bestConsecutiveLaps = allLaps.BestConsecutive(eventManager.Event.Laps);
                    if (bestConsecutiveLaps.Any())
                    {
                        var totalTime = bestConsecutiveLaps.TotalTime();
                        
                        // Check if this is the pilot's best time so far
                        if (!pilotBestTimes.ContainsKey(pilot) || totalTime < pilotBestTimes[pilot].bestTime)
                        {
                            pilotBestTimes[pilot] = (totalTime, race);
                        }
                    }
                }
            }

            // Sort pilots by best time
            var sortedPilots = pilotBestTimes.OrderBy(kvp => kvp.Value.bestTime).ToList();

            if (!sortedPilots.Any())
                return;

            // Add section header
            var headerText = new TextNode("", Color.White);
            headerText.Text = "\nPilot Performance Leaderboard:";
            headerText.Alignment = RectangleAlignment.CenterLeft;
            chapterList.AddChild(headerText);

            var separatorText = new TextNode("", Color.White);
            separatorText.Text = "===================================";
            separatorText.Alignment = RectangleAlignment.CenterLeft;
            chapterList.AddChild(separatorText);

            // Generate leaderboard entries
            int position = 1;
            foreach (var pilotEntry in sortedPilots)
            {
                var pilot = pilotEntry.Key;
                var bestTime = pilotEntry.Value.bestTime;
                var bestRace = pilotEntry.Value.bestRace;

                // Calculate the time this race appears in the stream
                var raceOffsetTime = CalculateRaceOffsetTime(bestRace);
                
                // Format the time as H:MM:SS or MM:SS
                string timeString;
                if (raceOffsetTime.Hours > 0)
                {
                    timeString = $"{raceOffsetTime.Hours}:{raceOffsetTime.Minutes:D2}:{raceOffsetTime.Seconds:D2}";
                }
                else
                {
                    timeString = $"{raceOffsetTime.Minutes}:{raceOffsetTime.Seconds:D2}";
                }

                // Format race name
                string raceDisplayName = $"Time Trial {bestRace.RoundNumber}-{bestRace.RaceNumber}";

                // Create leaderboard entry
                var entryText = new TextNode("", Color.White);
                entryText.Text = $"{position}. {pilot.Name}: {bestTime.TotalSeconds:F2}s (at {timeString} - {raceDisplayName})";
                entryText.Alignment = RectangleAlignment.CenterLeft;
                chapterList.AddChild(entryText);

                position++;
            }
        }

        private TimeSpan CalculateRaceOffsetTime(Race race)
        {
            if (availableRaces == null || !availableRaces.Any())
                return TimeSpan.Zero;

            var racesToProcess = GetFilteredRaces();
            if (!racesToProcess.Any())
                return TimeSpan.Zero;

            // IMPORTANT: When a specific race is selected, that race becomes the reference point (0:00)
            // Use the filtered races to find the first race in the current view
            var firstRaceInView = racesToProcess.OrderBy(r => r.Start).First();
            var streamStartTime = firstRaceInView.Start;

            // Calculate time elapsed from the view start to this race
            var raceStartTime = race.Start;
            var elapsedFromStreamStart = raceStartTime - streamStartTime;
            
            // Calculate cumulative offset up to this race, starting from the first race in view
            var allRacesInOrder = availableRaces.OrderBy(r => r.Start).ToList();
            var cumulativeOffsetForThisRace = TimeSpan.Zero;
            
            // Add up all offsets from races starting with the first race in view up to this race
            var firstRaceIndex = allRacesInOrder.IndexOf(firstRaceInView);
            var currentRaceIndex = allRacesInOrder.IndexOf(race);
            
            for (int i = firstRaceIndex; i <= currentRaceIndex; i++)
            {
                var raceInSequence = allRacesInOrder[i];
                cumulativeOffsetForThisRace += variableOffsets[raceInSequence];
            }
            
            var totalOffset = elapsedFromStreamStart + cumulativeOffsetForThisRace;
            
            Logger.UI.Log(this, $"CalculateRaceOffsetTime: Race {race.RaceName ?? race.RaceNumber.ToString()} - elapsed: {elapsedFromStreamStart.TotalSeconds}s, cumulative offset: {cumulativeOffsetForThisRace.TotalSeconds}s, total: {totalOffset.TotalSeconds}s", Logger.LogType.Notice);
            
            // Ensure we don't go negative
            if (totalOffset < TimeSpan.Zero)
                totalOffset = TimeSpan.Zero;
                
            return totalOffset;
        }

        private string FormatTimeForChapter(TimeSpan time)
        {
            // Format: "HH:MM:SS" for chapter markers (YouTube standard format with leading zeros)
            int totalSeconds = (int)time.TotalSeconds;
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }

        private int CalculateTimeOffsetInSeconds(Race race)
        {
            // Calculate the offset time for this race and return total seconds
            var offsetTime = CalculateRaceOffsetTime(race);
            return (int)offsetTime.TotalSeconds;
        }

        private string CleanYouTubeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            // Remove &t= or ?t= parameter and its value (handles both t=123 and t=123s formats)
            // This regex matches &t= or ?t= followed by digits and optionally 's'
            var cleanedUrl = System.Text.RegularExpressions.Regex.Replace(url, @"[&?]t=\d+s?", "");
            
            // Clean up any double && or ?& combinations that might result
            cleanedUrl = cleanedUrl.Replace("&&", "&").Replace("?&", "?");
            
            // If the URL ends with & or ?, remove it
            cleanedUrl = cleanedUrl.TrimEnd('&', '?');
            
            return cleanedUrl;
        }

        private string GenerateYouTubeUrl(int offsetSeconds)
        {
            // Get the base URL from the textbox (it should already be cleaned)
            string baseUrl = youtubeUrlInput?.Text?.Trim() ?? "https://www.youtube.com/watch?v=XFODx1hxC5o";
            
            // If URL already has parameters, use & otherwise use ?
            string separator = baseUrl.Contains("?") ? "&" : "?";
            
            return $"{baseUrl}{separator}t={offsetSeconds}";
        }

        private void OpenYouTubeUrl(Race race)
        {
            try
            {
                int offsetSeconds = CalculateTimeOffsetInSeconds(race);
                string url = GenerateYouTubeUrl(offsetSeconds);
                
                Logger.UI.Log(this, $"Opening YouTube URL: {url}", Logger.LogType.Notice);
                ExternalData.DataTools.StartBrowser(url);
            }
            catch (Exception ex)
            {
                Logger.UI.LogException(this, ex);
            }
        }

        private string GetPilotResults(Race race)
        {
            var results = new List<string>();
            
            // Get all pilots who participated in this race
            var pilots = race.Pilots.Where(p => p != null).ToList();
            
            if (race.Type == EventTypes.TimeTrial)
            {
                // Check if we should show anything for Time Trial races
                if (!timeTrialPilotNamesCheckbox.Value && !timeTrialTimesCheckbox.Value)
                {
                    return ""; // Show nothing if both checkboxes are unchecked
                }
                
                // For Time Trial races, show times and check for personal bests
                var pilotTimes = new List<(Pilot pilot, TimeSpan totalTime, bool hasValidTime, bool isPB)>();
                
                foreach (var pilot in pilots)
                {
                    TimeSpan totalTime = TimeSpan.Zero;
                    bool hasValidTime = false;
                    
                    if (eventManager?.Event != null)
                    {
                        // Use best consecutive laps as configured for the event
                        // If pilot doesn't have enough consecutive laps, they get DNF (hasValidTime = false)
                        var allLaps = race.GetValidLaps(pilot, false);
                        if (allLaps.Any())
                        {
                            var bestConsecutiveLaps = allLaps.BestConsecutive(eventManager.Event.Laps);
                            if (bestConsecutiveLaps.Any())
                            {
                                totalTime = bestConsecutiveLaps.TotalTime();
                                hasValidTime = true; // Only set true if we have enough consecutive laps
                            }
                        }
                    }
                    
                    bool isPB = hasValidTime && IsPersonalBest(pilot, totalTime, race);
                    pilotTimes.Add((pilot, totalTime, hasValidTime, isPB));
                }
                
                // Sort by total time (fastest first)
                pilotTimes = pilotTimes.OrderBy(pt => pt.hasValidTime ? pt.totalTime : TimeSpan.MaxValue).ToList();
                
                // Format the results with times
                foreach (var (pilot, totalTime, hasValidTime, isPB) in pilotTimes)
                {
                    var resultParts = new List<string>();
                    
                    // Add pilot name if checkbox is checked
                    if (timeTrialPilotNamesCheckbox.Value)
                    {
                        var pilotName = pilot.Name;
                        // Add PB flame to pilot name ONLY if:
                        // 1. Pilot has valid consecutive laps (hasValidTime = true)
                        // 2. This is their personal best time (isPB = true)
                        // Pilots with DNF (hasValidTime = false) never get flames
                        if (hasValidTime && isPB)
                        {
                            pilotName += "🔥";
                        }
                        resultParts.Add(pilotName);
                    }
                    
                    // Add time if checkbox is checked
                    if (timeTrialTimesCheckbox.Value)
                    {
                        if (hasValidTime)
                        {
                            var timeString = $"{totalTime.TotalSeconds:F2}";
                            resultParts.Add(timeString);
                        }
                        else
                        {
                            resultParts.Add("DNF");
                        }
                    }
                    
                    // Join the parts with space
                    if (resultParts.Count > 0)
                    {
                        results.Add(string.Join(" ", resultParts));
                    }
                }
            }
            else
            {
                // Check if we should show anything for Race events
                if (!racePilotNamesCheckbox.Value && !racePositionsCheckbox.Value)
                {
                    return ""; // Show nothing if both checkboxes are unchecked
                }
                
                // For other race types, show positions
                var pilotResults = new List<(Pilot pilot, int position, bool dnf)>();
                
                foreach (var pilot in pilots)
                {
                    // Get the race result for this pilot
                    var result = eventManager?.ResultManager?.GetResult(race, pilot);
                    
                    if (result != null)
                    {
                        pilotResults.Add((pilot, result.Position, result.DNF));
                    }
                }
                
                // Sort by position
                pilotResults = pilotResults.OrderBy(pr => pr.position).ToList();
                
                // Format the results with positions
                foreach (var (pilot, position, dnf) in pilotResults)
                {
                    var resultParts = new List<string>();
                    
                    // Add pilot name if checkbox is checked
                    if (racePilotNamesCheckbox.Value)
                    {
                        resultParts.Add(pilot.Name);
                    }
                    
                    // Add position if checkbox is checked  
                    if (racePositionsCheckbox.Value)
                    {
                        if (dnf)
                        {
                            resultParts.Add("DNF");
                        }
                        else
                        {
                            string positionText = "";
                            switch (position)
                            {
                                case 1: positionText = "1st"; break;
                                case 2: positionText = "2nd"; break;
                                case 3: positionText = "3rd"; break;
                                default: positionText = $"{position}th"; break;
                            }
                            resultParts.Add(positionText);
                        }
                    }
                    
                    // Join the parts with space
                    if (resultParts.Count > 0)
                    {
                        results.Add(string.Join(" ", resultParts));
                    }
                }
            }
            
            return string.Join(" | ", results);
        }

        private bool IsPersonalBest(Pilot pilot, TimeSpan totalTime, Race currentRace)
        {
            // Get all Time Trial races for this pilot across all available races
            var allTimeTrialRaces = availableRaces.Where(r => r.Type == EventTypes.TimeTrial).ToList();
            
            TimeSpan bestTime = TimeSpan.MaxValue;
            Race bestRace = null;
            
            // Find the pilot's best time across all Time Trial races
            foreach (var race in allTimeTrialRaces)
            {
                TimeSpan raceTime = TimeSpan.Zero;
                bool hasValidTime = false;
                
                if (eventManager?.Event != null)
                {
                    // Use best consecutive laps as configured for the event
                    var allLaps = race.GetValidLaps(pilot, false);
                    if (allLaps.Any())
                    {
                        var bestConsecutiveLaps = allLaps.BestConsecutive(eventManager.Event.Laps);
                        if (bestConsecutiveLaps.Any())
                        {
                            raceTime = bestConsecutiveLaps.TotalTime();
                            hasValidTime = true;
                        }
                    }
                }
                
                // Update best time if this race has a better time
                if (hasValidTime && raceTime < bestTime)
                {
                    bestTime = raceTime;
                    bestRace = race;
                }
            }
            
            // Return true only if this race is the one with the pilot's best time
            // and the total time matches the best time
            return bestRace == currentRace && totalTime == bestTime;
        }

        private void CreateRaceOffsetNode(Race race)
        {
            // Calculate the current cumulative time from stream start for this race
            var cumulativeTimeFromStart = CalculateRaceOffsetTime(race);
            
            // Calculate what the integer offset should be - this should show the difference
            // between the current displayed time and the original race start time
            var racesToProcess = GetFilteredRaces();
            var firstRaceInView = racesToProcess.OrderBy(r => r.Start).First();
            var originalTimeFromStreamStart = (originalHeatOffsets[race] - firstRaceInView.Start.TimeOfDay).TotalSeconds;
            var currentTimeFromStreamStart = cumulativeTimeFromStart.TotalSeconds;
            var actualOffsetFromOriginal = currentTimeFromStreamStart - originalTimeFromStreamStart;
            
            // Create integer offset input (seconds) - shows actual change from original start time
            var offsetNode = new IntegerInputNode((int)Math.Round(actualOffsetFromOriginal), Theme.Current.InfoPanel.Heading.XNA, Theme.Current.InfoPanel.HeadingText.XNA);
            
            // Create time input (HH:MM:SS format) - initialize with cumulative time from stream start
            var timeNode = new HMSTimeInputNode(cumulativeTimeFromStart, Theme.Current.InfoPanel.Heading.XNA, Theme.Current.InfoPanel.HeadingText.XNA);
            
            // Store the race in a local variable to avoid closure issues
            Race currentRace = race;
            
            // Handle offset input changes -> treat input as change from original start time
            offsetNode.OnValueChangedWithSender += (sender, val) =>
            {
                // The user entered a change from the original start time in seconds
                // Convert this directly to the individual race offset
                var newIndividualOffset = TimeSpan.FromSeconds(val);
                
                // Store the previous offset for change calculation
                var previousOffset = variableOffsets[currentRace];
                var offsetChange = newIndividualOffset - previousOffset;
                
                // Update this race's individual offset
                variableOffsets[currentRace] = newIndividualOffset;
                
                // Apply the offset change to all subsequent races
                ApplyOffsetToSubsequentRaces(currentRace, offsetChange);
                
                // Regenerate chapter markers with updated offsets
                GenerateChapterMarkers();
                
                // Update YouTube URL displays to reflect new offsets
                UpdateAllYouTubeUrlDisplays();
            };
            
            // Handle time input changes -> convert to individual race offset and propagate changes
            timeNode.OnTimeChangedWithSender += (sender, newTimeFromStart) =>
            {
                // The user entered a cumulative time from stream start in HH:MM:SS format
                // We need to convert this back to an individual offset for this race
                var targetCumulativeTime = newTimeFromStart;
                
                // Use the exact same logic as CalculateRaceOffsetTime to ensure consistency
                var racesToProcess = GetFilteredRaces();
                var firstRaceInView = racesToProcess.OrderBy(r => r.Start).First();
                var streamStartTime = firstRaceInView.Start;
                var elapsedFromStreamStart = currentRace.Start - streamStartTime;
                
                // The target formula is: targetCumulativeTime = elapsedFromStreamStart + cumulativeOffsetUpToThisRace
                // So: cumulativeOffsetUpToThisRace = targetCumulativeTime - elapsedFromStreamStart
                var targetCumulativeOffset = targetCumulativeTime - elapsedFromStreamStart;
                
                // Calculate cumulative offset from all previous races (same logic as CalculateRaceOffsetTime)
                var allRacesInOrder = availableRaces.OrderBy(r => r.Start).ToList();
                var firstRaceIndex = allRacesInOrder.IndexOf(firstRaceInView);
                var currentRaceIndex = allRacesInOrder.IndexOf(currentRace);
                
                var cumulativeOffsetFromPreviousRaces = TimeSpan.Zero;
                for (int i = firstRaceIndex; i < currentRaceIndex; i++) // Only previous races
                {
                    var raceInSequence = allRacesInOrder[i];
                    cumulativeOffsetFromPreviousRaces += variableOffsets[raceInSequence];
                }
                
                // Calculate what this race's individual offset should be
                var newIndividualOffset = targetCumulativeOffset - cumulativeOffsetFromPreviousRaces;
                
                // Store the previous offset for change calculation
                var previousOffset = variableOffsets[currentRace];
                var offsetChange = newIndividualOffset - previousOffset;
                
                // Update this race's individual offset
                variableOffsets[currentRace] = newIndividualOffset;
                
                // Update the integer offset display to show change from original time
                if (raceOffsets.ContainsKey(currentRace))
                {
                    raceOffsets[currentRace].Value = (int)newIndividualOffset.TotalSeconds;
                }
                
                // Apply the offset change to all subsequent races
                ApplyOffsetToSubsequentRaces(currentRace, offsetChange);
                
                // Regenerate chapter markers with updated offsets
                GenerateChapterMarkers();
                
                // Update YouTube URL displays to reflect new offsets
                UpdateAllYouTubeUrlDisplays();
            };
            
            raceOffsets[race] = offsetNode;
            raceTimeInputs[race] = timeNode;
        }

        private void UpdateRaceOffsetTimeDisplays()
        {
            // Time inputs are now automatically synchronized through CreateRaceOffsetNode method
            // The TimeInputNode values are updated directly when offsets change
            // No need to manually update displays since synchronization handles this
            
            // Update YouTube URLs for all affected races
            UpdateAllYouTubeUrlDisplays();
        }

        private void UpdateStreamReferenceTime(Race newReferenceRace)
        {
            // CRITICAL: When the reference race changes, we need to reset all individual race offsets
            // because they were calculated relative to the previous reference point
            
            // Reset all individual race offsets to zero since we have a new reference point
            var racesToReset = availableRaces?.ToList() ?? new List<Race>();
            foreach (var race in racesToReset)
            {
                variableOffsets[race] = TimeSpan.Zero;
            }
            
            // Update all UI textboxes to reflect the reset offsets
            foreach (var race in racesToReset)
            {
                // Update integer offset display (should be 0 since we reset to zero)
                if (raceOffsets.ContainsKey(race))
                {
                    raceOffsets[race].Value = 0;
                }
                
                // Update time display with recalculated cumulative time
                if (raceTimeInputs.ContainsKey(race))
                {
                    var newCumulativeTime = CalculateRaceOffsetTime(race);
                    raceTimeInputs[race].Value = newCumulativeTime;
                }
            }
            
            // The reference point is now determined by GetFilteredRaces() based on the selection
            // No need to manually set streamStartReferenceTime as it's not used by CalculateRaceOffsetTime
            
            // Recalculate and update all displays using the new reference point
            foreach (var race in availableRaces)
            {
                if (raceTimeInputs.ContainsKey(race))
                {
                    // Calculate the cumulative time from stream start for this race
                    // This will now use the new reference point from GetFilteredRaces()
                    var cumulativeTimeFromStart = CalculateRaceOffsetTime(race);
                    
                    // Update the time input to show this cumulative time
                    raceTimeInputs[race].Value = cumulativeTimeFromStart;
                    
                    // Update the offset input to show the change from original start time (individual offset)
                    if (raceOffsets.ContainsKey(race))
                    {
                        raceOffsets[race].Value = (int)variableOffsets[race].TotalSeconds;
                    }
                }
            }
        }

        private void ApplyOffsetToSubsequentRaces(Race changedRace, TimeSpan offsetChange)
        {
            // Only apply to subsequent races if the offset change is not zero
            if (offsetChange == TimeSpan.Zero) return;
            
            Logger.UI.Log(this, $"ApplyOffsetToSubsequentRaces: Applying {offsetChange.TotalSeconds}s offset change from race {changedRace.RaceName ?? changedRace.RaceNumber.ToString()}", Logger.LogType.Notice);
            
            // Update all race time displays using cumulative offset logic (like CalculateRaceOffsetTime)
            // This ensures all subsequent races show the correct cumulative time from stream start
            foreach (var race in availableRaces)
            {
                if (raceTimeInputs.ContainsKey(race))
                {
                    // Calculate the cumulative time from stream start for this race
                    var cumulativeTimeFromStart = CalculateRaceOffsetTime(race);
                    
                    // Update the time input to show this cumulative time
                    raceTimeInputs[race].Value = cumulativeTimeFromStart;
                    
                    // Update the offset input to show the change from original start time (individual offset)
                    if (raceOffsets.ContainsKey(race))
                    {
                        raceOffsets[race].Value = (int)variableOffsets[race].TotalSeconds;
                    }
                }
            }
        }



        public override void Dispose()
        {
            // Clean up chapter list TextNode objects
            if (chapterList != null)
            {
                var chapterNodes = chapterList.Children.ToArray();
                foreach (var node in chapterNodes)
                {
                    node.Dispose();
                }
                chapterList.RemoveChild(chapterNodes);
            }

            // Properly dispose of all tracked input nodes
            foreach (var kvp in raceOffsets)
            {
                kvp.Value?.Dispose();
            }
            raceOffsets.Clear();

            foreach (var kvp in raceTimeInputs)
            {
                kvp.Value?.Dispose();
            }
            raceTimeInputs.Clear();

            foreach (var kvp in raceNameDisplays)
            {
                kvp.Value?.Dispose();
            }
            raceNameDisplays.Clear();

            foreach (var kvp in youtubeUrlDisplays)
            {
                kvp.Value?.Dispose();
            }
            youtubeUrlDisplays.Clear();

            // Clean up race list
            if (raceList != null)
            {
                var existingChildren = raceList.Children.ToArray();
                raceList.RemoveChild(existingChildren);
                
                foreach (var child in existingChildren)
                {
                    child.Dispose();
                }
            }
            
            base.Dispose();
        }
    }
} 