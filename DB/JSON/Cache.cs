using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class Cache
    {
        private Dictionary<Type, DatabaseObject[]> cache;

        public Cache() 
        {
            cache = new Dictionary<Type, DatabaseObject[]>();
        }

        public void Load<T>(IEnumerable<T> objects) where T : DatabaseObject
        {
            lock (cache) 
            { 
                cache[typeof(T)] = objects.ToArray();
            }
        }

        public T GetObjectByID<T>(Guid id) where T : DatabaseObject
        { 
            lock (cache)
            {
                if (cache.TryGetValue(typeof(T), out DatabaseObject[] objs)) 
                {
                    return objs.FirstOrDefault(r => r.ID == id) as T;
                }
            }

            return null;
        }

        public IEnumerable<T> GetObjects<T>() where T : DatabaseObject
        {
            lock (cache)
            {
                if (cache.TryGetValue(typeof(T), out DatabaseObject[] objs))
                {
                    return objs.OfType<T>();
                }
            }
            return null;
        }
    }
}
