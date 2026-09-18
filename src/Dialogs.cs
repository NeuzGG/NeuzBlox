using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace NeuzBlox
{
    /// <summary>Borderless dark modal with a draggable header.</summary>
    public class NDialog : Form
    {
        readonly Label _title = new Label();
        readonly NButton _x = new NButton();

        public NDialog(string title, int w, int h)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Theme.Surface;
            ClientSize = new Size(w, h);
            KeyPreview = true;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.Font;

            _title.Text = title;
            _title.Font = Theme.F12B;
            _title.ForeColor = Theme.Text;
            _title.BackColor = Color.Transparent;
            _title.AutoSize = true;
            _title.Location = new Point(20, 16);

            _x.Glyph = "";
            _x.Kind = BtnKind.Quiet;
            _x.Width = 34;
            _x.Height = 30;
            _x.Location = new Point(w - 46, 12);
            _x.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _x.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(_title);
            Controls.Add(_x);

            MouseDown += DragHeader;
            _title.MouseDown += DragHeader;

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            };
        }

        void DragHeader(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Point p = sender == this ? e.Location : new Point(e.X + ((Control)sender).Left, e.Y + ((Control)sender).Top);
            if (p.Y > 54) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.BorderHi, 1))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, Width, 3), Theme.Accent, Theme.Accent2,
                System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                e.Graphics.FillRectangle(br, 0, 0, Width, 3);
        }
    }

    public static class Dlg
    {
        public static bool Confirm(IWin32Window owner, string title, string message, string okText, bool danger)
        {
            using (var d = new NDialog(title, 460, 220))
            {
                var lbl = new Label();
                lbl.Text = message;
                lbl.Font = Theme.F9;
                lbl.ForeColor = Theme.TextDim;
                lbl.BackColor = Color.Transparent;
                lbl.Parent = d;
                lbl.SetBounds(20, 62, 420, 92);

                var ok = new NButton();
                ok.Text = okText;
                ok.Kind = danger ? BtnKind.Danger : BtnKind.Accent;
                ok.Width = 130;
                ok.Height = 34;
                ok.Location = new Point(310, 164);
                ok.Click += delegate { d.DialogResult = DialogResult.OK; d.Close(); };

                var cancel = new NButton();
                cancel.Text = "Cancel";
                cancel.Kind = BtnKind.Ghost;
                cancel.Width = 100;
                cancel.Height = 34;
                cancel.Location = new Point(200, 164);
                cancel.Click += delegate { d.DialogResult = DialogResult.Cancel; d.Close(); };

                d.Controls.Add(ok);
                d.Controls.Add(cancel);
                ok.BringToFront();
                cancel.BringToFront();
                return d.ShowDialog(owner) == DialogResult.OK;
            }
        }

        public static void Info(IWin32Window owner, string title, string message)
        {
            using (var d = new NDialog(title, 460, 210))
            {
                var lbl = new Label();
                lbl.Text = message;
                lbl.Font = Theme.F9;
                lbl.ForeColor = Theme.TextDim;
                lbl.BackColor = Color.Transparent;
                lbl.Parent = d;
                lbl.SetBounds(20, 62, 420, 90);

                var ok = new NButton();
                ok.Text = "Got it";
                ok.Kind = BtnKind.Accent;
                ok.Width = 120;
                ok.Height = 34;
                ok.Location = new Point(320, 156);
                ok.Click += delegate { d.DialogResult = DialogResult.OK; d.Close(); };
                d.Controls.Add(ok);
                ok.BringToFront();
                d.ShowDialog(owner);
            }
        }

        public static string Prompt(IWin32Window owner, string title, string label, string initial, string placeholder)
        {
            using (var d = new NDialog(title, 440, 210))
            {
                var lbl = new Label();
                lbl.Text = label;
                lbl.Font = Theme.F9;
                lbl.ForeColor = Theme.TextDim;
                lbl.BackColor = Color.Transparent;
                lbl.Parent = d;
                lbl.SetBounds(20, 62, 400, 18);

                var tb = new NTextBox();
                tb.Placeholder = placeholder;
                tb.Text = initial ?? "";
                tb.Parent = d;
                tb.SetBounds(20, 88, 400, 34);

                string result = null;
                var ok = new NButton();
                ok.Text = "Save";
                ok.Kind = BtnKind.Accent;
                ok.Width = 120;
                ok.Height = 34;
                ok.Location = new Point(300, 150);
                ok.Click += delegate { result = tb.Text.Trim(); d.DialogResult = DialogResult.OK; d.Close(); };
                d.Controls.Add(ok);
                ok.BringToFront();

                tb.KeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (e.KeyCode != Keys.Enter) return;
                    e.SuppressKeyPress = true;
                    result = tb.Text.Trim();
                    d.DialogResult = DialogResult.OK;
                    d.Close();
                };

                d.Shown += delegate { tb.Focus(); };
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                return result;
            }
        }
    }

    /// <summary>Add or edit one account. Verification happens off the UI thread.</summary>
    public class AccountDialog : NDialog
    {
        public Account Result;

        readonly Account _model;
        readonly NTextBox _alias = new NTextBox();
        readonly NTextBox _cookie = new NTextBox();
        readonly NTextBox _note = new NTextBox();
        readonly Label _status = new Label();
        readonly NButton _save = new NButton();
        readonly NButton _reveal = new NButton();
        readonly NButton _paste = new NButton();
        bool _busy;

        public AccountDialog(Account existing)
            : base(existing == null ? "Add account" : "Edit account", 560, 430)
        {
            _model = existing != null ? existing : new Account();

            var help = new Label();
            help.Text = "NeuzBlox signs a client in with your account's own session cookie, the same way\n" +
                        "your browser stays logged in. Paste the .ROBLOSECURITY value from a browser\n" +
                        "where this account is already signed in. It is stored encrypted for your\n" +
                        "Windows user only, and never leaves this PC except to roblox.com.";
            help.Font = Theme.F8;
            help.ForeColor = Theme.TextFaint;
            help.BackColor = Color.Transparent;
            help.Parent = this;
            help.SetBounds(20, 60, 520, 66);

            AddLabel("Display name in NeuzBlox", 132);
            _alias.Placeholder = "Main, Alt 1, Farm account ...";
            _alias.Text = _model.Alias;
            _alias.Parent = this;
            _alias.SetBounds(20, 152, 520, 34);

            AddLabel(".ROBLOSECURITY cookie", 196);
            _cookie.Placeholder = existing == null ? "_|WARNING:-DO-NOT-SHARE-THIS...." : "leave as-is to keep the saved cookie";
            _cookie.UseSystemPasswordChar = true;
            _cookie.Parent = this;
            _cookie.SetBounds(20, 216, 440, 34);

            _reveal.Glyph = "";
            _reveal.Kind = BtnKind.Ghost;
            _reveal.SetBounds(466, 216, 34, 34);
            _reveal.Click += delegate { _cookie.UseSystemPasswordChar = !_cookie.UseSystemPasswordChar; };
            Controls.Add(_reveal);

            _paste.Glyph = "";
            _paste.Kind = BtnKind.Ghost;
            _paste.SetBounds(506, 216, 34, 34);
            _paste.Click += delegate
            {
                try { if (Clipboard.ContainsText()) _cookie.Text = RobloxApi.NormalizeCookie(Clipboard.GetText()); }
                catch { }
            };
            Controls.Add(_paste);

            AddLabel("Note (optional)", 260);
            _note.Placeholder = "what you use this account for";
            _note.Text = _model.Note;
            _note.Parent = this;
            _note.SetBounds(20, 280, 520, 34);

            _status.Font = Theme.F8;
            _status.ForeColor = Theme.TextDim;
            _status.BackColor = Color.Transparent;
            _status.Parent = this;
            _status.SetBounds(20, 326, 520, 34);

            _save.Text = existing == null ? "Verify and add" : "Verify and save";
            _save.Kind = BtnKind.Accent;
            _save.SetBounds(360, 372, 180, 36);
            _save.Click += delegate { Commit(); };
            Controls.Add(_save);

            var cancel = new NButton();
            cancel.Text = "Cancel";
            cancel.Kind = BtnKind.Ghost;
            cancel.SetBounds(256, 372, 96, 36);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            var tip = new ToolTip();
            tip.SetToolTip(_reveal, "Show / hide the cookie");
            tip.SetToolTip(_paste, "Paste from clipboard");

            Shown += delegate { _alias.Focus(); };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Commit(); }
            };
        }

        void AddLabel(string text, int y)
        {
            var l = new Label();
            l.Text = text;
            l.Font = Theme.F9B;
            l.ForeColor = Theme.TextDim;
            l.BackColor = Color.Transparent;
            l.Parent = this;
            l.SetBounds(20, y, 400, 18);
        }

        void SetStatus(string msg, Color c)
        {
            _status.ForeColor = c;
            _status.Text = msg;
        }

        void Commit()
        {
            if (_busy) return;

            string cookie = RobloxApi.NormalizeCookie(_cookie.Text);
            if (string.IsNullOrEmpty(cookie)) cookie = _model.Cookie;

            if (!RobloxApi.LooksLikeCookie(cookie))
            {
                SetStatus("That does not look like a .ROBLOSECURITY value. Copy the whole cookie, warning text included.", Theme.Red);
                return;
            }

            _busy = true;
            _save.Enabled = false;
            SetStatus("Checking the session with Roblox...", Theme.Accent);

            string alias = _alias.Text.Trim();
            string note = _note.Text.Trim();
            string btid = _model.EnsureTracker();

            var th = new Thread(delegate()
            {
                UserResult ur = RobloxApi.WhoAmI(cookie, btid);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _save.Enabled = true;
                        if (!ur.Ok)
                        {
                            SetStatus(ur.Error, Theme.Red);
                            return;
                        }
                        _model.Cookie = !string.IsNullOrEmpty(ur.RefreshedCookie) ? ur.RefreshedCookie : cookie;
                        _model.Username = ur.User.Name;
                        _model.DisplayName = ur.User.DisplayName;
                        _model.UserId = ur.User.Id;
                        _model.Alias = alias.Length > 0 ? alias : ur.User.DisplayName;
                        _model.Note = note;
                        Result = _model;
                        DialogResult = DialogResult.OK;
                        Close();
                    });
                }
                catch { }
            });
            th.IsBackground = true;
            th.Start();
        }
    }
}
