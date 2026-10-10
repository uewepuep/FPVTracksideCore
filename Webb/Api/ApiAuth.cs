using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Webb.Api
{
    // Who may change things through the API.
    //
    // Reads are open to anyone who can reach the web server, as the event's JSON
    // files already are. Changes are accepted from this machine, or from another
    // one that presents the API token set in the FPV_API_TOKEN environment
    // variable. With no token set, only this machine can make changes.
    public static class ApiAuth
    {
        public const string TokenVariable = "FPV_API_TOKEN";

        public static string[] ConfiguredTokens()
        {
            string token = Environment.GetEnvironmentVariable(TokenVariable);
            return string.IsNullOrEmpty(token) ? new string[0] : new[] { token };
        }

        // Authorization: Bearer <token>, or X-Api-Token: <token>.
        public static string PresentedToken(HttpListenerContext context)
        {
            string auth = context.Request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return auth.Substring(7).Trim();

            return context.Request.Headers["X-Api-Token"];
        }

        public static bool TokenMatches(string presented, string[] configured)
        {
            if (string.IsNullOrEmpty(presented))
                return false;

            byte[] p = Encoding.UTF8.GetBytes(presented);
            bool match = false;
            foreach (string token in configured)
            {
                byte[] t = Encoding.UTF8.GetBytes(token);
                // Constant time for equal lengths; the length itself isn't secret enough to matter.
                if (t.Length == p.Length && CryptographicOperations.FixedTimeEquals(t, p))
                    match = true;
            }
            return match;
        }

        // The request came from this machine. False when the connection has
        // already gone (HttpListener throws rather than answering then).
        public static bool IsLocal(HttpListenerContext context)
        {
            try
            {
                return context.Request.IsLocal;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool CanWrite(HttpListenerContext context)
        {
            return IsLocal(context) || TokenMatches(PresentedToken(context), ConfiguredTokens());
        }

        public static void RequireWrite(HttpListenerContext context)
        {
            if (IsLocal(context))
                return;

            string[] tokens = ConfiguredTokens();
            if (tokens.Length == 0)
                throw new ApiException(403, "forbidden", "changes are only accepted from this machine; set " + TokenVariable + " on it to allow other machines");

            if (!TokenMatches(PresentedToken(context), tokens))
                throw new ApiException(401, "unauthorized", "send the API token as 'Authorization: Bearer <token>'");
        }
    }
}
