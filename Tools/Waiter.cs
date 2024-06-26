﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tools
{
    public class Waiter : IDisposable
    {
        private AutoResetEvent autoResetEvent;

        public bool IsDisposed { get; private set; }

        public Waiter()
        {
            autoResetEvent = new AutoResetEvent(false);
        }

        public void Dispose() 
        {
            if (IsDisposed) 
                return;

            IsDisposed = true;
            autoResetEvent.Dispose();
            autoResetEvent = null;
        }

        public bool Set()
        {
            try
            {
                if (!IsDisposed)
                {
                    autoResetEvent?.Set();
                    return true;
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        public bool WaitOne(TimeSpan timeOut)
        {
            try
            {
                if (!IsDisposed)
                {
                    if (autoResetEvent.WaitOne(timeOut))
                        return true;
                }
            }
            catch 
            { 
                return false; 
            }
            return false;
        }

        public delegate bool BoolReturnDel();
        public static bool WaitFor(BoolReturnDel action, TimeSpan timeout, int stepMilliseconds = 10)
        {
            DateTime start = DateTime.Now;
            while (!action())
            {
                Thread.Sleep(stepMilliseconds);
                if (DateTime.Now - start > timeout)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
