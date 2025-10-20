using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaceLib
{
    public static class TimeZone
    {
        public static IEnumerable<TimeZoneInfo> GetSystemTimeZones()
        {
            return TimeZoneInfo.GetSystemTimeZones();
        }

        public static string GetIanaTimeZoneLocal()
        {
            if (TimeZoneInfo.Local.Id == "AUS Eastern Standard Time")
            {
                return "Australia/Melbourne"; // Supposed to be. Australia/Sydney. But take that! 
            }

            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out string iana))
            {
                return iana;
            }

            return "UTC";
        }

        public static IEnumerable<string> GetIanaTimeZones()
        {
            foreach (TimeZoneInfo timeZoneInfo in GetSystemTimeZones())
            {
                
                if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneInfo.Id, out string iana))
                {
                    yield return iana;
                }
            }
        }
    }
}
