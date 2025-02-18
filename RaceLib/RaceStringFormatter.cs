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

        public string Practice { get; set; }
        public string TimeTrial { get; set; }
        public string Race { get; set; }
        public string Freestyle { get; set; }
        public string Endurance { get; set; }
        public string CasualPractice { get; set; }
        public string Game { get; set; }

        public EventManager EventManager { get; set; }

        public RaceStringFormatter(EventManager eventManager)
        {
            Instance = this;

            EventManager = eventManager;

            Practice = "Practice";
            TimeTrial = "Time Trial";
            Race = "Race";
            Freestyle = "Freestyle";
            Endurance = "Endurance";
            Game = "Game";
        }

        public string GetEventTypeText(EventTypes eventType)
        {
            switch (eventType)
            {
                case RaceLib.EventTypes.Practice: return Practice;
                case RaceLib.EventTypes.TimeTrial: return TimeTrial;
                case RaceLib.EventTypes.Race: return Race;
                case RaceLib.EventTypes.Freestyle: return Freestyle;
                case RaceLib.EventTypes.Endurance: return Endurance;
                case RaceLib.EventTypes.CasualPractice: return CasualPractice;
                case RaceLib.EventTypes.Game:
                    if (EventManager.GameManager.GameType == null)
                        return Game;
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
                return GetEventTypeText(round.EventType) + " Round " + round.RoundNumber;
            }
        }

        public string RoundToStringShort(Round round)
        {
            return GetEventTypeText(round.EventType).Substring(0, 1) + round.RoundNumber;
        }
    }
}
