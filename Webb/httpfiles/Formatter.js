
class Formatter
{
    constructor(eventManager, document, window, history, contentName)
    {
        this.eventManager = eventManager;
        this.document = document;
        this.window = window;
        this.history = history;
        this.contentName = contentName;
        this.lastAction = null;

        var formatter = this;
        window.onresize = function(event)
        {
            formatter.ResizeWindow();
        };
        window.onload = function(event)
        {
            formatter.ResizeWindow();
        };

        const self = this;
        window.setInterval(() =>
        {
            self.RepeatLastAction();
        }, 10000);
    }

    GetOptions()
    {
        return ["Event Status", "Rounds", "Lap Records", "Lap Counts", "Points"];
    }

    Show(name)
    {
        switch (name)
        {
            case "Event Status":
                this.ShowEventStatus();
                break;

            case "Rounds":
                this.ShowRounds();
                break;

            case "Lap Records":
                this.ShowLapRecords();
                break;

            case "Lap Counts":
                this.ShowLapCounts();
                break;

            case "Points":
                this.ShowPoints();
                break;
        }

        let url = new URL(this.window.location.href);
        url.pathname = name;

        history.pushState({}, null, url.toString());
    }


    RepeatLastAction()
    {
        if (this.lastAction != null)
        {
            this.lastAction();
        }
    }

