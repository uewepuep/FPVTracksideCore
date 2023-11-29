using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB
{
    public class Club : DatabaseObjectT<RaceLib.Club>
    {
        public string Name { get; set; }

        public string SyncWith { get; set; }

        public Club() { }

        public Club(RaceLib.Club obj)
            : base(obj)
        {
        }
    }
}
