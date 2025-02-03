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
            return true;
        }

        public bool Delete(Detection obj)
        {
            return true;
        }

        public bool Delete(IEnumerable<Detection> objs)
        {
            return true;
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
            return true;
        }

        public bool Insert(IEnumerable<Detection> objs)
        {
            return true;
        }

        public bool Update(Detection obj)
        {
            return true;
        }

        public bool Update(IEnumerable<Detection> objs)
        {
            return true;
        }

        public bool Upsert(Detection obj)
        {
            return true;
        }

        public bool Upsert(IEnumerable<Detection> objs)
        {
            return true;
        }
    }
}
