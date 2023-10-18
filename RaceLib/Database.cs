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

namespace RaceLib
{
    public class Database : IDisposable
    {
        private LiteDatabase database;

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
        }


        public Database()
            :this(directory + @"/event.db")
        {
        }

        public Database(string path)
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
                database = new LiteDatabase(connectionString);
            }
            else
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Directory.Exists)
                {
                    fi.Directory.Create();
                }
                hasInitialised = true;

                database = new LiteDatabase(connectionString + "\" Upgrade=\"true\"");

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

        ~Database()
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
            System.IO.File.Move(path, oldName);
            database = new LiteDatabase(path);
        }

        private bool UpgradeData(int oldVersion, int newVersion)
        {
            try
            {
                if (oldVersion == 0)
                {
                    MoveAway();
                    return false;
                }

                Event[] events = Events.FindAll().ToArray();
                Pilot[] ps = Pilots.FindAll().ToArray();
                Lap[] ls = Laps.FindAll().ToArray();
                Race[] races = Races.FindAll().ToArray();
                Channel[] cs = Channels.FindAll().ToArray();
                Detection[] ds = Detections.FindAll().ToArray();
                Round[] rns = Rounds.FindAll().ToArray();
                Result[] results = Results.FindAll().ToArray();
                Club[] clubs = Clubs.FindAll().ToArray();
                PilotChannel[] pilotChannels = PilotChannels.FindAll().ToArray();

                if (oldVersion < 9)
                {
                    List<PilotChannel> wholeList = new List<PilotChannel>();
                    BsonValue[] reader = database.Execute("SELECT $ FROM Event;").ToEnumerable().ToArray();
                    foreach (BsonValue bevent in reader)
                    {
                        Event even = events.FirstOrDefault(e => e.ID == bevent["_id"].AsGuid);

                        List<PilotChannel> eventPilotChannels = CreatePilotChannels(bevent).ToList();
                        even.PilotChannels = eventPilotChannels;
                        wholeList.AddRange(eventPilotChannels);
                    }

                    reader = database.Execute("SELECT $ FROM Race;").ToEnumerable().ToArray();
                    foreach (BsonValue brace in reader)
                    {
                        Race race = races.FirstOrDefault(e => e.ID == brace["_id"].AsGuid);

                        List<PilotChannel> eventPilotChannels = CreatePilotChannels(brace).ToList();
                        race.PilotChannels = eventPilotChannels;
                        wholeList.AddRange(eventPilotChannels);
                    }

                    pilotChannels = wholeList.ToArray();

                    var oldTableName = new ModifiedLiteCollection<Result>(database, "Results");
                    results = Results.FindAll().ToArray();

                    Channel[] channels = Channel.Read(new Tools.Profile("Profile 1"));
                    foreach (Event eve in events)
                    {
                        if (eve.Channels == null)
                        {
                            eve.Channels = channels;
                        }
                    }
                }

                string oldName = path + database.UserVersion + "_" + Guid.NewGuid().ToString().Substring(0, 6);
                database.Dispose();
                System.IO.File.Move(path, oldName);
                database = new LiteDatabase(path);

                PilotChannels.Insert(pilotChannels);
                Events.Insert(events);
                Pilots.Insert(ps);
                Laps.Insert(ls);
                Races.Insert(races);
                Channels.Insert(cs);
                Detections.Insert(ds);
                Rounds.Insert(rns);
                Results.Insert(results);
                Clubs.Insert(clubs);

                try
                {
                    if (oldVersion < 10)
                    {
                        ResultManager pp = new ResultManager(null);

                        IEnumerable<Race> toMigrate = Races
                                                        .Include(r => r.PilotChannels)
                                                        .Include(r => r.PilotChannels.Select(pc => pc.Pilot))
                                                        .Include(r => r.PilotChannels.Select(pc => pc.Channel))
                                                        .Include(r => r.Laps)
                                                        .Include(r => r.Detections)
                                                        .Include(r => r.Detections.Select(d => d.Pilot))
                                                        .Include(r => r.Round)
                                                        .Include(r => r.Event)
                                                        .FindAll().ToArray();
                        foreach (Race race in toMigrate)
                        {
                            Result[] raceResults = Results.Find(r => r.Race.ID == race.ID).ToArray();
                            Results.Delete(raceResults);

                            foreach (Lap lap in race.Laps)
                            {
                                // When loading, we need to set the race object reference onto each Lap for performance quick access reasons.
                                lap.Race = race;

                                // Same with detections...
                                if (lap.Detection != null)
                                {
                                    Detection d = race.Detections.FirstOrDefault(da => da.ID == lap.Detection.ID);
                                    if (d != null)
                                    {
                                        lap.Detection = d;
                                    }
                                }
                            }

                            pp.SaveResults(this, race);
                        }
                    }
                }
                catch (Exception)
                {
                    // not that vital..
                }

                return true;
            }
            catch (Exception)
            {
                MoveAway();
                return false;
            }
        }


        private void InitTables()
        {
            if (Channels.Count() != Channel.AllChannels.Count())
            {
                foreach (Channel c in Channel.AllChannels)
                {
                    Channels.Upsert(c);
                }
            }

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

        private IEnumerable<PilotChannel> CreatePilotChannels(BsonValue obj)
        {
            BsonArray pilots = obj["Pilots"].AsArray;
            BsonArray channels = obj["PilotChannels"].AsArray;

            if (channels == null)
            {
                channels = obj["Channels"].AsArray;
            }

            int length = Math.Min(pilots.Count, channels.Count);
            for (int i = 0; i < length; i++)
            {
                BsonValue pilotrow = pilots[i];
                BsonValue channelrow = channels[i];

                Pilot p = Pilots.GetObject(pilotrow["$id"].AsGuid);
                Channel c = Channels.GetObject(channelrow["$id"].AsGuid);

                PilotChannel pc = new PilotChannel(p, c);
                yield return pc;
            }
        }

        public Club GetDefaultClub()
        {
            Club club;

            club = Clubs.FindAll().OrderByDescending(r => r.SyncWith == SyncWith.FPVTrackside).FirstOrDefault();
            
            if (club == null)
            {
                club = new Club();
                club.SyncWith = SyncWith.FPVTrackside;
                Clubs.Insert(club);
            }
            return club;
        }

        public Event GetCreateEvent(string eventName)
        {
            Event aevent = Events
                .Include(e => e.Channels)
                .Include(e => e.PilotChannels)
                .Include(e => e.PilotChannels.Select(pc => pc.Pilot))
                .Include(e => e.PilotChannels.Select(pc => pc.Channel))
                .FindAll()
                .Where(r => r.Name == eventName)
                .FirstOrDefault();

            if (aevent != null)
                return aevent;

            aevent = new Event();
            aevent.ID = Guid.NewGuid();
            aevent.Name = eventName;

            Events.Insert(aevent);
            return aevent;
        }
    }
    
    public class ModifiedLiteCollection<T> where T : BaseDBObject
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
        private LiteDatabase database;
        private string name;

        public ModifiedLiteCollection(LiteDatabase database, string name)
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

        public IEnumerable<T> FindAll() 
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

        public T GetCreateObject(Guid id)
        {
            return collection.GetCreateObject(id);
        }

        public T GetCreateObject(int externalId)
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

        public BsonValue Insert(T doc)
        {
            doc.Modified = DateTime.Now;
            return collection.Insert(doc);
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

        //public IEnumerable<T> GetRaw()
        //{
        //    BsonValue[] table = database.Execute("SELECT $ FROM " + name +";").ToEnumerable().ToArray();
        //    foreach (BsonValue row in table)
        //    {
        //        T t = Activator.CreateInstance<T>();

        //        foreach (PropertyInfo pi in typeof(T).GetProperties())
        //        {
        //            BsonValue bsonValue = row[pi.Name];
        //        }

        //        yield return t;
        //    }
        //}
    }

    public static class CollectionExt
    {
        public static T GetCreateObject<T>(this ILiteCollection<T> collection, Guid Id) where T : BaseDBObject
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

        public static T GetCreateObject<T>(this ILiteCollection<T> collection, int externalId) where T : BaseDBObject
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
}
