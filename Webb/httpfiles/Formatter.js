
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

        let pilotChannels = [];
        for (const pilotChannel of race.PilotChannels)
        {
            pilotChannel.Channel = await eventManager.GetChannel(pilotChannel.Channel);
            pilotChannel.Pilot = await eventManager.GetPilot(pilotChannel.Pilot);
            pilotChannel.Result = await eventManager.GetPilotResult(race.ID, pilotChannel.Pilot.ID);

            if (pilotChannel.Channel != null && pilotChannel.Pilot != null)
            {
                pilotChannels.push(pilotChannel);
            }
        }

        pilotChannels.sort((a, b) => { return a.Channel.Frequency - b.Channel.Frequency });

        for (const pilotChannel of pilotChannels)
        {
            output += "<tr>";
            output += "<td class=\"race_pilot\">" + pilotChannel.Pilot.Name + "</td>";
            output += "<td class=\"race_channel\">" + this.ChannelToString(pilotChannel.Channel) + "</td>";
            output += "<td class=\"race_channel_color\" style=\"background-color: " + pilotChannel.Channel.Color + "\"></td>";
            output += "<td class=\"race_result\">" + this.ResultToString(pilotChannel.Result) + "</td>";
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

        let rounds = await eventManager.GetRounds();

        rounds.sort((a, b) => { return a.RoundNumber - b.RoundNumber });

        for (const round of rounds)
        {
            if (round.Valid)
            {
                output += "<div id=\"round" + round.RoundNumber + "\" class=\"round\">";
                output += "<h3>" + round.EventType + " Round " + round.RoundNumber + "</h3>";

                let races = await this.eventManager.GetRoundRaces(round.ID);
                races = races.sort((a, b) => { return a.RaceNumber - b.RaceNumber });

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

    async ShowLapRecords()
    {
        let output = "<h2>Lap Records</h2>";


        this.content.innerHTML = output;
    }

    ToStringPosition(position)
    {
        let post = "th";

        position = position.toString();

        if (position.length == 1 || position[position.length - 2] != '1')
        {
            let lastChar = position[position.length - 1]
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

