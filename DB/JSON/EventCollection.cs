using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{

    public class EventCollection : IDatabaseCollection<Event>
    {
        private SplitJsonCollection<Event> collection;


        public EventCollection(DirectoryInfo dataDirectory)
        {
            collection = new SplitJsonCollection<Event>(dataDirectory);
        }

        public IEnumerable<Event> All()
        {
            return collection.All();
        }

        public bool Delete(Guid id)
        {
            return collection.Delete(id);
        }

        public bool Delete(Event obj)
        {
            return collection.Delete(obj);
        }

        public int Delete(IEnumerable<Event> objs)
        {
            return collection.Delete(objs);
        }

        public Event GetCreateExternalObject(int id)
        {
            return collection.GetCreateExternalObject(id);
        }

        public Event GetCreateObject(Guid id)
        {
            return collection.GetCreateObject(id);
        }

        public Event GetObject(Guid id)
        {
            return collection.GetObject(id);
        }

        public IEnumerable<Event> GetObjects(IEnumerable<Guid> ids)
        {
            return collection.GetObjects(ids);
        }

        public bool Insert(Event obj)
        {
            obj.Races = GetRaceIDs(obj).ToArray();
            return collection.Insert(obj);
        }

        public int Insert(IEnumerable<Event> objs)
        {
            Event[] events = objs.ToArray();
            foreach (Event obj in events)
            {
                obj.Races = GetRaceIDs(obj).ToArray();
            }
            return collection.Insert(events);
        }

        public bool Update(Event obj)
        {
            obj.Races = GetRaceIDs(obj).ToArray();
            return collection.Update(obj);
        }

        public int Update(IEnumerable<Event> objs)
        {
            Event[] events = objs.ToArray();
            foreach (Event obj in events)
            {
                obj.Races = GetRaceIDs(obj).ToArray();
            }

            return collection.Update(events);
        }

        public bool Upsert(Event obj)
        {
            obj.Races = GetRaceIDs(obj).ToArray();
            return collection.Upsert(obj);
        }

        public int Upsert(IEnumerable<Event> objs)
        {
            Event[] events = objs.ToArray();
            foreach (Event obj in events)
            {
                obj.Races = GetRaceIDs(obj).ToArray();
            }
            return collection.Upsert(events);
        }

        public string GetFilename(Guid id)
        {
            return collection.GetFilename(id);
        }

        private IEnumerable<Guid> GetRaceIDs(Event even)
        {
            DirectoryInfo dir = collection.GetDirectoryInfo(even.ID);
            if (dir.Exists)
            {
                foreach (DirectoryInfo raceDir in dir.EnumerateDirectories())
                {
                    Guid output;
                    if (Guid.TryParse(raceDir.Name, out output))
                    {
                        yield return output;
                    }
                }
            }
        }
    }
}
