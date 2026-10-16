using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools
{
    // Times use Stopwatch timestamps: a monotonic, high resolution counter that's much cheaper to read than
    // DateTime.Now (which converts to local time) and doesn't jump when the clock is adjusted.
    public class DebugTimer
    {
        public long Start { get; private set; }

        private Dictionary<string, long> points;

        public DebugTimer()
        {
            Start = Stopwatch.GetTimestamp();
            points = new Dictionary<string, long>();
        }

        public void Record(string name)
        {
            points.Add(name, Stopwatch.GetTimestamp());
        }

        public string Report()
        {
            string data = "";
            long last = Start;

            foreach (var kvp in points)
            {
                data += kvp.Key + " " + Stopwatch.GetElapsedTime(Start, kvp.Value).TotalMilliseconds.ToString("0.00") + ",  ";
                last = kvp.Value;
            }

            data += "Total " + Stopwatch.GetElapsedTime(Start, last).TotalMilliseconds.ToString("0.00") + ",  ";

            return data;
        }


#if DEBUG

        private static Dictionary<string, TimeSpan> total = new Dictionary<string, TimeSpan>();
        private static Dictionary<string, TimeSpan> max = new Dictionary<string, TimeSpan>();
        private static Dictionary<string, long> start = new Dictionary<string, long>();
#endif

        public static void DebugStartTime(object obj)
        {
#if DEBUG
            DebugStartTime(obj.GetType().Name);
#endif
        }

        public static void DebugStartTime(string name)
        {
#if DEBUG
            long now = Stopwatch.GetTimestamp();
            lock (start)
            {
                start[name] = now;
            }
#endif
        }

        public static void DebugEndTime(object obj)
        {
#if DEBUG
            DebugEndTime(obj.GetType().Name);
#endif
        }

        public static void DebugEndTime(string name)
        {
#if DEBUG
            long now = Stopwatch.GetTimestamp();
            long last;
            bool found;
            lock (start)
            {
                found = start.TryGetValue(name, out last);
            }

            if (found)
            {
                TimeSpan time = Stopwatch.GetElapsedTime(last, now);
                lock (total)
                {
                    if (total.ContainsKey(name))
                    {
                        total[name] += time;
                    }
                    else
                    {
                        total.Add(name, time);
                    }
                }

                lock (max)
                {
                    if (max.ContainsKey(name))
                    {
                        if (max[name] < time)
                        {
                            max[name] = time;
                        }
                    }
                    else
                    {
                        max.Add(name, time);
                    }
                }
            }
#endif
        }

        public static IEnumerable<string> GetDebugTimeString(int frameCount)
        {
#if DEBUG
            lock (total)
            {
                foreach (var kvp in total.OrderByDescending(k => k.Value))
                {
                    if (kvp.Value.TotalSeconds > 0.1)
                    {
                        double timePerFrame = (kvp.Value.TotalMilliseconds / frameCount);
                        double thisMax = max[kvp.Key].TotalMilliseconds;
                        yield return kvp.Key + ": Avg " + timePerFrame.ToString("0.000") + "ms, Max " + thisMax.ToString("0.000") + "ms, Total: " + kvp.Value.TotalSeconds.ToString("0.00000") + "s";
                    }
                }
            }
            
#else
            yield break;
#endif
        }

        public static void Clear()
        {
#if DEBUG
            lock (total)
            {
                total.Clear();
            }

            lock (max)
            {
                max.Clear();
            }
#endif
        }
    }
}
