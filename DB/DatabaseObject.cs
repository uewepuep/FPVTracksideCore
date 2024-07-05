using LiteDB;
using Newtonsoft.Json;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace DB
{
    public class DatabaseObject
    {
        public Guid ID { get; set; }
        
        [JsonIgnore]
        public DateTime Creation { get; set; }

        [JsonIgnore]
        public DateTime Modified { get; set; }
        public int ExternalID { get; set; }

        public DatabaseObject()
        {
            Creation = DateTime.Now;
            Modified = Creation;
        }

        public void Modify()
        {
            Modified = DateTime.Now;
        }

        public override bool Equals(object obj)
        {
            if (obj == null) return false;

            if (obj.GetType() == GetType())
            {
                DatabaseObject baseDBObjectT = obj as DatabaseObject;
                if (baseDBObjectT != null)
                {
                    if (baseDBObjectT.ID == default && ID == default)
                    {
                        return ReferenceEquals(this, baseDBObjectT);
                    }

                    return baseDBObjectT.ID == ID;
                }
            }

            return base.Equals(obj);
        }

        public static bool operator ==(DatabaseObject a, DatabaseObject b)
        {
            if (ReferenceEquals(a, null))
            {
                return ReferenceEquals(b, null);
            }

            return a.Equals(b);
        }

        public static bool operator !=(DatabaseObject a, DatabaseObject b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return GetType().GetHashCode() + ID.GetHashCode();
        }
    }

    public class DatabaseObjectT<T> : DatabaseObject where T : RaceLib.BaseObject
    {
        public DatabaseObjectT()
        {
        }

        public DatabaseObjectT(T obj)
        {
            Copy(obj, this);
        }

        public virtual T GetRaceLibObject(ICollectionDatabase database)
        {
            T t = Activator.CreateInstance<T>();
            Copy(this, t);
            return t;
        }

        protected void Copy<Source, Destination>(Source[] source, out Destination[] destination) where Destination: new()
        {
            if (source == null)
            {
                destination = null;
                return;
            }

            destination = new Destination[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                destination[i] = new Destination();
                Copy(source[i], destination[i]);
            }
        }

        protected void Copy<Source, Destination>(Source source, Destination destination)
        {
            if (source == null || destination == null)
                return;

            Type typeSrc = source.GetType();
            Type typeDest = destination.GetType();

            // Iterate the Properties of the source instance and  
            // populate them from their desination counterparts  
            PropertyInfo[] srcProps = typeSrc.GetProperties();

            foreach (PropertyInfo srcProp in srcProps)
            {
                if (!srcProp.CanRead)
                {
                    continue;
                }
                PropertyInfo targetProperty = typeDest.GetProperty(srcProp.Name);
                if (targetProperty == null)
                {
                    continue;
                }
                if (!targetProperty.CanWrite)
                {
                    continue;
                }
                if (targetProperty.GetSetMethod(true) != null && targetProperty.GetSetMethod(true).IsPrivate)
                {
                    continue;
                }
                if ((targetProperty.GetSetMethod().Attributes & MethodAttributes.Static) != 0)
                {
                    continue;
                }
                object? value = srcProp.GetValue(source, null);

                if (value != null)
                {
                    if (targetProperty.PropertyType.IsEnum)
                    {
                        IEnumerable<Enum> valid = Enum.GetValues(targetProperty.PropertyType).OfType<Enum>().Where(e => e.ToString() == value.ToString());
                        if (!valid.Any())
                        {
                            continue;
                        }

                        value = valid.First();
                    }
                    else if (srcProp.PropertyType.IsEnum && targetProperty.PropertyType == typeof(string))
                    {
                        value = value.ToString();
                    }
                    else if (!targetProperty.PropertyType.IsAssignableFrom(srcProp.PropertyType))
                    {
                        continue;
                    }
                }

                // Passed all tests, lets set the value
                targetProperty.SetValue(destination, value, null);
            }
        }
    }

    public static class DatabaseObjectExt
    {
        public static T Convert<T>(this DatabaseObjectT<T> baseDBObject, ICollectionDatabase database) where T : RaceLib.BaseObject
        {
            if (baseDBObject == null)
                return null;

            return baseDBObject.GetRaceLibObject(database);
        }

        public static IEnumerable<T> Convert<T>(this IEnumerable<DatabaseObjectT<T>> baseDBObjects, ICollectionDatabase database) where T : RaceLib.BaseObject
        {
            if (baseDBObjects == null)
            {
                yield break;
            }

            foreach (var obj in baseDBObjects)
            {
                yield return obj.GetRaceLibObject(database);
            }
        }

        public static T Convert<T>(this Guid id, ICollectionDatabase database) where T : RaceLib.BaseObject, new()
        {
            return database.GetCollection<T>().GetObject(id);
        }

        public static IEnumerable<T> Convert<T>(this IEnumerable<Guid> ids, ICollectionDatabase database) where T : RaceLib.BaseObject, new()
        {
            return database.GetCollection<T>().GetObjects(ids);
        }

        public static T Convert<T>(this RaceLib.BaseObject baseDBObject) where T : DatabaseObject
        {
            if (baseDBObject == null)
                return null;

            if (baseDBObject.ID == Guid.Empty)
            {
                baseDBObject.ID = Guid.NewGuid();
            }

            T t = (T)Activator.CreateInstance(typeof(T), baseDBObject);
            return t;
        }

        public static IEnumerable<T> Convert<T>(this IEnumerable<RaceLib.BaseObject> baseDBObjects) where T : DatabaseObject
        {
            if (baseDBObjects == null)
            {
                yield break;
            }

            RaceLib.BaseObject[] array = baseDBObjects.ToArray();
            foreach (var obj in array)
            {
                yield return obj.Convert<T>();
            }
        }
    }
}
