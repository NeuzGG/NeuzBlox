using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace NeuzBlox
{
    public class LiveGame
    {
        public long PlaceId;
        public string JobId = "";
        public string Name = "";
        public DateTime SeenUtc;
    }

    /// <summary>
    /// Works out which game each client is actually in.
    ///
    /// Launching into a place tells us the place up front, but most of the time people
    /// open the Roblox app and pick a game inside the client - and then nothing we did
    /// at launch knows where they ended up. The client writes it to its own log, so we
    /// read that: every client keeps its own file, each one carries the account's userid,
    /// and the last "Joining game" or "returnToLuaApp" line says where it is right now.
    ///
    /// Read-only, off the UI thread, and opened with full sharing because the client
    /// still has the file open.
    /// </summary>
    public static class GameWatcher
    {
        const string JoinMark = "! Joining game '";
        const string LeaveMark = "returnToLuaApp";
        const string UserMark = "userid:";
        const int TailBytes = 192 * 1024;

        class LogState
        {
            public DateTime Written;
            public long UserId;
            public long PlaceId;
            public string JobId = "";
            public bool InGame;
        }

        static readonly Dictionary<long, LiveGame> ByUser = new Dictionary<long, LiveGame>();
        static readonly Dictionary<string, LogState> Logs = new Dictionary<string, LogState>();
        static readonly object Gate = new object();

        // "..._20260918T173750Z_Player_B2B9A_last.log" - the client's start time, UTC
        static readonly System.Text.RegularExpressions.Regex RxStamp =
            new System.Text.RegularExpressions.Regex(@"_(\d{8}T\d{6})Z_Player_",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        static Thread _thread;
        static volatile bool _run;

        public static void Start()
        {
            if (_run) return;
            _run = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "NeuzBlox.GameWatcher";
            _thread.Start();
        }

        public static void Stop()
        {
            _run = false;
        }

        /// <summary>Where this account's client is right now, or null if it is not in a game.</summary>
        public static LiveGame ForUser(long userId)
        {
            if (userId <= 0) return null;
            lock (Gate)
            {
                LiveGame g;
                return ByUser.TryGetValue(userId, out g) ? g : null;
            }
        }

        static void Loop()
        {
            while (_run)
            {
                try { Scan(); }
                catch (Exception ex) { Log.Write("GameWatcher scan failed: " + ex.Message); }
                for (int i = 0; i < 20 && _run; i++) Thread.Sleep(200);
            }
        }

        static void Scan()
        {
            // A log file outlives the client that wrote it, and a client that was killed
            // never logs "returnToLuaApp" - so the last thing in the file is still a join,
            // for hours afterwards. Reading it alone reports a game nobody is playing.
            // The only trustworthy signal is a client process that is running right now.
            List<DateTime> starts = RunningClientStarts();
            if (starts.Count == 0)
            {
                lock (Gate) ByUser.Clear();
                return;
            }

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Roblox\logs");
            if (!Directory.Exists(dir)) return;

            string[] files;
            try { files = Directory.GetFiles(dir, "*_Player_*.log"); }
            catch { return; }

            var fresh = new Dictionary<long, LiveGame>();

            foreach (string path in files)
            {
                DateTime started;
                if (!TryStamp(path, out started)) continue;
                if (!MatchesRunningClient(started, starts)) continue;

                DateTime written;
                try { written = File.GetLastWriteTimeUtc(path); }
                catch { continue; }

                LogState st;
                lock (Gate) Logs.TryGetValue(path, out st);

                if (st == null || st.Written != written)
                {
                    string text = ReadTail(path);
                    if (text == null) continue;

                    st = new LogState();
                    st.Written = written;
                    st.UserId = ExtractUserId(text);

                    int join = text.LastIndexOf(JoinMark, StringComparison.Ordinal);
                    int leave = text.LastIndexOf(LeaveMark, StringComparison.Ordinal);
                    if (join >= 0 && leave < join)
                    {
                        string jobId;
                        long placeId;
                        if (ParseJoin(text, join, out jobId, out placeId))
                        {
                            st.InGame = true;
                            st.JobId = jobId;
                            st.PlaceId = placeId;
                        }
                    }

                    lock (Gate) Logs[path] = st;
                }

                if (st.UserId <= 0 || !st.InGame) continue;

                var g = new LiveGame();
                g.PlaceId = st.PlaceId;
                g.JobId = st.JobId;
                g.SeenUtc = written;
                g.Name = RobloxApi.GetPlaceName(st.PlaceId) ?? "";
                fresh[st.UserId] = g;
            }

            lock (Gate)
            {
                ByUser.Clear();
                foreach (KeyValuePair<long, LiveGame> kv in fresh) ByUser[kv.Key] = kv.Value;

                // drop cache entries for logs that are gone
                if (Logs.Count > 40)
                {
                    var keep = new List<string>(Logs.Keys);
                    foreach (string k in keep)
                        if (!File.Exists(k)) Logs.Remove(k);
                }
            }
        }

        static List<DateTime> RunningClientStarts()
        {
            var list = new List<DateTime>();
            try
            {
                foreach (System.Diagnostics.Process p in
                         System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta"))
                {
                    try { list.Add(p.StartTime.ToUniversalTime()); }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }
            return list;
        }

        /// <summary>The log's filename carries the client's start time; pair them up.</summary>
        static bool MatchesRunningClient(DateTime logStarted, List<DateTime> starts)
        {
            foreach (DateTime s in starts)
                if (Math.Abs((s - logStarted).TotalSeconds) <= 120) return true;
            return false;
        }

        static bool TryStamp(string path, out DateTime started)
        {
            started = DateTime.MinValue;
            var m = RxStamp.Match(Path.GetFileName(path));
            if (!m.Success) return false;
            return DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd'T'HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out started);
        }

        static bool ParseJoin(string text, int join, out string jobId, out long placeId)
        {
            jobId = "";
            placeId = 0;
            try
            {
                int idStart = join + JoinMark.Length;
                int idEnd = text.IndexOf('\'', idStart);
                if (idEnd < 0) return false;
                jobId = text.Substring(idStart, idEnd - idStart);

                const string PlaceMark = " place ";
                int p = text.IndexOf(PlaceMark, idEnd, StringComparison.Ordinal);
                if (p < 0) return false;
                int d = p + PlaceMark.Length;
                var digits = new StringBuilder();
                while (d < text.Length && char.IsDigit(text[d])) digits.Append(text[d++]);
                if (digits.Length == 0) return false;
                return long.TryParse(digits.ToString(), NumberStyles.Integer,
                                     CultureInfo.InvariantCulture, out placeId);
            }
            catch { return false; }
        }

        static long ExtractUserId(string text)
        {
            long id = 0;
            int i = text.LastIndexOf(UserMark, StringComparison.Ordinal);
            while (i >= 0 && id <= 0)
            {
                int d = i + UserMark.Length;
                var digits = new StringBuilder();
                while (d < text.Length && char.IsDigit(text[d])) digits.Append(text[d++]);
                if (digits.Length > 3) long.TryParse(digits.ToString(), out id);
                if (id > 0) break;
                i = text.LastIndexOf(UserMark, Math.Max(0, i - 1), StringComparison.Ordinal);
                if (i == 0) break;
            }
            return id;
        }

        static string ReadTail(string path)
        {
            try
            {
                // the client still holds this file open, so share everything
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length;
                    long start = Math.Max(0, len - TailBytes);
                    fs.Seek(start, SeekOrigin.Begin);
                    int want = (int)(len - start);
                    if (want <= 0) return null;
                    var buf = new byte[want];
                    int got = 0;
                    while (got < want)
                    {
                        int n = fs.Read(buf, got, want - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    return Encoding.UTF8.GetString(buf, 0, got);
                }
            }
            catch { return null; }
        }
    }
}
