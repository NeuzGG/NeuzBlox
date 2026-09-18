using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

// Dev helper: click at client-relative coordinates inside a process's main window.
// usage: Click.exe <processName> <x> <y>
static class Click
{
    delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr extra);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, WHEEL = 0x0800;

    static void Main(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("usage: Click.exe <process> <x> <y> [wheel <notches>]"); return; }
        string name = args[0];
        int cx = int.Parse(args[1]);
        int cy = int.Parse(args[2]);
        int wheel = (args.Length > 4 && args[3] == "wheel") ? int.Parse(args[4]) : 0;

        IntPtr best = IntPtr.Zero;
        long bestArea = -1;
        foreach (Process p in Process.GetProcessesByName(name))
        {
            int pid = p.Id;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint wpid;
                GetWindowThreadProcessId(h, out wpid);
                if (wpid != (uint)pid || !IsWindowVisible(h)) return true;
                RECT r;
                if (!GetWindowRect(h, out r)) return true;
                long a = (long)Math.Max(0, r.R - r.L) * Math.Max(0, r.B - r.T);
                if (a > bestArea) { bestArea = a; best = h; }
                return true;
            }, IntPtr.Zero);
        }

        if (best == IntPtr.Zero) { Console.WriteLine("window not found"); return; }

        SetForegroundWindow(best);
        Thread.Sleep(250);

        var pt = new POINT();
        pt.X = cx; pt.Y = cy;
        ClientToScreen(best, ref pt);
        SetCursorPos(pt.X, pt.Y);
        Thread.Sleep(80);

        if (wheel != 0)
        {
            for (int i = 0; i < Math.Abs(wheel); i++)
            {
                mouse_event(WHEEL, 0, 0, (uint)(wheel > 0 ? 120 : -120), IntPtr.Zero);
                Thread.Sleep(45);
            }
            Console.WriteLine("scrolled " + wheel + " at " + pt.X + "," + pt.Y);
            return;
        }

        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(40);
        mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        Console.WriteLine("clicked " + pt.X + "," + pt.Y);
    }
}
