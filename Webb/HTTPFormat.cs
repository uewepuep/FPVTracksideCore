using Microsoft.Xna.Framework;
using RaceLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Tools;

namespace Webb
{
    public static class HTTPFormat
    {

        public static string ToHex(this Color color)
        {
            return "#" + string.Format("{0:X2}", color.R) + string.Format("{0:X2}", color.G) + string.Format("{0:X2}", color.B);
        }

        public static string FormatTable(IWebbTable webbTable, string className)
        {
            string output = "";
            if (webbTable != null)
            {
                output += "<h2>" + webbTable.Name + "</h2>";
                output += "<div class=\"" + className + "\">";

                int id = 0;
                
                List<IEnumerable<string>> table = webbTable.GetTable().ToList();

                string[] headings = webbTable.GetHeadings().ToArray();

                foreach (IEnumerable<string> row in table)
                {
                    output += "<div class=\"row\">";
                    int i = 0;
                    foreach (string cell in row)
                    {
                        string cellClass;
                        if (headings.Length > i)
                        {
                            cellClass = Regex.Replace(headings[i], "[^a-zA-Z]", "").ToLower();
                        }
                        else
                        {
                            cellClass = "";
                        }

                        output += "<div id=\"" + id + "\"class=\"" + cellClass + "\">" + cell + "</div>";
                        i++;
                    }
                    output += "</div>";
                    id++;
                }
                output += "</div>";

                output += "<div id=\"json\" style=\"display:none;\">";
                output += Newtonsoft.Json.JsonConvert.SerializeObject(table);
                output += "</div>";
            }

            output += "<br><br><a href=\"?autoscroll=true\">Autoscroll</a>";

            return output;
        }
    }
}
