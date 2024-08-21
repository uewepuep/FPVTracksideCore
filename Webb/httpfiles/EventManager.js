class EventManager
{

    constructor(eventDirectory, tooOld)
    {
        this.eventDirectory = eventDirectory;
        this.accessor = new Accessor(tooOld);
    }

    async GetEvent()
    {
        return (await this.accessor.GetJSON(this.eventDirectory + "/Event.json"))[0];
    }

    async GetClub(id)
    {
        return (await this.accessor.GetJSON("index.php?page=pages/ClubData.php&id=" + id))[0];
    }

    async GetPilots()
    {
        let output = [];
        let event = await this.GetEvent();
        let allPilots = await this.accessor.GetJSON(this.eventDirectory + "/Pilots.json");

        for (let i = 0; i < allPilots.length; i++)
        {
            let pilot  = allPilots[i];
            for (let j = 0; j < event.PilotChannels.length; j++)
            {
                let pilotChannel = event.PilotChannels[j];
                if (pilotChannel.Pilot == pilot.ID)
                {
                    output.push(pilot);
                }
            }
        }
        return output;
    }

    async GetRounds(delegate = null)
    {
        let allRounds = await this.accessor.GetJSON(this.eventDirectory + "/Rounds.json");

        allRounds.sort((a, b) => 
        { 
            if (a == null || b == null)
                return 0;

            return a.Order - b.Order 
        });
        
        let rounds = [];
        for (let i = 0; i < allRounds.length; i++)
        {
            let round = allRounds[i];

            if (!round.Valid)
                continue;

            if (delegate == null || delegate(round))
            {
                rounds.push(round);
            }
        }
        return rounds;
    }

    async GetRace(id)
    {
        let raceArray = await this.accessor.GetJSON(this.eventDirectory + "/" + id + "/Race.json");
        if (raceArray == null)
            return null;

        if (raceArray.length > 0)
        {
            return raceArray[0];
        }
        return null;
    }

    async GetRaces(delegate = null)
    {
        let races = [];

        let event = await this.GetEvent();

        let raceIds = event.Races;
        if (raceIds == null)
            return null;

        for (const raceId of raceIds)
        {
            let race = await this.GetRace(raceId);
            if (race == null)
                continue;

            if (delegate == null || delegate(race))
            {
                races.push(race);
            }
        }
        return races;
    }
    
    RaceHasPilot(race, pilotId)
    {
        for (const pilotChannel of race.PilotChannels)
        {
            if (pilotChannel.Pilot == pilotId)
                return true;
        }

        return false;
    }

    GetPilotRaces(races, pilotID)
    {
        let output = [];
        for (const race of races)
        {
            if (this.RaceHasPilot(race, pilotID))
            {
                output.push(race);
            }
        }
        return output;
    }

    GetValidLapsPilot(race, pilotId, maxLapNumber = 999)
    {
        let output = [];

        const laps = this.GetValidLaps(race);

        for (const lap of laps)
        {
            if (lap.detectionObject.Pilot == pilotId && lap.LapNumber <= maxLapNumber)
            {
                output.push(lap);
            }
        }
        return output;
    }

    GetValidLaps(race)
    {
        let output = [];

        for (const lap of race.Laps)
        {
            if (lap.detectionObject == null)
            {
                let detection = this.GetDetection(race, lap);
                lap.detectionObject = detection;
            }

            if (lap.detectionObject == null)
                continue;

            if (lap.detectionObject.Valid == false)
                continue;

            if (lap.LengthSeconds <= 0)
                continue;

            output.push(lap);
        }

        output.sort((a, b) => 
        { 
            if (a == null || b == null)
                return 0;

            return a.EndTime - b.EndTime 
        });

        return output;
    }

    GetDetection(race, lap)
    {
        for (const detection of race.Detections)
        {
            if (lap.Detection == detection.ID)
                return detection;
        }
        return null;
    }

    async GetLapRecords(pbLabs, lapCount, wherePilot = null)
    {
        let records = [];

        let pilots = await this.GetPilots();
        let races = await this.GetRaces((r) => { return r.Valid; });

        let hasHoleshot = true;

        let raceLapsTarget = lapCount;
        if (hasHoleshot)
            raceLapsTarget++;

        for (const pilotIndex in pilots)
        {
            const pilot = pilots[pilotIndex];

            if (wherePilot != null)
            {
                if (wherePilot(pilot) == false)
                    continue;
            }

            const pilotRecord =
            {
                pilot: pilot,
                holeshot: [],
                lap: [],
                laps: [],
                race: []
            };

            for (const raceIndex in races)
            {
                const race = races[raceIndex];

                if (this.RaceHasPilot(race, pilot.ID))
                {
                    let raceLaps = this.GetValidLapsPilot(race, pilot.ID);

                    let nonHoleshots = this.ExcludeHoleshot(raceLaps);

                    let holeshot = [this.GetHoleshot(raceLaps)];
                    let lap = this.BestConsecutive(nonHoleshots, pbLabs);
                    let laps = this.BestConsecutive(nonHoleshots, lapCount);

                    let raceTime = [];
                    if (raceLaps.length == raceLapsTarget)
                    {
                        raceTime = raceLaps;
                    }

                    if (this.TotalTime(holeshot) < this.TotalTime(pilotRecord.holeshot) && holeshot != null)
                        pilotRecord.holeshot = holeshot;

                    if (this.TotalTime(lap) < this.TotalTime(pilotRecord.lap))
                        pilotRecord.lap = lap;

                    if (this.TotalTime(laps) < this.TotalTime(pilotRecord.laps))
                        pilotRecord.laps = laps;

                    if (this.TotalTime(raceTime) < this.TotalTime(pilotRecord.race))
                        pilotRecord.race = raceTime;
                }
            }
            records.push(pilotRecord);
        }
        return records;
    }

    async GetLapCounts()
    {
        let records = [];

        let pilots = await this.GetPilots();
        let races = await this.GetRaces((r) => { return r.Valid; });
        let rounds = await this.GetRounds();

        for (const pilotIndex in pilots)
        {
            const pilot = pilots[pilotIndex];

            const pilotRecord =
            {
                pilot: pilot,
                total: 0
            };

            for (const roundIndex in rounds)
            {
                const round = rounds[roundIndex];
                let roundName = round.EventType[0] + round.RoundNumber;

                let roundRaces = []
                for (const raceIndex in races)
                {
                    const race = races[raceIndex];

                    if (round.ID == race.Round)
                    {
                        roundRaces.push(race);
                    }
                }

                for (const raceIndex in roundRaces)
                {
                    const race = roundRaces[raceIndex];

                    if (this.RaceHasPilot(race, pilot.ID))
                    {
                        let raceLaps = this.GetValidLapsPilot(race, pilot.ID);
                        let exclude = this.ExcludeHoleshot(raceLaps)
                        const count = exclude.length;
                        pilotRecord[roundName] = count;
                        pilotRecord.total += count;
                    }
                }
            }
            records.push(pilotRecord);
        }
        return records;
    }

    async GetPoints(rounds)
    {
        let records = [];

        let pilots = await this.GetPilots();
        let races = await this.GetRaces((r) => 
        { 
            if (r == null)
                return false;

            return r.Valid; 
        });

        let rros = [];

        for (const pilotIndex in pilots)
        {
            const pilot = pilots[pilotIndex];

            const pilotRecord =
            {
                pilot: pilot,
                total: 0,
                bracket: "none"
            };

            let totalName = "total";

            let worstResult = 100000;

            for (const roundIndex in rounds)
            {
                const round = rounds[roundIndex];
                let roundName = round.EventType[0] + round.RoundNumber;
                let pointSummary = round.PointSummary;

                let roundRaces = []
                for (const raceIndex in races)
                {
                    const race = races[raceIndex];

                    if (round.ID == race.Round)
                    {
                        roundRaces.push(race);
                    }
                }

                for (const raceIndex in roundRaces)
                {
                    const race = roundRaces[raceIndex];
                    if (this.RaceHasPilot(race, pilot.ID))
                    {
                        if (pilotRecord.bracket == "none")
                        {
                            pilotRecord.bracket = race.Bracket;
                        }

                        let result = await this.GetPilotResult(race.ID, pilot.ID);
                        if (result != null)
                        {
                            if (worstResult > result.Points)
                                worstResult = result.Points;

                            pilotRecord[roundName] = result.Points;
                            pilotRecord[totalName] += result.Points;
                        }
                        else
                        {
                            worstResult = 0;
                        }
                    }
                }

                if (pointSummary != null)
                {
                    let rroResult = null;
                    let rro = await this.GetGeneralResults(r => r.ResultType == "RoundRollOver" && r.Round == round.ID && r.Pilot == pilot.ID);
                    if (rro.length == 1)
                    {
                        rroResult = rro[0];
                    }

                    if (rroResult != null && worstResult > rroResult.Points)
                    {
                        worstResult = rroResult.Points;
                    }

                    if (pointSummary.DropWorstRound && worstResult < 10000)
                    {
                        pilotRecord.total -= worstResult;
                    }

                    totalName = "total_" + roundName;
                    pilotRecord[totalName] = pilotRecord.total;
                    pilotRecord.lastTotal = pilotRecord.total;
                    pilotRecord.total = 0;

                    if (rroResult != null)
                    {
                        pilotRecord["RRO_" + roundName] = rroResult.Points;
                        pilotRecord.total += pilotRecord["RRO_" + roundName];
                    }

                    rros[roundName] = totalName;
                }

                totalName = "total";
            }
            
            if (pilotRecord.total > 0 && pilotRecord.lastTotal == 0)
            {
                pilotRecord.lastTotal = pilotRecord.total;
            }

            records.push(pilotRecord);
        }

        return records;
    }

    GetHoleshot(validLaps)
    {
        for (let i = 0; i < validLaps.length; i++)
        {
            let lap = validLaps[i];
            if (lap.LapNumber == 0)
            {
                return lap;
            }
        }

        return null;
    }

    ExcludeHoleshot(validLaps)
    {
        let nonHoleshot = [];
        for (let i = 0; i < validLaps.length; i++)
        {
            let lap = validLaps[i];
            if (lap.LapNumber != 0)
            {
                nonHoleshot.push(lap);
            }
        }
        return nonHoleshot;
    }

    BestConsecutive(validLaps, consecutive)
    {
        let best = [];
        let bestTime = 10000;

        validLaps.sort((a, b) => { return a.LapNumber - b.LapNumber; });

        for (let i = 0; i <= validLaps.length - consecutive; i++)
        {
            let current = [];
            for (let j = i; j < i + consecutive; j++)
            {
                let lap = validLaps[j];
                current.push(lap);
            }

            if (current.length != consecutive)
                continue;

            let time = this.TotalTime(current);
            if (best.length == 0 || time < bestTime)
            {
                best = current;
                bestTime = time;
            }
        }
        return best;
    }
    TotalTime(validLaps)
    {
        if (validLaps == null || validLaps.length == 0)
            return Number.MAX_SAFE_INTEGER;

        let total = 0;

        for (const lapIndex in validLaps)
        {
            const lap = validLaps[lapIndex];
            if (lap != null)
            {
                total += lap.LengthSeconds;
            }
            else
            {
                return Number.MAX_SAFE_INTEGER;
            }
        }

        return total;
    }

    BestLap(validLaps)
    {
        if (validLaps == null || validLaps.length == 0)
        return Number.MAX_SAFE_INTEGER;

        let min = Number.MAX_SAFE_INTEGER;

        for (const lapIndex in validLaps)
        {
            const lap = validLaps[lapIndex];

            if (lap.LapNumber == 0)
                continue;
            
            min = Math.min(min, lap.LengthSeconds);
        }

        return min;
    }

    async GetRound(roundId)
    {
        let rounds = await this.GetRounds();
        for (const roundIndex in rounds)
        {
            const round = rounds[roundIndex];
            if (round.ID == roundId)
                return round;
        }
        return null;
    }

    async GetRoundRaces(roundId)
    {
        let races = await this.GetRaces((r) => { return r.Round == roundId && r.Valid });
        races.sort((a, b) => 
        { 
            if (a == null || b == null)
                return 0;

            return a.RaceNumber - b.RaceNumber 
        });
        return races;
    }

    async GetPilot(id)
    {
        return this.GetObjectByID(this.eventDirectory + "/Pilots.json", id);
    }

    async GetChannels()
    {
        return await this.accessor.GetJSON("httpfiles/Channels.json");
    }

    async GetEventChannels()
    {
        let event = await this.GetEvent();
        let channels = await this.GetChannels();

        let output = [];
        for (let j = 0; j < channels.length; j++)
        {
            for (let i = 0; i < event.Channels.length; i++)
            {
                if (channels[j].ID == event.Channels[i])
                {
                    output.push(channels[j]);
                }
            }
        }

        output.sort((a, b) => { return a.Frequency - b.Frequency });

        return output;
    }

    async GetChannel(id)
    {
        let event = await this.GetEvent();
        let channelColors = event.ChannelColors;
        let eventChannels = await this.GetEventChannels();

        let lastChannel = null;
        let colorIndex = 0;
        for (let i = 0; i < eventChannels.length; i++)
        {
            let channel = eventChannels[i];

            if (i > 0)
            {
                if (!this.InterferesWith(channel, lastChannel))
                {
                    colorIndex = (colorIndex + 1) % channelColors.length;
                }
            }

            if (channel.ID == id)
            {
                channel.Color = channelColors[colorIndex];
                return channel;
            }

            lastChannel = channel;
        }

        return null;
    }

    async GetPilotResult(raceID, pilotID)
    {
        let results = await this.GetResults(raceID);
        if (results == null)
            return null;

        for (const result of results)
        {
            if (result.Pilot == pilotID && result.Valid)
                return result;
        }
    }

    async GetPrevCurrentNextRace()
    {
        let output = {};
        let rounds = await this.GetRounds(r => r.Valid);

        let last = null;
        let lastLast = null;

        let returnNext = false;

        for (const round of rounds)
        {
            let races = await this.GetRoundRaces(round.ID);

            for (const race of races)
            {
                if (race.Valid)
                {
                    if (returnNext)
                    {
                        output.PreviousRace = lastLast;
                        output.CurrentRace = last;
                        output.NextRace = race;

                        return output;
                    }

                    if (race.End == null || race.End == "0001/01/01 0:00:00")
                    {
                        returnNext = true;
                    }
                    lastLast = last;
                    last = race;
                }
            }
        }
        output.PreviousRace = lastLast;
        output.CurrentRace = last;
        return output;
    }

    async GetResults(raceID)
    {
        return await this.accessor.GetJSON(this.eventDirectory + "/" + raceID + "/Result.json");
    }

    
    async GetGeneralResults(delegate = null)
    {
        let results = await this.accessor.GetJSON(this.eventDirectory + "/Results.json");
        let rro = [];

        for (let i = 0; i < results.length; i++)
        {
            let result = results[i];
            if (delegate == null || delegate(result))
            {
                rro.push(result);
            }
        }
        return rro;
    }

    async GetObjectByID(url, id)
    {
        let objects = await this.accessor.GetJSON(url);
        if (objects == null)
            return null;

        for (const object of objects) {
            if (object.ID == id) {
                return object;
            }
        }

        return null;
    }

    InterferesWith(channelA, channelB)
    {
        if (channelA == null || channelB == null)
            return false;

        const range = 15;
        return Math.abs(channelA.Frequency - channelB.Frequency) < range;
    }

    TimeSpanToSeconds(timespan)
    {
        const components = timespan.split(":");
        if (components.length == 3)
        {
            const hours = parseInt(components[0]);
            const minutes = parseInt(components[1]);
            const seconds = parseFloat(components[2]);
            return 3600 * hours + 60 * minutes + seconds;
        }
        return 0;
    }

    CalculatePositions(race)
    {
        let detections = race.Detections;

        detections.sort((a, b) => 
        { 
            if (a == null || b == null)
                return 0;

            let result = a.RaceSector - b.RaceSector 
            if (result == 0)
            {
                return a.Time - b.Time;
            }
        });

        let outputPilotIds = []
        for (const detection of detections)
        {
            if (!detection.Valid)
                continue;

            let pilotId = detection.Pilot;
            if (!outputPilotIds.includes(pilotId))
            {
                outputPilotIds.push(pilotId);
            }
        }
        return outputPilotIds;
    }
}