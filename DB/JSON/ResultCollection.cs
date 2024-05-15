using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class ResultCollection : SplitJsonCollection<Result>
    {
        public ResultCollection(DirectoryInfo directoryInfo) 
            : base(directoryInfo, null)
        {
        }

        public override string GetFilename(Guid id)
        {
            if (id == Guid.Empty)
            {
                return Path.Combine(Directory.FullName, "Results.json");
            }

            return base.GetFilename(id);
        }

        protected override Guid ObjectToID(Result t)
        {
            return t.Race;
        }

        protected override IEnumerable<Result> DiskAll()
        {
            IEnumerable<Result> baseAll = base.DiskAll();

            string generalResults = GetFilename(Guid.Empty);
            return baseAll.Union(Read(generalResults));
        }
    }
}
