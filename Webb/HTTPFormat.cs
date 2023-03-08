using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace Webb
{
    public static class HTTPFormat
    {

        public static string ToHex(this Color color)
        {
            return "#" + string.Format("{0:X2}", color.R) + string.Format("{0:X2}", color.G) + string.Format("{0:X2}", color.B);
        }

        public static string FormatTable(IWebbTable webbTable)
        {
            string output = "";
            if (webbTable != null)
            {
                output += "<h1>" + webbTable.Name + "</h2>";
                output += "<table>";

                foreach (IEnumerable<string> row in webbTable.GetTable())
                {
                    output += "<tr>";
                    foreach (string cell in row)
                    {
                        output += "<td class=\"data\">" + cell + "</td>";
                    }
                    output += "</tr>";
                }
                output += "</table>";
            }
            return output;
        }
    }
}
