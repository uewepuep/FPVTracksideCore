using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Tools;

namespace Webb.Api
{
    // What a caller may change. Each role includes the ones below it.
    public enum ApiRole
    {
        // Read only. Anyone who can reach the web server, and anyone not signed in.
        None = 0,

        // Run stages: create, send, result, reset and lock stage races.
        Stages = 1,

        // Run the event: pilots, rounds, races, laps, race control, sound,
        // OBS scenes and triggers, meeting sync, and everything Stages can do.
        Operator = 2,

        // Everything: also settings, OBS setup, opening other events, menu
        // commands, and managing web users.
        Admin = 3,
    }

    public class ApiUserKey
    {
        public Guid Id { get; set; }
        public string Label { get; set; }

        // SHA-256 of the key. Keys are 32 random bytes, so a plain hash is enough.
        public string Hash { get; set; }

        // The start of the key, to tell keys apart in a list.
        public string Prefix { get; set; }
        public DateTime Created { get; set; }
    }

    public class ApiUser
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public ApiRole Role { get; set; }

        // pbkdf2-sha256$iterations$salt$hash; empty = no password (keys only).
        public string PasswordHash { get; set; }
        public bool Disabled { get; set; }
        public DateTime Created { get; set; }
        public List<ApiUserKey> Keys { get; set; } = new List<ApiUserKey>();
    }

    public class ApiUsersFile
    {
        // Requests from the FPVTrackside machine itself act as Admin without
        // signing in. Ignored while no Admin user exists, so the machine can
        // never lock itself out.
        public bool TrustThisMachine { get; set; } = true;
        public List<ApiUser> Users { get; set; } = new List<ApiUser>();
    }

    // Who is making a request.
    public class ApiCaller
    {
        public ApiRole Role { get; set; }

        // "this-computer", "session", "key", "environment-token" or "anonymous".
        public string Via { get; set; }

        // The signed-in user, when there is one.
        public ApiUser User { get; set; }

        public string Name
        {
            get
            {
                if (User != null) return User.Name;
                switch (Via)
                {
                    case "this-computer": return "This computer";
                    case "environment-token": return ApiAuth.TokenVariable;
                    default: return null;
                }
            }
        }

        public static readonly ApiCaller Anonymous = new ApiCaller { Role = ApiRole.None, Via = "anonymous" };
    }

    // The web users and their sign-in sessions.
    //
    // Users live in WebUsers.json in FPVTrackside's working directory, so they
    // apply to every event and profile on the machine. Passwords are PBKDF2
    // hashes and keys SHA-256 hashes; neither can be read back. Sessions are
    // kept in memory only: restarting FPVTrackside signs everyone out.
    public static class ApiUsers
    {
        public const string FileName = "WebUsers.json";
        public const int MinPasswordLength = 8;
        public const int MaxNameLength = 40;

        private const string SessionPrefix = "fts_";
        private const string KeyPrefix = "ftk_";
        private const int Pbkdf2Iterations = 100000;

        public static readonly TimeSpan SessionLength = TimeSpan.FromHours(24);

        // Failed sign-ins per address before it has to wait.
        public const int MaxFailures = 5;
        public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(1);

        private static readonly object sync = new object();
        private static ApiUsersFile data;
        private static string loadedFrom;

        private class Session
        {
            public Guid UserId;
            public DateTime Expires;
            public DateTime LastSeen;
        }

        private static readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>();

        private class Failures
        {
            public int Count;
            public DateTime First;
            public DateTime LockedUntil;
        }

        private static readonly Dictionary<string, Failures> failures = new Dictionary<string, Failures>();

        // Where the users are kept. Set by the API sandbox; otherwise
        // FPVTrackside's working directory.
        public static string FilePath { get; set; }

        private static string Path()
        {
            if (!string.IsNullOrEmpty(FilePath))
                return FilePath;
            return IOTools.ResolveFromWorkingDirectory(FileName);
        }

        // ───────────────────────── file ─────────────────────────

        private static ApiUsersFile Data
        {
            get
            {
                string path = Path();
                if (data == null || loadedFrom != path)
                {
                    data = Load(path);
                    loadedFrom = path;
                }
                return data;
            }
        }

        private static ApiUsersFile Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    ApiUsersFile f = JsonConvert.DeserializeObject<ApiUsersFile>(File.ReadAllText(path));
                    if (f != null)
                    {
                        f.Users = f.Users ?? new List<ApiUser>();
                        foreach (ApiUser u in f.Users)
                            u.Keys = u.Keys ?? new List<ApiUserKey>();
                        return f;
                    }
                }
            }
            catch (Exception e)
            {
                // A damaged file must not open the door: with no users the
                // only way in is from this machine.
                Logger.HTTP.LogException(typeof(ApiUsers), e);
            }
            return new ApiUsersFile();
        }

        private static void Save()
        {
            string path = Path();
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(Data, Formatting.Indented));
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try { File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
            }
            File.Move(tmp, path, true);
        }

        // Re-read the file on next use (for tests, and after editing it by hand).
        public static void Reload()
        {
            lock (sync)
            {
                data = null;
            }
        }

        // ───────────────────────── reading ─────────────────────────

        public static bool TrustThisMachine
        {
            get { lock (sync) { return Data.TrustThisMachine || !HasAdminLocked(); } }
        }

        public static bool TrustThisMachineSetting
        {
            get { lock (sync) { return Data.TrustThisMachine; } }
        }

        public static bool HasAdmin
        {
            get { lock (sync) { return HasAdminLocked(); } }
        }

        private static bool HasAdminLocked()
        {
            return Data.Users.Any(u => !u.Disabled && u.Role == ApiRole.Admin);
        }

        public static List<ApiUser> List()
        {
            lock (sync)
            {
                return Data.Users.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).Select(Copy).ToList();
            }
        }

        private static ApiUser Copy(ApiUser u)
        {
            return new ApiUser
            {
                Id = u.Id,
                Name = u.Name,
                Role = u.Role,
                PasswordHash = u.PasswordHash,
                Disabled = u.Disabled,
                Created = u.Created,
                Keys = u.Keys.Select(k => new ApiUserKey { Id = k.Id, Label = k.Label, Hash = k.Hash, Prefix = k.Prefix, Created = k.Created }).ToList(),
            };
        }

        private static ApiUser Find(string idOrName)
        {
            Guid id;
            if (Guid.TryParse(idOrName, out id))
                return Data.Users.FirstOrDefault(u => u.Id == id);
            return Data.Users.FirstOrDefault(u => string.Equals(u.Name, idOrName, StringComparison.OrdinalIgnoreCase));
        }

        public static ApiUser Get(string idOrName)
        {
            lock (sync)
            {
                ApiUser u = Find(idOrName);
                return u == null ? null : Copy(u);
            }
        }

        // ───────────────────────── changing ─────────────────────────

        public static string CleanName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0)
                throw ApiException.BadRequest("a user needs a name");
            if (name.Length > MaxNameLength)
                throw ApiException.BadRequest("names are at most " + MaxNameLength + " characters");
            if (name.Any(char.IsControl))
                throw ApiException.BadRequest("names can't contain control characters");
            Guid ignored;
            if (Guid.TryParse(name, out ignored))
                throw ApiException.BadRequest("a name can't be a GUID");
            return name;
        }

        public static void CheckPassword(string password)
        {
            if (password == null || password.Length < MinPasswordLength)
                throw ApiException.BadRequest("passwords are at least " + MinPasswordLength + " characters");
        }

        public static ApiUser Create(string name, ApiRole role, string password)
        {
            name = CleanName(name);
            if (role == ApiRole.None)
                throw ApiException.BadRequest("choose a role: stages, operator or admin");
            if (!string.IsNullOrEmpty(password))
                CheckPassword(password);

            string hash = string.IsNullOrEmpty(password) ? null : HashPassword(password);
            lock (sync)
            {
                if (Find(name) != null)
                    throw ApiException.Conflict("there is already a user called " + name);

                ApiUser u = new ApiUser { Id = Guid.NewGuid(), Name = name, Role = role, PasswordHash = hash, Created = DateTime.UtcNow };
                Data.Users.Add(u);
                Save();
                return Copy(u);
            }
        }

        // Change a user. Refuses a change that would leave no Admin while this
        // machine isn't trusted, because nobody could then manage users.
        public static ApiUser Update(string idOrName, string name, ApiRole? role, string password, bool? disabled)
        {
            if (name != null)
                name = CleanName(name);
            if (password != null)
                CheckPassword(password);
            string hash = password == null ? null : HashPassword(password);

            lock (sync)
            {
                ApiUser u = Find(idOrName);
                if (u == null)
                    throw ApiException.NotFound("user");

                if (name != null && !string.Equals(name, u.Name, StringComparison.OrdinalIgnoreCase) && Find(name) != null)
                    throw ApiException.Conflict("there is already a user called " + name);
                if (role == ApiRole.None)
                    throw ApiException.BadRequest("choose a role: stages, operator or admin");

                ApiUser before = Copy(u);
                if (name != null) u.Name = name;
                if (role != null) u.Role = role.Value;
                if (hash != null) u.PasswordHash = hash;
                if (disabled != null) u.Disabled = disabled.Value;

                if (!Data.TrustThisMachine && !HasAdminLocked())
                {
                    u.Name = before.Name; u.Role = before.Role; u.PasswordHash = before.PasswordHash; u.Disabled = before.Disabled;
                    throw ApiException.Conflict("that would leave no Admin user while this machine isn't trusted; nobody could manage users");
                }

                // A new password, a lower role or disabling signs them out everywhere.
                if (hash != null || u.Disabled || (role != null && role.Value < before.Role))
                    EndSessionsLocked(u.Id);

                Save();
                return Copy(u);
            }
        }

        public static void Delete(string idOrName)
        {
            lock (sync)
            {
                ApiUser u = Find(idOrName);
                if (u == null)
                    throw ApiException.NotFound("user");

                Data.Users.Remove(u);
                if (!Data.TrustThisMachine && !HasAdminLocked())
                {
                    Data.Users.Add(u);
                    throw ApiException.Conflict("that would leave no Admin user while this machine isn't trusted; nobody could manage users");
                }
                EndSessionsLocked(u.Id);
                Save();
            }
        }

        public static void SetTrustThisMachine(bool trust)
        {
            lock (sync)
            {
                if (!trust && !HasAdminLocked())
                    throw ApiException.Conflict("add an Admin user with a password first, or nobody could manage users from another machine");
                Data.TrustThisMachine = trust;
                Save();
            }
        }

        // Returns the new key, which is shown once and never again.
        public static string AddKey(string idOrName, string label, out ApiUserKey created)
        {
            string key = KeyPrefix + RandomToken();
            lock (sync)
            {
                ApiUser u = Find(idOrName);
                if (u == null)
                    throw ApiException.NotFound("user");

                created = new ApiUserKey
                {
                    Id = Guid.NewGuid(),
                    Label = string.IsNullOrWhiteSpace(label) ? "Key " + (u.Keys.Count + 1) : label.Trim(),
                    Hash = Sha256(key),
                    Prefix = key.Substring(0, KeyPrefix.Length + 6),
                    Created = DateTime.UtcNow,
                };
                u.Keys.Add(created);
                Save();
                return key;
            }
        }

        public static void DeleteKey(string idOrName, Guid keyId)
        {
            lock (sync)
            {
                ApiUser u = Find(idOrName);
                if (u == null)
                    throw ApiException.NotFound("user");
                if (u.Keys.RemoveAll(k => k.Id == keyId) == 0)
                    throw ApiException.NotFound("key");
                Save();
            }
        }

        // ───────────────────────── signing in ─────────────────────────

        // Throws 429 while the address is locked out after too many failures.
        public static string SignIn(string name, string password, string address, out ApiUser user, out DateTime expires)
        {
            user = null;
            expires = DateTime.MinValue;
            DateTime now = DateTime.UtcNow;

            lock (sync)
            {
                Failures f;
                if (failures.TryGetValue(address ?? "", out f) && f.LockedUntil > now)
                    throw new ApiException(429, "too_many_attempts", "too many failed sign-ins; try again in " + Math.Ceiling((f.LockedUntil - now).TotalSeconds) + " seconds");
            }

            ApiUser found;
            lock (sync)
            {
                ApiUser u = Find(name ?? "");
                found = u == null ? null : Copy(u);
            }

            // Hash even when there's no such user, so the time taken doesn't say which names exist.
            bool ok = VerifyPassword(password ?? "", found?.PasswordHash) && found != null && !found.Disabled;

            lock (sync)
            {
                if (!ok)
                {
                    Failures f;
                    if (!failures.TryGetValue(address ?? "", out f) || now - f.First > FailureWindow)
                    {
                        f = new Failures { First = now };
                        failures[address ?? ""] = f;
                    }
                    f.Count++;
                    if (f.Count >= MaxFailures)
                    {
                        f.LockedUntil = now + Lockout;
                        f.Count = 0;
                        f.First = now;
                    }
                    throw new ApiException(401, "unauthorized", "wrong name or password");
                }

                failures.Remove(address ?? "");
                PruneSessionsLocked(now);

                string token = SessionPrefix + RandomToken();
                expires = now + SessionLength;
                sessions[Sha256(token)] = new Session { UserId = found.Id, Expires = expires, LastSeen = now };
                user = found;
                return token;
            }
        }

        public static void SignOut(string token)
        {
            if (string.IsNullOrEmpty(token))
                return;
            lock (sync)
            {
                sessions.Remove(Sha256(token));
            }
        }

        private static void EndSessionsLocked(Guid userId)
        {
            foreach (string k in sessions.Where(s => s.Value.UserId == userId).Select(s => s.Key).ToList())
                sessions.Remove(k);
        }

        // Signed-in users who have made a request within the window. The
        // stages app polls every few seconds, so an open tab counts.
        public static int ActiveUsers(TimeSpan within)
        {
            DateTime since = DateTime.UtcNow - within;
            lock (sync)
            {
                return sessions.Values.Where(s => s.LastSeen >= since && s.Expires > DateTime.UtcNow).Select(s => s.UserId).Distinct().Count();
            }
        }

        private static void PruneSessionsLocked(DateTime now)
        {
            foreach (string k in sessions.Where(s => s.Value.Expires <= now).Select(s => s.Key).ToList())
                sessions.Remove(k);
        }

        // The user a session token or API key belongs to, or null. The user is
        // looked up on every request, so a role change applies at once.
        public static ApiCaller Resolve(string presented)
        {
            if (string.IsNullOrEmpty(presented))
                return null;

            string hash = Sha256(presented);
            lock (sync)
            {
                if (presented.StartsWith(SessionPrefix, StringComparison.Ordinal))
                {
                    Session s;
                    if (!sessions.TryGetValue(hash, out s))
                        return null;
                    if (s.Expires <= DateTime.UtcNow)
                    {
                        sessions.Remove(hash);
                        return null;
                    }
                    ApiUser u = Data.Users.FirstOrDefault(x => x.Id == s.UserId);
                    if (u == null || u.Disabled)
                        return null;
                    s.LastSeen = DateTime.UtcNow;
                    return new ApiCaller { Role = u.Role, Via = "session", User = Copy(u) };
                }

                if (presented.StartsWith(KeyPrefix, StringComparison.Ordinal))
                {
                    byte[] h = Encoding.ASCII.GetBytes(hash);
                    foreach (ApiUser u in Data.Users)
                    {
                        if (u.Disabled)
                            continue;
                        foreach (ApiUserKey k in u.Keys)
                        {
                            if (k.Hash != null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(k.Hash), h))
                                return new ApiCaller { Role = u.Role, Via = "key", User = Copy(u) };
                        }
                    }
                }
            }
            return null;
        }

        // ───────────────────────── crypto ─────────────────────────

        private static string RandomToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string Sha256(string s)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
        }

        public static string HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
            return "pbkdf2-sha256$" + Pbkdf2Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }

        // Always does the full hash, so a missing user takes as long as a wrong password.
        public static bool VerifyPassword(string password, string stored)
        {
            byte[] salt = new byte[16];
            byte[] expected = new byte[32];
            int iterations = Pbkdf2Iterations;
            bool parsed = false;

            if (!string.IsNullOrEmpty(stored))
            {
                string[] parts = stored.Split('$');
                if (parts.Length == 4 && parts[0] == "pbkdf2-sha256" && int.TryParse(parts[1], out iterations) && iterations > 0)
                {
                    try
                    {
                        salt = Convert.FromBase64String(parts[2]);
                        expected = Convert.FromBase64String(parts[3]);
                        parsed = true;
                    }
                    catch (FormatException)
                    {
                        iterations = Pbkdf2Iterations;
                    }
                }
            }

            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected) && parsed;
        }
    }
}
