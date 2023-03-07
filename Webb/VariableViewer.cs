using RaceLib;
using Sound;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Webb
{
    public class VariableViewer
    {

        private EventManager eventManager;
        private SoundManager soundManager;

        public VariableViewer(EventManager eventManager, SoundManager soundManager)
        {
            this.eventManager = eventManager;
            this.soundManager = soundManager;
        }

        public string DumpObject(IEnumerable<string> requestPath, int refresh, int decimalPlaces)
        {
            if (requestPath.Count() == 0)
            {
                return VariableDumper(eventManager, 0, 0);
            }

            object o = FindObject(eventManager, requestPath);
            if (o != null) 
            { 
                return VariableDumper(o, refresh, decimalPlaces);
            }

            o = FindObject(soundManager, requestPath);
            if (o != null)
            {
                return VariableDumper(o, refresh, decimalPlaces);
            }

            return "";
        }

        private string VariableDumper(object found, int refresh, int decimalPlaces)
        {
            string output = "";
            if (found != null)
            {
                if (found.GetType().IsPrimitive || found.GetType() == typeof(string))
                {
                    if (typeof(double).IsAssignableFrom(found.GetType()))
                    {
                        double dbl = (double)found;

                        dbl = Math.Round((double)dbl, decimalPlaces);

                        output += dbl.ToString();
                    }
                    else
                    {
                        output += found.ToString();
                    }
                }
                else
                {
                    output += MakeTable(found, refresh, decimalPlaces);
                }
            }
            return output;
        }

        private object FindObject(object obj, IEnumerable<string> requestPath)
        {
            Type type = obj.GetType();

            IEnumerable<string> next = requestPath.Skip(1);

            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            {
                System.Collections.IEnumerable ienum = obj as System.Collections.IEnumerable;
                int i = 0;
                foreach (object o in ienum)
                {
                    if (i.ToString() == requestPath.FirstOrDefault())
                    {
                        if (next.Count() == 0)
                        {
                            return o;
                        }
                        return FindObject(o, next);
                    }
                    i++;
                }
            }
            else
            {
                foreach (PropertyInfo pi in type.GetProperties())
                {
                    if (pi.Name == requestPath.FirstOrDefault())
                    {
                        object value = pi.GetValue(obj);

                        if (next.Count() == 0)
                        {
                            return value;
                        }

                        if (value == null)
                        {
                            return null;
                        }

                        return FindObject(value, next);
                    }
                }
            }


            return null;
        }

        private string MakeTable(object obj, int refresh, int decimalPlaces)
        {
            string append = "?refresh=" + refresh + "&decimalplaces=" + decimalPlaces;

            string output = "<table>";
            Type type = obj.GetType();

            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            {
                System.Collections.IEnumerable ienum = obj as System.Collections.IEnumerable;

                int i = 0;
                foreach (object o in ienum)
                {
                    if (o.GetType().IsPrimitive || o is string)
                    {
                        output += "<tr><td>" + o.ToString() + "</td></tr>";
                    }
                    else
                    {
                        string link = "<a href=\"" + i + "/" + append + "\">" + o.ToString() + "</a>";

                        output += "<tr><td>" + link + "</td></tr>";
                        i++;
                    }
                }
            }
            else
            {
                foreach (PropertyInfo pi in type.GetProperties())
                {
                    string link = "<a href=\"" + pi.Name + "/" + append + "\">" + pi.Name + "</a>";

                    object value = pi.GetValue(obj);
                    string strValue = "null";
                    if (value != null)
                    {
                        strValue = value.ToString();
                    }
                    output += "<tr><td>" + link + "</td><td>" + strValue + "</td></tr>";
                }
            }
            output += "</table>";
            return output;
        }
    }
}
