using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TabbedRdp
{
    public sealed class MainForm : Form
    {
        private readonly AppState _state;
        private readonly LaunchParser.Result _startup;

        private readonly ToolStripComboBox _address = new ToolStripComboBox { AutoSize = false, Width = 260, FlatStyle = FlatStyle.System };
        private readonly ToolStripButton _fullScreenButton = new ToolStripButton("Full screen") { ToolTipText = "Full screen (F11) — leave with the connection bar or Ctrl+Alt+Break" };
        private readonly ToolStripButton _reconnectButton = new ToolStripButton("Reconnect");
        private readonly ToolStripButton _disconnectButton = new ToolStripButton("Disconnect");
        private readonly ToolStripButton _sidebarButton = new ToolStripButton("Connections") { CheckOnClick = true, Alignment = ToolStripItemAlignment.Right };

        private readonly SplitContainer _split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        private readonly TreeView _tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true, BorderStyle = BorderStyle.None, FullRowSelect = true, ShowLines = false };
        private readonly TextBox _filter = new TextBox { Dock = DockStyle.Top };
        private readonly SessionTabControl _tabs = new SessionTabControl { Dock = DockStyle.Fill };
        private readonly Label _empty = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
            Text = "Type a computer name above and press Enter,\r\ndouble-click a saved connection, or press Ctrl+N.\r\n\r\n"
                 + "Ctrl+Tab / Ctrl+Shift+Tab  switch tabs   •   Ctrl+W  close tab   •   F11  full screen",
        };
        private readonly ToolStripStatusLabel _status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ContextMenuStrip _tabMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip _treeMenu = new ContextMenuStrip();

        private bool _exiting;
        private Timer _exitTimeout;

        public MainForm(LaunchParser.Result startup)
        {
            _state = AppState.Load();
            _startup = startup;

            Text = "Tabbed RDP";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = SystemFonts.MessageBoxFont;
            KeyPreview = true;
            MinimumSize = new Size(640, 420);
            RestoreWindow();

            BuildSidebar();
            BuildTabs();

            var statusStrip = new StatusStrip();
            statusStrip.Items.Add(_status);

            Controls.Add(_split);
            Controls.Add(statusStrip);
            Controls.Add(BuildToolbar());

            _sidebarButton.Checked = _state.SidebarVisible;
            _split.Panel1Collapsed = !_state.SidebarVisible;

            Load += (s, e) => _split.SplitterDistance = Math.Max(120, _state.SidebarWidth);
            Shown += (s, e) =>
            {
                _address.Focus();
                ShowLaunchErrors(_startup.Errors);
                OpenRequest(_startup.Request);
            };
            UpdateUi();
        }

        // ---- layout ----------------------------------------------------------------------

        private ToolStrip _toolbar;

        private ToolStrip BuildToolbar()
        {
            if (_toolbar != null) return _toolbar;

            _toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 3, 6, 3), Stretch = true };
            var connect = new ToolStripButton("Connect") { Font = new Font(Font, FontStyle.Bold) };
            connect.Click += (s, e) => QuickConnect();
            _address.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; QuickConnect(); }
            };
            _address.ToolTipText = "Computer name or IP, optionally with :port (Ctrl+L)";
            RefreshAddressList();

            var newButton = new ToolStripButton("New…") { ToolTipText = "New connection with all options (Ctrl+N)" };
            newButton.Click += (s, e) => NewConnection();
            var openButton = new ToolStripButton("Open .rdp…") { ToolTipText = "Open an .rdp file (Ctrl+O)" };
            openButton.Click += (s, e) => OpenRdpFile(saveToList: false);

            _fullScreenButton.Click += (s, e) => CurrentSession?.ToggleFullScreen();
            _reconnectButton.Click += (s, e) => CurrentSession?.Connect();
            _disconnectButton.Click += (s, e) => CurrentSession?.Disconnect();
            _sidebarButton.CheckedChanged += (s, e) =>
            {
                _split.Panel1Collapsed = !_sidebarButton.Checked;
                _state.SidebarVisible = _sidebarButton.Checked;
            };

            _toolbar.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripLabel("Computer:"), _address, connect,
                new ToolStripSeparator(), newButton, openButton,
                new ToolStripSeparator(), _reconnectButton, _disconnectButton, _fullScreenButton,
                _sidebarButton,
            });
            return _toolbar;
        }

        private void BuildSidebar()
        {
            var header = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, RenderMode = ToolStripRenderMode.System };
            var add = new ToolStripButton("Add") { ToolTipText = "Add a saved connection" };
            add.Click += (s, e) => AddSavedConnection(new ConnectionInfo());
            var edit = new ToolStripButton("Edit");
            edit.Click += (s, e) => { if (SelectedSaved is ConnectionInfo c) EditSavedConnection(c); };
            var delete = new ToolStripButton("Delete");
            delete.Click += (s, e) => { if (SelectedSaved is ConnectionInfo c) DeleteSavedConnection(c); };
            var import = new ToolStripButton("Import .rdp") { Alignment = ToolStripItemAlignment.Right };
            import.Click += (s, e) => OpenRdpFile(saveToList: true);
            header.Items.AddRange(new ToolStripItem[] { add, edit, delete, import });

            SetCue(_filter, "Search connections…");
            _filter.TextChanged += (s, e) => RefreshTree();
            _filter.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down) { _tree.Focus(); e.Handled = true; }
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ConnectFirstMatch(); }
            };

            _tree.NodeMouseDoubleClick += (s, e) => { if (e.Node.Tag is ConnectionInfo c) OpenSession(c); };
            _tree.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && SelectedSaved is ConnectionInfo c) { e.SuppressKeyPress = true; OpenSession(c); }
                if (e.KeyCode == Keys.Delete && SelectedSaved is ConnectionInfo d) DeleteSavedConnection(d);
                if (e.KeyCode == Keys.F2 && SelectedSaved is ConnectionInfo f) EditSavedConnection(f);
            };
            _tree.NodeMouseClick += (s, e) => { if (e.Button == MouseButtons.Right) _tree.SelectedNode = e.Node; };

            _treeMenu.Opening += (s, e) => BuildTreeMenu();
            _tree.ContextMenuStrip = _treeMenu;

            _split.Panel1.Controls.Add(_tree);
            _split.Panel1.Controls.Add(_filter);
            _split.Panel1.Controls.Add(header);
            _split.SplitterMoved += (s, e) => { if (!_split.Panel1Collapsed) _state.SidebarWidth = _split.SplitterDistance; };
            RefreshTree();
        }

        private void BuildTabs()
        {
            _tabs.CloseClicked += (s, page) => CloseTab(page);
            _tabs.TabRightClicked += (s, page) => { BuildTabMenu(); _tabMenu.Show(Cursor.Position); };
            _tabs.SelectedIndexChanged += (s, e) =>
            {
                UpdateUi();
                CurrentSession?.FocusRemote();
            };
            _split.Panel2.Controls.Add(_tabs);
            _split.Panel2.Controls.Add(_empty);
        }

        // ---- sessions --------------------------------------------------------------------

        private RdpSession CurrentSession => _tabs.SelectedTab?.Tag as RdpSession;

        private IEnumerable<RdpSession> Sessions => _tabs.TabPages.Cast<TabPage>().Select(p => p.Tag).OfType<RdpSession>();

        private void OpenSession(ConnectionInfo info)
        {
            if (_exiting) return;
            var session = new RdpSession(info.Clone());
            SavedCredentials.Apply(session.Info);
            var page = new TabPage(session.Info.DisplayName)
            {
                Tag = session,
                ToolTipText = session.Info.Address + (string.IsNullOrWhiteSpace(session.Info.UserName) ? "" : $" ({session.Info.FullUserName})"),
            };
            page.Controls.Add(session);
            session.StateChanged += (s, e) =>
            {
                _tabs.RefreshTabs();
                if (CurrentSession == session) UpdateUi();
            };
            session.CloseRequested += (s, e) => RemoveTab(page);

            _tabs.TabPages.Add(page);
            _tabs.SelectedTab = page;
            UpdateUi();

            _state.AddRecent(session.Info.Address);
            RefreshAddressList();
            SaveState();

            // Let the tab lay out first so "fit to window" gets the real size.
            BeginInvoke((Action)session.Connect);
        }

        private void CloseTab(TabPage page) => (page?.Tag as RdpSession)?.RequestClose();

        private void RemoveTab(TabPage page)
        {
            if (!_tabs.TabPages.Contains(page)) return;
            int index = _tabs.TabPages.IndexOf(page);
            _tabs.TabPages.Remove(page);
            (page.Tag as RdpSession)?.Dispose();
            page.Dispose();
            if (_tabs.TabPages.Count > 0) _tabs.SelectedIndex = Math.Min(index, _tabs.TabPages.Count - 1);
            UpdateUi();
            if (_exiting && _tabs.TabPages.Count == 0) BeginInvoke((Action)Close);
        }

        private void QuickConnect()
        {
            string text = _address.Text.Trim();
            if (text.StartsWith("/v:", StringComparison.OrdinalIgnoreCase)) text = text.Substring(3);
            if (!ConnectionInfo.TryParseAddress(text, out var host, out var port))
            {
                _address.Focus();
                return;
            }

            var saved = _state.FindByAddress(host, port)
                ?? _state.Connections.FirstOrDefault(c => string.Equals(c.Name, text, StringComparison.OrdinalIgnoreCase));
            if (saved != null)
            {
                OpenSession(saved);
                return;
            }

            using (var dialog = new CredentialDialog(port == 3389 ? host : $"{host}:{port}", _state.LastUserName))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var info = new ConnectionInfo { Host = host, Port = port, Password = dialog.Password };
                info.SetFullUserName(dialog.UserName);
                if (!string.IsNullOrWhiteSpace(dialog.UserName)) _state.LastUserName = dialog.UserName;

                if (dialog.SaveConnection)
                {
                    info.SavePassword = !string.IsNullOrEmpty(info.Password);
                    _state.Connections.Add(info.Clone());
                    RefreshTree();
                }
                OpenSession(info);
            }
        }

        private void NewConnection()
        {
            var seed = new ConnectionInfo();
            if (ConnectionInfo.TryParseAddress(_address.Text, out var host, out var port))
            {
                seed.Host = host;
                seed.Port = port;
            }
            if (!string.IsNullOrWhiteSpace(_state.LastUserName)) seed.SetFullUserName(_state.LastUserName);

            using (var dialog = new ConnectionDialog(seed, Groups(), "Connect", showSaveToList: true))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (dialog.SaveToList)
                {
                    _state.Connections.Add(dialog.Result.Clone());
                    RefreshTree();
                }
                if (!string.IsNullOrWhiteSpace(dialog.Result.UserName)) _state.LastUserName = dialog.Result.FullUserName;
                OpenSession(dialog.Result);
            }
        }

        private void OpenRdpFile(bool saveToList)
        {
            using (var dialog = new OpenFileDialog { Filter = "Remote Desktop files (*.rdp)|*.rdp|All files (*.*)|*.*", Multiselect = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (var file in dialog.FileNames)
                {
                    try
                    {
                        if (saveToList)
                        {
                            _state.Connections.Add(RdpFile.Load(file));
                            continue;
                        }
                        var launch = LaunchParser.Parse(new[] { file });
                        if (launch.DelegateReason != null) LaunchParser.RunMstsc(new[] { file });
                        else OpenRequest(launch.Request);
                        ShowLaunchErrors(launch.Errors);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"{Path.GetFileName(file)}: {ex.Message}", "Open .rdp file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                if (saveToList)
                {
                    RefreshTree();
                    SaveState();
                }
            }
        }

        /// <summary>Opens what a launch (command line, .rdp file, or another instance) asked for.</summary>
        public void OpenRequest(LaunchRequest request)
        {
            foreach (var info in request.Resolve())
            {
                var toOpen = info;
                // "/v:host" alone: use the saved connection for that host if there is one.
                if (string.IsNullOrWhiteSpace(info.UserName) && string.IsNullOrEmpty(info.Password) &&
                    _state.FindByAddress(info.Host, info.Port) is ConnectionInfo saved)
                {
                    toOpen = saved.Clone();
                    toOpen.FullScreen |= info.FullScreen;
                    toOpen.AdminSession |= info.AdminSession;
                }
                Log.Write($"  open tab: {toOpen.Address} user={toOpen.FullUserName} password={(string.IsNullOrEmpty(toOpen.Password) ? "no" : "yes")} " +
                          $"size={(toOpen.Width > 0 ? $"{toOpen.Width}x{toOpen.Height}" : "fit")} full={toOpen.FullScreen} admin={toOpen.AdminSession}");
                OpenSession(toOpen);
            }

            // Bring the window forward when a connector adds a tab.
            if (WindowState == FormWindowState.Minimized) WindowState = _state.WindowMaximized ? FormWindowState.Maximized : FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void ShowLaunchErrors(List<string> errors)
        {
            if (errors.Count > 0)
                MessageBox.Show(this, string.Join("\n", errors), "Tabbed RDP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ---- saved connections -----------------------------------------------------------

        private ConnectionInfo SelectedSaved => _tree.SelectedNode?.Tag as ConnectionInfo;

        private IEnumerable<string> Groups() => _state.Connections.Select(c => c.Group);

        private void AddSavedConnection(ConnectionInfo seed)
        {
            if (_tree.SelectedNode?.Tag is string group && string.IsNullOrEmpty(seed.Group)) seed.Group = group;
            using (var dialog = new ConnectionDialog(seed, Groups(), "Save", showSaveToList: false))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _state.Connections.Add(dialog.Result);
                RefreshTree(dialog.Result);
                SaveState();
            }
        }

        private void EditSavedConnection(ConnectionInfo info)
        {
            using (var dialog = new ConnectionDialog(info, Groups(), "Save", showSaveToList: false))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                int index = _state.Connections.IndexOf(info);
                if (index >= 0) _state.Connections[index] = dialog.Result;
                RefreshTree(dialog.Result);
                SaveState();
            }
        }

        private void DeleteSavedConnection(ConnectionInfo info)
        {
            if (MessageBox.Show(this, $"Delete \"{info.DisplayName}\"?", "Delete connection",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            _state.Connections.Remove(info);
            RefreshTree();
            SaveState();
        }

        private void ConnectFirstMatch()
        {
            var node = _tree.Nodes.Cast<TreeNode>().SelectMany(g => g.Nodes.Cast<TreeNode>()).FirstOrDefault();
            if (node?.Tag is ConnectionInfo c) OpenSession(c);
        }

        private void RefreshTree(ConnectionInfo select = null)
        {
            string filter = _filter.Text.Trim();
            bool Matches(ConnectionInfo c) => filter.Length == 0 || new[] { c.Name, c.Host, c.Group, c.UserName, c.Notes }
                .Any(v => v != null && v.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);

            var collapsed = new HashSet<string>(_tree.Nodes.Cast<TreeNode>().Where(n => !n.IsExpanded).Select(n => n.Text));

            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            TreeNode toSelect = null;

            var groups = _state.Connections.Where(Matches)
                .GroupBy(c => string.IsNullOrWhiteSpace(c.Group) ? "" : c.Group.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key.Length == 0 ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                string title = group.Key.Length == 0 ? "Ungrouped" : group.Key;
                var groupNode = new TreeNode($"{title} ({group.Count()})") { Tag = group.Key, NodeFont = new Font(Font, FontStyle.Bold) };
                foreach (var c in group.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase))
                {
                    var node = new TreeNode(c.DisplayName)
                    {
                        Tag = c,
                        ToolTipText = c.Address
                            + (string.IsNullOrWhiteSpace(c.UserName) ? "" : $"\n{c.FullUserName}")
                            + (string.IsNullOrWhiteSpace(c.Notes) ? "" : $"\n\n{c.Notes}"),
                    };
                    groupNode.Nodes.Add(node);
                    if (c == select) toSelect = node;
                }
                _tree.Nodes.Add(groupNode);
                if (filter.Length > 0 || !collapsed.Contains(groupNode.Text)) groupNode.Expand();
            }
            _tree.EndUpdate();
            if (toSelect != null) _tree.SelectedNode = toSelect;
            RefreshAddressList();
        }

        private void RefreshAddressList()
        {
            string current = _address.Text;
            _address.Items.Clear();
            foreach (var a in _state.RecentAddresses) _address.Items.Add(a);
            _address.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _address.AutoCompleteSource = AutoCompleteSource.CustomSource;
            var source = new AutoCompleteStringCollection();
            source.AddRange(_state.RecentAddresses.Concat(_state.Connections.Select(c => c.Address)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            _address.AutoCompleteCustomSource = source;
            _address.Text = current;
        }

        // ---- menus -----------------------------------------------------------------------

        private void BuildTabMenu()
        {
            _tabMenu.Items.Clear();
            var session = CurrentSession;
            var page = _tabs.SelectedTab;
            if (session == null) return;

            bool connected = session.State == SessionState.Connected;
            _tabMenu.Items.Add("Reconnect", null, (s, e) => session.Connect());
            _tabMenu.Items.Add("Disconnect", null, (s, e) => session.Disconnect()).Enabled = session.IsActive;
            _tabMenu.Items.Add("Full screen\tF11", null, (s, e) => session.ToggleFullScreen()).Enabled = connected;
            var smart = new ToolStripMenuItem("Smart sizing") { Checked = session.SmartSizing };
            smart.Click += (s, e) => session.SmartSizing = !session.SmartSizing;
            _tabMenu.Items.Add(smart);
            _tabMenu.Items.Add(new ToolStripSeparator());
            _tabMenu.Items.Add("Duplicate tab", null, (s, e) => OpenSession(session.Info));
            if (!_state.Connections.Any(c => c.Id == session.Info.Id))
            {
                _tabMenu.Items.Add("Save to connections…", null, (s, e) =>
                {
                    AddSavedConnection(session.Info.Clone(newId: true));
                });
            }
            _tabMenu.Items.Add("Copy address", null, (s, e) => Clipboard.SetText(session.Info.Address));
            _tabMenu.Items.Add(new ToolStripSeparator());
            _tabMenu.Items.Add("Close tab\tCtrl+W", null, (s, e) => CloseTab(page));
            _tabMenu.Items.Add("Close other tabs", null, (s, e) =>
            {
                foreach (var other in _tabs.TabPages.Cast<TabPage>().Where(p => p != page).ToList()) CloseTab(other);
            }).Enabled = _tabs.TabPages.Count > 1;
        }

        private void BuildTreeMenu()
        {
            _treeMenu.Items.Clear();
            if (SelectedSaved is ConnectionInfo c)
            {
                var connect = new ToolStripMenuItem("Connect", null, (s, e) => OpenSession(c)) { Font = new Font(_treeMenu.Font, FontStyle.Bold) };
                _treeMenu.Items.Add(connect);
                _treeMenu.Items.Add("Edit…\tF2", null, (s, e) => EditSavedConnection(c));
                _treeMenu.Items.Add("Duplicate", null, (s, e) =>
                {
                    var copy = c.Clone(newId: true);
                    copy.Name = c.DisplayName + " (copy)";
                    AddSavedConnection(copy);
                });
                _treeMenu.Items.Add("Copy address", null, (s, e) => Clipboard.SetText(c.Address));
                _treeMenu.Items.Add("Delete\tDel", null, (s, e) => DeleteSavedConnection(c));
                _treeMenu.Items.Add(new ToolStripSeparator());
            }
            else if (_tree.SelectedNode?.Tag is string group)
            {
                var members = _state.Connections.Where(x => string.Equals((x.Group ?? "").Trim(), group, StringComparison.OrdinalIgnoreCase)).ToList();
                _treeMenu.Items.Add($"Connect all ({members.Count})", null, (s, e) => members.ForEach(OpenSession));
                _treeMenu.Items.Add(new ToolStripSeparator());
            }
            _treeMenu.Items.Add("Add connection…", null, (s, e) => AddSavedConnection(new ConnectionInfo()));
            _treeMenu.Items.Add("Import .rdp files…", null, (s, e) => OpenRdpFile(saveToList: true));
        }

        // ---- keyboard --------------------------------------------------------------------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Note: while the remote desktop has focus, keystrokes go to the remote machine.
            switch (keyData)
            {
                case Keys.Control | Keys.N: NewConnection(); return true;
                case Keys.Control | Keys.O: OpenRdpFile(saveToList: false); return true;
                case Keys.Control | Keys.W: CloseTab(_tabs.SelectedTab); return true;
                case Keys.Control | Keys.L: _address.Focus(); _address.SelectAll(); return true;
                case Keys.Control | Keys.F: _sidebarButton.Checked = true; _filter.Focus(); _filter.SelectAll(); return true;
                case Keys.F11: CurrentSession?.ToggleFullScreen(); return true;
                case Keys.F5: CurrentSession?.Connect(); return true;
                case Keys.Control | Keys.Tab: CycleTabs(1); return true;
                case Keys.Control | Keys.Shift | Keys.Tab: CycleTabs(-1); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CycleTabs(int delta)
        {
            int count = _tabs.TabPages.Count;
            if (count < 2) return;
            _tabs.SelectedIndex = ((_tabs.SelectedIndex + delta) % count + count) % count;
        }

        // ---- state / UI ------------------------------------------------------------------

        private void UpdateUi()
        {
            bool any = _tabs.TabPages.Count > 0;
            _tabs.Visible = any;
            _empty.Visible = !any;

            var session = CurrentSession;
            _reconnectButton.Enabled = session != null;
            _disconnectButton.Enabled = session?.IsActive == true;
            _fullScreenButton.Enabled = session?.State == SessionState.Connected;
            _status.Text = session?.StatusText ?? $"Ready — {_state.Connections.Count} saved connection(s). Engine: {RdpEngineName()}";
            Text = session == null ? "Tabbed RDP" : $"{session.Info.DisplayName} - Tabbed RDP";
        }

        private static string RdpEngineName()
        {
            if (RdpClientHost.ClassName != "unknown") return RdpClientHost.ClassName;
            try { return File.Exists(Path.Combine(Environment.SystemDirectory, "mstscax.dll")) ? "mstscax.dll" : "mstscax.dll not found!"; }
            catch { return "?"; }
        }

        private void SaveState()
        {
            try { _state.Save(); }
            catch (Exception ex) { _status.Text = "Could not save settings: " + ex.Message; }
        }

        private void RestoreWindow()
        {
            StartPosition = FormStartPosition.WindowsDefaultLocation;
            Size = new Size(1280, 860);
            if (_state.WindowWidth > 300 && _state.WindowHeight > 200)
            {
                var bounds = new Rectangle(_state.WindowX, _state.WindowY, _state.WindowWidth, _state.WindowHeight);
                if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = bounds;
                }
            }
            if (_state.WindowMaximized) WindowState = FormWindowState.Maximized;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var active = Sessions.Where(s => s.IsActive).ToList();
            if (!_exiting && active.Count > 0 && e.CloseReason == CloseReason.UserClosing)
            {
                var answer = MessageBox.Show(this,
                    $"{active.Count} remote session(s) are still connected.\n\nDisconnect all and exit?",
                    "Tabbed RDP", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                e.Cancel = true;
                if (answer != DialogResult.Yes) return;

                // Disconnect cleanly; RemoveTab() closes the form once the last tab is gone.
                _exiting = true;
                _exitTimeout = new Timer { Interval = 6000 };
                _exitTimeout.Tick += (s, a) => { _exitTimeout.Stop(); Close(); };
                _exitTimeout.Start();
                foreach (var page in _tabs.TabPages.Cast<TabPage>().ToList()) CloseTab(page);
                return;
            }

            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _state.WindowX = bounds.X;
            _state.WindowY = bounds.Y;
            _state.WindowWidth = bounds.Width;
            _state.WindowHeight = bounds.Height;
            _state.WindowMaximized = WindowState == FormWindowState.Maximized;
            SaveState();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            foreach (var session in Sessions.ToList())
            {
                try { session.Dispose(); } catch { }
            }
            base.OnFormClosed(e);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void SetCue(TextBox box, string cue) =>
            box.HandleCreated += (s, e) => SendMessage(box.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, cue);
    }
}
