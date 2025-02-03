using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class DummyCollection<T> : IDatabaseCollection<T>
    {
        public IEnumerable<T> All()
        {
            return Enumerable.Empty<T>();
        }

        public bool Delete(Guid id)
        {
            return true;
        }

        public bool Delete(T obj)
        {
            return true;
        }

        public bool Delete(IEnumerable<T> objs)
        {
            return true;
        }

        public T GetCreateExternalObject(int id)
        {
            throw new NotImplementedException();
        }

        public T GetCreateObject(Guid id)
        {
            throw new NotImplementedException();
        }

        public T GetObject(Guid id)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<T> GetObjects(IEnumerable<Guid> ids)
        {
            throw new NotImplementedException();
        }

        public bool Insert(T obj)
        {
            return true;
        }

        public bool Insert(IEnumerable<T> objs)
        {
            return true;
        }

        public bool Update(T obj)
        {
            return true;
        }

        public bool Update(IEnumerable<T> objs)
        {
            return true;
        }

        public bool Upsert(T obj)
        {
            return true;
        }

        public bool Upsert(IEnumerable<T> objs)
        {
            return true;
        }
    }
}
