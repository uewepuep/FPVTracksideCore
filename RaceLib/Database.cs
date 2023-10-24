using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Database : IDisposable
    {
        private DB.IDatabase database;

        public int Version { get; private set; }

        private ConvertedCollection<Event, DB.Event> events;
        private ConvertedCollection<Pilot, DB.Pilot> pilots;
        private ConvertedCollection<Channel, DB.Channel> channels;
        private ConvertedCollection<PilotChannel, DB.PilotChannel> pilotChannels;
        private ConvertedCollection<Race, DB.Race> races;
        private ConvertedCollection<Lap, DB.Lap> laps;
        private ConvertedCollection<Detection, DB.Detection> detections;
        private ConvertedCollection<Round, DB.Round> rounds;
        private ConvertedCollection<Result, DB.Result> results;
        private ConvertedCollection<Club, DB.Club> clubs;
        private ConvertedCollection<Patreon, DB.Patreon> patreons;

        public Database()
        {
            database = new DB.LiteDatabase();
            Version = database.Version;

            events = new ConvertedCollection<Event, DB.Event>(database.GetCollection<DB.Event>());
            pilots = new ConvertedCollection<Pilot, DB.Pilot>(database.GetCollection<DB.Pilot>());
            channels = new ConvertedCollection<Channel, DB.Channel>(database.GetCollection<DB.Channel>());
            pilotChannels = new ConvertedCollection<PilotChannel, DB.PilotChannel>(database.GetCollection<DB.PilotChannel>());
            races = new ConvertedCollection<Race, DB.Race>(database.GetCollection<DB.Race>());
            laps = new ConvertedCollection<Lap, DB.Lap>(database.GetCollection<DB.Lap>());
            detections = new ConvertedCollection<Detection, DB.Detection>(database.GetCollection<DB.Detection>());
            rounds = new ConvertedCollection<Round, DB.Round>(database.GetCollection<DB.Round>());
            results = new ConvertedCollection<Result, DB.Result>(database.GetCollection<DB.Result>());
            clubs = new ConvertedCollection<Club, DB.Club>(database.GetCollection<DB.Club>());
            patreons = new ConvertedCollection<Patreon, DB.Patreon>(database.GetCollection<DB.Patreon>());
        }

        public void Dispose()
        {
            database?.Dispose();
            database = null;
        }


        private DB.ICollection<T> GetCollection<T>()
        {
            if (typeof(T) == typeof(Event)) return events as DB.ICollection<T>;
            if (typeof(T) == typeof(Pilot)) return pilots as DB.ICollection<T>;
            if (typeof(T) == typeof(Channel)) return channels as DB.ICollection<T>;
            if (typeof(T) == typeof(PilotChannel)) return pilotChannels as DB.ICollection<T>;
            if (typeof(T) == typeof(Race)) return races as DB.ICollection<T>;
            if (typeof(T) == typeof(Lap)) return laps as DB.ICollection<T>;
            if (typeof(T) == typeof(Detection)) return detections as DB.ICollection<T>;
            if (typeof(T) == typeof(Round)) return rounds as DB.ICollection<T>;
            if (typeof(T) == typeof(Club)) return clubs as DB.ICollection<T>;
            if (typeof(T) == typeof(Patreon)) return patreons as DB.ICollection<T>;

            return default(DB.ICollection<T>);
        }


        public bool Update<T>(T obj) where T : BaseObject
        {
            DB.ICollection<T> collection = GetCollection<T>();
            if (collection == null) 
                return false;

            return collection.Update(obj);
        }

        public int Update<T>(IEnumerable<T> objs) where T : BaseObject
        {
            DB.ICollection<T> collection = GetCollection<T>();
            if (collection == null)
                return 0;

            return collection.Update(objs);
        }

        public bool Insert<T>(T obj) where T : BaseObject
        {
            DB.ICollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

            return collection.Insert(obj);
        }

        public int Insert<T>(IEnumerable<T> objs) where T : BaseObject
        {
            DB.ICollection<T> collection = GetCollection<T>();
            if (collection == null)
                return 0;

            return collection.Insert(objs);
        }

        public bool Upsert<T>(T obj) where T : BaseObject
        {
            DB.ICollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

            return collection.Upsert(obj);
        }

        public int Upsert<T>(IEnumerable<T> objs) where T : BaseObject
        {
            DB.ICollection<T> collection = GetCollection<T>();
            if (collection == null)
                return 0;

            return collection.Upsert(objs);
        }

        public bool Delete<T>(Guid id) where T : BaseObject
        {
            DB.ICollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

            return collection.Delete(id);
        }

        public bool Delete<T>(T obj) where T : BaseObject
        {
            DB.ICollection<T> collection = GetCollection<T>();
            if (collection == null)
                return false;

            return collection.Delete(obj);
        }

        public int Delete<T>(IEnumerable<T> objs) where T : BaseObject
        {
            DB.ICollection<T> collection = GetCollection<T>();
            if (collection == null)
                return 0;

            return collection.Delete(objs);
        }

        public IEnumerable<T> All<T>() where T : BaseObject
        {
            DB.ICollection<T> collection = GetCollection<T>();
            if (collection == null)
                return default(IEnumerable<T>);

            return collection.All();
        }

        public T GetObject<T>(Guid id) where T : BaseObject
        {
            DB.ICollection<T> collection = GetCollection<T>();
            if (collection == null)
                return default(T);

            return collection.GetObject(id);
        }

        public T GetCreateObject<T>(Guid id) where T : BaseObject
        {
            DB.ICollection<T> collection = GetCollection<T>();
            if (collection == null)
                return default(T);

            return collection.GetCreateObject(id);
        }

        public T GetCreateExternalObject<T>(int id) where T : BaseObject
        {
            DB.ICollection<T> collection = GetCollection<T>();
            if (collection == null)
                return default(T);

            return collection.GetCreateExternalObject(id);
        }




        public IEnumerable<Event> GetEvents()
        {
            return database.GetEvents().Convert<Event>();
        }

        public Event LoadEvent(Guid id) 
        {
            return database.LoadEvent(id).Convert<Event>();
        }

        public IEnumerable<Race> LoadRaces(Event eve)
        {
            return database.LoadRaces(eve.ID).Convert<Race>();
        }

        public IEnumerable<Result> LoadResults(Guid id)
        {
            return database.LoadResults(id).Convert<Result>();
        }

        public Club GetDefaultClub()
        {
            return database.GetDefaultClub().Convert<Club>();   
        }

        public static void Init(DirectoryInfo data)
        {
            DB.LiteDatabase.Init(data);
        }
    }

    public class ConvertedCollection<L,D> : DB.ICollection<L> where D : DB.BaseDBObject where L : BaseObject
    {
        private DB.ICollection<D> collection;

        public ConvertedCollection(DB.ICollection<D> collection)
        {
            this.collection = collection;
        }

        public IEnumerable<L> All()
        {
            return collection.All().Convert<L>();
        }

        public bool Delete(Guid id)
        {
            return collection.Delete(id);
        }

        public bool Delete(L obj)
        {
            return collection.Delete(obj.Convert<D>());
        }

        public int Delete(IEnumerable<L> objs)
        {
            return collection.Delete(objs.Convert<D>());
        }

        public L GetCreateExternalObject(int id)
        {
            return collection.GetCreateExternalObject(id).Convert<L>();
        }

        public L GetCreateObject(Guid id)
        {
            return collection.GetCreateObject(id).Convert<L>();
        }

        public L GetObject(Guid id)
        {
            return collection.GetObject(id).Convert<L>();
        }

        public bool Insert(L obj)
        {
            return collection.Insert(obj.Convert<D>());
        }

        public int Insert(IEnumerable<L> objs)
        {
            return collection.Insert(objs.Convert<D>());
        }

        public bool Update(L obj)
        {
            return collection.Update(obj.Convert<D>());
        }

        public int Update(IEnumerable<L> objs)
        {
            return collection.Update(objs.Convert<D>());
        }

        public bool Upsert(L obj)
        {
            return collection.Upsert(obj.Convert<D>());
        }

        public int Upsert(IEnumerable<L> objs)
        {
            return collection.Upsert(objs.Convert<D>());
        }
    }

    public static class BaseObjectExt
    {
        public static T Convert<T>(this DB.BaseDBObject baseDBObject)
        {
            return DB.Converter.CreateConvertOne<DB.BaseDBObject, T>(baseDBObject);
        }

        public static IEnumerable<T> Convert<T>(this IEnumerable<DB.BaseDBObject> baseDBObjects)
        {
            return DB.Converter.CreateConvert<DB.BaseDBObject, T>(baseDBObjects);
        }

        public static T Convert<T>(this BaseObject baseDBObject)
        {
            return DB.Converter.CreateConvertOne<BaseObject, T>(baseDBObject);
        }

        public static IEnumerable<T> Convert<T>(this IEnumerable<BaseObject> baseDBObjects)
        {
            return DB.Converter.CreateConvert<BaseObject, T>(baseDBObjects);
        }
    }
}
