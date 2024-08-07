using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace ExternalData
{
    public enum SyncType
    {
        FPVTrackside,
        MultiGP
    }

    public enum LoginType
    {
        Success,
        InvalidPassword,
        Error
    }

    public interface ISync
    {
        SyncType SyncType { get; }

        event Action<bool> RaceSyncEvent;

        void SyncEvents(WorkSet workSet, WorkQueue queue, CallBacks<RaceLib.Event> callBack);
        void SyncDownRoundRaces(WorkSet workSet, WorkQueue queue, EventManager eventManager);
        void SyncUpResults(WorkSet workSet, WorkQueue queue, EventManager eventManager);
        void SyncUpResults(WorkSet workSet, WorkQueue queue, IEnumerable<Guid> eventIDs, Action callBack);

        LoginType Login(string authkey);
        void CreateAccount();
        void GetAuth();
    }

    public interface ITrackProvider
    {
        IEnumerable<Track> GetTracks();

        bool UploadTrack(Track track); 

        public bool CanUpload { get; }
    }
}
