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

        public bool NeedsUpdate { get; set; }

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
            NeedsUpdate = true;
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

            int leadLapPlus, maxLap;

            string lapText = Translator.Get("Label.Lap", "Lap") + " ";

            switch (EventManager.RaceManager.RaceType)
            {
                case EventTypes.Freestyle:
                case EventTypes.Practice:
                    Text = "";
                    break;
                case EventTypes.AggregateLaps:
                    GetLeadLapPlusMaxLap(out leadLapPlus, out maxLap);
                    Text = lapText + leadLapPlus;
                    break;

                case EventTypes.Race:
                    GetLeadLapPlusMaxLap(out leadLapPlus, out maxLap);
                    int lap = Math.Min(leadLapPlus, maxLap);

                    if (lap >= maxLap)
                        Text = lapText + lap + " / " + maxLap;
                    else
                        Text = lapText + leadLapPlus + " / " + maxLap;
                    break;

                case EventTypes.TimeTrial:
                case EventTypes.CasualPractice:
                    if (EventManager.Event.Laps == 1)
                    {
                        Text = Translator.Get("Label.BestLap", "Best Lap");
                    }
                    else
                    {
                        Text = Translator.Get("Label.Best" + EventManager.Event.Laps + "Laps", "Best " + EventManager.Event.Laps + " laps");
                    }
                    break;
                case EventTypes.Game:

                    if (EventManager.GameManager != null && EventManager.GameManager.GameType != null)
                    {
                        Text = "Target " + EventManager.GameManager.GameType.TargetPoints.ToString() + " pts";
                    }
                    else
                    {
                        Text = "";
                    }
                    break;
            }
        }

        private void GetLeadLapPlusMaxLap(out int leadLap, out int maxLap)
        {
            leadLap = EventManager.RaceManager.LeadLap + 1;
            maxLap = EventManager.Event.Laps;
            Race current = EventManager.RaceManager.CurrentRace;
            if (current != null)
            {
                maxLap = current.TargetLaps;
            }
        }

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.ButtonState == ButtonStates.Released && !EventManager.Event.RulesLocked)
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
            if (NeedsUpdate)
                UpdateText();

            if (EventManager.RaceManager.RaceType != EventTypes.Freestyle)
                base.Draw(drawer, parentAlpha);
        }

    }
}
