using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace ExternalData
{
    public static class DataTools
    {
        public static string TrimJSON(string json)
        {
            if (json == null)
                return null;

            int a = json.IndexOf('[');
            int b = json.IndexOf('{');

            int toUse;

            if (a == -1)
            {
                toUse = b;
            }
            else if (b == -1)
            {
                toUse = a;
            }
            else
            {
                toUse = Math.Min(a, b);
            }

            if (toUse > 0)
            {
                json = json.Substring(toUse);
            }
            return json.Trim();
        }

        public static void StartBrowser(string url)
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
    }
    public delegate void ResultCallback<T>(T result);
}

