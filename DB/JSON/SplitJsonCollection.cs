
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static OfficeOpenXml.ExcelErrorValue;

namespace DB.JSON
{
    public class SplitJsonCollection<T> : IDatabaseCollection<T> where T : DatabaseObject, new()
    {
        public DirectoryInfo Directory { get; private set; }

        public string Prefix { get; private set; }

        protected JsonIO<T> jsonIO;

        protected T[] allCache;
        protected bool cacheValid;

        public SplitJsonCollection(DirectoryInfo directoryInfo, string prefix = null)
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

        public virtual DirectoryInfo GetDirectoryInfo(Guid id)
        {
            return new DirectoryInfo(Path.Combine(Directory.FullName, id.ToString()));
        }

        public virtual string GetFilename(Guid id)
        {
            DirectoryInfo di = GetDirectoryInfo(id);
            if (!di.Exists)
                di.Create();

            string filename = Path.Combine(di.FullName, Prefix + ".json");
            return Path.Combine(di.FullName, filename);
        }

        public bool Update(T obj)
        {
            cacheValid = false;
            return Append(obj);
        }

        public bool Update(IEnumerable<T> objs)
        {
            cacheValid = false;
            return Append(objs);
        }

        public bool Insert(T obj)
        {
            cacheValid = false;
            return Append(obj);
        }

        public bool Insert(IEnumerable<T> objs)
        {
            cacheValid = false;
            return Append(objs);
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
            cacheValid = false;

            return Delete(new Guid[] { id });
        }

        public bool Delete(T obj)
        {
            cacheValid = false;

            return Delete(new Guid[] { obj.ID });
        }

        private bool Delete(IEnumerable<Guid> ids)
        {
            IEnumerable<T> objs = GetObjects(ids);
            return Delete(objs);
        }

        public bool Delete(IEnumerable<T> objs)
        {
            int allWritten = 0;
            int exceptCount = 0;

            cacheValid = false;

            var groups = objs.GroupBy(r => ObjectToID(r));

            foreach (var group in groups)
            {
                Guid id = group.Key;
                IEnumerable<T> delValues = group.Where(r => r != null);
                string filename = GetFilename(id);

                T[] existing = Read(filename);

                IEnumerable<T> except = existing.Except(delValues);

                exceptCount += except.Count();

                allWritten += jsonIO.Write(filename, except);
            }
            return allWritten - exceptCount == objs.Count();
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

        protected virtual IEnumerable<T> DiskAll()
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

        protected T[] Read(string filename)
        {
            return jsonIO.Read(filename);
        }

        private bool Append(T value)
        {
            return Append(new T[] { value });
        }

        private bool Append(IEnumerable<T> values)
        {
            int allWritten = 0;
            int exceptCount = 0;
            var groups = values.GroupBy(r => ObjectToID(r));

            foreach (var group in groups)
            {
                Guid id = group.Key;
                IEnumerable<T> newValues = group.Where(r => r != null);
                string filename = GetFilename(id);

                T[] existing = Read(filename);

                IEnumerable<T> except = existing.Except(newValues);
                exceptCount += except.Count();

                IEnumerable<T> appended = except.Concat(newValues);

                allWritten += jsonIO.Write(filename, appended);
            }

            return allWritten - exceptCount == values.Count();
        }

        private int Write(IEnumerable<T> values)
        {
            int count = 0;
            var groups = values.GroupBy(r => ObjectToID(r));

            foreach (var group in groups)
            {
                Guid id = group.Key;
                IEnumerable<T> newValues = group.Where(r => r != null);
                string filename = GetFilename(id);
                count += jsonIO.Write(filename, newValues);
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
            T[] ts = All().Where(r => ids.Contains(r.ID)).ToArray();

            foreach (Guid id in ids)
            {
                yield return ts.FirstOrDefault(t => t.ID == id);
            }
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
