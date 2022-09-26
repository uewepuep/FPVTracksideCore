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

namespace UI.Nodes
{
    public class PilotChanelList : Node
    {
        private Dictionary<Channel, ListNode<TextNode>> channelLists;

        private EventManager eventManager;

        private bool needsPilotRefresh;
        private bool needsChannelRefresh;

        private PanelNode panelNode;
        public PilotChanelList(EventManager em)
        {
            eventManager = em;
            eventManager.OnPilotChangedChannels += EventManager_OnPilotChangedChannels;
            eventManager.OnChannelsChanged += EventManager_OnChannelsChanged;
            eventManager.OnPilotRefresh += EventManager_OnPilotRefresh;

            panelNode = new PanelNode();
            AddChild(panelNode);

            channelLists = new Dictionary<Channel, ListNode<TextNode>>();
            
            needsChannelRefresh = true;
        }

        private void EventManager_OnPilotRefresh()
        {
            needsPilotRefresh = true;
        }

        public override void Dispose()
        {
            eventManager.OnPilotChangedChannels -= EventManager_OnPilotChangedChannels;
            eventManager.OnChannelsChanged -= EventManager_OnChannelsChanged;
            eventManager.OnPilotRefresh -= EventManager_OnPilotRefresh;

            base.Dispose();
        }

        private void EventManager_OnPilotChangedChannels(PilotChannel pc)
        {
            needsPilotRefresh = true;
        }


        private void EventManager_OnChannelsChanged()
        {
            needsChannelRefresh = true;
        }

        private void SetChannels(IEnumerable<Channel> channels)
        {
            panelNode.ClearDisposeChildren();

            HeadingNode heading = new HeadingNode(Theme.Current.InfoPanel, "Channel List");
            panelNode.AddChild(heading);

            channelLists = new Dictionary<Channel, ListNode<TextNode>>();

            Node headings = new Node();
            headings.RelativeBounds = new Tools.RectangleF(0, heading.RelativeBounds.Bottom, 1, 0.05f);
            panelNode.AddChild(headings);

            Node lists = new Node();
            lists.RelativeBounds = new Tools.RectangleF(0, headings.RelativeBounds.Bottom, 1, 1 - headings.RelativeBounds.Bottom);
            panelNode.AddChild(lists);

            foreach (var lane in channels.GetChannelGroups())
            {
                if (!lane.Any())
                    continue;

                string channelText = string.Join(", ", lane.Select(r => r.ToStringShort()));

                Node headingNode = new Node();
                headings.AddChild(headingNode);

                ColorNode headingColor = new ColorNode(eventManager.GetChannelColor(lane.First()));
                headingColor.RelativeBounds = new Tools.RectangleF(0, 0.8f, 1, 0.2f);
                headingNode.AddChild(headingColor);

                TextNode headingText = new TextNode(channelText, Theme.Current.TextMain.XNA);
                headingText.Alignment = RectangleAlignment.Center;
                headingText.RelativeBounds = new Tools.RectangleF(0, 0.05f, 1, 0.9f);
                headingNode.AddChild(headingText);

                ListNode<TextNode> pilotListNode = new ListNode<TextNode>(Theme.Current.ScrollBar.XNA);
                pilotListNode.ItemHeight = 30;
                lists.AddChild(pilotListNode);

                foreach (Channel c in lane)
                {
                    channelLists.Add(c, pilotListNode);
                }
            }

            AlignHorizontally(0.05f, headings.Children.ToArray());
            AlignHorizontally(0.05f, lists.Children.ToArray());

            needsPilotRefresh = true;
            needsChannelRefresh = false;
            RequestLayout();
        }

        private void SetPilots(IEnumerable<PilotChannel> pilotChannels)
        {
            foreach (var kvp in channelLists)
            {
                ListNode<TextNode> list = kvp.Value;
                list.ClearDisposeChildren();
            }

            foreach (var kvp in channelLists)
            {
                Channel c = kvp.Key;
                ListNode<TextNode> list = kvp.Value;

                IEnumerable<Pilot> alphabetical = pilotChannels.Where(pc => pc.Channel == c && !pc.Pilot.PracticePilot).Select(pc => pc.Pilot).OrderBy(p => p.Name);
                foreach (Pilot p in alphabetical)
                {
                    TextNode tn = new TextNode(p.Name, Theme.Current.TextMain.XNA);
                    list.AddChild(tn);
                }
                list.RequestLayout();
            }
            needsPilotRefresh = false;
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            if (needsChannelRefresh)
            {
                SetChannels(eventManager.Channels);
            }

            if (needsPilotRefresh)
            {
                SetPilots(eventManager.Event.PilotChannels);
            }

            base.Draw(id, parentAlpha);
        }

        public override bool OnDrop(MouseInputEvent finalInputEvent, Node node)
        {
            base.OnDrop(finalInputEvent, node);

            return true;
        }

    }
}
