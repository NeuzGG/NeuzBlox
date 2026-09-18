using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace NeuzBlox
{
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(11, 13, 18);
        public static readonly Color Surface = Color.FromArgb(18, 21, 29);
        public static readonly Color Surface2 = Color.FromArgb(23, 27, 37);
        public static readonly Color SurfaceHi = Color.FromArgb(30, 35, 48);
        public static readonly Color Border = Color.FromArgb(35, 40, 56);
        public static readonly Color BorderHi = Color.FromArgb(52, 60, 82);
        public static readonly Color Text = Color.FromArgb(230, 233, 242);
        public static readonly Color TextDim = Color.FromArgb(138, 147, 168);
        public static readonly Color TextFaint = Color.FromArgb(95, 103, 122);
        public static readonly Color Accent = Color.FromArgb(108, 140, 255);
        public static readonly Color Accent2 = Color.FromArgb(155, 108, 255);
        public static readonly Color Green = Color.FromArgb(53, 208, 127);
        public static readonly Color Amber = Color.FromArgb(255, 182, 72);
        public static readonly Color Red = Color.FromArgb(255, 92, 108);

        public static readonly Font F9 = new Font("Segoe UI", 9f);
        public static readonly Font F9B = new Font("Segoe UI Semibold", 9f);
        public static readonly Font F10 = new Font("Segoe UI", 10f);
        public static readonly Font F10B = new Font("Segoe UI Semibold", 10f);
        public static readonly Font F12B = new Font("Segoe UI Semibold", 12f);
        public static readonly Font F16B = new Font("Segoe UI Semibold", 16f);
        public static readonly Font F8 = new Font("Segoe UI", 8f);
        public static readonly Font Icon = new Font("Segoe MDL2 Assets", 11f);
        public static readonly Font IconBig = new Font("Segoe MDL2 Assets", 15f);

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            using (var p = Round(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color c, float w)
        {
            var rr = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (var p = Round(rr, radius))
            using (var pen = new Pen(c, w))
                g.DrawPath(pen, p);
        }

        public static void GradientRound(Graphics g, Rectangle r, int radius, Color a, Color b)
        {
            using (var p = Round(r, radius))
            using (var br = new LinearGradientBrush(new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)), a, b, LinearGradientMode.Horizontal))
                g.FillPath(br, p);
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>The NeuzBlox mark - same shape as the app icon, drawn at any size.</summary>
        public static void DrawMark(Graphics g, Rectangle box)
        {
            Smooth(g);
            int radius = (int)(box.Width * 0.235f);
            using (var path = Round(box, radius))
            {
                using (var br = new LinearGradientBrush(
                    new Rectangle(box.X, box.Y, Math.Max(1, box.Width), Math.Max(1, box.Height)),
                    Accent, Accent2, 35f))
                    g.FillPath(br, path);

                var half = new Rectangle(box.X, box.Y, box.Width, Math.Max(1, (int)(box.Height * 0.55f)));
                using (var gloss = new LinearGradientBrush(half,
                    Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                {
                    Region old = g.Clip;
                    g.SetClip(path, CombineMode.Replace);
                    g.FillRectangle(gloss, half);
                    g.Clip = old;
                }

                using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1f, box.Width * 0.012f)))
                    g.DrawPath(pen, path);
            }

            using (var f = new Font("Segoe UI", box.Width * 0.56f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var fmt = new StringFormat();
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                var text = new RectangleF(box.X, box.Y - box.Height * 0.012f, box.Width, box.Height);
                using (var shadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
                    g.DrawString("N", f, shadow, new RectangleF(text.X, text.Y + box.Height * 0.018f, text.Width, text.Height), fmt);
                using (var white = new SolidBrush(Color.White))
                    g.DrawString("N", f, white, text, fmt);
                fmt.Dispose();
            }
        }

        /// <summary>Stable per-account accent so avatars and chips stay recognisable.</summary>
        public static Color Hue(string seed)
        {
            int h = 17;
            if (!string.IsNullOrEmpty(seed))
                foreach (char c in seed) h = unchecked(h * 31 + c);
            Color[] palette = new Color[]
            {
                Color.FromArgb(108,140,255), Color.FromArgb(155,108,255), Color.FromArgb(53,208,127),
                Color.FromArgb(255,182,72), Color.FromArgb(255,122,162), Color.FromArgb(80,200,220),
                Color.FromArgb(230,120,90), Color.FromArgb(140,200,90)
            };
            return palette[Math.Abs(h) % palette.Length];
        }
    }

    public enum BtnKind { Accent, Ghost, Quiet, Danger, Success }

    /// <summary>Flat, hover-aware button with an optional Segoe MDL2 glyph.</summary>
    public class NButton : Control
    {
        BtnKind _kind = BtnKind.Ghost;
        string _glyph = "";
        int _radius = 8;
        readonly Anim _hoverA;
        readonly Anim _pressA;

        public NButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.F9B;
            Cursor = Cursors.Hand;
            Height = 32;
            BackColor = Color.Transparent;
            _hoverA = new Anim(this, 0f);
            _pressA = new Anim(this, 0f);
        }

        public BtnKind Kind { get { return _kind; } set { _kind = value; Invalidate(); } }
        public string Glyph { get { return _glyph; } set { _glyph = value ?? ""; Invalidate(); } }
        public int Radius { get { return _radius; } set { _radius = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { _hoverA.To(1f, 150); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hoverA.To(0f, 220); _pressA.To(0f, 160); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressA.To(1f, 70); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressA.To(0f, 240, Ease.OutBack); base.OnMouseUp(e); }

        protected override void OnEnabledChanged(EventArgs e)
        {
            if (!Enabled) { _hoverA.Set(0f); _pressA.Set(0f); }
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);

            float h = _hoverA.Value, p = _pressA.Value;
            int inset = (int)Math.Round(p * 1.5f);           // presses in slightly
            var r = new Rectangle(inset, inset, Width - inset * 2, Height - inset * 2);

            Color fill, fore, border = Color.Empty;
            switch (_kind)
            {
                case BtnKind.Accent:
                    fill = Theme.Accent; fore = Color.White;
                    break;
                case BtnKind.Success:
                    fill = Theme.Green; fore = Color.FromArgb(6, 20, 14);
                    break;
                case BtnKind.Danger:
                    fill = Color.FromArgb(40, 22, 28); fore = Theme.Red; border = Color.FromArgb(78, 42, 52);
                    break;
                case BtnKind.Quiet:
                    fill = Color.Transparent; fore = Theme.TextDim;
                    break;
                default:
                    fill = Theme.Surface2; fore = Theme.Text; border = Theme.Border;
                    break;
            }

            if (!Enabled)
            {
                fore = Theme.TextFaint;
                if (fill != Color.Transparent) fill = Theme.Surface2;
                if (border != Color.Empty) border = Theme.Border;
                if (fill != Color.Transparent) Theme.FillRound(g, r, _radius, fill);
                if (border != Color.Empty) Theme.DrawRound(g, r, _radius, border, 1f);
            }
            else if (_kind == BtnKind.Accent)
            {
                // soft glow that grows on hover
                if (h > 0.01f)
                {
                    var glow = new Rectangle(r.X - 3, r.Y - 2, r.Width + 6, r.Height + 6);
                    Theme.FillRound(g, glow, _radius + 3, Color.FromArgb((int)(46 * h), Theme.Accent2));
                }
                Color a = Theme.Mix(Theme.Mix(Theme.Accent, Color.White, 0.12 * h), Color.Black, 0.20 * p);
                Color b2 = Theme.Mix(Theme.Mix(Theme.Accent2, Color.White, 0.12 * h), Color.Black, 0.20 * p);
                Theme.GradientRound(g, r, _radius, a, b2);
            }
            else
            {
                if (_kind == BtnKind.Quiet)
                {
                    int qa = (int)(255 * h * 0.9f);
                    if (qa > 0) Theme.FillRound(g, r, _radius, Color.FromArgb(qa, Theme.Surface2));
                    fore = Theme.Mix(Theme.TextDim, Theme.Text, h);
                }
                else
                {
                    Color shade = Theme.Mix(Theme.Mix(fill, Color.White, 0.10 * h), Color.Black, 0.18 * p);
                    Theme.FillRound(g, r, _radius, shade);
                }
                if (border != Color.Empty)
                    Theme.DrawRound(g, r, _radius,
                        _kind == BtnKind.Danger
                            ? Theme.Mix(border, Theme.Red, 0.45 * h)
                            : Theme.Mix(border, Theme.BorderHi, h),
                        1f);
            }

            string txt = Text ?? "";
            bool hasGlyph = _glyph.Length > 0;
            var sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            sf.FormatFlags = StringFormatFlags.NoWrap;
            sf.Trimming = StringTrimming.EllipsisCharacter;

            using (var fb = new SolidBrush(fore))
            {
                if (hasGlyph && txt.Length > 0)
                {
                    SizeF gs = g.MeasureString(_glyph, Theme.Icon);
                    SizeF ts = g.MeasureString(txt, Font);
                    float total = gs.Width + 6 + ts.Width;
                    float x = (Width - total) / 2f;
                    g.DrawString(_glyph, Theme.Icon, fb, x, (Height - gs.Height) / 2f + 0.5f);
                    g.DrawString(txt, Font, fb, x + gs.Width + 6, (Height - ts.Height) / 2f);
                }
                else if (hasGlyph)
                {
                    g.DrawString(_glyph, Theme.Icon, fb, r, sf);
                }
                else
                {
                    g.DrawString(txt, Font, fb, r, sf);
                }
            }
            sf.Dispose();
        }
    }

    /// <summary>Rounded surface panel used as a card container.</summary>
    public class NCard : Panel
    {
        int _radius = 12;
        Color _fill;
        Color _stroke;

        public NCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            _fill = Theme.Surface;
            _stroke = Theme.Border;
            BackColor = Theme.Bg;
        }

        public int Radius { get { return _radius; } set { _radius = value; Invalidate(); } }
        public Color Fill { get { return _fill; } set { _fill = value; Invalidate(); } }
        public Color Stroke { get { return _stroke; } set { _stroke = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            e.Graphics.Clear(BackColor);
            var r = new Rectangle(0, 0, Width, Height);
            Theme.FillRound(e.Graphics, r, _radius, _fill);
            if (_stroke != Color.Empty) Theme.DrawRound(e.Graphics, r, _radius, _stroke, 1f);
            base.OnPaint(e);
        }
    }

    /// <summary>Dark text field with a rounded frame, placeholder and focus ring.</summary>
    public class NTextBox : Panel
    {
        readonly TextBox _tb = new TextBox();
        string _placeholder = "";
        bool _focus;

        public NTextBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Bg;
            Height = 34;
            _tb.BorderStyle = BorderStyle.None;
            _tb.BackColor = Theme.Surface2;
            _tb.ForeColor = Theme.Text;
            _tb.Font = Theme.F9;
            _tb.GotFocus += delegate { _focus = true; Invalidate(); };
            _tb.LostFocus += delegate { _focus = false; Invalidate(); };
            _tb.TextChanged += delegate { OnTextChanged(EventArgs.Empty); Invalidate(); };
            _tb.KeyDown += delegate(object s, KeyEventArgs e) { OnKeyDown(e); };
            _tb.HandleCreated += delegate { ApplyCue(); };
            Controls.Add(_tb);
        }

        public TextBox Inner { get { return _tb; } }

        public string Placeholder
        {
            get { return _placeholder; }
            set { _placeholder = value ?? ""; ApplyCue(); Invalidate(); }
        }

        /// <summary>The native edit control paints over us, so the hint has to be its own cue banner.</summary>
        void ApplyCue()
        {
            try
            {
                if (_tb.IsHandleCreated && !_tb.Multiline)
                    Native.SendMessageStr(_tb.Handle, Native.EM_SETCUEBANNER, (IntPtr)1, _placeholder);
            }
            catch { }
        }

        public bool Multiline
        {
            get { return _tb.Multiline; }
            set { _tb.Multiline = value; if (value) _tb.ScrollBars = ScrollBars.Vertical; Layout2(); }
        }

        public bool UseSystemPasswordChar
        {
            get { return _tb.UseSystemPasswordChar; }
            set { _tb.UseSystemPasswordChar = value; }
        }

        public override string Text
        {
            get { return _tb.Text; }
            set { _tb.Text = value; Invalidate(); }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Layout2(); }

        void Layout2()
        {
            int pad = 10;
            if (_tb.Multiline)
                _tb.SetBounds(pad, 8, Math.Max(10, Width - pad * 2), Math.Max(10, Height - 16));
            else
                _tb.SetBounds(pad, (Height - _tb.PreferredHeight) / 2, Math.Max(10, Width - pad * 2), _tb.PreferredHeight);
        }

        public new void Focus() { _tb.Focus(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            e.Graphics.Clear(BackColor);
            var r = new Rectangle(0, 0, Width, Height);
            Theme.FillRound(e.Graphics, r, 8, Theme.Surface2);
            Theme.DrawRound(e.Graphics, r, 8, _focus ? Theme.Accent : Theme.Border, _focus ? 1.4f : 1f);
            if (_tb.Multiline && _tb.Text.Length == 0 && _placeholder.Length > 0 && !_focus)
            {
                using (var b = new SolidBrush(Theme.TextFaint))
                    e.Graphics.DrawString(_placeholder, Theme.F9, b, 10, _tb.Multiline ? 8 : (Height - 17) / 2f);
            }
        }
    }

    /// <summary>Checkbox drawn to match the rest of the shell.</summary>
    public class NCheck : Control
    {
        bool _checked;
        Anim _checkA, _hoverA;

        public NCheck()
        {
            _checkA = new Anim(this, 0f);
            _hoverA = new Anim(this, 0f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.F9;
            Height = 24;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                _checkA.To(value ? 1f : 0f, value ? 260 : 150, value ? Ease.OutBack : Ease.OutCubic);
                Invalidate();
                var h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hoverA.To(1f, 140); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hoverA.To(0f, 200); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            var box = new Rectangle(0, (Height - 18) / 2, 18, 18);
            float c = _checkA.Value, hv = _hoverA.Value;

            Theme.FillRound(g, box, 5, Theme.Surface2);
            Theme.DrawRound(g, box, 5, Theme.Mix(Theme.Border, Theme.BorderHi, hv), 1f);

            if (c > 0.01f)
            {
                // the tick box pops in from the middle
                int pad = (int)Math.Round((1f - Math.Min(1f, c)) * 7f);
                var lit = new Rectangle(box.X + pad, box.Y + pad, box.Width - pad * 2, box.Height - pad * 2);
                if (lit.Width > 0 && lit.Height > 0)
                {
                    Theme.GradientRound(g, lit, Math.Max(2, 5 - pad), Theme.Accent, Theme.Accent2);
                    if (c > 0.45f)
                        using (var b = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1f, (c - 0.45f) / 0.55f)), Color.White)))
                        using (var f = new Font("Segoe MDL2 Assets", 9f))
                            g.DrawString("", f, b, box.X + 2, box.Y + 2);
                }
            }

            using (var b = new SolidBrush(Enabled ? Theme.Text : Theme.TextFaint))
            {
                var sf = new StringFormat();
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(Text, Font, b, new RectangleF(26, 0, Width - 26, Height), sf);
                sf.Dispose();
            }
        }
    }

    /// <summary>Small coloured status pill.</summary>
    public class NPill : Control
    {
        Color _tint;

        public NPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.F8;
            Height = 22;
            _tint = Theme.TextDim;
            BackColor = Color.Transparent;
        }

        public Color Tint { get { return _tint; } set { _tint = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            var r = new Rectangle(0, 0, Width, Height);
            Theme.FillRound(g, r, Height / 2, Color.FromArgb(38, _tint));
            Theme.DrawRound(g, r, Height / 2, Color.FromArgb(90, _tint), 1f);
            var sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            using (var b = new SolidBrush(_tint))
                g.DrawString(Text, Font, b, r, sf);
            sf.Dispose();
        }
    }

    /// <summary>Scrollable, double-buffered host for card rows.</summary>
    public class NScroll : Panel
    {
        public NScroll()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            AutoScroll = true;
            BackColor = Theme.Bg;
        }

        protected override Point ScrollToControl(Control activeControl)
        {
            return DisplayRectangle.Location;
        }
    }
}
