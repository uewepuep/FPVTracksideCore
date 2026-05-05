using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using RaceLib.Format;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;

namespace UI.Nodes.Rounds
{
    public class EventLuaStandingsNode : EventResultNode
    {
        private LuaStandingsPilotNode headingNode;
        private readonly List<LuaStandingsPilotNode> rowNodes = new List<LuaStandingsPilotNode>();
        private bool needsRefresh;
        private int columns = 1;

        public EventLuaStandingsNode(RoundsNode roundsNode, EventManager ev, Round round)
            : base(roundsNode, ev, round)
        {
            EventManager.RaceManager.OnRaceEnd += OnRaceEnd;
            SetHeading("Standings");
            Refresh();
        }

        public override void Dispose()
        {
            EventManager.RaceManager.OnRaceEnd -= OnRaceEnd;
            base.Dispose();
        }

        private void OnRaceEnd(Race race) => Refresh();

        public void Refresh()
        {
            needsRefresh = true;
            RequestLayout();
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

        private void UpdateDisplay()
        {
            Pilot[] stagePilots = EventManager.RaceManager.Races
                .Where(r => r.Round?.Stage == Stage)
                .SelectMany(r => r.Pilots)
                .Distinct()
                .ToArray();

            LuaRoundFormat format = EventManager.RoundManager.GetRoundFormat(Stage) as LuaRoundFormat;
            LuaStandingsResult result = format?.GetStandings(stagePilots);

            headingNode?.Dispose();
            headingNode = null;
            foreach (LuaStandingsPilotNode rn in rowNodes) rn.Dispose();
            rowNodes.Clear();

            if (result == null) return;

            if (result.Headings != null)
            {
                headingNode = new LuaStandingsPilotNode(isHeading: true);
                headingNode.Update("Pilot", result.Headings);
                contentContainer.AddChild(headingNode);
            }

            foreach (LuaStandingsRow row in result.Rows)
            {
                LuaStandingsPilotNode node = new LuaStandingsPilotNode(isHeading: false);
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
            foreach (LuaStandingsPilotNode rn in rowNodes) all.Add(rn);

            int start = 0;
            while (start < all.Count)
            {
                int count = Math.Min(pilotsPerColumn, all.Count - start);
                float leftAlign = column * width;
                Node.MakeColumns(all.Skip(start).Take(count), count, leftAlign, width);
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
    }

    public class LuaStandingsPilotNode : Node
    {
        private const int NameWidth = 150;
        private const int ValueWidth = 25;
        private const int ScoreWidth = 50;
        private const int Padding = 10;
        private const int VertPad = 2;

        private readonly ColorNode background;
        private readonly TextNode nameNode;
        private readonly TextNode scoreNode;
        private TextNode[] valueNodes = Array.Empty<TextNode>();

        public LuaStandingsPilotNode(bool isHeading)
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

        public int GetRequiredWidth()
        {
            return NameWidth + Padding + valueNodes.Length * (ValueWidth + Padding) + ScoreWidth + Padding;
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
            scoreBounds.Width = ScoreWidth;
            scoreNode.Layout(scoreBounds);

            work.Width = scoreBounds.Right - work.X;
            background.BoundsF = work;
        }
    }
}
