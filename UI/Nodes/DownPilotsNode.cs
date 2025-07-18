﻿using Composition.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class DownPilotsNode : AnimatedNode
    {
        public EventManager EventManager { get; private set; } 

        public RaceManager RaceManager { get { return EventManager.RaceManager; } }

        private Node container;

        private bool needUpdatePosition;
        private bool needsLapRefresh;

        public event Action<Pilot> OnClick;

        public IEnumerable<DownChannelNode> DownChannelNodes
        {
            get
            {
                return container.Children.OfType<DownChannelNode>();
            }
        }

        public DownPilotsNode(EventManager eventManager)
        {
            EventManager = eventManager;

            container = new ColorNode(Theme.Current.PilotViewTheme.LapBackground);
            AddChild(container);

            EventManager.OnEventChange += EventChange;
            EventManager.RaceManager.OnLapDetected += LapDetected;
            EventManager.RaceManager.OnLapDisqualified += LapDisqualified;
            EventManager.RaceManager.OnPilotAdded += PilotChannelChanged;
            EventManager.RaceManager.OnPilotRemoved += PilotChannelChanged;
            EventManager.RaceManager.OnRaceEnd += RaceChanged;
            EventManager.RaceManager.OnRaceClear += RaceChanged;
            EventManager.RaceManager.OnRaceChanged += RaceChanged;
            EventManager.RaceManager.OnRaceReset += RaceChanged;
            EventManager.RaceManager.OnRaceResumed += RaceChanged;
            EventManager.RaceManager.OnLapsRecalculated += RaceChanged;
        }

        public override void Dispose()
        {
            EventManager.OnEventChange -= EventChange;
            EventManager.RaceManager.OnLapDetected -= LapDetected;
            EventManager.RaceManager.OnLapDisqualified -= LapDisqualified;
            EventManager.RaceManager.OnPilotAdded -= PilotChannelChanged;
            EventManager.RaceManager.OnPilotRemoved -= PilotChannelChanged;
            EventManager.RaceManager.OnRaceEnd -= RaceChanged;
            EventManager.RaceManager.OnRaceClear -= RaceChanged;
            EventManager.RaceManager.OnRaceChanged -= RaceChanged;
            EventManager.RaceManager.OnRaceReset -= RaceChanged;
            EventManager.RaceManager.OnRaceResumed -= RaceChanged;
            EventManager.RaceManager.OnLapsRecalculated -= RaceChanged;

            base.Dispose();
        }

        public void UpdateDown(IEnumerable<ChannelNodeBase> channelNodeBases)
        {
            bool change = false;

            foreach (ChannelNodeBase channelNodeBase in channelNodeBases)
            {
                Channel channel = channelNodeBase.Channel;
                Pilot pilot = channelNodeBase.Pilot;

                DownChannelNode dcn = DownChannelNodes.FirstOrDefault(d => d.Channel == channel);

                if (channelNodeBase.CrashedOut && pilot != null)
                {
                    if (dcn == null)
                    {
                        dcn = new DownChannelNode(EventManager, channel, pilot);
                        dcn.OnClick += Dcn_OnClick;
                        container.AddChild(dcn);
                        change = true;
                    }
                }
                else
                {
                    if (dcn != null)
                    {
                        dcn.Dispose();
                        change = true;
                    }
                }
            }

            if (change)
            {
                LayoutList();
            }
        }

        private void Dcn_OnClick(Pilot obj)
        {
            OnClick?.Invoke(obj);
        }

        private void LayoutList()
        {
            DownChannelNode[] dcns = DownChannelNodes.ToArray();

            if (dcns.Length == 0)
                return;

            float height = Math.Min(1.0f / dcns.Length, 0.25f);
            float y = 0;
            float halfSpacer = 0.025f;

            foreach (DownChannelNode dcn in DownChannelNodes)
            {
                dcn.RelativeBounds = new RectangleF(0, y + halfSpacer, 1, height - halfSpacer);
                y += height;
            }
            RequestLayout();
        }

        private void RaceChanged(Race race)
        {
            needUpdatePosition = true;
            needsLapRefresh = true;
        }

        private void PilotChannelChanged(PilotChannel pc)
        {
            needUpdatePosition = true;
        }

        private void LapDisqualified(Lap lap)
        {
            needUpdatePosition = true;
        }

        private void LapDetected(Lap lap)
        {
            foreach (DownChannelNode dcn in DownChannelNodes)
            {
                if (dcn.Pilot == lap.Pilot)
                {
                    dcn.LapsNode.AddLap(lap);
                }
            }
        }

        private void EventChange()
        {
            needUpdatePosition = true;
            needsLapRefresh = true;
        }

        public override void Update(GameTime gameTime)
        {
            if (!Visible)
            {
                return;
            }

            if (needUpdatePosition)
            {
                foreach (DownChannelNode dcn in DownChannelNodes)
                {
                    dcn.UpdatePosition();
                }
                needUpdatePosition = false;
            }

            if (needsLapRefresh)
            {
                foreach (DownChannelNode dcn in DownChannelNodes)
                {
                    dcn.LapsNode.RefreshData();
                }
                needsLapRefresh = false;
            }
            base.Update(gameTime);
        }

    }

    public class DownChannelNode : Node
    {
        public LapsNode LapsNode { get; private set; }
        public ChannelPilotNameNode pilotNameNode { get; private set; }

        public EventManager EventManager { get; private set; }

        public Pilot Pilot { get; private set; }

        public Channel Channel { get; private set; }

        private TextNode positionNode;
        private PBContainerNode PBNode;

        public event Action<Pilot> OnClick;

        public DownChannelNode(EventManager eventManager, Channel channel, Pilot pilot)
        {
            EventManager = eventManager;
            Channel = channel;

            Color c = eventManager.GetChannelColor(channel);
            pilotNameNode = new ChannelPilotNameNode(eventManager, channel, c, 1);
            pilotNameNode.RelativeBounds = new RectangleF(0, 0, 0.4f, 0.6f);
            AddChild(pilotNameNode);

            ImageButtonNode restoreVideo = new ImageButtonNode("img/up.png", Color.Transparent, Theme.Current.Hover.XNA);
            restoreVideo.RelativeBounds = new RectangleF(1 - 0.05f, 0, 0.05f, 0.3f);
            restoreVideo.OnClick += RestoreVideo_OnClick;
            AddChild(restoreVideo);

            PBNode = new PBContainerNode(EventManager, Theme.Current.PilotViewTheme.PilotOverlayText.XNA, 1);
            PBNode.RelativeBounds = new RectangleF(pilotNameNode.RelativeBounds.Right + 0.1f, 0, 0.15f, 0.3f);
            AddChild(PBNode);

            LapsNode = new LapsNode(eventManager);
            LapsNode.ChannelColor = c;
            LapsNode.RelativeBounds = new RectangleF(0, pilotNameNode.RelativeBounds.Bottom, 1, 1 - pilotNameNode.RelativeBounds.Bottom);
            AddChild(LapsNode);

            positionNode = new TextNode("", Theme.Current.PilotViewTheme.PositionText.XNA);
            positionNode.Alignment = RectangleAlignment.TopRight;
            positionNode.Style.Bold = true;
            positionNode.Style.Border = true;
            positionNode.RelativeBounds = new RectangleF(0.55f, 0, 0.4f, pilotNameNode.RelativeBounds.Bottom);
            AddChild(positionNode);

            Pilot = pilot;
            pilotNameNode.SetPilot(pilot);
            LapsNode.SetPilot(pilot);
            PBNode.Pilot = pilot;

            AddChild(new BorderNode(c));
        }

        private void RestoreVideo_OnClick(Composition.Input.MouseInputEvent mie)
        {
            OnClick?.Invoke(Pilot);
        }

        public void UpdatePosition()
        {
            Race race = EventManager.RaceManager.CurrentRace;
            if (Pilot != null && race != null && (EventManager.RaceManager.RaceType.HasResult()))
            {
                int position;
                TimeSpan behind;
                Pilot behindWho;

                bool showPosition;

                if (EventManager.RaceManager.RaceType == EventTypes.TimeTrial)
                {
                    showPosition = EventManager.LapRecordManager.GetPosition(Pilot, EventManager.Event.Laps, out position, out behindWho, out behind);
                }
                else
                {
                    showPosition = race.GetPosition(Pilot, out position, out behindWho, out behind);
                }

                if (showPosition)
                {
                    positionNode.Text = position.ToStringPosition();
                }
                else
                {
                    positionNode.Text = "";
                }
            }
        }
    }
}
