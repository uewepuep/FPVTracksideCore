using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools
{
    public interface IWebbTable
    {
        string Name { get; }
        IEnumerable<IEnumerable<string>> GetTable();
    }
}
