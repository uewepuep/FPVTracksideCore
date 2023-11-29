using DB.Lite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class SplitDirJsonCollection<T> : IDatabaseCollection<T> where T : DatabaseObject, new()
    {
        public DirectoryInfo Directory { get; private set; }

        public string Prefix { get; private set; }

        private JsonIO<T> jsonIO;

        private T[] allCache;
        private bool cacheValid;

        public SplitDirJsonCollection(DirectoryInfo directoryInfo, string prefix = null)
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
            cacheValid = false;
        }

        protected virtual Guid ObjectToID(T t)
        {
            return t.ID;
        }

        public DirectoryInfo GetDirectoryInfo(Guid id)
        {
            return new DirectoryInfo(Path.Combine(Directory.FullName, id.ToString()));
        }

        protected virtual string GetFilename(Guid id)
        {
            DirectoryInfo di = GetDirectoryInfo(id);
            if (!di.Exists)
                di.Create();

            string filename = Path.Combine(di.FullName, Prefix + ".json");
            return Path.Combine(di.FullName, filename);
        }

        public bool Update(T obj)
        {
            IEnumerable<T> except = All().Where(r => r.ID != obj.ID);
            IEnumerable<T> added = except.Append(obj);
            
            cacheValid = false;
            return Write(added) > 1;
        }

        public int Update(IEnumerable<T> objs)
        {
            IEnumerable<T> except = All().Where(r => !objs.Select(a => a.ID).Contains(r.ID));
            IEnumerable<T> added = except.Union(objs);
            
            cacheValid = false;
            return Write(added);
        }

        public bool Insert(T obj)
        {
            IEnumerable<T> appended = All().Append(obj);

            cacheValid = false;
            return Write(appended) > 1;
        }

        public int Insert(IEnumerable<T> objs)
        {
            IEnumerable<T> appended = All().Union(objs);

            cacheValid = false;
            return Write(appended);
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

        public int Upsert(IEnumerable<T> objs)
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
            cacheValid = false;

            return Delete(new Guid[] { id }) > 1;
        }

        public bool Delete(T obj)
        {
            cacheValid = false;

            return Delete(new Guid[] { obj.ID }) > 1;
        }

        public int Delete(IEnumerable<T> objs)
        {
            cacheValid = false;

            return Delete(objs.Select(r => r.ID));
        }

        private int Delete(IEnumerable<Guid> ids)
        {
            IEnumerable<T> except = All().Where(r => !ids.Contains(r.ID));

            cacheValid = false;
            return Write(except.ToArray());
        }

        public IEnumerable<T> All()
        {
            if (!cacheValid)
            {
                allCache = DiskAll().ToArray();
                cacheValid = true;
            }

            return allCache;
        }

        private IEnumerable<T> DiskAll()
        {
            foreach (DirectoryInfo di in Directory.EnumerateDirectories())
            {
                if (Guid.TryParse(di.Name, out Guid id) && id != Guid.Empty)
                {
                    string filename = GetFilename(id);
                    T[] ts = jsonIO.Read(filename);
                    foreach (T t in ts)
                    {
                        if (t != null)
                            yield return t;
                    }
                }
            }
        }

        private int Write(IEnumerable<T> values)
        {
            int count = 0;
            var groups = values.GroupBy(r => ObjectToID(r));

            foreach (var group in groups)
            {
                Guid id = group.Key;
                IEnumerable<T> ts = group.Where(r => r != null);
                string filename = GetFilename(id);
                jsonIO.Write(filename, ts);
            }
            return count;
        }

        public T GetObject(Guid id)
        {
            if (id == Guid.Empty)
                return null;

            return All().FirstOrDefault(r => r.ID == id);
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
