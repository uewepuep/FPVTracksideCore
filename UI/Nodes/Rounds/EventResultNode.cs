using Composition;
using Composition.Input;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes.Rounds
{
    public abstract class EventResultNode : EventXNode
    {
        public StageNode StageNode { get; private set; }

        public Stage Stage
        {
            get
            {
                return Round.Stage;
            }
        }

        public RoundsNode RoundsNode { get; private set; }

        public EventResultNode(RoundsNode roundsNode, EventManager ev, Round round) 
            : base(ev, round)
        {
            RoundsNode = roundsNode;

            StageNode = new StageNode(roundsNode, ev, round.Stage);
            StageNode.AddWrapNode(this);
            AddChild(StageNode);

            headingbg.Color = StageNode.Color;
            headingbg.SetFilename(null);

            UpdateRoundNodes();

            canSum = canAddTimes = canAddLapCount = false;
        }

        protected override void LayoutChildren(RectangleF bounds)
        {
            foreach (Node n in Children)
            {
                if (n == StageNode)
                    continue;
                n.Layout(bounds);
            }
        }

        protected void UpdateRoundNodes()
        {
            List<Node> roundNodes = RoundsNode.RoundNodes.Where(rn => rn.Round.Stage == Stage).OfType<Node>().ToList();
            roundNodes.Add(this);
            StageNode.SetNodes(roundNodes);

            StageNode.Refresh();
        }

        public override void SetHeading(string text)
        {
            string stage = "";
            if (Round.Stage != null)
            {
                stage = Round.Stage.ToString() + " - ";
            }

            base.SetHeading(stage + text);
        }


        public override void Layout(RectangleF parentBounds)
        {
            base.Layout(parentBounds);
            StageNode.Layout(parentBounds);
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            base.Draw(id, parentAlpha);
            StageNode.Draw(id, parentAlpha);
        }

        public override void DrawChildren(Drawer id, float parentAlpha)
        {
            foreach (Node n in Children)
            {
                if (n == StageNode)
                    continue;

                if (n.Drawable)
                {
                    n.Draw(id, parentAlpha * Alpha);
                }
            }
        }

        public abstract bool HasResult();

    }
}
