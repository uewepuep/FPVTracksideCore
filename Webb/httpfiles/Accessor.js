class Accessor
{
    constructor()
    {
        this.cache = [];
        this.tooOld = 5000;
    }

    async GetJSON(url)
    {
        try
        {
            const now = Date.now();

            let tryCache = this.cache[url];
            if (tryCache != null)
            {
                const age = now - tryCache.time;
                if (age < this.tooOld && tryCache.response != null)
                {
                    return await tryCache.getJSON();
                }
            }

            let response = await fetch(url);
            let json = await response.json();

            let newCache = new CacheItem(json);
            this.cache[url] = newCache;

            this.lastTime = newCache.time;
            return json;
        }
        catch (e)
        {
            return null;
        }
    }

    
}

class CacheItem
{
    constructor(json)
    {
        this.string = JSON.stringify(json);
        this.time = Date.now();
    }

    getJSON()
    {
        JSON.parse(this.string);
    }
}