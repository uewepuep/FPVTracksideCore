using ExternalData;
using Newtonsoft.Json;
using RaceLib;
using Sound;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing.Imaging;
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
    public class EventWebServer : IDisposable
    {
        private EventManager eventManager;
        private SoundManager soundManager;
        private IRaceControl raceControl;

        private Thread thread;
        public bool Running { get; private set; }

        private HttpListener listener;

        public string Url { get; private set; }

        public FileInfo CSSStyleSheet { get; private set; }

        public WebRaceControl WebRaceControl { get; private set; }

        private bool localOnly;

        private IWebbTable[] webbTables;

        public ToolColor[] ChannelColors { get; private set; }

        public EventWebServer(EventManager eventManager, SoundManager soundManager, IRaceControl raceControl, IEnumerable<IWebbTable> tables, IEnumerable<Tools.ToolColor> channelColors)
        {
            CSSStyleSheet = new FileInfo("httpfiles/style.css");
            this.eventManager = eventManager;
            this.soundManager = soundManager;
            this.raceControl = raceControl;
            WebRaceControl = new WebRaceControl(eventManager, soundManager, raceControl);
            Url = "http://localhost:8080/";

            if (!CSSStyleSheet.Exists)
            {
                FileStream fileStream = CSSStyleSheet.Create();
                fileStream.Dispose();
            }

            webbTables = tables.ToArray();

            ChannelColors = channelColors.ToArray();
        }

        public void Dispose() 
        {
            Stop();
        }


        public IEnumerable<string> GetPages()
        {
            yield return "Rounds";
            yield return "Event Status";

            foreach (IWebbTable table in webbTables)
            {
                yield return table.Name;
            }

            if (raceControl != null) 
            {
                yield return "RaceControl";
                yield return "Variable Viewer";
            }
        }

        public bool Start()
        {
            try
            {
                Running = true;

                if (thread != null)
                {
                    Stop();
                }
                
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
            while (Running)
            {

                if (listener == null)
                {
                    CreateListener();
                }

                try
                {
                    HttpListenerContext context = listener.GetContext();
                    HandleRequest(context);

                }
                catch (Exception ex)
                {
                    Logger.HTTP.LogException(this, ex);
                    listener.Abort();
                    listener = null;
                }
            }
        }

        private void CreateListener()
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
            string path = Uri.UnescapeDataString(context.Request.Url.AbsolutePath);
            string[] requestPath = path.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToArray();

            string refreshText = "";

            int decimalPlaces = 2;
            int refresh = 60;
            bool autoScroll = false;

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
                            case "autoscroll":
                                bool.TryParse(value, out autoScroll);
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

            string output = "<html><head>" + refreshText + "</head><link rel=\"stylesheet\" href=\"/httpfiles/style.css\">";

            if (autoScroll)
                output += "<script src=\"/httpfiles/scroll.js\"></script>";
            
            output += "<body>";

            string heading = "<div class=\"top\">";
            heading += "<img src=\"/img/logo.png\">";
            heading += "<div class=\"time\">" + DateTime.Now.ToString("h:mm tt").ToLower() + "</div>";
            heading += "</div>";

            IEnumerable<string> items = GetPages();

            string content = "<div class=\"content\">";
            if (requestPath.Length == 0)
            {
                foreach (string item in items.OrderBy(r =>r))
                {
                    content += "<br><a class=\"menu\" href=\"/" + item + "/\">" + item + "</a> ";
                }

                if (localOnly)
                {
                    string url = listener.Prefixes.FirstOrDefault();
                    url = url.Replace("localhost", "+");
                    content += "<p>By default this webserver is only accessible from this machine. To access it over the network run in an Adminstrator command prompt:</p><p> netsh http add urlacl url = \"" + url + "\" user=everyone</p><p>Then restart the software</p>";
                }
            }
            else
            {
                string action = requestPath[0];
                string[] parameters = requestPath.Skip(1).ToArray();

                switch (action)
                {
                    case "VariableViewer":
                        heading = "";

                        VariableViewer vv = new VariableViewer(eventManager, soundManager);
                        content += vv.DumpObject(parameters, refresh, decimalPlaces);
                        break;
                    case "RaceControl":
                        if (nameValueCollection.Count > 0)
                        {
                            WebRaceControl.HandleInput(nameValueCollection);
                        }
                        content += WebRaceControl.GetHTML();
                        break;
                    case "httpfiles":
                    case "img":
                    case "themes":
                        if (requestPath.Length > 1)
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
                                content += "<h1>Files</h2>";
                                content += "<ul>";
                                foreach (FileInfo filename in files)
                                {
                                    content += "<li>";
                                    content += "<a href=\"httpfiles\\" + filename.Name + " \">" + filename.Name + "</a>";
                                    content += "</li>";
                                }
                                content += "</ul>";
                            }
                        }
                        break;
                    case "Rounds":
                        content += WebbRounds.Rounds(eventManager);
                        break;
                    case "Event Status":
                        content += WebbRounds.EventStatus(eventManager, webbTables.FirstOrDefault());
                        break;

                    case "Lap Count":
                        IWebbTable webbTable2 = webbTables.FirstOrDefault(w => w.Name == action);
                        content += HTTPFormat.FormatTable(webbTable2, "");
                        break;

                    default:
                        IWebbTable webbTable = webbTables.FirstOrDefault(w => w.Name == action);
                        content += HTTPFormat.FormatTable(webbTable, "columns");
                        break;
                }
            }
            content += "</div>";

            output += heading + content;

            output += "</body></html>";
            return Encoding.ASCII.GetBytes(output);
        }
              

        public bool Stop()
        {
            Running = false;
            listener?.Abort();
            thread?.Join();

            return true;
        }
    }
}
