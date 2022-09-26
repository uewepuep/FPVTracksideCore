using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace RaceLib
{
    public class BaseDBObject
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

        public BaseDBObject()
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
                BaseDBObject baseDBObjectT = obj as BaseDBObject;
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

        public static bool operator ==(BaseDBObject a, BaseDBObject b)
        {
            if (ReferenceEquals(a, null))
            {
                return ReferenceEquals(b, null);
            }

            return a.Equals(b);
        }

        public static bool operator !=(BaseDBObject a, BaseDBObject b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return GetType().GetHashCode() + ID.GetHashCode();
        }
    }

}
