using Newtonsoft.Json;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class JsonDatabase : IDisposable
    {
        public int Version
        {
            get
            {
                return 1;
            }
        }

        public DirectoryInfo DataDirectory { get; private set; }
        
        public JsonCollection<Pilot> Pilots { get; private set; }
        public JsonCollection<Patreon> Patreons { get; private set; }
        public JsonCollection<Club> Clubs { get; private set; }
        public ChannelCollections Channels { get; private set; }


        public EventCollection Events { get; private set; }
        public JsonCollection<Round> Rounds { get; private set; }

        public SplitDirJsonCollection<Race> Races { get; private set; }
        public ResultCollection Results { get; private set; }

        public Guid EventId { get; private set; }

        public JsonDatabase()
        {
            DataDirectory = new DirectoryInfo("events");
            Events = new EventCollection(DataDirectory);
            Patreons = new JsonCollection<Patreon>(DataDirectory);
            Clubs = new JsonCollection<Club>(DataDirectory);
            Channels = new ChannelCollections();
        }

        public JsonDatabase(Guid eventId)
            :this()
        {
            Init(eventId);
        }

        public void Init(Guid eventId)
        {
            this.EventId = eventId;
            if (eventId != Guid.Empty)
            {
                DirectoryInfo eventDirectory = new DirectoryInfo(Path.Combine(DataDirectory.FullName, eventId.ToString()));
                Rounds = new JsonCollection<Round>(eventDirectory);
                Pilots = new JsonCollection<Pilot>(eventDirectory);

                Races = new SplitDirJsonCollection<Race>(eventDirectory);
                Results = new ResultCollection(eventDirectory);
            }
        }
        public void Dispose()
        {
        }
    }


    public class JSONDatabaseConverted : JsonDatabase, ICollectionDatabase
    {
        public JSONDatabaseConverted() 
            :base()
        { 
        }
        

        public IDatabaseCollection<T> GetCollection<T>() where T : RaceLib.BaseObject, new()
        {
            if (typeof(T) == typeof(RaceLib.Patreon))
                return new ConvertedCollection<RaceLib.Patreon, Patreon>(Patreons, null) as IDatabaseCollection<T>;

            if (typeof(T) == typeof(RaceLib.Event))
                return new ConvertedCollection<RaceLib.Event, Event>(Events, this) as IDatabaseCollection<T>;

            if (typeof(T) == typeof(RaceLib.Club))
                return new ConvertedCollection<RaceLib.Club, Club>(Clubs, this) as IDatabaseCollection<T>;

            if (typeof(T) == typeof(RaceLib.Pilot))
                return new ConvertedCollection<RaceLib.Pilot, Pilot>(Pilots, this) as IDatabaseCollection<T>;

            if (typeof(T) == typeof(RaceLib.Channel))
                return new ConvertedCollection<RaceLib.Channel, Channel>(Channels, this) as IDatabaseCollection<T>;

            if (typeof(T) == typeof(RaceLib.Race))
                return new ConvertedCollection<RaceLib.Race, Race>(Races, this) as IDatabaseCollection<T>;

            if (typeof(T) == typeof(RaceLib.Round))
                return new ConvertedCollection<RaceLib.Round, Round>(Rounds, this) as IDatabaseCollection<T>;

            if (typeof(T) == typeof(RaceLib.Result))
                return new ConvertedCollection<RaceLib.Result, Result>(Results, this) as IDatabaseCollection<T>;

            if (typeof(T) == typeof(RaceLib.Detection))
                return new ConvertedCollection<RaceLib.Detection, Detection>(new DetectionCollection(Races), this) as IDatabaseCollection<T>;

            if (typeof(T) == typeof(RaceLib.PilotChannel))
                return new DummyCollection<T>();

            if (typeof(T) == typeof(RaceLib.Lap))
                return new DummyCollection<T>();

            throw new NotImplementedException();
        }

        public T GetObject<T>(Guid id) where T : RaceLib.BaseObject, new()
        {
            return GetCollection<T>().GetObject(id);
        }

        IEnumerable<RaceLib.Event> ICollectionDatabase.GetEvents()
        {
            foreach (Event even in Events.All())
            {
                yield return even.GetSimpleRaceLibEvent(this);
            }
        }

        RaceLib.Event ICollectionDatabase.LoadEvent()
        {
            return Events.GetObject(EventId).Convert(this);
        }

        IEnumerable<RaceLib.Race> ICollectionDatabase.LoadRaces()
        {
            return Races.All().Convert(this);
        }

        IEnumerable<RaceLib.Result> ICollectionDatabase.LoadResults()
        {
            return Results.All().Convert(this);
        }
    }
}
