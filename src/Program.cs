using System;
using System.Drawing;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace NeuzBlox
{
    public static class AppInfo
    {
        public const string Version = "1.1.0";
        public const string Author = "neuzgg";
        public const string GitHubHandle = "github.com/neuzgg";
        public const string GitHubUrl = "https://github.com/neuzgg";

        /// <summary>
        /// The Discord application NeuzBlox publishes presence under, so ticking the box is
        /// all anyone has to do. An application ID is public - it rides along in every
        /// presence payload - so shipping it is normal and safe. The client SECRET and any
        /// bot token are the parts that must never appear here.
        /// </summary>
        public const string DefaultDiscordAppId = "1550508640245514322";

        public static string BuildDate
        {
            get
            {
                try
                {
                    return System.IO.File.GetLastWriteTime(Application.ExecutablePath)
                        .ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
                }
                catch { return ""; }
            }
        }
    }

    public static class AppIcon
    {
        static Icon _cached;

        public static Icon Get()
        {
            if (_cached != null) return _cached;
            try { _cached = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
            if (_cached == null) _cached = Draw();
            return _cached;
        }

        /// <summary>Fallback mark if the embedded icon cannot be read.</summary>
        static Icon Draw()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    Theme.Smooth(g);
                    g.Clear(Color.Transparent);
                    Theme.GradientRound(g, new Rectangle(0, 0, 32, 32), 8, Theme.Accent, Theme.Accent2);
                    using (var b = new SolidBrush(Color.White))
                    using (var f = new Font("Segoe UI Semibold", 15f))
                        g.DrawString("N", f, b, new RectangleF(0, 1, 32, 30),
                            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
                ServicePointManager.DefaultConnectionLimit = 24;
                ServicePointManager.Expect100Continue = false;
            }
            catch { }

            bool createdNew;
            using (var single = new Mutex(true, "NeuzBlox_SingleInstance_v1", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "NeuzBlox is already running.\r\n\r\nLook for it in the system tray near the clock.",
                        "NeuzBlox", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log.Write("UI exception: " + e.Exception);
                    MessageBox.Show("Something went wrong:\r\n\r\n" + e.Exception.Message,
                        "NeuzBlox", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Write("Fatal: " + e.ExceptionObject);
                };

                Log.Write("NeuzBlox " + AppInfo.Version + " started.");
                try
                {
                    // The splash does the slow half of startup (vault, Roblox version lookup)
                    // and hands the result over, so the main window opens already warm.
                    Boot boot = null;
                    if (Settings.Load().ShowSplash)
                    {
                        var splash = new Splash();
                        Application.Run(splash);
                        boot = splash.Result;
                        splash.Dispose();
                    }
                    Application.Run(new MainForm(boot));
                }
                catch (Exception ex)
                {
                    Log.Write("Startup failure: " + ex);
                    MessageBox.Show("NeuzBlox could not start:\r\n\r\n" + ex.Message +
                        "\r\n\r\nDetails were written to\r\n" + Paths.LogFile,
                        "NeuzBlox", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                Log.Write("NeuzBlox exited.");
            }
        }
    }
}
