using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class DetectionCollection : IDatabaseCollection<Detection>
    {
        private IDatabaseCollection<Race> raceCollection;

        public DetectionCollection(IDatabaseCollection<Race> raceCollection) 
        { 
            this.raceCollection = raceCollection;
        }

        public IEnumerable<Detection> All()
        {
            return raceCollection.All().Where(r => r.Detections != null).SelectMany(r => r.Detections);
        }

        public bool Delete(Guid id)
        {
            return false;
        }

        public bool Delete(Detection obj)
        {
            return false;
        }

        public int Delete(IEnumerable<Detection> objs)
        {
            return 0;
        }

        public Detection GetCreateExternalObject(int id)
        {
            throw new NotImplementedException();
        }

        public Detection GetCreateObject(Guid id)
        {
            throw new NotImplementedException();
        }

        public Detection GetObject(Guid id)
        {
            return All().FirstOrDefault(d => d.ID == id);
        }

        public IEnumerable<Detection> GetObjects(IEnumerable<Guid> ids)
        {
            return All().Where(d => ids.Contains(d.ID));
        }

        public bool Insert(Detection obj)
        {
            return false;
        }

        public int Insert(IEnumerable<Detection> objs)
        {
            return 0;
        }

        public bool Update(Detection obj)
        {
            return false;
        }

        public int Update(IEnumerable<Detection> objs)
        {
            return 0;
        }

        public bool Upsert(Detection obj)
        {
            return false;
        }

        public int Upsert(IEnumerable<Detection> objs)
        {
            return 0;
        }
    }
}
