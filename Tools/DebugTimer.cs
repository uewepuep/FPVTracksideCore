using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools
{
    public class DebugTimer
    {
        public DateTime Start { get; private set; }

        private Dictionary<string, DateTime> points;

        public DebugTimer()
        {
            Start = DateTime.Now;
            points = new Dictionary<string, DateTime>();
        }

        public void Record(string name)
        {
            points.Add(name, DateTime.Now);
        }

        public string Report()
        {
            string data = "";
            DateTime last = Start;

            foreach (var kvp in points)
            {
                data += kvp.Key + " " + (kvp.Value - Start).TotalMilliseconds.ToString("0.00") + ",  ";
                last = kvp.Value;
            }

            data += "Total " + (last - Start).TotalMilliseconds.ToString("0.00") + ",  ";

            return data;
        }


#if DEBUG

        private static Dictionary<string, TimeSpan> times = new Dictionary<string, TimeSpan>();
        private static Dictionary<string, DateTime> start = new Dictionary<string, DateTime>();
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
            if (start.ContainsKey(name))
            {
                start[name] = DateTime.Now;
            }
            else
            {
                start.Add(name, DateTime.Now);
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
            DateTime last;
            if (start.TryGetValue(name, out last))
            {
                DateTime now = DateTime.Now;
                TimeSpan time = now - last;
                lock (times)
                {
                    if (times.ContainsKey(name))
                    {
                        times[name] += time;
                    }
                    else
                    {
                        times.Add(name, time);
                    }
                }
            }
#endif
        }

        public static IEnumerable<string> GetDebugTimeString(int frameCount)
        {
#if DEBUG
            lock (times)
            {
                foreach (var kvp in times.OrderByDescending(k => k.Value))
                {
                    if (kvp.Value.TotalSeconds > 0.1)
                    {
                        double timePerFrame = (kvp.Value.TotalSeconds / frameCount) * 1000;
                        yield return kvp.Key + ": " + timePerFrame.ToString("0.00000") + " Total: " + kvp.Value.TotalSeconds.ToString("0.00000");
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
            lock (times)
            {
                var keys = times.Keys.ToArray();
                times.Clear();
                foreach (var key in keys)
                {
                    times.Add(key, TimeSpan.Zero);
                }
            }
#endif
        }
    }
}
