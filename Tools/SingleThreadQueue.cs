using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools
{
    public class SingleThreadQueue<T> : IDisposable
    {
        private WorkQueue queue;
        private T t;

        public SingleThreadQueue(string name, T t)
        {
            queue = new WorkQueue(name);
            this.t = t;
        }

        public void Dispose()
        {
            queue.Dispose();

            if (t is IDisposable)
            {
                ((IDisposable)t).Dispose();
            }
        }

        public void DoOne(Action<T> work)
        {
            queue.Enqueue(() =>
            {
                work(t);
            });
        }
    }
}
