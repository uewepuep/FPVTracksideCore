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
    public class StageNode : Node
    {
        private BorderNode borderNode;

        private List<Node> toWrap;

        public Stage Stage { get; private set; }

        public EventManager EventManager { get; private set; }

        public StageNode(EventManager eventManager, Stage stage)
        {
            EventManager = eventManager;

            Stage = stage;
            toWrap = new List<Node>();

            borderNode = new BorderNode(Color.Yellow);
            AddChild(borderNode);
        }

        public void SetNodes(IEnumerable<Node> nodes)
        {
            lock (toWrap)
            {
                toWrap.Clear();
                toWrap.AddRange(nodes);
            }
        }

        public override void Layout(RectangleF parentBounds)
        {
            lock (toWrap)
            {
                int right = toWrap.Select(e => e.Bounds.Right).Max();
                int left = toWrap.Select(e => e.Bounds.X).Min();

                SetBounds(new RectangleF(left, parentBounds.Y, right - left, parentBounds.Height));
                Scale(1, 0.99f);
            }

            base.Layout(parentBounds);
        }

        public void AddWrapNode(Node node)
        {
            lock (toWrap)
            {
                toWrap.Add(node);
            }
        }

        public void CleanUp()
        {
            lock (toWrap)
            {
                toWrap.RemoveAll(t => t.Disposed);
            }

            if (!toWrap.Any())
            {
                Dispose();
            }
        }

        public override bool OnDrop(MouseInputEvent finalInputEvent, Node node)
        {
            EventRoundNode eventRoundNode = node as EventRoundNode;
            if (eventRoundNode != null)
            {
                Round round = eventRoundNode.Round;
                if (round != null)
                {
                    EventManager.RoundManager.SetStage(round, Stage);
                }
            }
            return base.OnDrop(finalInputEvent, node);
        }
    }
}
