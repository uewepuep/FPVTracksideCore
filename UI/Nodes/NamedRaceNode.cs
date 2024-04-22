using Composition;
using Composition.Input;
using Composition.Nodes;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;
using UI.Nodes.Rounds;

namespace UI.Nodes
{
    public class NamedRaceNode : AnimatedRelativeNode
    {
        private EventRaceNode eventRaceNode;
        private TextNode label;
        private EventManager eventManager;
        private BorderPanelShadowNode panel;

        private NextRoundNode nextRoundNode;

        public NamedRaceNode(string name, EventManager eventManager)
        {
            this.eventManager = eventManager;

            panel = new BorderPanelShadowNode(Theme.Current.Rounds.Background, Theme.Current.Rounds.Border.XNA);
            AddChild(panel);

            label = new TextNode(name, Theme.Current.TextMain.XNA);
            label.RelativeBounds = new RectangleF(0, 0.01f, 1, 0.1f);
            panel.AddChild(label);

            nextRoundNode = new NextRoundNode(eventManager);
            nextRoundNode.Visible = false;
            panel.AddChild(nextRoundNode);
        }

        public Race Race { get; private set; }

        public void SetRace(Race race)
        {
            nextRoundNode.Visible = false;

            if (eventRaceNode != null)
            {
                eventRaceNode.Dispose();
                eventRaceNode = null;
            }

            Race = race;

            if (race == null)
            {
                return;
            }

            eventRaceNode = new EventRaceNode(eventManager, race);
            eventRaceNode.RelativeBounds = new RectangleF(0, label.RelativeBounds.Bottom, 1, 1 - label.RelativeBounds.Bottom);
            eventRaceNode.Scale(0.9f);
            eventRaceNode.Alignment = RectangleAlignment.TopCenter;
            eventRaceNode.NeedRefresh += () => { Refresh(); };
            eventRaceNode.NeedFullRefresh += () => { Refresh(); };


            panel.AddChild(eventRaceNode);
        }

        public void Refresh()
        {
            SetRace(Race);
        }

        public void ShowNextRoundOptions(Round currentRound)
        {
            nextRoundNode.Visible = true;
            nextRoundNode.RelativeBounds = new RectangleF(0, label.RelativeBounds.Bottom, 1, 1 - label.RelativeBounds.Bottom);
            nextRoundNode.Scale(0.9f);
            nextRoundNode.GenerateOptions(currentRound);
        }

        public override bool OnDrop(MouseInputEvent finalInputEvent, Node node)
        {
            base.OnDrop(finalInputEvent, node);
         
            
            IPilot ipilotnode = node as IPilot;
            return ipilotnode != null;
        }

    }
}
