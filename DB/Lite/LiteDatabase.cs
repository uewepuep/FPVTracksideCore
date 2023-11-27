using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using LiteDB;
using System.Threading;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Channels;
using static System.Reflection.Metadata.BlobBuilder;
using System.Collections;
using RaceLib;

namespace DB.Lite
{
    public class LiteDatabase : ICollectionDatabase
    {
        private LiteDB.LiteDatabase database;

        private static ushort version;
        private string path;

        private static bool hasInitialised;
        private static AutoResetEvent mutex;
        private static Thread mutexThread;
        private bool hasMutex;

        private static int instances = 0;
        private static int waiting = 0;

        public ModifiedLiteCollection<Event> Events { get { return new ModifiedLiteCollection<Event>(database, "Event"); } }
        public ModifiedLiteCollection<Pilot> Pilots { get { return new ModifiedLiteCollection<Pilot>(database, "Pilot"); } }
        public ModifiedLiteCollection<Channel> Channels { get { return new ModifiedLiteCollection<Channel>(database, "Channel"); } }
        public ModifiedLiteCollection<PilotChannel> PilotChannels { get { return new ModifiedLiteCollection<PilotChannel>(database, "PilotChannel"); } }

        public ModifiedLiteCollection<Race> Races { get { return new ModifiedLiteCollection<Race>(database, "Race"); } }
        public ModifiedLiteCollection<Lap> Laps { get { return new ModifiedLiteCollection<Lap>(database, "Lap"); } }
        public ModifiedLiteCollection<Detection> Detections { get { return new ModifiedLiteCollection<Detection>(database, "Detection"); } }
        public ModifiedLiteCollection<Round> Rounds { get { return new ModifiedLiteCollection<Round>(database, "Round"); } }
        public ModifiedLiteCollection<Result> Results { get { return new ModifiedLiteCollection<Result>(database, "Result"); } }

        public ModifiedLiteCollection<Club> Clubs { get { return new ModifiedLiteCollection<Club>(database, "Club"); } }

        public ModifiedLiteCollection<Patreon> Patreons { get { return new ModifiedLiteCollection<Patreon>(database, "Patreon"); } }

        public int Version { get { return database.UserVersion; } }

        private static string directory;

        public static void Init(DirectoryInfo directoryInfo)
        {
            directory = directoryInfo.FullName;

            using (LiteDatabase db = new LiteDatabase())
            {
                if (!db.Channels.All().Any())
                {
                    db.Channels.Insert(RaceLib.Channel.AllChannels.Convert<Channel>());
                }
            }
        }

        public LiteDatabase()
            : this(directory + @"/event.db")
        {
        }

        public LiteDatabase(string path)
        {
            if (mutex == null)
                mutex = new AutoResetEvent(true);

            waiting++;
            if (!mutex.WaitOne(10000))
            {
#if DEBUG
                throw new Exception("Database wait timeout");
#endif
            }
            waiting--;

            instances++;
            hasMutex = true;
            mutexThread = Thread.CurrentThread;

            this.path = path;

            version = 10;

            string connectionString = "Filename=\"" + path;
            if (hasInitialised)
            {
                database = new LiteDB.LiteDatabase(connectionString);
            }
            else
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Directory.Exists)
                {
                    fi.Directory.Create();
                }
                hasInitialised = true;

                database = new LiteDB.LiteDatabase(connectionString + "\" Upgrade=\"true\"");

