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
        private DB.LiteDatabase database;

        public int Version { get; private set; }

        public Database()
        {
            database = new DB.LiteDatabase();
            Version = database.Version;
        }

        public void Dispose()
        {
            database?.Dispose();
            database = null;
        }

        public void Update<T>(T obj) where T : BaseObject
        {

        }

        public void Update<T>(IEnumerable<T> objs) where T : BaseObject
        {

        }

        public void Insert<T>(T obj) where T : BaseObject
        {

        }

        public void Insert<T>(IEnumerable<T> objs) where T : BaseObject
        {

        }

        public void Upsert<T>(T obj) where T : BaseObject
        {

        }

        public void Upsert<T>(IEnumerable<T> objs) where T : BaseObject
        {

        }
        public void Delete<T>(Guid id)
        {
        }

        public void Delete<T>(T obj) 
        { 
        }

        public void Delete<T>(IEnumerable<T> objs)
        {
        }

        public IEnumerable<T> Find<T>() where T : BaseObject
        {
            yield break;
        }

        public T GetObject<T>(Guid id)
        {
            return default(T);
        }

        public T GetCreateObject<T>(Guid id)
        {
            return default(T);
        }

        public T GetCreateRemoteObject<T>(int id)
        {
            return default(T);
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
            throw new NotImplementedException();
        }

        public static void Init(DirectoryInfo data)
        {
            DB.LiteDatabase.Init(data);
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
    }
}
