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

            // Get the recording start time from the first frame
            var firstFrame = frameTimes.OrderBy(f => f.Time).First();
            
            // Calculate the offset from recording start to the requested time
            TimeSpan offsetFromRecordingStart = dateTime - firstFrame.Time;
            
            // Debug logging
            
            // Return the offset as the media time position
            return offsetFromRecordingStart + latency;
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
