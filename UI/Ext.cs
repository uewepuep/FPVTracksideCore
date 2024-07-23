using ImageServer;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI
{
    public static class Ext
    {
        public static string ToStringRaceTime(this TimeSpan timespan)
        {
            return ToStringRaceTime(timespan, ApplicationProfileSettings.Instance.ShownDecimalPlaces);
        }

        public static string ToStringRaceTime(this TimeSpan timespan, int decimalPlaces)
        {
            string decPlaces = "";
            for (int i = 0; i < decimalPlaces; i++)
            {
                decPlaces += "0";
            }


            if (timespan.TotalHours < 1)
            {
                return string.Format("{0,5:##0." + decPlaces + "}", timespan.TotalSeconds);
            }
            else
            {
                return string.Format("{0,2}:{1,2:D2}:{2,2:D2}", (int)timespan.TotalHours, timespan.Minutes, timespan.Seconds);
            }
        }

        public static Channel GetChannel(this VideoBounds videoBounds)
        {
            return Channel.AllChannels.GetByShortString(videoBounds.Channel);
        }
    }
}
