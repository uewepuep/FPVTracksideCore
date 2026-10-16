using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using Timing.Aruco;
using Tools;

namespace UI.Nodes.Aruco
{
    /// <summary>
    /// The marshal graph for an ArUco trace. Everything that makes a marker count read
    /// differently from a receiver RSSI lives here rather than in RSSIWaveformGraph, so the
    /// RotorHazard rendering path keeps exactly the behaviour it has always had:
    ///
    /// - the value axis is gridded and labelled in whole markers, not the x10 it is stored as
    /// - one threshold line, since the gate has one, and it is not draggable, since
    ///   RSSICrossingCalculator's peak-midpoint rule is not how ArUco decides a crossing
    /// - the time axis zooms and pans
    /// </summary>
    public class ArucoWaveformGraph : RSSIWaveformGraph
    {
        /// <summary>Divisor between the plotted values and the numbers on the axis.</summary>
        public int ValueScale { get; set; } = 1;

        /// <summary>Units suffix for the threshold label, e.g. "markers".</summary>
        public string ValueLabel { get; set; }

        // ArUco emits a crossing once the count has stayed below the threshold for
        // FlickerLengthMs - when the marker left view, not at the peak's midpoint - so letting
        // the thresholds be dragged would silently move every lap.
        protected override bool ThresholdsDraggable => false;

        // --- time axis ---------------------------------------------------------------------

        // Full extent of the trace. View may show any window inside this; zooming never invents
        // time the capture does not have.
        private float fullMinTime;
        private float fullMaxTime;

        private bool panning;
        private Point panLastPosition;

        /// <summary>Wheel notches multiply or divide the visible time span by this.</summary>
        private const float ZoomStep = 1.25f;

        /// <summary>Tightest window the wheel will go to. Below this there is nothing to see.</summary>
        private const float MinViewSeconds = 0.2f;

        private readonly TextNode rangeLabel;

        public bool IsZoomed => fullMaxTime > fullMinTime && View.Width < fullMaxTime - fullMinTime;

        public float FullMinTime => fullMinTime;
        public float FullMaxTime => fullMaxTime;
        public float ViewMinTime => View.X;
        public float ViewMaxTime => View.X + View.Width;

        public ArucoWaveformGraph()
        {
            // Zoomed in there is nothing on the time axis to say where you are - the race
            // timeline under the graph still spans the whole race. Shown only while zoomed.
            rangeLabel = new TextNode("", Color.White);
            rangeLabel.Alignment = RectangleAlignment.BottomRight;
            rangeLabel.Visible = false;
            AddChild(rangeLabel);
        }

        public override void SetWaveform(IReadOnlyList<TimeSpan> times, IReadOnlyList<int> values,
                                         int enterAt, int exitAt, IEnumerable<TimeSpan> lapCrossings)
        {
            // A threshold-free refresh still re-enters here, so remember the window first.
            float keptViewX = View.X;
            float keptViewWidth = View.Width;
            bool keepView = IsZoomed;

            base.SetWaveform(times, values, enterAt, exitAt, lapCrossings);

            if (times == null || times.Count == 0)
                return;

            float minTime = (float)times[0].TotalSeconds;
            float maxTime = (float)times[times.Count - 1].TotalSeconds;

            // The base divides through by View in ToPixel/FromPixel, so a degenerate axis puts
            // NaN into every plotted point. A flat trace sitting exactly on its threshold is
            // reachable here - a channel that never saw the gate, with MarkerThreshold 0 - and
            // it should draw a flat line rather than vanish.
            if (maxRssiBound == minRssiBound)
            {
                maxRssiBound = minRssiBound + 1;
                DrawLapCrossings();
            }
            if (maxTime <= minTime)
                maxTime = minTime + 1;

            bool sameRange = fullMinTime == minTime && fullMaxTime == maxTime;

            fullMinTime = minTime;
            fullMaxTime = maxTime;

            View = new RectangleF(minTime, maxRssiBound, maxTime - minTime, minRssiBound - maxRssiBound);

            // Carry the window over when the trace has not changed, so zooming in and then
            // correcting a lap does not snap back to the whole race every time.
            if (keepView && sameRange)
                SetTimeView(keptViewX, keptViewWidth);

            AddMarkerCountAxis();
        }

