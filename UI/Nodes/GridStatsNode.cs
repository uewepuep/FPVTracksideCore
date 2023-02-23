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

        public GridStatsNode(EventManager eventManager)
        {
            EventManager = eventManager;

            if (GeneralSettings.Instance.GridShowPBs)
            {
                ColorNode background = new ColorNode(Theme.Current.PanelAlt.XNA);
                AddChild(background);

                pBList = new LapRecordsSummaryNode(eventManager);
                pBList.ShowPositions = false;
                pBList.RelativeBounds = new RectangleF(0, 0, 1, 1);
                background.AddChild(pBList);

                EventManager.RaceManager.OnPilotAdded += RaceManager_OnPilotAdded;
            }
        }

        public override void Dispose()
        {
            if (GeneralSettings.Instance.GridShowPBs)
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
