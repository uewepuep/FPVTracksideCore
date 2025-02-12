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
    public class PilotChannelNode : Node, IPilot
    {
        public Pilot Pilot { get; private set; }
        public Channel Channel { get; private set; }

        public event PilotClickDelegate OnPilotClick;
        public event PilotClickDelegate OnPilotChannelClick;

        public TextButtonNode ChannelNode { get; private set; }
        public TextButtonNode PilotNameNode { get; private set; }
        public TextNode ChannelChangeNode { get; private set; }

        private ColorNode channelColor;

        private EventManager eventManager;

        private ImageNode profileIcon;

        public PilotChannelNode(EventManager ev, ToolTexture background, Color hover, Color text, ToolTexture channelTexture)
        {
            ColorNode backgroundNode = new ColorNode(background);
            AddChild(backgroundNode);

            eventManager = ev;

            profileIcon = new ImageNode("img/profileicon.png");
            profileIcon.Alpha = 0.3f;
            profileIcon.Visible = false;
            AddChild(profileIcon);

            PilotNameNode = new TextButtonNode("", Color.Transparent, hover, text);
            PilotNameNode.RelativeBounds = new RectangleF(0, 0, 0.83f, 1);
            PilotNameNode.TextNode.Alignment = RectangleAlignment.Center;

            PilotNameNode.OnClick += (mie) =>
            {
                OnPilotClick?.Invoke(mie, Pilot);
            };
            AddChild(PilotNameNode);

            ChannelNode = new TextButtonNode("", Color.Transparent, hover, text);
            ChannelNode.RelativeBounds = new RectangleF(0.7f, 0, 0.3f, 1);
            ChannelNode.OnClick += (mie) =>
            {
                OnPilotChannelClick?.Invoke(mie, Pilot);
            };
            ChannelNode.TextNode.Alignment = RectangleAlignment.BottomRight;
            AddChild(ChannelNode);

            channelColor = new ColorNode(channelTexture);
            channelColor.RelativeBounds = new RectangleF(0.65f, 0, 0.35f, 1);
            channelColor.Scale(0.6f);
            ChannelNode.AddChild(channelColor);

            ChannelChangeNode = new TextNode("Δ", text);
            ChannelChangeNode.Visible = false;
            ChannelChangeNode.Style.Bold = true;
            ChannelChangeNode.Style.Border = true;
            channelColor.AddChild(ChannelChangeNode);

            RectangleF channelTextBounds = ChannelNode.TextNode.RelativeBounds;
            channelTextBounds.Width = channelColor.RelativeBounds.X - channelTextBounds.X;
            channelTextBounds.Width -= 0.075f;
            ChannelNode.TextNode.RelativeBounds = channelTextBounds;
        }

        public void SetPilotChannel(PilotChannel pc)
        {
            if (pc != null)
            {
                SetPilotChannel(pc.Pilot, pc.Channel, null);
            }
            else
            {
                SetPilotChannel(null, null, null);
            }
        }

        public void UpdateProfileIcon()
        {
            if (Pilot == null)
            {
                profileIcon.Visible = false;
                return;
            }

            profileIcon.Visible = System.IO.File.Exists(Pilot.PhotoPath);

            if (profileIcon.Visible)
            {
                profileIcon.RelativeBounds = new RectangleF(0.02f, 0.1f, 0.05f, 0.8f);
                PilotNameNode.RelativeBounds = new RectangleF(profileIcon.RelativeBounds.Right, 0, 0.83f - profileIcon.RelativeBounds.Right, 1);
            }
            else
            {
                PilotNameNode.RelativeBounds = new RectangleF(0, 0, 0.83f, 1);
            }
        }

        public void SetPilotChannel(Pilot p, Channel c, IEnumerable<Channel> shared)
        {
            Pilot = p;

            if (Pilot == null)
            {
                PilotNameNode.Text = "";
            }
            else
            {
                PilotNameNode.Text = Pilot.Name;
            }


            Channel = c;

            if (Channel == null)
            {
                ChannelNode.Text = "";
                channelColor.Tint = Theme.Current.PanelAlt.XNA;
            }
            else
            {
                ChannelNode.Text = Channel.UIDisplayName;

                if (Pilot == null && shared != null && shared.Any())
                {
                    ChannelNode.Text += "/" + string.Join("/",shared.Select(s => s.UIDisplayName));
                }

                channelColor.Tint = eventManager.GetChannelColor(Channel);
            }
        }
    }

    public interface IPilot
    {
        Pilot Pilot { get; }
        Channel Channel { get; }
    }
}
