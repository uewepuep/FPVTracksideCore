using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class EventStatusNodeTopBar : Node
    {
        private TopBarNode topBar;
        private EventStatusNode eventStatus;

        public EventStatusNodeTopBar(EventManager eventManager)
        {
            topBar = new TopBarNode();
            topBar.RelativeBounds = new RectangleF(0, 0, 1, 0.1f);
            topBar.Init(eventManager, null);
            topBar.DisableTimeNodes();
            AddChild(topBar);

            eventStatus = new EventStatusNode(eventManager);
            eventStatus.RelativeBounds = new RectangleF(0, 0.12f, 1, 0.85f);
            AddChild(eventStatus);
        }
    }


    public class EventStatusNode : Node
    {
        private NamedRaceNode prevRaceNode;
        private NamedRaceNode currentRaceNode;
        private NamedRaceNode nextRaceNode;

        private LapRecordsSummaryNode lapRecordsNode;
        private PointsSummaryNode pointsSummaryNode;
        private LapCountSummaryNode lapCountSummaryNode;


        private EventManager eventManager;

        private Node resultsContainer;

        public EventStatusNode(EventManager eventManager)
        {
            this.eventManager = eventManager;

            Node racesContainer = new Node();
            racesContainer.RelativeBounds = new RectangleF(0, 0, 1, 0.3f);
            AddChild(racesContainer);

            prevRaceNode = new NamedRaceNode("Prev Race", eventManager);
            currentRaceNode = new NamedRaceNode("Current Race", eventManager);
            nextRaceNode = new NamedRaceNode("Next Race", eventManager);
            
            racesContainer.AddChild(prevRaceNode, currentRaceNode, nextRaceNode);
            AlignHorizontally(0.2f, racesContainer.Children);

            resultsContainer = new Node();
            resultsContainer.RelativeBounds = new RectangleF(0, 0.33f, 1, 0.65f);
            AddChild(resultsContainer);

            lapRecordsNode = new LapRecordsSummaryNode(eventManager);
            pointsSummaryNode = new PointsSummaryNode(eventManager);
            lapCountSummaryNode = new LapCountSummaryNode(eventManager);

            lapRecordsNode.ItemHeight = pointsSummaryNode.ItemHeight = lapCountSummaryNode.ItemHeight = 30;

            resultsContainer.AddChild(lapRecordsNode, pointsSummaryNode, lapCountSummaryNode);

            eventManager.RaceManager.OnRaceChanged += OnRace;
            eventManager.RaceManager.OnRaceEnd += OnRace;
            eventManager.RaceManager.OnRaceCreated += OnRace;

            Refresh();
        }

        public override void Dispose()
        {
            eventManager.RaceManager.OnRaceChanged -= OnRace;
            eventManager.RaceManager.OnRaceEnd -= OnRace;
            eventManager.RaceManager.OnRaceCreated -= OnRace;
            base.Dispose();
        }

        private void OnRace(Race race)
        {
            Refresh();
        }

        private void Refresh()
        {
            prevRaceNode.SetRace(eventManager.RaceManager.GetPrevRace());
            currentRaceNode.SetRace(eventManager.RaceManager.CurrentRace);
            nextRaceNode.SetRace(eventManager.RaceManager.GetNextRace(true, false));


            lapCountSummaryNode.Visible = eventManager.Event.EventType == EventTypes.Endurance;
            pointsSummaryNode.Visible = eventManager.Event.EventType == EventTypes.Race;

            if (pointsSummaryNode.Visible)
            {
                pointsSummaryNode.Refresh();
            }

            if (lapCountSummaryNode.Visible)
            {
                lapCountSummaryNode.Refresh();
            }

            AlignHorizontally(0.1f, resultsContainer.VisibleChildren.ToArray());

            RequestLayout();
        }
    }
}
