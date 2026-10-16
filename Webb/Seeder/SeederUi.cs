using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Webb.Seeder
{
    // Serves the Seeder web app (SeederUI, a Nuxt build) at /seeder. The
    // build is embedded in this DLL, so FPVTrackside needs no install step and
    // any phone or laptop on the network can open http://<machine>:8080/seeder.
    // Every action the page takes goes through /api/v1 — this only hands out files.
    public static class SeederUi
    {
        public const string Prefix = "seeder";
        private const string ResourcePrefix = "Webb.SeederUI/";

        private static readonly Lazy<Dictionary<string, string>> files = new Lazy<Dictionary<string, string>>(() =>
        {
            // LogicalName carries the path relative to the build folder; MSBuild
            // writes it with the host OS's separator.
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in typeof(SeederUi).Assembly.GetManifestResourceNames())
            {
                if (name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                    map[name.Substring(ResourcePrefix.Length).Replace('\\', '/')] = name;
            }
            return map;
        });

        public static bool IsBuilt { get { return files.Value.ContainsKey("index.html"); } }

        public static bool TryHandle(HttpListenerContext context, string[] path, out byte[] response)
        {
            response = null;
            if (path.Length == 0)
                return false;

            HttpListenerResponse res = context.Response;

            if (!path[0].Equals(Prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            // "/seeder" → "/seeder/" so the app's relative routing resolves.
            if (path.Length == 1 && !context.Request.Url.AbsolutePath.EndsWith("/"))
            {
                res.StatusCode = 301;
                res.RedirectLocation = "/" + Prefix + "/";
                response = new byte[0];
                return true;
            }

            if (!IsBuilt)
            {
                res.StatusCode = 503;
                res.ContentType = "text/html; charset=utf-8";
                response = Encoding.UTF8.GetBytes(
                    "<!doctype html><meta charset=utf-8><title>Seeder</title>" +
                    "<body style=\"font:16px system-ui;max-width:640px;margin:60px auto;padding:0 20px\">" +
                    "<h1>Seeder page not built</h1>" +
                    "<p>This FPVTrackside build doesn't include the Seeder web app. Run <code>npm ci &amp;&amp; npm run generate</code> " +
                    "in <code>SeederUI/</code> and rebuild, or use a release build.</p>" +
                    "<p>The stages API itself works: see <a href=\"/api/v1/docs\">/api/v1/docs</a>.</p>");
                return true;
            }

            string rel = string.Join("/", path.Skip(1));
            if (rel.Contains(".."))
                rel = "";

            string resource;
            bool fallback = false;
            if (!(rel.Length > 0 && files.Value.TryGetValue(rel, out resource))
                && !files.Value.TryGetValue(rel.Length == 0 ? "index.html" : rel.TrimEnd('/') + "/index.html", out resource))
            {
                // A file-looking miss is a real 404; anything else is a page
                // route (/stages/s/3) the app resolves itself.
                if (Path.HasExtension(rel))
                {
                    res.StatusCode = 404;
                    response = new byte[0];
                    return true;
                }
                resource = files.Value.ContainsKey("200.html") ? files.Value["200.html"] : files.Value["index.html"];
                fallback = true;
            }

            using (Stream s = typeof(SeederUi).Assembly.GetManifestResourceStream(resource))
            using (MemoryStream ms = new MemoryStream())
            {
                s.CopyTo(ms);
                response = ms.ToArray();
            }

            string ext = fallback ? ".html" : Path.GetExtension(resource).ToLowerInvariant();
            res.ContentType = ContentType(ext);
            // Hashed build assets never change; pages must always be fresh.
            res.AddHeader("Cache-Control", rel.StartsWith("_nuxt/") || rel.StartsWith("_fonts/") ? "public, max-age=31536000, immutable" : "no-cache");
            return true;
        }

        private static string ContentType(string ext)
        {
            switch (ext)
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".webp": return "image/webp";
                case ".ico": return "image/x-icon";
                case ".woff2": return "font/woff2";
                case ".woff": return "font/woff";
                case ".ttf": return "font/ttf";
                default: return "application/octet-stream";
            }
        }
    }
}
