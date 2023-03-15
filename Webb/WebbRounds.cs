using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Webb
{
    public class WebbRounds
    {
        public static string Rounds(EventManager eventManager)
        {
            string output = "";
            output += "<h2>Rounds</h2>";
            bool newRow = true;

            foreach (Round round in eventManager.RoundManager.Rounds)
            {
                IEnumerable<Race> races = eventManager.RaceManager.GetRaces(round);

                if (newRow)
                {
                    output += "<div class=\"rounds\">";
                    newRow = false;
                }

                if (races.Count() == 0)
                    continue;

                output += "<div id=\"round" + round.RoundNumber + "\" class=\"round\">";
                output += "<h3>" + round.ToString();

                if (round.RoundType != Round.RoundTypes.Round)
                {
                    output += " - " + round.RoundType;
                }
                output += "</h3>";

                foreach (Race race in races)
                {
                    output += RaceBox(eventManager, race);
                }
                output += "</div>";

                if (newRow)
                {
                    output += "</div>";
                }
            }
            output += "</div>";
            return output;
        }


        public static string RaceBox(EventManager eventManager, Race race)
        {
            string output = "<div id=\"" + race.RaceNumber + "\" class=\"race_status\">";
            output += "<h4>" + race.RaceName + "</h4>";

            output += RaceTable(eventManager, eventManager.Channels, race);

            output += "</div>";
            return output;
        }

        private static string RaceTable(EventManager eventManager, IEnumerable<Channel> eventchannels, Race race)
        {
            string output = "<table class=\"race_table\">";
            foreach (Channel channel in eventchannels)
            {
                string pilotName = "";
                string resultText = "";

                Pilot pilot = race.GetPilot(channel);
                if (pilot != null)
                {
                    pilotName = pilot.Name;
                    resultText = eventManager.ResultManager.GetResultText(race, pilot);
                }

                output += "<tr>";
                output += "<td class=\"race_pilot\">" + pilotName + "</td>";
                output += "<td class=\"race_channel\">" + channel.GetBandChannelText() + "</td>";
                output += "<td class=\"race_channel_color\" style=\"background-color: " + eventManager.GetChannelColor(channel).ToHex() + "\"> </td>";
                output += "<td class=\"race_result\"> " + resultText + "</td>";
                output += "</tr>";
            }
            output += "</table>";
            return output;
        }

        public static string EventStatus(EventManager eventManager, IWebbTable webbTable)
        {
            string output = "";
            output += "<h2>Event Status</h2>";

            Dictionary<string, Race> raceNames = new Dictionary<string, Race>();

            Race prevRace = eventManager.RaceManager.GetPrevRace();
            if (prevRace != null)
                raceNames.Add("Previous Race", prevRace);


            Race currentRace = eventManager.RaceManager.CurrentRace;
            if (currentRace != null)
                raceNames.Add("Current Race", currentRace);


            Race nextRace = eventManager.RaceManager.GetNextRace(true, false);
            if (nextRace != null)
                raceNames.Add("Next Race", nextRace);


            foreach (var kvp in raceNames)
            {
                output += "<div class=\"race_status\">";
                output += "<h3>" + kvp.Key + "</h3>";
                output += "<h4>" + kvp.Value.RaceName + "</h4>";

                output += RaceTable(eventManager, eventManager.Channels, kvp.Value);

                output += "</div>";
            }

            output += "<div>";
            output += HTTPFormat.FormatTable(webbTable);
            output += "</div>";


            return output;
        }
    }
}
