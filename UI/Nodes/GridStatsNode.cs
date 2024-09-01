using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using Tools;

namespace UI.Nodes
{
    public class GridStatsNode : AnimatedNode
    {
        public EventManager EventManager { get; private set; }

        private LapRecordsSummaryNode pBList;

        private RenderTargetNode renderTargetNode;

        public GridStatsNode(EventManager eventManager)
        {
            EventManager = eventManager;

            renderTargetNode = new RenderTargetNode(575, 465);
            renderTargetNode.CanScroll = false;
            AddChild(renderTargetNode);

            if (ApplicationProfileSettings.Instance.GridShowPBs)
            {
                ColorNode background = new ColorNode(Theme.Current.PanelAlt.XNA);
                renderTargetNode.AddChild(background);

                pBList = new LapRecordsSummaryNode(eventManager);
                pBList.ShowPositions = false;
                pBList.CanTint = false;
                pBList.RelativeBounds = new RectangleF(0, 0, 1, 1);
                pBList.SetHeadingsHeight(0.1f, 0.08f, 35);
                background.AddChild(pBList);

                EventManager.RaceManager.OnPilotAdded += RaceManager_OnPilotAdded;
            }
        }

        public override void Dispose()
        {
            if (ApplicationProfileSettings.Instance.GridShowPBs)
            {
                EventManager.RaceManager.OnPilotAdded -= RaceManager_OnPilotAdded;
            }
            base.Dispose();
        }

        private void RaceManager_OnPilotAdded(PilotChannel pc)
        {
            Race current = EventManager.RaceManager.CurrentRace;

            if (current != null)
            {
                pBList.SetFilterPilots(current.Pilots);
            }
        }
    }
}
