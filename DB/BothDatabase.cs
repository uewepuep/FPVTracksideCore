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

        private Guid eventId;

        public BothDatabase(DirectoryInfo dataDir)
        {
            jsondb = new CollectionDatabase(new JSON.JSONDatabaseConverted(dataDir));
            litedb = new CollectionDatabase(new Lite.LiteDatabase());
        }

        public void Dispose()
        {
            jsondb?.Dispose();
            litedb?.Dispose();

            jsondb = null;
            litedb = null;
        }

        public void Init(Guid eventId)
        {
            this.eventId = eventId;
            jsondb.Init(eventId);
            litedb.Init(eventId);
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

        public Event LoadEvent()
        {
            Event eve = jsondb.LoadEvent();
            if (eve.Pilots.Any(p => p != null))
            {
                return eve;
            }

            Event eve2 = litedb.LoadEvent();
            if (eve2 != null)
            {
                jsondb.Upsert(eve2);
                jsondb.Upsert(eve2.Pilots);
                return eve2;
            }

            return eve;
        }

        public IEnumerable<Race> LoadRaces()
        {
            Race[] races = jsondb.LoadRaces().ToArray();
            if (races.Any())
            {
                return races;
            }
            races = litedb.LoadRaces().ToArray();

            // Quick little fix as event isn't loaded here, its normally applied in race manager.
            Event eve = jsondb.LoadEvent();
            foreach (Race race in races)
            {
                race.Event = eve;
            }

            RaceLib.Round[] rounds = races.Select(ra => ra.Round).Distinct().OrderBy(r => r.Order).ToArray();   

            jsondb.Upsert(rounds);
            jsondb.Upsert(races);
            return races;
        }

        public IEnumerable<Result> LoadResults()
        {
            IEnumerable<Result> results = jsondb.LoadResults();
            if (results.Any())
            {
                return results;
            }
            results = litedb.LoadResults();

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
