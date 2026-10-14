using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Timing;
using Timing.Aruco;
using Tools;

namespace UI.Nodes.Aruco
{
    /// <summary>
    /// The marshal editor for an ArUco trace. Adds what only a marker-count graph needs - the
    /// zoom bar, the "nothing captured" message, and redrawing the crossing lines as laps are
    /// edited - so MarshalEditorNode itself stays the RotorHazard-shaped one it was.
    /// </summary>
    public class ArucoMarshalEditorNode : MarshalEditorNode
    {
        private ArucoWaveformGraph arucoGraph;
        private WaveformRangeBarNode rangeBar;
        private TextNode noWaveformLabel;
        private TextButtonNode recalculateButton;

        /// <summary>
        /// Flicker length used when recalculating from the recorded trace, rather than whatever
        /// the timer ran with. Three seconds is longer than any real drop-out behind the gate,
        /// so a marker that flickers on the way through is read as one pass instead of several -
        /// which is the whole reason to recalculate a race after the fact.
        /// </summary>
        private const int RecalculateFlickerMs = 3000;

        // The graph takes the top half, so the lap grid here is 0.295 of the panel against the
        // plain editor's 0.65. Showing the base class's 6 rows in that would halve the row
        // height; 3 keeps each row about as tall as it is without a graph, and the rest scroll.
        protected override int VisibleLapRows => 3;

        public ArucoMarshalEditorNode(RaceManager raceManager, Race race, Pilot pilot,
                                      IEnumerable<Lap> laps, Color channel, RSSIWaveform waveform)
            : base(raceManager, race, pilot, laps, channel, waveform)
        {
        }

        protected override RSSIWaveformGraph CreateWaveformGraph()
        {
            arucoGraph = new ArucoWaveformGraph();
            return arucoGraph;
        }

        protected override void AddExtraContent(Node inner)
        {
            base.AddExtraContent(inner);

            // The base sizes its graph to the whole panel; give the bar the bottom of it.
            waveformGraph.RelativeBounds = new RectangleF(0, 0, 1, 0.9f);

            // The wheel zooms and a right-drag pans, but neither is visible. This bar is: the
            // track is the whole race, the thumb is what the graph is showing, dragging its
            // middle pans and dragging an end zooms.
            rangeBar = new WaveformRangeBarNode(Theme.Current.Editor.Background.XNA, Theme.Current.ScrollBar.XNA);
            rangeBar.RelativeBounds = new RectangleF(0.005f, 0.91f, 0.99f, 0.08f);
            rangeBar.RangeChanged += (min, max) => arucoGraph.SetTimeWindow(min, max);
            graphBackground.AddChild(rangeBar);

            // The graph keeps its slot in the layout whether or not there is a trace to draw, so
            // an absent one would read as a black void with no explanation. Say why instead.
            noWaveformLabel = new TextNode("No ArUco waveform for this race", Theme.Current.Editor.Text.XNA);
            noWaveformLabel.RelativeBounds = new RectangleF(0.1f, 0.42f, 0.8f, 0.16f);
            noWaveformLabel.Alignment = RectangleAlignment.Center;
            graphBackground.AddChild(noWaveformLabel);

            // Sits on the graph rather than in the editor's button row, which belongs to
            // LapEditorNode and is shared with every other timing system.
            recalculateButton = new TextButtonNode("Recalc " + (RecalculateFlickerMs / 1000.0).ToString("0.#") + "s",
                Theme.Current.Editor.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.Editor.Text.XNA);
            recalculateButton.RelativeBounds = new RectangleF(0.86f, 0.015f, 0.135f, 0.09f);
            recalculateButton.OnClick += RecalculateButton_OnClick;
            graphBackground.AddChild(recalculateButton);
        }

        /// <summary>
        /// A recalculated crossing counts as the same pass as an existing lap when it lands
        /// within this of it. A longer flicker reports the same instant, give or take how the
        /// flicker boundary rounds to a frame - tens of milliseconds - so a second is generous
        /// while still being far shorter than any lap.
        /// </summary>
        private const double MatchToleranceSeconds = 1.0;

        private void RecalculateButton_OnClick(MouseInputEvent mie)
        {
            ArucoWaveform aruco = waveform as ArucoWaveform;
            if (aruco == null)
                return;

            ApplyCrossings(Recalculate(aruco, RecalculateFlickerMs));
        }

