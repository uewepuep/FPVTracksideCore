using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
            if (GetIanaTimeZone(TimeZoneInfo.Local, out string iana))
            {
                if (iana == "Australia/Sydney")
                {
                    return "Australia/Melbourne"; // Supposed to be. Australia/Sydney. But take that! 
                }

                return iana;
            }

            return "UTC";
        }

        public static IEnumerable<string> GetIanaTimeZones()
        {
            foreach (TimeZoneInfo timeZoneInfo in GetSystemTimeZones())
            {
                if (GetIanaTimeZone(timeZoneInfo, out string iana))
                {
                    yield return iana;
                }
            }
        }

        private static bool GetIanaTimeZone(TimeZoneInfo timeZoneInfo, out string iana)
        {
            Regex regex = new Regex("([A-z_]+/[A-z_]+)");
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneInfo.Id, out iana))
            {
                return true;
            }
            else
            {
                if (regex.IsMatch(timeZoneInfo.Id))
                {
                    iana = timeZoneInfo.Id;
                    return true;
                }
            }
            return false;
        }
    }

}
