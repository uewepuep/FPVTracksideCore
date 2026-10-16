using System;
using System.Collections.Generic;
using System.Linq;

namespace Webb.Api
{
    // Signing in and web users: /auth/* and /users.
    public partial class RestApi
    {
        private void RegisterAccess()
        {
            const string tag = "Access";

            Open("GET", "auth/me", tag, "Who am I", null, typeof(AccessMeDto), Me,
                "Who this caller is, what they may change, and the roles there are. Open to everyone; a caller who isn't signed in gets role `none`.");
            Open("POST", "auth/login", tag, "Sign in", typeof(LoginRequest), typeof(LoginDto), Login,
                "Signs in as a web user and returns a session token to send as `Authorization: Bearer <token>`. Sessions last " + ApiUsers.SessionLength.TotalHours + " hours and end when FPVTrackside closes. "
                + "After " + ApiUsers.MaxFailures + " wrong passwords from one address, sign-ins from it are refused (429) for a minute.");
            Open("POST", "auth/logout", tag, "Sign out", null, typeof(OkDto), r =>
            {
                ApiUsers.SignOut(ApiAuth.PresentedToken(r.Context));
                return new OkDto { Ok = true };
            }, "Ends the session whose token is sent. API keys are not affected.");
            Add(new ApiRoute
            {
                Method = "POST", Template = "auth/password", Tag = tag, Summary = "Change my password",
                Description = "For a signed-in user (session or API key). Ends every other session of that user.",
                RequestType = typeof(PasswordChangeRequest), ResponseType = typeof(OkDto), Writes = true, Role = ApiRole.Seeder, OffUIThread = true,
                Handler = ChangeOwnPassword,
            });

            Admin("GET", "users", tag, "List web users", null, typeof(UserDto), r => ApiUsers.List().Select(ToDto).ToList(), responseArray: true);
            Admin("POST", "users", tag, "Add a web user", typeof(UserCreateRequest), typeof(UserDto), r =>
            {
                UserCreateRequest b = r.BodyAs<UserCreateRequest>();
                ApiUser u = ApiUsers.Create(b.Name, ApiAuth.ParseRole(b.Role), b.Password);
                LogAccess(r, "added user " + u.Name + " (" + ApiAuth.RoleName(u.Role) + ")");
                r.Status = 201;
                return ToDto(u);
            }, "`role` is `stages`, `operator` or `admin`. A user without a password can only use API keys.", status: 201);
            Admin("PATCH", "users/{userId}", tag, "Change a web user", typeof(UserPatchRequest), typeof(UserDto), r =>
            {
                UserPatchRequest b = r.BodyAs<UserPatchRequest>();
                ApiUser u = ApiUsers.Update(r.Param("userId"), b.Name, b.Role == null ? (ApiRole?)null : ApiAuth.ParseRole(b.Role), b.Password, b.Disabled);
                LogAccess(r, "changed user " + u.Name);
                return ToDto(u);
            }, "Only the fields sent change. A new password, a lower role or `disabled: true` signs the user out everywhere.");
            Admin("DELETE", "users/{userId}", tag, "Remove a web user", null, typeof(OkDto), r =>
            {
                ApiUser u = ApiUsers.Get(r.Param("userId"));
                ApiUsers.Delete(r.Param("userId"));
                LogAccess(r, "removed user " + u?.Name);
                return new OkDto { Ok = true };
            }, "Their sessions and API keys stop working straight away.");
            Admin("POST", "users/{userId}/keys", tag, "Make an API key for a user", typeof(KeyCreateRequest), typeof(KeyCreatedDto), r =>
            {
                ApiUserKey created;
                string key = ApiUsers.AddKey(r.Param("userId"), r.BodyAs<KeyCreateRequest>().Label, out created);
                LogAccess(r, "made API key '" + created.Label + "' for " + r.Param("userId"));
                r.Status = 201;
                return new KeyCreatedDto { Key = key, Info = ToDto(created) };
            }, "For scripts and other systems (e.g. a timing dashboard). The key acts with the user's role and is shown only in this response.", status: 201);
            Admin("DELETE", "users/{userId}/keys/{keyId}", tag, "Remove an API key", null, typeof(OkDto), r =>
            {
                ApiUsers.DeleteKey(r.Param("userId"), r.GuidParam("keyId"));
                LogAccess(r, "removed an API key of " + r.Param("userId"));
                return new OkDto { Ok = true };
            });

            Admin("GET", "access", tag, "Access settings", null, typeof(AccessSettingsDto), r => AccessSettings());
            Admin("PATCH", "access", tag, "Change access settings", typeof(AccessSettingsRequest), typeof(AccessSettingsDto), r =>
            {
                AccessSettingsRequest b = r.BodyAs<AccessSettingsRequest>();
                if (b.TrustThisMachine != null)
                {
                    ApiUsers.SetTrustThisMachine(b.TrustThisMachine.Value);
                    LogAccess(r, "trust this machine " + (b.TrustThisMachine.Value ? "on" : "off"));
                }
                return AccessSettings();
            }, "`trustThisMachine: false` makes people on the FPVTrackside machine sign in too. It needs an Admin user first, so nobody is locked out.");

            Examples(new Dictionary<string, string>
            {
                ["POST auth/login"] = "{ \"name\": \"Alex\", \"password\": \"correct horse battery\" }",
                ["POST users"] = "{ \"name\": \"Alex\", \"role\": \"operator\", \"password\": \"correct horse battery\" }",
                ["PATCH users/{userId}"] = "{ \"role\": \"stages\" }",
                ["POST users/{userId}/keys"] = "{ \"label\": \"Club dashboard\" }",
                ["PATCH access"] = "{ \"trustThisMachine\": false }",
            });
        }

