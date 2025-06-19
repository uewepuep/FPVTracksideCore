using Composition.Nodes;
using ExternalData;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Joins;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class PatreonsNode : Node
    {
        public const int PerLine = 2;
        public const float LinePadding = 0.2f;

        private ListNode<Node> listNode;

        private Node top;

        public PatreonsNode() 
        {
            BorderPanelNode background = new BorderPanelNode();
            background.RelativeBounds = new RectangleF(0.0f, 0.02f, 1.0f, 0.98f);
            background.Scale(0.8f, 1);
            AddChild(background);

            HeadingNode heading = new HeadingNode(Theme.Current.InfoPanel, "The Patreons of FPVTrackside");
            heading.RelativeBounds = new RectangleF(0, 0, 1, 0.08f);
            background.AddChild(heading);

            listNode = new ListNode<Node>(Theme.Current.ScrollBar.XNA);
            listNode.RelativeBounds = new RectangleF(0, heading.RelativeBounds.Bottom, 1, 1 - heading.RelativeBounds.Bottom);
            listNode.ItemHeight = 100;
            listNode.ItemPadding = 10;
            background.AddChild(listNode);

            top = new Node();

            TextNode details = new TextNode("Thank you to all our wonderful supporters.", Theme.Current.InfoPanel.Text.XNA);
            details.Alignment = RectangleAlignment.Center;
            details.RelativeBounds = new RectangleF(0.2f, 0, 0.5f, 1);
            details.Scale(0.8f, 0.5f);
            top.AddChild(details);

            TextButtonNode learnMore = new TextButtonNode("Learn More", Theme.Current.InfoPanel.Heading.XNA, Theme.Current.Hover.XNA, Theme.Current.InfoPanel.HeadingText.XNA);
            learnMore.RelativeBounds = new RectangleF(0.7f, 0, 0.15f, 1);
            learnMore.Scale(1, 0.5f);
            learnMore.OnClick += (mie) =>
            {
                DataTools.StartBrowser("https://www.patreon.com/fpvtrackside");
            };
            top.AddChild(learnMore);

            listNode.AddChild(top);
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        public void Refresh()
        {
            top.Remove();

            listNode.ClearDisposeChildren();

            listNode.AddChild(top);

            Node container = new Node();

            Patreon[] patreons;

            using (IDatabase db = DatabaseFactory.Open(Guid.Empty))
            {
                patreons = db.All<Patreon>().Where(p => p.Active).Randomise().ToArray();
            }

            foreach (Patreon patreon in patreons)
            {
                PatreonNode pn = new PatreonNode();
                pn.SetPatreon(patreon);
                container.AddChild(pn);

                if (container.ChildCount == PerLine)
                {
                    AlignHorizontally(LinePadding, container.Children);
                    listNode.AddChild(container);
                    container = new Node();
                }
            }

            if (container.ChildCount > 0)
            {
                List<Node> nodes = container.Children.ToList();

                for(int i = nodes.Count; i < PerLine; i++)
                {
                    nodes.Add(null);
                }

                AlignHorizontally(LinePadding, nodes.ToArray());
                listNode.AddChild(container);
            }
            else
            {
                container.Dispose();
            }

            RequestLayout();
            listNode.RequestLayout();
        }
    }

    public class PatreonNode : Node
    {
        public ImageNode ImageNode { get; private set; }
        public TextNode NameNode { get; private set; }
        public TextNode InfoNode { get; private set; }

        public PatreonNode()
        {
            ImageNode = new ImageNode();
            ImageNode.RelativeBounds = new RectangleF(0, 0, 0.3f, 1);
            ImageNode.Scale(0.8f);
            AddChild(ImageNode);

            Node container = new Node();
            container.RelativeBounds = new RectangleF(0.35f, 0.2f, 1 - ImageNode.RelativeBounds.Right, 0.65f);
            AddChild(container);

            NameNode = new TextNode("", Theme.Current.TextMain.XNA);
            NameNode.RelativeBounds = new RectangleF(0, 0, 1, 0.6f);
            NameNode.Alignment = RectangleAlignment.CenterLeft;
            container.AddChild(NameNode);

            InfoNode = new TextNode("", Theme.Current.TextMain.XNA);
            InfoNode.RelativeBounds = new RectangleF(0, NameNode.RelativeBounds.Bottom, 1, 1 - NameNode.RelativeBounds.Bottom);
            InfoNode.Scale(1, 0.8f);
            InfoNode.Style.Italic = true;
            InfoNode.Alignment = RectangleAlignment.CenterLeft;
            container.AddChild(InfoNode);

            container.CollapseDispose();
        }

        public void SetPatreon(Patreon patreon)
        {
            NameNode.Text = patreon.Name;
            InfoNode.Text = patreon.Tier + " since " + patreon.StartDate.ToString("MMMM") + " " + patreon.StartDate.Year;
            ImageNode.SetFilename(patreon.ThumbFilename);
        }

        public void SetPatreon(string name, string info, string filename)
        {
            NameNode.Text = name;
            InfoNode.Text = info;
            ImageNode.SetFilename(filename);
        }
    }
}
