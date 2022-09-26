using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition
{
    public abstract class AInterpolated<T>
    {
        public T Initial { get; private set; }
        public T Target { get; private set; }

        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }
        public TimeSpan TimeToTake { get; private set; }

        public bool Valid { get { return TimeToTake.TotalSeconds > 0; } }

        public T Output { get { return Lerp(); } }

        public bool Finished { get { return (StartTime + TimeToTake) < DateTime.Now; } }

        public TimeSpan Remaining { get { return EndTime - DateTime.Now; } }

        public AInterpolated(T inital, T target, TimeSpan timeToTake)
        {
            StartTime = DateTime.Now;
            EndTime = StartTime + timeToTake;
            TimeToTake = timeToTake;

            Initial = inital;
            Target = target;
        }

        protected abstract T Lerp();

        public void Snap()
        {
            EndTime = DateTime.Now;
            StartTime = EndTime - TimeToTake;
        }

        public void SetTarget(T target, bool resetTimer = true)
        {
            SetTarget(Output, target, resetTimer);
        }

        public void SetTarget(T target, TimeSpan timeToTake)
        {
            TimeToTake = timeToTake;
            SetTarget(Output, target, true);
        }

        public void SetTarget(T current, T target, TimeSpan timeToTake)
        {
            TimeToTake = timeToTake;
            SetTarget(current, target, true);
        }

        public void SetTarget(T current, T target, bool resetTimer = true)
        {
            Target = target;
            if (resetTimer)
            {
                Initial = current;
                StartTime = DateTime.Now;
                EndTime = StartTime + TimeToTake;
            }
        }
    }
}
