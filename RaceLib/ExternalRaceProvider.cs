using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public abstract class ExternalRaceProvider
    {
        public virtual string Name { get; }

        public abstract void TriggerCreateRaces(Round round);
    }
}
