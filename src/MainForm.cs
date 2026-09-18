using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NeuzBlox
{
    /// <summary>Left-rail navigation entry.</summary>
    public class NavItem : Control
    {
        bool _active;
        string _glyph = "";
        readonly Anim _activeA;
        readonly Anim _hoverA;

        public NavItem(string text, string glyph)
        {
            _activeA = new Anim(this, 0f);
            _hoverA = new Anim(this, 0f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Text = text;
            _glyph = glyph;
            Font = Theme.F9B;
            Height = 40;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        public bool Active
        {
            get { return _active; }
            set
            {
                if (_active == value) return;
                _active = value;
                _activeA.To(value ? 1f : 0f, value ? 280 : 180, value ? Ease.OutBack : Ease.OutCubic);
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hoverA.To(1f, 150); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hoverA.To(0f, 220); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Theme.Surface);

            float a = _activeA.Value, hv = _hoverA.Value;
            var r = new Rectangle(0, 2, Width, Height - 4);

            if (hv > 0.01f && a < 0.99f)
                Theme.FillRound(g, r, 9, Theme.Mix(Theme.Surface, Theme.Surface2, hv * (1f - a)));

            if (a > 0.01f)
            {
                Theme.FillRound(g, r, 9, Theme.Mix(Theme.Surface, Color.FromArgb(30, 37, 56), Math.Min(1f, a)));

                // the accent rail grows out from the middle
                float grow = Math.Min(1f, a);
                int full = r.Height - 16;
                int barH = (int)(full * grow);
                if (barH > 2)
                {
                    var bar = new Rectangle(0, r.Y + 8 + (full - barH) / 2, 3, barH);
                    Theme.FillRound(g, bar, 2, Theme.Accent);
                }
            }

            Color fg = Theme.Mix(Theme.TextDim, Theme.Text, Math.Max(hv, Math.Min(1f, a)));
            using (var b = new SolidBrush(Theme.Mix(fg, Theme.Accent, Math.Min(1f, a))))
                g.DrawString(_glyph, Theme.Icon, b, 16 + 2f * Math.Min(1f, a), r.Y + 10);
            using (var b = new SolidBrush(fg))
                g.DrawString(Text, Font, b, 44 + 2f * Math.Min(1f, a), r.Y + 10);
        }
    }

    public class MainForm : Form
    {
        Settings _cfg;
        List<Account> _accounts;
        readonly InstanceManager _mgr = new InstanceManager();
        readonly Dictionary<long, Image> _avatars = new Dictionary<long, Image>();
        readonly List<AccountCard> _accCards = new List<AccountCard>();
        readonly List<InstanceCard> _instCards = new List<InstanceCard>();

        NotifyIcon _tray;
        System.Windows.Forms.Timer _tick;
        Panel _rail, _host, _statusBar;
        Panel _pgAccounts, _pgInstances, _pgSettings, _pgCredits;
        NavItem _navAcc, _navInst, _navSet, _navCredits;
        NScroll _accList, _instList;
        NTextBox _target, _search;
        ComboBox _mode;
        Label _statusLbl, _rightLbl, _miState, _instHint;
        NButton _miToggle, _launchSel, _launchAll;
        string _playerPath;
        bool _reallyExit;
        DateTime? _updateSince;
        DiscordRpc _rpc;
        System.Windows.Forms.Timer _rpcTick;
        string _rpcLast = "";

        public MainForm() : this(null) { }

        public MainForm(Boot boot)
        {
            if (boot != null)
            {
                _cfg = boot.Cfg;
                _accounts = boot.Accounts;
                _playerPath = boot.PlayerPath;
            }
            else
            {
                _cfg = Settings.Load();
                _accounts = AccountStore.Load();
                _playerPath = RobloxClient.FindPlayer(_cfg.PlayerPath, false);
            }
            Anim.Enabled = _cfg.Animations;

            Text = "NeuzBlox";
            ClientSize = new Size(1100, 720);
            MinimumSize = new Size(960, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            Font = Theme.F9;
            AutoScaleMode = AutoScaleMode.Font;
            DoubleBuffered = true;
            Icon = AppIcon.Get();

            BuildRail();
            BuildStatusBar();
            BuildHost();
            BuildTray();

            _mgr.Config = _cfg;
            _mgr.PlayerPathProvider = delegate
            {
                // Roblox moves itself to a new version folder when it updates, so never
                // trust the path we resolved at startup.
                string fresh = RobloxClient.FindPlayer(_cfg.PlayerPath);
                if (!string.IsNullOrEmpty(fresh)) _playerPath = fresh;
                return _playerPath;
            };
            _mgr.AccountUpdated = delegate(Account a) { SafeInvoke(delegate { AccountStore.Save(_accounts); }); };
            _mgr.Changed = delegate { SafeInvoke(delegate { RefreshInstances(); UpdateCounters(); PushPresence(false); }); };
            _mgr.Status = delegate(string msg, bool bad) { SafeInvoke(delegate { SetStatus(msg, bad); }); };

            MultiInstance.StateChanged += delegate { SafeInvoke(UpdateMultiInstanceUi); };

            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = 800;
            _tick.Tick += delegate
            {
                _mgr.Poll();
                foreach (InstanceCard c in _instCards) c.Sync();
                UpdateCounters();
                WatchRobloxUpdate();
            };
            _tick.Start();

            Show(Page.Accounts);
            RebuildAccounts();
            UpdateMultiInstanceUi();
            UpdateCounters();

            Load += delegate
            {
                Native.UseDarkTitleBar(Handle);
                FadeIn();
                GameWatcher.Start();
                StartDiscord();
                if (_cfg.UnlockOnStart) MultiInstance.Enable();
                UpdateMultiInstanceUi();
                if (!_cfg.RiskAcknowledged) ShowFirstRun();
                if (string.IsNullOrEmpty(_playerPath))
                    SetStatus("Roblox client not found - set the path in Settings.", true);
                else
                    SetStatus("Ready. " + _accounts.Count + " account" + (_accounts.Count == 1 ? "" : "s") + " loaded.", false);

                ResolveCurrentClient();
            };

            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized && _cfg.MinimizeToTray)
                {
                    Hide();
                    if (_tray != null) _tray.Visible = true;
                }
            };

            FormClosing += OnClosing;
        }

        // ---------------------------------------------------------------- chrome

        enum Page { Accounts, Instances, Settings, Credits }

        void FadeIn()
        {
            if (!Anim.Enabled) { Opacity = 1; return; }
            Opacity = 0;
            var t = new System.Windows.Forms.Timer();
            t.Interval = 16;
            double v = 0;
            t.Tick += delegate
            {
                v += 0.12;
                if (v >= 1) { v = 1; t.Stop(); t.Dispose(); }
                try { Opacity = v; }
                catch { }
            };
            t.Start();
        }

        // ---------------------------------------------------------------- discord

        void StartDiscord()
        {
            if (_rpcTick == null)
            {
                _rpcTick = new System.Windows.Forms.Timer();
                _rpcTick.Interval = 15000;   // Discord throttles presence to one update / 15s
                _rpcTick.Tick += delegate { PushPresence(false); };
            }

            if (!_cfg.DiscordEnabled)
            {
                StopDiscord();
                return;
            }

            string appId = _cfg.EffectiveDiscordAppId;
            if (string.IsNullOrEmpty(appId))
            {
                StopDiscord();
                return;
            }
            var th = new Thread(delegate()
            {
                var rpc = new DiscordRpc();
                bool ok = rpc.Connect(appId);
                SafeInvoke(delegate
                {
                    if (_rpc != null) { try { _rpc.Dispose(); } catch { } }
                    _rpc = rpc;
                    if (ok)
                    {
                        _rpcLast = "";
                        PushPresence(true);
                        _rpcTick.Start();
                    }
                    else
                    {
                        Log.Write("Discord rich presence unavailable: " + rpc.LastError);
                    }
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        void StopDiscord()
        {
            if (_rpcTick != null) _rpcTick.Stop();
            if (_rpc != null)
            {
                try { _rpc.Dispose(); }
                catch { }
                _rpc = null;
            }
        }

        const string AssetBig = "logo_big";
        const string AssetSmall = "logo_small";

        void PushPresence(bool force)
        {
            if (_rpc == null || !_rpc.Connected) return;

            var live = new List<RbxInstance>();
            foreach (RbxInstance i in _mgr.Snapshot())
                if (i.IsLive) live.Add(i);
            int n = live.Count;

            // One game for everyone -> show that game. Mixed -> stay generic.
            // Prefer where the client actually is (read from its log) over where it was
            // aimed at launch - most sessions start on the home app and pick a game later.
            long placeId = 0;
            string gameName = null;
            bool allSame = n > 0;
            bool first = true;
            foreach (RbxInstance i in live)
            {
                long p = i.LivePlaceId > 0 ? i.LivePlaceId : (i.Target != null ? i.Target.PlaceId : 0);
                string label = i.LivePlaceId > 0 ? i.LiveGameName : i.TargetLabel;
                if (first) { placeId = p; gameName = label; first = false; }
                else if (p != placeId) { allSame = false; break; }
            }
            if (!allSame) { placeId = 0; gameName = null; }

            string details;
            if (n == 0) details = "Idle in the launcher";
            else if (placeId > 0 && !string.IsNullOrEmpty(gameName)) details = gameName;
            else if (allSame) details = "Browsing the Roblox app";
            else details = "Playing across " + n + " games";

            string state;
            if (n > 0 && _cfg.DiscordShowAccounts)
            {
                var names = new List<string>();
                foreach (RbxInstance i in live)
                    if (!names.Contains(i.Account.Label)) names.Add(i.Account.Label);
                state = string.Join(", ", names.ToArray());
            }
            else if (n > 0)
            {
                state = n + (n == 1 ? " client running" : " clients running");
            }
            else
            {
                state = _accounts.Count + (_accounts.Count == 1 ? " account saved" : " accounts saved");
            }

            // The game's own icon goes in the big slot when we have it, our mark in the
            // small one. Discord proxies a raw https URL itself, so no token is needed.
            string largeImage = AssetBig;
            string largeText = "NeuzBlox " + AppInfo.Version;
            if (placeId > 0)
            {
                string icon = RobloxApi.GetGameIconCached(placeId);
                if (!string.IsNullOrEmpty(icon))
                {
                    largeImage = icon;
                    largeText = gameName;
                }
                else if (!RobloxApi.GameIconKnown(placeId))
                {
                    FetchGameIcon(placeId);   // lands later, then we re-push
                }
            }

            string key = details + "|" + state + "|" + largeImage;
            if (!force && key == _rpcLast) return;
            _rpcLast = key;

            _rpc.SetActivity(details, state, n, n > 0 ? Math.Max(n, _accounts.Count) : 0, true,
                             largeImage, largeText, AssetSmall, "NeuzBlox");
        }

        void FetchGameIcon(long placeId)
        {
            var th = new Thread(delegate()
            {
                RobloxApi.GetGameIcon(placeId);
                SafeInvoke(delegate { PushPresence(true); });
            });
            th.IsBackground = true;
            th.Start();
        }

        void BuildRail()
        {
            _rail = new Panel();
            _rail.Dock = DockStyle.Left;
            _rail.Width = 216;
            _rail.BackColor = Theme.Surface;
            Controls.Add(_rail);

            var logo = new Label();
            logo.Text = "NeuzBlox";
            logo.Font = Theme.F16B;
            logo.ForeColor = Theme.Text;
            logo.BackColor = Color.Transparent;
            logo.AutoSize = true;
            logo.Location = new Point(18, 22);
            _rail.Controls.Add(logo);

            var tag = new Label();
            tag.Text = "multi-instance launcher";
            tag.Font = Theme.F8;
            tag.ForeColor = Theme.TextFaint;
            tag.BackColor = Color.Transparent;
            tag.AutoSize = true;
            tag.Location = new Point(20, 50);
            _rail.Controls.Add(tag);

            _navAcc = MakeNav("Accounts", "", 88, Page.Accounts);
            _navInst = MakeNav("Instances", "", 132, Page.Instances);
            _navSet = MakeNav("Settings", "", 176, Page.Settings);
            _navCredits = MakeNav("Credits", "", 220, Page.Credits);

            var card = new NCard();
            card.Fill = Theme.Surface2;
            card.Stroke = Theme.Border;
            card.BackColor = Theme.Surface;
            card.SetBounds(14, 0, 188, 118);
            card.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            _rail.Controls.Add(card);
            _rail.Resize += delegate { card.Top = _rail.Height - 134; };
            card.Top = _rail.Height - 134;

            var t = new Label();
            t.Text = "MULTI-INSTANCE";
            t.Font = Theme.F8;
            t.ForeColor = Theme.TextFaint;
            t.BackColor = Color.Transparent;
            t.AutoSize = true;
            t.Location = new Point(14, 12);
            card.Controls.Add(t);

            _miState = new Label();
            _miState.Font = Theme.F10B;
            _miState.ForeColor = Theme.Green;
            _miState.BackColor = Color.Transparent;
            _miState.AutoSize = true;
            _miState.Location = new Point(14, 30);
            card.Controls.Add(_miState);

            _miToggle = new NButton();
            _miToggle.Kind = BtnKind.Ghost;
            _miToggle.SetBounds(14, 60, 160, 34);
            _miToggle.Click += delegate
            {
                if (MultiInstance.Active)
                {
                    MultiInstance.Disable();
                    SetStatus("Multi-instance locked. New clients will hand off to the first one.", false);
                }
                else
                {
                    MultiInstance.Enable();
                    SetStatus("Multi-instance unlocked. Launch as many accounts as you like.", false);
                }
                UpdateMultiInstanceUi();
            };
            card.Controls.Add(_miToggle);
        }

        NavItem MakeNav(string text, string glyph, int y, Page page)
        {
            var n = new NavItem(text, glyph);
            n.SetBounds(12, y, 192, 40);
            n.Click += delegate { Show(page); };
            _rail.Controls.Add(n);
            return n;
        }

        void BuildStatusBar()
        {
            _statusBar = new Panel();
            _statusBar.Dock = DockStyle.Bottom;
            _statusBar.Height = 34;
            _statusBar.BackColor = Theme.Surface;
            Controls.Add(_statusBar);

            _statusLbl = new Label();
            _statusLbl.Font = Theme.F8;
            _statusLbl.ForeColor = Theme.TextDim;
            _statusLbl.BackColor = Color.Transparent;
            _statusLbl.AutoEllipsis = true;
            _statusLbl.SetBounds(232, 9, 620, 18);
            _statusBar.Controls.Add(_statusLbl);

            _rightLbl = new Label();
            _rightLbl.Font = Theme.F8;
            _rightLbl.ForeColor = Theme.TextFaint;
            _rightLbl.BackColor = Color.Transparent;
            _rightLbl.TextAlign = ContentAlignment.MiddleRight;
            _rightLbl.SetBounds(_statusBar.Width - 596, 9, 580, 18);
            _statusBar.Controls.Add(_rightLbl);

            EventHandler layoutBar = delegate
            {
                int w = _statusBar.ClientSize.Width;
                int rw = Math.Min(560, Math.Max(200, w - 420));
                _rightLbl.SetBounds(w - 16 - rw, 9, rw, 18);
                _statusLbl.SetBounds(232, 9, Math.Max(100, _rightLbl.Left - 248), 18);
            };
            _statusBar.Resize += layoutBar;
            layoutBar(null, EventArgs.Empty);
        }

        void BuildHost()
        {
            _host = new Panel();
            _host.Dock = DockStyle.Fill;
            _host.BackColor = Theme.Bg;
            Controls.Add(_host);
            _host.BringToFront();

            _pgAccounts = BuildAccountsPage();
            _pgInstances = BuildInstancesPage();
            _pgSettings = BuildSettingsPage();
            _pgCredits = BuildCreditsPage();

            _host.Controls.Add(_pgAccounts);
            _host.Controls.Add(_pgInstances);
            _host.Controls.Add(_pgSettings);
            _host.Controls.Add(_pgCredits);
        }

        static Panel NewPage()
        {
            var p = new Panel();
            p.Dock = DockStyle.Fill;
            p.BackColor = Theme.Bg;
            p.Visible = false;
            p.Padding = new Padding(26, 20, 26, 12);
            return p;
        }

        static Label Head(Panel p, string title, string sub)
        {
            var t = new Label();
            t.Text = title;
            t.Font = Theme.F16B;
            t.ForeColor = Theme.Text;
            t.BackColor = Color.Transparent;
            t.AutoSize = true;
            t.Location = new Point(26, 20);
            p.Controls.Add(t);

            var s = new Label();
            s.Text = sub;
            s.Font = Theme.F9;
            s.ForeColor = Theme.TextDim;
            s.BackColor = Color.Transparent;
            s.AutoSize = true;
            s.Location = new Point(28, 50);
            p.Controls.Add(s);
            return s;
        }

        void Show(Page p)
        {
            _pgAccounts.Visible = p == Page.Accounts;
            _pgInstances.Visible = p == Page.Instances;
            _pgSettings.Visible = p == Page.Settings;
            _pgCredits.Visible = p == Page.Credits;
            _navAcc.Active = p == Page.Accounts;
            _navInst.Active = p == Page.Instances;
            _navSet.Active = p == Page.Settings;
            _navCredits.Active = p == Page.Credits;
            if (p == Page.Instances) RefreshInstances();
        }

        // ---------------------------------------------------------------- accounts page

        Panel BuildAccountsPage()
        {
            Panel p = NewPage();
            Head(p, "Accounts", "Each account launches its own client. They run side by side.");

            var add = new NButton();
            add.Text = "Add account";
            add.Glyph = "";
            add.Kind = BtnKind.Accent;
            add.SetBounds(0, 28, 136, 34);
            add.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            add.Click += delegate { AddAccount(); };
            p.Controls.Add(add);

            var verify = new NButton();
            verify.Text = "Verify all";
            verify.Glyph = "";
            verify.Kind = BtnKind.Ghost;
            verify.SetBounds(0, 28, 110, 34);
            verify.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            verify.Click += delegate { VerifyAll(); };
            p.Controls.Add(verify);

            _search = new NTextBox();
            _search.Placeholder = "Search accounts";
            _search.SetBounds(0, 28, 180, 34);
            _search.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _search.TextChanged += delegate { RebuildAccounts(); };
            p.Controls.Add(_search);

            // launch bar
            var bar = new NCard();
            bar.SetBounds(26, 88, 100, 112);
            bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            p.Controls.Add(bar);

            var lblWhere = new Label();
            lblWhere.Text = "WHERE TO JOIN";
            lblWhere.Font = Theme.F8;
            lblWhere.ForeColor = Theme.TextFaint;
            lblWhere.BackColor = Color.Transparent;
            lblWhere.AutoSize = true;
            lblWhere.Location = new Point(16, 12);
            bar.Controls.Add(lblWhere);

            _mode = MakeCombo(new string[]
            {
                "Roblox app (home)", "Game / place", "Private server link", "Specific server (job ID)", "Follow a user"
            }, 186);
            _mode.Location = new Point(16, 32);
            _mode.SelectedIndexChanged += delegate { ApplyModeHints(); _cfg.LastMode = _mode.SelectedIndex.ToString(CultureInfo.InvariantCulture); };
            bar.Controls.Add(_mode);

            _target = new NTextBox();
            _target.SetBounds(212, 30, 300, 34);
            _target.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _target.Text = _cfg.LastInput;
            _target.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                List<Account> pick = Selected();
                LaunchMany(pick.Count > 0 ? pick : new List<Account>(_accounts));
            };
            bar.Controls.Add(_target);

            var presets = new NButton();
            presets.Glyph = "";
            presets.Kind = BtnKind.Ghost;
            presets.SetBounds(0, 30, 34, 34);
            presets.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            presets.Click += delegate { ShowPresets(presets); };
            bar.Controls.Add(presets);

            var savePreset = new NButton();
            savePreset.Glyph = "";
            savePreset.Kind = BtnKind.Ghost;
            savePreset.SetBounds(0, 30, 34, 34);
            savePreset.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            savePreset.Click += delegate { SavePreset(); };
            bar.Controls.Add(savePreset);

            _launchSel = new NButton();
            _launchSel.Text = "Launch selected";
            _launchSel.Glyph = "";
            _launchSel.Kind = BtnKind.Accent;
            _launchSel.SetBounds(16, 70, 160, 34);
            _launchSel.Click += delegate { LaunchMany(Selected()); };
            bar.Controls.Add(_launchSel);

            _launchAll = new NButton();
            _launchAll.Text = "Launch all";
            _launchAll.Glyph = "";
            _launchAll.Kind = BtnKind.Ghost;
            _launchAll.SetBounds(184, 70, 120, 34);
            _launchAll.Click += delegate { LaunchMany(new List<Account>(_accounts)); };
            bar.Controls.Add(_launchAll);

            _instHint = new Label();
            _instHint.Font = Theme.F8;
            _instHint.ForeColor = Theme.TextFaint;
            _instHint.BackColor = Color.Transparent;
            _instHint.AutoSize = true;
            _instHint.Location = new Point(316, 80);
            bar.Controls.Add(_instHint);

            _accList = new NScroll();
            _accList.SetBounds(26, 214, 100, 100);
            _accList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _accList.Resize += delegate { ReflowAccounts(); };
            p.Controls.Add(_accList);

            var tip = new ToolTip();
            tip.SetToolTip(presets, "Saved destinations");
            tip.SetToolTip(savePreset, "Save this destination as a preset");

            p.Resize += delegate
            {
                int w = p.ClientSize.Width;
                _search.Left = w - 26 - _search.Width;
                verify.Left = _search.Left - 8 - verify.Width;
                add.Left = verify.Left - 8 - add.Width;
                bar.Width = w - 52;
                savePreset.Left = bar.ClientSize.Width - 16 - savePreset.Width;
                presets.Left = savePreset.Left - 6 - presets.Width;
                _target.Width = presets.Left - 10 - _target.Left;
                _accList.Width = w - 52;
                _accList.Height = Math.Max(80, p.ClientSize.Height - 214 - 12);
                ReflowAccounts();
            };

            ApplyModeHints();
            return p;
        }

        ComboBox MakeCombo(string[] items, int w)
        {
            var cb = new ComboBox();
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.FlatStyle = FlatStyle.Flat;
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            cb.BackColor = Theme.Surface2;
            cb.ForeColor = Theme.Text;
            cb.Font = Theme.F9;
            cb.ItemHeight = 24;
            cb.Width = w;
            cb.Items.AddRange(items);
            cb.SelectedIndex = 0;
            cb.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var b = new SolidBrush(sel ? Theme.SurfaceHi : Theme.Surface2))
                    e.Graphics.FillRectangle(b, e.Bounds);
                if (e.Index >= 0)
                    using (var b = new SolidBrush(Theme.Text))
                        e.Graphics.DrawString(cb.Items[e.Index].ToString(), Theme.F9, b, e.Bounds.X + 7, e.Bounds.Y + 4);
            };
            return cb;
        }

        JoinMode CurrentMode()
        {
            switch (_mode.SelectedIndex)
            {
                case 1: return JoinMode.Place;
                case 2: return JoinMode.PrivateServer;
                case 3: return JoinMode.JobId;
                case 4: return JoinMode.FollowUser;
                default: return JoinMode.App;
            }
        }

        void ApplyModeHints()
        {
            switch (CurrentMode())
            {
                case JoinMode.Place:
                    _target.Placeholder = "roblox.com/games/... link or place ID";
                    _target.Enabled = true;
                    break;
                case JoinMode.PrivateServer:
                    _target.Placeholder = "full private server link (with privateServerLinkCode)";
                    _target.Enabled = true;
                    break;
                case JoinMode.JobId:
                    _target.Placeholder = "placeId + &gameId=<job id>";
                    _target.Enabled = true;
                    break;
                case JoinMode.FollowUser:
                    _target.Placeholder = "user ID or roblox.com/users/... link";
                    _target.Enabled = true;
                    break;
                default:
                    _target.Placeholder = "not needed - each client opens on the Roblox home screen";
                    _target.Enabled = false;
                    break;
            }
        }

        List<Account> Selected()
        {
            var list = new List<Account>();
            foreach (AccountCard c in _accCards)
                if (c.Selected) list.Add(c.Model);
            return list;
        }

        void RebuildAccounts()
        {
            foreach (AccountCard c in _accCards) c.Dispose();
            _accCards.Clear();
            _accList.Controls.Clear();

            string q = (_search != null ? _search.Text : "").Trim().ToLowerInvariant();

            foreach (Account a in _accounts)
            {
                if (q.Length > 0)
                {
                    string hay = (a.Alias + " " + a.Username + " " + a.Note + " " + a.UserId).ToLowerInvariant();
                    if (hay.IndexOf(q, StringComparison.Ordinal) < 0) continue;
                }

                var card = new AccountCard(a);
                Account captured = a;
                card.LaunchClicked += delegate { LaunchMany(new List<Account>(new Account[] { captured })); };
                card.EditClicked += delegate { EditAccount(captured); };
                card.RemoveClicked += delegate { RemoveAccount(captured); };
                card.SelectionChanged += delegate { UpdateCounters(); };
                _accCards.Add(card);
                _accList.Controls.Add(card);
                card.PlayEntrance(_accCards.Count - 1);
                LoadAvatar(card);
            }

            if (_accounts.Count == 0)
            {
                var empty = new Label();
                empty.Text = "No accounts yet.\n\nAdd one with the button above. Each account you add can run in its own\nRoblox client at the same time as the others.";
                empty.Font = Theme.F9;
                empty.ForeColor = Theme.TextFaint;
                empty.AutoSize = true;
                empty.Location = new Point(14, 24);
                empty.BackColor = Color.Transparent;
                _accList.Controls.Add(empty);
            }

            ReflowAccounts();
            UpdateCounters();
        }

        void ReflowAccounts()
        {
            int w = Math.Max(300, _accList.ClientSize.Width - 4);
            int h = _accList.LogicalToDeviceUnits(74);
            int gap = _accList.LogicalToDeviceUnits(4);
            int y = 0;
            foreach (AccountCard c in _accCards)
            {
                c.SetBounds(0, y, w, h);
                y += h + gap;
            }
        }

        void LoadAvatar(AccountCard card)
        {
            Account a = card.Model;
            if (a.UserId <= 0) return;

            Image cached;
            if (_avatars.TryGetValue(a.UserId, out cached)) { card.Avatar = cached; card.Invalidate(); return; }

            string file = Path.Combine(Paths.CacheDir, a.UserId.ToString(CultureInfo.InvariantCulture) + ".png");
            var th = new Thread(delegate()
            {
                try
                {
                    byte[] data = null;
                    if (File.Exists(file) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalDays < 14)
                        data = File.ReadAllBytes(file);
                    if (data == null)
                    {
                        string url = RobloxApi.GetAvatarUrl(a.UserId);
                        if (!string.IsNullOrEmpty(url)) data = RobloxApi.Download(url);
                        if (data != null) try { File.WriteAllBytes(file, data); } catch { }
                    }
                    if (data == null) return;
                    Image img;
                    using (var ms = new MemoryStream(data)) img = Image.FromStream(ms);
                    SafeInvoke(delegate
                    {
                        _avatars[a.UserId] = img;
                        card.Avatar = img;
                        card.Invalidate();
                    });
                }
                catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        void AddAccount()
        {
            using (var d = new AccountDialog(null))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Result == null) return;
                foreach (Account ex in _accounts)
                {
                    if (ex.UserId == d.Result.UserId && ex.UserId > 0)
                    {
                        Dlg.Info(this, "Already added", "That account is already in your list as \"" + ex.Label + "\".");
                        return;
                    }
                }
                _accounts.Add(d.Result);
                AccountStore.Save(_accounts);
                RebuildAccounts();
                SetStatus("Added " + d.Result.Label + " (@" + d.Result.Username + ").", false);
            }
        }

        void EditAccount(Account a)
        {
            using (var d = new AccountDialog(a))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                AccountStore.Save(_accounts);
                RebuildAccounts();
                SetStatus("Updated " + a.Label + ".", false);
            }
        }

        void RemoveAccount(Account a)
        {
            if (!Dlg.Confirm(this, "Remove account",
                "Remove \"" + a.Label + "\" from NeuzBlox?\n\nThis only deletes the saved session on this PC.\nThe Roblox account itself is untouched.",
                "Remove", true)) return;
            _accounts.Remove(a);
            AccountStore.Save(_accounts);
            RebuildAccounts();
            SetStatus("Removed " + a.Label + ".", false);
        }

        void VerifyAll()
        {
            if (_accounts.Count == 0) return;
            SetStatus("Checking " + _accounts.Count + " sessions with Roblox...", false);
            var snapshot = new List<Account>(_accounts);
            var th = new Thread(delegate()
            {
                int ok = 0, bad = 0;
                foreach (Account a in snapshot)
                {
                    UserResult r = RobloxApi.WhoAmI(a.Cookie, a.EnsureTracker());
                    if (r.Ok)
                    {
                        ok++;
                        a.Username = r.User.Name;
                        a.DisplayName = r.User.DisplayName;
                        a.UserId = r.User.Id;
                        if (!string.IsNullOrEmpty(r.RefreshedCookie)) a.Cookie = r.RefreshedCookie;
                        if (string.IsNullOrEmpty(a.Alias)) a.Alias = r.User.DisplayName;
                    }
                    else bad++;
                    Thread.Sleep(350);
                }
                SafeInvoke(delegate
                {
                    AccountStore.Save(_accounts);
                    RebuildAccounts();
                    SetStatus(ok + " session" + (ok == 1 ? "" : "s") + " healthy" + (bad > 0 ? ", " + bad + " need a fresh cookie." : "."), bad > 0);
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        // ---------------------------------------------------------------- launching

        void LaunchMany(List<Account> accounts)
        {
            if (accounts == null || accounts.Count == 0)
            {
                SetStatus("Pick at least one account first.", true);
                return;
            }

            if (string.IsNullOrEmpty(_playerPath))
            {
                SetStatus("Roblox client not found - set the path in Settings.", true);
                Show(Page.Settings);
                return;
            }

            string err;
            JoinTarget target = JoinTarget.Parse(_target.Text, CurrentMode(), out err);
            if (target == null)
            {
                SetStatus(err, true);
                return;
            }

            _cfg.LastInput = _target.Text;
            _cfg.Save();

            if (accounts.Count > 1 && !MultiInstance.Active)
            {
                if (!Dlg.Confirm(this, "Multi-instance is off",
                    "Launching " + accounts.Count + " accounts with multi-instance locked will just focus\nthe first client instead of opening more.\n\nUnlock it now and continue?",
                    "Unlock and launch", false)) return;
                MultiInstance.Enable();
                UpdateMultiInstanceUi();
            }

            string label = target.Label;
            if (target.Mode == JoinMode.Place || target.Mode == JoinMode.PrivateServer || target.Mode == JoinMode.JobId)
                ResolvePlaceName(target);

            int delay = Math.Max(0, _cfg.LaunchDelaySeconds);
            var queue = new List<Account>(accounts);

            if (RobloxClient.IsUpdating())
                SetStatus("Roblox is updating itself - clients will start as soon as it finishes.", false);
            else
                SetStatus("Launching " + queue.Count + " client" + (queue.Count == 1 ? "" : "s")
                    + (queue.Count > 1 ? ", " + delay + "s apart" : "") + "...", false);
            Show(Page.Instances);

            var th = new Thread(delegate()
            {
                for (int i = 0; i < queue.Count; i++)
                {
                    if (i > 0 && delay > 0) Thread.Sleep(delay * 1000);
                    Account a = queue[i];
                    SafeInvoke(delegate { _mgr.Launch(a, target, target.Label); });
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        void ResolvePlaceName(JoinTarget t)
        {
            long pid = t.PlaceId;
            if (pid <= 0) return;
            var th = new Thread(delegate()
            {
                string name = RobloxApi.GetPlaceName(pid);
                if (string.IsNullOrEmpty(name)) return;
                SafeInvoke(delegate
                {
                    string prefix = t.Mode == JoinMode.PrivateServer ? "Private: " : "";
                    t.Label = prefix + name;
                    foreach (RbxInstance i in _mgr.Snapshot())
                        if (i.Target == t) i.TargetLabel = t.Label;
                    RefreshInstances();
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        void ShowPresets(Control anchor)
        {
            var menu = new ContextMenuStrip();
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Theme.Surface2;
            menu.ForeColor = Theme.Text;
            menu.ShowImageMargin = false;

            if (_cfg.Presets.Count == 0)
            {
                var none = new ToolStripMenuItem("No presets saved yet");
                none.Enabled = false;
                menu.Items.Add(none);
            }
            else
            {
                foreach (Preset pr in _cfg.Presets)
                {
                    Preset captured = pr;
                    var mi = new ToolStripMenuItem(pr.Name);
                    mi.Click += delegate
                    {
                        _target.Text = captured.Input;
                        int idx;
                        if (int.TryParse(captured.Mode, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx)
                            && idx >= 0 && idx < _mode.Items.Count) _mode.SelectedIndex = idx;
                        ApplyModeHints();
                    };
                    menu.Items.Add(mi);
                }
                menu.Items.Add(new ToolStripSeparator());
                var clear = new ToolStripMenuItem("Clear all presets");
                clear.Click += delegate { _cfg.Presets.Clear(); _cfg.Save(); SetStatus("Presets cleared.", false); };
                menu.Items.Add(clear);
            }
            menu.Show(anchor, new Point(0, anchor.Height + 4));
        }

        void SavePreset()
        {
            if (CurrentMode() != JoinMode.App && _target.Text.Trim().Length == 0)
            {
                SetStatus("Nothing to save - enter a destination first.", true);
                return;
            }
            string name = Dlg.Prompt(this, "Save preset", "Name this destination", "", "Pet Sim main lobby");
            if (string.IsNullOrEmpty(name)) return;
            var p = new Preset();
            p.Name = name;
            p.Input = _target.Text.Trim();
            p.Mode = _mode.SelectedIndex.ToString(CultureInfo.InvariantCulture);
            _cfg.Presets.Add(p);
            _cfg.Save();
            SetStatus("Preset \"" + name + "\" saved.", false);
        }

        // ---------------------------------------------------------------- instances page

        Panel BuildInstancesPage()
        {
            Panel p = NewPage();
            Head(p, "Instances", "Every client NeuzBlox started, and what it is doing right now.");

            var arrange = new NButton();
            arrange.Text = "Arrange windows";
            arrange.Glyph = "";
            arrange.Kind = BtnKind.Ghost;
            arrange.SetBounds(0, 28, 156, 34);
            arrange.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            arrange.Click += delegate
            {
                int n = _mgr.Arrange(_cfg.Layout);
                SetStatus(n == 0 ? "No running client windows to arrange yet." : "Arranged " + n + " window" + (n == 1 ? "" : "s") + " (" + _cfg.Layout + ").", n == 0);
            };
            p.Controls.Add(arrange);

            var closeAll = new NButton();
            closeAll.Text = "Close all";
            closeAll.Glyph = "";
            closeAll.Kind = BtnKind.Danger;
            closeAll.SetBounds(0, 28, 110, 34);
            closeAll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            closeAll.Click += delegate
            {
                int live = _mgr.LiveCount;
                if (live == 0) { SetStatus("Nothing running.", false); return; }
                if (!Dlg.Confirm(this, "Close all clients", "Close " + live + " running Roblox client" + (live == 1 ? "" : "s") + "?", "Close them", true)) return;
                _mgr.CloseAll();
                SetStatus("Closed all clients.", false);
            };
            p.Controls.Add(closeAll);

            var clear = new NButton();
            clear.Text = "Clear finished";
            clear.Glyph = "";
            clear.Kind = BtnKind.Quiet;
            clear.SetBounds(0, 28, 130, 34);
            clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            clear.Click += delegate { _mgr.Clear(true); RefreshInstances(); };
            p.Controls.Add(clear);

            _instList = new NScroll();
            _instList.SetBounds(26, 96, 100, 100);
            _instList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _instList.Resize += delegate { ReflowInstances(); };
            p.Controls.Add(_instList);

            p.Resize += delegate
            {
                int w = p.ClientSize.Width;
                closeAll.Left = w - 26 - closeAll.Width;
                arrange.Left = closeAll.Left - 8 - arrange.Width;
                clear.Left = arrange.Left - 8 - clear.Width;
                _instList.Width = w - 52;
                _instList.Height = Math.Max(80, p.ClientSize.Height - 96 - 12);
                ReflowInstances();
            };

            return p;
        }

        void RefreshInstances()
        {
            List<RbxInstance> items = _mgr.Snapshot();
            items.Reverse();

            bool sameSet = items.Count == _instCards.Count;
            // first paint of the empty list still needs its placeholder
            if (sameSet && items.Count == 0 && _instList.Controls.Count == 0) sameSet = false;
            if (sameSet)
                for (int i = 0; i < items.Count; i++)
                    if (!ReferenceEquals(items[i], _instCards[i].Model)) { sameSet = false; break; }

            if (!sameSet)
            {
                foreach (InstanceCard c in _instCards) c.Dispose();
                _instCards.Clear();
                _instList.Controls.Clear();

                foreach (RbxInstance inst in items)
                {
                    RbxInstance captured = inst;
                    var card = new InstanceCard(inst);
                    card.FocusClicked += delegate { Native.Focus(captured.Hwnd); };
                    card.CloseClicked += delegate { _mgr.Close(captured); };
                    _instCards.Add(card);
                    _instList.Controls.Add(card);
                    card.PlayEntrance(_instCards.Count - 1);
                }

                if (items.Count == 0)
                {
                    var empty = new Label();
                    empty.Text = "Nothing launched yet.\n\nGo to Accounts, pick who should play and where, then hit Launch.";
                    empty.Font = Theme.F9;
                    empty.ForeColor = Theme.TextFaint;
                    empty.AutoSize = true;
                    empty.BackColor = Color.Transparent;
                    empty.Location = new Point(14, 24);
                    _instList.Controls.Add(empty);
                }
                ReflowInstances();
            }

            foreach (InstanceCard c in _instCards) c.Sync();
        }

        void ReflowInstances()
        {
            int w = Math.Max(300, _instList.ClientSize.Width - 4);
            int h = _instList.LogicalToDeviceUnits(70);
            int gap = _instList.LogicalToDeviceUnits(4);
            int y = 0;
            foreach (InstanceCard c in _instCards)
            {
                c.SetBounds(0, y, w, h);
                y += h + gap;
            }
        }

        // ---------------------------------------------------------------- settings page

        Panel BuildSettingsPage()
        {
            Panel p = NewPage();
            Head(p, "Settings", "Tune how NeuzBlox launches and arranges your clients.");

            var scroll = new NScroll();
            scroll.SetBounds(26, 96, 100, 100);
            scroll.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            p.Controls.Add(scroll);

            int y = 0;
            var cards = new List<NCard>();

            // launching
            NCard c1 = Section(scroll, ref y, "Launching", 196, cards);

            var delayLbl = new Label();
            delayLbl.Font = Theme.F9;
            delayLbl.ForeColor = Theme.Text;
            delayLbl.BackColor = Color.Transparent;
            delayLbl.AutoSize = true;
            delayLbl.Location = new Point(18, 48);
            delayLbl.Text = "Seconds between each client";
            c1.Controls.Add(delayLbl);

            var delayBox = new NTextBox();
            delayBox.Text = _cfg.LaunchDelaySeconds.ToString(CultureInfo.InvariantCulture);
            delayBox.SetBounds(18, 70, 90, 32);
            delayBox.TextChanged += delegate
            {
                int v;
                if (int.TryParse(delayBox.Text, out v) && v >= 0 && v <= 120) { _cfg.LaunchDelaySeconds = v; _cfg.Save(); UpdateCounters(); }
            };
            c1.Controls.Add(delayBox);

            var delayHint = new Label();
            delayHint.Font = Theme.F8;
            delayHint.ForeColor = Theme.TextFaint;
            delayHint.BackColor = Color.Transparent;
            delayHint.AutoSize = true;
            delayHint.Location = new Point(118, 79);
            delayHint.Text = "Roblox dislikes bursts. 6-10s is a good range.";
            c1.Controls.Add(delayHint);

            var unlockChk = Check(c1, 18, 116, "Unlock multi-instance when NeuzBlox starts", _cfg.UnlockOnStart);
            unlockChk.CheckedChanged += delegate { _cfg.UnlockOnStart = unlockChk.Checked; _cfg.Save(); };

            var rejoinChk = Check(c1, 18, 146, "Rejoin automatically if a client closes on its own", _cfg.AutoRejoin);
            rejoinChk.CheckedChanged += delegate { _cfg.AutoRejoin = rejoinChk.Checked; _cfg.Save(); };

            // windows
            NCard c2 = Section(scroll, ref y, "Windows", 184, cards);

            var renameChk = Check(c2, 18, 48, "Rename each client window so you can tell accounts apart", _cfg.RenameWindows);
            renameChk.CheckedChanged += delegate { _cfg.RenameWindows = renameChk.Checked; _cfg.Save(); };

            var titleBox = new NTextBox();
            titleBox.Text = _cfg.TitleFormat;
            titleBox.Placeholder = "{alias} - NeuzBlox";
            titleBox.SetBounds(18, 78, 300, 32);
            titleBox.TextChanged += delegate { _cfg.TitleFormat = titleBox.Text; _cfg.Save(); };
            c2.Controls.Add(titleBox);

            var titleHint = new Label();
            titleHint.Font = Theme.F8;
            titleHint.ForeColor = Theme.TextFaint;
            titleHint.BackColor = Color.Transparent;
            titleHint.AutoSize = true;
            titleHint.Location = new Point(328, 87);
            titleHint.Text = "{alias}  {user}  {slot}  {target}";
            c2.Controls.Add(titleHint);

            var layoutLbl = new Label();
            layoutLbl.Font = Theme.F9;
            layoutLbl.ForeColor = Theme.Text;
            layoutLbl.BackColor = Color.Transparent;
            layoutLbl.AutoSize = true;
            layoutLbl.Location = new Point(18, 126);
            layoutLbl.Text = "Arrange layout";
            c2.Controls.Add(layoutLbl);

            var layoutCb = MakeCombo(new string[] { "grid", "columns", "rows", "stack" }, 150);
            layoutCb.Location = new Point(130, 122);
            int li = Array.IndexOf(new string[] { "grid", "columns", "rows", "stack" }, (_cfg.Layout ?? "grid").ToLowerInvariant());
            layoutCb.SelectedIndex = li < 0 ? 0 : li;
            layoutCb.SelectedIndexChanged += delegate { _cfg.Layout = layoutCb.SelectedItem.ToString(); _cfg.Save(); };
            c2.Controls.Add(layoutCb);

            // app
            NCard c3 = Section(scroll, ref y, "Application", 190, cards);

            var trayChk = Check(c3, 18, 48, "Minimise to the system tray instead of the taskbar", _cfg.MinimizeToTray);
            trayChk.CheckedChanged += delegate { _cfg.MinimizeToTray = trayChk.Checked; _cfg.Save(); };

            var exitChk = Check(c3, 18, 78, "Close all running clients when NeuzBlox exits", _cfg.CloseAllOnExit);
            exitChk.CheckedChanged += delegate { _cfg.CloseAllOnExit = exitChk.Checked; _cfg.Save(); };

            var pathLbl = new Label();
            pathLbl.Font = Theme.F9;
            pathLbl.ForeColor = Theme.Text;
            pathLbl.BackColor = Color.Transparent;
            pathLbl.AutoSize = true;
            pathLbl.Location = new Point(18, 112);
            pathLbl.Text = "Roblox client";
            c3.Controls.Add(pathLbl);

            var pathBox = new NTextBox();
            pathBox.Text = _playerPath ?? "";
            pathBox.Placeholder = "path to RobloxPlayerBeta.exe";
            pathBox.SetBounds(18, 134, 420, 32);
            pathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            c3.Controls.Add(pathBox);

            var browse = new NButton();
            browse.Text = "Browse";
            browse.Kind = BtnKind.Ghost;
            browse.SetBounds(448, 134, 92, 32);
            browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browse.Click += delegate
            {
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Filter = "RobloxPlayerBeta.exe|RobloxPlayerBeta.exe|Executables|*.exe";
                    ofd.Title = "Locate RobloxPlayerBeta.exe";
                    if (ofd.ShowDialog(this) != DialogResult.OK) return;
                    pathBox.Text = ofd.FileName;
                    _cfg.PlayerPath = ofd.FileName;
                    _cfg.Save();
                    _playerPath = ofd.FileName;
                    UpdateCounters();
                    SetStatus("Roblox client set.", false);
                }
            };
            c3.Controls.Add(browse);

            var redetect = new NButton();
            redetect.Text = "Auto-detect";
            redetect.Kind = BtnKind.Quiet;
            redetect.SetBounds(548, 134, 110, 32);
            redetect.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            redetect.Click += delegate
            {
                _cfg.PlayerPath = "";
                _cfg.Save();
                _playerPath = RobloxClient.FindPlayer("");
                pathBox.Text = _playerPath ?? "";
                UpdateCounters();
                SetStatus(string.IsNullOrEmpty(_playerPath) ? "Still could not find the Roblox client." : "Found the Roblox client.", string.IsNullOrEmpty(_playerPath));
            };
            c3.Controls.Add(redetect);

            // appearance
            NCard cA = Section(scroll, ref y, "Appearance", 118, cards);

            var animChk = Check(cA, 18, 48, "Animations and effects", _cfg.Animations);
            animChk.CheckedChanged += delegate
            {
                _cfg.Animations = animChk.Checked;
                _cfg.Save();
                Anim.Enabled = animChk.Checked;
                SetStatus(animChk.Checked ? "Animations on." : "Animations off - the app will feel snappier on weak PCs.", false);
                Invalidate(true);
            };

            var splashChk = Check(cA, 18, 78, "Show the loading screen on startup", _cfg.ShowSplash);
            splashChk.CheckedChanged += delegate { _cfg.ShowSplash = splashChk.Checked; _cfg.Save(); };

            // discord
            NCard cD = Section(scroll, ref y, "Discord rich presence", 266, cards);

            var dInfo = new Label();
            dInfo.Font = Theme.F8;
            dInfo.ForeColor = Theme.TextFaint;
            dInfo.BackColor = Color.Transparent;
            dInfo.AutoSize = true;
            dInfo.Location = new Point(18, 44);
            dInfo.Text = "Shows NeuzBlox on your Discord profile while it is open. Just tick the box -\n"
                       + "it uses the built-in NeuzBlox application, so there is nothing to set up.";
            cD.Controls.Add(dInfo);

            // Checkboxes get their own rows and only as much width as their label needs -
            // a wide one sits on top of anything beside it and swallows its clicks.
            var dChk = Check(cD, 18, 88, "Enable rich presence", _cfg.DiscordEnabled);
            var dNames = Check(cD, 18, 116, "Include account names (your Discord friends will see them)", _cfg.DiscordShowAccounts);

            var dStatus = new Label();
            dStatus.Font = Theme.F8;
            dStatus.ForeColor = Theme.TextDim;
            dStatus.BackColor = Color.Transparent;
            dStatus.AutoSize = true;
            dStatus.Location = new Point(18, 148);
            cD.Controls.Add(dStatus);

            var dIdLabel = new Label();
            dIdLabel.Text = "Use your own Discord application instead (optional)";
            dIdLabel.Font = Theme.F8;
            dIdLabel.ForeColor = Theme.TextFaint;
            dIdLabel.BackColor = Color.Transparent;
            dIdLabel.AutoSize = true;
            dIdLabel.Location = new Point(18, 182);
            cD.Controls.Add(dIdLabel);

            var dIdBox = new NTextBox();
            dIdBox.Text = _cfg.DiscordAppId;
            dIdBox.Placeholder = "leave empty to use the built-in NeuzBlox app";
            dIdBox.SetBounds(18, 202, 480, 32);
            dIdBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cD.Controls.Add(dIdBox);

            var dTest = new NButton();
            dTest.Glyph = "";
            dTest.Kind = BtnKind.Ghost;
            dTest.Text = "Reconnect";
            dTest.SetBounds(512, 202, 140, 32);   // 48px right margin on the 700px base card
            dTest.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cD.Controls.Add(dTest);

            EventHandler applyDiscord = delegate
            {
                _cfg.DiscordEnabled = dChk.Checked;
                _cfg.DiscordShowAccounts = dNames.Checked;
                _cfg.DiscordAppId = dIdBox.Text.Trim();
                _cfg.Save();
                StopDiscord();
                if (_cfg.DiscordEnabled)
                {
                    dStatus.ForeColor = Theme.TextDim;
                    dStatus.Text = "Connecting to Discord...";
                    StartDiscord();
                    var check = new System.Windows.Forms.Timer();
                    check.Interval = 1600;
                    check.Tick += delegate
                    {
                        check.Stop();
                        check.Dispose();
                        if (_rpc != null && _rpc.Connected)
                        {
                            dStatus.ForeColor = Theme.Green;
                            dStatus.Text = "Connected - your presence is live.";
                        }
                        else
                        {
                            dStatus.ForeColor = Theme.Amber;
                            dStatus.Text = _rpc == null
                                ? "Not connected - is Discord running?"
                                : "Not connected: " + _rpc.LastError;
                        }
                    };
                    check.Start();
                }
                else
                {
                    dStatus.ForeColor = Theme.TextFaint;
                    dStatus.Text = "Rich presence is off.";
                }
            };

            dChk.CheckedChanged += applyDiscord;
            dNames.CheckedChanged += applyDiscord;
            dTest.Click += applyDiscord;
            dStatus.Text = _cfg.DiscordEnabled ? "Rich presence is on." : "Rich presence is off.";

            // data
            NCard c4 = Section(scroll, ref y, "Stored data", 150, cards);

            var dataInfo = new Label();
            dataInfo.Font = Theme.F8;
            dataInfo.ForeColor = Theme.TextDim;
            dataInfo.BackColor = Color.Transparent;
            dataInfo.AutoSize = true;
            dataInfo.Location = new Point(18, 46);
            dataInfo.Text = "Account sessions are encrypted with Windows DPAPI and readable only by your\n"
                          + "Windows user on this PC. Nothing is uploaded anywhere except roblox.com.";
            c4.Controls.Add(dataInfo);

            var openFolder = new NButton();
            openFolder.Text = "Open data folder";
            openFolder.Glyph = "";
            openFolder.Kind = BtnKind.Ghost;
            openFolder.SetBounds(18, 96, 170, 32);
            openFolder.Click += delegate
            {
                try { System.Diagnostics.Process.Start("explorer.exe", Paths.Root); }
                catch { }
            };
            c4.Controls.Add(openFolder);

            var wipe = new NButton();
            wipe.Text = "Delete all saved accounts";
            wipe.Kind = BtnKind.Danger;
            wipe.SetBounds(200, 96, 210, 32);
            wipe.Click += delegate
            {
                if (!Dlg.Confirm(this, "Delete everything",
                    "Delete every saved account session from this PC?\n\nYour Roblox accounts are not affected - you will just have to\npaste their cookies again next time.",
                    "Delete all", true)) return;
                _accounts.Clear();
                AccountStore.Wipe();
                RebuildAccounts();
                SetStatus("All saved sessions deleted.", false);
            };
            c4.Controls.Add(wipe);

            scroll.Resize += delegate
            {
                int w = Math.Max(420, scroll.ClientSize.Width - 4);
                foreach (NCard c in cards) c.Width = w;
            };

            p.Resize += delegate
            {
                scroll.Width = p.ClientSize.Width - 52;
                scroll.Height = Math.Max(80, p.ClientSize.Height - 96 - 12);
            };

            p.VisibleChanged += delegate
            {
                if (p.Visible) scroll.AutoScrollPosition = new Point(0, 0);
            };

            return p;
        }

        NCard Section(Control host, ref int y, string title, int h, List<NCard> bag)
        {
            var c = new NCard();
            c.SetBounds(0, y, 700, h);
            host.Controls.Add(c);
            bag.Add(c);

            var t = new Label();
            t.Text = title.ToUpperInvariant();
            t.Font = Theme.F8;
            t.ForeColor = Theme.TextFaint;
            t.BackColor = Color.Transparent;
            t.AutoSize = true;
            t.Location = new Point(18, 16);
            c.Controls.Add(t);

            y += h + 14;
            return c;
        }

        /// <summary>
        /// Sized to its own label. A fixed-width checkbox overlaps whatever sits beside it
        /// and, being a full control, paints over it and eats its clicks.
        /// </summary>
        NCheck Check(Control host, int x, int y, string text, bool state)
        {
            var c = new NCheck();
            c.Text = text;
            c.Checked = state;
            int w = TextRenderer.MeasureText(text, Theme.F9).Width + 36;
            c.SetBounds(x, y, Math.Min(600, Math.Max(120, w)), 24);
            host.Controls.Add(c);
            return c;
        }

        // ---------------------------------------------------------------- credits page

        Panel BuildCreditsPage()
        {
            Panel p = NewPage();
            Head(p, "Credits", "Who made this, and how it works.");

            var hero = new CreditsHero();
            hero.SetBounds(26, 96, 700, 156);
            hero.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            p.Controls.Add(hero);

            var gh = new NButton();
            gh.Text = AppInfo.GitHubHandle;
            gh.Glyph = "";
            gh.Kind = BtnKind.Ghost;
            gh.SetBounds(0, 96, 150, 34);
            gh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            gh.Click += delegate
            {
                try { System.Diagnostics.Process.Start(AppInfo.GitHubUrl); }
                catch (Exception ex) { SetStatus("Could not open the browser: " + ex.Message, true); }
            };
            hero.Controls.Add(gh);

            var copy = new NButton();
            copy.Glyph = "";
            copy.Kind = BtnKind.Quiet;
            copy.SetBounds(0, 96, 34, 34);
            copy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            copy.Click += delegate
            {
                try { Clipboard.SetText(AppInfo.GitHubUrl); SetStatus("Link copied.", false); }
                catch { }
            };
            hero.Controls.Add(copy);

            hero.Resize += delegate
            {
                copy.Left = hero.ClientSize.Width - 22 - copy.Width;
                gh.Left = copy.Left - 8 - gh.Width;
                gh.Top = hero.ClientSize.Height - 22 - gh.Height;
                copy.Top = gh.Top;
            };

            var tipc = new ToolTip();
            tipc.SetToolTip(gh, AppInfo.GitHubUrl);
            tipc.SetToolTip(copy, "Copy the link");

            var card = new NCard();
            card.SetBounds(26, 266, 700, 300);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            p.Controls.Add(card);

            var body = new Label();
            body.Font = Theme.F9;
            body.ForeColor = Theme.TextDim;
            body.BackColor = Color.Transparent;
            body.SetBounds(22, 18, 660, 260);
            body.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            body.Text =
                "HOW IT WORKS\r\n" +
                "The Roblox client refuses to open twice because it claims a named Windows lock on\r\n" +
                "startup. NeuzBlox holds that lock itself, so each client it starts keeps running instead\r\n" +
                "of handing off to the first one.\r\n" +
                "\r\n" +
                "To put a different account in each window, NeuzBlox asks Roblox for a one-time launch\r\n" +
                "ticket using that account's own session, exactly like the website does when you press\r\n" +
                "Play, and hands the ticket to the new client.\r\n" +
                "\r\n" +
                "WHAT IT DOES NOT DO\r\n" +
                "No files are patched, no memory is touched, no code is injected into Roblox, and no\r\n" +
                "gameplay is automated. It launches clients and arranges windows, nothing more.\r\n" +
                "\r\n" +
                "YOUR DATA\r\n" +
                "Sessions are stored encrypted with Windows DPAPI under your Windows user account.\r\n" +
                "A .ROBLOSECURITY cookie is a full login - treat it like a password and never paste one\r\n" +
                "into a site or tool you do not trust. Nothing is sent anywhere except roblox.com.\r\n" +
                "\r\n" +
                "BUILT WITH\r\n" +
                "C# on WinForms, compiled by the C# compiler that ships inside Windows. No NuGet\r\n" +
                "packages, no frameworks, no third-party code - the JSON parser, the dark theme, the\r\n" +
                "animation engine and the Discord presence client are all hand-rolled here.";
            card.Controls.Add(body);

            var ver = new Label();
            ver.Font = Theme.F8;
            ver.ForeColor = Theme.TextFaint;
            ver.BackColor = Color.Transparent;
            ver.AutoSize = true;
            ver.Text = "NeuzBlox " + AppInfo.Version + "  ·  " + AppInfo.BuildDate
                     + "  ·  running as " + Environment.UserName;
            ver.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            ver.Location = new Point(22, card.Height - 32);
            card.Controls.Add(ver);
            ver.BringToFront();

            p.Resize += delegate
            {
                hero.Width = p.ClientSize.Width - 52;
                card.Width = p.ClientSize.Width - 52;
                card.Height = Math.Max(140, p.ClientSize.Height - 266 - 12);
                body.Width = card.ClientSize.Width - 44;
                body.Height = Math.Max(60, card.ClientSize.Height - 54);
                ver.Top = card.ClientSize.Height - 30;
            };

            return p;
        }

        // ---------------------------------------------------------------- tray + status

        void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = AppIcon.Get();
            _tray.Text = "NeuzBlox";
            _tray.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Theme.Surface2;
            menu.ForeColor = Theme.Text;
            menu.ShowImageMargin = false;

            var open = new ToolStripMenuItem("Open NeuzBlox");
            open.Click += delegate { RestoreWindow(); };
            var arrange = new ToolStripMenuItem("Arrange windows");
            arrange.Click += delegate { _mgr.Arrange(_cfg.Layout); };
            var closeAll = new ToolStripMenuItem("Close all clients");
            closeAll.Click += delegate { _mgr.CloseAll(); };
            var exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { _reallyExit = true; Close(); };

            menu.Items.Add(open);
            menu.Items.Add(arrange);
            menu.Items.Add(closeAll);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { RestoreWindow(); };
        }

        void RestoreWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void SetStatus(string msg, bool bad)
        {
            SetStatus(msg, bad, true);
        }

        void SetStatus(string msg, bool bad, bool log)
        {
            _statusLbl.ForeColor = bad ? Theme.Red : Theme.TextDim;
            _statusLbl.Text = msg;
            if (log) Log.Write((bad ? "! " : "  ") + msg);
        }

        /// <summary>
        /// Off the UI thread, ask Roblox which build is current and point at it. Third-party
        /// tools re-register the roblox-player link to other builds; launching one of those
        /// sends the client through its installer, which then opens a client we never
        /// authenticated - so the wrong account appears, or none at all.
        /// </summary>
        void ResolveCurrentClient()
        {
            var th = new Thread(delegate()
            {
                string path = RobloxClient.FindPlayer(_cfg.PlayerPath, true);
                string warn = RobloxClient.HandlerMismatch();
                SafeInvoke(delegate
                {
                    if (!string.IsNullOrEmpty(path)) _playerPath = path;
                    UpdateCounters();
                    if (!string.IsNullOrEmpty(warn)) SetStatus(warn, false);
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        /// <summary>
        /// Roblox finishes its own install in the background and a client started mid-way
        /// through just dies. Say so plainly and keep a clock on it, so waiting looks like
        /// waiting instead of a hang.
        /// </summary>
        int _updateTick;

        void WatchRobloxUpdate()
        {
            // enumerating processes is not free; every ~2.4s is plenty for an installer
            if ((++_updateTick % 3) != 0 && !_updateSince.HasValue) return;

            bool updating = RobloxClient.IsUpdating();

            if (updating)
            {
                if (!_updateSince.HasValue)
                {
                    _updateSince = DateTime.UtcNow;
                    Log.Write("Roblox installer is running - holding launches");
                }
                int secs = (int)(DateTime.UtcNow - _updateSince.Value).TotalSeconds;
                SetStatus("Roblox is installing its own update - NeuzBlox is waiting for it ("
                          + RobloxClient.Elapsed(secs) + "). Leave this open.", false, false);
            }
            else if (_updateSince.HasValue)
            {
                _updateSince = null;
                SetStatus("Roblox finished updating. You can launch now.", false);
                ResolveCurrentClient();
            }
        }

        void UpdateMultiInstanceUi()
        {
            bool on = MultiInstance.Active;
            _miState.Text = on ? MultiInstance.Detail : "Locked";
            _miState.ForeColor = !on ? Theme.Amber : (MultiInstance.Owned ? Theme.Green : Theme.Accent);
            _miToggle.Text = on ? "Lock again" : "Unlock now";
            _miToggle.Glyph = on ? "" : "";
            _miToggle.Kind = on ? BtnKind.Ghost : BtnKind.Accent;
        }

        void UpdateCounters()
        {
            int live = _mgr.LiveCount;
            int sel = 0;
            foreach (AccountCard c in _accCards) if (c.Selected) sel++;

            if (_launchSel != null)
                _launchSel.Text = sel > 0 ? "Launch selected (" + sel + ")" : "Launch selected";
            if (_instHint != null)
                _instHint.Text = _cfg.LaunchDelaySeconds + "s between clients";

            _rightLbl.Text = live + " running  ·  " + _accounts.Count + " accounts  ·  client "
                + RobloxClient.Version(_playerPath);
            _navInst.Text = live > 0 ? "Instances (" + live + ")" : "Instances";
            _navInst.Invalidate();
        }

        void ShowFirstRun()
        {
            Dlg.Info(this, "Before you start",
                "NeuzBlox keeps one Roblox session per account so several can play at once.\n\n" +
                "A session cookie is as powerful as a password. Only ever paste one you copied\n" +
                "yourself, and never share this PC's NeuzBlox data folder with anyone.\n\n" +
                "Sessions are encrypted for your Windows user and stay on this machine.");
            _cfg.RiskAcknowledged = true;
            _cfg.Save();
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!_reallyExit && _cfg.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                int live = _mgr.LiveCount;
                if (live > 0)
                {
                    e.Cancel = true;
                    Hide();
                    try
                    {
                        _tray.BalloonTipTitle = "NeuzBlox is still running";
                        _tray.BalloonTipText = live + " client" + (live == 1 ? "" : "s") + " open. Right-click the tray icon to exit.";
                        _tray.ShowBalloonTip(2500);
                    }
                    catch { }
                    return;
                }
            }

            if (_cfg.CloseAllOnExit) _mgr.CloseAll();
            _cfg.Save();
            AccountStore.Save(_accounts);
            StopDiscord();
            GameWatcher.Stop();
            MultiInstance.Disable();
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } }
            catch { }
        }

        void SafeInvoke(MethodInvoker action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch { }
        }
    }

    /// <summary>Credits banner: the mark, the wordmark, and a light sweeping across it.</summary>
    public class CreditsHero : Control
    {
        readonly Anim _sweep;
        readonly Anim _enter;

        public CreditsHero()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            _sweep = new Anim(this, 0f);
            _enter = new Anim(this, Anim.Enabled ? 0f : 1f);

            VisibleChanged += delegate
            {
                if (Visible)
                {
                    if (Anim.Enabled) { _sweep.Loop(3200); _enter.Set(0f); _enter.To(1f, 520, Ease.OutQuint); }
                }
                else _sweep.Stop();
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(BackColor);

            float v = _enter.Value;
            if (v <= 0.005f) return;
            g.TranslateTransform(0f, (1f - v) * 12f);

            var r = new Rectangle(0, 0, Width, Height);
            using (var path = Theme.Round(r, 14))
            {
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)),
                    Theme.Mix(Theme.Bg, Color.FromArgb(28, 33, 58), v),
                    Theme.Mix(Theme.Bg, Color.FromArgb(20, 24, 36), v), 20f))
                    g.FillPath(br, path);

                // light sweep
                if (Anim.Enabled && v > 0.6f)
                {
                    float s = _sweep.Value;
                    int x = (int)(Width * (s * 1.6f - 0.3f));
                    var band = new Rectangle(x - 70, 0, 140, Height);
                    Region old = g.Clip;
                    g.SetClip(path, System.Drawing.Drawing2D.CombineMode.Replace);
                    using (var sh = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new Rectangle(band.X, band.Y, Math.Max(1, band.Width), Math.Max(1, band.Height)),
                        Color.Transparent, Color.Transparent, 0f))
                    {
                        var blend = new System.Drawing.Drawing2D.ColorBlend(3);
                        blend.Colors = new Color[]
                        {
                            Color.FromArgb(0, 255, 255, 255),
                            Color.FromArgb(16, 255, 255, 255),
                            Color.FromArgb(0, 255, 255, 255)
                        };
                        blend.Positions = new float[] { 0f, 0.5f, 1f };
                        sh.InterpolationColors = blend;
                        g.FillRectangle(sh, band);
                    }
                    g.Clip = old;
                }
            }
            Theme.DrawRound(g, r, 14, Theme.Mix(Theme.Bg, Theme.BorderHi, v), 1f);

            var mark = new Rectangle(24, (Height - 62) / 2, 62, 62);
            Theme.DrawMark(g, mark);

            int tx = mark.Right + 20;
            using (var f = new Font("Segoe UI Semibold", 19f))
            using (var b = new SolidBrush(Theme.Mix(Theme.Bg, Theme.Text, v)))
                g.DrawString("NeuzBlox", f, b, tx, mark.Y - 2);

            using (var b = new SolidBrush(Theme.Mix(Theme.Bg, Theme.TextDim, v)))
                g.DrawString("multi-instance launcher for Roblox  ·  v" + AppInfo.Version,
                             Theme.F9, b, tx + 2, mark.Y + 32);

            using (var b = new SolidBrush(Theme.Mix(Theme.Bg, Theme.TextFaint, v)))
                g.DrawString("made by " + AppInfo.Author, Theme.F8, b, tx + 2, mark.Y + 52);
        }
    }

    /// <summary>Dark colours for context menus.</summary>
    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextFaint;
            base.OnRenderItemText(e);
        }

        class DarkColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Theme.SurfaceHi; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.SurfaceHi; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.SurfaceHi; } }
            public override Color MenuItemBorder { get { return Theme.BorderHi; } }
            public override Color MenuBorder { get { return Theme.BorderHi; } }
            public override Color ToolStripDropDownBackground { get { return Theme.Surface2; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Surface2; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Surface2; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Surface2; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Border; } }
        }
    }
}
