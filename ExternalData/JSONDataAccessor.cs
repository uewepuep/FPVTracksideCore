using System;
using System.Linq;
using System.Net;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using System.Text;
using System.Reflection;
using System.Web;

namespace ExternalData
{
    public class JSONDataAccessor : IDataAccessor
    {
        public enum Encodings
        {
            JSON,
            xwwwformurlencoded
        }

        public string HTTPAuthUsername { get; set; }
        public string HTTPAuthPassword { get; set; }

        public bool WriteToConsole { get; set; }

        public string RootURL { get; private set; }

        public Encodings Encoding { get; set; }

        public string ContentType
        {
            get
            {
                switch (Encoding)
                {
                    default:
                    case Encodings.JSON:
                        return "application/json";
                    case Encodings.xwwwformurlencoded:
                        return "application/x-www-form-urlencoded";
                }
            }
        }

        private JsonSerializerSettings serializerSettings;

        public string LastFailedResponse { get; private set; }

        public JSONDataAccessor()
        {
            Encoding = Encodings.JSON;
            WriteToConsole = false;

            serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                DateFormatString = "yyy/MM/dd H:mm:ss"
            };
        }

        public JSONDataAccessor(string rootURL)
            :this()
        {
            RootURL = rootURL;
        }

        public HTTPResponseResult PutObject(IRequest request)
        {
            string url = RootURL + request.URL;

            string content = Encode(request);
            if (WriteToConsole)
            {
                Console.WriteLine(url);
                Console.WriteLine(content);
            }

            return HTTPManager.Request(new HTTPRequest(url, content) { Username = HTTPAuthUsername, Password = HTTPAuthPassword, ContentType = ContentType });
        }

        private string Encode(IRequest request)
        {
            switch (Encoding)
            {
                default:
                case Encodings.JSON:
                    return JsonConvert.SerializeObject(request, serializerSettings);
                case Encodings.xwwwformurlencoded:
                    return Getxwwwformurlencoded(request);
            }
        }

        private string Getxwwwformurlencoded(IRequest request)
        {
            string output = "";

            foreach (PropertyInfo pi in request.GetType().GetProperties())
            {
                if (pi.GetCustomAttributes().OfType<Newtonsoft.Json.JsonIgnoreAttribute>().Any())
                {
                    continue;
                }

                if (output != "")
                    output += "&";

                output += HttpUtility.UrlEncode(pi.Name) + "=" + HttpUtility.UrlEncode(pi.GetValue(request).ToString());
            }

            return output;
        }

        public void GetObject<T>(IRequest request, CallBack<T> acceptObject)
        {
            try
            {
                string requestcont = Encode(request);

                HTTPManager.Request(new HTTPRequest(RootURL + request.URL, requestcont) { Username = HTTPAuthUsername, Password = HTTPAuthPassword, ContentType = ContentType }, (string content, Exception e) =>
                {
                    try
                    { 
                        if (acceptObject != null)
                        {
                            T o;
                            if (content == null)
                            {
                                o = default(T);
                            }
                            else
                            {
                                string pageContents = DataTools.TrimJSON(content);
                                o = JsonConvert.DeserializeObject<T>(pageContents, serializerSettings);
                            }
                            acceptObject(o, e);
                        }
                    }
                    catch (Exception ex)
                    {
                        acceptObject(default(T), ex);
                    }
                });
            }
            catch (Exception e)
            {
                acceptObject(default(T), e);
            }
        }

        public T GetObject<T>(IRequest request)
        {
            HTTPResponseResult result = PutObject(request);

            if (result == null || result.AsyncWaitHandle == null)
            {
                return default(T);
            }

            result.AsyncWaitHandle.WaitOne(10000);

            string response = result.Response;

            if (WriteToConsole)
            {
                Console.WriteLine(response);
            }
      
            if (string.IsNullOrEmpty(response))
            {
                return default(T);
            }

            string pageContents = DataTools.TrimJSON(response);
            if (string.IsNullOrEmpty(pageContents))
            {
                return default(T);
            }

            try
            {
                T os = JsonConvert.DeserializeObject<T>(pageContents);
                return os;
            }
            catch (Exception e) 
            {
                LastFailedResponse = response;
                throw e;
            }
        }
    }
}

