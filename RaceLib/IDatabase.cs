using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace RaceLib
{
    public interface IDatabase : IDisposable
    {
        int Version { get; }

        IEnumerable<SimpleEvent> GetSimpleEvents();

        void Init(Guid eventId);

        Event LoadEvent();
        IEnumerable<Race> LoadRaces();
        IEnumerable<Result> LoadResults();

        bool Insert<T>(T t) where T : BaseObject, new();
        bool Insert<T>(IEnumerable<T> t) where T : BaseObject, new();
        
        bool Update<T>(T t) where T : BaseObject, new();
        bool Update<T>(IEnumerable<T> t) where T : BaseObject, new();
        
        bool Upsert<T>(T t) where T : BaseObject, new();
        bool Upsert<T>(IEnumerable<T> t) where T : BaseObject, new();

        bool Delete<T>(T t) where T : BaseObject, new();
        bool Delete<T>(IEnumerable<T> t) where T : BaseObject, new();

        IEnumerable<T> All<T>() where T : BaseObject, new();

        T GetCreateExternalObject<T>(int externalId) where T : BaseObject, new();
        T GetCreateObject<T>(Guid id) where T : BaseObject, new();

        T GetObject<T>(Guid id) where T : BaseObject, new();
        IEnumerable<T> GetObjects<T>(IEnumerable<Guid> ids) where T : BaseObject, new();
    }

    public interface IDatabaseFactory
    {
        IDatabase Open(Guid eventId);
    }

    public static class DatabaseFactory
    {
        private static IDatabaseFactory databaseFactory;

        public static void Init(IDatabaseFactory dbf)
        {
            databaseFactory = dbf;
        }

        public static IDatabase Open()
        {
            return Open(Guid.Empty);
        }

        public static IDatabase Open(Guid eventId) 
        {
            return new DatabaseErrorWrapper(databaseFactory.Open(eventId));
        }
    }

    public class DatabaseErrorWrapper : IDatabase
    {
        private IDatabase database;

        public int Version
        {
            get
            {
                return database.Version;
            }
        }

        public DatabaseErrorWrapper(IDatabase database)
        {
            this.database = database;
        }

        public IEnumerable<T> All<T>() where T : BaseObject, new()
        {
            return database.All<T>();
        }

        public void Dispose()
        {
            database?.Dispose();
            database = null;
        }

        public T GetCreateExternalObject<T>(int externalId) where T : BaseObject, new()
        {
            return database.GetCreateExternalObject<T>(externalId);
        }

        public T GetCreateObject<T>(Guid id) where T : BaseObject, new()
        {
            return database.GetCreateObject<T>(id);
        }

        public T GetObject<T>(Guid id) where T : BaseObject, new()
        {
            return database.GetObject<T>(id);
        }

        public IEnumerable<T> GetObjects<T>(IEnumerable<Guid> ids) where T : BaseObject, new()
        {
            return database.GetObjects<T>(ids);
        }

        public IEnumerable<SimpleEvent> GetSimpleEvents()
        {
            return database.GetSimpleEvents();
        }

        public void Init(Guid eventId)
        {
            database.Init(eventId);
        }

        public Event LoadEvent()
        {
            return database.LoadEvent();
        }

        public IEnumerable<Race> LoadRaces()
        {
            return database.LoadRaces();
        }

        public IEnumerable<Result> LoadResults()
        {
            return database.LoadResults();  
        }

        private void Log<T>(T t) where T : BaseObject
        {
            Logger.RaceLog.LogStackTrace(this, t);
            try
            {
               Logger.RaceLog.Log(this, "Error writing " + typeof(T), t.ID, Logger.LogType.Error);
            }
            catch (Exception)
            {
                Logger.RaceLog.Log(this, "Error writing " + typeof(T), Logger.LogType.Error);
            }
        }

        private void Log<T>(IEnumerable<T> t) where T : BaseObject
        {
            Logger.RaceLog.LogStackTrace(this, t);
            try
            {
                Logger.RaceLog.Log(this, "Error writing " + typeof(T), string.Join(", ", t.Select(t => t.ID)), Logger.LogType.Error);
            }
            catch (Exception)
            {
                Logger.RaceLog.Log(this, "Error writing " + typeof(T) + "[]", Logger.LogType.Error);
            }
        }

        private const int RetryCount = 3;
        private const int RetryDelayMs = 200;

        private bool TryWithRetry<T>(Func<bool> operation, T t) where T : BaseObject
        {
            for (int attempt = 0; attempt < RetryCount; attempt++)
            {
                try
                {
                    if (operation())
                        return true;
                }
                catch (Exception ex)
                {
                    Logger.RaceLog.Log(this, "Database operation attempt " + (attempt + 1) + " failed for " + typeof(T) + ": " + ex.Message, Logger.LogType.Error);
                }

                if (attempt < RetryCount - 1)
                    Thread.Sleep(RetryDelayMs);
            }

            Log(t);
            return false;
        }

        private bool TryWithRetry<T>(Func<bool> operation, IEnumerable<T> t) where T : BaseObject
        {
            for (int attempt = 0; attempt < RetryCount; attempt++)
            {
                try
                {
                    if (operation())
                        return true;
                }
                catch (Exception ex)
                {
                    Logger.RaceLog.Log(this, "Database operation attempt " + (attempt + 1) + " failed for " + typeof(T) + "[]: " + ex.Message, Logger.LogType.Error);
                }

                if (attempt < RetryCount - 1)
                    Thread.Sleep(RetryDelayMs);
            }

            Log(t);
            return false;
        }

        public bool Delete<T>(T t) where T : BaseObject, new()
        {
            return TryWithRetry(() => database.Delete(t), t);
        }

        public bool Delete<T>(IEnumerable<T> t) where T : BaseObject, new()
        {
            return TryWithRetry(() => database.Delete(t), t);
        }

        public bool Insert<T>(T t) where T : BaseObject, new()
        {
            return TryWithRetry(() => database.Insert(t), t);
        }

        public bool Insert<T>(IEnumerable<T> t) where T : BaseObject, new()
        {
            return TryWithRetry(() => database.Insert(t), t);
        }

        public bool Update<T>(T t) where T : BaseObject, new()
        {
            return TryWithRetry(() => database.Update(t), t);
        }

        public bool Update<T>(IEnumerable<T> t) where T : BaseObject, new()
        {
            return TryWithRetry(() => database.Update(t), t);
        }

        public bool Upsert<T>(T t) where T : BaseObject, new()
        {
            return TryWithRetry(() => database.Upsert(t), t);
        }

        public bool Upsert<T>(IEnumerable<T> t) where T : BaseObject, new()
        {
            return TryWithRetry(() => database.Upsert(t), t);
        }
    }
}
