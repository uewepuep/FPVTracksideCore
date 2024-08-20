using DB;
using DB.JSON;
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
using System.Reflection.Metadata;
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

        private bool localOnly;

        public ToolColor[] ChannelColors { get; private set; }

        public EventWebServer(EventManager eventManager, SoundManager soundManager, IRaceControl raceControl, IEnumerable<Tools.ToolColor> channelColors)
        {
            CSSStyleSheet = new FileInfo("httpfiles/style.css");
            this.eventManager = eventManager;
            this.soundManager = soundManager;
            this.raceControl = raceControl;
            Url = "http://localhost:8080/";

            if (!CSSStyleSheet.Exists)
            {
                FileStream fileStream = CSSStyleSheet.Create();
                fileStream.Dispose();
            }

            ChannelColors = channelColors.ToArray();
        }

        public void Dispose() 
        {
            Stop();
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

            NameValueCollection nameValueCollection;
            using (Stream receiveStream = context.Request.InputStream)
            {
                using (StreamReader readStream = new StreamReader(receiveStream, System.Text.Encoding.UTF8))
                {
                    string documentContents = readStream.ReadToEnd();
                    nameValueCollection = HttpUtility.ParseQueryString(documentContents);
                }
            }

            if (requestPath.Length > 0)
            {
                string action = requestPath[0];
                string[] parameters = requestPath.Skip(1).ToArray();

                string content = "";
                DirectoryInfo eventRoot = new DirectoryInfo("events/" + eventManager.Event.ID.ToString());
                switch (action)
                {
                    case "events":
                        string target = string.Join('\\', requestPath);

                        if (target == "")
                            target = eventRoot.FullName;

                        if (target.Contains("."))
                        {
                            if (File.Exists(target))
                            {
                                return File.ReadAllBytes(target);
                            }
                            return new byte[0];
                        }
                        else
                        {
                            DirectoryInfo di = new DirectoryInfo(target);
                            if (eventRoot.Exists && di.Exists)
                            {
                                content += ListDirectory(eventRoot, di);
                            }
                        }
                        break;
                }
            }
            else
            {
                requestPath = new string[] { "httpfiles", "index.html" };
            }


            FileInfo file = new FileInfo(string.Join('\\', requestPath));

#if DEBUG
            string[] basehttpFilesDir = new string[] { "..", "..", "..", "..", "..", "..", "..","FPVTracksideCore","Webb" };

            string combined = Path.Combine(string.Join('\\', basehttpFilesDir), string.Join('\\', requestPath));

            FileInfo debugAdjustedFile = new FileInfo(combined);
            if (debugAdjustedFile.Exists)
            {
                file = debugAdjustedFile;
            }
#endif

            if (!file.Exists)
            {
                requestPath = new string[] { "httpfiles", "index.html" };
                file = new FileInfo(string.Join('\\', requestPath));
                if (!file.Exists)
                {
                    return new byte[0]; 
                }
            }

            HttpListenerResponse response = context.Response;
            switch (file.Extension)
            {
                case ".html":
                case ".htm":
                    response.ContentType = "text/html";
                    break;
                case ".json":
                    response.ContentType = "text/json";
                    break;
            }

            if (file.Name == "index.html")
            {
                string text = File.ReadAllText(file.FullName);

                string replaced = text.Replace("%eventDirectory%", "events/" + eventManager.EventId.ToString());

                return Encoding.ASCII.GetBytes(replaced);
            }

            return File.ReadAllBytes(file.FullName);
        }

        private byte[] SerializeASCII<T>(IEnumerable<T> ts)
        {
            JsonSerializerSettings settings = new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                DateFormatString = "yyyy/MM/dd H:mm:ss.FFF"
            };

            string json = JsonConvert.SerializeObject(ts, settings);
            return Encoding.ASCII.GetBytes(json);
        }
              
        private byte[] GetHTML(HttpListenerContext context, string content)
        {
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
            output += "<script src=\"/httpfiles/EventManager.js\"></script>";
            output += "<script src=\"/httpfiles/Formatter.js\"></script>";

            output += "<body id=\"body\">";

            output += content;

            output += "</body></html>";
            return Encoding.ASCII.GetBytes(output);
        }

        private byte[] GetFormattedHTML(HttpListenerContext context, string content)
        {
            string output = "<div class=\"top\">";
            output += "<img src=\"/img/logo.png\">";
            output += "<div class=\"time\">" + DateTime.Now.ToString("h:mm tt").ToLower() + "</div>";
            output += "</div>";


            output += "<div class=\"content\">";
            output += "<div id=\"content\"></div>";

            output += content;


            output += "</div>";

            return GetHTML(context, output);
        }

        private string ListDirectory(DirectoryInfo docRoot, DirectoryInfo target)
        {
            string content = "";

            string name = Path.GetRelativePath(docRoot.FullName, target.FullName);
            if (name == ".")
                name = "event";

            content += "<h1>" + name + "</h2>";
            content += "<ul>";

            foreach (DirectoryInfo subDir in target.GetDirectories())
            {
                content += "<li><a href=\"" + Path.GetRelativePath(docRoot.FullName, subDir.FullName) + " \">" + subDir.Name + "</a></li>";
            }

            foreach (FileInfo filename in target.GetFiles())
            {
                content += "<li><a href=\"" + Path.GetRelativePath(docRoot.FullName, filename.FullName) + " \">" + filename.Name + "</a></li>";
            }

            content += "</ul>";
            return content;
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
