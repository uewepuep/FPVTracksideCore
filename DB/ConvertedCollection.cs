using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB
{
    public class ConvertedCollection<R, D> : IDatabaseCollection<R> where R : RaceLib.BaseObject, new() where D : DatabaseObjectT<R>
    {
        private IDatabaseCollection<D> collection;
        private ICollectionDatabase database;

        public ConvertedCollection(IDatabaseCollection<D> collection, ICollectionDatabase database)
        {
            this.collection = collection;
            this.database = database;
            System.Diagnostics.Debug.Assert(collection != null);
        }

        public IEnumerable<R> All()
        {
            return collection.All().Convert(database);
        }

        public bool Delete(Guid id)
        {
            return collection.Delete(id);
        }

        public bool Delete(R obj)
        {
            return collection.Delete(obj.Convert<D>());
        }

        public bool Delete(IEnumerable<R> objs)
        {
            return collection.Delete(objs.Convert<D>());
        }

        public R GetCreateExternalObject(int id)
        {
            return collection.GetCreateExternalObject(id).Convert(database);
        }

        public R GetCreateObject(Guid id)
        {
            return collection.GetCreateObject(id).Convert(database);
        }

        public R GetObject(Guid id)
        {
            return collection.GetObject(id).Convert(database);
        }

        public IEnumerable<R> GetObjects(IEnumerable<Guid> ids)
        {
            return collection.GetObjects(ids).Convert(database);
        }

        public bool Insert(R obj)
        {
            return collection.Insert(obj.Convert<D>());
        }

        public bool Insert(IEnumerable<R> objs)
        {
            return collection.Insert(objs.Convert<D>());
        }

        public bool Update(R obj)
        {
            return collection.Update(obj.Convert<D>());
        }

        public bool Update(IEnumerable<R> objs)
        {
            return collection.Update(objs.Convert<D>());
        }

        public bool Upsert(R obj)
        {
            return collection.Upsert(obj.Convert<D>());
        }

        public bool Upsert(IEnumerable<R> objs)
        {
            return collection.Upsert(objs.Convert<D>());
        }
    }

}
