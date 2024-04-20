using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class ChannelCollections : IDatabaseCollection<Channel>
    {
        private static bool firstRun = true;

        private static Channel[] allChannels;

        public ChannelCollections() 
        {
            if (firstRun)
            {
                allChannels = RaceLib.Channel.AllChannels.Convert<Channel>().ToArray();

                JsonIO<Channel> io = new JsonIO<Channel>();
                io.Write("httpfiles/Channels.json", All());
                firstRun = false;
            }
        }

        public IEnumerable<Channel> All()
        {
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

        public int Delete(IEnumerable<Channel> objs)
        {
            return 0;
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
                return null;

            return All().Where(r => ids.Contains(r.ID));
        }

        public bool Insert(Channel obj)
        {
            return false;
        }

        public int Insert(IEnumerable<Channel> objs)
        {
            return 0;
        }

        public bool Update(Channel obj)
        {
            return false;
        }

        public int Update(IEnumerable<Channel> objs)
        {
            return 0;
        }

        public bool Upsert(Channel obj)
        {
            return false;
        }

        public int Upsert(IEnumerable<Channel> objs)
        {
            return 0;
        }
    }
}
