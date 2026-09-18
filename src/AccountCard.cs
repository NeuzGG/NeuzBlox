using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace NeuzBlox
{
    public class AccountCard : Control
    {
        public Account Model;
        public Image Avatar;

        readonly NCheck _pick = new NCheck();
        readonly NButton _launch = new NButton();
        readonly NButton _edit = new NButton();
        readonly NButton _del = new NButton();
        readonly Anim _enter;
        readonly Anim _hoverA;

        public event EventHandler LaunchClicked;
        public event EventHandler EditClicked;
        public event EventHandler RemoveClicked;
        public event EventHandler SelectionChanged;

        public AccountCard(Account model)
        {
            Model = model;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Bg;
            Height = 74;

            _enter = new Anim(this, Anim.Enabled ? 0f : 1f);
            _hoverA = new Anim(this, 0f);

            _pick.Text = "";
            _pick.Width = 22;
            _pick.Height = 22;
            _pick.CheckedChanged += delegate
            {
                Invalidate();
                var h = SelectionChanged;
                if (h != null) h(this, EventArgs.Empty);
            };

            _launch.Text = "Launch";
            _launch.Glyph = "";
            _launch.Kind = BtnKind.Accent;
            _launch.Width = 96;
            _launch.Height = 30;
            _launch.Click += delegate { var h = LaunchClicked; if (h != null) h(this, EventArgs.Empty); };

            _edit.Glyph = "";
            _edit.Kind = BtnKind.Ghost;
            _edit.Width = 34;
            _edit.Height = 30;
            _edit.Click += delegate { var h = EditClicked; if (h != null) h(this, EventArgs.Empty); };

            _del.Glyph = "";
            _del.Kind = BtnKind.Danger;
            _del.Width = 34;
            _del.Height = 30;
            _del.Click += delegate { var h = RemoveClicked; if (h != null) h(this, EventArgs.Empty); };

            Controls.Add(_pick);
            Controls.Add(_launch);
            Controls.Add(_edit);
            Controls.Add(_del);

            var tip = new ToolTip();
            tip.SetToolTip(_edit, "Edit alias, note or cookie");
            tip.SetToolTip(_del, "Remove this account from NeuzBlox");

            SetChildrenVisible(!Anim.Enabled);
        }

        /// <summary>Fades and lifts the row in; staggered by the list so they cascade.</summary>
        public void PlayEntrance(int index)
        {
            if (!Anim.Enabled) { _enter.Set(1f); SetChildrenVisible(true); return; }
            _enter.Set(0f);
            SetChildrenVisible(false);
            _enter.Completed = delegate { SetChildrenVisible(true); };
            _enter.To(1f, 380, Ease.OutQuint, Math.Min(index, 12) * 45);
        }

        void SetChildrenVisible(bool v)
        {
            _pick.Visible = v;
            _launch.Visible = v;
            _edit.Visible = v;
            _del.Visible = v;
        }

        public bool Selected
        {
            get { return _pick.Checked; }
            set { _pick.Checked = value; }
        }

        protected override void OnMouseEnter(EventArgs e) { _hoverA.To(1f, 160); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hoverA.To(0f, 240); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.X > 40 && e.X < Width - 190) Selected = !Selected;
            base.OnMouseClick(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _pick.Location = new Point(14, (Height - _pick.Height) / 2);
            int right = Width - 16;
            _del.Location = new Point(right - _del.Width, (Height - _del.Height) / 2);
            _edit.Location = new Point(_del.Left - 6 - _edit.Width, (Height - _edit.Height) / 2);
            _launch.Location = new Point(_edit.Left - 8 - _launch.Width, (Height - _launch.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(BackColor);

            float v = _enter.Value;
            if (v <= 0.005f) return;
            float hv = _hoverA.Value;

            // rise in on entrance, lift a hair on hover
            g.TranslateTransform(0f, (1f - v) * 10f - hv * 1.5f);

            // fade by blending toward the page background - the row is opaque anyway
            Func<Color, Color> fade = delegate(Color c)
            {
                return v >= 0.999f ? c : Theme.Mix(BackColor, c, v);
            };

            var r = new Rectangle(0, 2, Width, Height - 6);

            Color fill = Selected
                ? Theme.Mix(Color.FromArgb(26, 32, 48), Color.FromArgb(32, 39, 58), hv)
                : Theme.Mix(Theme.Surface, Theme.Surface2, hv);
            Theme.FillRound(g, r, 12, fade(fill));

            Color stroke = Selected ? Theme.Accent : Theme.Mix(Theme.Border, Theme.BorderHi, hv);
            Theme.DrawRound(g, r, 12, fade(stroke), Selected ? 1.4f : 1f);

            int ax = 48, ay = r.Y + (r.Height - 42) / 2;
            var av = new Rectangle(ax, ay, 42, 42);
            Color hue = Theme.Hue(Model.Id);

            if (Avatar != null)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(av);
                    Region old = g.Clip;
                    g.SetClip(path, CombineMode.Replace);
                    if (v >= 0.999f)
                    {
                        g.DrawImage(Avatar, av);
                    }
                    else
                    {
                        using (var ia = new ImageAttributes())
                        {
                            var cm = new ColorMatrix();
                            cm.Matrix33 = v;
                            ia.SetColorMatrix(cm);
                            g.DrawImage(Avatar, av, 0, 0, Avatar.Width, Avatar.Height, GraphicsUnit.Pixel, ia);
                        }
                    }
                    g.Clip = old;
                }
                using (var pen = new Pen(fade(Color.FromArgb(120, hue)), 1.6f))
                    g.DrawEllipse(pen, av);
            }
            else
            {
                using (var b = new SolidBrush(fade(Color.FromArgb(48, hue))))
                    g.FillEllipse(b, av);
                using (var pen = new Pen(fade(Color.FromArgb(130, hue)), 1.4f))
                    g.DrawEllipse(pen, av);
                var sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                using (var b = new SolidBrush(fade(hue)))
                    g.DrawString(Initials(Model.Label), Theme.F10B, b, av, sf);
                sf.Dispose();
            }

            int tx = av.Right + 14;
            int avail = Math.Max(40, Width - tx - 200);

            var cut = new StringFormat();
            cut.Trimming = StringTrimming.EllipsisCharacter;
            cut.FormatFlags = StringFormatFlags.NoWrap;

            using (var b = new SolidBrush(fade(Theme.Text)))
                g.DrawString(Model.Label, Theme.F10B, b, new RectangleF(tx, r.Y + 13, avail, 20), cut);
            using (var b = new SolidBrush(fade(Theme.TextDim)))
                g.DrawString(Model.Subtitle, Theme.F8, b, new RectangleF(tx, r.Y + 34, avail, 18), cut);
            cut.Dispose();

            string state;
            Color tint;
            if (Model.UserId <= 0) { state = "not verified"; tint = Theme.Amber; }
            else { state = "ready"; tint = Theme.Green; }

            var pill = new Rectangle(tx + (int)g.MeasureString(Model.Label, Theme.F10B).Width + 10, r.Y + 14, 0, 18);
            pill.Width = (int)g.MeasureString(state, Theme.F8).Width + 18;
            if (pill.Right < Width - 200)
            {
                Theme.FillRound(g, pill, 9, fade(Color.FromArgb(34, tint)));
                var sf2 = new StringFormat();
                sf2.Alignment = StringAlignment.Center;
                sf2.LineAlignment = StringAlignment.Center;
                using (var b = new SolidBrush(fade(tint)))
                    g.DrawString(state, Theme.F8, b, pill, sf2);
                sf2.Dispose();
            }
        }

        static string Initials(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            s = s.Trim();
            string[] parts = s.Split(new char[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return ("" + parts[0][0] + parts[1][0]).ToUpperInvariant();
            return s.Substring(0, Math.Min(2, s.Length)).ToUpperInvariant();
        }
    }
}
