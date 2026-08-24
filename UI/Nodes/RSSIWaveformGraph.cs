using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;

namespace UI.Nodes
{
    // Plots an RSSI-over-time trace with enter/exit threshold lines and vertical lap-crossing
    // markers, matching what RotorHazard's own Marshal page graph shows. Also matches RH's
    // graph interactions: drag on the graph moves whichever threshold (enter/exit) is closer
    // to the cursor, and a tap (click without dragging) selects the nearest lap crossing.
    public class RSSIWaveformGraph : GraphNode
    {
        public int EnterAt { get; private set; }
        public int ExitAt { get; private set; }

        // Fired continuously while the user drags a threshold line, with the live values.
        public event Action<int, int> ThresholdsDragged;

        // Fired when the user taps (clicks without dragging) near a lap crossing.
        public event Action<TimeSpan> LapTapped;

        private IReadOnlyList<TimeSpan> times;
        private List<TimeSpan> lapCrossingTimes;

        private int minRssiBound;
        private int maxRssiBound;

        private bool dragging;
        private bool draggingEnter;
        private bool hasDragged;
        private Point pressPosition;

        private readonly TextNode enterLabel;
        private readonly TextNode exitLabel;

        public RSSIWaveformGraph()
        {
            enterLabel = new TextNode("", Color.OrangeRed);
            enterLabel.Alignment = RectangleAlignment.BottomLeft;
            AddChild(enterLabel);

            exitLabel = new TextNode("", Color.Yellow);
            exitLabel.Alignment = RectangleAlignment.BottomLeft;
            AddChild(exitLabel);
        }

        public void SetWaveform(IReadOnlyList<TimeSpan> times, IReadOnlyList<int> values, int enterAt, int exitAt, IEnumerable<TimeSpan> lapCrossings)
        {
            Clear();

            this.times = times;
            this.lapCrossingTimes = lapCrossings?.ToList() ?? new List<TimeSpan>();
            EnterAt = enterAt;
            ExitAt = exitAt;

            if (times == null || times.Count == 0)
                return;

            float minTime = (float)times[0].TotalSeconds;
            float maxTime = (float)times[times.Count - 1].TotalSeconds;

            minRssiBound = Math.Min(values.Min(), exitAt);
            maxRssiBound = Math.Max(values.Max(), enterAt);

            GraphSeries rssiSeries = GetCreateSeries("RSSI", Color.LightGreen);
            for (int i = 0; i < times.Count; i++)
            {
                rssiSeries.AddPoint((float)times[i].TotalSeconds, values[i]);
            }

            DrawThresholds(minTime, maxTime);

            int lapIndex = 0;
            foreach (TimeSpan crossing in lapCrossingTimes)
            {
                GraphSeries marker = GetCreateSeries("Lap" + lapIndex, Color.White);
                float x = (float)crossing.TotalSeconds;
                marker.AddPoint(x, minRssiBound);
                marker.AddPoint(x, maxRssiBound);
                lapIndex++;
            }

            View = new RectangleF(minTime, maxRssiBound, maxTime - minTime, minRssiBound - maxRssiBound);
        }

        // Highlights (or clears, when crossingTime is null) the lap at the given time, so a
        // click on a lap row in the table can be reflected back onto the graph.
        public void SetHighlightedLap(TimeSpan? crossingTime)
        {
            GraphSeries highlight = GetCreateSeries("Highlight", Color.Cyan);
            highlight.Thickness = 4f;
            highlight.Clear();

            if (crossingTime.HasValue)
            {
                float x = (float)crossingTime.Value.TotalSeconds;
                highlight.AddPoint(x, minRssiBound);
                highlight.AddPoint(x, maxRssiBound);
            }
        }

        private void DrawThresholds(float minTime, float maxTime)
        {
            GraphSeries enterSeries = GetCreateSeries("Enter", Color.OrangeRed);
            enterSeries.Clear();
            enterSeries.AddPoint(minTime, EnterAt);
            enterSeries.AddPoint(maxTime, EnterAt);

            GraphSeries exitSeries = GetCreateSeries("Exit", Color.Yellow);
            exitSeries.Clear();
            exitSeries.AddPoint(minTime, ExitAt);
            exitSeries.AddPoint(maxTime, ExitAt);
        }

