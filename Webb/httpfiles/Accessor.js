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
                if (age < this.tooOld)
                {
                    return tryCache.json;
                }
            }

            const response = await fetch(url);
            const json = await response.json();

            let newCache = new CacheItem(json);
            this.cache[url] = newCache;

            this.lastTime = newCache.time;
            return json;
        }
        catch
        {
            return null;
        }
    }

    
}

class CacheItem
{
    constructor(json)
    {
        this.json = json;
        this.time = Date.now();
    }
}