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
        private static RaceStringFormatter inst;

        public static RaceStringFormatter Instance
        {
            get
            {
                if (inst == null)
                {
                    inst = new RaceStringFormatter();
                }

                return inst;
            }
        }


        public string Practice { get; set; }
        public string TimeTrial { get; set; }
        public string Race { get; set; }
        public string Freestyle { get; set; }
        public string Endurance { get; set; }
        public string CasualPractice { get; set; }
        public string PointsGame { get; set; }

        public RaceStringFormatter()
        {
            Practice = "Practice";
            TimeTrial = "Time Trial";
            Race = "Race";
            Freestyle = "Freestyle";
            Endurance = "Endurance";
            PointsGame = "Game";
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
                case RaceLib.EventTypes.PointsGame: return PointsGame;
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
