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

    async GetRaces(delegate)
    {
        let races = [];

        let raceIds = await this.GetJSON("races");
        if (raceIds == null)
            return null;

        for (const raceId of raceIds)
        {
            let race = await this.GetRace(raceId);
            if (delegate(race))
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

    GetValidLaps(race, pilotId)
    {
        let output = [];

        for (const lap of race.Laps)
        {
            let detection = this.GetDetection(race, lap);
            lap.detectionObject = detection;

            if (detection == null)
                continue;

            if (detection.Valid == false)
                continue;

            if (lap.Length < 0)
                continue;

            if (lap.Pilot == pilotId)
            {
                output.push(lap);
            }
        }

        output.sort((a, b) => { return a.Start - b.Start });

        return output;
    }

    GetDetection(race, lap)
    {
        for (const detection of race.Detections)
        {
            if (lap.detection == detection.ID)
                return detection;
        }
        return null;
    }

    async GetLapRecords()
    {
        let records = [];

        let pilots = await this.GetPilots();
        let races = await this.GetRaces();

        for (const pilot in pilots)
        {
            for (const race in races)
            {
                if (this.RaceHasPilot(race, pilot.ID))
                {
                    let laps = this.GetValidLaps(race, pilot.ID);

                }
            }
        }
    }

    BestConsecutive(validLaps, consecutive)
    {
        let best = [];

        for (const lap in validLaps)
        {
            
        }

        laps.Where(l => l.Detection.Valid && !l.Detection.IsHoleshot && l.Length.TotalSeconds > 0).OrderBy(l => l.End).ToArray();
        for (int i = 0; i <= filtered.Length - consecutive; i++)
        {
            IEnumerable < Lap > current = filtered.Skip(i).Take(consecutive);
            if (!best.Any() || current.TotalTime() < best.TotalTime()) {
                best = current;
            }
        }
        return best;
    }

    TotalTime(validLaps)
    {
        let start = new Date();
        let end = new Date();

        for (const lap in validLaps)
        {
            let lapStart = new Date(lap.Start);
            let lapEnd = new Date(lap.End);

            if (start > lapStart)
                start = lapStart;
            if (end < lapEnd)
                end = lapEnd;
        }

        return end - start;
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