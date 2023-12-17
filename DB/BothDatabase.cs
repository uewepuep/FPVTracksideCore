using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB
{
    public class BothDatabase : IDatabase
    {
        public int Version
        {
            get
            {
                return 1;
            }
        }

        private CollectionDatabase jsondb;
        private CollectionDatabase litedb;

        public BothDatabase()
        {
            jsondb = new CollectionDatabase(new JSON.JsonDatabase());
            litedb = new CollectionDatabase(new Lite.LiteDatabase());
        }

        public void Dispose()
        {
            jsondb?.Dispose();
            litedb?.Dispose();

            jsondb = null;
            litedb = null;
        }

        public IEnumerable<T> All<T>() where T : BaseObject, new()
        {
            return Merge(jsondb.All<T>(), litedb.All<T>());
        }

        public bool Delete<T>(T t) where T : BaseObject, new()
        {
            return jsondb.Delete(t) && litedb.Delete(t);
        }

        public int Delete<T>(IEnumerable<T> t) where T : BaseObject, new()
        {
            return jsondb.Delete(t) + litedb.Delete(t);
        }

        public T GetCreateExternalObject<T>(int externalId) where T : BaseObject, new()
        {
            return jsondb.GetCreateExternalObject<T>(externalId);
        }

        public T GetCreateObject<T>(Guid id) where T : BaseObject, new()
        {
            return jsondb.GetCreateObject<T>(id);
        }

        public RaceLib.Club GetDefaultClub()
        {
            return jsondb.GetDefaultClub();
        }

        public IEnumerable<Event> GetEvents()
        {
            return Merge(jsondb.GetEvents(), litedb.GetEvents());
        }

        public T GetObject<T>(Guid id) where T : BaseObject, new()
        {
            T t = jsondb.GetObject<T>(id);
            if (t == null)
            {
                return litedb.GetObject<T>(id);
            }
            return t;
        }

        public IEnumerable<T> GetObjects<T>(IEnumerable<Guid> ids) where T : BaseObject, new()
        {
            return Merge(jsondb.GetObjects<T>(ids), litedb.GetObjects<T>(ids));
        }

        public bool Insert<T>(T t) where T : BaseObject, new()
        {
            return jsondb.Insert(t);
        }

        public int Insert<T>(IEnumerable<T> t) where T : BaseObject, new()
        {
            return jsondb.Insert(t);
        }

        public Event LoadEvent(Guid eventId)
        {
            Event eve = jsondb.LoadEvent(eventId);
            if (eve.Pilots.Any(p => p != null))
            {
                return eve;
            }
            eve = litedb.LoadEvent(eventId);
            jsondb.Upsert(eve);
            jsondb.Upsert(eve.Pilots);
            return eve;
        }

        public IEnumerable<Race> LoadRaces(Guid eventId)
        {
            Race[] races = jsondb.LoadRaces(eventId).ToArray();
            if (races.Any())
            {
                return races;
            }
            races = litedb.LoadRaces(eventId).ToArray();

            // Quick little fix as event isn't loaded here, its normally applied in race manager.
            Event eve = jsondb.LoadEvent(eventId);
            foreach (Race race in races)
            {
                race.Event = eve;
            }

            RaceLib.Round[] rounds = races.Select(ra => ra.Round).Distinct().OrderBy(r => r.Order).ToArray();   

            jsondb.Upsert(rounds);
            jsondb.Upsert(races);
            return races;
        }

        public IEnumerable<Result> LoadResults(Guid eventId)
        {
            IEnumerable<Result> results = jsondb.LoadResults(eventId);
            if (results.Any())
            {
                return results;
            }
            results = litedb.LoadResults(eventId);

            jsondb.Upsert(results);
            return results;
        }

        public bool Update<T>(T t) where T : BaseObject, new()
        {
            return litedb.Update(t);
        }

        public int Update<T>(IEnumerable<T> t) where T : BaseObject, new()
        {
            return litedb.Update(t);
        }

        public bool Upsert<T>(T t) where T : BaseObject, new()
        {
            return litedb.Upsert(t);
        }

        public int Upsert<T>(IEnumerable<T> t) where T : BaseObject, new()
        {
            return litedb.Upsert(t);
        }


        private List<T> Merge<T>(IEnumerable<T> primary, IEnumerable<T> secondary) where T : BaseObject
        {
            List<T> output = primary.ToList();
            foreach (T t in secondary)
            {
                T? existing = output.FirstOrDefault(a => a.ID == t.ID);
                if (existing == null)
                {
                    output.Add(t);
                }
            }
            return output;
        }
    }
}
