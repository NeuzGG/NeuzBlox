using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace NeuzBlox
{
    /// <summary>What the splash screen loaded, handed straight to the main window.</summary>
    public class Boot
    {
        public Settings Cfg;
        public List<Account> Accounts;
        public string PlayerPath;
    }

    /// <summary>
    /// The loading screen. It is not decoration for its own sake - the slow parts of
    /// startup (reading the vault, asking Roblox which client build is current) happen
    /// here, so the main window opens already knowing everything.
    /// </summary>
    public class Splash : Form
    {
        public Boot Result;

        readonly Anim _fade;
        readonly Anim _progress;
        readonly Anim _shimmer;
        readonly Anim _rise;
        readonly System.Windows.Forms.Timer _poll = new System.Windows.Forms.Timer();

        volatile string _stage = "Starting up";
        volatile float _want;
        volatile bool _done;
        DateTime _shownAt;
        bool _closing;

        const int W = 480, H = 300;

        public Splash()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(W, H);
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Opacity = 0;
            Icon = AppIcon.Get();
            Text = "NeuzBlox";

            using (var path = Theme.Round(new Rectangle(0, 0, W, H), 16))
                Region = new Region(path);

            _fade = new Anim(this, 0f);
            _progress = new Anim(this, 0f);
            _shimmer = new Anim(this, 0f);
            _rise = new Anim(this, 0f);

            _poll.Interval = 30;
            _poll.Tick += delegate
            {
                _progress.To(_want, 420, Ease.OutCubic);
                Invalidate();
                if (_done && !_closing && (DateTime.UtcNow - _shownAt).TotalMilliseconds > 1400)
                    BeginFadeOut();
            };

            Load += delegate
            {
                _shownAt = DateTime.UtcNow;
                _rise.To(1f, 620, Ease.OutQuint);
                _shimmer.Loop(1600);
                StartFadeIn();
                _poll.Start();
                StartWork();
            };
        }

        void StartFadeIn()
        {
            if (!Anim.Enabled) { Opacity = 1; return; }
            var t = new System.Windows.Forms.Timer();
            t.Interval = 16;
            double v = 0;
            t.Tick += delegate
            {
                v += 0.09;
                if (v >= 1) { v = 1; t.Stop(); t.Dispose(); }
                try { Opacity = v; }
                catch { }
            };
            t.Start();
        }

        void BeginFadeOut()
        {
            _closing = true;
            _poll.Stop();
            // a looping anim never finishes on its own, and would keep the shared 60fps
            // clock awake for the rest of the app's life
            _shimmer.Stop();
            _rise.Stop();
            _progress.Stop();
            if (!Anim.Enabled) { Close(); return; }
            var t = new System.Windows.Forms.Timer();
            t.Interval = 16;
            double v = 1;
            t.Tick += delegate
            {
                v -= 0.10;
                if (v <= 0)
                {
                    t.Stop();
                    t.Dispose();
                    Close();
                    return;
                }
                try { Opacity = v; }
                catch { }
            };
            t.Start();
        }

        void StartWork()
        {
            var th = new Thread(delegate()
            {
                var boot = new Boot();
                try
                {
                    Step("Loading settings", 0.14f);
                    boot.Cfg = Settings.Load();
                    Anim.Enabled = boot.Cfg.Animations;

                    Step("Opening the account vault", 0.34f);
                    boot.Accounts = AccountStore.Load();

                    Step("Asking Roblox which client is current", 0.62f);
                    boot.PlayerPath = RobloxClient.FindPlayer(boot.Cfg.PlayerPath, true);

                    Step("Checking the multi-instance lock", 0.86f);
                    Thread.Sleep(120);

                    Step("Ready", 1f);
                }
                catch (Exception ex)
                {
                    Log.Write("Splash work failed: " + ex.Message);
                    if (boot.Cfg == null) boot.Cfg = new Settings();
                    if (boot.Accounts == null) boot.Accounts = new List<Account>();
                }
                Result = boot;
                _done = true;
            });
            th.IsBackground = true;
            th.Name = "NeuzBlox.Boot";
            th.Start();
        }

        void Step(string text, float pct)
        {
            _stage = text;
            _want = pct;
            Thread.Sleep(90);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);

            var full = new Rectangle(0, 0, W, H);
            using (var bg = new LinearGradientBrush(full, Color.FromArgb(14, 17, 24), Color.FromArgb(9, 11, 16), 60f))
                g.FillRectangle(bg, full);

            // soft accent glow behind the mark
            using (var glow = new GraphicsPath())
            {
                glow.AddEllipse(-120, -150, 420, 340);
                using (var br = new PathGradientBrush(glow))
                {
                    br.CenterColor = Color.FromArgb(70, Theme.Accent);
                    br.SurroundColors = new Color[] { Color.FromArgb(0, Theme.Accent) };
                    g.FillPath(br, glow);
                }
            }
            using (var glow2 = new GraphicsPath())
            {
                glow2.AddEllipse(W - 250, H - 190, 380, 300);
                using (var br = new PathGradientBrush(glow2))
                {
                    br.CenterColor = Color.FromArgb(52, Theme.Accent2);
                    br.SurroundColors = new Color[] { Color.FromArgb(0, Theme.Accent2) };
                    g.FillPath(br, glow2);
                }
            }

            float rise = _rise.Value;
            int lift = (int)((1f - rise) * 14f);
            int alpha = (int)(rise * 255f);
            if (alpha < 0) alpha = 0;
            if (alpha > 255) alpha = 255;

            var mark = new Rectangle(W / 2 - 34, 46 + lift, 68, 68);
            if (rise > 0.02f) Theme.DrawMark(g, mark);

            var mid = new StringFormat();
            mid.Alignment = StringAlignment.Center;
            mid.LineAlignment = StringAlignment.Center;

            using (var f = new Font("Segoe UI Semibold", 22f))
            using (var b = new SolidBrush(Color.FromArgb(alpha, Theme.Text)))
                g.DrawString("NeuzBlox", f, b, new RectangleF(0, 126 + lift, W, 36), mid);

            using (var b = new SolidBrush(Color.FromArgb((int)(alpha * 0.75f), Theme.TextDim)))
                g.DrawString("multi-instance launcher", Theme.F9, b, new RectangleF(0, 160 + lift, W, 20), mid);

            // progress track
            var track = new Rectangle(70, 214, W - 140, 6);
            Theme.FillRound(g, track, 3, Color.FromArgb(40, 46, 64));

            float p = _progress.Value;
            if (p > 0.001f)
            {
                int fw = (int)(track.Width * p);
                if (fw < 6) fw = 6;
                var fill = new Rectangle(track.X, track.Y, fw, track.Height);
                Theme.GradientRound(g, fill, 3, Theme.Accent, Theme.Accent2);

                // shimmer sweeping along the filled part
                if (Anim.Enabled)
                {
                    float s = _shimmer.Value;
                    int sx = track.X + (int)(fw * s) - 32;
                    var band = new Rectangle(sx, track.Y, 64, track.Height);
                    var clip = new Rectangle(fill.X, fill.Y - 2, fill.Width, fill.Height + 4);
                    Region old = g.Clip;
                    g.SetClip(clip, CombineMode.Replace);
                    using (var sh = new LinearGradientBrush(
                        new Rectangle(band.X, band.Y, Math.Max(1, band.Width), Math.Max(1, band.Height)),
                        Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0f))
                    {
                        var blend = new ColorBlend(3);
                        blend.Colors = new Color[]
                        {
                            Color.FromArgb(0, 255, 255, 255),
                            Color.FromArgb(150, 255, 255, 255),
                            Color.FromArgb(0, 255, 255, 255)
                        };
                        blend.Positions = new float[] { 0f, 0.5f, 1f };
                        sh.InterpolationColors = blend;
                        g.FillRectangle(sh, band);
                    }
                    g.Clip = old;
                }
            }

            using (var b = new SolidBrush(Theme.TextDim))
                g.DrawString(_stage, Theme.F8, b, new RectangleF(0, 232, W, 18), mid);

            using (var b = new SolidBrush(Theme.TextFaint))
            {
                g.DrawString("v" + AppInfo.Version, Theme.F8, b, 22, H - 30);
                var right = new StringFormat();
                right.Alignment = StringAlignment.Far;
                g.DrawString("by " + AppInfo.Author, Theme.F8, b, new RectangleF(0, H - 30, W - 22, 18), right);
                right.Dispose();
            }

            mid.Dispose();

            using (var pen = new Pen(Color.FromArgb(58, 66, 92), 1f))
            using (var path = Theme.Round(new Rectangle(0, 0, W - 1, H - 1), 16))
                g.DrawPath(pen, path);
        }
    }
}
