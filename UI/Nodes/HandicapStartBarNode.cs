using Composition;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Tools;

namespace UI.Nodes
{
    public class HandicapStartBarNode : ColorNode, IUpdateableNode
    {
        private readonly EventManager eventManager;

        private readonly TextNode label;
        private readonly ColorNode track;
        private readonly ColorNode cursor;
        private readonly TextNode cursorLabel;
        private readonly Node chipContainer;

        private Race trackedRace;
        private TimeSpan windowDuration;
        private DateTime hideAt;
        private bool active;

        private const float TrailingGraceSeconds = 2.0f;

        public HandicapStartBarNode(EventManager eventManager)
            : base(new Color(0, 0, 0, 170))
        {
            this.eventManager = eventManager;

            label = new TextNode("Handicap start", Color.White);
            label.Alignment = RectangleAlignment.CenterLeft;
            label.RelativeBounds = new RectangleF(0.005f, 0.0f, 0.18f, 1.0f);
            label.Style.Border = true;
            AddChild(label);

            track = new ColorNode(new Color(255, 255, 255, 60));
            track.KeepAspectRatio = false;
            track.RelativeBounds = new RectangleF(0.2f, 0.45f, 0.78f, 0.1f);
            AddChild(track);

            chipContainer = new Node();
            chipContainer.RelativeBounds = new RectangleF(0.2f, 0.0f, 0.78f, 1.0f);
            AddChild(chipContainer);

            cursor = new ColorNode(new Color(255, 220, 60));
            cursor.KeepAspectRatio = false;
            cursor.RelativeBounds = new RectangleF(0.2f, 0.05f, 0.003f, 0.9f);
            cursor.Visible = false;
            AddChild(cursor);

            cursorLabel = new TextNode("", new Color(255, 220, 60));
            cursorLabel.Alignment = RectangleAlignment.CenterLeft;
            cursorLabel.Style.Border = true;
            cursorLabel.Visible = false;
            AddChild(cursorLabel);

            Visible = false;

            eventManager.RaceManager.OnRaceChanged += OnRaceChanged;
            eventManager.RaceManager.OnRacePreStart += OnRacePreStart;
            eventManager.RaceManager.OnRaceStart += OnRaceStart;
            eventManager.RaceManager.OnRaceEnd += OnRaceEnded;
            eventManager.RaceManager.OnRaceClear += OnRaceCleared;
            eventManager.RaceManager.OnRaceReset += OnRaceCleared;
        }

        public override void Dispose()
        {
            eventManager.RaceManager.OnRaceChanged -= OnRaceChanged;
            eventManager.RaceManager.OnRacePreStart -= OnRacePreStart;
            eventManager.RaceManager.OnRaceStart -= OnRaceStart;
            eventManager.RaceManager.OnRaceEnd -= OnRaceEnded;
            eventManager.RaceManager.OnRaceClear -= OnRaceCleared;
            eventManager.RaceManager.OnRaceReset -= OnRaceCleared;
            base.Dispose();
        }

        private void OnRaceChanged(Race race)
        {
            Rebuild(race);
        }

        private void OnRacePreStart(Race race)
        {
            Rebuild(race);
        }

        private void OnRaceStart(Race race)
        {
            if (race == null || trackedRace != race) Rebuild(race);
            if (!active) return;
            hideAt = race.Start + windowDuration + TimeSpan.FromSeconds(TrailingGraceSeconds);
            cursor.Visible = true;
            cursorLabel.Visible = true;
        }

        private void OnRaceEnded(Race race)
        {
            Hide();
        }

        private void OnRaceCleared(Race race)
        {
            Hide();
        }

        private void Hide()
        {
            active = false;
            trackedRace = null;
            Visible = false;
            cursor.Visible = false;
            cursorLabel.Visible = false;
        }

        private void Rebuild(Race race)
        {
            chipContainer.ClearDisposeChildren();

            if (race == null || race.Round == null || !race.Round.Handicapped || race.Ended)
            {
                Hide();
                return;
            }

            TimeSpan maxOffset = TimeSpan.Zero;
            foreach (RacePilotChannel pc in race.PilotChannelsSafe)
            {
                if (pc.HandicapOffset > maxOffset) maxOffset = pc.HandicapOffset;
            }

            if (maxOffset <= TimeSpan.Zero)
            {
                Hide();
                return;
            }

            trackedRace = race;
            active = true;

            if (maxOffset < TimeSpan.FromSeconds(0.5)) maxOffset = TimeSpan.FromSeconds(0.5);
            windowDuration = maxOffset;

            label.Text = "Handicap start (" + maxOffset.TotalSeconds.ToString("0.0") + "s)";

            foreach (RacePilotChannel pc in race.PilotChannelsSafe)
            {
                if (pc.Pilot == null || pc.Channel == null) continue;

                TimeSpan offset = pc.HandicapOffset;

                float t = (float)(offset.TotalSeconds / maxOffset.TotalSeconds);
                if (t < 0f) t = 0f;
                if (t > 1f) t = 1f;

                Color channelColor = eventManager.GetRaceChannelColor(race, pc.Channel);

                ColorNode chip = new ColorNode(channelColor);
                chip.KeepAspectRatio = false;
                float chipWidth = 0.04f;
                float chipX = t * (1.0f - chipWidth);
                chip.RelativeBounds = new RectangleF(chipX, 0.15f, chipWidth, 0.55f);
                chipContainer.AddChild(chip);

                TextNode chipLabel = new TextNode(pc.Pilot.Name + "  +" + offset.TotalSeconds.ToString("0.0") + "s", Color.White);
                chipLabel.Alignment = RectangleAlignment.TopLeft;
                chipLabel.Style.Border = true;
                chipLabel.RelativeBounds = new RectangleF(chipX, 0.72f, 0.25f, 0.28f);
                chipContainer.AddChild(chipLabel);
            }

            Visible = true;

            if (race.Started && !race.Ended)
            {
                hideAt = race.Start + windowDuration + TimeSpan.FromSeconds(TrailingGraceSeconds);
                cursor.Visible = true;
                cursorLabel.Visible = true;
            }
            else
            {
                cursor.Visible = false;
                cursorLabel.Visible = false;
            }
        }

        public void Update(GameTime gameTime)
        {
            if (!active || trackedRace == null) return;

            if (!trackedRace.Started)
            {
                cursor.Visible = false;
                cursorLabel.Visible = false;
                return;
            }

            if (DateTime.Now >= hideAt)
            {
                Hide();
                return;
            }

            TimeSpan elapsed = DateTime.Now - trackedRace.Start;
            double secs = elapsed.TotalSeconds;
            double total = windowDuration.TotalSeconds;
            if (total <= 0) return;

            float t = (float)(secs / total);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            float trackLeft = 0.2f;
            float trackWidth = 0.78f;
            float xRel = trackLeft + t * trackWidth;
            cursor.RelativeBounds = new RectangleF(xRel - 0.0015f, 0.05f, 0.003f, 0.9f);

            cursorLabel.Text = secs.ToString("0.0") + "s";
            float labelWidth = 0.06f;
            float labelX = xRel + 0.005f;
            if (labelX + labelWidth > 1f) labelX = xRel - labelWidth - 0.005f;
            cursorLabel.RelativeBounds = new RectangleF(labelX, 0.05f, labelWidth, 0.4f);

            RequestLayout();
        }
    }
}
