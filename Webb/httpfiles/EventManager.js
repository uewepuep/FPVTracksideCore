class EventManager
{
    constructor()
    {
    }

    async GetPilots()
    {
        return await this.GetJSON("event/Pilots.json");
    }

    async GetRounds()
    {
        return await this.GetJSON("event/Rounds.json");
    }

    async GetRace(id)
    {
        let raceArray = await this.GetJSON("event/" + id + "/Race.json");
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

        let raceIds = await this.GetJSON("races");
        if (raceIds == null)
            return null;

        for (const raceId of raceIds)
        {
            let race = await this.GetRace(raceId);
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

    GetValidLaps(race, pilotId)
    {
        let output = [];

        const laps = this.GetValidLaps(race);

        for (const lap of race.Laps)
        {
            if (lap.detectionObject.Pilot == pilotId)
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

        output.sort((a, b) => { return a.EndTime - b.EndTime });

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

    async GetLapRecords()
    {
        let records = [];

        let pilots = await this.GetPilots();
        let races = await this.GetRaces((r) => { return r.Valid; });

        let eventLapsTarget = 4;
        let hasHoleshot = true;

        let raceLapsTarget = eventLapsTarget;
        if (hasHoleshot)
            raceLapsTarget++;

        for (const pilotIndex in pilots)
        {
            const pilot = pilots[pilotIndex];

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
                    let raceLaps = this.GetValidLaps(race, pilot.ID);

                    let nonHoleshots = this.ExcludeHoleshot(raceLaps);

                    let holeshot = [this.GetHoleshot(raceLaps)];
                    let lap = this.BestConsecutive(nonHoleshots, 1);
                    let laps = this.BestConsecutive(nonHoleshots, eventLapsTarget);

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
                        let raceLaps = this.GetValidLaps(race, pilot.ID);
                        const count = raceLaps.length;
                        pilotRecord[roundName] = count;
                        pilotRecord.total += count;
                    }
                }
            }
            records.push(pilotRecord);
        }
        return records;
    }

    async GetPoints()
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
                        let result = await this.GetPilotResult(race.ID, pilot.ID);
                        if (result != null)
                        {
                            pilotRecord[roundName] = result.Points;
                            pilotRecord.total += result.Points;
                        }
                    }
                }
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

        for (let i = 0; i <= validLaps.length - consecutive; i++)
        {
            //let current = validLaps.Skip(i).Take(consecutive);
            let current = [];
            for (let j = i; j < i + consecutive; j++)
            {
                let lap = validLaps[j];
                current.push(lap);
            }
            
            if (best.length == 0 || this.TotalTime(current) < bestTime)
            {
                best = current;
                bestTime = this.TotalTime(best);
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
        }

        return total;
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
        let races = await this.GetRaces((r) => { return r.Round == roundId });
        races.sort((a, b) => { return a.RaceNumber - b.RaceNumber });
        return races;
    }

    async GetPilot(id)
    {
        return this.GetObjectByID("event/Pilots.json", id);
    }

    async GetChannel(id)
    {
        let colorChannels = await this.GetJSON("channelcolors/");

        for (const colorChannel of colorChannels) {
            if (colorChannel.ID == id)
                return colorChannel;
        }
    }

    async GetRounds()
    {
        let rounds = await this.GetJSON("event/Rounds.json");
        rounds.sort((a, b) => { return a.RoundNumber - b.RoundNumber });
        return rounds;
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
        let outputRaces = [];
        let rounds = await this.GetRounds();
        for (const round of rounds)
        {
            if (round.Valid)
            {
                let races = await this.GetRoundRaces(round.ID);

                let last = null;
                let lastLast = null;
                for (const race of races)
                {
                    if (race.Valid)
                    {
                        if (race.End == null || race.End == "0001/01/01 0:00:00")
                        {
                            outputRaces.push(lastLast);
                            outputRaces.push(last);
                            outputRaces.push(race);
                            return outputRaces;
                        }
                        lastLast = last;
                        last = race;
                    }
                }
            }
        }
        return outputRaces;
    }

    async GetResults(raceID)
    {
        return await this.GetJSON("event/" + raceID + "/Result.json");
    }

    async GetObjectByID(url, id)
    {
        let objects = await this.GetJSON(url);
        for (const object of objects) {
            if (object.ID == id) {
                return object;
            }
        }

        return null;
    }

    async GetJSON(url)
    {
        try
        {
            const response = await fetch(url);
            const json = await response.json();

            this.time = Date.now();
            return json;
        }
        catch
        {
            return null;
        }
        
    }
}