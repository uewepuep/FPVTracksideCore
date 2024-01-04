using RaceLib;
using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace DB
{
    public class CollectionDatabase : IDatabase
    {
        private ICollectionDatabase database;

        public int Version { get; private set; }

        public CollectionDatabase(ICollectionDatabase db)
        {
            database = db;
            Version = database.Version;
        }

        public void Dispose()
        {
            database?.Dispose();
            database = null;
        }

        public void Init(Guid eventId)
        {
            database.Init(eventId);
        }

        public IDatabaseCollection<T> GetCollection<T>() where T : BaseObject, new()
        {
            return database.GetCollection<T>();
        }

        public bool Update<T>(T obj) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null) 
                return false;

            return collection.Update(obj);
        }

        public int Update<T>(IEnumerable<T> objs) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return 0;

            return collection.Update(objs);
        }

        public bool Insert<T>(T obj) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

            return collection.Insert(obj);
        }

        public int Insert<T>(IEnumerable<T> objs) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return 0;

            return collection.Insert(objs);
        }

        public bool Upsert<T>(T obj) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

            return collection.Upsert(obj);
        }

        public int Upsert<T>(IEnumerable<T> objs) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return 0;

            return collection.Upsert(objs);
        }

        public bool Delete<T>(Guid id) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

            return collection.Delete(id);
        }

        public bool Delete<T>(T obj) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

            return collection.Delete(obj);
        }

        public int Delete<T>(IEnumerable<T> objs) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return 0;

            return collection.Delete(objs);
        }

        public IEnumerable<T> All<T>() where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return default(IEnumerable<T>);

            return collection.All();
        }

        public T GetObject<T>(Guid id) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return default(T);

            return collection.GetObject(id);
        }



        public IEnumerable<T> GetObjects<T>(IEnumerable<Guid> ids) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return default(T[]);

            return collection.GetObjects(ids);
        }

        public T GetCreateObject<T>(Guid id) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return default(T);

            return collection.GetCreateObject(id);
        }

        public T GetCreateExternalObject<T>(int id) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return default(T);

            return collection.GetCreateExternalObject(id);
        }

        public IEnumerable<Event> GetEvents()
        {
            return database.GetEvents();
        }

        public Event LoadEvent() 
        {
            return database.LoadEvent();
        }

        public IEnumerable<Race> LoadRaces()
        {
            IEnumerable<Race> races = database.LoadRaces();
            return races;

            //if (races.Any())
            //{
            //    Event ev = races.First().Event.Convert();
            //    foreach (Race srcRace in races)
            //    {
            //        Race destRace = srcRace.Convert();
            //        destRace.Event = ev;

            //        // Already existing detections on the laps..
            //        IEnumerable<Detection> lapDetections = destRace.Laps.Select(l => l.Detection);

            //        foreach (Detection srcDet in srcRace.Detections)
            //        {
            //            Detection destDet = lapDetections.GetObject<Detection>(srcDet.ID);
            //            if (destDet != null)
            //            {
            //                destDet = srcDet.Convert();
            //            }
            //            destRace.Detections.Add(destDet);
            //        }

            //        yield return destRace;
            //    }
            //}
        }

        public IEnumerable<Result> LoadResults()
        {
            return database.LoadResults();
        }

        public RaceLib.Club GetDefaultClub()
        {
            return database.GetCollection<RaceLib.Club>().All().FirstOrDefault();
        }
    }

    public interface ICollectionDatabase: IDisposable
    {
        int Version { get; }

        void Init(Guid eventId);

        IDatabaseCollection<T> GetCollection<T>() where T : BaseObject, new();
        IEnumerable<Event> GetEvents();
        Event LoadEvent();
        IEnumerable<Race> LoadRaces();
        IEnumerable<Result> LoadResults();
    }

    public interface IDatabaseCollection<T>
    {
        bool Update(T obj);
        int Update(IEnumerable<T> objs);
        public bool Insert(T obj);
        int Insert(IEnumerable<T> objs);
        bool Upsert(T obj);
        int Upsert(IEnumerable<T> objs);
        bool Delete(Guid id);
        bool Delete(T obj);
        int Delete(IEnumerable<T> objs);
        IEnumerable<T> All();
        T GetObject(Guid id);
        IEnumerable<T> GetObjects(IEnumerable<Guid> ids);
        T GetCreateObject(Guid id);
        T GetCreateExternalObject(int id);
    }
}
