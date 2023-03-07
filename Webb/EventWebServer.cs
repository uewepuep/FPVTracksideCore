using RaceLib;
using Sound;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
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

            byte[] buffer = Response(context);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            System.IO.Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            // You must close the output stream.
            output.Close();
        }

        private byte[] Response(HttpListenerContext context)
        {
            string[] requestPath = context.Request.Url.AbsolutePath.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToArray();

            string refreshText = "";

            int decimalPlaces = 2;
            int refresh = 60;

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

            string output = "<html><head>" + refreshText + "</head><link rel=\"stylesheet\" href=\"/httpfiles/style.css\"><body>";

            output += "<div class=\"top\">";
            output += "<h1>FPVTrackside Webserver - </h2>";
            output += "</div>";


            string[] items = new string[] { "VariableViewer", "RaceControl", "LapRecords", "httpfiles" };


            foreach (string item in items)
            {
                output += "<a class=\"menu\" href=\"/" + item + "/\">" + item + "</a> ";
            }

            output += "<div class=\"content\">";
            if (requestPath.Length == 0)
            {
                if (localOnly)
                {
                    string url = listener.Prefixes.FirstOrDefault();
                    url = url.Replace("localhost", "+");
                    output += "<p>By default this webserver is only accessible from this machine. To access it over the network run in an Adminstrator command prompt:</p><p> netsh http add urlacl url = \"" + url + "\" user=everyone</p><p>Then restart the software</p>";
                }
            }
            else
            {
                switch (requestPath[0])
                {
                    case "VariableViewer":
                        VariableViewer vv = new VariableViewer(eventManager, soundManager);
                        output += vv.DumpObject(requestPath.Skip(1), refresh, decimalPlaces);
                        break;
                    case "RaceControl":
                        if (nameValueCollection.Count > 0)
                        {
                            WebRaceControl.HandleInput(nameValueCollection);
                        }
                        output += WebRaceControl.GetHTML();
                        break;
                    case "LapRecords":
                        break;
                    case "httpfiles":
                    case "themes":
                        if (isFileRequest(requestPath))
                        {
                            string filename = string.Join('\\', requestPath);
                            return File.ReadAllBytes(filename);
                        }
                        else
                        {
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
                        break;
                }
            }
            
            output += "</div>";
            output += "</body></html>";
            return Encoding.ASCII.GetBytes(output);
        }

        private bool isFileRequest(string[] requestPath)
        {
            if (requestPath.Length > 1)
            {
                return requestPath[0] == "httpfiles" || requestPath[0] == "themes";
            }
            return false;
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
