class EventManager
{
    constructor()
    {
    }

    async GetPilots()
    {
        return await this.GetJSON("event/Pilots.json");
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

            if (lap.LengthSeconds < 0)
                continue;

            if (lap.detectionObject.Pilot == pilotId)
            {
                output.push(lap);
            }
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

        let eventLaps = 4;

        for (const pilotIndex in pilots)
        {
            const pilot = pilots[pilotIndex];

            const pilotRecord =
            {
                pilot: pilot,
                holeshot: [],
                lap: [],
                laps: [],
                racetime: []
            };

            for (const raceIndex in races)
            {
                const race = races[raceIndex];

                if (this.RaceHasPilot(race, pilot.ID))
                {
                    let raceLaps = this.GetValidLaps(race, pilot.ID);

                    let holeshot = this.BestConsecutive(raceLaps, 0);
                    let lap = this.BestConsecutive(raceLaps, 1);
                    let laps = this.BestConsecutive(raceLaps, eventLaps);
                    let raceTime = this.BestConsecutive(raceLaps, eventLaps);

                    if (this.TotalTime(holeshot) < this.TotalTime(pilotRecord.holeshot))
                        pilotRecord.holeshot = holeshot;

                    if (this.TotalTime(lap) < this.TotalTime(pilotRecord.lap))
                        pilotRecord.lap = lap;

                    if (this.TotalTime(laps) < this.TotalTime(pilotRecord.laps))
                        pilotRecord.laps = laps;

                    if (this.TotalTime(raceTime) < this.TotalTime(pilotRecord.raceTime))
                        pilotRecord.raceTime = raceTime;
                }
            }
            records.push(pilotRecord);
        }
        return records;
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
                current.push(validLaps[j]);
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

            total += lap.LengthSeconds;
        }

        return total;
    }

    async GetRoundRaces(roundId)
    {
        return await this.GetRaces((r) => { return r.Round == roundId });
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
        return await this.GetJSON("event/Rounds.json");
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
            return json;
        }
        catch
        {
            return null;
        }
        
    }
}