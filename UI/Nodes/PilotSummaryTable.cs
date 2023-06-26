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
    public class PilotSummaryTable : Node, IWebbTable
    {
        protected EventManager eventManager;

        protected ListNode<PilotResultNode> rows;

        protected Node headings;
        protected int columnToOrderBy;
        protected bool orderByLast;

        public int ItemHeight { get { return rows.ItemHeight; } set { rows.ItemHeight = value; } }

        private bool needsRefresh;

        public bool ShowPositions { get; set; }

        public Pilot[] FilterPilots { get; private set; }

        private HeadingNode title;
        public string Name { get { return title.Text; } }

        public PilotSummaryTable(EventManager eventManager, string name)
        {
            ShowPositions = true;
            this.eventManager = eventManager;

            PanelNode panelNode = new PanelNode();
            AddChild(panelNode);

            title = new HeadingNode(Theme.Current.InfoPanel, name);
            panelNode.AddChild(title);

            headings = new Node();
            panelNode.AddChild(headings);

            rows = new ListNode<PilotResultNode>(Theme.Current.ScrollBar.XNA);
            rows.ItemPadding = 1;
            rows.Scale(0.99f);

            rows.BackgroundColors = new Color[]
            {
                new Color(Theme.Current.PanelAlt.XNA, 0.5f),
                new Color(Theme.Current.Panel.XNA, 0.5f)
            };

            panelNode.AddChild(rows);

            SetHeadingsHeight(0.05f, 0.05f, 40);

            eventManager.RaceManager.OnRaceEnd += (Race race) => { Refresh(); };

            needsRefresh = true;

        }

        public void SetHeadingsHeight(float titleHeight, float headingsHeight, int itemHeight)
        {
            title.RelativeBounds = new RectangleF(0, 0, 1, titleHeight);
            headings.RelativeBounds = new RectangleF(0, title.RelativeBounds.Bottom, 1, headingsHeight);
            rows.RelativeBounds = new RectangleF(0, headings.RelativeBounds.Bottom, 1, 1 - headings.RelativeBounds.Bottom);
            rows.ItemHeight = itemHeight;
        }

        public void OrderByLast()
        {
            orderByLast = true;
        }

        public void Refresh()
        {
            needsRefresh = true;
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (needsRefresh) 
            {
                needsRefresh = false;
                DoRefresh();
            }

            base.Draw(id, parentAlpha);
        }

        private void DoRefresh()
        {
            IEnumerable<Pilot> pilots = eventManager.Event.Pilots.Where(p => !p.PracticePilot).Distinct();

            if (FilterPilots != null)
            {
                pilots = pilots.Intersect(FilterPilots);
            }

            headings.ClearDisposeChildren();

            Node container = new Node();

            float right = 0;
            // Add position heading
            if (ShowPositions)
            {
                TextNode position = new TextNode("Position", Theme.Current.InfoPanel.Text.XNA);
                position.RelativeBounds = new RectangleF(0.0f, 0.17f, 0.1f, 0.73f);
                headings.AddChild(position);

                right = position.RelativeBounds.Right;
            }

            // Add pilot name heading
            {
                TextButtonNode pilotsHeading = new TextButtonNode("Pilots", Theme.Current.InfoPanel.Foreground.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.Text.XNA);
                pilotsHeading.TextNode.Alignment = RectangleAlignment.TopCenter;
                pilotsHeading.RelativeBounds = new RectangleF(right, 0, 0.2f, 1);
                headings.AddChild(pilotsHeading);
                pilotsHeading.OnClick += (mie) => { columnToOrderBy = 0; Refresh(); };

                container.RelativeBounds = new RectangleF(pilotsHeading.RelativeBounds.Right, 0, 1 - pilotsHeading.RelativeBounds.Right, 1);
            }

            Round[] rounds;
            int column;


            headings.AddChild(container);

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

            if (ShowPositions)
            {
                for (int i = 0; i < rows.ChildCount; i++)
                {
                    PilotResultNode pilotLapsNode = rows.GetChild<PilotResultNode>(i);
                    if (pilotLapsNode != null)
                    {
                        pilotLapsNode.Position = i + 1;
                    }
                }
            }

            rows.RequestLayout();
            RequestLayout();
            RequestRedraw();
        }

        public void SetFilterPilots(IEnumerable<Pilot> pilots)
        {
            FilterPilots = pilots.ToArray();
            Refresh();
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
                PilotResultNode tn = new PilotResultNode(eventManager, ShowPositions);
                rows.AddChild(tn);
            }
            return (PilotResultNode)rows.GetChild(index);
        }

        public IEnumerable<string> GetHeadings()
        {
            foreach (Node n2 in headings.Children)
            {
                TextNode tn2 = n2 as TextNode;
                if (tn2 != null)
                {
                    yield return tn2.Text;
                    continue;
                }
                TextButtonNode tbn2 = n2 as TextButtonNode;
                if (tbn2 != null)
                {
                    yield return tbn2.Text;
                    continue;
                }

                foreach (Node n in n2.Children)
                {
                    TextNode tn = n as TextNode;
                    if (tn != null)
                    {
                        yield return tn.Text;
                    }
                    TextButtonNode tbn = n as TextButtonNode;
                    if (tbn != null)
                    {
                        yield return tbn.Text;
                    }
                }
            }
        }


        public IEnumerable<IEnumerable<string>> GetTable()
        {
            DoRefresh();

            yield return GetHeadings();

            foreach (PilotResultNode row in rows.Children) 
            {
                yield return row.GetValues();
            }
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

                    if (positionNode!= null) 
                        positionNode.Text = value.ToStringPosition();
                }
            }

            public PilotResultNode(EventManager eventManager, bool position)
            {
                this.eventManager = eventManager;

                float right = 0.0f;
                if (position) 
                {
                    positionNode = new TextNode("", Theme.Current.InfoPanel.Text.XNA);
                    positionNode.RelativeBounds = new RectangleF(0, 0, 0.1f, 0.75f);
                    positionNode.Alignment = RectangleAlignment.BottomCenter;
                    AddChild(positionNode);

                    right = positionNode.RelativeBounds.Right;
                }

                pilotName = new TextNode("", Theme.Current.InfoPanel.Text.XNA);
                pilotName.RelativeBounds = new RectangleF(right, 0, 0.2f, 0.75f);
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

            public bool GetValue(int columnToOrderBy, out double value)
            {
                Node n = cont.GetChild(columnToOrderBy - 1);

                ResultNode rn = n as ResultNode;
                if (rn != null && rn.Result != null)
                {
                    value = rn.Result.Points;
                    return true;
                }

                TextNode tn = n as TextNode;
                if (tn != null)
                {
                    double output;
                    if (double.TryParse(tn.Text, out output))
                    {
                        value = output;
                        return true;
                    }
                }

                value = 0;
                return false;
            }

            public IEnumerable<string> GetValues() 
            { 
                yield return positionNode.Text;
                yield return pilotName.Text;

                foreach (TextNode n in cont.Children.OfType<TextNode>()) 
                {
                    yield return n.Text;
                }
            }
        }
    }


}
