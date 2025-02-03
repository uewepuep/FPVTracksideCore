using DB.Lite;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class JsonCollection<T> : IDatabaseCollection<T> where T : DatabaseObject, new()
    {
        public DirectoryInfo Directory { get; private set; }

        public string Prefix { get; private set; }

        private JsonIO<T> jsonIO;

        public JsonCollection(DirectoryInfo directoryInfo, string prefix = null)
        {
            jsonIO = new JsonIO<T>();
            Directory = directoryInfo;

            if (!directoryInfo.Exists)
            {
                directoryInfo.Create();
            }

            if (prefix == null)
            {
                prefix = typeof(T).Name;
            }

            Prefix = prefix;
        }

        public virtual string GetFileName(Guid? id = null)
        {
            string filename = Prefix;

            if (id.HasValue)
            {
                filename += "_" + id.ToString();
            }
            else
            {
                filename += "s";
            }

            filename += ".json";
            return Path.Combine(Directory.FullName, filename);
        }

        public bool Update(T obj)
        {
            IEnumerable<T> except = All().Where(r => r.ID != obj.ID);
            IEnumerable<T> added = except.Append(obj);
            return Write(added) - except.Count() == 1;
        }

        public bool Update(IEnumerable<T> objs)
        {
            IEnumerable<T> except = All().Where(r => !objs.Select(a => a.ID).Contains(r.ID));
            IEnumerable<T> added = except.Union(objs);
            return Write(added) == added.Count();
        }

        public bool Insert(T obj)
        {
            int allCount = All().Count();
            IEnumerable<T> appended = All().Append(obj);
            return Write(appended) == allCount + 1;
        }

        public bool Insert(IEnumerable<T> objs)
        {
            IEnumerable<T> appended = All().Union(objs);
            return Write(appended) == appended.Count();
        }

        public bool Upsert(T obj)
        {
            if (All().Any(r => r.ID == obj.ID))
            {
                return Update(obj);
            }
            else
            {
                return Insert(obj);
            }
        }

        public bool Upsert(IEnumerable<T> objs)
        {
            if (All().Any(r => objs.Select(s => s.ID).Contains(r.ID)))
            {
                return Update(objs);
            }
            else
            {
                return Insert(objs);
            }
        }

        public bool Delete(Guid id)
        {
            return Delete(new Guid[] { id });
        }

        public bool Delete(T obj)
        {
            return Delete(new Guid[] { obj.ID });
        }

        public bool Delete(IEnumerable<T> objs)
        {
            return Delete(objs.Select(r => r.ID));
        }

        private bool Delete(IEnumerable<Guid> ids)
        {
            IEnumerable<T> except = All().Where(r => !ids.Contains(r.ID));
            return Write(except.ToArray()) == except.Count();
        }

        public IEnumerable<T> All()
        {
            string fileName = GetFileName();

            return jsonIO.Read(fileName);
        }

        private int Write(IEnumerable<T> values)
        {
            string fileName = GetFileName();

            return jsonIO.Write(fileName, values);
        }

        public T GetObject(Guid id)
        {
            IEnumerable<T> all = All();
            return all.FirstOrDefault(r => r.ID == id);
        }

        public IEnumerable<T> GetObjects(IEnumerable<Guid> ids)
        {
            return All().Where(r => ids.Contains(r.ID));
        }

        public T GetCreateObject(Guid id)
        {
            T t = GetObject(id);
            if (t == null)
            {
                t = new T();
            }
            return t;
        }

        public T GetCreateExternalObject(int id)
        {
            T t = All().FirstOrDefault(r => r.ExternalID == id);
            if (t == null)
            {
                t = new T();
                t.ExternalID = id;
            }
            return t;
        }
    }
}
