window.onresize = function(event)
{
    ResizeWindow();
};
window.onload = function(event)
{
    ResizeWindow();
};

function ResizeWindow()
{
    const width = this.document.body.clientWidth;

    var columns = (width / 900);
    columns = Math.min(Math.max(Math.floor(columns), 1), 5);

    var lapcolumns = (width / 500);
    lapcolumns = Math.min(Math.max(Math.floor(lapcolumns), 1), 10);

    var menu_columns = width / 175.0;
    menu_columns = Math.min(Math.max(Math.floor(menu_columns), 1), 6);

    
    var graphWidth = 800;
    if (width < graphWidth)
    {
        graphWidth = width;
    }

    this.document.documentElement.style.setProperty('--graph-width', graphWidth);
    this.document.documentElement.style.setProperty('--data-columns', columns);
    this.document.documentElement.style.setProperty('--lap-columns', lapcolumns);
    this.document.documentElement.style.setProperty('--menu-columns', menu_columns);
}

class Formatter
{
    constructor(root, eventManager, document, window, history, contentName, tooOld)
    {
        this.root = root;
        this.eventManager = eventManager;
        this.document = document;
        this.window = window;
        this.history = history;
        this.contentName = contentName;
        this.lastAction = null;

        var formatter = this;

        const self = this;
        window.setInterval(() =>
        {
            self.RepeatLastAction();
        }, tooOld);
    }

    async GetOptions()
    {
        let options = [];

        const eventDetails = await this.eventManager.GetEvent();
        const diff = Date.now() - Date.parse(eventDetails.LastOpened);
        if (diff < 48 * 3600 * 1000)
        {
            options.push("Event Status");
        }

        options.push("Rounds");
        options.push("Lap Records");
        options.push("Lap Counts");
        options.push("Points");

        return options;
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

        output += "<table class=\"race_table\">";
        for (const pilotChannel of pilotChannels)
        {
            output += "<tr>";
            output += "<td class=\"race_pilot\">" + this.ToPilotNameLink(pilotChannel.Pilot) +"</td>";
            output += "<td class=\"race_channel\">" + this.ChannelToString(pilotChannel.Channel) + "</td>";
            output += "<td class=\"race_channel_color\" style=\"background-color: " + pilotChannel.Channel.Color + "\"></td>";
            output += "<td class=\"race_result\">" + this.ResultToString(pilotChannel.Result) + "</td>";
            output += "</tr>";
        }
        output += '</table>';

        output += "</div>";
        return output;
    }

    FormatRaceSummary(raceSummary)
    {
        let output = "<div id=\"" + raceSummary.RaceNumber + "\" class=\"race_status\">";

        let raceName = raceSummary.EventType + " " + raceSummary.RoundNumber + "-" + raceSummary.RaceNumber;

        output += "<h4><a href=\"#\" onclick=\"formatter.ShowRace('" + raceSummary.RaceID + "')\">" + raceName + "</a></h4>";
        output += "<div class=\"columns\">";
        output += "<div class=\"row\" >";
        output += "<div class=\"position\">Position</div>";
        output += "<div class=\"pilots\">Pilot</div>";
        output += "<div class=\"channel\">Channel</div>";
        output += "<div class=\"channel_color\"> </div>";

        let maxLaps = 0;
        for (const pilotSummary of raceSummary.PilotSummaries)
        {
            let count = 0;
            for (const lap in pilotSummary)
            {
                if (lap.startsWith("Lap"))
                {
                    count++;
                }
            }

            maxLaps = Math.max(maxLaps, count);
        }

        for (let i = 1; i <= maxLaps; i++)
        {
            output += "<div class=\"lap\">Lap " + i + "</div>";
        }

        output += "<div class=\"lap\">BestLap</div>";
        output += "<div class=\"lap\">PB</div>";
        output += "<div class=\"lap\">Best " + raceSummary.TargetLaps +"</div>";
        output += '</div>';

        for (const pilotSummary of raceSummary.PilotSummaries)
        {
            output += "<div class=\"row\" >";

            output += "<div class=\"position\">" + this.ToStringPosition(pilotSummary.Position) + "</div>";
            output += "<div class=\"pilots\">" + this.ToPilotNameLink(pilotSummary) + "</div>";
            output += "<div class=\"channel\">" + pilotSummary.Channel + "</div>";
            output += "<div class=\"channel_color\" style=\"background-color: " + pilotSummary.ChannelColor + "\"></div>";

            for (let i = 1; i <= maxLaps; i++)
            {
                let lap = pilotSummary["Lap " + i];
                if (lap != null)
                {
                    output += "<div class=\"lap\">" + this.ToStringTime(lap) + "</div>";
                }
                else
                {
                    output += "<div class=\"lap\"> </div>";
                }
            }

            for (const lap in pilotSummary)
            {
                if (lap.startsWith("Best"))
                {
                    output += "<div class=\"lap\">" + this.ToStringTime(pilotSummary[lap]) +"</div>";
                }
            }


            output += '</div>';
        }
        output += '</div>';

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
        return channel.ShortBand + channel.Number;
    }

