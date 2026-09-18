using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace NeuzBlox
{
    public class UserInfo
    {
        public long Id;
        public string Name = "";
        public string DisplayName = "";
    }

    public class ApiResult
    {
        public bool Ok;
        public string Error = "";
        public string RefreshedCookie;
    }

    public class TicketResult : ApiResult
    {
        public string Ticket = "";
    }

    public class UserResult : ApiResult
    {
        public UserInfo User;
    }

    /// <summary>
    /// Thin wrapper over the public Roblox web endpoints NeuzBlox needs:
    /// identify an account from its session cookie, and mint the one-shot
    /// authentication ticket the desktop client redeems at launch.
    /// </summary>
    public static class RobloxApi
    {
        const string UA = "Roblox/WinInet";
        const string TicketUrl = "https://auth.roblox.com/v1/authentication-ticket";
        const string WhoAmIUrl = "https://users.roblox.com/v1/users/authenticated";

        /// <summary>Accepts a raw value, a "name=value" pair, or a full document.cookie dump.</summary>
        public static string NormalizeCookie(string raw)
        {
            if (raw == null) return "";
            string s = raw.Trim().Trim('"', '\'');
            int idx = s.IndexOf(".ROBLOSECURITY=", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                s = s.Substring(idx + ".ROBLOSECURITY=".Length);
                int semi = s.IndexOf(';');
                if (semi >= 0) s = s.Substring(0, semi);
            }
            return s.Trim().Trim('"', '\'');
        }

        public static bool LooksLikeCookie(string cookie)
        {
            if (string.IsNullOrEmpty(cookie)) return false;
            return cookie.Length > 60 && cookie.IndexOf("WARNING", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static HttpWebRequest Build(string method, string url, string cookie, string btid)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = UA;
            req.Accept = "application/json";
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;
            req.AllowAutoRedirect = true;
            req.KeepAlive = false;
            req.Referer = "https://www.roblox.com/";   // restricted header: must go through the property
            req.Headers["Origin"] = "https://www.roblox.com";
            if (!string.IsNullOrEmpty(cookie))
            {
                string c = ".ROBLOSECURITY=" + cookie;
                if (!string.IsNullOrEmpty(btid))
                    c += "; RBXEventTrackerV2=CreateDate=1/1/2024 12:00:00 AM&rbxid=&browserid=" + btid;
                req.Headers["Cookie"] = c;
            }
            return req;
        }

        static string ReadBody(HttpWebResponse resp)
        {
            if (resp == null) return "";
            using (Stream st = resp.GetResponseStream())
            {
                if (st == null) return "";
                using (var sr = new StreamReader(st, Encoding.UTF8))
                    return sr.ReadToEnd();
            }
        }

        static string ExtractRotatedCookie(WebHeaderCollection headers)
        {
            try
            {
                string raw = headers["Set-Cookie"];
                if (string.IsNullOrEmpty(raw)) return null;
                int i = raw.IndexOf(".ROBLOSECURITY=", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return null;
                string v = raw.Substring(i + ".ROBLOSECURITY=".Length);
                int semi = v.IndexOf(';');
                if (semi >= 0) v = v.Substring(0, semi);
                v = v.Trim();
                return LooksLikeCookie(v) ? v : null;
            }
            catch { return null; }
        }

        /// <summary>Roblox hands out the CSRF token in the 403 it returns for an unguarded POST.</summary>
        public static string GetCsrfToken(string cookie, string btid)
        {
            try
            {
                var req = Build("POST", TicketUrl, cookie, btid);
                req.ContentLength = 0;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    string t = resp.Headers["x-csrf-token"];
                    ReadBody(resp);
                    return t;
                }
            }
            catch (WebException wex)
            {
                var resp = wex.Response as HttpWebResponse;
                if (resp != null)
                {
                    string t = resp.Headers["x-csrf-token"];
                    try { ReadBody(resp); } catch { }
                    resp.Close();
                    if (!string.IsNullOrEmpty(t)) return t;
                }
            }
            catch { }
            return null;
        }

        public static UserResult WhoAmI(string cookie, string btid)
        {
            var r = new UserResult();
            if (!LooksLikeCookie(cookie))
            {
                r.Error = "That does not look like a .ROBLOSECURITY value.";
                return r;
            }
            try
            {
                var req = Build("GET", WhoAmIUrl, cookie, btid);
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    string body = ReadBody(resp);
                    r.RefreshedCookie = ExtractRotatedCookie(resp.Headers);
                    var root = Json.Parse(body);
                    var u = new UserInfo();
                    u.Id = Json.Long(root, "id", 0);
                    u.Name = Json.Str(root, "name") ?? "";
                    u.DisplayName = Json.Str(root, "displayName") ?? u.Name;
                    if (u.Id <= 0) { r.Error = "Roblox did not return a user for this session."; return r; }
                    r.User = u;
                    r.Ok = true;
                    return r;
                }
            }
            catch (WebException wex)
            {
                var resp = wex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.Unauthorized)
                    r.Error = "Session expired or invalid - sign in again and copy a fresh cookie.";
                else
                    r.Error = "Network error: " + wex.Message;
                if (resp != null) resp.Close();
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }
            return r;
        }

        /// <summary>Mints a single-use launch ticket for the account behind this cookie.</summary>
        public static TicketResult GetAuthTicket(string cookie, string btid)
        {
            var r = new TicketResult();
            if (!LooksLikeCookie(cookie))
            {
                r.Error = "Stored cookie is missing or malformed.";
                return r;
            }

            // A dead session never yields a token, so don't fail here - let the real
            // POST come back with a status we can explain to the user.
            string csrf = GetCsrfToken(cookie, btid);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var req = Build("POST", TicketUrl, cookie, btid);
                    if (!string.IsNullOrEmpty(csrf)) req.Headers["X-CSRF-TOKEN"] = csrf;
                    req.ContentType = "application/json";
                    req.ContentLength = 0;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        string ticket = resp.Headers["rbx-authentication-ticket"];
                        r.RefreshedCookie = ExtractRotatedCookie(resp.Headers);
                        ReadBody(resp);
                        if (string.IsNullOrEmpty(ticket))
                        {
                            r.Error = "Roblox accepted the request but returned no ticket.";
                            return r;
                        }
                        r.Ticket = ticket;
                        r.Ok = true;
                        return r;
                    }
                }
                catch (WebException wex)
                {
                    var resp = wex.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        string fresh = resp.Headers["x-csrf-token"];
                        HttpStatusCode code = resp.StatusCode;
                        string body = "";
                        try { body = ReadBody(resp); } catch { }
                        resp.Close();
                        if (code == HttpStatusCode.Unauthorized)
                        {
                            r.Error = "Session expired - re-add this account with a fresh cookie.";
                            return r;
                        }
                        if (code == HttpStatusCode.Forbidden && !string.IsNullOrEmpty(fresh) && fresh != csrf)
                        {
                            csrf = fresh;
                            continue;
                        }
                        if ((int)code == 429)
                        {
                            r.Error = "Roblox is rate-limiting logins. Wait a moment and raise the launch delay.";
                            return r;
                        }
                        r.Error = "Roblox returned " + (int)code + " " + code + (string.IsNullOrEmpty(body) ? "" : ": " + Trim(body, 180));
                        return r;
                    }
                    r.Error = "Network error: " + wex.Message;
                    return r;
                }
                catch (Exception ex)
                {
                    r.Error = ex.Message;
                    return r;
                }
            }
            if (string.IsNullOrEmpty(r.Error)) r.Error = "Ticket request failed.";
            return r;
        }

        static readonly Dictionary<long, string> NameCache = new Dictionary<long, string>();

        /// <summary>Cached: the game watcher asks for this repeatedly.</summary>
        public static string GetPlaceName(long placeId)
        {
            lock (IconGate)
            {
                string hit;
                if (NameCache.TryGetValue(placeId, out hit)) return hit;
            }
            string name = FetchPlaceName(placeId);
            if (!string.IsNullOrEmpty(name))
                lock (IconGate) NameCache[placeId] = name;
            return name;
        }

        static string FetchPlaceName(long placeId)
        {
            try
            {
                string uJson = SimpleGet("https://apis.roblox.com/universes/v1/places/"
                    + placeId.ToString(CultureInfo.InvariantCulture) + "/universe");
                long universeId = Json.Long(Json.Parse(uJson), "universeId", 0);
                if (universeId <= 0) return null;
                string gJson = SimpleGet("https://games.roblox.com/v1/games?universeIds="
                    + universeId.ToString(CultureInfo.InvariantCulture));
                var data = Json.Arr(Json.Parse(gJson), "data");
                if (data != null && data.Count > 0) return Json.Str(data[0], "name");
            }
            catch { }
            return null;
        }

        static readonly Dictionary<long, string> IconCache = new Dictionary<long, string>();
        static readonly object IconGate = new object();

        /// <summary>Cached game icon, or null if it has not been fetched yet. Never blocks.</summary>
        public static string GetGameIconCached(long placeId)
        {
            lock (IconGate)
            {
                string url;
                if (IconCache.TryGetValue(placeId, out url)) return url;
                return null;
            }
        }

        public static bool GameIconKnown(long placeId)
        {
            lock (IconGate) return IconCache.ContainsKey(placeId);
        }

        /// <summary>
        /// The game's icon. Discord accepts a raw https URL here and proxies it itself,
        /// so this is all that is needed to show the actual game art in rich presence.
        /// </summary>
        public static string GetGameIcon(long placeId)
        {
            lock (IconGate)
            {
                string cached;
                if (IconCache.TryGetValue(placeId, out cached)) return cached;
            }

            string result = null;
            try
            {
                string uJson = SimpleGet("https://apis.roblox.com/universes/v1/places/"
                    + placeId.ToString(CultureInfo.InvariantCulture) + "/universe");
                long universeId = Json.Long(Json.Parse(uJson), "universeId", 0);
                if (universeId > 0)
                {
                    string j = SimpleGet("https://thumbnails.roblox.com/v1/games/icons?universeIds="
                        + universeId.ToString(CultureInfo.InvariantCulture)
                        + "&size=512x512&format=Png&isCircular=false");
                    var data = Json.Arr(Json.Parse(j), "data");
                    if (data != null && data.Count > 0)
                    {
                        string state = Json.Str(data[0], "state");
                        string url = Json.Str(data[0], "imageUrl");
                        if (!string.IsNullOrEmpty(url) &&
                            (string.IsNullOrEmpty(state) || state == "Completed"))
                            result = url;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("game icon lookup failed for " + placeId + ": " + ex.Message);
            }

            lock (IconGate) IconCache[placeId] = result;   // cache misses too, so we stop retrying
            return result;
        }

        public static string GetAvatarUrl(long userId)
        {
            try
            {
                string j = SimpleGet("https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds="
                    + userId.ToString(CultureInfo.InvariantCulture) + "&size=48x48&format=Png&isCircular=false");
                var data = Json.Arr(Json.Parse(j), "data");
                if (data != null && data.Count > 0) return Json.Str(data[0], "imageUrl");
            }
            catch { }
            return null;
        }

        public static byte[] Download(string url)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = UA;
                req.Timeout = 15000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (Stream st = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[8192];
                    int n;
                    while ((n = st.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    return ms.ToArray();
                }
            }
            catch { return null; }
        }

        static string SimpleGet(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UA;
            req.Accept = "application/json";
            req.Timeout = 15000;
            using (var resp = (HttpWebResponse)req.GetResponse())
                return ReadBody(resp);
        }

        static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