        // Positioned every frame (like GraphNode's own X/Y labels) rather than only when
        // thresholds change, since Bounds isn't valid yet the first time SetWaveform runs
        // (before this node has been laid out) - self-corrects as soon as it is.
        public override void Draw(Drawer id, float parentAlpha)
        {
            PositionThresholdLabels();
            base.Draw(id, parentAlpha);
        }

        private void PositionThresholdLabels()
        {
            if (times == null || times.Count == 0 || Bounds.Width == 0 || Bounds.Height == 0)
                return;

            float labelHeight = Bounds.Height * 0.06f;
            float labelWidth = Bounds.Width * 0.1f;
            float x = Bounds.Left + 6;

            enterLabel.Text = EnterAt.ToString();
            float enterY = ToPixel(new Vector2(0, EnterAt)).Y;
            enterLabel.BoundsF = new RectangleF(x, enterY - labelHeight - 2, labelWidth, labelHeight);

            exitLabel.Text = ExitAt.ToString();
            float exitY = ToPixel(new Vector2(0, ExitAt)).Y;
            exitLabel.BoundsF = new RectangleF(x, exitY - labelHeight - 2, labelWidth, labelHeight);
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (times == null || times.Count == 0)
                return base.OnMouseInput(mouseInputEvent);

            if (mouseInputEvent.Button == MouseButtons.Left)
            {
                if (mouseInputEvent.ButtonState == ButtonStates.Pressed && Contains(mouseInputEvent.Position))
                {
                    dragging = true;
                    hasDragged = false;
                    pressPosition = mouseInputEvent.Position;

                    int rssiAtPress = (int)FromPixel(mouseInputEvent.Position).Y;
                    int midpoint = (EnterAt + ExitAt) / 2;
                    draggingEnter = rssiAtPress >= midpoint;

                    return true;
                }

                if (mouseInputEvent.ButtonState == ButtonStates.Released)
                {
                    if (dragging && !hasDragged)
                    {
                        SelectNearestLap(mouseInputEvent.Position);
                    }
                    dragging = false;
                    hasDragged = false;
                }
            }

            if (dragging && Mouse.GetState().LeftButton == ButtonState.Pressed)
            {
                Point change = mouseInputEvent.Position - pressPosition;
                if (!hasDragged && (Math.Abs(change.X) > 3 || Math.Abs(change.Y) > 3))
                {
                    hasDragged = true;
                }

                if (hasDragged)
                {
                    UpdateThresholdFromDrag(mouseInputEvent.Position);
                    return true;
                }
            }

            return base.OnMouseInput(mouseInputEvent);
        }

        private void UpdateThresholdFromDrag(Point position)
        {
            int rssi = (int)FromPixel(position).Y;
            rssi = Math.Clamp(rssi, minRssiBound, maxRssiBound);

            if (draggingEnter)
            {
                EnterAt = rssi;
                if (EnterAt < ExitAt)
                {
                    ExitAt = EnterAt;
                }
            }
            else
            {
                ExitAt = rssi;
                if (ExitAt > EnterAt)
                {
                    EnterAt = ExitAt;
                }
            }

            float minTime = (float)times[0].TotalSeconds;
            float maxTime = (float)times[times.Count - 1].TotalSeconds;
            DrawThresholds(minTime, maxTime);

            ThresholdsDragged?.Invoke(EnterAt, ExitAt);
        }

        private void SelectNearestLap(Point position)
        {
            if (lapCrossingTimes == null || lapCrossingTimes.Count == 0)
                return;

            float tapSeconds = FromPixel(position).X;

            TimeSpan nearest = lapCrossingTimes
                .OrderBy(t => Math.Abs(t.TotalSeconds - tapSeconds))
                .First();

            LapTapped?.Invoke(nearest);
        }
    }
}
