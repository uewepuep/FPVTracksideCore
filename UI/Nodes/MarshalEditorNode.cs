using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Timing;
using Tools;

namespace UI.Nodes
{
    // The native marshal screen: LapEditorNode's lap grid/commit flow, plus an RSSI waveform
    // graph with draggable enter/exit thresholds (mirroring RotorHazard's own Marshal page),
    // and a Commit that pushes the result back to RotorHazard via IRemoteMarshalUpdatable
    // rather than staying purely local.
    public class MarshalEditorNode : LapEditorNode
    {
        private ColorNode graphBackground;
        private RSSIWaveformGraph waveformGraph;

        private RSSIWaveform waveform;
        private LapEditorContainer selectedLapContainer;

        protected override string TitleText => "Marshal / Lap Editor - " + Pilot.Name;
        protected override RectangleF TitleBounds => new RectangleF(0, 0, 0.9f, 0.045f);

        // The race timeline (raceNode, from the base class) draws its own "0.00"/length labels
        // ABOVE its own RelativeBounds - 0.8x its own height, per UpdateRaceNode(). RaceNodeBounds
        // here must leave that much clear space below the graph, or the two collide.
        protected override RectangleF RaceNodeBounds => new RectangleF(0.01f, 0.57f, 0.98f, 0.04f);
        protected override RectangleF LapsNodeBounds => new RectangleF(0.01f, 0.63f, 0.98f, 0.295f);
        protected override float ButtonContainerTop => 0.945f;

        // waveform may be null until RotorHazard's ts_race_marshal broadcast is extended to
        // include history_values/history_times/enter_at/exit_at - the graph just stays hidden
        // in that case, everything else still works.
        //
        // NOTE: AddExtraContent() runs as part of the base constructor, which completes before
        // this constructor's body (and so before `waveform` below is assigned) ever runs - it
        // must not depend on the waveform field. It only builds the (still-empty) widgets;
        // SetWaveform() below is what actually populates them, called once base construction
        // has finished and the field exists.
        public MarshalEditorNode(RaceManager raceManager, Race race, Pilot pilot, IEnumerable<Lap> laps, Color channel, RSSIWaveform waveform)
            : base(raceManager, race, pilot, laps, channel)
        {
            AspectRatio = 1.6f;
            Scale(0.94f, 0.94f);

            SetWaveform(waveform);
        }

        protected override void AddExtraContent(Node inner)
        {
            graphBackground = new ColorNode(Theme.Current.Editor.Foreground.XNA);
            graphBackground.RelativeBounds = new RectangleF(0.01f, 0.06f, 0.98f, 0.45f);
            inner.AddChild(graphBackground);

            waveformGraph = new RSSIWaveformGraph();
            waveformGraph.RelativeBounds = new RectangleF(0, 0, 1, 1);
            waveformGraph.ThresholdsDragged += WaveformGraph_ThresholdsDragged;
            waveformGraph.LapTapped += WaveformGraph_LapTapped;
            graphBackground.AddChild(waveformGraph);
        }

        public void SetWaveform(RSSIWaveform waveform)
        {
            this.waveform = waveform;

            bool hasWaveform = waveform != null;
            graphBackground.Visible = hasWaveform;

            if (!hasWaveform)
                return;

            RefreshGraph(waveform.EnterAt, waveform.ExitAt);
        }

        private void RefreshGraph(int enterAt, int exitAt)
        {
            if (waveform == null)
                return;

            IEnumerable<TimeSpan> crossings = lapContainers.Where(lc => lc.Valid).Select(lc => lc.End - Race.Start);
            waveformGraph.SetWaveform(waveform.Times, waveform.Values, enterAt, exitAt, crossings);
        }

        // Bidirectional selection: clicking a lap row highlights it on the graph (this hook,
        // called for every container created anywhere in the base class - initial load, manual
        // add, split), and tapping the graph selects the nearest lap row (below).
        protected override void OnLapContainerCreated(LapEditorContainer lc)
        {
            lc.OnSelected += LapContainer_OnSelected;
        }

        private void LapContainer_OnSelected(LapEditorContainer lc)
        {
            SelectLapContainer(lc);
        }

        private void SelectLapContainer(LapEditorContainer lc)
        {
            if (selectedLapContainer != null)
            {
                selectedLapContainer.IsSelected = false;
            }

            selectedLapContainer = lc;

            if (lc != null)
            {
                lc.IsSelected = true;
                waveformGraph.SetHighlightedLap(lc.End - Race.Start);
            }
            else
            {
                waveformGraph.SetHighlightedLap(null);
            }
        }

        private void WaveformGraph_LapTapped(TimeSpan crossingTime)
        {
            DateTime crossingAbs = Race.Start + crossingTime;

            LapEditorContainer nearest = lapContainers
                .Where(lc => lc.Valid)
                .OrderBy(lc => Math.Abs((lc.End - crossingAbs).TotalSeconds))
                .FirstOrDefault();

            if (nearest != null)
            {
                SelectLapContainer(nearest);
            }
        }

        private void WaveformGraph_ThresholdsDragged(int enterAt, int exitAt)
        {
            RecalculateFromWaveform(enterAt, exitAt);
        }

