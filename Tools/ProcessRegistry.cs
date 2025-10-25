using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools
{
    public class AutoRegisterProcess : System.Diagnostics.Process
    {
        public AutoRegisterProcess()
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            ProcessRegistry.Instance.Remove(this);
        }


        public bool StartAndRegister()
        {
            if (Start())
            {
                ProcessRegistry.Instance.Register(this);
                return true;
            }
            return false;
        }

    }

    public class ProcessRegistry : IDisposable
    {
        public static ProcessRegistry Instance { get; private set; }

        private List<Process> processList = new List<Process>();

        public IEnumerable<int> Ids
        {
            get
            {
                return processList.Select(p => p.Id);
            }
        }

        public ProcessRegistry()
        {
            if (Instance != null)
            {
                throw new Exception("Only one allowed");
            }    

            Instance = this;

            int[] existing = Read();
            foreach (int processId in existing)
            {
                try
                {
                    Process process = Process.GetProcessById(processId);
                    process.Kill();
                }
                catch
                {

                }
            }

            Write();
        }

        public void Dispose()
        {
            lock (processList)
            {
                KillAll(processList);
                processList.Clear();
            }

            Write();
        }

        public static void KillAll(IEnumerable<Process> processes)
        {
            foreach(Process process in processes)
            {
                if (process != null)
                {
                    process.Kill();
                }
            }
        }

        public void Register(Process process)
        {
            lock (processList)
            {
                processList.Add(process);
            }

            Write();
        }

        public void Remove(Process process)
        {
            lock (processList)
            {
                processList.Remove(process);
            }
        }

        private const string filename = "childProcesses.json";

        private int[] Read()
        {
            return IOTools.Read<int>("", filename);
        }

        private void Write()
        {
            IOTools.Write<int>("", filename, Ids.ToArray());
            Logger.AllLog.LogCall(this, Ids.ToArray());
        }
    }
}
