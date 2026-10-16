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
    // files already are (settings and OBS setup excepted: those need Admin).
    // Changes need a role (see ApiRole), which a caller gets in one of four ways:
    //
    //  * signing in as a web user (POST /auth/login) and sending the session
    //    token it returns;
    //  * sending one of a web user's API keys, for scripts and other systems;
    //  * sending the FPV_API_TOKEN environment variable, which acts as Admin
    //    (kept for setups made before there were users);
    //  * being on the FPVTrackside machine itself, which acts as Admin unless
    //    "trust this machine" is switched off. A page from another website
    //    open in this machine's browser does NOT count as this machine: the
    //    request has to come from FPVTrackside's own pages or from a program,
    //    and name this machine, so a malicious page can't drive the API from
    //    the race laptop's browser.
    //
    // Tokens and keys go in 'Authorization: Bearer <token>' (or X-Api-Token),
    // never in a cookie, so another website can't make the browser send them.
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

        // The connection comes from this machine. False when the connection has
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

        // The connection comes from this machine AND the request was made by
        // FPVTrackside's own pages or by a program, not by another website.
        public static bool IsTrustedLocal(HttpListenerContext context)
        {
            if (!IsLocal(context))
                return false;

            string fetchSite = context.Request.Headers["Sec-Fetch-Site"];
            string origin = context.Request.Headers["Origin"];
            string host = context.Request.Headers["Host"];
            return IsSameSiteRequest(fetchSite, origin, host, context.Request.Url?.Scheme ?? "http")
                && NamesThisMachine(context.Request.Url?.Host);
        }

        // A browser marks every request with where it came from. Programs
        // (curl, scripts, dd-pits) send neither header.
        public static bool IsSameSiteRequest(string fetchSite, string origin, string host, string scheme)
        {
            if (!string.IsNullOrEmpty(fetchSite)
                && !fetchSite.Equals("same-origin", StringComparison.OrdinalIgnoreCase)
                && !fetchSite.Equals("none", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(origin))
            {
                if (string.IsNullOrEmpty(host))
                    return false;
                return string.Equals(origin.TrimEnd('/'), scheme + "://" + host, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        // The Host header names this machine: an IP address, localhost or the
        // machine's own name. Anything else is a hostname some other site
        // pointed at 127.0.0.1 (DNS rebinding), and is not trusted.
        public static bool NamesThisMachine(string host)
        {
            if (string.IsNullOrEmpty(host))
                return false;

            host = host.Trim('[', ']').TrimEnd('.');
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip))
                return true;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            string machine = Environment.MachineName;
            if (string.IsNullOrEmpty(machine))
                return false;
            // "laptop" or "laptop.local"; a longer name could be anyone's.
            return host.Equals(machine, StringComparison.OrdinalIgnoreCase)
                || host.Equals(machine + ".local", StringComparison.OrdinalIgnoreCase);
        }

        // Who is making this request, and what they may do.
        public static ApiCaller Identify(HttpListenerContext context)
        {
            string presented = PresentedToken(context);
            if (!string.IsNullOrEmpty(presented))
            {
                ApiCaller user = ApiUsers.Resolve(presented);
                if (user != null)
                    return user;
                if (TokenMatches(presented, ConfiguredTokens()))
                    return new ApiCaller { Role = ApiRole.Admin, Via = "environment-token" };
            }

            if (ApiUsers.TrustThisMachine && IsTrustedLocal(context))
                return new ApiCaller { Role = ApiRole.Admin, Via = "this-computer" };

            return ApiCaller.Anonymous;
        }

        public static bool CanWrite(HttpListenerContext context)
        {
            return Identify(context).Role > ApiRole.None;
        }

        public static ApiCaller Require(HttpListenerContext context, ApiRole needed)
        {
            ApiCaller caller = Identify(context);
            if (caller.Role >= needed)
                return caller;

            if (caller.Role == ApiRole.None)
            {
                if (!string.IsNullOrEmpty(PresentedToken(context)))
                    throw new ApiException(401, "unauthorized", "that sign-in or key isn't valid any more (signed out, expired or removed); sign in again");
                throw new ApiException(401, "unauthorized", "sign in first (POST /api/v1/auth/login), or send an API key as 'Authorization: Bearer <key>'");
            }

            throw new ApiException(403, "forbidden", caller.Name + " is " + RoleName(caller.Role) + "; this needs " + RoleName(needed));
        }

        public static string RoleName(ApiRole role)
        {
            switch (role)
            {
                case ApiRole.Seeder: return "Seeder";
                case ApiRole.Operator: return "Operator";
                case ApiRole.Admin: return "Admin";
                default: return "read only";
            }
        }

        public static string RoleKey(ApiRole role)
        {
            return role == ApiRole.None ? "none" : role.ToString().ToLowerInvariant();
        }

        public static ApiRole ParseRole(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "seeder": return ApiRole.Seeder;
                case "operator": return ApiRole.Operator;
                case "admin": return ApiRole.Admin;
                default: throw ApiException.BadRequest("role must be seeder, operator or admin");
            }
        }

        // The address a request came from, for counting failed sign-ins.
        public static string RemoteAddress(HttpListenerContext context)
        {
            try
            {
                IPAddress a = context.Request.RemoteEndPoint?.Address;
                if (a == null)
                    return "";
                if (a.IsIPv4MappedToIPv6)
                    a = a.MapToIPv4();
                return a.ToString();
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
