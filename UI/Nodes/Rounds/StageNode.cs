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
        private ColorNode colorNode;

        private List<Node> toWrap;

        public Stage Stage { get; private set; }

        public EventManager EventManager { get; private set; }
        public RoundsNode RoundsNode { get; private set; }

        public EventRoundNode[] EventRoundNodes
        {
            get
            {
                lock (toWrap)
                {
                    return toWrap.OfType<EventRoundNode>().ToArray();
                }
            }
        }

        public StageNode(RoundsNode roundsNode, EventManager eventManager, Stage stage)
        {
            EventManager = eventManager;
            RoundsNode = roundsNode;

            Stage = stage;
            toWrap = new List<Node>();


            borderNode = new BorderNode(Stage.Color);
            borderNode.Width = 5;
            AddChild(borderNode);

            colorNode = new ColorNode(Stage.Color);
            AddChild(colorNode);

            CloseNode closeNode = new CloseNode();
            closeNode.OnClick += CloseNode_OnClick;
            colorNode.AddChild(closeNode);
        }

        private void CloseNode_OnClick(MouseInputEvent mie)
        {
            EventManager.RoundManager.DeleteStage(Stage);
            Dispose();
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
            base.Layout(parentBounds);

            lock (toWrap)
            {
                int right = toWrap.Select(e => e.Bounds.Right).Max();
                int left = toWrap.Select(e => e.Bounds.X).Min();

                int padding = borderNode.Width;
                left -= padding;
                right += padding;

                int rightWidth = 20;

                RectangleF bounds = new RectangleF(left, parentBounds.Y, (right - left)  + rightWidth, parentBounds.Height);
                SetBounds(bounds, parentBounds);
                colorNode.Layout(new RectangleF(bounds.Right - rightWidth, bounds.Y, rightWidth, bounds.Height));
            }
        }

        public void AddWrapNode(Node node)
        {
            lock (toWrap)
            {
                toWrap.Add(node);
            }
        }

        public void AddWrapNodes(IEnumerable<Node> nodes)
        {
            lock (toWrap)
            {
                toWrap.AddRange(nodes);
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
                    RoundsNode.OrderByDrop(eventRoundNode, finalInputEvent.Position.X);
                    return true;
                }
            }
            return base.OnDrop(finalInputEvent, node);
        }

        public void RemoveWrapped(Node node)
        {
            lock (toWrap)
            {
                toWrap.Remove(node);
            }
        }

        public void SetBounds(RectangleF bounds, RectangleF parentBounds)
        {
            RelativeBounds = new RectangleF(
                (bounds.X - parentBounds.X) / parentBounds.Width,
                (bounds.Y - parentBounds.Y) / parentBounds.Height,
                bounds.Width / parentBounds.Width,
                bounds.Height / parentBounds.Height
                );
        }
    }
}
