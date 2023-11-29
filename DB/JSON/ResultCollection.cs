using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DB.JSON
{
    public class ResultCollection : SplitDirJsonCollection<Result>
    {
        public ResultCollection(DirectoryInfo directoryInfo) 
            : base(directoryInfo, null)
        {
        }

        protected override Guid ObjectToID(Result t)
        {
            return t.Race;
        }
    }
}
