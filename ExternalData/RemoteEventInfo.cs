using System;

namespace ExternalData
{
    public class RemoteEventInfo
    {
        public Guid ID { get; set; }
        public int ExternalID { get; set; }
        public string Name { get; set; }
        public DateTime Start { get; set; }
        public SyncType Source { get; set; }
    }
}
