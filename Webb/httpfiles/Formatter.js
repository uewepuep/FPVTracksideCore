
class Formatter
{
    constructor(eventManager, content)
    {
        this.eventManager = eventManager;
        this.content = content;
    }

    async RaceTable(race, round)
    {
        let output = "<div id=\"" + race.RaceNumber + "\" class=\"race_status\">";

        let raceName = round.EventType + " " + round.RoundNumber + "-" + race.RaceNumber;

        output += "<h4>" + raceName + "</h4>";

        output += "<table class=\"race_table\">";
        for (const pilotChannel of race.PilotChannels)
        {
            let channel = await eventManager.GetChannel(pilotChannel.Channel);
            let pilotName = "";
            let result = null;

            let pilot = await eventManager.GetPilot(pilotChannel.Pilot);
            if (pilot != null)
            {
                pilotName = pilot.Name;
                result = await eventManager.GetPilotResult(race.ID, pilot.ID);
            }

            output += "<tr>";
            output += "<td class=\"race_pilot\">" + pilotName + "</td>";
            output += "<td class=\"race_channel\">" + this.ChannelToString(channel) + "</td>";
            //output += "<td class=\"race_channel_color\" style=\"background-color: " + channel.GetStyleColor() + "\"></td>";
            output += "<td class=\"race_result\">" + this.ResultToString(result) + "</td>";
            output += "</tr>";
        }
        output += '</table>';

        output += "</div>";
        return output;
    }

    ResultToString(result)
    {
        if (result == null)
            return "";

        if (result.Valid)
        {
            if (result.DNF)
            {
                return "DNF";
            }

            return this.ToStringPosition(result.Position);
        }
    }

    ChannelToString(channel)
    {
        return channel.Band.substring(0, 1) + channel.Number;
    }

    async ShowRounds()
    {
        let output = "<h2>Rounds</h2>";

        const rounds = await eventManager.GetRounds();
        for (const round of rounds)
        {
            if (round.Valid)
            {
                output += "<div id=\"round" + round.RoundNumber + "\" class=\"round\">";
                output += "<h3>" + round.EventType + " Round " + round.RoundNumber + "</h3>";

                let races = await this.eventManager.GetRoundRaces(round.ID);
                for (const race of races) {
                    if (race.Valid) {
                        output += await this.RaceTable(race, round);
                    }
                }

                output += "</div>";
            }
        }

        this.content.innerHTML = output;
    }

    ToStringPosition(position)
    {
        let post = "th";

        if (position.Length == 1 || position[position.Length - 2] != '1')
        {
            let lastChar = position[position.Length - 1]
            switch (lastChar)
            {
                case '1': post = "st"; break;
                case '2': post = "nd"; break;
                case '3': post = "rd"; break;
            }
        }
        return position + post;
    }
}

