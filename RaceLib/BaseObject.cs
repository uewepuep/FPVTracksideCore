using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
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
        [Category("Cloud")]
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
}
