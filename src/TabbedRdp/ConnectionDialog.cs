using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TabbedRdp
{
    public enum ConnectionDialogMode
    {
        /// <summary>"+" / Ctrl+T: connect to a new computer.</summary>
        NewConnection,
        /// <summary>Edit an entry of the saved-connections list.</summary>
        EditSaved,
        /// <summary>Alt menu → Options: settings of the current tab ("Apply &amp; reconnect").</summary>
        TabOptions,
        /// <summary>Default settings for every new connection.</summary>
        Defaults,
    }

    /// <summary>
    /// All Remote Desktop settings the embedded client supports, grouped like mstsc's "Show Options"
    /// tabs but as sidebar pages.
    /// </summary>
    public sealed class ConnectionDialog : Form
    {
        private static readonly (string Label, int W, int H)[] Presets =
        {
            ("3840 × 2160 (4K)", 3840, 2160),
            ("2560 × 1440", 2560, 1440),
            ("1920 × 1200", 1920, 1200),
            ("1920 × 1080 (Full HD)", 1920, 1080),
            ("1680 × 1050", 1680, 1050),
            ("1600 × 900", 1600, 900),
            ("1440 × 900", 1440, 900),
            ("1366 × 768", 1366, 768),
            ("1280 × 1024", 1280, 1024),
            ("1280 × 800", 1280, 800),
            ("1280 × 720", 1280, 720),
            ("1024 × 768", 1024, 768),
            ("800 × 600", 800, 600),
        };

        private static readonly int[] ColorDepths = { 32, 24, 16, 15 };
        private static readonly int[] AuthLevels = { 2, 0, 1 };           // warn, connect, don't connect
        private static readonly int[] GatewayUsages = { 1, 2 };           // always, bypass for local
        private static readonly int[] GatewayLogons = { 0, 1, 4 };        // password, smart card, select later

        private readonly ConnectionDialogMode _mode;
        private readonly Action<ConnectionInfo> _saveAsDefault;
        private readonly ConnectionInfo _original;

        // navigation
        private readonly ListBox _nav = new ListBox();
        private readonly Panel _pageHost = new Panel();
        private readonly Label _pageTitle = new Label();
        private readonly List<(string Title, Control Page)> _pages = new List<(string, Control)>();

        // Connection
        private readonly ComboBox _computer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        private readonly TextBox _user = new TextBox();
        private readonly TextBox _password = new TextBox { UseSystemPasswordChar = true };
        private readonly CheckBox _showPassword = new CheckBox { Text = "Show", AutoSize = true };
        private readonly CheckBox _remember = new CheckBox { Text = "Remember password (encrypted)", AutoSize = true };
        private readonly TextBox _name = new TextBox();
        private readonly ComboBox _group = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        private readonly TextBox _notes = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 70 };
        private readonly CheckBox _saveToList = new CheckBox { Text = "Save to my connections", AutoSize = true };

        // Display
        private readonly RadioButton _fit = new RadioButton { Text = "Fit to window — resize the remote desktop with the tab (dynamic resolution)", AutoSize = true };
        private readonly RadioButton _fixed = new RadioButton { Text = "Fixed size", AutoSize = true };
        private readonly RadioButton _full = new RadioButton { Text = "Full screen", AutoSize = true };
        private readonly ComboBox _preset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        private readonly NumericUpDown _width = new NumericUpDown { Minimum = 200, Maximum = 8192, Width = 72 };
        private readonly NumericUpDown _height = new NumericUpDown { Minimum = 200, Maximum = 8192, Width = 72 };
        private readonly CheckBox _smartSizing = new CheckBox { Text = "Scale the remote desktop to fit the tab (smart sizing)", AutoSize = true };
        private readonly ComboBox _colors = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _bar = new CheckBox { Text = "Show the connection bar in full screen", AutoSize = true };
        private readonly CheckBox _pinBar = new CheckBox { Text = "Pin the connection bar", AutoSize = true };

        // Local resources
        private readonly ComboBox _audio = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _mic = new CheckBox { Text = "Record from this computer (microphone)", AutoSize = true };
        private readonly ComboBox _keyboard = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _clipboard = new CheckBox { Text = "Clipboard", AutoSize = true };
        private readonly CheckBox _printers = new CheckBox { Text = "Printers", AutoSize = true };
        private readonly CheckBox _drives = new CheckBox { Text = "Local drives", AutoSize = true };
        private readonly CheckBox _smartCards = new CheckBox { Text = "Smart cards", AutoSize = true };
        private readonly CheckBox _ports = new CheckBox { Text = "Serial ports (COM)", AutoSize = true };

        // Experience
        private readonly ComboBox _speed = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _wallpaper = new CheckBox { Text = "Desktop background", AutoSize = true };
        private readonly CheckBox _fontSmoothing = new CheckBox { Text = "Font smoothing", AutoSize = true };
        private readonly CheckBox _composition = new CheckBox { Text = "Desktop composition", AutoSize = true };
        private readonly CheckBox _drag = new CheckBox { Text = "Show window contents while dragging", AutoSize = true };
        private readonly CheckBox _animations = new CheckBox { Text = "Menu and window animation", AutoSize = true };
        private readonly CheckBox _themes = new CheckBox { Text = "Visual styles", AutoSize = true };
        private readonly CheckBox _cursor = new CheckBox { Text = "Cursor shadow and blinking", AutoSize = true };
        private readonly CheckBox _autoReconnect = new CheckBox { Text = "Reconnect automatically if the connection drops", AutoSize = true };
        private bool _applyingPreset;

        // Security & advanced
        private readonly CheckBox _nla = new CheckBox { Text = "Use Network Level Authentication (CredSSP)", AutoSize = true };
        private readonly ComboBox _auth = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _prompt = new CheckBox { Text = "Always ask for credentials", AutoSize = true };
        private readonly CheckBox _admin = new CheckBox { Text = "Connect to the admin / console session (/admin)", AutoSize = true };
        private readonly TextBox _gwHost = new TextBox();
        private readonly ComboBox _gwUsage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox _gwLogon = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _gwSame = new CheckBox { Text = "Use my gateway credentials for the remote computer", AutoSize = true };
        private readonly TextBox _shell = new TextBox();
        private readonly TextBox _workDir = new TextBox();

        // bottom bar
        private readonly CheckBox _updateSaved = new CheckBox { Text = "Also update the saved connection", AutoSize = true, Checked = true };
        private readonly Label _savedNote = new Label { AutoSize = true, Tag = "muted" };

        public ConnectionInfo Result { get; private set; }
        public bool SaveToList => _saveToList.Checked;
        public bool UpdateSaved => _updateSaved.Visible && _updateSaved.Checked;

        /// <param name="addresses">History / saved addresses for the computer box.</param>
        /// <param name="saveAsDefault">Called with the collected settings when "Save as default" is clicked.</param>
        /// <param name="canUpdateSaved">TabOptions: the tab came from a saved connection.</param>
        public ConnectionDialog(ConnectionInfo info, ConnectionDialogMode mode, IEnumerable<string> groups,
            IEnumerable<string> addresses, Action<ConnectionInfo> saveAsDefault, bool canUpdateSaved = false)
        {
            _mode = mode;
            _saveAsDefault = saveAsDefault;
            _original = info;
            Result = info;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            Text = mode == ConnectionDialogMode.NewConnection ? "New connection"
                 : mode == ConnectionDialogMode.Defaults ? "Default settings for new connections"
                 : mode == ConnectionDialogMode.TabOptions ? $"Options — {info.DisplayName}"
                 : $"Connection — {info.DisplayName}";
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 600);
            MinimumSize = new Size(700, 480);

            FillCombos(groups, addresses);

            if (mode != ConnectionDialogMode.Defaults) _pages.Add(("Connection", BuildConnectionPage()));
            _pages.Add(("Display", BuildDisplayPage()));
            _pages.Add(("Local resources", BuildResourcesPage()));
            _pages.Add(("Experience", BuildExperiencePage()));
            _pages.Add(("Security & advanced", BuildAdvancedPage()));

            BuildShell(canUpdateSaved);
            Populate(info);
            ResumeLayout(true);

            Theme.ApplyWindow(this);
            StylePageTitle();
            _nav.SelectedIndex = 0;

            Shown += (s, e) =>
            {
                if (mode == ConnectionDialogMode.NewConnection || mode == ConnectionDialogMode.EditSaved)
                {
                    if (_computer.Text.Length == 0) _computer.Focus();
                    else if (_user.Text.Length == 0) _user.Focus();
                    else _password.Focus();
                }
            };
        }

        // ---- shell -----------------------------------------------------------------------

        private void BuildShell(bool canUpdateSaved)
        {
            _nav.Dock = DockStyle.Left;
            _nav.Width = 190;
            _nav.BorderStyle = BorderStyle.None;
            _nav.DrawMode = DrawMode.OwnerDrawFixed;
            _nav.ItemHeight = 36;
            _nav.IntegralHeight = false;
            _nav.Tag = "noTheme";
            _nav.Items.AddRange(_pages.Select(p => (object)p.Title).ToArray());
            _nav.DrawItem += DrawNavItem;
            _nav.SelectedIndexChanged += (s, e) => ShowPage(_nav.SelectedIndex);

            _pageTitle.Dock = DockStyle.Top;
            _pageTitle.UseMnemonic = false;
            _pageTitle.Height = 44;
            _pageTitle.TextAlign = ContentAlignment.MiddleLeft;
            _pageTitle.Font = new Font(Font.FontFamily, 14f, FontStyle.Regular);

            _pageHost.Dock = DockStyle.Fill;
            _pageHost.Padding = new Padding(24, 8, 16, 8);
            foreach (var (_, page) in _pages)
            {
                page.Dock = DockStyle.Fill;
                page.Visible = false;
                _pageHost.Controls.Add(page);
            }
            _pageHost.Controls.Add(_pageTitle);

            // bottom bar
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(16, 12, 16, 12), Tag = "surface" };
            var primary = new Button
            {
                Text = _mode == ConnectionDialogMode.NewConnection ? "Connect"
                     : _mode == ConnectionDialogMode.TabOptions ? "Apply && reconnect"
                     : _mode == ConnectionDialogMode.Defaults ? "Save defaults" : "Save",
                Tag = "primary",
                AutoSize = true,
                MinimumSize = new Size(110, 32),
                Dock = DockStyle.Right,
            };
            primary.Click += (s, e) => Accept();
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, MinimumSize = new Size(96, 32), AutoSize = true, Dock = DockStyle.Right };
            AcceptButton = primary;
            CancelButton = cancel;

            var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Tag = "keepBack", BackColor = Color.Transparent };
            if (_mode != ConnectionDialogMode.Defaults && _saveAsDefault != null)
            {
                var saveDefault = new Button { Text = "Save as default", AutoSize = true, MinimumSize = new Size(0, 32), Margin = new Padding(0) };
                saveDefault.Click += (s, e) =>
                {
                    if (!Collect(out var settings, requireHost: false)) return;
                    _saveAsDefault(settings);
                    _savedNote.Text = "✓ Saved as default for new connections";
                };
                left.Controls.Add(saveDefault);
            }
            _updateSaved.Visible = _mode == ConnectionDialogMode.TabOptions && canUpdateSaved;
            _updateSaved.Margin = new Padding(12, 8, 3, 0);
            left.Controls.Add(_updateSaved);
            _savedNote.Margin = new Padding(12, 9, 3, 0);
            left.Controls.Add(_savedNote);

            bottom.Controls.Add(left);
            bottom.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8, Tag = "keepBack", BackColor = Color.Transparent });
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8, Tag = "keepBack", BackColor = Color.Transparent });
            bottom.Controls.Add(primary);

            Controls.Add(_pageHost);
            Controls.Add(_nav);
            Controls.Add(bottom);
        }

        private void StylePageTitle()
        {
            var p = Theme.Current;
            _nav.BackColor = p.Surface;
            _nav.ForeColor = p.Text;
            _pageTitle.ForeColor = p.Text;
        }

        private void DrawNavItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var p = Theme.Current;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(p.Surface)) e.Graphics.FillRectangle(bg, e.Bounds);
            if (selected)
            {
                var r = Rectangle.Inflate(e.Bounds, -6, -3);
                using (var b = new SolidBrush(p.Selected)) e.Graphics.FillRectangle(b, r);
                using (var a = new SolidBrush(p.Accent)) e.Graphics.FillRectangle(a, r.Left, r.Top + 6, 3, r.Height - 12);
            }
            TextRenderer.DrawText(e.Graphics, _nav.Items[e.Index].ToString(), Font,
                new Rectangle(e.Bounds.Left + 20, e.Bounds.Top, e.Bounds.Width - 24, e.Bounds.Height), p.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void ShowPage(int index)
        {
            if (index < 0) return;
            for (int i = 0; i < _pages.Count; i++) _pages[i].Page.Visible = i == index;
            _pageTitle.Text = _pages[index].Title;
        }

        // ---- page building helpers -------------------------------------------------------

        /// <summary>A scrollable page: a two-column table (label / control) stacked from the top.</summary>
        private sealed class Page
        {
            public readonly Panel Root = new Panel { AutoScroll = true };
            private readonly TableLayoutPanel _table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Padding = new Padding(0, 0, 8, 8),
            };

            public Page()
            {
                _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
                _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                Root.Controls.Add(_table);
            }

            public void Section(string title)
            {
                var label = new Label { Text = title, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold), Margin = new Padding(0, _table.RowCount == 0 ? 4 : 18, 0, 6) };
                Add(label, span: true);
            }

            public void Row(string label, Control control, bool fill = true)
            {
                int row = _table.RowCount++;
                _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 6, 3) }, 0, row);
                if (fill) control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                control.Margin = new Padding(0, 3, 0, 3);
                _table.Controls.Add(control, 1, row);
            }

            public void Indented(Control control)
            {
                int row = _table.RowCount++;
                _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                control.Margin = new Padding(0, 2, 0, 2);
                _table.Controls.Add(control, 1, row);
            }

            public void Add(Control control, bool span = true, int indent = 0)
            {
                int row = _table.RowCount++;
                _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                if (!(control is Label)) control.Margin = new Padding(indent, 3, 0, 3);
                _table.Controls.Add(control, 0, row);
                if (span) _table.SetColumnSpan(control, 2);
            }

            public void Hint(string text, bool indented = true)
            {
                var hint = new Label { Text = text, AutoSize = true, Tag = "muted", Margin = new Padding(0, 0, 0, 6), MaximumSize = new Size(520, 0) };
                if (indented) Indented(hint); else Add(hint);
            }
        }

        private static FlowLayoutPanel Inline(params Control[] controls)
        {
            var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Tag = "keepBack", BackColor = Color.Transparent };
            foreach (var c in controls)
            {
                if (c is Label label)
                {
                    label.AutoSize = true;
                    label.Margin = new Padding(6, 6, 6, 0);
                }
                flow.Controls.Add(c);
            }
            return flow;
        }

        // ---- pages -----------------------------------------------------------------------

        private Control BuildConnectionPage()
        {
            var page = new Page();
            page.Section("Remote computer");
            page.Row("Computer:", _computer);
            page.Hint("Name or IP address, optionally with a port: server01, 10.0.0.5:3390, [fe80::1]");
            page.Row("User name:", _user);
            page.Hint(@"DOMAIN\user, user@domain or .\localadmin — leave empty to be asked when connecting");
            _password.Width = 260;
            _showPassword.CheckedChanged += (s, e) => _password.UseSystemPasswordChar = !_showPassword.Checked;
            page.Row("Password:", Inline(_password, _showPassword), fill: false);
            page.Indented(_remember);

            page.Section("Saved connection");
            page.Row("Display name:", _name);
            page.Row("Group / client:", _group);
            page.Row("Notes:", _notes);
            if (_mode == ConnectionDialogMode.NewConnection) page.Indented(_saveToList);
            return page.Root;
        }

        private Control BuildDisplayPage()
        {
            var page = new Page();
            page.Section("Remote desktop size");
            page.Add(_fit);
            page.Add(Inline(_fixed, _preset, new Label { Text = "or" }, _width, new Label { Text = "×" }, _height), span: true);
            page.Add(_full);
            page.Hint("Fit to window needs Windows 8.1 / Server 2012 R2 or newer on the remote side.", indented: false);

            _preset.Items.AddRange(Presets.Select(p => (object)p.Label).ToArray());
            _preset.Items.Add("Custom");
            _preset.SelectedIndexChanged += (s, e) =>
            {
                if (_preset.SelectedIndex >= 0 && _preset.SelectedIndex < Presets.Length)
                {
                    _width.Value = Presets[_preset.SelectedIndex].W;
                    _height.Value = Presets[_preset.SelectedIndex].H;
                    _fixed.Checked = true;
                }
            };
            EventHandler sizeTyped = (s, e) =>
            {
                int i = Array.FindIndex(Presets, p => p.W == (int)_width.Value && p.H == (int)_height.Value);
                _preset.SelectedIndex = i >= 0 ? i : Presets.Length;
            };
            _width.ValueChanged += sizeTyped;
            _height.ValueChanged += sizeTyped;
            _width.Enter += (s, e) => _fixed.Checked = true;
            _height.Enter += (s, e) => _fixed.Checked = true;
            _full.CheckedChanged += (s, e) => _smartSizing.Enabled = !_full.Checked;
            // The three size options live in different containers, so keep them exclusive by hand.
            foreach (var radio in new[] { _fit, _fixed, _full })
            {
                radio.CheckedChanged += (s, e) =>
                {
                    if (!((RadioButton)s).Checked) return;
                    foreach (var other in new[] { _fit, _fixed, _full })
                        if (other != s) other.Checked = false;
                };
            }

            page.Add(_smartSizing);

            page.Section("Colors");
            page.Row("Color depth:", _colors);

            page.Section("Full screen");
            page.Add(_bar);
            page.Add(_pinBar);
            page.Hint("Full screen can be switched any time with F11 or Alt → Connection → Full screen. Ctrl+Alt+Break leaves it.", indented: false);
            return page.Root;
        }

        private Control BuildResourcesPage()
        {
            var page = new Page();
            page.Section("Remote audio");
            page.Row("Playback:", _audio);
            page.Indented(_mic);

            page.Section("Keyboard");
            page.Row("Windows key combinations:", _keyboard);
            page.Hint("For example Alt+Tab, Win+R. \"On the remote computer\" also sends them while the tab is windowed.");

            page.Section("Local devices and resources");
            page.Add(_clipboard);
            page.Add(_printers);
            page.Add(_drives);
            page.Add(_smartCards);
            page.Add(_ports);
            return page.Root;
        }

        private Control BuildExperiencePage()
        {
            var page = new Page();
            page.Section("Performance");
            page.Row("Connection speed:", _speed);
            page.Hint("Choosing a speed sets the options below; changing an option switches to \"Custom\".");

            page.Section("Allow the following");
            foreach (var c in new[] { _wallpaper, _fontSmoothing, _composition, _drag, _animations, _themes, _cursor })
            {
                page.Add(c);
                c.CheckedChanged += (s, e) => { if (!_applyingPreset) MatchPreset(); };
            }
            _speed.SelectedIndexChanged += (s, e) => ApplyPreset(_speed.SelectedIndex);

            page.Section("Connection");
            page.Add(_autoReconnect);
            return page.Root;
        }

        private Control BuildAdvancedPage()
        {
            var page = new Page();
            page.Section("Authentication");
            page.Add(_nla);
            page.Row("If authentication fails:", _auth);
            page.Add(_prompt);
            page.Add(_admin);

            page.Section("RD Gateway");
            page.Row("Gateway server:", _gwHost);
            page.Hint("Leave empty to connect directly.");
            page.Row("Use the gateway:", _gwUsage);
            page.Row("Logon method:", _gwLogon);
            page.Indented(_gwSame);

            page.Section("Start a program on connection");
            page.Row("Program path:", _shell);
            page.Row("Start in folder:", _workDir);
            page.Hint("Leave empty for the normal desktop.");
            return page.Root;
        }

        private void FillCombos(IEnumerable<string> groups, IEnumerable<string> addresses)
        {
            var known = (addresses ?? Enumerable.Empty<string>()).Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _computer.Items.AddRange(known.Cast<object>().ToArray());
            _computer.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _computer.AutoCompleteSource = AutoCompleteSource.ListItems;

            _group.Items.AddRange((groups ?? Enumerable.Empty<string>()).Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g).Cast<object>().ToArray());

            _colors.Items.AddRange(new object[] { "Highest quality (32 bit)", "True color (24 bit)", "High color (16 bit)", "High color (15 bit)" });
            _audio.Items.AddRange(new object[] { "Play on this computer", "Play on the remote computer", "Do not play" });
            _keyboard.Items.AddRange(new object[] { "On this computer", "On the remote computer", "Only when using the full screen" });
            _speed.Items.AddRange(new object[]
            {
                "LAN (10 Mbps or higher) — best quality",
                "Broadband (high speed)",
                "Broadband (low speed)",
                "Slow link / modem",
                "Custom",
            });
            _auth.Items.AddRange(new object[] { "Warn me (recommended)", "Connect and don't warn me", "Do not connect" });
            _gwUsage.Items.AddRange(new object[] { "Always", "Bypass for local addresses" });
            _gwLogon.Items.AddRange(new object[] { "Ask for password (NTLM)", "Smart card", "Allow me to select later" });
        }

        // ---- experience presets ----------------------------------------------------------

        // wallpaper, font smoothing, composition, drag, animations, themes, cursor
        private static readonly bool[][] SpeedPresets =
        {
            new[] { true, true, true, true, true, true, true },
            new[] { false, true, true, true, true, true, true },
            new[] { false, false, false, false, false, true, true },
            new[] { false, false, false, false, false, false, false },
        };

        private CheckBox[] ExperienceBoxes => new[] { _wallpaper, _fontSmoothing, _composition, _drag, _animations, _themes, _cursor };

        private void ApplyPreset(int index)
        {
            if (index < 0 || index >= SpeedPresets.Length) return;
            _applyingPreset = true;
            var boxes = ExperienceBoxes;
            for (int i = 0; i < boxes.Length; i++) boxes[i].Checked = SpeedPresets[index][i];
            _applyingPreset = false;
        }

        private void MatchPreset()
        {
            var state = ExperienceBoxes.Select(b => b.Checked).ToArray();
            int match = Array.FindIndex(SpeedPresets, p => p.SequenceEqual(state));
            _applyingPreset = true;
            _speed.SelectedIndex = match >= 0 ? match : SpeedPresets.Length;
            _applyingPreset = false;
        }

        // ---- load / save -----------------------------------------------------------------

        private void Populate(ConnectionInfo info)
        {
            _computer.Text = info.Address ?? "";
            _user.Text = info.FullUserName ?? "";
            _password.Text = info.Password ?? "";
            _remember.Checked = info.SavePassword;
            _name.Text = info.Name ?? "";
            _group.Text = info.Group ?? "";
            _notes.Text = info.Notes ?? "";

            int w = info.Width > 0 ? info.Width : 1920, h = info.Height > 0 ? info.Height : 1080;
            _width.Value = Math.Max(_width.Minimum, Math.Min(_width.Maximum, w));
            _height.Value = Math.Max(_height.Minimum, Math.Min(_height.Maximum, h));
            if (info.FullScreen) _full.Checked = true;
            else if (info.Width > 0 && info.Height > 0) _fixed.Checked = true;
            else _fit.Checked = true;
            _smartSizing.Checked = info.SmartSizing;
            _smartSizing.Enabled = !info.FullScreen;
            _colors.SelectedIndex = Math.Max(0, Array.IndexOf(ColorDepths, info.ColorDepth));
            _bar.Checked = info.DisplayConnectionBar;
            _pinBar.Checked = info.PinConnectionBar;

            _audio.SelectedIndex = Math.Max(0, Math.Min(2, (int)info.Audio));
            _mic.Checked = info.AudioCapture;
            _keyboard.SelectedIndex = Math.Max(0, Math.Min(2, info.KeyboardHook));
            _clipboard.Checked = info.RedirectClipboard;
            _printers.Checked = info.RedirectPrinters;
            _drives.Checked = info.RedirectDrives;
            _smartCards.Checked = info.RedirectSmartCards;
            _ports.Checked = info.RedirectPorts;

            _applyingPreset = true;
            _wallpaper.Checked = !info.DisableWallpaper;
            _fontSmoothing.Checked = info.FontSmoothing;
            _composition.Checked = info.DesktopComposition;
            _drag.Checked = !info.DisableFullWindowDrag;
            _animations.Checked = !info.DisableMenuAnimations;
            _themes.Checked = !info.DisableThemes;
            _cursor.Checked = !info.DisableCursorSettings;
            _applyingPreset = false;
            MatchPreset();
            _autoReconnect.Checked = info.AutoReconnect;

            _nla.Checked = info.UseNla;
            _auth.SelectedIndex = Math.Max(0, Array.IndexOf(AuthLevels, info.AuthenticationLevel));
            _prompt.Checked = info.PromptForCredentials;
            _admin.Checked = info.AdminSession;
            bool gateway = !string.IsNullOrWhiteSpace(info.GatewayHost) && info.GatewayUsage != 0 && info.GatewayUsage != 4;
            _gwHost.Text = gateway ? info.GatewayHost : "";
            _gwUsage.SelectedIndex = Math.Max(0, Array.IndexOf(GatewayUsages, info.GatewayUsage));
            _gwLogon.SelectedIndex = Math.Max(0, Array.IndexOf(GatewayLogons, info.GatewayCredentialsSource));
            _gwSame.Checked = info.GatewayUseSameCredentials;
            _shell.Text = info.AlternateShell ?? "";
            _workDir.Text = info.WorkingDirectory ?? "";
        }

        private bool Collect(out ConnectionInfo info, bool requireHost)
        {
            info = _original.Clone();

            if (_mode != ConnectionDialogMode.Defaults)
            {
                if (ConnectionInfo.TryParseAddress(_computer.Text, out var host, out var port))
                {
                    info.Host = host;
                    info.Port = port;
                }
                else if (requireHost)
                {
                    _nav.SelectedIndex = 0;
                    MessageBox.Show(this, "Enter a computer name or IP address.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _computer.Focus();
                    return false;
                }
                info.SetFullUserName(_user.Text);
                info.Password = _password.Text;
                info.SavePassword = _remember.Checked && _password.Text.Length > 0;
                info.Name = _name.Text.Trim();
                info.Group = _group.Text.Trim();
                info.Notes = _notes.Text;
            }

            info.FullScreen = _full.Checked;
            info.Width = _fixed.Checked ? (int)_width.Value : 0;
            info.Height = _fixed.Checked ? (int)_height.Value : 0;
            info.SmartSizing = _smartSizing.Checked;
            info.ColorDepth = ColorDepths[Math.Max(0, _colors.SelectedIndex)];
            info.DisplayConnectionBar = _bar.Checked;
            info.PinConnectionBar = _pinBar.Checked;

            info.Audio = (AudioMode)Math.Max(0, _audio.SelectedIndex);
            info.AudioCapture = _mic.Checked;
            info.KeyboardHook = Math.Max(0, _keyboard.SelectedIndex);
            info.RedirectClipboard = _clipboard.Checked;
            info.RedirectPrinters = _printers.Checked;
            info.RedirectDrives = _drives.Checked;
            info.RedirectSmartCards = _smartCards.Checked;
            info.RedirectPorts = _ports.Checked;

            info.DisableWallpaper = !_wallpaper.Checked;
            info.FontSmoothing = _fontSmoothing.Checked;
            info.DesktopComposition = _composition.Checked;
            info.DisableFullWindowDrag = !_drag.Checked;
            info.DisableMenuAnimations = !_animations.Checked;
            info.DisableThemes = !_themes.Checked;
            info.DisableCursorSettings = !_cursor.Checked;
            info.AutoReconnect = _autoReconnect.Checked;

            info.UseNla = _nla.Checked;
            info.AuthenticationLevel = AuthLevels[Math.Max(0, _auth.SelectedIndex)];
            info.PromptForCredentials = _prompt.Checked;
            info.AdminSession = _admin.Checked;
            info.GatewayHost = _gwHost.Text.Trim();
            info.GatewayUsage = info.GatewayHost.Length == 0 ? 0 : GatewayUsages[Math.Max(0, _gwUsage.SelectedIndex)];
            info.GatewayCredentialsSource = GatewayLogons[Math.Max(0, _gwLogon.SelectedIndex)];
            info.GatewayUseSameCredentials = _gwSame.Checked;
            info.AlternateShell = _shell.Text.Trim();
            info.WorkingDirectory = _workDir.Text.Trim();
            return true;
        }

        private void Accept()
        {
            if (!Collect(out var info, requireHost: _mode != ConnectionDialogMode.Defaults)) return;
            Result = info;
            DialogResult = DialogResult.OK;
        }
    }
}
