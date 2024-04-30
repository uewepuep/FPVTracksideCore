using Composition;
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
using UI.Video;

namespace UI.Nodes
{
    public class LapSummaryNode : TextNode
    {
        public EventManager EventManager { get; private set; }
        public ReplayNode ReplayNode { get; private set; }

        private bool NeedsUpdateText;

        public LapSummaryNode(EventManager eventManager, ReplayNode replayNode, Color text)
            : base("", text)
        {
            ReplayNode = replayNode;

            EventManager = eventManager;
            EventManager.RaceManager.OnLapDetected += OnNeedsUpdateText;
            EventManager.RaceManager.OnLapDisqualified += OnNeedsUpdateText;
            EventManager.RaceManager.OnLapSplit += OnNeedsUpdateText;
            EventManager.RaceManager.OnRaceClear += OnNeedsUpdateText;
            EventManager.OnEventChange += OnNeedsUpdateText;

            Alignment = RectangleAlignment.CenterRight;

            UpdateText();
        }


        public override void Dispose()
        {
            EventManager.RaceManager.OnLapDetected -= OnNeedsUpdateText;
            EventManager.RaceManager.OnLapDisqualified -= OnNeedsUpdateText;
            EventManager.RaceManager.OnLapSplit -= OnNeedsUpdateText;
            EventManager.RaceManager.OnRaceClear -= OnNeedsUpdateText;
            EventManager.OnEventChange -= OnNeedsUpdateText;

            base.Dispose();
        }


        private void OnNeedsUpdateText(object obj)
        {
            OnNeedsUpdateText();
        }

        private void OnNeedsUpdateText()
        {
            NeedsUpdateText = true;
        }

        private void UpdateText()
        {
            if (EventManager.Event == null)
                return;

            if (ReplayNode != null && ReplayNode.Active)
            {
                Text = "";
                return;
            }

            switch (EventManager.RaceManager.RaceType)
            {
                case EventTypes.Freestyle:
                case EventTypes.Practice:
                case EventTypes.AggregateLaps:
                    Text = "";
                    break;

                case EventTypes.Race:
                    int leadLapPlus = EventManager.RaceManager.LeadLap + 1;
                    int maxLap = EventManager.Event.Laps;
                    Race current = EventManager.RaceManager.CurrentRace;
                    if (current != null)
                    {
                        maxLap = current.TargetLaps;
                    }

                    int lap = Math.Min(leadLapPlus, maxLap);

                    if (lap >= maxLap)
                        Text = "Lap " + lap + " / " + maxLap;
                    else
                        Text = "Lap " + leadLapPlus + " / " + maxLap;
                    break;

                case EventTypes.TimeTrial:
                case EventTypes.CasualPractice:
                    if (EventManager.Event.Laps == 1)
                    {
                        Text = "Best Lap";
                    }
                    else
                    {
                        Text = "Best " + EventManager.Event.Laps + " laps";
                    }
                    break;
            }
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.ButtonState == ButtonStates.Released && !EventManager.Event.Locked)
            {
                MouseMenu mouseMenu = new MouseMenu(this);

                for (int laps = 1; laps <= 6; laps ++)
                {
                    int thisLaps = laps;
                    mouseMenu.AddItem("Set Laps " + laps, () =>
                    {
                        EventManager.SetEventLaps(thisLaps);
                    });
                }

                mouseMenu.Show(Bounds.X, Bounds.Bottom);
            }

            return base.OnMouseInput(mouseInputEvent);
        }

        public override void Draw(Drawer drawer, float parentAlpha)
        {
            if (NeedsUpdateText)
                UpdateText();

            if (EventManager.RaceManager.EventType != EventTypes.Freestyle)
                base.Draw(drawer, parentAlpha);
        }

    }
}
