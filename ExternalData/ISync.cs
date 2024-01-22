using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace ExternalData
{
    public interface ISync
    {
        event Action<bool> RaceSyncEvent;

        void SyncEvents(WorkSet workSet, WorkQueue queue, CallBacks<RaceLib.Event> callBack);
        void SyncDownRoundRaces(WorkSet workSet, WorkQueue queue, EventManager eventManager);
        void SyncUpResults(WorkSet workSet, WorkQueue queue, EventManager eventManager);
        void SyncUpResults(WorkSet workSet, WorkQueue queue, IEnumerable<Guid> eventIDs, Action callBack);

        bool Login(string username, string password);
        void CreateAccount();
        void ForgotPassword();
    }
}
