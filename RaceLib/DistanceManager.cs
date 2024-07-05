using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timing;

namespace RaceLib
{
    public class DistanceManager
    {
        private Dictionary<int, float> timingSystemIndexDistance;

        public bool HasDistance { get; private set; }

        public DistanceManager()
        {
        }

        public void Initialize(EventManager eventManager, TimingSystemManager timingSystemManager, TrackFlightPath trackFlightPath)
        {
            Dictionary<int, float> indexDistance2 = new Dictionary<int, float>();

            ITimingSystem[] timingSystems = timingSystemManager.TimingSystemsSectorOrder.ToArray();
            Sector[] sectors = eventManager.Event.Sectors;

            if (sectors == null || sectors.Length == 0)
            {
                sectors = trackFlightPath.Sectors;
            }

            int max = Math.Min(timingSystems.Length, sectors.Length);

            for (int i = 0; i < max; i++)
            {
                ITimingSystem timingSystem = timingSystems[i];
                Sector sector = sectors[i];
                if (sector.CalculateSpeed)
                {
                    int index = timingSystemManager.GetIndex(timingSystem);
                    indexDistance2.Add(index, sector.Length);
                }
            }

            HasDistance = indexDistance2.Values.Any(a => a > 0);
            timingSystemIndexDistance = indexDistance2;
        }

        public bool GetDistance(Split split, out float distance) 
        {
            if (timingSystemIndexDistance.TryGetValue(split.Detection.TimingSystemIndex, out distance))
            {
                return true;
            }
            distance = 0;
            return false;
        }
    }
}
