using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timing;
using Tools;

namespace UI.Nodes
{
    public class RSSINode : Node
    {
        private EventManager eventManager;

        public RSSINode(EventManager ev)
        {
            eventManager = ev;
        }

        public void ReadRSSI()
        {
            RSSI[] rssis = eventManager.RaceManager.TimingSystemManager.GetRSSI().ToArray();

            RSSIChannelNode[] channelNodes = Children.OfType<RSSIChannelNode>().ToArray();

            List<RSSIChannelNode> usedNodes = new List<RSSIChannelNode>();

            foreach (RSSI rssi in rssis)
            {
                Channel channel = eventManager.Channels.FirstOrDefault(c => c.Frequency == rssi.Frequency);
                if (channel == null)
                {
                    continue;
                }

                RSSIChannelNode rssiNode = channelNodes.FirstOrDefault(r => r.Channel == channel);
                if (rssiNode == null)
                {
                    rssiNode = new RSSIChannelNode(eventManager, channel);
                    AddChild(rssiNode);
                }

                usedNodes.Add(rssiNode);
                rssiNode.SetRSSI(rssi);
            }

            foreach (RSSIChannelNode node in channelNodes) 
            { 
                if (!usedNodes.Contains(node))
                {
                    node.Dispose();
                }
            }

            AlignHorizontally(0.05f, Children.OfType<RSSIChannelNode>().OrderBy(r => r.Channel.Frequency).ToArray());
        }

        public override void Draw(Drawer id, float parentAlpha)
        {
            ReadRSSI();

            base.Draw(id, parentAlpha);
        }

        class RSSIChannelNode : Node
        {
            public AnimatedNode RSSI { get; private set; }

            public TextNode Value { get; private set; }

            public AnimatedNode LocalMax { get; private set; }

            public ColorNode Detected { get; private set; }

            public Channel Channel { get; private set; }

            public RSSIChannelNode(EventManager eventManager, Channel channel)
            {
                Channel = channel;
                Color color = eventManager.GetChannelColor(channel);
                color.A = 220;

                Node container = new Node();
                container.RelativeBounds = new RectangleF(0.1f, 0, 0.8f, 0.95f);
                AddChild(container);

                RSSI = new AnimatedNode();
                container.AddChild(RSSI);


                ColorNode cn = new ColorNode(color);
                RSSI.AddChild(cn);

                LocalMax = new AnimatedNode();
                LocalMax.RelativeBounds = new RectangleF(0, 0.99f, 1, 0.01f);
                LocalMax.SetAnimationTime(TimeSpan.FromSeconds(2));
                container.AddChild(LocalMax);

                ColorNode localMaxColor = new ColorNode(color);
                LocalMax.AddChild(localMaxColor);


                Detected = new ColorNode(color);
                Detected.Visible = false;
                RSSI.AddChild(Detected);


                TextNode tn = new TextNode(channel.GetBandChannelText() + " " + channel.GetFrequencyText(), Theme.Current.TextMain.XNA);
                tn.RelativeBounds = new RectangleF(0, container.RelativeBounds.Bottom, 1, 1 - container.RelativeBounds.Bottom);
                tn.Alignment = Tools.RectangleAlignment.BottomCenter;
                AddChild(tn);

                Value = new TextNode("", Theme.Current.TextMain.XNA);
                Value.RelativeBounds = new RectangleF(0.05f, 0, 0.9f, 1);
                Value.Alignment = RectangleAlignment.BottomCenter;
                Value.Style.Border = true;
                RSSI.AddChild(Value);
            }

            public void ClearRSSI()
            {
                Value.Text = "";
                RSSI.RelativeBounds = new RectangleF(0, 0, 1, 0);
                RectangleF localBounds = LocalMax.RelativeBounds;
                localBounds.Y = 1 - localBounds.Height;
                LocalMax.RelativeBounds = localBounds;
                LocalMax.Snap();
            }

            public void SetRSSI(RSSI rssi)
            {
                float frssi = rssi.CurrentRSSI;

                frssi = Math.Min(rssi.ScaleMax, frssi);
                frssi = Math.Max(rssi.ScaleMin, frssi);

                float height = (frssi - rssi.ScaleMin) / rssi.ScaleMax;
                float invHeight = 1 - height;

                float lastY = RSSI.RelativeBounds.Y;

                RSSI.RelativeBounds = new RectangleF(0, invHeight, 1, height);

                RectangleF localBounds = LocalMax.RelativeBounds;

                localBounds.Y = invHeight;
                if (invHeight < lastY)
                {
                    LocalMax.Snap();
                }
                LocalMax.RelativeBounds = localBounds;

                Value.Text = "RSSI " + string.Format("{0,5:####}", rssi.CurrentRSSI);

                Detected.Visible = rssi.Detected;
                RequestLayout();
            }
        }
    }
}