    async ShowEventStatus()
    {
        let output = "<h2>Event Status</h2><br>";
        output += "<div class=\"current_status\">";

        const prevcurrentnext = await this.GetPrevCurrentNextRaceSummaries();

        for (const raceName in prevcurrentnext)
        {
            const raceSummary = prevcurrentnext[raceName];
            if (raceSummary != null)
            {
                let raceNameHuman = raceName.replace("Race", " Race");

                output += "<div class=\"round\">";
                output += "<h3>" + raceNameHuman +  "</h3>";
                output += this.FormatRaceSummary(raceSummary);
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
        this.SetContent(await this.GetLapRecords());
        this.GetLapRecordsGraph();//Auto appends

        this.lastAction = this.ShowLapRecords;
    }

    async GetLapRecords()
    {
        const eventDetails = await this.eventManager.GetEvent();

        const lapCount = eventDetails.Laps;
        const pbLaps = eventDetails.PBLaps;

        let pilotRecords = await this.eventManager.GetLapRecords(pbLaps, lapCount);

        const showPB = (pbLaps != lapCount);
        const showHoleShot = (eventDetails.PrimaryTimingSystemLocation == "Holeshot");
        const showRaceTime = (eventDetails.EventType ==  "Race");

        pilotRecords.sort((a, b) => { return this.eventManager.TotalTime(a.laps) - this.eventManager.TotalTime(b.laps) });

        let output = "<h2>Lap Records</h2>";
        output += "<div class=\"columns\">";
        output += "<div class=\"row\" >";
        output += "<div class=\"position\">Position</div>";
        output += "<div class=\"pilots\">Pilots</div>";
        if (showHoleShot) output += "<div class=\"holeshot\">Holeshot</div>";
        if (showPB) output += "<div class=\"lap\">" + pbLaps + " Lap" + this.Plural(pbLaps) + " </div>";
        output += "<div class=\"laps\">" + lapCount + " Lap" + this.Plural(lapCount) + " </div>";
        if (showRaceTime) output += "<div class=\"racetime\">Race Time</div>";
        output += "</div>";

        let i = 1;
        for (const pilotRecord of pilotRecords)
        {
            output += "<div class=\"row\">";
            output += "<div class=\"position\">" + this.ToStringPosition(i) + "</div>";
            output += "<div class=\"pilots\">" + this.ToPilotNameLink(pilotRecord.pilot) + "</div>";
            if (showHoleShot) output += "<div class=\"holeshot\">" + this.LapsToTime(pilotRecord.holeshot) + "</div>";
            if (showPB) output += "<div class=\"lap\">" + this.LapsToTime(pilotRecord.lap) + "</div>";
            output += "<div class=\"laps\">" + this.LapsToTime(pilotRecord.laps) + "</div>";
            if (showRaceTime) output += "<div class=\"racetime\">" + this.LapsToTime(pilotRecord.race) + "</div>";

            output += "</div>";
            i++;
        }
        output += "</div>";
        return output;
    }

    async GetLapRecordsGraph()
    {
        const eventDetails = await this.eventManager.GetEvent();
        const colors = eventDetails.ChannelColors;

        let pilots = await this.eventManager.GetPilots();
        let rounds = await this.eventManager.GetRounds(r => r.Valid);

        const lapCount = eventDetails.PBLaps;

        let output = "<div >";
        output += "<h2> " + eventDetails.PBLaps + "  Lap times over Rounds</h2><br>";

        output += "<canvas class=\"graph\" id=\"posgraph\" width=\"800\" height=\"600\"> </canvas>";
        output += "</div>";
        this.AppendContent(output);

        let graph = new Graph(this.document, "posgraph");

        let best = 1000;
        let worst = 0;

        for (const round of rounds)
        {
            let races = await this.eventManager.GetRoundRaces(round.ID);
            for (const race of races)
            {
                for (const pilotIndex in pilots)
                {
                    const pilot = pilots[pilotIndex];
        
                    let colorIndex = pilotIndex % colors.length;
                    let color = colors[colorIndex];
        
                    const path = graph.GetPath(pilot.Name, color);

                    if (this.eventManager.RaceHasPilot(race, pilot.ID))
                    {
                        const raceLaps = this.eventManager.GetValidLapsPilot(race, pilot.ID);
                        const nonHoleshots = this.eventManager.ExcludeHoleshot(raceLaps);

                        const laps = this.eventManager.BestConsecutive(nonHoleshots, lapCount);

                        const time = this.eventManager.TotalTime(laps);
                        if (time == Number.MAX_SAFE_INTEGER || time == 0)
                            continue;

                        path.AddPoint(round.Order, time);

                        if (best > time)
                            best = time;
                        if (worst < time)
                            worst = time;
                    }
                }
            }
        }

        let minOrder = 100000;
        let maxOrder = 0;
    
        for (const round of rounds)
        {
            let roundName = round.EventType[0] + round.RoundNumber;

            graph.AddXLabel(roundName, round.Order);

            if (minOrder > round.Order)
                minOrder = round.Order;
            if (maxOrder < round.Order)
                maxOrder = round.Order;
        }

        const width = maxOrder - minOrder

        let scale = 1;
        if (worst - best > 5)
        {
            scale = 5;
        }

        for (let i = Math.floor(best); i < Math.ceil(worst); i += scale)
        {
            graph.AddYLabel(i, i);
        }

        const canvas = document.getElementById("posgraph");
        graph.SetView(minOrder - 100, best - scale, width + 300, (worst - best) + scale);
        graph.MakeGraph(canvas);

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
        let rounds = await this.eventManager.GetRounds(r => r.EventType == "Race");
        let pilotRecords = await this.eventManager.GetPoints(rounds);

        pilotRecords.sort((a, b) => 
        { 
            if (a.bracket != b.bracket)
            {
                return a.bracket.localeCompare(b.bracket); 
            }
            return b.lastTotal - a.lastTotal;
        });

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
                output += "<li>" + this.ToPilotNameLink(pilot) + "</li>";

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
                output += "<td class=\"cell_text\">" + this.ToPilotNameLink(pilot) +  "</td>";
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

        output += "<canvas class=\"graph\" id=\"posgraph\" width=\"800\" height=\"600\"> </canvas>";
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

    async ShowPilot(pilotId)
    {            
        const eventDetails = await this.eventManager.GetEvent();

        const lapCount = eventDetails.Laps;
        const pbLaps = eventDetails.PBLaps;
        const color = eventDetails.ChannelColors[0];

        let pilotRecords = await this.eventManager.GetLapRecords(pbLaps, lapCount, (p) => { return p.ID == pilotId; });

        // There will only be one.
        let pilotRecord = pilotRecords[0];

        var pilot = await eventManager.GetPilot(pilotId);

        let graph = new Graph(this.document, "posgraph");

        let output = "<h2>" + pilot.Name + "</h2>";
       
        let lapsOutput = "<div class=\"pilot_laps\">";

        let rounds = await eventManager.GetRounds();

        let totalLapCount = 0;
        let totalRaceCount = 0;

        let worstLap = 0;
        let bestLap = 200;

        for (const round of rounds)
        {
            if (round.Valid)
            {
                graph.AddXLabel("R" + round.RoundNumber, totalLapCount);

                let races = await this.eventManager.GetRoundRaces(round.ID);
                for (const race of races)
                {
                    if (race.Valid && eventManager.RaceHasPilot(race, pilotId))
                    {
                        lapsOutput += "<div class=\"pilot_lap\">";
                        lapsOutput += "<h3>Round "  + round.RoundNumber + "</h3>";
                        lapsOutput += '<table class=\"race_table\">';

                        let laps = eventManager.GetValidLapsPilot(race, pilotId);
                        for (const lap of laps)
                        {
                            const length = lap.LengthSeconds;
                            lapsOutput += "<tr>";
                            lapsOutput += "<td class=\"cell_text\">" + this.ToLapNumber(lap.LapNumber) +  "</td>";
                            lapsOutput += "<td class=\"cell_numeric\">" + this.ToStringTime(length) +  "</td>";
                            lapsOutput += "</tr>";

                            if (lap.LapNumber != 0)
                            {
                                totalLapCount++;

                                const path = graph.GetPath(pilot.Name, color);
                                path.AddPoint(totalLapCount, length);


                                if (worstLap < length)
                                {
                                    worstLap = length;
                                }

                                if (bestLap > length)
                                {
                                    bestLap = length;
                                }
                            }
                        }
                        lapsOutput += "</table>";
                        lapsOutput += "</div>";
                        totalRaceCount++;
                    }
                }
            }
        }
        
        lapsOutput += "</div>";

        output += "<div class=\"details\">";
        output += "<h3>Personal Records<h3>";
        output += '<table class=\"race_table\">';

        output += "<tr><td class=\"cell_text\">Holeshot</td><td class=\"cell_numeric\">" + this.LapsToTime(pilotRecord.holeshot) + "</td></tr>";
        output += "<tr><td class=\"cell_text\">" + pbLaps + " Lap(s)</td><td class=\"cell_numeric\">" + this.LapsToTime(pilotRecord.lap) + "</td></tr>";
        output += "<tr><td class=\"cell_text\">" + lapCount + " Lap(s)</td><td class=\"cell_numeric\">" + this.LapsToTime(pilotRecord.laps) + "</td></tr>";
        output += "<tr><td class=\"cell_text\">Race time</td><td class=\"cell_numeric\">" + this.LapsToTime(pilotRecord.race) + "</td></tr>";
        output += "<tr><td class=\"cell_text\">Lap Count </td><td class=\"cell_numeric\">" + totalLapCount + "</td></tr>";
        output += "<tr><td class=\"cell_text\">Race Count </td><td class=\"cell_numeric\">" + totalRaceCount + "</td></tr>";
        output += "</table>";

        output += "<div class=\"graph\">";
        output += "<h2>Lap times</h2><br>";
        output += "<canvas class=\"graph\" id=\"posgraph\" width=\"800\" height=\"600\"> </canvas>";
        output += "</div>";
        
        this.SetContent(output);
        this.AppendContent(lapsOutput);

        for (let i = Math.floor(bestLap); i < Math.ceil(worstLap); i++)
        {
            graph.AddYLabel(i, i);
        }

        const canvas = document.getElementById("posgraph");
        graph.SetView(0, bestLap, totalLapCount, worstLap - bestLap);
        graph.MakeGraph(canvas);

        this.lastAction = () => { this.ShowPilot(pilotId); }
    }

    FormatRoundsTable(rounds, pilotRecords)
    {
        let output = "<div class=\"columns\">";
        output += "<div class=\"row\" >";
        output += "<div class=\"pilots\">Pilots</div>";

        let hasBrackets = false;
        for (const pilotRecord of pilotRecords)
        {
            if (pilotRecord.bracket != "none" && pilotRecord.bracket != null)
            {
                hasBrackets = true;
                break;
            }
        }

        if (hasBrackets)
        {
            output += "<div class=\"bracket\">Bracket</div>";
        }

        let addTotal = true;
        for (const round of rounds)
        {
            let roundName = round.EventType[0] + round.RoundNumber;
            output += "<div class=\"r\">" + roundName + "</div>";
            addTotal = true;

            let hasTotal = false;
            let hasRRO = false;
            for (const pilotRecord of pilotRecords)
            {
                if (pilotRecord["total_" + roundName] != null)
                    hasTotal = true;

                if (pilotRecord["RRO_" + roundName] != null)
                hasRRO = true;
            }

            if (hasTotal)
            {
                output += "<div class=\"total\">Total</div>";
                addTotal = false;
            }

            if (hasRRO)
            {
                output += "<div class=\"r\">RRO</div>";
                addTotal = true;
            }
        }

        if (addTotal)
        {
            output += "<div class=\"total\">Total</div>";
            output += "</div>";
        }

        let i = 1;
        for (const pilotRecord of pilotRecords)
        {
            output += "<div class=\"row\">";
            output += "<div class=\"pilots\">" + this.ToPilotNameLink(pilotRecord.pilot) + "</div>";

            if (hasBrackets)
            {
                let bracket = pilotRecord.bracket;
                if (bracket == "none")
                    bracket = "";

                output += "<div class=\"bracket\">" + bracket + "</div>";
            }

            for (const round of rounds)
            {
                let roundName = round.EventType[0] + round.RoundNumber;
                let value = pilotRecord[roundName];
                if (value == null)
                    value = " ";

                output += "<div class=\"r\">" + value + "</div>";

                let totalName = "total_" + roundName;
                if (pilotRecord[totalName] != null)
                {
                    output += "<div class=\"total\">" + pilotRecord[totalName] + "</div>";
                }

                let rroName = "RRO_" + roundName;
                if (pilotRecord[rroName] != null)
                {
                    output += "<div class=\"r\">" + pilotRecord[rroName] + "</div>";
                }
            }

            if (addTotal)
            {
                output += "<div class=\"total\">" + pilotRecord.total + "</div>";
            }

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

    ToLapNumber(number)
    {
        if (number == 0)
            return "Holeshot";
        return "Lap " + number;
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

        if (position <= 0)
            return "";

        return position + post;
    }

    ToPilotNameLink(pilot)
    {
        return "<a href=\"#\" onclick=\"formatter.ShowPilot('" + pilot.ID + "')\">" + pilot.Name + "</a>";
    }

    async GetPrevCurrentNextRaceSummaries()
    {
        let prevcurrentnext = await this.eventManager.GetPrevCurrentNextRace();

        for (const raceName in prevcurrentnext)
        {
            prevcurrentnext[raceName] = await this.GetRaceSummary(prevcurrentnext[raceName]);
        }
        return prevcurrentnext;
    }

    async GetRaceSummary(race)
    {
        let event = await this.eventManager.GetEvent();
        let round = await this.eventManager.GetRound(race.Round);

        const targetLength = this.eventManager.TimeSpanToSeconds(event.RaceLength)

        const start = Date.parse(race.Start);
        const end = Date.parse(race.End);
        const now = Date.now();

        const raceTime = (now - start);

        let summary = 
        { 
            RaceID : race.ID,
            RoundNumber: round.RoundNumber,
            RaceNumber : race.RaceNumber,
            EventType : round.EventType,
            RaceStart : race.Start,
            RaceEnd : race.End,
            RaceTime : raceTime / 1000,
            Remaining : targetLength - (raceTime / 1000),
            MaxLength : targetLength,
            PBLaps : event.PBLaps,
            TargetLaps : race.TargetLaps,
            Bracket : race.Bracket,
            PrimaryTimingSystemLocation: race.PrimaryTimingSystemLocation,
            PilotSummaries : []
        };

        for (const pilotChannel of race.PilotChannels)
        {
            let pilotId = pilotChannel.Pilot;
            let pilot = await this.eventManager.GetPilot(pilotId);
            let laps = await this.eventManager.GetValidLapsPilot(race, pilotId);
            let result = await this.eventManager.GetPilotResult(race.ID, pilotId);
            let channel = await this.eventManager.GetChannel(pilotChannel.Channel);

            let pilotSummary = 
            {
                ID : pilotId,
                Name : pilot.Name,
                Position : 0,
                Points : 0,
                Channel : channel.ShortBand + "" + channel.Number,
                ChannelColor : channel.Color,
                Frequency : channel.Frequency,
            };

            if (result != null)
            {
                pilotSummary.Position = result.Position;
                pilotSummary.Points = result.Points;
            }

            let bestLap = this.eventManager.BestLap(laps);
            if (bestLap < 100000)
            {
                pilotSummary.BestLap = bestLap;
            }


            let nonHoleshots = this.eventManager.ExcludeHoleshot(laps);
            let pbLaps = this.eventManager.BestConsecutive(nonHoleshots, event.PBLaps);
            if (pbLaps.length > 0)
            {
                pilotSummary["BestConsecutive" + event.PBLaps] = this.eventManager.TotalTime(pbLaps);
            }

            let targetLaps = this.eventManager.BestConsecutive(nonHoleshots, event.TargetLaps);
            if (targetLaps.length > 0)
            {
                pilotSummary["BestConsecutive" + race.TargetLaps] = this.eventManager.TotalTime(targetLaps);
            }

            for (const lap of laps)
            {
                let lapName = "Lap " + lap.LapNumber;
                if (lap.LapNumber == 0)
                    lapName = "HS";

                pilotSummary[lapName] = lap.LengthSeconds;
            }

            pilotSummary.Total = this.eventManager.TotalTime(laps);
            if (result != null)
            {
                pilotSummary["Position"] =  result.DNF ? "DNF" : result.Position;
                pilotSummary["Points"] = result.Points;
            }

            summary.PilotSummaries.push(pilotSummary);
        }


        if (race.End == null || race.End == "0001/01/01 0:00:00")
        {
            const positions = this.eventManager.CalculatePositions(race);

            for(const index in summary.PilotSummaries)
            {
                const pilotSummary = summary.PilotSummaries[index];
                pilotSummary.Position = 1 + positions.indexOf(pilotSummary.ID);
            }
        }

        summary.PilotSummaries.sort((a, b) => 
        { 
            if (a == null || b == null)
                return 0;

            const result = a.Position - b.Position 
            if (result == 0)
            {
                return a.Frequency - b.Frequency;
            }
            return result;
        });

        return summary;
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

