using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public interface IDatabase : IDisposable
    {
        int Version { get; }

        Club GetDefaultClub();
        IEnumerable<Event> GetEvents();
        Event LoadEvent(Guid eventId);
        IEnumerable<Race> LoadRaces(Guid eventId);
        IEnumerable<Result> LoadResults(Guid eventId);

        bool Insert<T>(T t) where T : BaseObject;
        int Insert<T>(IEnumerable<T> t) where T : BaseObject;
        
        bool Update<T>(T t) where T : BaseObject;
        int Update<T>(IEnumerable<T> t) where T : BaseObject;
        
        bool Upsert<T>(T t) where T : BaseObject;
        int Upsert<T>(IEnumerable<T> t) where T : BaseObject;

        bool Delete<T>(T t) where T : BaseObject;
        int Delete<T>(IEnumerable<T> t) where T : BaseObject;

        IEnumerable<T> All<T>() where T : BaseObject;

        T GetCreateExternalObject<T>(int externalId) where T : BaseObject;
        T GetCreateObject<T>(Guid id) where T : BaseObject;
    }

    public interface InterfaceDatabaseFactory
    {
        IDatabase Open();
    }

    public static class DatabaseFactory
    {
        private static InterfaceDatabaseFactory databaseFactory;

        public static void Init(InterfaceDatabaseFactory dbf)
        {
            databaseFactory = dbf;
        }

        public static IDatabase Open() 
        { 
            return databaseFactory.Open();
        }
    }
        


}