        private void Add(ApiRoute route)
        {
            router.Add(route);
        }

        private void Open(string method, string template, string tag, string summary, Type request, Type response, Func<ApiRequest, object> handler, string description = null)
        {
            Add(new ApiRoute { Method = method, Template = template, Tag = tag, Summary = summary, Description = description, RequestType = request, ResponseType = response, Role = ApiRole.None, OffUIThread = true, Handler = handler });
        }

        private void Admin(string method, string template, string tag, string summary, Type request, Type response, Func<ApiRequest, object> handler, string description = null, int status = 200, bool responseArray = false)
        {
            Add(new ApiRoute { Method = method, Template = template, Tag = tag, Summary = summary, Description = description, RequestType = request, ResponseType = response, ResponseIsArray = responseArray, SuccessStatus = status, Writes = method != "GET", Role = ApiRole.Admin, OffUIThread = true, Handler = handler });
        }

        private object Me(ApiRequest r)
        {
            ApiCaller c = r.Caller;
            return new AccessMeDto
            {
                Role = ApiAuth.RoleKey(c.Role),
                Name = c.Name,
                Via = c.Via,
                UserId = c.User?.Id,
                CanManageUsers = c.Role >= ApiRole.Admin,
                OnThisMachine = ApiAuth.IsTrustedLocal(r.Context),
                Roles = RoleList(),
            };
        }

        private object Login(ApiRequest r)
        {
            LoginRequest b = r.BodyAs<LoginRequest>();
            if (string.IsNullOrWhiteSpace(b.Name) || string.IsNullOrEmpty(b.Password))
                throw ApiException.BadRequest("send name and password");

            ApiUser user;
            DateTime expires;
            string token;
            try
            {
                token = ApiUsers.SignIn(b.Name.Trim(), b.Password, ApiAuth.RemoteAddress(r.Context), out user, out expires);
            }
            catch (ApiException e) when (e.Status == 401)
            {
                LogAccess(r, "failed sign-in as " + b.Name.Trim());
                throw;
            }
            LogAccess(r, user.Name + " signed in");
            return new LoginDto { Token = token, Expires = expires.ToLocalTime(), User = ToDto(user), Role = ApiAuth.RoleKey(user.Role) };
        }

