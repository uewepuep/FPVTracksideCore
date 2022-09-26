using RaceLib;
using Sound;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Web;
using Tools;

namespace Webb
{
    public class EventWebServer
    {
        private EventManager eventManager;
        private SoundManager soundManager;
        private IRaceControl raceControl;

        private Thread thread;
        private bool run;

        private HttpListener listener;

        public string Url { get; private set; }

        public FileInfo CSSStyleSheet { get; private set; }

        public WebRaceControl WebRaceControl { get; private set; }

        private bool localOnly;

        public EventWebServer(EventManager eventManager, SoundManager soundManager, IRaceControl raceControl)
        {
            CSSStyleSheet = new FileInfo("httpfiles/style.css");
            this.eventManager = eventManager;
            this.soundManager = soundManager;
            this.raceControl = raceControl;
            WebRaceControl = new WebRaceControl(eventManager, soundManager, raceControl);

            if (!CSSStyleSheet.Exists)
            {
                FileStream fileStream = CSSStyleSheet.Create();
                fileStream.Dispose();
            }
        }

        public bool Start()
        {
            try
            {

                Url = "http://localhost:8080/";


                run = true;

                thread = new Thread(Run);

                thread.Name = "Webb Thread";
                thread.Start();

                return true;
            }
            catch
            {
                return false;
            }


        }

        private void Run()
        {
            try
            {
                // Try open all interfaces first
                listener = new HttpListener();
                listener.Prefixes.Add(Url.Replace("localhost", "+"));
                listener.Start();
                Logger.HTTP.Log(this, "Listening on " + listener.Prefixes.FirstOrDefault());
            }
            catch (Exception ex)
            {
                localOnly = true;
                Logger.HTTP.LogException(this, ex);

                // just open localhost
                listener = new HttpListener();
                listener.Prefixes.Add(Url);
                listener.Start();
                Logger.HTTP.Log(this, "Listening on " + listener.Prefixes.FirstOrDefault());
            }

            while (run)
            {

                try
                {
                    HttpListenerContext context = listener.GetContext();
                    HandleRequest(context);
                }
                catch (HttpListenerException hex)
                {
                    Logger.HTTP.LogException(this, hex);
                    return;
                }
                catch (Exception ex)
                {
                    Logger.HTTP.LogException(this, ex);
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            HttpListenerResponse response = context.Response;

            if (context.Request.HttpMethod == "OPTIONS")
            {
                response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Accept, X-Requested-With");
                response.AddHeader("Access-Control-Allow-Methods", "GET, POST");
                response.AddHeader("Access-Control-Max-Age", "1728000");
            }
            response.AppendHeader("Access-Control-Allow-Origin", "*");

            string responseString = BuildResponse(context);

            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            System.IO.Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            // You must close the output stream.
            output.Close();
        }

        private string BuildResponse(HttpListenerContext context)
        {
            string[] requestPath = context.Request.Url.AbsolutePath.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToArray();

            string refreshText = "";

            int decimalPlaces = 2;
            int refresh = 5;

            string query = context.Request.Url.Query;
            if (query.StartsWith("?"))
            {
                string[] queries = query.Split('?', '&');

                foreach (string q in queries)
                {
                    string[] split = q.Split('=');
                    if (split.Length == 2)
                    {
                        string key = split[0].ToLower();
                        string value = split[1];

                        switch (key)
                        {
                            case "refresh":
                                int.TryParse(value, out refresh);
                                break;
                            case "decimalplaces":
                                int.TryParse(value, out decimalPlaces);
                                break;
                        }
                    }
                }
            }
            NameValueCollection nameValueCollection;
            using (Stream receiveStream = context.Request.InputStream)
            {
                using (StreamReader readStream = new StreamReader(receiveStream, System.Text.Encoding.UTF8))
                {
                    string documentContents = readStream.ReadToEnd();
                    nameValueCollection = HttpUtility.ParseQueryString(documentContents);
                }
            }

            if (refresh == 0)
            {
                refreshText = "";
            }
            else
            {
                refreshText = "<meta http-equiv=\"refresh\" content=\"" + refresh + "\" >";
            }

            string output = "<html><head>" + refreshText + "</head><link rel=\"stylesheet\" href=\"httpfiles\\style.css\"><body>";
           
            if (requestPath.Length == 0)
            {
                if (nameValueCollection.Count > 0)
                {
                    WebRaceControl.HandleInput(nameValueCollection);
                }
                output += WebRaceControl.GetHTML();
                
                output += "<h1>Variable Viewer</h2>";
                output += VariableDumper(eventManager, 0, 0);

                if (localOnly)
                {
                    string url = listener.Prefixes.FirstOrDefault();
                    url = url.Replace("localhost", "+");
                    output += "<p>By default this webserver is only accessible from this machine. To access it over the network run in an Adminstrator command prompt:</p><p> netsh http add urlacl url = \"" + url + "\" user=everyone</p><p>Then restart the software</p>";
                }

                DirectoryInfo di = new DirectoryInfo("httpfiles");

                FileInfo[] files = di.GetFiles();
                if (files.Any())
                {
                    output += "<h1>Files</h2>";
                    output += "<ul>";
                    foreach (FileInfo filename in files)
                    {
                        output += "<li>";
                        output += "<a href=\"httpfiles\\" + filename.Name + " \">" + filename.Name + "</a>";
                        output += "</li>";
                    }
                    output += "</ul>";
                }
            }
            else
            {
                if (isFileRequest(requestPath))
                {
                    return File.ReadAllText("httpfiles\\" + requestPath[1]);
                }
                else
                {
                    object found = FindObject(eventManager, requestPath);
                    if (found == null)
                    {
                        found = FindObject(soundManager, requestPath);
                    }

                    output += VariableDumper(found, refresh, decimalPlaces);
                }
            }

            output += "</body></html>";
            return output;
        }

        private bool isFileRequest(string[] requestPath)
        {
            if (requestPath.Length > 1)
            {
                return requestPath[0] == "httpfiles";
            }
            return false;
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

        public bool Stop()
        {
            run = false;
            listener.Abort();
            thread.Join();

            return true;
        }
    }
}
