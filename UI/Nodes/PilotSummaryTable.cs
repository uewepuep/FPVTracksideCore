using Composition;
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
    public class PilotSummaryTable : Node
    {
        protected EventManager eventManager;

        protected ListNode<PilotResultNode> rows;

        protected Node headings;
        protected int columnToOrderBy;
        protected bool orderByLast;

        public int ItemHeight { get { return rows.ItemHeight; } set { rows.ItemHeight = value; } }

        public PilotSummaryTable(EventManager eventManager, string name)
        {
            this.eventManager = eventManager;

            PanelNode panelNode = new PanelNode();
            AddChild(panelNode);

            HeadingNode heading = new HeadingNode(Theme.Current.InfoPanel, name);
            panelNode.AddChild(heading);

            headings = new Node();
            headings.RelativeBounds = new Tools.RectangleF(0, heading.RelativeBounds.Bottom, 1, 0.05f);
            panelNode.AddChild(headings);

            rows = new ListNode<PilotResultNode>(Theme.Current.ScrollBar.XNA);
            rows.RelativeBounds = new Tools.RectangleF(0, headings.RelativeBounds.Bottom, 1, 1 - headings.RelativeBounds.Bottom);
            rows.ItemHeight = 40;
            rows.ItemPadding = 0;
            rows.Scale(0.99f);

            rows.BackgroundColors = new Color[]
            {
                new Color(Theme.Current.PanelAlt.XNA, 0.5f),
                new Color(Theme.Current.Panel.XNA, 0.5f)
            };

            panelNode.AddChild(rows);

            eventManager.RaceManager.OnRaceEnd += (Race race) => { Refresh(); };
        }

        public void OrderByLast()
        {
            orderByLast = true;
        }

        public void Refresh()
        {
            IEnumerable<Pilot> pilots = eventManager.Event.Pilots.Where(p => !p.PracticePilot).Distinct();


            headings.ClearDisposeChildren();

            Node container = new Node();
            container.RelativeBounds = new RectangleF(0.3f, 0, 0.7f, 1);
            headings.AddChild(container);


            // Add position heading
            {
                Node headingNode = new Node();
                headings.AddChild(headingNode);

                TextNode position = new TextNode("Position", Theme.Current.InfoPanel.Text.XNA);
                position.RelativeBounds = new RectangleF(0.0f, 0.17f, 0.1f, 0.73f);
                headingNode.AddChild(position);
            }

            // Add pilot name heading
            {
                Node headingNode = new Node();
                headings.AddChild(headingNode);

                TextButtonNode pilotsHeading = new TextButtonNode("Pilots", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
                pilotsHeading.TextNode.Alignment = RectangleAlignment.TopCenter;
                pilotsHeading.RelativeBounds = new RectangleF(0.1f, 0, 0.2f, 1);
                headingNode.AddChild(pilotsHeading);
                pilotsHeading.OnClick += (mie) => { columnToOrderBy = 0; Refresh(); };
            }

            Round[] rounds;
            int column;

            CreateHeadings(container, out rounds, out column);

            if (orderByLast)
            {
                columnToOrderBy = column;
                orderByLast = false;
            }

            AlignHorizontally(0.04f, container.Children.ToArray());

            int index = 0;
            foreach (Pilot p in pilots)
            {
                if (p == null)
                    continue;

                PilotResultNode t = GetCreateNode(index);
                SetResult(t, p, rounds);

                index++;
            }

            for (; index < rows.ChildCount; index++)
            {
                PilotResultNode t = GetCreateNode(index);
                t.Dispose();
            }

            SetOrder();

            for (int i = 0; i < rows.ChildCount; i++)
            {
                PilotResultNode pilotLapsNode = rows.GetChild<PilotResultNode>(i);
                if (pilotLapsNode != null)
                {
                    pilotLapsNode.Position = i + 1;
                }
            }

            rows.RequestLayout();
            RequestLayout();
            RequestRedraw();
        }

        public virtual void SetOrder()
        {
            
        }

        public virtual void CreateHeadings(Node container, out Round[] rounds, out int column)
        {
            rounds = eventManager.Event.Rounds.OrderBy(r => r.Order).ThenBy(r => r.RoundNumber).ToArray();

            column = 0;
            foreach (Round r in rounds)
            {
                column++;
                int ca = column;

                TextButtonNode headingText = new TextButtonNode(r.ToStringShort(), Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
                headingText.TextNode.Alignment = RectangleAlignment.TopRight;
                headingText.OnClick += (mie) => { columnToOrderBy = ca; Refresh(); };
                container.AddChild(headingText);
            }
        }

        protected virtual void SetResult(PilotResultNode pilotResNode, Pilot pilot, Round[] rounds)
        {
            
        }

        private PilotResultNode GetCreateNode(int index)
        {
            while (index >= rows.ChildCount)
            {
                PilotResultNode tn = new PilotResultNode(eventManager);
                rows.AddChild(tn);
            }
            return (PilotResultNode)rows.GetChild(index);
        }

        protected class PilotResultNode : Node
        {
            public Pilot Pilot { get; private set; }
            private TextNode pilotName;

            private EventManager eventManager;
            private Node cont;

            private TextNode positionNode;

            private int position;
            public int Position
            {
                get
                {
                    return position;
                }
                set
                {
                    position = value;
                    positionNode.Text = value.ToStringPosition();
                }
            }

            public PilotResultNode(EventManager eventManager)
            {
                this.eventManager = eventManager;

                positionNode = new TextNode("", Theme.Current.InfoPanel.Text.XNA);
                positionNode.RelativeBounds = new RectangleF(0, 0, 0.1f, 0.75f);
                positionNode.Alignment = RectangleAlignment.BottomCenter;
                AddChild(positionNode);

                pilotName = new TextNode("", Theme.Current.InfoPanel.Text.XNA);
                pilotName.RelativeBounds = new RectangleF(0.1f, 0, 0.2f, 0.75f);
                pilotName.Alignment = RectangleAlignment.BottomCenter;
                AddChild(pilotName);

                cont = new Node();
                cont.RelativeBounds = new RectangleF(pilotName.RelativeBounds.Right, 0, 1 - pilotName.RelativeBounds.Right, pilotName.RelativeBounds.Height);
                AddChild(cont);
            }

            public void Set(Pilot p, IEnumerable<Node> resultNodes)
            {
                Pilot = p;
                pilotName.Text = p.Name;

                foreach (Node n in cont.Children)
                {
                    if (n != pilotName)
                    {
                        n.Dispose();
                    }
                }

                foreach (Node n in resultNodes)
                {
                    cont.AddChild(n);
                }

                AlignHorizontally(0.04f, cont.Children.ToArray());
            }

            public double GetValue(int columnToOrderBy)
            {
                Node n = cont.GetChild(columnToOrderBy - 1);

                ResultNode rn = n as ResultNode;
                if (rn != null && rn.Result != null)
                {
                    return rn.Result.Points;
                }

                TextNode tn = n as TextNode;
                if (tn != null)
                {
                    double output;
                    if (double.TryParse(tn.Text, out output))
                    {
                        return output;
                    }
                }

                return 0;
            }
        }
    }


}
