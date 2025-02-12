using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class ChannelCollection : IDatabaseCollection<Channel>
    {
        private static bool firstRun = true;


        public ChannelCollection() 
        {
            if (firstRun)
            {
                JsonIO<Channel> io = new JsonIO<Channel>();
                io.Write("httpfiles/Channels.json", All());
                firstRun = false;
            }
        }

        private Channel[] allChannels;

        public IEnumerable<Channel> All()
        {
            if (allChannels == null)
            {
                allChannels = RaceLib.Channel.AllChannels.Convert<Channel>().ToArray();
            }
            return allChannels;
        }

        public bool Delete(Guid id)
        {
            return false;
        }

        public bool Delete(Channel obj)
        {
            return false;
        }

        public bool Delete(IEnumerable<Channel> objs)
        {
            return false;
        }

        public Channel GetCreateExternalObject(int id)
        {
            throw new NotImplementedException();
        }

        public Channel GetCreateObject(Guid id)
        {
            throw new NotImplementedException();
        }

        public Channel GetObject(Guid id)
        {
            return All().FirstOrDefault(r => r.ID == id);
        }

        public IEnumerable<Channel> GetObjects(IEnumerable<Guid> ids)
        {
            if (ids == null)
                yield break;

            Channel[] ts = All().Where(r => ids.Contains(r.ID)).ToArray();

            foreach (Guid id in ids)
            {
                yield return ts.FirstOrDefault(t => t.ID == id);
            }
        }

        public bool Insert(Channel obj)
        {
            return false;
        }

        public bool Insert(IEnumerable<Channel> objs)
        {
            return false;
        }

        public bool Update(Channel obj)
        {
            return false;
        }

        public bool Update(IEnumerable<Channel> objs)
        {
            return false;
        }

        public bool Upsert(Channel obj)
        {
            return false;
        }

        public bool Upsert(IEnumerable<Channel> objs)
        {
            return false;
        }
    }
}