        /// <summary>
        /// Redraws just the white crossing markers, leaving the trace, threshold, axis and zoom
        /// untouched. Editing a lap in the table below moves its line straight away this way.
        /// </summary>
        public void SetLapCrossings(IEnumerable<TimeSpan> lapCrossings)
        {
            List<TimeSpan> updated = lapCrossings == null ? new List<TimeSpan>() : new List<TimeSpan>(lapCrossings);

            // GraphNode can only add series, never remove one, so a shorter lap list retires the
            // surplus by emptying it - GraphSeries.Draw skips a series with no points.
            for (int i = updated.Count; i < lapCrossingTimes.Count; i++)
            {
                GetCreateSeries("Lap" + i, Color.White).Clear();
            }

            lapCrossingTimes = updated;
            DrawLapCrossings();
        }

        // One gridline per whole marker, so the trace reads as "3 markers in frame" rather than
        // as the bare 30 it is stored as.
        private void AddMarkerCountAxis()
        {
            if (ValueScale <= 1)
                return;

            int step = ValueScale;

            // A gate can be papered with markers; keep the grid legible rather than exact.
            while (step > 0 && (maxRssiBound - minRssiBound) / step > 10)
                step += ValueScale;

            int first = (int)Math.Ceiling(minRssiBound / (double)step) * step;

            for (int value = first; value <= maxRssiBound; value += step)
            {
                // The threshold draws its own line and carries its own label at this level - a
                // gridline through it would double up and collide with the text.
                if (value == EnterAt)
                    continue;

                AddYLabel(value, (value / ValueScale).ToString());
            }
        }

        // One threshold, so only the Enter line is drawn. The base's Exit series is left empty.
        protected override void DrawThresholds(float minTime, float maxTime)
        {
            GraphSeries enterSeries = GetCreateSeries("Enter", Color.OrangeRed);
            enterSeries.Clear();
            enterSeries.AddPoint(minTime, EnterAt);
            enterSeries.AddPoint(maxTime, EnterAt);

            GetCreateSeries("Exit", Color.Yellow).Clear();
        }

        protected override void PositionThresholdLabels()
        {
            exitLabel.Visible = false;

            if (times == null || times.Count == 0 || Bounds.Width == 0 || Bounds.Height == 0)
                return;

            float labelHeight = Bounds.Height * 0.06f;
            // A bare number needs almost no room; a units suffix ("2 markers") needs the width
            // or TextNode shrinks the whole label to fit.
            float labelWidth = Bounds.Width * (string.IsNullOrEmpty(ValueLabel) ? 0.1f : 0.2f);

            int shown = ValueScale > 1 ? EnterAt / ValueScale : EnterAt;
            enterLabel.Text = shown + (string.IsNullOrEmpty(ValueLabel) ? "" : " " + ValueLabel);

            float enterY = ToPixel(new Vector2(0, EnterAt)).Y;
            enterLabel.BoundsF = new RectangleF(Bounds.Left + 6, enterY - labelHeight - 2, labelWidth, labelHeight);
        }

        // --- zoom and pan ------------------------------------------------------------------

        /// <summary>
        /// The one place View's time axis is written, so every clamp lives here: never wider than
        /// the captured trace, never tighter than a fraction of a second, never scrolled off
        /// either end. Y is left exactly as SetWaveform established it - this is an X zoom.
        /// </summary>
        private void SetTimeView(float min, float width)
        {
            float full = fullMaxTime - fullMinTime;
            if (full <= 0)
                return;

            width = Math.Clamp(width, Math.Min(MinViewSeconds, full), full);

            // fullMaxTime - width is fullMinTime once width has been clamped to the full span -
            // but only in exact arithmetic. In float it can land a hair below fullMinTime, and
            // Math.Clamp throws when its max is under its min. Traces starting before the gun
            // (a delayed start arms detection early, so fullMinTime is negative) hit this on the
            // first wheel notch that zooms all the way back out.
            float latestStart = Math.Max(fullMinTime, fullMaxTime - width);
            min = Math.Clamp(min, fullMinTime, latestStart);

            View = new RectangleF(min, View.Y, width, View.Height);
        }

