﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB
{
    public class CacheCollection<T> : IDatabaseCollection<T> where T : RaceLib.BaseObject, new()
    {

        private T[] cache;

        private IDatabaseCollection<T> original;

        public CacheCollection(IDatabaseCollection<T> original) 
        { 
            this.original = original;
        }

        public IEnumerable<T> All()
        {
            if (cache == null)
            {
                cache = original.All().ToArray();
            }
            return cache;
        }

        public bool Delete(Guid id)
        {
            cache = null;
            return original.Delete(id);
        }

        public bool Delete(T obj)
        {
            cache = null;
            return original.Delete(obj);
        }

        public bool Delete(IEnumerable<T> objs)
        {
            cache = null;
            return original.Delete(objs);
        }

        public T GetCreateExternalObject(int id)
        {
            T t = All().FirstOrDefault(r => r.ExternalID == id);
            if (t == null)
            {
                t = original.GetCreateExternalObject(id);
            }
            return t;
        }

        public T GetCreateObject(Guid id)
        {
            T t = All().FirstOrDefault(r => r.ID == id);
            if (t == null)
            {
                t = original.GetCreateObject(id);
            }
            return t;
        }

        public T GetObject(Guid id)
        {
            return All().FirstOrDefault(r => r.ID == id);
        }

        public IEnumerable<T> GetObjects(IEnumerable<Guid> ids)
        {
            T[] ts = All().Where(r => ids.Contains(r.ID)).ToArray();

            foreach (Guid id in ids)
            {
                yield return ts.FirstOrDefault(t => t.ID == id);
            }
        }

        public bool Insert(T obj)
        {
            cache = null;
            return original.Insert(obj);
        }

        public bool Insert(IEnumerable<T> objs)
        {
            cache = null;
            return original.Insert(objs);
        }

        public bool Update(T obj)
        {
            cache = null;
            return original.Insert(obj);
        }

        public bool Update(IEnumerable<T> objs)
        {
            cache = null;
            return original.Insert(objs);
        }

        public bool Upsert(T obj)
        {
            cache = null;
            return original.Insert(obj);
        }

        public bool Upsert(IEnumerable<T> objs)
        {
            cache = null;
            return original.Insert(objs);
        }
    }
}
