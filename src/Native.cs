using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace NeuzBlox
{
    /// <summary>Win32 interop used for window discovery, titling, tiling and dark chrome.</summary>
    public static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetWindowText(IntPtr hWnd, string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessageStr(IntPtr hWnd, int Msg, IntPtr wParam, string lParam);

        public const int EM_SETCUEBANNER = 0x1501;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS { public int L, R, T, B; }

        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_MAXIMIZE = 3;
        public const int SW_RESTORE = 9;

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HTCAPTION = 0x2;

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        /// <summary>Paints the native title bar dark on Windows 10 1809+ / Windows 11.</summary>
        public static void UseDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
            }
            catch { }
        }

        public static string GetText(IntPtr hWnd)
        {
            int len = GetWindowTextLength(hWnd);
            if (len <= 0) return "";
            var sb = new StringBuilder(len + 2);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetClass(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>All visible top-level windows belonging to a process id.</summary>
        public static List<IntPtr> WindowsOfProcess(int pid)
        {
            var found = new List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint wpid;
                GetWindowThreadProcessId(h, out wpid);
                if (wpid == (uint)pid && IsWindowVisible(h)) found.Add(h);
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>Best guess at a process's main window (largest visible window with a class name).</summary>
        public static IntPtr MainWindowOfProcess(int pid)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = -1;
            foreach (IntPtr h in WindowsOfProcess(pid))
            {
                RECT r;
                if (!GetWindowRect(h, out r)) continue;
                long area = (long)Math.Max(0, r.Right - r.Left) * Math.Max(0, r.Bottom - r.Top);
                if (area > bestArea) { bestArea = area; best = h; }
            }
            return best;
        }

        /// <summary>Reliable-ish foreground activation (Windows blocks naive SetForegroundWindow).</summary>
        public static void Focus(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;
            try
            {
                ShowWindow(hWnd, SW_RESTORE);
                uint fgPid;
                uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out fgPid);
                uint thisThread = GetCurrentThreadId();
                if (fgThread != thisThread) AttachThreadInput(thisThread, fgThread, true);
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
                if (fgThread != thisThread) AttachThreadInput(thisThread, fgThread, false);
            }
            catch { }
        }
    }
}
