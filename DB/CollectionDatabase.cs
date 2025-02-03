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

        public bool Update<T>(IEnumerable<T> objs) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

            return collection.Update(objs);
        }

        public bool Insert<T>(T obj) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

            return collection.Insert(obj);
        }

        public bool Insert<T>(IEnumerable<T> objs) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

            return collection.Insert(objs);
        }

        public bool Upsert<T>(T obj) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

            return collection.Upsert(obj);
        }

        public bool Upsert<T>(IEnumerable<T> objs) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

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

        public bool Delete<T>(IEnumerable<T> objs) where T : BaseObject, new()
        {
            IDatabaseCollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

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

        public IEnumerable<SimpleEvent> GetSimpleEvents()
        {
            return database.GetSimpleEvents();
        }

        public Event LoadEvent() 
        {
            return database.LoadEvent();
        }

        public IEnumerable<Race> LoadRaces()
        {
            IEnumerable<Race> races = database.LoadRaces();
            return races;
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
        IEnumerable<SimpleEvent> GetSimpleEvents();
        Event LoadEvent();
        IEnumerable<Race> LoadRaces();
        IEnumerable<Result> LoadResults();
    }

    public interface IDatabaseCollection<T>
    {
        bool Update(T obj);
        bool Update(IEnumerable<T> objs);
        bool Insert(T obj);
        bool Insert(IEnumerable<T> objs);
        bool Upsert(T obj);
        bool Upsert(IEnumerable<T> objs);
        bool Delete(Guid id);
        bool Delete(T obj);
        bool Delete(IEnumerable<T> objs);
        IEnumerable<T> All();
        T GetObject(Guid id);
        IEnumerable<T> GetObjects(IEnumerable<Guid> ids);
        T GetCreateObject(Guid id);
        T GetCreateExternalObject(int id);
    }
}
