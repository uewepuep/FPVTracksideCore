using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace DB.JSON
{
    public class JsonIO<T> where T : DatabaseObject
    {
        public const int FailureDelayMs = 1000;
        public const int FailureCount = 10;

        private static JsonSerializerSettings Settings = new JsonSerializerSettings()
        {
            Formatting = Formatting.Indented,
            DateFormatString = "yyyy/MM/dd H:mm:ss.FFF"
        };


        private Dictionary<string, T[]> cache;

        public JsonIO() 
        {
            cache = new Dictionary<string, T[]>();
        } 

        public T[] Read(string filename)
        {
            lock (cache)
            {
                if (cache.TryGetValue(filename, out T[] result))
                {
                    return result;
                }
            }
            if (!File.Exists(filename))
                return new T[0];

            string json = null;

            DateTime readTime = DateTime.MinValue;

            bool success = false;
            int failCount = 0;
            while (!success && failCount < FailureCount) 
            {
                try
                {
                    json = File.ReadAllText(filename);
                    success = true;
                    readTime = DateTime.Now;
                }
                catch
                {
                    Thread.Sleep(FailureDelayMs);
                    failCount++;    
                }
            }
            
            if (!success)
                return new T[0];

#if DEBUG
            Logger.Input.Log(null, filename);
#endif

            try
            {
                T[] result = JsonConvert.DeserializeObject<T[]>(json, Settings);
                if (result == null)
                {
                    return new T[0];
                }

                lock (cache) 
                {
                    cache[filename] = result;
                }

                return result;
            }
            catch
            {
                return new T[0];
            }
        }

        public int Write(string filename, IEnumerable<T> objs)
        {
            int count = 0;
            string json;
            T[] array = null;
            try
            {
                array = objs.Where(o => o != null).ToArray();
                json = JsonConvert.SerializeObject(array, Settings);
                count = array.Length;
            }
            catch
            {
                lock(cache)
                {
                    cache.Remove(filename);
                }
                return 0;
            }

            int failCount = 0;
            while (failCount < FailureCount)
            {
                try
                {
                    File.WriteAllText(filename, json);

                    lock (cache)
                    {
                        cache[filename] = array;
                    }

                    return count;
                }
                catch
                {
                    Thread.Sleep(FailureDelayMs);
                    failCount++;
                }
            }
            lock (cache)
            {
                cache.Remove(filename);
            }
            return 0;
        }
    }
}
