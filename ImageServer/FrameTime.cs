using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace ImageServer
{
    public struct FrameTime
    {
        public int Frame { get; set; }
        public DateTime Time { get; set; }
        public double Seconds { get; set; }
    }

    public static class FrameTimeExt
    {
        public static DateTime FirstFrame(this IEnumerable<FrameTime> frameTimes)
        {
            var possibleFirstFrames = frameTimes.Where(r => r.Frame == 1);
            if (possibleFirstFrames.Any())
            {
                return possibleFirstFrames.First().Time;
            }

            return default(DateTime);
        }

        public static TimeSpan GetMediaTime(this IEnumerable<FrameTime> frameTimes, DateTime dateTime, TimeSpan latency)
        {
            if (!frameTimes.Any())
            {
                return default(TimeSpan);
            }

            FrameTime closest = frameTimes.OrderBy(r => Math.Abs(r.Time.Ticks - dateTime.Ticks)).First();

            TimeSpan difference = dateTime - closest.Time;

            return TimeSpan.FromSeconds(closest.Seconds) + difference + latency;
        }

        public static DateTime GetRealTime(this IEnumerable<FrameTime> frameTimes, TimeSpan media, TimeSpan latency)
        {
            if (!frameTimes.Any())
            {
                return default(DateTime);
            }

            FrameTime closest = frameTimes.OrderBy(r => Math.Abs(r.Seconds - media.TotalSeconds)).First();

            double difference = media.TotalSeconds - closest.Seconds;

            DateTime output = closest.Time + TimeSpan.FromSeconds(difference);

            output -= latency;
            return output;
        }
    }
}
