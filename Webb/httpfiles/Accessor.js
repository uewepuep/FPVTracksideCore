class Accessor
{
    constructor(tooOld)
    {
        this.cache = [];
        this.tooOld = tooOld;
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
                    return tryCache.getJSON();
                }
            }

            let response = await fetch(url);
            let text = await response.text();

            let newCache = new CacheItem(text);
            this.cache[url] = newCache;

            this.lastTime = newCache.time;
            return newCache.getJSON();
        }
        catch (e)
        {
            return [];
        }
    }

    
}

class CacheItem
{
    constructor(text)
    {
        this.string = text;
        this.time = Date.now();
    }

    getJSON()
    {
        return JSON.parse(this.string);
    }
}