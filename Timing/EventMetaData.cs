using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timing
{
    public class EventMetaData
    {
        public EventMetaData(string name)
        {
            Name = name;
        }

        public string Name { get; set; }
    }
}
