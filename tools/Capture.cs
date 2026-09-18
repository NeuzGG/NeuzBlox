using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Dev helper: bring a process's windows to the front and screenshot the desktop.
// usage: Capture.exe <out.png> [processName] [delayMs]
static class Capture
{
    delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool f);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int L, T, R, B; }

    [STAThread]
    static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "shot.png";
        string procName = args.Length > 1 ? args[1] : null;
        int delay = args.Length > 2 ? int.Parse(args[2]) : 700;
        bool windowOnly = args.Length > 3 && args[3] == "window";
        IntPtr shot = IntPtr.Zero;

        if (!string.IsNullOrEmpty(procName))
        {
            Process[] ps = Process.GetProcessesByName(procName);
            var wins = new List<IntPtr>();
            foreach (Process p in ps)
            {
                int pid = p.Id;
                EnumWindows(delegate(IntPtr h, IntPtr l)
                {
                    uint wpid;
                    GetWindowThreadProcessId(h, out wpid);
                    if (wpid == (uint)pid && IsWindowVisible(h)) wins.Add(h);
                    return true;
                }, IntPtr.Zero);
            }
            // biggest first so the main window is raised before any modal on top of it
            wins.Sort(delegate(IntPtr a, IntPtr b) { return Area(b).CompareTo(Area(a)); });
            foreach (IntPtr h in wins) Raise(h);
            if (wins.Count > 0) shot = wins[0];
        }

        Thread.Sleep(delay);

        // Window mode captures only the app's own rectangle. If the window cannot be
        // found it writes nothing at all - it must never quietly fall back to grabbing
        // the whole desktop, which would capture whatever else the user has open.
        if (windowOnly)
        {
            RECT wr;
            if (shot == IntPtr.Zero || !GetWindowRect(shot, out wr))
            {
                Console.WriteLine("window not visible - captured nothing");
                return;
            }
            {
                var area = new Rectangle(wr.L, wr.T, Math.Max(1, wr.R - wr.L), Math.Max(1, wr.B - wr.T));
                area.Intersect(Screen.PrimaryScreen.Bounds);
                using (var wb = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(wb))
                        g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                    wb.Save(outPath, ImageFormat.Png);
                }
                Console.WriteLine("saved window " + outPath);
                return;
            }
        }

        Rectangle b2 = Screen.PrimaryScreen.Bounds;
        using (var bmp = new Bitmap(b2.Width, b2.Height, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
                g.CopyFromScreen(b2.Location, Point.Empty, b2.Size);
            bmp.Save(outPath, ImageFormat.Png);
        }
        Console.WriteLine("saved " + outPath);
    }

    static long Area(IntPtr h)
    {
        RECT r;
        if (!GetWindowRect(h, out r)) return 0;
        return (long)Math.Max(0, r.R - r.L) * Math.Max(0, r.B - r.T);
    }

    static void Raise(IntPtr h)
    {
        uint pid;
        uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        uint me = GetCurrentThreadId();
        if (fg != me) AttachThreadInput(me, fg, true);
        ShowWindow(h, 9);
        BringWindowToTop(h);
        SetForegroundWindow(h);
        if (fg != me) AttachThreadInput(me, fg, false);
        Thread.Sleep(120);
    }
}
