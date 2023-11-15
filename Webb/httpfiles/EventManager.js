class EventManager
{
    constructor()
    {
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
        let races = new Array();

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
        return this.GetObjectByID("channels", id);
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