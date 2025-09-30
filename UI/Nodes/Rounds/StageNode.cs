using Composition.Input;
using Composition.Layers;
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

        public Color Color { get; private set; }

        public TitleNode Title { get; private set; }

        public StageNode(RoundsNode roundsNode, EventManager eventManager, Stage stage)
        {
            Stage = stage;
            EventManager = eventManager;
            RoundsNode = roundsNode;
            Color = Theme.Current.Rounds.Heading.XNA;

            toWrap = new List<Node>();

            borderNode = new BorderNode(Color);
            borderNode.Width = 2;
            AddChild(borderNode);

            if (stage != null)
            {
                if (stage.Color != Color.Transparent)
                    Color = stage.Color;

                Title = new TitleNode(stage.Name, Color);
                AddChild(Title);
            }
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
                int padding = borderNode.Width;
                int titleWidth = 20;
                int right = toWrap.Select(e => e.Bounds.Right).Max();
                int left = toWrap.Select(e => e.Bounds.X).Min();

                if (Title != null)
                {
                    RectangleF titleBounds = parentBounds;
                    titleBounds.X = left - titleWidth;
                    titleBounds.Width = titleWidth;
                    Title.Layout(titleBounds);
                }

                left -= padding;
                right += padding;

                RectangleF bounds = new RectangleF(left - titleWidth, parentBounds.Y, (right - left) + titleWidth, parentBounds.Height);
                SetBounds(bounds, BoundsF);
                borderNode.Layout(bounds);
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

        public override Rectangle? CanDrop(MouseInputEvent finalInputEvent, Node node)
        {
            EventRoundNode eventRoundNode = node as EventRoundNode;
            if (eventRoundNode != null)
                return Bounds;

            return base.CanDrop(finalInputEvent, node);
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
            BoundsF = bounds;
        }


        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.Button == MouseButtons.Right && mouseInputEvent.ButtonState == ButtonStates.Released && Title.Contains(mouseInputEvent.Position))
            {
                MouseMenu mm = new MouseMenu(this);

                mm.AddItem("Edit Stage", EditStage);

                mm.Show(mouseInputEvent);
            }
            return base.OnMouseInput(mouseInputEvent);
        }


        public void EditStage()
        {
            ObjectEditorNode<Stage> editor = new ObjectEditorNode<Stage>(Stage);
            GetLayer<PopupLayer>().Popup(editor);
            editor.OnOK += (r) =>
            {
                if (editor.Selected != null)
                {
                    using (IDatabase db = DatabaseFactory.Open(EventManager.EventId))
                    {
                        db.Upsert(editor.Selected);
                    }
                    RoundsNode.Refresh();
                }
            };
        }

        public void Refresh()
        {
            Title.TextNode.Text = Stage.Name;
        }
    }

    public class TitleNode : Node
    {
        public TextNode TextNode { get; private set; }

        public ColorNode ColorNode { get; private set; }

        public TitleNode(string text, Color color) 
        {
            ColorNode = new ColorNode(color);
            AddChild(ColorNode);

            TextNode = new TextVerticalNode(text, Theme.Current.Rounds.Text.XNA);
            AddChild(TextNode);
        }
    }
}
