using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Patreon : BaseObjectT<DB.Patreon>
    {
        public string Name { get; set; }
        public string ThumbURL { get; set; }
        public DateTime StartDate { get; set; }
        public string Tier { get; set; }
        public bool Active { get; set; }
        public string ThumbFilename { get; set; }
        public int Amount { get; set; }

        public Patreon()
        {
        }

        public Patreon(DB.Patreon obj)
            : base(obj)
        {
        }
    }
}
