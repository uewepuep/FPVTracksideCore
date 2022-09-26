using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spreadsheets
{
    public interface ISheet : IDisposable
    {
        bool Open(FileInfo file, string sheetname);

        string GetText(int r, int c);
        void SetValue(int r, int c, object value);

        IEnumerable<string> GetRowText(int i);
        IEnumerable<string> GetColumnText(int i);

        bool Calculate();
        bool Save();
    }
}