        private void RecalculateFromWaveform(int enterAt, int exitAt)
        {
            if (waveform == null)
                return;

            List<RSSICrossingCalculator.Crossing> crossings = RSSICrossingCalculator.Calculate(waveform.Times, waveform.Values, enterAt, exitAt);

            // NOTE: this replaces all laps with the recalculated set - unlike RotorHazard's own
            // Recalculate, it doesn't yet preserve manually-added laps separately, since
            // LapEditorContainer doesn't currently track a lap's source. Good enough for a first
            // pass; worth revisiting if that distinction turns out to matter in practice.
            foreach (LapEditorContainer lc in lapContainers.ToArray())
            {
                lc.Remove();
            }
            lapContainers.Clear();
            selectedLapContainer = null;

            DateTime raceStart = Race.Start;
            DateTime prevEnd = raceStart;
            foreach (RSSICrossingCalculator.Crossing crossing in crossings.OrderBy(c => c.LapTime))
            {
                DateTime end = raceStart + crossing.LapTime;
                LapEditorContainer newLC = new LapEditorContainer(prevEnd, end, ChannelColor);
                newLC.OnValidityChanged += UpdateNumbersEtc;
                newLC.OnSplitLap += OnSplitLap;
                newLC.OnTimeChanged += () => { UpdateNumbersEtc(); };

                // Mirrors RotorHazard's own "auto-delete late laps" in marshal.js - a crossing
                // found past the race's actual length is almost always RSSI noise from the
                // capture window extending beyond the race (e.g. the pilot re-crossing the gate
                // after the race ended), not a real lap.
                if (crossing.LapTime > Race.Length)
                {
                    newLC.Valid = false;
                    newLC.Refresh();
                }

                lapContainers.Add(newLC);
                OnLapContainerCreated(newLC);
                prevEnd = end;
            }

            Layout();
            UpdateNumbersEtc();
            RefreshGraph(enterAt, exitAt);
        }

        protected override void OkButton_OnClick(MouseInputEvent mie)
        {
            base.OkButton_OnClick(mie);
            PushMarshalUpdate();
        }

        private void PushMarshalUpdate()
        {
            Lap[] currentLaps = Race.GetLaps(l => l.Pilot == Pilot).OrderBy(l => l.Number).ToArray();

            MarshalData marshalData = new MarshalData
            {
                RaceID = Race.ID,
                PilotID = Pilot.ID,
                PilotName = Pilot.Name,
                Laps = currentLaps.Select((lap, index) => new MarshalLap
                {
                    LapNumber = index,
                    Valid = lap.Detection.Valid,
                    Length = lap.Length,
                    RaceTime = lap.EndRaceTime
                }).ToArray()
            };

            foreach (IRemoteMarshalUpdatable remoteSystem in RaceManager.TimingSystemManager.AllSystems.OfType<IRemoteMarshalUpdatable>())
            {
                remoteSystem.PushMarshalUpdate(marshalData);
            }
        }
    }

    public static class RSSICrossingCalculator
    {
        public struct Crossing
        {
            public TimeSpan LapTime;
            public int PeakRssi;
        }

        // Port of RotorHazard's own client-side recalculate algorithm (marshal.js): track a
        // "crossing" while RSSI stays above enterAt, note the peak's midpoint time, close the
        // crossing once RSSI drops below exitAt. Deliberately doesn't replicate marshal.js's
        // extra min-lap-time/min-first-crossing/race-length filtering, which depends on
        // race-format settings not wired into this component yet.
        public static List<Crossing> Calculate(IReadOnlyList<TimeSpan> times, IReadOnlyList<int> values, int enterAt, int exitAt)
        {
            List<Crossing> crossings = new List<Crossing>();

            bool crossing = false;
            int peakRssi = 0;
            TimeSpan peakFirst = TimeSpan.Zero;
            TimeSpan peakLast = TimeSpan.Zero;

            for (int i = 0; i < times.Count; i++)
            {
                int rssi = values[i];
                TimeSpan time = times[i];

                if (!crossing && rssi > enterAt)
                {
                    crossing = true;
                    peakRssi = 0;
                }

                if (crossing && rssi >= peakRssi)
                {
                    peakLast = time;
                    if (rssi > peakRssi)
                    {
                        peakFirst = time;
                        peakRssi = rssi;
                    }
                }

                if (crossing && rssi < exitAt)
                {
                    TimeSpan lapTime = TimeSpan.FromTicks((peakFirst.Ticks + peakLast.Ticks) / 2);
                    if (lapTime > TimeSpan.Zero)
                    {
                        crossings.Add(new Crossing { LapTime = lapTime, PeakRssi = peakRssi });
                    }
                    crossing = false;
                    peakRssi = 0;
                }
            }

            if (crossing)
            {
                TimeSpan lapTime = TimeSpan.FromTicks((peakFirst.Ticks + peakLast.Ticks) / 2);
                if (lapTime > TimeSpan.Zero)
                {
                    crossings.Add(new Crossing { LapTime = lapTime, PeakRssi = peakRssi });
                }
            }

            return crossings;
        }
    }
}