                if (database.UserVersion != version && Events.Count() > 0)
                {
                    if (CanUpgrade(database.UserVersion, version))
                    {
                        UpgradeData(database.UserVersion, version);
                    }
                    else
                    {
                        MoveAway();
                    }
                }
                database.UserVersion = version;
                InitTables();
            }
        }

        ~LiteDatabase()
        {
            if (hasMutex)
            {
                Dispose();
            }
        }


        public void Dispose()
        {
            hasMutex = false;
            instances--;

            if (database != null)
            {
                database.Dispose();
                database = null;
                mutex.Set();
            }
        }

        private bool CanUpgrade(int oldVersion, int newVersion)
        {
            return true;
        }

        private void MoveAway()
        {
            string oldName = path + database.UserVersion + "_" + Guid.NewGuid().ToString().Substring(0, 6);
            database.Dispose();
            File.Move(path, oldName);
            database = new LiteDB.LiteDatabase(path);
        }

        private bool UpgradeData(int oldVersion, int newVersion)
        {
            return true;
        }

        private void InitTables()
        {
            try
            {
                Laps.EnsureIndex("Pilot");
                Pilots.EnsureIndex("Event");
                Races.EnsureIndex("Event");

                Results.EnsureIndex("Pilot");
                Results.EnsureIndex("Event");
                Results.EnsureIndex("Race");
                Results.EnsureIndex("Round");


                Events.EnsureIndex("ExternalID");
                Pilots.EnsureIndex("ExternalID");
                Laps.EnsureIndex("ExternalID");
                Races.EnsureIndex("ExternalID");
                Channels.EnsureIndex("ExternalID");
                Detections.EnsureIndex("ExternalID");
                Rounds.EnsureIndex("ExternalID");
                Results.EnsureIndex("ExternalID");
                Clubs.EnsureIndex("ExternalID");
            }
            catch
            {
            }
        }

        public IDatabaseCollection<T> GetCollection<T>() where T : BaseObject, new()
        {
            if (typeof(T) == typeof(RaceLib.Event)) return new ConvertedCollection<RaceLib.Event, Event>(Events, null) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Pilot)) return new ConvertedCollection<RaceLib.Pilot, Pilot>(Pilots, null) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Channel)) return new ConvertedCollection<RaceLib.Channel, Channel>(Channels, null) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.PilotChannel)) return new ConvertedCollection<RaceLib.PilotChannel, PilotChannel>(PilotChannels, null) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Race)) return new ConvertedCollection<RaceLib.Race, Race>(Races, null) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Lap)) return new ConvertedCollection<RaceLib.Lap, Lap>(Laps, null) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Detection)) return new ConvertedCollection<RaceLib.Detection, Detection>(Detections, null) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Round)) return new ConvertedCollection<RaceLib.Round, Round>(Rounds, null) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Club)) return new ConvertedCollection<RaceLib.Club, Club>(Clubs, null) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Patreon)) return new ConvertedCollection<RaceLib.Patreon, Patreon>(Patreons, null) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Result)) return new ConvertedCollection<RaceLib.Result, Result>(Results, null) as IDatabaseCollection<T>;

            throw new NotImplementedException();
        }

        public Club GetDefaultClub()
        {
            Club club;

            club = Clubs.All().OrderByDescending(r => r.SyncWith == "FPVTrackside").FirstOrDefault();

            if (club == null)
            {
                club = new Club();
                club.SyncWith = "FPVTrackside";
                Clubs.Insert(club);
            }
            return club;
        }

        IEnumerable<RaceLib.Event> ICollectionDatabase.GetEvents()
        {
            var events = Events.Include(e => e.Channels)
                         .Include(e => e.Club)
                         .Include(e => e.PilotChannels)
                         .Include(e => e.PilotChannels.Select(p => p.Pilot))
                         .FindAll()
                         .OrderBy(e => e.Name).ToArray();
            return events.Convert(null);
        }

        RaceLib.Event ICollectionDatabase.LoadEvent(Guid id)
        {
            Event eve = Events
                  .Include(e => e.PilotChannels)
                  .Include(e => e.PilotChannels.Select(pc => pc.Pilot))
                  .Include(e => e.PilotChannels.Select(pc => pc.Channel))
                  .Include(e => e.RemovedPilots)
                  .Include(e => e.Rounds)
                  .Include(e => e.Club)
                  .Include(e => e.Channels)
                  .FindById(id);
            return eve.Convert(null);
        }

        IEnumerable<RaceLib.Race> ICollectionDatabase.LoadRaces(Guid eventId)
        {
            return Races.Include(r => r.PilotChannels)
                        .Include(r => r.PilotChannels.Select(pc => pc.Pilot))
                        .Include(r => r.PilotChannels.Select(pc => pc.Channel))
                        .Include(r => r.Laps)
                        .Include(r => r.Detections)
                        .Include(r => r.Detections.Select(d => d.Pilot))
                        .Include(r => r.Round)
                        .Include(r => r.Event)
                        .Find(r => r.Event.ID == eventId && r.Valid).OrderBy(r => r.Creation).Convert(null);
        }

        IEnumerable<RaceLib.Result> ICollectionDatabase.LoadResults(Guid eventId)
        {

            return Results.Include(r => r.Event)
                          .Include(r => r.Pilot)
                          .Include(r => r.Race)
                          .Include(r => r.Round)
                          .Find(r => r.Event.ID == eventId).OrderBy(r => r.Creation).Convert(null);
        }
    }

    public class ModifiedLiteCollection<T> : IDatabaseCollection<T> where T : DatabaseObject
    {
        private ILiteCollection<T> _collection;
        private ILiteCollection<T> collection
        {
            get
            {
                if (_collection == null)
                {
                    _collection = database.GetCollection<T>(name);
                }
                return _collection;
            }
        }
        private LiteDB.LiteDatabase database;
        private string name;

        public ModifiedLiteCollection(LiteDB.LiteDatabase database, string name)
        {
            this.database = database;
            this.name = name;
        }

        public int Count()
        {
            return collection.Count();
        }

        public bool EnsureIndex(string field, bool unique = false)
        {
            return collection.EnsureIndex(field, unique);
        }

        public bool Delete(Guid id)
        {
            return collection.Delete(id);
        }

        public bool Delete(T obj)
        {
            return collection.Delete(obj.ID);
        }

        public int Delete(IEnumerable<T> objects)
        {
            int i = 0;
            foreach (var obj in objects)
            {
                if (collection.Delete(obj.ID))
                {
                    i++;
                }
            }

            return i;
        }

        public IEnumerable<T> All()
        {
            return collection.FindAll();
        }

        public IEnumerable<T> Find(Expression<Func<T, bool>> predicate)
        {
            return collection.Find(predicate);
        }

        public ILiteCollection<T> Include<K>(Expression<Func<T, K>> predicate)
        {
            return collection.Include(predicate);
        }

        public T GetObject(Guid Id)
        {
            return collection.FindById(Id);
        }

        public T GetObject(T t)
        {
            return collection.FindById(t.ID);
        }

        public IEnumerable<T> GetObjects(IEnumerable<T> t)
        {
            Guid[] ids = t.Select(i => i.ID).ToArray();
            return Find(a => ids.Contains(a.ID));
        }

        public IEnumerable<T> GetObjects(IEnumerable<Guid> ids)
        {
            return Find(a => ids.Contains(a.ID));
        }

        public T GetCreateObject(Guid id)
        {
            return collection.GetCreateObject(id);
        }

        public T GetCreateExternalObject(int externalId)
        {
            return collection.GetCreateObject(externalId);
        }

        public int Insert(IEnumerable<T> documents)
        {
            foreach (T doc in documents)
            {
                doc.Modified = DateTime.Now;
            }
            return collection.Insert(documents);
        }

        public bool Insert(T doc)
        {
            doc.Modified = DateTime.Now;
            return collection.Insert(doc) != null;
        }

        public bool Update(T doc)
        {
            doc.Modified = DateTime.Now;
            return collection.Update(doc);
        }

        public int Update(IEnumerable<T> documents)
        {
            foreach (T doc in documents)
            {
                doc.Modified = DateTime.Now;
            }
            return collection.Update(documents);
        }

        public bool Upsert(T doc)
        {
            doc.Modified = DateTime.Now;
            return collection.Upsert(doc);
        }

        public int Upsert(IEnumerable<T> documents)
        {
            foreach (T doc in documents)
            {
                doc.Modified = DateTime.Now;
            }
            return collection.Upsert(documents);
        }
    }

    public static class CollectionExt
    {
        public static T GetCreateObject<T>(this ILiteCollection<T> collection, Guid Id) where T : DatabaseObject
        {
            T t = collection.FindById(Id);
            if (t == null)
            {
                t = Activator.CreateInstance<T>();
                t.ID = Id;
                collection.Insert(t);
            }

            return t;
        }

        public static T GetCreateObject<T>(this ILiteCollection<T> collection, int externalId) where T : DatabaseObject
        {
            T t = collection.FindOne(d => d.ExternalID == externalId);
            if (t == null)
            {
                t = Activator.CreateInstance<T>();
                t.ExternalID = externalId;
                t.ID = Guid.NewGuid();
                collection.Insert(t);
            }
            return t;
        }
    }
    public class DBRefAttribute : BsonRefAttribute
    {
        public DBRefAttribute(string collection)
        : base(collection)
        { }
    }
}
