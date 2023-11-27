using Newtonsoft.Json;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class JsonDatabase : ICollectionDatabase
    {
        public int Version
        {
            get
            {
                return 1;
            }
        }

        private DirectoryInfo dataDirectory;

        
        private IDatabaseCollection<Pilot> pilots;
        private IDatabaseCollection<Patreon> patreons;
        private IDatabaseCollection<Club> clubs;
        private IDatabaseCollection<Channel> channels;


        private IDatabaseCollection<Event> events;
        private IDatabaseCollection<Round> rounds;

        private IDatabaseCollection<Race> races;
        private IDatabaseCollection<Result> results;

        private IDatabase database;

        private static Guid eventId;

        public JsonDatabase(IDatabase database)
        {
            this.database = database;
            dataDirectory = new DirectoryInfo("events");
            Init();
        }


        private void Init()
        {
            events = new SplitDirJsonCollection<Event>(dataDirectory);
            patreons = new JsonCollection<Patreon>(dataDirectory);
            clubs = new JsonCollection<Club>(dataDirectory);

            if (eventId != Guid.Empty)
            {
                DirectoryInfo eventDirectory = new DirectoryInfo(Path.Combine(dataDirectory.FullName, eventId.ToString()));
                rounds = new JsonCollection<Round>(eventDirectory);
                pilots = new JsonCollection<Pilot>(eventDirectory);

                races = new SplitDirJsonCollection<Race>(eventDirectory);
                results = new ResultCollection(eventDirectory);
            }

            channels = new Channels();
        }


        public void Dispose()
        {
        }

        public IDatabaseCollection<T> GetCollection<T>() where T : RaceLib.BaseObject, new()
        {
            if (typeof(T) == typeof(RaceLib.Patreon)) return new ConvertedCollection<RaceLib.Patreon, Patreon>(patreons, null) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Event)) return new ConvertedCollection<RaceLib.Event, Event>(events, database) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Club)) return new ConvertedCollection<RaceLib.Club, Club>(clubs, database) as IDatabaseCollection<T>;

            if (typeof(T) == typeof(RaceLib.Pilot)) return new ConvertedCollection<RaceLib.Pilot, Pilot>(pilots, database) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Channel)) return new ConvertedCollection<RaceLib.Channel, Channel>(channels, database) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Race)) return new ConvertedCollection<RaceLib.Race, Race>(races, database) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Round)) return new ConvertedCollection<RaceLib.Round, Round>(rounds, database) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Result)) return new ConvertedCollection<RaceLib.Result, Result>(results, database) as IDatabaseCollection<T>;

            if (typeof(T) == typeof(RaceLib.Detection)) return new ConvertedCollection<RaceLib.Detection, Detection>(new DetectionCollection(races), database) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.PilotChannel)) return new DummyCollection<T>();
            if (typeof(T) == typeof(RaceLib.Lap)) return new DummyCollection<T>();

            throw new NotImplementedException();
        }

        public T GetObject<T>(Guid id) where T : RaceLib.BaseObject, new()
        {
            return GetCollection<T>().GetObject(id);
        }

        IEnumerable<RaceLib.Event> ICollectionDatabase.GetEvents()
        {
            foreach (Event even in events.All())
            {
                yield return even.GetSimpleRaceLibEvent(database);
            }
        }

        RaceLib.Event ICollectionDatabase.LoadEvent(Guid id)
        {
            eventId = id;
            Init();

            return events.GetObject(id).Convert(database);
        }

        IEnumerable<RaceLib.Race> ICollectionDatabase.LoadRaces(Guid eventId)
        {
            return races.All().Where(r => r.Event == eventId).Convert(database);
        }

        IEnumerable<RaceLib.Result> ICollectionDatabase.LoadResults(Guid eventId)
        {
            return results.All().Where(r => r.Event == eventId).Convert(database);
        }
    }

}
