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
    public class EventTypeSummaryNode : TextNode
    {
        public EventManager EventManager { get; private set; }

        private bool NeedsUpdateText;

        public EventTypeSummaryNode(EventManager eventManager, Color text)
            : base("", text)
        {
            EventManager = eventManager;
            Alignment = RectangleAlignment.CenterLeft;

            EventManager.OnEventChange += OnNeedsUpdateText;
            EventManager.RaceManager.OnRaceClear += OnNeedsUpdateText;
            EventManager.RaceManager.OnRaceChanged += OnNeedsUpdateText;
            EventManager.RaceManager.OnPilotAdded += OnNeedsUpdateText;

            UpdateText();
        }

        public override void Dispose()
        {
            EventManager.OnEventChange -= OnNeedsUpdateText;
            EventManager.RaceManager.OnRaceClear -= OnNeedsUpdateText;
            EventManager.RaceManager.OnRaceChanged -= OnNeedsUpdateText;
            EventManager.RaceManager.OnPilotAdded -= OnNeedsUpdateText;

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

        public override bool OnMouseInput(MouseInputEvent mouseInputEvent)
        {
            if (mouseInputEvent.ButtonState == ButtonStates.Released && !EventManager.Event.Locked)
            {
                MouseMenu mouseMenu = new MouseMenu(this);

                foreach (EventTypes type in Event.GetEventTypes())
                {
                    EventTypes thisType = type;
                 
                    if (thisType == EventTypes.Game)
                    {
                        try
                        {

                            GameType[] gameTypes = GameType.Read(EventManager.Profile);
                            if (gameTypes.Any())
                            {
                                var subMenu = mouseMenu.AddSubmenu("Game");
                                foreach (GameType gameType in gameTypes)
                                {
                                    var gt = gameType;
                                    subMenu.AddItem(gameType.Name, () => { SelectGameType(gt); });
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.UI.LogException(this, e);
                        }
                    }
                    else
                    {
                        string typeString = RaceStringFormatter.Instance.GetEventTypeText(thisType);
                        mouseMenu.AddItem(typeString, () =>
                        {
                            EventManager.SetEventType(thisType);
                        });
                    }
                }

                mouseMenu.Show(Bounds.X, Bounds.Bottom);
            }

            return base.OnMouseInput(mouseInputEvent);
        }

        private void SelectGameType(GameType gameType)
        {
            EventManager.GameManager.SetGameType(gameType);
        }

        public override void Draw(Drawer drawer, float parentAlpha)
        {
            if (NeedsUpdateText)
                UpdateText();

            base.Draw(drawer, parentAlpha);
        }

        public void UpdateText()
        {
            Race currentRace = EventManager.RaceManager.CurrentRace;
            if (currentRace != null)
            {
                Text = RaceToString(currentRace);
            }
            else
            {
                if (EventManager.RaceManager.RaceType == EventTypes.Game && EventManager.GameManager.GameType != null)
                {
                    Text = EventManager.GameManager.GameType.ToString();
                }
                else
                {
                    Text = RaceStringFormatter.Instance.GetEventTypeText(EventManager.RaceManager.RaceType);
                }
            }
        }

        private string RaceToString(Race race)
        {
            NeedsUpdateText = false;
            string eventType = RaceStringFormatter.Instance.GetEventTypeText(race.Type);
            return "Round " + race.RoundNumber + "   " + eventType + " " + race.RaceNumber;
        }

    }
}
