
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
        let pilotRecords = await this.eventManager.GetLapRecords();

        pilotRecords.sort((a, b) => { return this.eventManager.TotalTime(a.laps) - this.eventManager.TotalTime(b.laps) });

        const lapCount = 4;

        let output = "<h2>Lap Records</h2>";
        output += "<div class=\"columns\">";
        output += "<div class=\"row\" >";
        output += "<div class=\"position\">Position</div>";
        output += "<div class=\"pilots\">Pilots</div>";
        output += "<div class=\"holeshot\">Holeshot</div>";
        output += "<div class=\"lap\">1 Lap</div>";
        output += "<div class=\"laps\">" + lapCount + " Laps</div>";
        output += "<div class=\"racetime\">Race Time</div>";
        output += "</div>";

        let i = 1;
        for (const pilotRecord of pilotRecords)
        {
            output += "<div class=\"row\">";
            output += "<div class=\"position\">" + this.ToStringPosition(i) + "</div>";
            output += "<div class=\"pilots\">" + pilotRecord.pilot.Name + "</div>";
            output += "<div class=\"holeshot\">" + this.LapsToTime(pilotRecord.holeshot) + "</div>";
            output += "<div class=\"lap\">" + this.LapsToTime(pilotRecord.lap) + "</div>";
            output += "<div class=\"laps\">" + this.LapsToTime(pilotRecord.laps) + "</div>";
            output += "<div class=\"racetime\">" + this.LapsToTime(pilotRecord.race) + "</div>";

            output += "</div>";
            i++;
        }
        output += "</div>";

        this.content.innerHTML = output;
    }

    async ShowLapCounts()
    {
        let pilotRecords = await this.eventManager.GetLapCounts();

        pilotRecords.sort((a, b) => { return b.total - a.total });

        let rounds = await this.eventManager.GetRounds();

        let output = "<h2>Lap Counts</h2>";
        output += "<div class=\"columns\">";
        output += "<div class=\"row\" >";
        output += "<div class=\"pilots\">Pilots</div>";

        for (const round of rounds)
        {
            let roundName = round.EventType[0] + round.RoundNumber;
            output += "<div class=\"r\">" + roundName + "</div>";
        }

        output += "<div class=\"total\">Total</div>";
        output += "</div>";

        let i = 1;
        for (const pilotRecord of pilotRecords)
        {
            output += "<div class=\"row\">";
            output += "<div class=\"pilots\">" + pilotRecord.pilot.Name + "</div>";

            for (const round of rounds)
            {
                let roundName = round.EventType[0] + round.RoundNumber;
                let value = pilotRecord[roundName];
                if (value == null)
                    value = " ";

                output += "<div class=\"r\">" + value + "</div>";
            }
            output += "<div class=\"total\">" + pilotRecord.total +  "</div>";

            output += "</div>";
            i++;
        }
        output += "</div>";

        this.content.innerHTML = output;
    }

    LapsToTime(laps)
    {
        if (laps == null || laps.length == 0)
            return "";
        let time = this.eventManager.TotalTime(laps);

        let value = Math.round(time * 100) / 100
        return value.toFixed(2);
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

