using System;
using System.Linq;
using System.Net;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using System.Text;

namespace ExternalData
{
    public delegate void ResponseDelegate(string content, Exception error);
    public class HTTPResponseResult : IAsyncResult
    {
        public object AsyncState { get { return Response; } }

        public WaitHandle AsyncWaitHandle { get { return waitHandle; } }

        public bool CompletedSynchronously { get { return false; } }

        public bool IsCompleted { get; private set; }

        public string Address { get; private set; }

        public string Response { get; private set; }
        public Exception Error { get; private set; }

        private AutoResetEvent waitHandle;

        public HTTPResponseResult(string address, out ResponseDelegate callBack)
        {
            this.Address = address;
            this.IsCompleted = false;
            waitHandle = new AutoResetEvent(false);

            callBack = SetResponse;
        }

        private void SetResponse(string response, Exception e)
        {
            this.Response = response;
            this.Error = e;
            IsCompleted = true;
            if (waitHandle != null)
            {
                waitHandle.Set();
                waitHandle.Dispose();
                waitHandle = null;
            }
        }
    }

    public class HTTPRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string Address { get; set; }
        public string Content { get; set; }
        public string ContentType { get; set; }
        public string Method { get; set; }

        public bool HasCredentials { get { return !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password); } }

        public HTTPRequest(string address, string content)
        {
            Address = address;
            Content = content;
            ContentType = "application/json";
            Method = "POST";
        }
    }

    public static class HTTPManager
    {
        public static HTTPResponseResult Request(HTTPRequest request)
        {
            ResponseDelegate callback;
            HTTPResponseResult result = new HTTPResponseResult(request.Address, out callback);

            Request(request, callback);

            return result;
        }

        public static IAsyncResult Request(HTTPRequest contentRequest, ResponseDelegate callback)
        {
            // Convert the string into a byte array.
            byte[] byteArray = Encoding.UTF8.GetBytes(contentRequest.Content);

#pragma warning disable SYSLIB0014 // Type or member is obsolete
            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(contentRequest.Address);
#pragma warning restore SYSLIB0014 // Type or member is obsolete
            request.ContentType = contentRequest.ContentType;
            request.Method = contentRequest.Method;
            request.ContentLength = byteArray.Length;

            if (contentRequest.HasCredentials)
            {
                request.Credentials = new NetworkCredential(contentRequest.Username, contentRequest.Password);
            }

            // start the asynchronous operation
            return request.BeginGetRequestStream(new AsyncCallback((IAsyncResult a) => { GetRequestStreamCallback(a, byteArray, callback); }), request);
        }

        private static IAsyncResult GetRequestStreamCallback(IAsyncResult asynchronousResult, byte[] byteArray, ResponseDelegate callback)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)asynchronousResult.AsyncState;

                // End the operation
                using (Stream postStream = request.EndGetRequestStream(asynchronousResult))
                {
                    // Write to the request stream.
                    postStream.Write(byteArray, 0, byteArray.Length);
                }
                // Start the asynchronous operation to get the response
                return request.BeginGetResponse(new AsyncCallback((IAsyncResult a) => { GetResponseCallback(a, callback); }), request);
            }
            catch (Exception e)
            {
#if DEBUG                
                System.Diagnostics.Debug.WriteLine(e.ToString());
#endif
                callback(null, e);
                return null;
            }
        }

        private static void GetResponseCallback(IAsyncResult asynchronousResult, ResponseDelegate callback)
        {
            HttpWebRequest request = null;
            HttpWebResponse response = null;
            string responseString = null;
            try
            {
                request = (HttpWebRequest)asynchronousResult.AsyncState;

                response = (HttpWebResponse)request.EndGetResponse(asynchronousResult);

                using (Stream streamResponse = response.GetResponseStream())
                {
                    using (StreamReader streamRead = new StreamReader(streamResponse))
                    {
                        responseString = streamRead.ReadToEnd();
                        callback(responseString, null);
                    }
                }
            }
            catch (System.Net.WebException we)
            {
                if (we.Response != null)
                {
                    using (Stream streamResponse = we.Response.GetResponseStream())
                    {
                        using (StreamReader streamRead = new StreamReader(streamResponse))
                        {
                            responseString = streamRead.ReadToEnd();
                            callback(responseString, null);
                        }
                    }
                }
#if DEBUG
                System.Diagnostics.Debug.WriteLine(we.ToString());
                if (responseString != null)
                {
                    System.Diagnostics.Debug.WriteLine(responseString);
                }
#endif
                callback(responseString, we);
            }
            catch (Exception e)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine(e.ToString());
                if (responseString != null)
                {
                    System.Diagnostics.Debug.WriteLine(responseString);
                }
#endif
                callback(null, e);
            }
        }
    }
}

