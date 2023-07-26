using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RaceLib;
using Tools;
using UI.Video;

namespace UI.Nodes
{
    public class TopBarNode : AnimatedNode
    {
        public EventManager EventManager { get; private set; }

        private TextNode eventName;
        private EventTypeSummaryNode type;

        private TextNode currentTime;
        private LapSummaryNode lapSummaryNode;
        private RaceTimeNode timeNode;
        private RaceTimeRemainingNode remainingNode;

        private int lastMinute;

        private Node lower;
        private Node upper;

        private AnimatedRelativeNode logoContainerNode;
        private ColorNode background;

        public TopBarNode()
        {
        }

        public void Init(EventManager eventManager, ReplayNode replayNode)
        {
            lastMinute = -1;
            EventManager = eventManager;
            AnimationTime = TimeSpan.FromSeconds(ProfileSettings.Instance.ReOrderAnimationSeconds);

            bool border = Theme.Current.TopPanelTextBorder;

            background = new ColorNode(Theme.Current.TopPanel);
            AddChild(background);

            logoContainerNode = new AnimatedRelativeNode();
            logoContainerNode.AnimationTime = AnimationTime;
            logoContainerNode.RelativeBounds = new RectangleF(0, 0, 0.3f, 1f);
            AddChild(logoContainerNode);

            ImageNode logoNode = new ColorNode(Theme.Current.FPVTracksideLogo);
            logoNode.Alignment = RectangleAlignment.CenterLeft;
            logoNode.KeepAspectRatio = true;
            logoContainerNode.AddChild(logoNode);

            upper = new Node();
            upper.RelativeBounds = new RectangleF(logoContainerNode.RelativeBounds.Right, 0, 1 - logoContainerNode.RelativeBounds.Right, 0.5f);
            upper.Scale(0.98f, 0.95f);
            AddChild(upper);

            lower = new Node();
            lower.RelativeBounds = new RectangleF(logoContainerNode.RelativeBounds.Right, 0.50f, 1 - logoContainerNode.RelativeBounds.Right, 0.5f);
            lower.Scale(0.98f, 0.95f);

            AddChild(lower);

            background.RelativeBounds = new RectangleF(0, 0, 1, 1);

            eventName = new TextNode("", Theme.Current.TopPanelText.XNA);
            eventName.Alignment = RectangleAlignment.CenterLeft;
            eventName.RelativeBounds = new RectangleF(0, 0, 0.85f, 1f);
            eventName.Style.Border = border;
            upper.AddChild(eventName);

            currentTime = new TextNode("", Theme.Current.TopPanelText.XNA);
            currentTime.Alignment = RectangleAlignment.CenterRight;
            currentTime.RelativeBounds = new RectangleF(eventName.RelativeBounds.Right, 0, 1 - eventName.RelativeBounds.Right, 1f);
            currentTime.Style.Border = border;
            upper.AddChild(currentTime);

            type = new EventTypeSummaryNode(EventManager, Theme.Current.TopPanelText.XNA);
            type.Style.Border = border;
            lower.AddChild(type);

            UpdateDetails();

            lapSummaryNode = new LapSummaryNode(eventManager, replayNode, Theme.Current.TopPanelText.XNA);
            lapSummaryNode.Alignment = RectangleAlignment.CenterLeft;
            lapSummaryNode.Style.Border = border;
            lower.AddChild(lapSummaryNode);

            timeNode = new RaceTimeNode(eventManager.RaceManager, replayNode, Theme.Current.TopPanelText.XNA);
            timeNode.Alignment = RectangleAlignment.CenterLeft;
            timeNode.Style.Border = border;
            lower.AddChild(timeNode);

            remainingNode = new RaceTimeRemainingNode(eventManager.RaceManager, replayNode, Theme.Current.TopPanelText.XNA);
            remainingNode.Alignment = RectangleAlignment.CenterLeft;
            remainingNode.Style.Border = border;
            lower.AddChild(remainingNode);

            LogoOnBottomLine(false);

            RepositionNodes();

            EventManager.OnEventChange += UpdateDetails;
            EventManager.RaceManager.OnRaceClear += UpdateDetails;
            EventManager.RaceManager.OnRaceChanged += UpdateDetails;
            EventManager.RaceManager.OnRaceReset += UpdateDetails;
        }

        public void DisableTimeNodes()
        {
            remainingNode.Visible = false;
            timeNode.Visible = false;
        }


        public override void Dispose()
        {
            EventManager.OnEventChange -= UpdateDetails;
            EventManager.RaceManager.OnRaceClear -= UpdateDetails;
            EventManager.RaceManager.OnRaceChanged -= UpdateDetails;
            EventManager.RaceManager.OnRaceReset -= UpdateDetails;

            base.Dispose();
        }

        public void RepositionNodes()
        {
            float top = 0.075f;
            float height = 1 - (2 * top);

            eventName.Visible = ProfileSettings.Instance.TopEventName;
            type.Visible = ProfileSettings.Instance.TopEventType;
            lapSummaryNode.Visible = ProfileSettings.Instance.TopLapInfo;
            timeNode.Visible = ProfileSettings.Instance.TopRaceTime;
            remainingNode.Visible = ProfileSettings.Instance.TopRemainingTime;
            currentTime.Visible = ProfileSettings.Instance.TopClock;

            type.RelativeBounds = new RectangleF(0, top, 0.37f, height);
            lapSummaryNode.RelativeBounds = new RectangleF(0, top, 0.18f, height);
            timeNode.RelativeBounds = new RectangleF(0, top, 0.19f, height);
            remainingNode.RelativeBounds = new RectangleF(0, top, 0.26f, height);

            Node[] ordered = new Node[] { type, lapSummaryNode, timeNode, remainingNode };

            float current = 0f;
            foreach (Node node in ordered)
            {
                if (node.Visible)
                {
                    RectangleF bounds = node.RelativeBounds;
                    bounds.X = current;
                    current = bounds.Right;

                    node.RelativeBounds = bounds;
                }
            }
            RequestLayout();
        }


        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            DateTime now = DateTime.Now;

            if (now.Minute != lastMinute)
            {
                currentTime.Text = now.ToString("h:mm tt").ToLower();
                lastMinute = now.Minute;
            }
        }

        public void UpdateDetails(object o)
        {
            UpdateDetails();
        }

        public void UpdateDetails()
        {
            if (EventManager.Event != null)
            {
                eventName.Text = EventManager.Event.Name;
            }
        }

        public void LogoOnBottomLine(bool bottom)
        {
            if (bottom)
            {
                logoContainerNode.RelativeBounds = new RectangleF(0, 0.5f, 0.3f, 0.5f);
            }
            else
            {
                logoContainerNode.RelativeBounds = new RectangleF(0, 0, 0.3f, 1f);
            }
        }

        public void SetAnimationTime(TimeSpan time)
        {
            AnimationTime = time;
            logoContainerNode.AnimationTime = time;
        }
    }
}