        private object ChangeOwnPassword(ApiRequest r)
        {
            ApiUser user = r.Caller.User;
            if (user == null)
                throw ApiException.BadRequest("only a signed-in web user has a password to change");

            PasswordChangeRequest b = r.BodyAs<PasswordChangeRequest>();
            if (!ApiUsers.VerifyPassword(b.CurrentPassword ?? "", ApiUsers.Get(user.Id.ToString())?.PasswordHash))
                throw new ApiException(403, "forbidden", "the current password is wrong");

            ApiUsers.Update(user.Id.ToString(), null, null, b.NewPassword, null);
            LogAccess(r, user.Name + " changed their password");
            return new OkDto { Ok = true, Message = "Password changed. Sign in again." };
        }

        private static AccessSettingsDto AccessSettings()
        {
            return new AccessSettingsDto
            {
                TrustThisMachine = ApiUsers.TrustThisMachineSetting,
                TrustInEffect = ApiUsers.TrustThisMachine,
                HasAdmin = ApiUsers.HasAdmin,
                EnvironmentTokenSet = ApiAuth.ConfiguredTokens().Length > 0,
                Roles = RoleList(),
            };
        }

        private static List<RoleDto> RoleList()
        {
            return new List<RoleDto>
            {
                new RoleDto { Key = "seeder", Name = "Seeder", Description = "Run Seeder stages: create them, send races to FPVTrackside, enter, reset and lock stage results." },
                new RoleDto { Key = "operator", Name = "Operator", Description = "Run the event: pilots, rounds, races, laps, race control, sound, OBS scenes and the on-screen view, plus everything Stages can do." },
                new RoleDto { Key = "admin", Name = "Admin", Description = "Everything: also settings, OBS setup, menu commands, opening other events, and web users." },
            };
        }

        private static UserDto ToDto(ApiUser u)
        {
            return new UserDto
            {
                Id = u.Id,
                Name = u.Name,
                Role = ApiAuth.RoleKey(u.Role),
                Disabled = u.Disabled,
                HasPassword = !string.IsNullOrEmpty(u.PasswordHash),
                Created = u.Created.ToLocalTime(),
                Keys = u.Keys.Select(ToDto).ToList(),
            };
        }

        private static KeyDto ToDto(ApiUserKey k)
        {
            return new KeyDto { Id = k.Id, Label = k.Label, Prefix = k.Prefix + "…", Created = k.Created.ToLocalTime() };
        }

        private static void LogAccess(ApiRequest r, string what)
        {
            Tools.Logger.HTTP.Log(typeof(RestApi), "Access: " + what + " (by " + (r.Caller?.Name ?? "anonymous") + " from " + ApiAuth.RemoteAddress(r.Context) + ")");
        }
    }

    [Doc("Who the caller is.")]
    public class AccessMeDto
    {
        [Doc("What the caller may change.", Values = new[] { "none", "seeder", "operator", "admin" })] public string Role { get; set; }
        [Doc("The user's name, \"This computer\" or FPV_API_TOKEN; empty when not signed in.")] public string Name { get; set; }
        [Doc("How the caller was recognised.", Values = new[] { "this-computer", "session", "key", "environment-token", "anonymous" })] public string Via { get; set; }
        [Doc("The signed-in user's ID.")] public Guid? UserId { get; set; }
        [Doc("Whether the caller may manage web users.")] public bool CanManageUsers { get; set; }
        [Doc("Whether the request came from the FPVTrackside machine's own browser or a program on it.")] public bool OnThisMachine { get; set; }
        [Doc("The roles a user can have, lowest first.")] public List<RoleDto> Roles { get; set; }
    }

    [Doc("A role a web user can have.")]
    public class RoleDto
    {
        [Doc("Role key.", Example = "operator")] public string Key { get; set; }
        [Doc("Role name.")] public string Name { get; set; }
        [Doc("What it allows.")] public string Description { get; set; }
    }

    [Doc("Sign in as a web user.")]
    public class LoginRequest
    {
        [Doc("User name (not case-sensitive).", Required = true)] public string Name { get; set; }
        [Doc("Password.", Required = true)] public string Password { get; set; }
    }

