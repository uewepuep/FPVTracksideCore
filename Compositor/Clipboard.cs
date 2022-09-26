using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Composition
{
    public interface IClipboard
    {
        string[] GetLines();

        string GetText();

        void SetLines(IEnumerable<string> items);

        void SetText(string text);
    }
}
