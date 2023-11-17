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

            if (lap.Pilot == pilotId)
            {
                output.push(lap);
            }
        }
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