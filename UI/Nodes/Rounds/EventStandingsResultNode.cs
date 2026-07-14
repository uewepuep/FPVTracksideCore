using Composition.Nodes;
using Composition.Layers;
using Composition.Input;
using Microsoft.Xna.Framework;
using RaceLib;
using RaceLib.Format;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;

namespace UI.Nodes.Rounds
{
    // Shared behaviour for standings tables sourced from a format (Lua script or Spreadsheet).
    // Subclasses only need to fetch a StandingsResult for the round's stage pilots.
    public abstract class EventStandingsResultNode : EventResultNode
    {
        public ImageButtonNode MenuButton { get; private set; }

        private StandingsPilotNode headingNode;
        private readonly List<StandingsPilotNode> rowNodes = new List<StandingsPilotNode>();
        private bool needsRefresh;
        private int columns = 1;

        public EventStandingsResultNode(RoundsNode roundsNode, EventManager ev, Round round)
            : base(roundsNode, ev, round)
        {
            EventManager.RaceManager.OnRaceEnd += OnRaceEnd;

            MenuButton = new ImageButtonNode(@"img\settings.png", Color.Transparent, Theme.Current.Hover.XNA, Theme.Current.Rounds.Text.XNA);
            MenuButton.OnClick += MenuButton_OnClick;
            buttonContainer.AddChild(MenuButton, 0);

            UpdateButtons();
            SetHeading("Standings");
            Refresh();
        }

        public override void Dispose()
        {
            EventManager.RaceManager.OnRaceEnd -= OnRaceEnd;
            base.Dispose();
        }

        private void OnRaceEnd(Race race) => Refresh();

        protected override void UpdateButtons()
        {
            base.UpdateButtons();

            if (MenuButton != null)
                MenuButton.Scale(0.6f);
        }

        public void Refresh()
        {
            needsRefresh = true;
            RequestLayout();
        }

        private void MenuButton_OnClick(MouseInputEvent mie)
        {
            MouseMenu mm = new MouseMenu(this);

            MakeMenu(mm);

            Point position = new Point(MenuButton.Bounds.X, MenuButton.Bounds.Bottom);
            mm.Show(position - mie.Translation);
        }

        public void MakeMenu(MouseMenu mm)
        {
            if (StageNode != null)
                mm.AddItem("Edit Stage", StageNode.EditStage);
            mm.AddItem("Copy to Clipboard", CopyToClipboard);

            FileTools.ExportMenu(mm, "Export", PlatformTools, "Save", MakeTable(), GetLayer<PopupLayer>());
        }

        private void CopyToClipboard()
        {
            string tsv = MakeTable().ToTSV();
            if (!string.IsNullOrEmpty(tsv))
            {
                PlatformTools.Clipboard.SetText(tsv);
            }
        }

        public string[][] MakeTable()
        {
            List<string[]> output = new List<string[]>();
            if (headingNode != null)
                output.Add(headingNode.MakeLine());

            foreach (StandingsPilotNode pn in rowNodes)
            {
                output.Add(pn.MakeLine());
            }
            return output.ToArray();
        }

        public override void Layout(RectangleF parentBounds)
        {
            if (needsRefresh)
            {
                UpdateDisplay();
                needsRefresh = false;
            }
            base.Layout(parentBounds);
        }

        // Fetches the standings for this round's stage. Return null if the format has no result to show.
        protected abstract StandingsResult GetStandingsResult(Pilot[] stagePilots);

        private void UpdateDisplay()
        {
            Pilot[] stagePilots = EventManager.RaceManager.Races
                .Where(r => r.Round?.Stage == Stage)
                .SelectMany(r => r.Pilots)
                .Distinct()
                .ToArray();

            StandingsResult result = GetStandingsResult(stagePilots);

            headingNode?.Dispose();
            headingNode = null;
            foreach (StandingsPilotNode rn in rowNodes) rn.Dispose();
            rowNodes.Clear();

            if (result == null) return;

            if (result.Headings != null)
            {
                headingNode = new StandingsPilotNode(isHeading: true);
                headingNode.Update("Pilot", result.Headings);
                contentContainer.AddChild(headingNode);
            }

            foreach (StandingsRow row in result.Rows)
            {
                StandingsPilotNode node = new StandingsPilotNode(isHeading: false);
                node.Update(row.Name, row.Values);
                contentContainer.AddChild(node);
                rowNodes.Add(node);
            }

            UpdateColumns();
            RequestFormatLayout();
        }

