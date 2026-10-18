using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class RaceStringFormatter
    {
        public static RaceStringFormatter Instance { get; private set; }

        public string EventTypePractice { get; set; }
        public string EventTypeTimeTrial { get; set; }
        public string EventTypeRace { get; set; }
        public string EventTypeFreestyle { get; set; }
        public string EventTypeEndurance { get; set; }
        public string EventTypeCasualPractice { get; set; }
        public string EventTypeGame { get; set; }
        public string EventTypeTraining { get; set; }

        public EventManager EventManager { get; set; }

        public string Round { get; set; }

        public RaceStringFormatter(EventManager eventManager)
        {
            Instance = this;

            EventManager = eventManager;

            EventTypePractice = "Practice";
            EventTypeTimeTrial = "Time Trial";
            EventTypeRace = "Race";
            EventTypeFreestyle = "Freestyle";
            EventTypeEndurance = "Endurance";
            EventTypeCasualPractice = "Casual Practice";
            EventTypeGame = "Game";
            Round = "Round";
            EventTypeTraining = "Training";
        }

        public virtual string GetEventTypeText(EventTypes eventType)
        {
            switch (eventType)
            {
                case EventTypes.Practice: return EventTypePractice;
                case EventTypes.TimeTrial: return EventTypeTimeTrial;
                case EventTypes.Race: return EventTypeRace;
                case EventTypes.Freestyle: return EventTypeFreestyle;
                case EventTypes.Endurance: return EventTypeEndurance;
                case EventTypes.CasualPractice: return EventTypeCasualPractice;
                case EventTypes.Training: return EventTypeTraining;
                case EventTypes.Game:
                    if (EventManager.GameManager.GameType == null)
                        return EventTypeGame;
                    return EventManager.GameManager.GameType.Name;
            }
            return "Unknown";
        }

        public string RoundToString(Round round)
        {
            if (!string.IsNullOrEmpty(round.Name))
            {
                return round.Name;
            }
            else
            {
                return GetEventTypeText(round.EventType) + " " + Round +" " + round.RoundNumber;
            }
        }

        public string RoundToStringShort(Round round)
        {
            string eventTypeText = GetEventTypeText(round.EventType);
            if (string.IsNullOrEmpty(eventTypeText))
            {
                return round.RoundNumber.ToString();
            }

            return eventTypeText.Substring(0, 1) + round.RoundNumber;
        }

        public string RaceToStringShort(Race race)
        {
            return RoundToStringShort(race.Round) + "-" + race.RaceNumber;
        }
    }
}
