using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace RaceLib
{
    public class TimedActionManager
    {
        public enum ActionTypes
        {
            Default,
            NextRaceStarts,
            NextRaceCallout
        }

        private List<Tuple<DateTime, ActionTypes, System.Action>> actionList;

        public TimedActionManager()
        {
            actionList = new List<Tuple<DateTime, ActionTypes, System.Action>>();
        }

        public void Enqueue(DateTime after, System.Action action)
        {
            Enqueue(after, ActionTypes.Default, action);
        }

        public void Enqueue(DateTime after, ActionTypes actionType, System.Action action)
        {
            Tuple<DateTime, ActionTypes, System.Action> kvp = new Tuple<DateTime, ActionTypes, System.Action>(after, actionType, action);

            lock (actionList)
            {
                actionList.Add(kvp);
            }
        }

        public void Cancel(ActionTypes type)
        {
            lock (actionList)
            {
                actionList.RemoveAll(i => i.Item2 == type);
            }
        }

        public void Update()
        {
            DateTime now = DateTime.Now;
            Tuple<DateTime, ActionTypes, System.Action>[] todo;

            lock (actionList)
            {
                todo = actionList.Where(i => i.Item1 < now).ToArray();
            }

            foreach (var kvp in todo)
            {
                try
                {
                    kvp.Item3();
                }
                catch (Exception e)
                {
                    Logger.TimingLog.LogException(this, e);
                }
            }

            lock (actionList)
            {
                actionList.RemoveAll(a => todo.Contains(a));
            }
        }
    }
}
