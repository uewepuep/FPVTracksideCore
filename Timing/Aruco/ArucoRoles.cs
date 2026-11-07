using System.Collections.Generic;
using System.Linq;

namespace Timing.Aruco
{
    /// <summary>
    /// Which ArUco instance plays which part. The Primary closes laps. The reference is the
    /// instance whose shared settings (thresholds, detector parameters) every instance runs on:
    /// the Primary, or the first Split when there is none, so a Split's UI can stay limited to
    /// MarkerIds.
    ///
    /// Kept in one place so the detection loop, the timing system and the settings editor
    /// cannot disagree about it.
    /// </summary>
    public static class ArucoRoles
    {
        public static bool IsPrimary(ArucoTimingSettings settings)
        {
            return settings != null && settings.Role == TimingSystemRole.Primary;
        }

        /// <summary>The first Primary, or null.</summary>
        public static ArucoTimingSettings Primary(IEnumerable<ArucoTimingSettings> instances)
        {
            return instances.FirstOrDefault(IsPrimary);
        }

        /// <summary>The first Primary, otherwise the first instance; null when there is none.</summary>
        public static ArucoTimingSettings Reference(IEnumerable<ArucoTimingSettings> instances)
        {
            return Primary(instances) ?? instances.FirstOrDefault(s => s != null);
        }

        /// <summary>
        /// Keeps the first Primary and demotes any later one to Split, so only the start/finish
        /// gate closes laps and the others record split times. Returns the demoted instances.
        /// </summary>
        public static List<ArucoTimingSettings> KeepSinglePrimary(IEnumerable<ArucoTimingSettings> instances)
        {
            var demoted = new List<ArucoTimingSettings>();
            bool seenPrimary = false;
            foreach (var s in instances)
            {
                if (!IsPrimary(s)) continue;

                if (!seenPrimary)
                {
                    seenPrimary = true;
                    continue;
                }

                s.Role = TimingSystemRole.Split;
                demoted.Add(s);
            }
            return demoted;
        }
    }
}
