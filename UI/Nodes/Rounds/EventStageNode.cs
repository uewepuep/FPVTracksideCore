using Composition;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes.Rounds
{
    public abstract class EventStageNode : EventXNode
    {
        public StageNode StageNode { get; set; }

        public Stage Stage
        {
            get
            {
                return Round.Stage;
            }
        }

        public EventStageNode(RoundsNode roundsNode, EventManager ev, Round round) 
            : base(ev, round)
        {
            StageNode = new StageNode(roundsNode, ev, round.Stage);
            StageNode.AddWrapNode(this);

            var roundNodes = roundsNode.RoundNodes.Where(rn => rn.Round.Stage == Stage);
            StageNode.AddWrapNodes(roundNodes);
        }

        public override void Layout(RectangleF parentBounds)
        {
            StageNode.Layout(parentBounds);

            base.Layout(parentBounds);
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            base.Draw(id, parentAlpha);
            StageNode?.Draw(id, parentAlpha);   
        }

        public abstract bool HasResult();

    }
}
