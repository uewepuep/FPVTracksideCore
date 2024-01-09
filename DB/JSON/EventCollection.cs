using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{

    public class EventCollection : IDatabaseCollection<Event>
    {
        private SplitDirJsonCollection<Event> collection;



        public EventCollection(DirectoryInfo dataDirectory)
        {
            collection = new SplitDirJsonCollection<Event>(dataDirectory);
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
            foreach (Event obj in objs)
            {
                obj.Races = GetRaceIDs(obj).ToArray();
            }
            return collection.Insert(objs);
        }

        public bool Update(Event obj)
        {
            obj.Races = GetRaceIDs(obj).ToArray();
            return collection.Update(obj);
        }

        public int Update(IEnumerable<Event> objs)
        {
            foreach (Event obj in objs)
            {
                obj.Races = GetRaceIDs(obj).ToArray();
            }

            return collection.Update(objs);
        }

        public bool Upsert(Event obj)
        {
            obj.Races = GetRaceIDs(obj).ToArray();
            return collection.Upsert(obj);
        }

        public int Upsert(IEnumerable<Event> objs)
        {
            foreach (Event obj in objs)
            {
                obj.Races = GetRaceIDs(obj).ToArray();
            }
            return collection.Upsert(objs);
        }


        private IEnumerable<Guid> GetRaceIDs(Event even)
        {
            DirectoryInfo dir = collection.GetDirectoryInfo(even.ID);
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
