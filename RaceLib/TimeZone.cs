using System;
using System.Collections.Generic;

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
            bool hasAny = false;

            foreach (TimeZoneInfo timeZoneInfo in GetSystemTimeZones())
            {
                hasAny = true;
                if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneInfo.Id, out string iana))
                {
                    yield return iana;
                }
            }

            if (!hasAny)
            {
                foreach (string iana in IanasForNonWindows())
                {
                    yield return iana;
                }
            }
        }

        private static string[] IanasForNonWindows()
        {
            return ["Africa / Cairo",
                    "Africa / Casablanca",
                    "Africa / Johannesburg",
                    "Africa / Juba",
                    "Africa / Khartoum",
                    "Africa / Lagos",
                    "Africa / Nairobi",
                    "Africa / Sao_Tome",
                    "Africa / Tripoli",
                    "Africa / Windhoek",
                    "America / Adak",
                    "America / Anchorage",
                    "America / Araguaina",
                    "America / Asuncion",
                    "America / Bahia",
                    "America / Bogota",
                    "America / Buenos_Aires",
                    "America / Cancun",
                    "America / Caracas",
                    "America / Cayenne",
                    "America / Chicago",
                    "America / Chihuahua",
                    "America / Cuiaba",
                    "America / Denver",
                    "America / Godthab",
                    "America / Grand_Turk",
                    "America / Guatemala",
                    "America / Halifax",
                    "America / Havana",
                    "America / Indianapolis",
                    "America / La_Paz",
                    "America / Los_Angeles",
                    "America / Mexico_City",
                    "America / Miquelon",
                    "America / Montevideo",
                    "America / New_York",
                    "America / Phoenix",
                    "America / Port - au - Prince",
                    "America / Punta_Arenas",
                    "America / Regina",
                    "America / Santiago",
                    "America / Sao_Paulo",
                    "America / St_Johns",
                    "America / Tijuana",
                    "America / Whitehorse",
                    "Asia / Almaty",
                    "Asia / Amman",
                    "Asia / Baghdad",
                    "Asia / Baku",
                    "Asia / Bangkok",
                    "Asia / Barnaul",
                    "Asia / Beirut",
                    "Asia / Calcutta",
                    "Asia / Chita",
                    "Asia / Colombo",
                    "Asia / Damascus",
                    "Asia / Dhaka",
                    "Asia / Dubai",
                    "Asia / Hebron",
                    "Asia / Hovd",
                    "Asia / Irkutsk",
                    "Asia / Jerusalem",
                    "Asia / Kabul",
                    "Asia / Kamchatka",
                    "Asia / Karachi",
                    "Asia / Katmandu",
                    "Asia / Krasnoyarsk",
                    "Asia / Magadan",
                    "Asia / Novosibirsk",
                    "Asia / Omsk",
                    "Asia / Pyongyang",
                    "Asia / Qyzylorda",
                    "Asia / Rangoon",
                    "Asia / Riyadh",
                    "Asia / Sakhalin",
                    "Asia / Seoul",
                    "Asia / Shanghai",
                    "Asia / Singapore",
                    "Asia / Srednekolymsk",
                    "Asia / Taipei",
                    "Asia / Tashkent",
                    "Asia / Tbilisi",
                    "Asia / Tehran",
                    "Asia / Tokyo",
                    "Asia / Tomsk",
                    "Asia / Ulaanbaatar",
                    "Asia / Vladivostok",
                    "Asia / Yakutsk",
                    "Asia / Yekaterinburg",
                    "Asia / Yerevan",
                    "Atlantic / Azores",
                    "Atlantic / Cape_Verde",
                    "Atlantic / Reykjavik",
                    "Australia / Adelaide",
                    "Australia / Brisbane",
                    "Australia / Darwin",
                    "Australia / Eucla",
                    "Australia / Hobart",
                    "Australia / Lord_Howe",
                    "Australia / Perth",
                    "Australia / Sydney",
                    "Etc / GMT - 12",
                    "Etc / GMT - 13",
                    "Etc / GMT + 11",
                    "Etc / GMT + 12",
                    "Etc / GMT + 2",
                    "Etc / GMT + 8",
                    "Etc / GMT + 9",
                    "Etc / UTC",
                    "Europe / Astrakhan",
                    "Europe / Berlin",
                    "Europe / Bucharest",
                    "Europe / Budapest",
                    "Europe / Chisinau",
                    "Europe / Istanbul",
                    "Europe / Kaliningrad",
                    "Europe / Kiev",
                    "Europe / London",
                    "Europe / Minsk",
                    "Europe / Moscow",
                    "Europe / Paris",
                    "Europe / Samara",
                    "Europe / Saratov",
                    "Europe / Volgograd",
                    "Europe / Warsaw",
                    "Indian / Mauritius",
                    "Pacific / Apia",
                    "Pacific / Auckland",
                    "Pacific / Bougainville",
                    "Pacific / Chatham",
                    "Pacific / Easter",
                    "Pacific / Fiji",
                    "Pacific / Guadalcanal",
                    "Pacific / Honolulu",
                    "Pacific / Kiritimati",
                    "Pacific / Marquesas",
                    "Pacific / Norfolk",
                    "Pacific / Port_Moresby",
                    "Pacific / Tongatapu"]; 
        }
    }
}
