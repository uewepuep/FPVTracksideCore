using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using Tools;

namespace UI.Nodes
{
    // The Results tab: Lap Records, Lap Count and Points as sub-tabs.
    public class ResultsNode : Node
    {
        public PointsSummaryNode PointsSummaryNode { get; private set; }
        public LapCountSummaryNode LapCountSummaryNode { get; private set; }
        public LapRecordsSummaryNode LapRecordsSummaryNode { get; private set; }

        public Node Showing { get { return tabs.Showing; } }

        // Raised when the sub-tab changes, like TabbedMultiNode.OnTabChange.
        public Action<string, Node> OnTabChange { get; set; }

        private TabbedMultiNode tabs;

        public ResultsNode(EventManager eventManager)
        {
            PointsSummaryNode = new PointsSummaryNode(eventManager);
            LapCountSummaryNode = new LapCountSummaryNode(eventManager);
            LapRecordsSummaryNode = new LapRecordsSummaryNode(eventManager);

            // No bar behind them, and plain panel-coloured buttons, so the sub-tabs
            // read as buttons rather than a second tab bar.
            Color panel = Theme.Current.Panel.XNA;
            ToolTexture buttonBackground = new ToolTexture(panel.R, panel.G, panel.B, panel.A);

            TabButtonsNode tabButtons = new TabButtonsNode(ToolTexture.Transparent, buttonBackground, Theme.Current.Hover.XNA, Theme.Current.Tabs.Text.XNA);
            tabButtons.RelativeBounds = new RectangleF(0, 0, 1, 0.035f);
            tabButtons.MinimumSlots = 6;
            tabButtons.Padding = 0.05f;
            AddChild(tabButtons);

            tabs = new TabbedMultiNode(TimeSpan.FromSeconds(0.3f), tabButtons);
            tabs.RelativeBounds = new RectangleF(0, tabButtons.RelativeBounds.Bottom, 1, 1 - tabButtons.RelativeBounds.Bottom);
            tabs.OnTabChange += (text, node) => { OnTabChange?.Invoke(text, node); };
            AddChild(tabs);

            tabs.AddTab("Lap Records", LapRecordsSummaryNode, (mie) => { ShowLapRecords(); });
            tabs.AddTab("Lap Count", LapCountSummaryNode, (mie) => { ShowLapCount(); });
            tabs.AddTab("Points", PointsSummaryNode, (mie) => { ShowPoints(); });

            // Start on Lap Records. A first show raises no tab change.
            tabs.Show(LapRecordsSummaryNode);
        }

        // Shows whichever sub-tab was last open, refreshed.
        public void Show()
        {
            if (Showing == LapCountSummaryNode)
                ShowLapCount();
            else if (Showing == PointsSummaryNode)
                ShowPoints();
            else
                ShowLapRecords();
        }

        public void ShowPoints()
        {
            PointsSummaryNode.OrderByLast();
            PointsSummaryNode.Refresh();
            tabs.Show(PointsSummaryNode);
        }

        public void ShowLapCount()
        {
            LapCountSummaryNode.OrderByLast();
            LapCountSummaryNode.Refresh();
            tabs.Show(LapCountSummaryNode);
        }

        public void ShowLapRecords()
        {
            LapRecordsSummaryNode.Refresh();
            tabs.Show(LapRecordsSummaryNode);
        }
    }
}
