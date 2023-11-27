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
            return false;
        }

        public bool Delete(T obj)
        {
            return false;
        }

        public int Delete(IEnumerable<T> objs)
        {
            return 0;
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
            return false;
        }

        public int Insert(IEnumerable<T> objs)
        {
            return 0;
        }

        public bool Update(T obj)
        {
            return false;
        }

        public int Update(IEnumerable<T> objs)
        {
            return 0;
        }

        public bool Upsert(T obj)
        {
            return false;
        }

        public int Upsert(IEnumerable<T> objs)
        {
            return 0;
        }
    }
}
