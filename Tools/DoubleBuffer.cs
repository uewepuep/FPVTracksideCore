using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools
{
    public class DoubleBuffer<T> : IDisposable
    {
        private T[] buffers;
        public T[] Buffers { get { return buffers; } }

        private short write;
        private short read;

        public DoubleBuffer(params T[] buf)
            : this()
        {
            buffers = buf;
        }

        public DoubleBuffer(params object[] args)
            : this()
        {
            for (int i = 0; i < buffers.Length; i++)
            {
                buffers[i] = (T)Activator.CreateInstance(typeof(T), args);
            }
        }

        public DoubleBuffer()
        {
            buffers = new T[2];
            write = 0;
            read = 1;
        }

        public void Dispose()
        {
            if (typeof(IDisposable).IsAssignableFrom(typeof(T)))
            {
                for (int i = 0; i < buffers.Length; i++)
                {
                    if (buffers[i] != null)
                    {
                        ((IDisposable)buffers[i]).Dispose();
                        buffers[i] = default(T);
                    }
                }
            }
        }

        public T Writable()
        {
            return buffers[write];
        }

        public void Set(T t)
        {
            buffers[write] = t;
        }

        public T Readable()
        {
            return buffers[read];
        }

        public void Swap()
        {
            write = read;
            read = (short)((read + 1) % buffers.Length);
        }
    }
}
