using Composition;
using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;

namespace UI.Nodes
{
    // Each pilot's final place in the event (RaceLib.EventRanking), the stage that
    // decided it, their event points and best lap. Drag a pilot onto a place to pin
    // them there; right-click to unpin.
    public class FinalResultsNode : PilotSummaryTable
    {
        private Dictionary<Pilot, EventPlacing> placings;
        private Dictionary<Pilot, int> points;
        private bool showPoints;

        public FinalResultsNode(EventManager eventManager)
            : base(eventManager, "Final Results")
        {
            placings = new Dictionary<Pilot, EventPlacing>();
            points = new Dictionary<Pilot, int>();
            eventManager.OnPilotRefresh += Refresh;
        }

        public override void Dispose()
        {
            eventManager.OnPilotRefresh -= Refresh;
            base.Dispose();
        }

        public override void CreateHeadings(Node container, out Round[] rounds, out int column)
        {
            placings = new EventRanking(eventManager).Calculate(ResultModes.Auto).ToDictionary(p => p.Pilot);

            points = eventManager.ResultManager.GetResults(r => r.Valid && r.Pilot != null && r.Race != null && r.Race.Valid)
                .GroupBy(r => r.Pilot)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Points));

            // No point showing a column of zeros (eg events with no points rounds)
            showPoints = points.Values.Any(p => p != 0);

            rounds = new Round[0];
            column = 0;

            foreach (string heading in GetColumns())
            {
                // Same as the base table's headings, so they match the Position and Pilots headings
                TextButtonNode headingText = new TextButtonNode(heading, Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
                headingText.TextNode.Alignment = RectangleAlignment.TopRight;
                container.AddChild(headingText);
            }
        }

        private IEnumerable<string> GetColumns()
        {
            yield return "Stage";
            if (showPoints)
                yield return "Points";
            yield return "Best Lap";
        }

        private EventPlacing GetPlacing(Pilot pilot)
        {
            EventPlacing placing;
            if (pilot != null && placings.TryGetValue(pilot, out placing))
            {
                return placing;
            }
            return null;
        }

        private int? GetPosition(Pilot pilot)
        {
            return GetPlacing(pilot)?.Position;
        }

        // The row under the mouse, in display order with its place (1 = top)
        private PilotResultNode RowAt(Point position, out int place)
        {
            place = 0;
            if (!rows.Contains(position))
                return null;

            foreach (PilotResultNode row in rows.ChildrenOfType)
            {
                place++;
                if (row.Visible && row.Pilot != null && row.Contains(position))
                    return row;
            }
            return null;
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            int place;
            PilotResultNode row = RowAt(mouseInputEvent.Position, out place);
            if (row != null)
            {
                if (mouseInputEvent.Button == MouseButtons.Left && mouseInputEvent.ButtonState == ButtonStates.Pressed)
                {
                    GetLayer<DragLayer>()?.RegisterDrag(row, mouseInputEvent);
                }

                if (mouseInputEvent.Button == MouseButtons.Right && mouseInputEvent.ButtonState == ButtonStates.Released)
                {
                    ShowPinMenu(row.Pilot, mouseInputEvent);
                    return true;
                }
            }

            return base.OnMouseInput(mouseInputEvent);
        }

        private void ShowPinMenu(Pilot pilot, MouseInputEvent mouseInputEvent)
        {
            bool pinned = GetPlacing(pilot)?.Pinned ?? false;
            bool anyPinned = placings.Values.Any(p => p.Pinned);
            if (!pinned && !anyPinned)
                return;

            MouseMenu menu = new MouseMenu(this);
            if (pinned)
            {
                menu.AddItem("Unpin " + pilot.Name, () =>
                {
                    eventManager.UnpinFinalResult(pilot);
                    Refresh();
                });
            }
            if (anyPinned)
            {
                menu.AddItem("Unpin everyone", () =>
                {
                    eventManager.ClearFinalResultPins();
                    Refresh();
                });
            }
            menu.Show(mouseInputEvent);
        }

        public override Rectangle? CanDrop(MouseInputEvent mouseInputEvent, Node node)
        {
            PilotResultNode dragged = node as PilotResultNode;
            int place;
            PilotResultNode target = RowAt(mouseInputEvent.Position, out place);
            if (dragged != null && target != null && rows.ChildrenOfType.Contains(dragged))
            {
                return target.Bounds;
            }
            return base.CanDrop(mouseInputEvent, node);
        }

        public override bool OnDrop(MouseInputEvent finalInputEvent, Node node)
        {
            PilotResultNode dragged = node as PilotResultNode;
            int place;
            PilotResultNode target = RowAt(finalInputEvent.Position, out place);
            if (dragged != null && target != null && rows.ChildrenOfType.Contains(dragged))
            {
                if (target != dragged)
                {
                    eventManager.PinFinalResult(dragged.Pilot, place);
                    Refresh();
                }
                return true;
            }
            return base.OnDrop(finalInputEvent, node);
        }

        public override void SetOrder()
        {
            rows.SetOrder<PilotResultNode, int>(pa => GetPosition(pa.Pilot) ?? int.MaxValue);
        }

        public override void CalculatePositions()
        {
            foreach (PilotResultNode row in rows.ChildrenOfType)
            {
                EventPlacing placing = GetPlacing(row.Pilot);
                row.SetPosition(placing?.Position ?? 0);
                row.SetPinned(placing != null && placing.Pinned);
                row.SetDraggable(row.Pilot != null);
            }
        }

        protected override void SetResult(PilotResultNode pilotResNode, Pilot pilot, Round[] rounds)
        {
            EventPlacing placing;
            placings.TryGetValue(pilot, out placing);

            string stage = placing?.Stage?.Name ?? "";

            int pilotPoints;
            points.TryGetValue(pilot, out pilotPoints);

            string bestLap = "";
            Lap[] laps;
            bool overallBest;
            if (eventManager.LapRecordManager.GetBestLaps(pilot, eventManager.Event.PBLaps, out laps, out overallBest) && laps.Any())
            {
                bestLap = laps.TotalTime().ToStringRaceTime();
            }

            List<string> values = new List<string>();
            values.Add(stage);
            if (showPoints)
                values.Add(pilotPoints != 0 ? pilotPoints.ToString() : "");
            values.Add(bestLap);

            List<Node> nodes = new List<Node>();
            foreach (string value in values)
            {
                TextNode tn = new TextNode(value, Theme.Current.Rounds.Text.XNA);
                tn.Alignment = RectangleAlignment.TopRight;
                nodes.Add(tn);
            }

            pilotResNode.Set(pilot, nodes);
        }
    }
}