        /// <summary>
        /// Brings the lap list into line with a recalculation by validating and disqualifying
        /// the laps that are already there, rather than replacing them.
        ///
        /// A longer flicker can only ever merge passes, never split one, so its crossings are
        /// the timer's own crossings with some dropped. Re-using the rows keeps the times the
        /// timer actually measured, keeps any manual edit or split made since, and leaves no
        /// replaced laps to dispose of. A crossing with no lap to match - only possible when
        /// recalculating with a shorter flicker than the race ran with - is added.
        /// </summary>
        private void ApplyCrossings(IEnumerable<TimeSpan> crossings)
        {
            List<LapEditorContainer> unclaimed = new List<LapEditorContainer>(lapContainers);
            List<TimeSpan> unmatched = new List<TimeSpan>();

            foreach (TimeSpan crossing in crossings.OrderBy(c => c))
            {
                LapEditorContainer nearest = null;
                double nearestDistance = double.MaxValue;

                foreach (LapEditorContainer lc in unclaimed)
                {
                    double distance = Math.Abs((lc.End - Race.Start - crossing).TotalSeconds);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = lc;
                    }
                }

                // Each crossing claims its nearest lap and no other crossing can take it, so two
                // passes close together still map one to one.
                if (nearest != null && nearestDistance <= MatchToleranceSeconds)
                {
                    nearest.Valid = true;
                    unclaimed.Remove(nearest);
                }
                else
                {
                    unmatched.Add(crossing);
                }
            }

            // Whatever no crossing claimed was a flicker the longer window swallows.
            foreach (LapEditorContainer lc in unclaimed)
            {
                lc.Valid = false;
            }

            foreach (TimeSpan crossing in unmatched)
            {
                AddLapContainer(crossing);
            }

            foreach (LapEditorContainer lc in lapContainers)
            {
                lc.Refresh();
            }

            Layout();
            UpdateNumbersEtc();
        }

        /// <summary>
        /// Replays the recorded marker counts through the same <see cref="ArucoGate"/> the timer
        /// runs live, with a different flicker length. Anything else - a second implementation of
        /// the rule, or RotorHazard's peak-midpoint algorithm - would put laps somewhere the
        /// timer never would.
        /// </summary>
        private static IEnumerable<TimeSpan> Recalculate(ArucoWaveform waveform, int flickerMs)
        {
            List<TimeSpan> crossings = new List<TimeSpan>();

            int scale = Math.Max(1, waveform.ValueScale);
            ArucoGate gate = new ArucoGate();

            // The gate works in absolute time; the trace is relative to the race start. Any fixed
            // origin does, as long as it is far enough from DateTime.MinValue for the negative
            // sample times before a delayed start not to underflow it.
            DateTime origin = DateTime.MinValue.AddDays(1);

            int count = Math.Min(waveform.Times.Length, waveform.Values.Length);
            for (int i = 0; i < count; i++)
            {
                int markers = waveform.Values[i] / scale;

                if (gate.Update(markers, origin + waveform.Times[i], waveform.MarkerThreshold, flickerMs,
                                out DateTime crossing))
                {
                    crossings.Add(crossing - origin);
                }
            }

            return crossings;
        }

        public override void SetWaveform(RSSIWaveform waveform)
        {
            ArucoWaveform aruco = waveform as ArucoWaveform;

            if (aruco != null)
            {
                arucoGraph.ValueScale = aruco.ValueScale;
                arucoGraph.ValueLabel = aruco.ValueLabel;
            }

            base.SetWaveform(waveform);

            bool hasWaveform = waveform != null;

            // The base hides the whole panel when there is no trace; keep it up for the message.
            graphBackground.Visible = true;
            waveformGraph.Visible = hasWaveform;
            rangeBar.Visible = hasWaveform;
            recalculateButton.Visible = waveform is ArucoWaveform;
            noWaveformLabel.Visible = !hasWaveform;
        }

        // Every lap edit in the table funnels through here - a retyped time (per keystroke, via
        // LapEditorContainer.OnTimeChanged), a validity toggle, an added lap, a split. The white
        // crossing lines are drawn from those same laps, so they are redrawn with them; without
        // this the graph keeps showing where the laps used to be the moment one is corrected.
        protected override void UpdateNumbersEtc()
        {
            base.UpdateNumbersEtc();

            // Runs during base construction too, before SetWaveform has been called.
            if (waveform == null)
                return;

            arucoGraph.SetLapCrossings(CurrentCrossings());

            // The cyan highlight is a crossing line too, and the selected lap may be the one
            // that just moved - or may have just been marked invalid.
            arucoGraph.SetHighlightedLap(selectedLapContainer != null && selectedLapContainer.Valid
                ? selectedLapContainer.End - Race.Start
                : (TimeSpan?)null);
        }

        // The bar mirrors the graph rather than owning the zoom, so a wheel zoom or a right-drag
        // on the graph moves the thumb with no event wiring between the two.
        public override void Draw(Drawer id, float parentAlpha)
        {
            if (rangeBar != null && rangeBar.Visible)
            {
                rangeBar.SetRange(arucoGraph.FullMinTime, arucoGraph.FullMaxTime,
                                  arucoGraph.ViewMinTime, arucoGraph.ViewMaxTime);
            }

            base.Draw(id, parentAlpha);
        }
    }
}
