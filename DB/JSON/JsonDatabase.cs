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

        private Guid eventId;

        public JsonDatabase()
        {
            dataDirectory = new DirectoryInfo("events");
            events = new EventCollection(dataDirectory);
            patreons = new JsonCollection<Patreon>(dataDirectory);
            clubs = new JsonCollection<Club>(dataDirectory);
            channels = new Channels();
        }


        public void Init(Guid eventId)
        {
            this.eventId = eventId;
            if (eventId != Guid.Empty)
            {
                DirectoryInfo eventDirectory = new DirectoryInfo(Path.Combine(dataDirectory.FullName, eventId.ToString()));
                rounds = new JsonCollection<Round>(eventDirectory);
                pilots = new JsonCollection<Pilot>(eventDirectory);

                races = new SplitDirJsonCollection<Race>(eventDirectory);
                results = new ResultCollection(eventDirectory);
            }
        }


        public void Dispose()
        {
        }

        public IDatabaseCollection<T> GetCollection<T>() where T : RaceLib.BaseObject, new()
        {
            if (typeof(T) == typeof(RaceLib.Patreon)) return new ConvertedCollection<RaceLib.Patreon, Patreon>(patreons, null) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Event)) return new ConvertedCollection<RaceLib.Event, Event>(events, this) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Club)) return new ConvertedCollection<RaceLib.Club, Club>(clubs, this) as IDatabaseCollection<T>;

            if (typeof(T) == typeof(RaceLib.Pilot)) return new ConvertedCollection<RaceLib.Pilot, Pilot>(pilots, this) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Channel)) return new ConvertedCollection<RaceLib.Channel, Channel>(channels, this) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Race)) return new ConvertedCollection<RaceLib.Race, Race>(races, this) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Round)) return new ConvertedCollection<RaceLib.Round, Round>(rounds, this) as IDatabaseCollection<T>;
            if (typeof(T) == typeof(RaceLib.Result)) return new ConvertedCollection<RaceLib.Result, Result>(results, this) as IDatabaseCollection<T>;

            if (typeof(T) == typeof(RaceLib.Detection)) return new ConvertedCollection<RaceLib.Detection, Detection>(new DetectionCollection(races), this) as IDatabaseCollection<T>;
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
                yield return even.GetSimpleRaceLibEvent(this);
            }
        }

        RaceLib.Event ICollectionDatabase.LoadEvent()
        {
            return events.GetObject(eventId).Convert(this);
        }

        IEnumerable<RaceLib.Race> ICollectionDatabase.LoadRaces()
        {
            return races.All().Convert(this);
        }

        IEnumerable<RaceLib.Result> ICollectionDatabase.LoadResults()
        {
            return results.All().Convert(this);
        }
    }

}
