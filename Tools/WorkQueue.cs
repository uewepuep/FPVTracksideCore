using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tools
{
    public class WorkQueue : IDisposable
    {
        private Queue<WorkItem> queue;

        public float Progress 
        { 
            get 
            {
                if (!NeedWorkDone)
                    return 1;

                int count = queue.Count;

                if (doingOne)
                    count++;

                if (MaxQueueLength == 0)
                    return 0;

                float value = 1 - (count / (float)MaxQueueLength);

                value = Math.Max(0, Math.Min(1, value));

                return value; 
            } 
        }

        public int MaxQueueLength { get; private set; }

        public int QueueLength
        {
            get
            {
                return queue.Count;
            }
        }

        public bool NeedWorkDone { get; private set; }

        public event System.Action OnEnqueue;
        public event Action<WorkItem> BeforeStart;
        public event System.Action OnCompleteOne;
        public event System.Action OnCompleteLast;
        public event Action<WorkItem, Exception> OnError;

        private volatile bool disposing;
        private AutoResetEvent mutex;
        private Thread thread;
        private bool doingOne;

        public WorkItem[] WorkItems 
        { 
            get 
            {
                lock (queue)
                {
                    return queue.ToArray();
                }
            } 
        }

        public string Name { get; private set; }

        public ThreadPriority Priority { get { return thread.Priority; } set { thread.Priority = value; } }

        public WorkQueue(string name)
        {
            mutex = new AutoResetEvent(false);
            disposing = false;

            thread = new Thread(Do);
            thread.Name = name;
            Name = name;
            thread.Start();

            queue = new Queue<WorkItem>();
            NeedWorkDone = false;
        }

        public void Dispose()
        {
            disposing = true;
            mutex.Set();
            thread.Join();
        }

        public void Enqueue(Action action)
        {
            Enqueue(new WorkItem() { Action = action });
        }

        public void Enqueue(string name, Action action)
        {
            Enqueue(new WorkItem() { Name = name, Action = action });
        }

        public void Enqueue(WorkSet workset, string name, Action action)
        {
            Enqueue(new WorkItem() { WorkSet = workset,  Name = name, Action = action });
        }

        public void Enqueue(WorkItem item)
        {
            lock (queue)
            {
                queue.Enqueue(item);
            }
            MaxQueueLength++;

            NeedWorkDone = true;
            mutex.Set();
            
            OnEnqueue?.Invoke();
        }

        private void Do()
        {
            while (!disposing)
            {
                try
                {
                    // Check if we're disposing every 100ms.
                    while (!disposing)
                    {
                        if (mutex.WaitOne(100))
                        {
                            break;
                        }
                    }

                    if (disposing)
                        break;

                    while (NeedWorkDone && !disposing)
                    {
                        //Thread.Sleep(1000);
                        DoOne();
                        OnCompleteOne?.Invoke();
                    }

                    OnCompleteLast?.Invoke();
                }
                catch (ThreadAbortException)
                {
                    // Only abort if we're disposing
                    if (disposing)
                    {
                        throw;
                    }
                }
            }
        }

        private bool DoOne()
        {
            doingOne = true;
            WorkItem next = null;
            lock (queue)
            {
                if (queue.Any())
                {
                    next = queue.Dequeue();
                }
            }

            if (next != null)
            {
                bool result = false;
                try
                {
                    if (next != null)
                    {
                        BeforeStart?.Invoke(next);

                        next.Action();
                        result = true;
                        lock (queue)
                        {
                            if (!queue.Any())
                            {
                                NeedWorkDone = false;
                                MaxQueueLength = 0;
                            }
                        }
                    }
                }
                catch (ThreadAbortException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Logger.AllLog.LogException(next, e);

                    OnError?.Invoke(next, e);

                    if (next != null && next.WorkSet != null)
                    {
                        next.WorkSet.Error(next, e);
                    }
                }

                doingOne = false;
                return result;
            }
            else
            {
                NeedWorkDone = false;
                MaxQueueLength = 0;
                doingOne = false;
                return false;
            }
        }

        public void Clear()
        {
            lock (queue)
            {
                queue.Clear();
            }
        }

        public void Clear(WorkSet workset)
        {
            lock (queue)
            {
                WorkItem[] toKeep = queue.Where(w => w.WorkSet == workset).ToArray();
                queue.Clear();
                foreach (var wi in toKeep)
                {
                    queue.Enqueue(wi);
                }
            }
        }

        public string NextText()
        {
            lock (queue)
            {
                if (queue.Any())
                {
                    WorkItem next = queue.Peek();
                    if (next != null)
                    {
                        return next.Name;
                    }
                }
            }
            return "Loading";
        }
    }
    
    public class WorkItem
    {
        public Action Action { get; set; }
        public string Name { get; set; }
        public WorkSet WorkSet { get; set; }
    }

    public class WorkSet
    {
        public event Action<WorkItem, Exception> OnError;

        public void Error(WorkItem wi, Exception e)
        {
            OnError?.Invoke(wi, e);
        }
    }
}
