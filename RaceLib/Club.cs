using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public class Club : BaseObjectT<DB.Club>
    {
        public string Name { get; set; }

        public SyncWith SyncWith { get; set; }

        public Club()
        {
        }

        public Club(DB.Club obj)
            : base(obj)
        {
        }
    }
}
