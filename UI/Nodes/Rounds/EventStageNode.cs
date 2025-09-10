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
            AddChild(StageNode);

            var roundNodes = roundsNode.RoundNodes.Where(rn => rn.Round.Stage == Stage);
            StageNode.AddWrapNodes(roundNodes);

            headingbg.Color = StageNode.Color;
            headingbg.SetFilename(null);
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

        public override void Layout(RectangleF parentBounds)
        {
            base.Layout(parentBounds);

            RectangleF adjustedBounds = parentBounds;
            adjustedBounds.Y = BoundsF.Y;
            adjustedBounds.Height = BoundsF.Height;

            StageNode.Layout(adjustedBounds);
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
