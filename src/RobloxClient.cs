using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace NeuzBlox
{
    public enum JoinMode
    {
        App,
        Place,
        PrivateServer,
        JobId,
        FollowUser
    }

    /// <summary>Where an instance should land once it has authenticated.</summary>
    public class JoinTarget
    {
        public JoinMode Mode = JoinMode.App;
        public long PlaceId;
        public string LinkCode = "";
        public string AccessCode = "";
        public string JobId = "";
        public long UserId;
        public string Label = "Roblox app";

        static readonly Regex RxPlace = new Regex(@"(?:games|place)\D{0,3}/(\d{4,})", RegexOptions.IgnoreCase);
        static readonly Regex RxLinkCode = new Regex(@"privateServerLinkCode=([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
        static readonly Regex RxAccessCode = new Regex(@"accessCode=([A-Za-z0-9\-]+)", RegexOptions.IgnoreCase);
        static readonly Regex RxJob = new Regex(@"(?:gameId|jobId)=([A-Za-z0-9\-]{8,})", RegexOptions.IgnoreCase);
        static readonly Regex RxUser = new Regex(@"users/(\d{2,})", RegexOptions.IgnoreCase);
        static readonly Regex RxDigits = new Regex(@"^\s*(\d{4,})\s*$");

        public static JoinTarget Parse(string input, JoinMode requested, out string error)
        {
            error = null;
            var t = new JoinTarget();
            input = (input ?? "").Trim();

            if (requested == JoinMode.App || input.Length == 0)
            {
                t.Mode = JoinMode.App;
                t.Label = "Roblox app (home)";
                return t;
            }

            if (input.IndexOf("/share", StringComparison.OrdinalIgnoreCase) >= 0 &&
                input.IndexOf("code=", StringComparison.OrdinalIgnoreCase) >= 0 &&
                RxPlace.Match(input).Success == false)
            {
                error = "Roblox share links cannot be resolved offline. Open the link once in a browser, then paste the roblox.com/games/... URL it lands on.";
                return null;
            }

            Match m;

            if (requested == JoinMode.FollowUser)
            {
                m = RxUser.Match(input);
                long uid;
                if (m.Success) uid = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                else if (!long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out uid))
                {
                    error = "Enter the user ID or a roblox.com/users/... profile link to follow.";
                    return null;
                }
                t.Mode = JoinMode.FollowUser;
                t.UserId = uid;
                t.Label = "Follow user " + uid;
                return t;
            }

            m = RxDigits.Match(input);
            if (m.Success) t.PlaceId = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            else
            {
                m = RxPlace.Match(input);
                if (m.Success) t.PlaceId = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            Match link = RxLinkCode.Match(input);
            Match access = RxAccessCode.Match(input);
            Match job = RxJob.Match(input);

            if (requested == JoinMode.PrivateServer || link.Success || access.Success)
            {
                if (t.PlaceId <= 0)
                {
                    error = "A private server needs the full link (it contains both the place and the code).";
                    return null;
                }
                t.Mode = JoinMode.PrivateServer;
                if (link.Success) t.LinkCode = link.Groups[1].Value;
                if (access.Success) t.AccessCode = access.Groups[1].Value;
                if (t.LinkCode.Length == 0 && t.AccessCode.Length == 0)
                {
                    error = "No private server code found in that link.";
                    return null;
                }
                t.Label = "Private server " + t.PlaceId;
                return t;
            }

            if (requested == JoinMode.JobId || job.Success)
            {
                if (t.PlaceId <= 0)
                {
                    error = "Joining a specific server needs a place ID as well as the job ID.";
                    return null;
                }
                t.Mode = JoinMode.JobId;
                t.JobId = job.Success ? job.Groups[1].Value : input;
                t.Label = "Server " + Short(t.JobId);
                return t;
            }

            if (t.PlaceId <= 0)
            {
                error = "Could not find a place ID in that. Paste a roblox.com/games/... link or the numeric ID.";
                return null;
            }

            t.Mode = JoinMode.Place;
            t.Label = "Place " + t.PlaceId;
            return t;
        }

        static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= 10 ? s : s.Substring(0, 8) + "...";
        }

        public string BuildPlaceLauncherUrl(string btid)
        {
            const string b = "https://assetgame.roblox.com/game/PlaceLauncher.ashx";
            string pid = PlaceId.ToString(CultureInfo.InvariantCulture);
            switch (Mode)
            {
                case JoinMode.PrivateServer:
                    {
                        var sb = new StringBuilder(b);
                        sb.Append("?request=RequestPrivateGame&browserTrackerId=").Append(btid);
                        sb.Append("&placeId=").Append(pid);
                        if (!string.IsNullOrEmpty(AccessCode)) sb.Append("&accessCode=").Append(Uri.EscapeDataString(AccessCode));
                        if (!string.IsNullOrEmpty(LinkCode)) sb.Append("&linkCode=").Append(Uri.EscapeDataString(LinkCode));
                        sb.Append("&isPlayTogetherGame=false");
                        return sb.ToString();
                    }
                case JoinMode.JobId:
                    return b + "?request=RequestGameJob&browserTrackerId=" + btid
                             + "&placeId=" + pid
                             + "&gameId=" + Uri.EscapeDataString(JobId)
                             + "&isPlayTogetherGame=false";
                case JoinMode.FollowUser:
                    return b + "?request=RequestFollowUser&browserTrackerId=" + btid
                             + "&userId=" + UserId.ToString(CultureInfo.InvariantCulture);
                default:
                    return b + "?request=RequestGame&browserTrackerId=" + btid
                             + "&placeId=" + pid
                             + "&isPlayTogetherGame=false";
            }
        }
    }

    public static class RobloxClient
    {
        static string _currentFolder;
        static DateTime _currentFolderAt;
        static readonly object VersionGate = new object();

        /// <summary>
        /// Asks Roblox which client build is current. This matters more than it sounds:
        /// launching any other build makes the client bounce through its own installer,
        /// and the installer then starts a fresh client WITHOUT our authentication ticket -
        /// so you get whatever account was last signed in instead of the one you picked.
        /// Cached, because it is a network call on the launch path.
        /// </summary>
        public static string CurrentVersionFolder(bool allowNetwork)
        {
            lock (VersionGate)
            {
                if (!string.IsNullOrEmpty(_currentFolder) && (DateTime.UtcNow - _currentFolderAt).TotalMinutes < 15)
                    return _currentFolder;
                if (!allowNetwork) return _currentFolder;
            }

            string folder = null;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(
                    "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer");
                req.UserAgent = "Roblox/WinInet";
                req.Accept = "application/json";
                req.Timeout = 9000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (Stream st = resp.GetResponseStream())
                using (var sr = new StreamReader(st))
                    folder = Json.Str(Json.Parse(sr.ReadToEnd()), "clientVersionUpload");
            }
            catch (Exception ex)
            {
                Log.Write("client-version lookup failed: " + ex.Message);
            }

            lock (VersionGate)
            {
                if (!string.IsNullOrEmpty(folder))
                {
                    _currentFolder = folder;
                    _currentFolderAt = DateTime.UtcNow;
                }
                return _currentFolder;
            }
        }

        /// <summary>The version folder Windows' roblox-player link currently points at.</summary>
        public static string RegisteredVersionFolder()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\roblox-player\shell\open\command"))
                {
                    if (k == null) return null;
                    return k.GetValue("version") as string;
                }
            }
            catch { return null; }
        }

        public static string FindPlayer(string overridePath)
        {
            return FindPlayer(overridePath, true);
        }

        /// <summary>
        /// Locates RobloxPlayerBeta.exe. Roblox's own answer wins over the registered
        /// protocol handler, because third-party tools re-point that handler at builds
        /// Roblox will immediately try to replace.
        /// </summary>
        public static string FindPlayer(string overridePath, bool allowNetwork)
        {
            if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath)) return overridePath;

            string want = CurrentVersionFolder(allowNetwork);
            if (!string.IsNullOrEmpty(want))
            {
                foreach (string root in CandidateRoots())
                {
                    string exe = Path.Combine(root, want, "RobloxPlayerBeta.exe");
                    if (File.Exists(exe)) return exe;
                }
            }

            string fromReg = FromProtocolHandler();
            if (fromReg != null) return fromReg;

            string newest = null;
            DateTime newestTime = DateTime.MinValue;
            foreach (string root in CandidateRoots())
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string dir in Directory.GetDirectories(root))
                    {
                        string exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
                        if (!File.Exists(exe)) continue;
                        DateTime t = File.GetLastWriteTimeUtc(exe);
                        if (t > newestTime) { newestTime = t; newest = exe; }
                    }
                }
                catch { }
            }
            return newest;
        }

        static IEnumerable<string> CandidateRoots()
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Roblox\Versions");
            string pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if (!string.IsNullOrEmpty(pf86)) yield return Path.Combine(pf86, @"Roblox\Versions");
            string pf = Environment.GetEnvironmentVariable("ProgramFiles");
            if (!string.IsNullOrEmpty(pf)) yield return Path.Combine(pf, @"Roblox\Versions");
        }

        static string FromProtocolHandler()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\roblox-player\shell\open\command"))
                {
                    if (k == null) return null;
                    string cmd = k.GetValue(null) as string;
                    if (string.IsNullOrEmpty(cmd)) return null;
                    var m = Regex.Match(cmd, "\"([^\"]+RobloxPlayerBeta\\.exe)\"", RegexOptions.IgnoreCase);
                    if (!m.Success) m = Regex.Match(cmd, "([A-Za-z]:\\\\[^\"]+RobloxPlayerBeta\\.exe)", RegexOptions.IgnoreCase);
                    if (m.Success && File.Exists(m.Groups[1].Value)) return m.Groups[1].Value;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Non-null when Windows' Roblox link points somewhere other than the build Roblox
        /// says is current - the situation that makes clients bounce through the installer.
        /// </summary>
        public static string HandlerMismatch()
        {
            string want = CurrentVersionFolder(false);
            if (string.IsNullOrEmpty(want)) return null;
            string reg = RegisteredVersionFolder();
            if (string.IsNullOrEmpty(reg) || string.Equals(reg, want, StringComparison.OrdinalIgnoreCase)) return null;
            return "Windows' Roblox link points at " + reg + ", but Roblox's current client is "
                 + want + ". NeuzBlox is launching the current one.";
        }

        static string _verPath, _verText;

        /// <summary>Cached - the status bar asks for this on every UI tick.</summary>
        public static string Version(string playerPath)
        {
            if (_verText != null && _verPath == playerPath) return _verText;
            _verPath = playerPath;
            _verText = ReadVersion(playerPath);
            return _verText;
        }

        static string ReadVersion(string playerPath)
        {
            try
            {
                if (string.IsNullOrEmpty(playerPath) || !File.Exists(playerPath)) return "not found";
                var vi = FileVersionInfo.GetVersionInfo(playerPath);
                string v = vi.FileVersion;
                if (!string.IsNullOrEmpty(v)) return v.Replace(", ", ".");
                return Path.GetFileName(Path.GetDirectoryName(playerPath));
            }
            catch { return "unknown"; }
        }

        /// <summary>Builds the same launch URI the website hands the client, with our ticket in it.</summary>
        public static string BuildLaunchUri(string ticket, JoinTarget target, string btid, string locale)
        {
            long launchTime = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            var sb = new StringBuilder();
            sb.Append("roblox-player:1");
            sb.Append("+launchmode:").Append(target.Mode == JoinMode.App ? "app" : "play");
            sb.Append("+gameinfo:").Append(ticket);
            sb.Append("+launchtime:").Append(launchTime.ToString(CultureInfo.InvariantCulture));
            if (target.Mode != JoinMode.App)
                sb.Append("+placelauncherurl:").Append(Uri.EscapeDataString(target.BuildPlaceLauncherUrl(btid)));
            sb.Append("+browsertrackerid:").Append(btid);
            sb.Append("+robloxLocale:").Append(locale);
            sb.Append("+gameLocale:").Append(locale);
            sb.Append("+channel:");
            return sb.ToString();
        }

        public static Process Launch(string playerPath, string launchUri)
        {
            var psi = new ProcessStartInfo();
            psi.FileName = playerPath;
            psi.Arguments = launchUri;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetDirectoryName(playerPath);
            return Process.Start(psi);
        }

        /// <summary>Every RobloxPlayerBeta process currently running, whoever started it.</summary>
        public static Process[] RunningClients()
        {
            try { return Process.GetProcessesByName("RobloxPlayerBeta"); }
            catch { return new Process[0]; }
        }

        /// <summary>
        /// True while Roblox is installing or repairing itself. This matters a lot: when the
        /// client decides it is out of date it spawns its own installer, and two of those at
        /// once abort with "another RobloxPlayerInstaller is running" - taking both clients
        /// down with them.
        /// </summary>
        public static bool IsUpdating()
        {
            if (!AnyProcess("RobloxPlayerInstaller")
                && !AnyProcess("RobloxPlayerLauncher")
                && !AnyProcess("RobloxSetup")) return false;
            return InstallerLooksBusy();
        }

        /// <summary>
        /// A finished installer can linger for ages doing nothing - it cannot delete its own
        /// exe while it is still running, so the process just sits there. Waiting on that
        /// forever would wedge every launch, so only treat it as busy while its log is still
        /// moving. The gap between BITS download batches can reach a minute, hence the
        /// generous window.
        /// </summary>
        static bool InstallerLooksBusy()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Roblox\logs");
                if (!Directory.Exists(dir)) return true;

                DateTime newest = DateTime.MinValue;
                foreach (string f in Directory.GetFiles(dir, "RobloxPlayerInstaller_*.log"))
                {
                    DateTime t = File.GetLastWriteTimeUtc(f);
                    if (t > newest) newest = t;
                }
                if (newest == DateTime.MinValue) return true;
                return (DateTime.UtcNow - newest).TotalSeconds < 120;
            }
            catch { return true; }
        }

        static bool AnyProcess(string name)
        {
            try { return Process.GetProcessesByName(name).Length > 0; }
            catch { return false; }
        }

        public static bool WaitWhileUpdating(int timeoutMs)
        {
            return WaitWhileUpdating(timeoutMs, null);
        }

        /// <summary>
        /// Blocks until Roblox has finished installing itself. This can take minutes - the
        /// installer downloads the client in pieces - so callers get a per-second tick to
        /// report progress instead of looking frozen.
        /// </summary>
        public static bool WaitWhileUpdating(int timeoutMs, Action<int> onTick)
        {
            if (!IsUpdating()) return true;
            var sw = Stopwatch.StartNew();
            int reported = -1;
            while (IsUpdating() && sw.ElapsedMilliseconds < timeoutMs)
            {
                int secs = (int)(sw.ElapsedMilliseconds / 1000);
                if (onTick != null && secs != reported)
                {
                    reported = secs;
                    try { onTick(secs); }
                    catch { }
                }
                Thread.Sleep(400);
            }
            // the installer rewrites the protocol handler on its way out - let it land
            Thread.Sleep(1500);
            return !IsUpdating();
        }

        public static string Elapsed(int seconds)
        {
            int m = seconds / 60, s = seconds % 60;
            return m.ToString(CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
