using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace NeuzBlox
{
    public enum InstState { Pending, Ticket, Starting, Running, Closed, Failed }

    public class RbxInstance
    {
        public int Slot;
        public Account Account;
        public Process Proc;
        public int Pid;
        public IntPtr Hwnd = IntPtr.Zero;
        public JoinTarget Target;
        public string TargetLabel = "";
        public DateTime StartedUtc = DateTime.UtcNow;
        public DateTime? EndedUtc;
        public InstState State = InstState.Pending;
        public string Message = "Queued";
        public int Rejoins;
        public int StartupRetries;
        public bool UserClosed;
        public bool TitleApplied;
        public bool EverHadWindow;

        /// <summary>Where this client actually is now, read from its own log.</summary>
        public long LivePlaceId;
        public string LiveGameName = "";

        /// <summary>The game if we know it, otherwise whatever it was launched at.</summary>
        public string WhereLabel
        {
            get
            {
                if (LivePlaceId > 0)
                    return string.IsNullOrEmpty(LiveGameName)
                        ? "Place " + LivePlaceId.ToString(CultureInfo.InvariantCulture)
                        : LiveGameName;
                return TargetLabel;
            }
        }

        public string Uptime
        {
            get
            {
                DateTime end = EndedUtc.HasValue ? EndedUtc.Value : DateTime.UtcNow;
                TimeSpan t = end - StartedUtc;
                if (t.TotalSeconds < 0) t = TimeSpan.Zero;
                if (t.TotalHours >= 1)
                    return ((int)t.TotalHours).ToString(CultureInfo.InvariantCulture) + "h " + t.Minutes + "m";
                if (t.TotalMinutes >= 1)
                    return t.Minutes.ToString(CultureInfo.InvariantCulture) + "m " + t.Seconds + "s";
                return t.Seconds.ToString(CultureInfo.InvariantCulture) + "s";
            }
        }

        public bool IsLive
        {
            get { return State == InstState.Pending || State == InstState.Ticket || State == InstState.Starting || State == InstState.Running; }
        }
    }

    /// <summary>
    /// Owns the lifecycle of every client NeuzBlox starts: ticket -> process -> window,
    /// then keeps watching so the UI can show real state instead of guessing.
    /// </summary>
    public class InstanceManager
    {
        readonly object _gate = new object();
        readonly List<RbxInstance> _items = new List<RbxInstance>();
        static readonly object LaunchGate = new object();
        static readonly object TicketGate = new object();
        static DateTime _lastTicket = DateTime.MinValue;
        int _nextSlot = 1;

        public Settings Config;
        public Func<string> PlayerPathProvider;
        public Action<Account> AccountUpdated;
        public Action Changed;
        public Action<string, bool> Status;

        public List<RbxInstance> Snapshot()
        {
            lock (_gate) return new List<RbxInstance>(_items);
        }

        public int LiveCount
        {
            get
            {
                int n = 0;
                lock (_gate) foreach (RbxInstance i in _items) if (i.IsLive) n++;
                return n;
            }
        }

        public void Clear(bool onlyDead)
        {
            lock (_gate)
            {
                if (onlyDead) _items.RemoveAll(delegate(RbxInstance i) { return !i.IsLive; });
                else _items.Clear();
            }
            Fire();
        }

        public RbxInstance Launch(Account acc, JoinTarget target, string targetLabel)
        {
            var inst = new RbxInstance();
            lock (_gate)
            {
                inst.Slot = _nextSlot++;
                inst.Account = acc;
                inst.Target = target;
                inst.TargetLabel = targetLabel;
                _items.Add(inst);
            }
            Fire();
            StartWorker(inst);
            return inst;
        }

        void StartWorker(RbxInstance inst)
        {
            var th = new Thread(delegate() { Work(inst); });
            th.IsBackground = true;
            th.Name = "NeuzBlox.Launch." + inst.Slot;
            th.Start();
        }

        void Work(RbxInstance inst)
        {
            // Only one client is allowed to bootstrap at a time. If Roblox decides it is out
            // of date, each client it starts spawns its own installer, and two installers
            // abort each other - which closes both clients a few seconds in.
            lock (LaunchGate)
            {
                WorkGated(inst);
            }
        }

        void WorkGated(RbxInstance inst)
        {
            try
            {
                if (RobloxClient.IsUpdating())
                {
                    inst.State = InstState.Ticket;
                    inst.Message = "Roblox is installing an update - waiting";
                    Fire();
                    RobloxClient.WaitWhileUpdating(900000, delegate(int secs)
                    {
                        inst.Message = "Roblox is installing an update - waiting " + RobloxClient.Elapsed(secs);
                        Fire();
                    });
                }

                // Resolve the client fresh: an update moves it to a new version folder.
                string player = PlayerPathProvider();
                if (string.IsNullOrEmpty(player))
                {
                    Fail(inst, "RobloxPlayerBeta.exe not found. Set the path in Settings.");
                    return;
                }

                inst.State = InstState.Ticket;
                inst.Message = "Requesting launch ticket";
                Fire();

                string btid = inst.Account.EnsureTracker();
                TicketResult tr;

                // Roblox is unhappy with bursts of ticket requests; space them out.
                lock (TicketGate)
                {
                    TimeSpan since = DateTime.UtcNow - _lastTicket;
                    if (since < TimeSpan.FromMilliseconds(1200))
                        Thread.Sleep((int)(1200 - since.TotalMilliseconds));
                    tr = RobloxApi.GetAuthTicket(inst.Account.Cookie, btid);
                    _lastTicket = DateTime.UtcNow;
                }

                if (!string.IsNullOrEmpty(tr.RefreshedCookie) && tr.RefreshedCookie != inst.Account.Cookie)
                {
                    inst.Account.Cookie = tr.RefreshedCookie;
                    var cb = AccountUpdated;
                    if (cb != null) cb(inst.Account);
                }

                if (!tr.Ok)
                {
                    Fail(inst, tr.Error);
                    return;
                }

                inst.State = InstState.Starting;
                inst.Message = "Starting client";
                Fire();

                string uri = RobloxClient.BuildLaunchUri(tr.Ticket, inst.Target, btid, Config.Locale);
                Process p = RobloxClient.Launch(player, uri);
                if (p == null)
                {
                    Fail(inst, "The client process did not start.");
                    return;
                }

                inst.Proc = p;
                inst.Pid = p.Id;
                inst.StartedUtc = DateTime.UtcNow;
                inst.Message = "Client starting";
                inst.Account.LastUsed = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                var cb2 = AccountUpdated;
                if (cb2 != null) cb2(inst.Account);
                Log.Write("Launched slot " + inst.Slot + " for " + inst.Account.Label + " pid=" + inst.Pid + " target=" + inst.TargetLabel);
                Fire();

                Settle(inst);
            }
            catch (Exception ex)
            {
                Fail(inst, ex.Message);
            }
        }

        /// <summary>
        /// Holds the launch gate until this client is really on its feet, so the next one
        /// does not start into the middle of an update.
        /// </summary>
        void Settle(RbxInstance inst)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline)
            {
                bool exited;
                try { exited = inst.Proc.HasExited; }
                catch { exited = true; }
                if (exited) break;

                if (Native.MainWindowOfProcess(inst.Pid) != IntPtr.Zero)
                {
                    inst.EverHadWindow = true;
                    Thread.Sleep(600);
                    return;
                }
                Thread.Sleep(350);
            }
            RobloxClient.WaitWhileUpdating(900000, delegate(int secs)
            {
                inst.Message = "Roblox is installing an update - waiting " + RobloxClient.Elapsed(secs);
                Fire();
            });
        }

        void Fail(RbxInstance inst, string msg)
        {
            inst.State = InstState.Failed;
            inst.Message = string.IsNullOrEmpty(msg) ? "Failed" : msg;
            inst.EndedUtc = DateTime.UtcNow;
            Log.Write("Slot " + inst.Slot + " failed: " + inst.Message);
            var s = Status;
            if (s != null) s(inst.Account.Label + ": " + inst.Message, true);
            Fire();
        }

        /// <summary>Called on a UI timer: refresh window handles, titles and exit state.</summary>
        public void Poll()
        {
            bool changed = false;
            List<RbxInstance> list = Snapshot();

            foreach (RbxInstance inst in list)
            {
                if (inst.Proc == null) continue;

                bool exited;
                try { exited = inst.Proc.HasExited; }
                catch { exited = true; }

                if (exited)
                {
                    if (inst.State != InstState.Closed && inst.State != InstState.Failed)
                    {
                        bool diedStartingUp = !inst.UserClosed && !inst.EverHadWindow;

                        inst.State = InstState.Closed;
                        inst.EndedUtc = DateTime.UtcNow;
                        inst.Message = inst.UserClosed ? "Closed by you"
                                     : (diedStartingUp ? "Closed before it opened - Roblox was probably updating"
                                                       : "Client exited");
                        inst.Hwnd = IntPtr.Zero;
                        changed = true;

                        // A client that never got as far as a window almost always means Roblox
                        // updated itself underneath us. An unfinished install can eat two
                        // attempts before the download completes, so allow a couple.
                        if (diedStartingUp && inst.StartupRetries < 2)
                        {
                            inst.StartupRetries++;
                            inst.State = InstState.Pending;
                            inst.Message = "Retrying after Roblox update";
                            inst.EndedUtc = null;
                            inst.Proc = null;
                            inst.Pid = 0;
                            inst.TitleApplied = false;
                            inst.StartedUtc = DateTime.UtcNow;
                            Log.Write("Slot " + inst.Slot + " died during startup, retrying once");
                            StartWorker(inst);
                            continue;
                        }

                        if (!inst.UserClosed && Config.AutoRejoin && inst.Rejoins < Config.MaxRejoins)
                        {
                            inst.Rejoins++;
                            inst.State = InstState.Pending;
                            inst.Message = "Rejoining (" + inst.Rejoins + "/" + Config.MaxRejoins + ")";
                            inst.EndedUtc = null;
                            inst.Proc = null;
                            inst.Pid = 0;
                            inst.TitleApplied = false;
                            inst.StartedUtc = DateTime.UtcNow;
                            StartWorker(inst);
                        }
                    }
                    continue;
                }

                if (inst.Hwnd == IntPtr.Zero || !Native.IsWindow(inst.Hwnd))
                {
                    IntPtr h = Native.MainWindowOfProcess(inst.Pid);
                    if (h != IntPtr.Zero)
                    {
                        inst.Hwnd = h;
                        inst.EverHadWindow = true;
                        inst.TitleApplied = false;
                        changed = true;
                    }
                }

                // what game this client wandered into after launch
                LiveGame g = GameWatcher.ForUser(inst.Account.UserId);
                long place = g != null ? g.PlaceId : 0;
                string gname = g != null ? g.Name : "";
                if (place != inst.LivePlaceId || gname != inst.LiveGameName)
                {
                    inst.LivePlaceId = place;
                    inst.LiveGameName = gname;
                    changed = true;
                }

                if (inst.Hwnd != IntPtr.Zero)
                {
                    if (inst.State != InstState.Running)
                    {
                        inst.State = InstState.Running;
                        inst.Message = "Running";
                        changed = true;
                    }
                    if (Config.RenameWindows && !inst.TitleApplied)
                    {
                        string title = (Config.TitleFormat ?? "{alias} - NeuzBlox")
                            .Replace("{alias}", inst.Account.Label)
                            .Replace("{user}", string.IsNullOrEmpty(inst.Account.Username) ? inst.Account.Label : inst.Account.Username)
                            .Replace("{slot}", inst.Slot.ToString(CultureInfo.InvariantCulture))
                            .Replace("{target}", inst.TargetLabel);
                        try { Native.SetWindowText(inst.Hwnd, title); inst.TitleApplied = true; }
                        catch { }
                    }
                }
            }

            if (changed) Fire();
        }

        public void Close(RbxInstance inst)
        {
            if (inst == null) return;
            inst.UserClosed = true;
            try
            {
                if (inst.Proc != null && !inst.Proc.HasExited)
                {
                    inst.Proc.CloseMainWindow();
                    if (!inst.Proc.WaitForExit(2500)) inst.Proc.Kill();
                }
            }
            catch { try { if (inst.Proc != null) inst.Proc.Kill(); } catch { } }
            inst.State = InstState.Closed;
            inst.Message = "Closed by you";
            inst.EndedUtc = DateTime.UtcNow;
            inst.Hwnd = IntPtr.Zero;
            Fire();
        }

        public void CloseAll()
        {
            foreach (RbxInstance i in Snapshot())
                if (i.IsLive) Close(i);
        }

        /// <summary>Tiles every running client across the primary monitor's work area.</summary>
        public int Arrange(string layout)
        {
            var live = new List<RbxInstance>();
            foreach (RbxInstance i in Snapshot())
                if (i.State == InstState.Running && i.Hwnd != IntPtr.Zero && Native.IsWindow(i.Hwnd))
                    live.Add(i);

            int n = live.Count;
            if (n == 0) return 0;

            System.Drawing.Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int cols, rows;
            switch ((layout ?? "grid").ToLowerInvariant())
            {
                case "columns": cols = n; rows = 1; break;
                case "rows": cols = 1; rows = n; break;
                case "stack":
                    {
                        int w = (int)(wa.Width * 0.62), h = (int)(wa.Height * 0.72);
                        int step = 34;
                        for (int i = 0; i < n; i++)
                        {
                            Native.ShowWindow(live[i].Hwnd, Native.SW_RESTORE);
                            Native.MoveWindow(live[i].Hwnd, wa.X + 40 + i * step, wa.Y + 30 + i * step, w, h, true);
                        }
                        return n;
                    }
                default:
                    cols = (int)Math.Ceiling(Math.Sqrt(n));
                    rows = (int)Math.Ceiling((double)n / cols);
                    break;
            }

            int cw = wa.Width / Math.Max(1, cols);
            int ch = wa.Height / Math.Max(1, rows);
            for (int i = 0; i < n; i++)
            {
                int cx = i % cols, cy = i / cols;
                Native.ShowWindow(live[i].Hwnd, Native.SW_RESTORE);
                Native.MoveWindow(live[i].Hwnd, wa.X + cx * cw, wa.Y + cy * ch, cw, ch, true);
            }
            return n;
        }

        void Fire()
        {
            var c = Changed;
            if (c != null) { try { c(); } catch { } }
        }
    }
}