    [Doc("A signed-in session.")]
    public class LoginDto
    {
        [Doc("Send as 'Authorization: Bearer <token>'. Keep it private.")] public string Token { get; set; }
        [Doc("When the session ends.")] public DateTime Expires { get; set; }
        [Doc("The signed-in user.")] public UserDto User { get; set; }
        [Doc("The user's role.", Values = new[] { "seeder", "operator", "admin" })] public string Role { get; set; }
    }

    [Doc("Change your own password.")]
    public class PasswordChangeRequest
    {
        [Doc("The password now.", Required = true)] public string CurrentPassword { get; set; }
        [Doc("The new password (at least 8 characters).", Required = true)] public string NewPassword { get; set; }
    }

    [Doc("A web user.")]
    public class UserDto
    {
        [Doc("User ID.")] public Guid Id { get; set; }
        [Doc("Name, used to sign in.")] public string Name { get; set; }
        [Doc("Role.", Values = new[] { "seeder", "operator", "admin" })] public string Role { get; set; }
        [Doc("A disabled user can't sign in and their keys don't work.")] public bool Disabled { get; set; }
        [Doc("Whether the user can sign in with a password (otherwise API keys only).")] public bool HasPassword { get; set; }
        [Doc("When the user was added.")] public DateTime Created { get; set; }
        [Doc("The user's API keys (the keys themselves are never shown again).")] public List<KeyDto> Keys { get; set; }
    }

    [Doc("An API key, without the key itself.")]
    public class KeyDto
    {
        [Doc("Key ID.")] public Guid Id { get; set; }
        [Doc("What the key is for.")] public string Label { get; set; }
        [Doc("The start of the key, to tell keys apart.")] public string Prefix { get; set; }
        [Doc("When the key was made.")] public DateTime Created { get; set; }
    }

    [Doc("Add a web user.")]
    public class UserCreateRequest
    {
        [Doc("Name, used to sign in.", Required = true)] public string Name { get; set; }
        [Doc("Role.", Required = true, Values = new[] { "seeder", "operator", "admin" })] public string Role { get; set; }
        [Doc("Password, at least 8 characters. Leave out for a user who only uses API keys.")] public string Password { get; set; }
    }

    [Doc("Change a web user. Fields left out are unchanged.")]
    public class UserPatchRequest
    {
        [Doc("New name.")] public string Name { get; set; }
        [Doc("New role.", Values = new[] { "seeder", "operator", "admin" })] public string Role { get; set; }
        [Doc("New password, at least 8 characters.")] public string Password { get; set; }
        [Doc("Disable or re-enable the user.")] public bool? Disabled { get; set; }
    }

    [Doc("Make an API key.")]
    public class KeyCreateRequest
    {
        [Doc("What the key is for.", Example = "Club dashboard")] public string Label { get; set; }
    }

    [Doc("A new API key.")]
    public class KeyCreatedDto
    {
        [Doc("The key. Send as 'Authorization: Bearer <key>'. Shown only once.")] public string Key { get; set; }
        [Doc("The key's details.")] public KeyDto Info { get; set; }
    }

    [Doc("Who may make changes.")]
    public class AccessSettingsDto
    {
        [Doc("People on the FPVTrackside machine act as Admin without signing in.")] public bool TrustThisMachine { get; set; }
        [Doc("Whether that trust applies now (it always does while there is no Admin user).")] public bool TrustInEffect { get; set; }
        [Doc("Whether an enabled Admin user exists.")] public bool HasAdmin { get; set; }
        [Doc("Whether the FPV_API_TOKEN environment variable is set (it acts as Admin).")] public bool EnvironmentTokenSet { get; set; }
        [Doc("The roles a user can have, lowest first.")] public List<RoleDto> Roles { get; set; }
    }

    [Doc("Change who may make changes.")]
    public class AccessSettingsRequest
    {
        [Doc("Let people on the FPVTrackside machine act as Admin without signing in.")] public bool? TrustThisMachine { get; set; }
    }
}