    async RaceTable(race, round, showName = true)
    {
        let output = "<div id=\"" + race.RaceNumber + "\" class=\"race_status\">";

        let raceName = round.EventType + " " + round.RoundNumber + "-" + race.RaceNumber;

        if (showName)
        {
            output += "<h4><a href=\"#\" onclick=\"formatter.ShowRace('" + race.ID + "')\">" + raceName + "</a></h4>";
        }

        output += "<table class=\"race_table\">";

        let pilotChannels = [];
        for (const pilotChannel of race.PilotChannels)
        {
            pilotChannel.Channel = await eventManager.GetChannel(pilotChannel.Channel);
            pilotChannel.Pilot = await eventManager.GetPilot(pilotChannel.Pilot);

            if (pilotChannel.Channel != null && pilotChannel.Pilot != null)
            {
                pilotChannel.Result = await eventManager.GetPilotResult(race.ID, pilotChannel.Pilot.ID);
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

    async ShowEventStatus()
    {
        let output = "<h2>Event Status</h2><br>";
        output += "<div class=\"current_status\">";

        const prevcurrentnext = await this.eventManager.GetPrevCurrentNextRace();

        for (let i = 0; i < prevcurrentnext.length; i++)
        {
            const race = prevcurrentnext[i];
            if (race != null)
            {
                output += "<div class=\"round\">";
                let round = await this.eventManager.GetRound(race.Round);
                if (round != null)
                {
                    output += "<h3>";
                    switch (i)
                    {
                        case 0:
                            output += "Previous Race";
                            break;
                        case 1:
                            output += "Current Race";
                            break;
                        case 2:
                            output += "Next Race";
                            break;
                    }
                    output += "</h3>";


                    output += await this.RaceTable(race, round);
                }
                output += "</div>";
            }
        }
        output += "</div><br>";

        output += await this.GetLapRecords();

        this.SetContent(output);

        this.lastAction = this.ShowEventStatus;
    }

    async ShowRounds()
    {
        let output = "<h2>Rounds</h2>";
        output += "<div class=\"rounds\">";

        let rounds = await eventManager.GetRounds();
        for (const round of rounds)
        {
            if (round.Valid)
            {
                output += "<div id=\"round" + round.RoundNumber + "\" class=\"round\">";
                output += "<h3>" + round.EventType + " Round " + round.RoundNumber + "</h3>";

                let races = await this.eventManager.GetRoundRaces(round.ID);
                races = races.sort((a, b) => { return a.RaceNumber - b.RaceNumber });

                for (const race of races)
                {
                    if (race.Valid)
                    {
                        output += await this.RaceTable(race, round);
                    }
                }

                output += "</div>";
            }
        }

        output += "</div>";
        this.SetContent(output);

        this.lastAction = this.ShowRounds;
    }

    async ShowLapRecords()
    {
        let output = await this.GetLapRecords();
        this.SetContent(output);

        this.lastAction = this.ShowLapRecords;
    }

    async GetLapRecords()
    {
        const eventDetails = await this.eventManager.GetEvent();

        const lapCount = eventDetails.Laps;
        const pbLaps = eventDetails.PBLaps;

        let pilotRecords = await this.eventManager.GetLapRecords(pbLaps, lapCount);

        pilotRecords.sort((a, b) => { return this.eventManager.TotalTime(a.laps) - this.eventManager.TotalTime(b.laps) });


        let output = "<h2>Lap Records</h2>";
        output += "<div class=\"columns\">";
        output += "<div class=\"row\" >";
        output += "<div class=\"position\">Position</div>";
        output += "<div class=\"pilots\">Pilots</div>";
        output += "<div class=\"holeshot\">Holeshot</div>";
        output += "<div class=\"lap\">" + pbLaps + " Lap" + this.Plural(pbLaps) + " </div>";
        output += "<div class=\"laps\">" + lapCount + " Lap" + this.Plural(lapCount) + " </div>";
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
        return output;
    }

    async ShowLapCounts()
    {
        let pilotRecords = await this.eventManager.GetLapCounts();

        pilotRecords.sort((a, b) => { return b.total - a.total });

        let rounds = await this.eventManager.GetRounds();

        let output = "<h2>Lap Counts</h2>";

        output += this.FormatRoundsTable(rounds, pilotRecords);

        this.SetContent(output);

        this.lastAction = this.ShowLapCounts;
    }

    async ShowPoints()
    {
        let pilotRecords = await this.eventManager.GetPoints();

        pilotRecords.sort((a, b) => { return b.total - a.total });

        let rounds = await this.eventManager.GetRounds();

        let output = "<h2>Points</h2>";

        output += this.FormatRoundsTable(rounds, pilotRecords);

        this.SetContent(output);
        this.lastAction = this.ShowPoints;
    }

    async ShowRace(raceid)
    {
        const race = await this.eventManager.GetRace(raceid);
        const round = await this.eventManager.GetRound(race.Round);

        if (!race.Valid)
        {
            return;
        }
        let raceName = round.EventType + " " + round.RoundNumber + "-" + race.RaceNumber;

        let output = "<h2>" + raceName + "</h2><br>";

        let start = new Date(race.Start);
        let end = new Date(race.End);

        let time = start.getTime();

        const epoch = 1000000000000; //ms since 70

        output += "<div class=\"details\">";
        output += "Target Laps - " + race.TargetLaps + "<br>";

        if (start.getTime() < epoch)
        {
            output += "Not Started<br>";
        }
        else
        {
            output += "Start -  " + start.toLocaleTimeString() + "<br>";
            
            if (end.getTime() > epoch)
            {
                const length = this.ToStringTime((end.getTime() - start.getTime()) / 1000);
                output += "End - " + end.toLocaleTimeString() + "<br>";
                output += "Length - " + length + "s<br>";
            }
        }

        output += "</div>";

        const pilots = [];
        const colors = [];
        output += "<h3>Pilots</h3>";
        output += "<ul>";
        for (const pilotChannel of race.PilotChannels)
        {
            var pilot = await eventManager.GetPilot(pilotChannel.Pilot);
            var channel = await eventManager.GetChannel(pilotChannel.Channel);

            if (pilot != null && channel != null)
            {
                output += "<li>" + pilot.Name + "</li>";

                pilots[pilot.ID] = pilot;
                colors[pilot.ID] = channel.Color;
            }
        }

        const pilotCount = Object.keys(pilots).length;

        output += "</ul>";

        output += "<h3>Results</h3>";
        output += "<div class=\"race_container\">";

        output += await this.RaceTable(race, round, false);

        output += "</div>";

        output += "<h3>Laps</h3>";
        output += "<div class=\"race_laps\">";

        let maxLapNumber = 99;
        if (round.EventType == "Race")
        {
            maxLapNumber = race.TargetLaps;
        }

        let hasHoleshot = false;
        let grouped = [];
        const allLaps = this.eventManager.GetValidLaps(race);
        for (const lap of allLaps)
        {
            const i = lap.LapNumber;
            if (i > maxLapNumber)
                continue;

            if (lap.LapNumber == 0)
                hasHoleshot = true;

            if (grouped[i] == null)
            {
                grouped[i] = [];
            }
            grouped[i].push(lap);
        }

        let graph = new Graph(this.document, "posgraph");

        for (let lapNumber = 0; lapNumber < grouped.length; lapNumber++)
        {
            let laps = grouped[lapNumber];
            if (laps == null)
                continue;

            // sort by detection time.
            laps.sort((a, b) => 
            { 
                return Date.parse(a.detectionObject.Time) - Date.parse(b.detectionObject.Time)
            });

            output += "<div class=\"race_lap\">";

            if (lapNumber == 0)
            {
                output += "<h3>Holeshot</h3>";
            }
            else
            {
                output += "<h3>Lap " + lapNumber + "</h3>";
            }
    
            output += '<table class=\"race_table\">';
            output += "<tr class=\"heading\">";
    
            output += "<td class=\"lap_pilot\">Pilot</td>";
            output += "<td class=\"lap_time\">Time</td>";
            output += "<td class=\"lap_behind\">Behind</td>";
            output += "<td class=\"lap_position\">Position</td>";
            output += "</tr>";
    
            let position = 1;
            let lastLap = null;

            for (const lap of laps)
            {
                const pilotID = lap.detectionObject.Pilot;
                const pilot = pilots[pilotID];
                const color = colors[pilotID];

                const length = lap.LengthSeconds;
                let behind = 0;

                if (lastLap != null)
                {
                    behind = (Date.parse(lap.EndTime) - Date.parse(lastLap.EndTime)) / 1000;
                }

                if (pilot == null)
                    continue;

                //$behind = FormatTime($lap->GetEnd() - $last_lap->GetEnd());
                output += "<tr>";
                output += "<td class=\"cell_text\">" + pilot.Name +  "</td>";
                output += "<td class=\"cell_numeric\">" + this.ToStringTime(length) +  "</td>";
                output += "<td class=\"cell_numeric\">" + this.ToStringTime(behind) +  "</td>";
                output += "<td class=\"cell_numeric\">" + this.ToStringPosition(position) +  "</td>";
    
                const path = graph.GetPath(pilot.Name, color);
                path.AddPoint(lapNumber, position);
    
                output += "</tr>";
    
                if (lastLap != null && lastLap.LapNumber != lap.LapNumber)
                {
                    output += "<tr>";
                    output += "<td> </td>";
                    output += "</tr>";
                }
    
                position++;
                lastLap = lap;
            }
            output += '</table>';
            output += "</div>";

        }
        output += "</div>";

        output += "<div class=\"graph\">";
        output += "<h2>Position Graph</h2><br>";

        output += "<canvas id=\"posgraph\" width=\"600\" height=\"300\"> </canvas>";
        output += "</div>";

        let lapCount = Math.min(maxLapNumber, grouped.length);
        for (let p = 1; p <= pilotCount; p++)
        {
            graph.AddYLabel(this.ToStringPosition(p), p);
        }

        for (let l = hasHoleshot ? 0 : 1; l <= lapCount; l++)
        {
            if (l == 0)
            {
                graph.AddXLabel("Holeshot", l);
            }
            else
            {
                graph.AddXLabel("Lap " + l, l);
            }
        }

        this.SetContent(output);

        const canvas = document.getElementById("posgraph");
        graph.SetView(hasHoleshot ? -0.5 : 0.5, 0, lapCount + 1.5, pilotCount + 1);
        graph.MakeGraph(canvas);

        this.lastAction = () => { this.ShowRace(raceid); }
    }

    FormatRoundsTable(rounds, pilotRecords)
    {
        let output = "<div class=\"columns\">";
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
            output += "<div class=\"total\">" + pilotRecord.total + "</div>";

            output += "</div>";
            i++;
        }
        output += "</div>";
        return output;
    }

    LapsToTime(laps)
    {
        if (laps == null || laps.length == 0)
            return "";
        const time = this.eventManager.TotalTime(laps);
        return this.ToStringTime(time);
    }

    ToStringTime(time)
    {
        const value = Math.round(time * 100) / 100
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

    ResizeWindow()
    {
        const width = this.document.body.clientWidth;

        var columns = (width / 900);
        columns = Math.min(Math.max(Math.floor(columns), 1), 5);

        var lapcolumns = (width / 500);
        lapcolumns = Math.min(Math.max(Math.floor(lapcolumns), 1), 10);

        var menu_columns = 5;
        if (width < 900)
            menu_columns = 1;

        this.document.documentElement.style.setProperty('--data-columns', columns);
        this.document.documentElement.style.setProperty('--lap-columns', lapcolumns);
        this.document.documentElement.style.setProperty('--menu-columns', menu_columns);
    }

    AppendContent(content)
    {
        const contentElement = document.getElementById(this.contentName);
        if (contentElement == null)
            return;

        if (typeof content === 'string' || content instanceof String)
        {
            contentElement.innerHTML += content;
        }
        else
        {
            contentElement.append(content);
        }

        const timeElement = document.getElementById("time");

        let time = this.eventManager.time;
        if (time != null && timeElement != null)
        {
            const date = new Date(time);
            timeElement.innerHTML = date.toLocaleTimeString();
        }
    }

    SetContent(content)
    {
        const contentElement = document.getElementById(this.contentName);
        if (contentElement == null)
            return;

        contentElement.innerHTML = "";
        this.AppendContent(content);
    }

    Plural(value)
    {
        if (value > 1)
            return "s";
        return "";
    }
}

