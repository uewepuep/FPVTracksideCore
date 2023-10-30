using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace RaceLib
{
    public class BaseObject
    {
        [System.ComponentModel.Browsable(false)]
        [XmlIgnore]
        [JsonIgnore]
        public Guid ID { get; set; }

        [System.ComponentModel.Browsable(false)]
        public DateTime Creation { get; set; }
        
        [System.ComponentModel.Browsable(false)]
        public DateTime Modified { get; set; }

        [System.ComponentModel.Browsable(false)]
        public int ExternalID { get; set; }

        public BaseObject()
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
                BaseObject baseDBObjectT = obj as BaseObject;
                if (baseDBObjectT != null)
                {
                    if (baseDBObjectT.ID == default(Guid) && ID == default(Guid))
                    {
                        return ReferenceEquals(this, baseDBObjectT);
                    }

                    return baseDBObjectT.ID == ID;
                }
            }

            return base.Equals(obj);
        }

        public static bool operator ==(BaseObject a, BaseObject b)
        {
            if (ReferenceEquals(a, null))
            {
                return ReferenceEquals(b, null);
            }

            return a.Equals(b);
        }

        public static bool operator !=(BaseObject a, BaseObject b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return GetType().GetHashCode() + ID.GetHashCode();
        }
    }

    public class BaseObjectT<T> : BaseObject where T : DB.DatabaseObject
    {
        public BaseObjectT()
        {
        }

        public BaseObjectT(T baseDBObject)
        {
            Copy(baseDBObject, this);
        }

        public virtual T GetDBObject()
        {
            T t = Activator.CreateInstance<T>();

            if (ID == Guid.Empty)
            {
                ID = Guid.NewGuid();
            }

            Copy(this, t);


            return t;
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

    public static class BaseObjectTExt
    {
        public static IEnumerable<T> GetDBObjects<T>(this IEnumerable<BaseObjectT<T>> baseObjects) where T : DB.DatabaseObject
        {
            if (baseObjects == null)
                return null;

            return baseObjects.Select(b => b.GetDBObject());
        }
    }
}
