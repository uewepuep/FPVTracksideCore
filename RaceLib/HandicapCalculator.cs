using System;
using System.Collections.Generic;
using System.Linq;

namespace RaceLib
{
    public static class HandicapCalculator
    {
        public static Dictionary<Pilot, TimeSpan> Calculate(
            IEnumerable<Pilot> pilots,
            int raceLaps,
            int pbLapCount,
            LapRecordManager lapRecords,
            TimeSpan? maxOffset = null)
        {
            Dictionary<Pilot, TimeSpan> offsets = new Dictionary<Pilot, TimeSpan>();

            if (pilots == null || lapRecords == null) return offsets;
            if (raceLaps <= 0 || pbLapCount <= 0) return offsets;

            Dictionary<Pilot, TimeSpan?> perLapByPilot = new Dictionary<Pilot, TimeSpan?>();
            foreach (Pilot p in pilots)
            {
                if (p == null) continue;

                if (lapRecords.GetBestLaps(p, pbLapCount, out Lap[] best, out _)
                    && best != null && best.Length == pbLapCount)
                {
                    TimeSpan total = best.TotalTime();
                    perLapByPilot[p] = TimeSpan.FromTicks(total.Ticks / pbLapCount);
                }
                else
                {
                    perLapByPilot[p] = null;
                }
            }

            List<KeyValuePair<Pilot, TimeSpan?>> withPB = perLapByPilot.Where(kv => kv.Value.HasValue).ToList();
            if (withPB.Count < 2) return offsets;

            TimeSpan slowestPerLap = withPB.Max(kv => kv.Value.Value);

            foreach (KeyValuePair<Pilot, TimeSpan?> kv in perLapByPilot)
            {
                if (!kv.Value.HasValue) continue;

                TimeSpan delta = slowestPerLap - kv.Value.Value;
                if (delta <= TimeSpan.Zero) continue;

                TimeSpan offset = TimeSpan.FromTicks(delta.Ticks * raceLaps);
                if (maxOffset.HasValue && offset > maxOffset.Value)
                    offset = maxOffset.Value;

                offsets[kv.Key] = offset;
            }

            return offsets;
        }
    }
}
