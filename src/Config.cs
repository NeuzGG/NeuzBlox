using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NeuzBlox
{
    public static class Paths
    {
        public static string Root
        {
            get
            {
                string p = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeuzBlox");
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                return p;
            }
        }

        public static string SettingsFile { get { return Path.Combine(Root, "settings.json"); } }
        public static string AccountsFile { get { return Path.Combine(Root, "accounts.dat"); } }
        public static string LogFile { get { return Path.Combine(Root, "neuzblox.log"); } }
        public static string CacheDir
        {
            get
            {
                string p = Path.Combine(Root, "cache");
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                return p;
            }
        }
    }

    public static class Log
    {
        static readonly object Gate = new object();

        public static void Write(string msg)
        {
            try
            {
                lock (Gate)
                {
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + msg;
                    File.AppendAllText(Paths.LogFile, line + Environment.NewLine, Encoding.UTF8);
                    var fi = new FileInfo(Paths.LogFile);
                    if (fi.Exists && fi.Length > 512 * 1024)
                    {
                        string[] all = File.ReadAllLines(Paths.LogFile);
                        var keep = new List<string>();
                        for (int i = Math.Max(0, all.Length - 1500); i < all.Length; i++) keep.Add(all[i]);
                        File.WriteAllLines(Paths.LogFile, keep.ToArray(), Encoding.UTF8);
                    }
                }
            }
            catch { }
        }
    }

    public class Preset
    {
        public string Name = "";
        public string Input = "";
        public string Mode = "place";
    }

    public class Account
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Alias = "";
        public string Username = "";
        public string DisplayName = "";
        public long UserId;
        public string Cookie = "";
        public string BrowserTrackerId = "";
        public string Note = "";
        public string LastUsed = "";
        public bool Enabled = true;

        public string Label
        {
            get
            {
                if (!string.IsNullOrEmpty(Alias)) return Alias;
                if (!string.IsNullOrEmpty(Username)) return Username;
                return "Account";
            }
        }

        public string Subtitle
        {
            get
            {
                if (string.IsNullOrEmpty(Username)) return "not verified yet";
                string s = "@" + Username;
                if (UserId > 0) s += "  ·  " + UserId.ToString(CultureInfo.InvariantCulture);
                return s;
            }
        }

        public string EnsureTracker()
        {
            if (string.IsNullOrEmpty(BrowserTrackerId))
            {
                var rnd = new Random(Guid.NewGuid().GetHashCode());
                BrowserTrackerId = rnd.Next(100000, 999999).ToString(CultureInfo.InvariantCulture)
                                 + rnd.Next(100000, 999999).ToString(CultureInfo.InvariantCulture);
            }
            return BrowserTrackerId;
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"id\":").Append(Json.Quote(Id)).Append(",");
            sb.Append("\"alias\":").Append(Json.Quote(Alias)).Append(",");
            sb.Append("\"username\":").Append(Json.Quote(Username)).Append(",");
            sb.Append("\"displayName\":").Append(Json.Quote(DisplayName)).Append(",");
            sb.Append("\"userId\":").Append(Json.Num(UserId)).Append(",");
            sb.Append("\"cookie\":").Append(Json.Quote(Cookie)).Append(",");
            sb.Append("\"btid\":").Append(Json.Quote(BrowserTrackerId)).Append(",");
            sb.Append("\"note\":").Append(Json.Quote(Note)).Append(",");
            sb.Append("\"lastUsed\":").Append(Json.Quote(LastUsed)).Append(",");
            sb.Append("\"enabled\":").Append(Enabled ? "true" : "false");
            sb.Append("}");
            return sb.ToString();
        }

        public static Account FromJson(object node)
        {
            var a = new Account();
            string id = Json.Str(node, "id");
            if (!string.IsNullOrEmpty(id)) a.Id = id;
            a.Alias = Json.Str(node, "alias") ?? "";
            a.Username = Json.Str(node, "username") ?? "";
            a.DisplayName = Json.Str(node, "displayName") ?? "";
            a.UserId = Json.Long(node, "userId", 0);
            a.Cookie = Json.Str(node, "cookie") ?? "";
            a.BrowserTrackerId = Json.Str(node, "btid") ?? "";
            a.Note = Json.Str(node, "note") ?? "";
            a.LastUsed = Json.Str(node, "lastUsed") ?? "";
            a.Enabled = Json.Bool(node, "enabled", true);
            return a;
        }
    }

    /// <summary>
    /// Accounts live in one blob encrypted with Windows DPAPI (current user).
    /// Another Windows account on this PC cannot read it, and it does not travel.
    /// </summary>
    public static class AccountStore
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NeuzBlox::v1::account-vault");

        public static List<Account> Load()
        {
            var list = new List<Account>();
            try
            {
                if (!File.Exists(Paths.AccountsFile)) return list;
                byte[] enc = File.ReadAllBytes(Paths.AccountsFile);
                if (enc.Length == 0) return list;
                byte[] plain = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(plain);
                var root = Json.Parse(json);
                var arr = Json.Arr(root, "accounts");
                if (arr != null)
                    foreach (object o in arr) list.Add(Account.FromJson(o));
            }
            catch (Exception ex)
            {
                Log.Write("AccountStore.Load failed: " + ex.Message);
            }
            return list;
        }

        public static void Save(List<Account> accounts)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\"version\":1,\"accounts\":[");
                for (int i = 0; i < accounts.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(accounts[i].ToJson());
                }
                sb.Append("]}");
                byte[] plain = Encoding.UTF8.GetBytes(sb.ToString());
                byte[] enc = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                string tmp = Paths.AccountsFile + ".tmp";
                File.WriteAllBytes(tmp, enc);
                if (File.Exists(Paths.AccountsFile)) File.Delete(Paths.AccountsFile);
                File.Move(tmp, Paths.AccountsFile);
            }
            catch (Exception ex)
            {
                Log.Write("AccountStore.Save failed: " + ex.Message);
            }
        }

        public static void Wipe()
        {
            try { if (File.Exists(Paths.AccountsFile)) File.Delete(Paths.AccountsFile); }
            catch { }
        }
    }

    public class Settings
    {
        public int LaunchDelaySeconds = 8;
        public bool UnlockOnStart = true;
        public bool MinimizeToTray = true;
        public bool RenameWindows = true;
        public string TitleFormat = "{alias} - NeuzBlox";
        public bool AutoArrange = false;
        public string Layout = "grid";
        public bool AutoRejoin = false;
        public int MaxRejoins = 3;
        public string PlayerPath = "";
        public string Locale = "en_us";
        public string LastInput = "";
        public string LastMode = "place";
        public bool RiskAcknowledged = false;
        public bool CloseAllOnExit = false;
        public bool Animations = true;
        public bool ShowSplash = true;
        public bool DiscordEnabled = false;
        public string DiscordAppId = "";          // optional override; blank uses the built-in app
        public bool DiscordShowAccounts = false;

        /// <summary>The app ID actually used: a custom one if set, otherwise NeuzBlox's own.</summary>
        public string EffectiveDiscordAppId
        {
            get
            {
                string s = (DiscordAppId ?? "").Trim();
                return s.Length > 0 ? s : AppInfo.DefaultDiscordAppId;
            }
        }
        public List<Preset> Presets = new List<Preset>();

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(Paths.SettingsFile)) return s;
                var root = Json.Parse(File.ReadAllText(Paths.SettingsFile, Encoding.UTF8));
                if (root == null) return s;
                s.LaunchDelaySeconds = Json.Int(root, "launchDelay", s.LaunchDelaySeconds);
                s.UnlockOnStart = Json.Bool(root, "unlockOnStart", s.UnlockOnStart);
                s.MinimizeToTray = Json.Bool(root, "minimizeToTray", s.MinimizeToTray);
                s.RenameWindows = Json.Bool(root, "renameWindows", s.RenameWindows);
                s.TitleFormat = Json.Str(root, "titleFormat") ?? s.TitleFormat;
                s.AutoArrange = Json.Bool(root, "autoArrange", s.AutoArrange);
                s.Layout = Json.Str(root, "layout") ?? s.Layout;
                s.AutoRejoin = Json.Bool(root, "autoRejoin", s.AutoRejoin);
                s.MaxRejoins = Json.Int(root, "maxRejoins", s.MaxRejoins);
                s.PlayerPath = Json.Str(root, "playerPath") ?? "";
                s.Locale = Json.Str(root, "locale") ?? s.Locale;
                s.LastInput = Json.Str(root, "lastInput") ?? "";
                s.LastMode = Json.Str(root, "lastMode") ?? "place";
                s.RiskAcknowledged = Json.Bool(root, "riskAcknowledged", false);
                s.CloseAllOnExit = Json.Bool(root, "closeAllOnExit", false);
                s.Animations = Json.Bool(root, "animations", true);
                s.ShowSplash = Json.Bool(root, "showSplash", true);
                s.DiscordEnabled = Json.Bool(root, "discordEnabled", false);
                s.DiscordAppId = Json.Str(root, "discordAppId") ?? "";
                s.DiscordShowAccounts = Json.Bool(root, "discordShowAccounts", false);
                var arr = Json.Arr(root, "presets");
                if (arr != null)
                {
                    foreach (object o in arr)
                    {
                        var p = new Preset();
                        p.Name = Json.Str(o, "name") ?? "";
                        p.Input = Json.Str(o, "input") ?? "";
                        p.Mode = Json.Str(o, "mode") ?? "place";
                        if (!string.IsNullOrEmpty(p.Input)) s.Presets.Add(p);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Settings.Load failed: " + ex.Message);
            }
            return s;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"launchDelay\": ").Append(Json.Num(LaunchDelaySeconds)).Append(",\n");
                sb.Append("  \"unlockOnStart\": ").Append(UnlockOnStart ? "true" : "false").Append(",\n");
                sb.Append("  \"minimizeToTray\": ").Append(MinimizeToTray ? "true" : "false").Append(",\n");
                sb.Append("  \"renameWindows\": ").Append(RenameWindows ? "true" : "false").Append(",\n");
                sb.Append("  \"titleFormat\": ").Append(Json.Quote(TitleFormat)).Append(",\n");
                sb.Append("  \"autoArrange\": ").Append(AutoArrange ? "true" : "false").Append(",\n");
                sb.Append("  \"layout\": ").Append(Json.Quote(Layout)).Append(",\n");
                sb.Append("  \"autoRejoin\": ").Append(AutoRejoin ? "true" : "false").Append(",\n");
                sb.Append("  \"maxRejoins\": ").Append(Json.Num(MaxRejoins)).Append(",\n");
                sb.Append("  \"playerPath\": ").Append(Json.Quote(PlayerPath)).Append(",\n");
                sb.Append("  \"locale\": ").Append(Json.Quote(Locale)).Append(",\n");
                sb.Append("  \"lastInput\": ").Append(Json.Quote(LastInput)).Append(",\n");
                sb.Append("  \"lastMode\": ").Append(Json.Quote(LastMode)).Append(",\n");
                sb.Append("  \"riskAcknowledged\": ").Append(RiskAcknowledged ? "true" : "false").Append(",\n");
                sb.Append("  \"closeAllOnExit\": ").Append(CloseAllOnExit ? "true" : "false").Append(",\n");
                sb.Append("  \"animations\": ").Append(Animations ? "true" : "false").Append(",\n");
                sb.Append("  \"showSplash\": ").Append(ShowSplash ? "true" : "false").Append(",\n");
                sb.Append("  \"discordEnabled\": ").Append(DiscordEnabled ? "true" : "false").Append(",\n");
                sb.Append("  \"discordAppId\": ").Append(Json.Quote(DiscordAppId)).Append(",\n");
                sb.Append("  \"discordShowAccounts\": ").Append(DiscordShowAccounts ? "true" : "false").Append(",\n");
                sb.Append("  \"presets\": [");
                for (int i = 0; i < Presets.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append("{\"name\":").Append(Json.Quote(Presets[i].Name))
                      .Append(",\"input\":").Append(Json.Quote(Presets[i].Input))
                      .Append(",\"mode\":").Append(Json.Quote(Presets[i].Mode)).Append("}");
                }
                sb.Append("]\n}");
                File.WriteAllText(Paths.SettingsFile, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Write("Settings.Save failed: " + ex.Message);
            }
        }
    }
}
