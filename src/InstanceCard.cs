using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace NeuzBlox
{
    public class InstanceCard : Control
    {
        public RbxInstance Model;

        readonly NButton _focus = new NButton();
        readonly NButton _close = new NButton();
        readonly Anim _enter;
        readonly Anim _hoverA;
        readonly Anim _pulse;
        bool _pulsing;

        public event EventHandler FocusClicked;
        public event EventHandler CloseClicked;

        public InstanceCard(RbxInstance model)
        {
            Model = model;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Bg;
            Height = 70;

            _enter = new Anim(this, Anim.Enabled ? 0f : 1f);
            _hoverA = new Anim(this, 0f);
            _pulse = new Anim(this, 0f);

            _focus.Text = "Focus";
            _focus.Glyph = "";
            _focus.Kind = BtnKind.Ghost;
            _focus.Width = 88;
            _focus.Height = 30;
            _focus.Click += delegate { var h = FocusClicked; if (h != null) h(this, EventArgs.Empty); };

            _close.Glyph = "";
            _close.Kind = BtnKind.Danger;
            _close.Width = 34;
            _close.Height = 30;
            _close.Click += delegate { var h = CloseClicked; if (h != null) h(this, EventArgs.Empty); };

            Controls.Add(_focus);
            Controls.Add(_close);

            var tip = new ToolTip();
            tip.SetToolTip(_focus, "Bring this client to the front");
            tip.SetToolTip(_close, "Close this client");

            _focus.Visible = !Anim.Enabled;
            _close.Visible = !Anim.Enabled;
        }

        public void PlayEntrance(int index)
        {
            if (!Anim.Enabled) { _enter.Set(1f); _focus.Visible = true; _close.Visible = true; return; }
            _enter.Set(0f);
            _focus.Visible = false;
            _close.Visible = false;
            _enter.Completed = delegate { _focus.Visible = true; _close.Visible = true; };
            _enter.To(1f, 380, Ease.OutQuint, Math.Min(index, 12) * 45);
        }

        protected override void OnMouseEnter(EventArgs e) { _hoverA.To(1f, 160); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hoverA.To(0f, 240); base.OnMouseLeave(e); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int right = Width - 16;
            _close.Location = new Point(right - _close.Width, (Height - _close.Height) / 2);
            _focus.Location = new Point(_close.Left - 6 - _focus.Width, (Height - _focus.Height) / 2);
        }

        public void Sync()
        {
            bool live = Model.State == InstState.Running;
            _focus.Enabled = live && Model.Hwnd != IntPtr.Zero;
            _close.Enabled = Model.IsLive;

            // breathe the badge while a client is still coming up, settle once it is in
            bool shouldPulse = Anim.Enabled
                && (Model.State == InstState.Ticket || Model.State == InstState.Starting || Model.State == InstState.Pending);
            if (shouldPulse != _pulsing)
            {
                _pulsing = shouldPulse;
                if (shouldPulse) _pulse.Loop(1500);
                else { _pulse.Stop(); _pulse.Set(0f); }
            }

            Invalidate();
        }

        void StateVisual(out string label, out Color tint)
        {
            switch (Model.State)
            {
                case InstState.Running: label = "running"; tint = Theme.Green; break;
                case InstState.Ticket: label = "authenticating"; tint = Theme.Accent; break;
                case InstState.Starting: label = "starting"; tint = Theme.Accent; break;
                case InstState.Pending: label = "queued"; tint = Theme.TextDim; break;
                case InstState.Failed: label = "failed"; tint = Theme.Red; break;
                default: label = "closed"; tint = Theme.TextFaint; break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(BackColor);

            float v = _enter.Value;
            if (v <= 0.005f) return;
            float hv = _hoverA.Value;

            g.TranslateTransform(0f, (1f - v) * 10f - hv * 1.5f);

            Func<Color, Color> fade = delegate(Color c)
            {
                return v >= 0.999f ? c : Theme.Mix(BackColor, c, v);
            };

            var r = new Rectangle(0, 2, Width, Height - 6);
            Theme.FillRound(g, r, 12, fade(Theme.Mix(Theme.Surface, Theme.Surface2, hv)));
            Theme.DrawRound(g, r, 12, fade(Theme.Mix(Theme.Border, Theme.BorderHi, hv)), 1f);

            string state;
            Color tint;
            StateVisual(out state, out tint);

            // slot badge - breathes while the client is still starting
            var badge = new Rectangle(14, r.Y + (r.Height - 34) / 2, 34, 34);
            double breathe = _pulsing ? (0.5 - 0.5 * Math.Cos(_pulse.Value * Math.PI * 2)) : 0.0;

            if (_pulsing && breathe > 0.01)
            {
                int ring = (int)(6 * breathe);
                var halo = new Rectangle(badge.X - ring, badge.Y - ring, badge.Width + ring * 2, badge.Height + ring * 2);
                Theme.FillRound(g, halo, 10 + ring, fade(Color.FromArgb((int)(42 * (1 - breathe)), tint)));
            }

            Theme.FillRound(g, badge, 10, fade(Color.FromArgb((int)(40 + 34 * breathe), tint)));
            Theme.DrawRound(g, badge, 10, fade(Color.FromArgb((int)(110 + 80 * breathe), tint)), 1f);
            var mid = new StringFormat();
            mid.Alignment = StringAlignment.Center;
            mid.LineAlignment = StringAlignment.Center;
            using (var b = new SolidBrush(fade(tint)))
                g.DrawString(Model.Slot.ToString(CultureInfo.InvariantCulture), Theme.F10B, b, badge, mid);

            int tx = badge.Right + 14;
            int avail = Math.Max(40, Width - tx - 160);
            var cut = new StringFormat();
            cut.Trimming = StringTrimming.EllipsisCharacter;
            cut.FormatFlags = StringFormatFlags.NoWrap;

            using (var b = new SolidBrush(fade(Theme.Text)))
                g.DrawString(Model.Account.Label, Theme.F10B, b, new RectangleF(tx, r.Y + 11, avail, 20), cut);

            string line2 = Model.WhereLabel;
            if (!string.IsNullOrEmpty(Model.Message)) line2 += "  ·  " + Model.Message;
            if (Model.Pid > 0) line2 += "  ·  pid " + Model.Pid.ToString(CultureInfo.InvariantCulture);
            if (Model.IsLive || Model.EndedUtc.HasValue) line2 += "  ·  " + Model.Uptime;

            using (var b = new SolidBrush(fade(Model.State == InstState.Failed ? Theme.Red : Theme.TextDim)))
                g.DrawString(line2, Theme.F8, b, new RectangleF(tx, r.Y + 33, avail, 18), cut);
            cut.Dispose();

            var pill = new Rectangle(tx + (int)g.MeasureString(Model.Account.Label, Theme.F10B).Width + 10, r.Y + 12, 0, 18);
            pill.Width = (int)g.MeasureString(state, Theme.F8).Width + 18;
            if (pill.Right < Width - 160)
            {
                Theme.FillRound(g, pill, 9, fade(Color.FromArgb(34, tint)));
                using (var b = new SolidBrush(fade(tint)))
                    g.DrawString(state, Theme.F8, b, pill, mid);
            }
            mid.Dispose();
        }
    }
}
