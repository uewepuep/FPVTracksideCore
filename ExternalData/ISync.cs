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
        None,  
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

        event Action<SyncType, bool> RaceSyncEvent;

        bool CanSyncDownRoundRaces { get; }

        void SyncDownEvents(WorkSet workSet, WorkQueue queue);
        void SyncDownRoundRaces(WorkSet workSet, WorkQueue queue, EventManager eventManager);

        void SyncUpEvent(WorkSet workSet, WorkQueue queue, Guid eventID);
        void SyncUpResults(WorkSet workSet, WorkQueue queue, EventManager eventManager);

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

    public class LoginException : Exception
    {

    }
}