        /// <summary>Show this time window, clamped. For a visible zoom control to drive.</summary>
        public void SetTimeWindow(float min, float max)
        {
            SetTimeView(min, max - min);
        }

        // Wheel zoom about the cursor: whatever moment sits under the pointer stays under it,
        // which is what makes zooming in on one suspect lap feel like pointing at it.
        private void ZoomAt(int wheelChange, Point position)
        {
            float full = fullMaxTime - fullMinTime;
            if (full <= 0 || View.Width <= 0)
                return;

            float anchor = FromPixel(position).X;
            float relative = (anchor - View.X) / View.Width;

            // WheelChange is the raw XNA delta, 120 per notch. Up (positive) zooms in.
            float factor = (float)Math.Pow(ZoomStep, -wheelChange / 120.0);
            float width = Math.Clamp(View.Width * factor, Math.Min(MinViewSeconds, full), full);

            SetTimeView(anchor - relative * width, width);
        }

        private void PanBy(int pixels)
        {
            if (Bounds.Width == 0)
                return;

            SetTimeView(View.X - pixels * (View.Width / Bounds.Width), View.Width);
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (times == null || times.Count == 0)
                return base.OnMouseInput(mouseInputEvent);

            if (mouseInputEvent.WheelChange != 0 && Contains(mouseInputEvent.Position))
            {
                ZoomAt(mouseInputEvent.WheelChange, mouseInputEvent.Position);
                return true;
            }

            // Right-drag pans. Left stays with the base, which uses it to tap-select a lap.
            if (mouseInputEvent.Button == MouseButtons.Right)
            {
                if (mouseInputEvent.ButtonState == ButtonStates.Pressed && Contains(mouseInputEvent.Position))
                {
                    panning = true;
                    panLastPosition = mouseInputEvent.Position;
                    return true;
                }

                if (mouseInputEvent.ButtonState == ButtonStates.Released)
                {
                    panning = false;
                }
            }

            if (panning)
            {
                if (Mouse.GetState().RightButton == ButtonState.Pressed)
                {
                    PanBy(mouseInputEvent.Position.X - panLastPosition.X);
                    panLastPosition = mouseInputEvent.Position;
                    return true;
                }

                panning = false;
            }

            return base.OnMouseInput(mouseInputEvent);
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            PositionRangeLabel();

            // Zoomed in, samples outside the window map to pixels outside Bounds, and neither
            // GraphSeries nor Drawer.DrawLine clips - the trace would be drawn straight across
            // the lap table below. Unzoomed nothing leaves Bounds, so don't pay for the
            // SpriteBatch restart PushClipRectangle costs.
            bool clip = IsZoomed && Bounds.Width > 0 && Bounds.Height > 0;

            if (clip)
                id.PushClipRectangle(Bounds);

            base.Draw(id, parentAlpha);

            if (clip)
                id.PopClipRectangle();
        }

        private void PositionRangeLabel()
        {
            rangeLabel.Visible = IsZoomed;
            if (!rangeLabel.Visible || Bounds.Width == 0 || Bounds.Height == 0)
                return;

            rangeLabel.Text = View.X.ToString("0.0") + " - " + (View.X + View.Width).ToString("0.0") + " s";

            float labelHeight = Bounds.Height * 0.06f;
            float labelWidth = Bounds.Width * 0.22f;
            rangeLabel.BoundsF = new RectangleF(Bounds.Right - labelWidth - 6, Bounds.Bottom - labelHeight - 2,
                                                labelWidth, labelHeight);
        }
    }
}