        private void UpdateColumns()
        {
            const int pilotsPerColumn = 26;
            int nodeCount = rowNodes.Count + (headingNode != null ? 1 : 0);
            columns = Math.Max(1, (int)Math.Ceiling(nodeCount / (float)pilotsPerColumn));

            float width = 1.0f / columns;
            int column = 0;

            List<Node> all = new List<Node>();
            if (headingNode != null) all.Add(headingNode);
            foreach (StandingsPilotNode rn in rowNodes) all.Add(rn);

            int start = 0;
            while (start < all.Count)
            {
                int count = Math.Min(pilotsPerColumn, all.Count - start);
                float leftAlign = column * width;
                Node.MakeColumns(all.Skip(start).Take(count), pilotsPerColumn, leftAlign, width);
                column++;
                start += count;
            }
        }

        public override void CalculateAspectRatio(float height)
        {
            float ap = 300 / 800.0f;

            if (rowNodes.Any() && height > 0)
                ap = rowNodes[0].GetRequiredWidth() / height;

            float aspectRatio = ap * columns;
            if (AspectRatio != aspectRatio)
                AspectRatio = aspectRatio;

            base.CalculateAspectRatio(height);
        }

        public override bool HasResult() => true;

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            MouseInputEvent translated = Translate(mouseInputEvent);

            if (translated.EventType == MouseInputEvent.EventTypes.Button && translated.Button == MouseButtons.Right && translated.ButtonState == ButtonStates.Released)
            {
                MouseMenu mouseMenu = new MouseMenu(this);
                mouseMenu.AddItem("Copy Pilots", CopyToClipboard);
                mouseMenu.AddItem("Edit Stage", StageNode.EditStage);

                mouseMenu.Show(mouseInputEvent.Position);
                return true;
            }

            return base.OnMouseInput(mouseInputEvent);
        }
    }

    // A single row (or heading) in a standings table: pilot name, per-column values, and a final score column.
    public class StandingsPilotNode : Node
    {
        private const int NameWidth = 150;
        private const int ValueWidth = 25;
        private const int ScoreWidth = 100;
        private const int Padding = 10;
        private const int VertPad = 2;

        private readonly ColorNode background;
        private readonly TextNode nameNode;
        private readonly TextNode scoreNode;
        private TextNode[] valueNodes = Array.Empty<TextNode>();

        public StandingsPilotNode(bool isHeading)
        {
            ToolTexture color = isHeading ? Theme.Current.Rounds.Background : Theme.Current.Rounds.Foreground;
            background = new ColorNode(color);
            background.Scale(0.95f, 0.9f);
            AddChild(background);

            nameNode = new TextNode("", Theme.Current.Rounds.Text.XNA);
            nameNode.Alignment = RectangleAlignment.BottomLeft;
            AddChild(nameNode);

            scoreNode = new TextNode("", Theme.Current.Rounds.Text.XNA);
            scoreNode.Alignment = RectangleAlignment.BottomRight;
            scoreNode.Style.Bold = true;
            AddChild(scoreNode);
        }

        public void Update(string name, string[] values)
        {
            nameNode.Text = name ?? "";

            foreach (TextNode vn in valueNodes) vn.Dispose();

            if (values == null || values.Length == 0)
            {
                valueNodes = Array.Empty<TextNode>();
                scoreNode.Text = "";
                return;
            }

            scoreNode.Text = values[values.Length - 1];

            TextNode[] newNodes = new TextNode[values.Length - 1];
            for (int i = 0; i < newNodes.Length; i++)
            {
                TextNode tn = new TextNode(values[i], Theme.Current.Rounds.Text.XNA);
                tn.Alignment = RectangleAlignment.BottomRight;
                AddChild(tn);
                newNodes[i] = tn;
            }
            valueNodes = newNodes;
        }

        public string[] MakeLine()
        {
            List<string> line = new List<string>();
            line.Add(nameNode.Text);
            foreach (TextNode vn in valueNodes)
            {
                line.Add(vn.Text);
            }
            line.Add(scoreNode.Text);
            return line.ToArray();
        }

        public int GetRequiredWidth()
        {
            int inner = NameWidth + Padding + valueNodes.Length * (ValueWidth + Padding) + ScoreWidth + Padding;
            return (int)Math.Ceiling(inner / 0.975f) + 20;
        }

        public override void Layout(RectangleF parentBounds)
        {
            BoundsF = CalculateRelativeBounds(parentBounds);

            background.Layout(BoundsF);
            RectangleF work = background.BoundsF;

            work.X += Padding;
            work.Y += VertPad;
            work.Height -= VertPad * 2;

            RectangleF nameBounds = work;
            nameBounds.Width = NameWidth;
            nameNode.Layout(nameBounds);

            float x = nameBounds.Right + Padding;
            foreach (TextNode vn in valueNodes)
            {
                RectangleF vb = work;
                vb.X = x;
                vb.Width = ValueWidth;
                vn.Layout(vb);
                x = vb.Right + Padding;
            }

            RectangleF scoreBounds = work;
            scoreBounds.X = x;
            scoreBounds.Width = work.Right - x - Padding;
            scoreNode.Layout(scoreBounds);
        }
    }
}
