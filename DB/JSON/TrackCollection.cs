using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class TrackCollection : SplitJsonCollection<Track>
    {
        public TrackCollection(DirectoryInfo directoryInfo) 
            : base(directoryInfo, "track")
        {
        }

        public override DirectoryInfo GetDirectoryInfo(Guid id)
        {
            return Directory;
        }

        public override string GetFilename(Guid id)
        {
            return Path.Combine(Directory.FullName, id.ToString() + ".fpvtrk");
        }

        protected override IEnumerable<Track> DiskAll()
        {
            foreach (FileInfo di in Directory.EnumerateFiles())
            {
                string name = di.Name.Replace(di.Extension, "");
                if (Guid.TryParse(name, out Guid id) && id != Guid.Empty)
                {
                    string filename = GetFilename(id);
                    Track[] ts = jsonIO.Read(filename);
                    foreach (Track t in ts)
                    {
                        if (t != null)
                            yield return t;
                    }
                }
            }
        }
    }
}
