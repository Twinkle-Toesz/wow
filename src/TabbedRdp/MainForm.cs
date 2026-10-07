using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TabbedRdp
{
    public sealed class MainForm : Form
    {
        private readonly AppState _state;
        private readonly LaunchParser.Result _startup;
        private readonly bool _customChrome;

        private readonly TabStripBar _strip = new TabStripBar();
        private readonly SplitContainer _split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 3 };
        private readonly Panel _content = new Panel { Dock = DockStyle.Fill, Tag = "keepBack" };
        private readonly Panel _welcome = new Panel { Dock = DockStyle.Fill, Tag = "keepBack" };
        private readonly TreeView _tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true, BorderStyle = BorderStyle.None, FullRowSelect = true, ShowLines = false, ItemHeight = 24 };
        private readonly TextBox _filter = new TextBox { Dock = DockStyle.Top };
        private readonly MenuStrip _menu = new MenuStrip { Dock = DockStyle.None, Visible = false, AutoSize = false, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 0, 2) };
        private readonly ContextMenuStrip _tabMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip _treeMenu = new ContextMenuStrip();

        private AltKeyWatcher _altWatcher;
        private AltMessageFilter _altFilter;
        private int _menuClosedAt;
        private bool _exiting;
        private Timer _exitTimeout;

        public MainForm(LaunchParser.Result startup, AppState state)
        {
            _state = state;
            _startup = startup;
            _customChrome = !_state.StandardTitleBar;

            Text = "Tabbed RDP";
            Icon = AppIcon.Create();
            Font = SystemFonts.MessageBoxFont;
            KeyPreview = true;
            MinimumSize = new Size(640, 420);
            RestoreWindow();

            BuildSidebar();
            BuildContent();
            BuildMenu();
            BuildStrip();

            Controls.Add(_split);
            Controls.Add(_strip);
            Controls.Add(_menu);
            _split.Panel1Collapsed = !_state.ShowSidebar;

            Theme.Changed += OnThemeChanged;
            ApplyTheme();
            RefreshWelcome();
            ShowSelected();

            Load += (s, e) =>
            {
                try { _split.SplitterDistance = Math.Max(140, _state.SidebarWidth); } catch { }
            };
            Shown += (s, e) =>
            {
                InstallAltHandling();
                ShowLaunchErrors(_startup.Errors);
                OpenRequest(_startup.Request);
            };
        }

        // ---- layout ----------------------------------------------------------------------

        private void BuildStrip()
        {
            _strip.CaptionButtons = _customChrome;
            _strip.SelectedChanged += (s, e) => ShowSelected();
            _strip.NewTabClicked += (s, e) => NewConnection();
            _strip.MenuClicked += (s, e) => ToggleMenu();
            _strip.CloseTabClicked += (s, session) => session.RequestClose();
            _strip.TabRightClicked += (s, session) =>
            {
                BuildTabMenu(session);
                _tabMenu.Show(Cursor.Position);
            };
            _strip.MinimizeClicked += (s, e) => WindowState = FormWindowState.Minimized;
            _strip.MaximizeClicked += (s, e) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            _strip.CloseWindowClicked += (s, e) => Close();
        }

        private void BuildContent()
        {
            _content.Controls.Add(_welcome);
            _split.Panel2.Controls.Add(_content);
            _welcome.Resize += (s, e) => CenterWelcome();
        }

        private void BuildSidebar()
        {
            var header = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, Padding = new Padding(4, 2, 4, 2) };
            var add = new ToolStripButton("Add") { ToolTipText = "Add a saved connection" };
            add.Click += (s, e) => AddSavedConnection(_state.Defaults.NewFromTemplate());
            var edit = new ToolStripButton("Edit");
            edit.Click += (s, e) => { if (SelectedSaved is ConnectionInfo c) EditSavedConnection(c); };
            var delete = new ToolStripButton("Delete");
            delete.Click += (s, e) => { if (SelectedSaved is ConnectionInfo c) DeleteSavedConnection(c); };
            var hide = new ToolStripButton("✕") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Hide (Ctrl+B)" };
            hide.Click += (s, e) => SetSidebar(false);
            header.Items.AddRange(new ToolStripItem[] { add, edit, delete, hide });

            SetCue(_filter, "Search connections… (Ctrl+F)");
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

        private void SetSidebar(bool visible)
        {
            _split.Panel1Collapsed = !visible;
            _state.ShowSidebar = visible;
            SaveState();
        }

        // ---- welcome page (no tabs open) -------------------------------------------------

        private Panel _welcomeCard;

        private void RefreshWelcome()
        {
            _welcome.SuspendLayout();
            _welcome.Controls.Clear();

            var card = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Tag = "keepBack", BackColor = Color.Transparent };
            card.Controls.Add(new Label { Text = "Tabbed RDP", AutoSize = true, Font = new Font(Font.FontFamily, 22f), Margin = new Padding(0, 0, 0, 2) });
            card.Controls.Add(new Label { Text = "Remote Desktop with tabs", AutoSize = true, Tag = "muted", Margin = new Padding(2, 0, 0, 18) });

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Tag = "keepBack", BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 18) };
            var newButton = new Button { Text = "+  New connection", Tag = "primary", AutoSize = true, MinimumSize = new Size(170, 36), Margin = new Padding(0, 0, 10, 0) };
            newButton.Click += (s, e) => NewConnection();
            var openButton = new Button { Text = "Open .rdp file…", AutoSize = true, MinimumSize = new Size(140, 36) };
            openButton.Click += (s, e) => OpenRdpFile();
            buttons.Controls.Add(newButton);
            buttons.Controls.Add(openButton);
            card.Controls.Add(buttons);

            var recent = _state.RecentAddresses.Take(8).ToList();
            if (recent.Count > 0)
            {
                card.Controls.Add(new Label { Text = "Recent", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(2, 0, 0, 6) });
                foreach (var address in recent)
                {
                    ConnectionInfo.TryParseAddress(address, out var host, out var port);
                    var saved = _state.FindByAddress(host, port);
                    var link = new LinkLabel
                    {
                        Text = saved != null && saved.DisplayName != address ? $"{saved.DisplayName}  ({address})" : address,
                        AutoSize = true,
                        LinkBehavior = LinkBehavior.HoverUnderline,
                        Margin = new Padding(2, 2, 0, 2),
                    };
                    link.LinkClicked += (s, e) =>
                    {
                        if (saved != null) OpenSession(saved);
                        else NewConnection(address);
                    };
                    card.Controls.Add(link);
                }
            }

            card.Controls.Add(new Label
            {
                Text = "Tap Alt for the menu   •   Ctrl+T new tab   •   Ctrl+Tab switch tabs   •   Ctrl+W close tab   •   F11 full screen",
                AutoSize = true,
                Tag = "muted",
                Margin = new Padding(2, 22, 0, 0),
            });

            _welcomeCard = card;
            _welcome.Controls.Add(card);
            Theme.Apply(card);
            _welcome.ResumeLayout(true);
            CenterWelcome();
        }

        private void CenterWelcome()
        {
            if (_welcomeCard == null) return;
            _welcomeCard.Location = new Point(
                Math.Max(16, (_welcome.ClientSize.Width - _welcomeCard.Width) / 2),
                Math.Max(16, (_welcome.ClientSize.Height - _welcomeCard.Height) / 2 - 30));
        }

        // ---- sessions --------------------------------------------------------------------

        private RdpSession CurrentSession => _strip.Selected;

        private IEnumerable<RdpSession> Sessions => _strip.Tabs;

        private void ShowSelected()
        {
            var selected = _strip.Selected;
            if (selected != null)
            {
                selected.Visible = true;
                selected.BringToFront();
            }
            foreach (var s in _strip.Tabs)
                if (s != selected) s.Visible = false;
            _welcome.Visible = selected == null;
            if (selected == null) RefreshWelcome();
            UpdateTitle();
            selected?.FocusRemote();
        }

        private void OpenSession(ConnectionInfo info)
        {
            if (_exiting) return;
            var session = new RdpSession(info.Clone());
            SavedCredentials.Apply(session.Info);
            session.StateChanged += (s, e) =>
            {
                _strip.RefreshTabs();
                if (_strip.Selected == session) UpdateTitle();
            };
            session.CloseRequested += (s, e) => RemoveSession(session);

            _content.Controls.Add(session);
            _strip.Add(session);
            _strip.Selected = session;
            _content.PerformLayout();

            _state.AddRecent(session.Info.Address);
            SaveState();

            // Connect while the tab is visible so the control gets a window and "fit to window" the real size.
            session.Connect();
        }

        private void RemoveSession(RdpSession session)
        {
            if (!_strip.Tabs.Contains(session)) return;
            _strip.Remove(session);
            _content.Controls.Remove(session);
            session.Dispose();
            ShowSelected();
            if (_exiting && _strip.Tabs.Count == 0) BeginInvoke((Action)Close);
        }

        private void NewConnection(string address = null)
        {
            var seed = _state.Defaults.NewFromTemplate();
            if (ConnectionInfo.TryParseAddress(address, out var host, out var port))
            {
                seed.Host = host;
                seed.Port = port;
            }
            if (!string.IsNullOrWhiteSpace(_state.LastUserName)) seed.SetFullUserName(_state.LastUserName);

            using (var dialog = new ConnectionDialog(seed, ConnectionDialogMode.NewConnection, Groups(), Addresses(), SaveDefaults))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var result = dialog.Result;
                if (dialog.SaveToList)
                {
                    _state.Connections.Add(result.Clone());
                    RefreshTree();
                }
                if (!string.IsNullOrWhiteSpace(result.UserName)) _state.LastUserName = result.FullUserName;
                OpenSession(result);
            }
        }

        private void ShowTabOptions(RdpSession session)
        {
            if (session == null) { ShowDefaults(); return; }
            var saved = _state.Connections.FirstOrDefault(c => c.Id == session.Info.Id);
            using (var dialog = new ConnectionDialog(session.Info, ConnectionDialogMode.TabOptions, Groups(), Addresses(), SaveDefaults, canUpdateSaved: saved != null))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var result = dialog.Result;
                if (dialog.UpdateSaved && saved != null)
                {
                    int index = _state.Connections.IndexOf(saved);
                    if (index >= 0) _state.Connections[index] = result.Clone();
                    RefreshTree();
                }
                SaveState();
                session.Reconfigure(result);
                _strip.RefreshTabs();
                UpdateTitle();
            }
        }

        private void ShowDefaults()
        {
            using (var dialog = new ConnectionDialog(_state.Defaults, ConnectionDialogMode.Defaults, Groups(), Addresses(), null))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                SaveDefaults(dialog.Result);
            }
        }

        private void SaveDefaults(ConnectionInfo settings)
        {
            _state.Defaults = settings.NewFromTemplate();
            SaveState();
        }

        private void OpenRdpFile()
        {
            using (var dialog = new OpenFileDialog { Filter = "Remote Desktop files (*.rdp)|*.rdp|All files (*.*)|*.*", Multiselect = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (var file in dialog.FileNames)
                {
                    try
                    {
                        var launch = LaunchParser.Parse(new[] { file }, _state.Defaults);
                        if (launch.DelegateReason != null) LaunchParser.RunMstsc(new[] { file });
                        else OpenRequest(launch.Request);
                        ShowLaunchErrors(launch.Errors);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"{Path.GetFileName(file)}: {ex.Message}", "Open .rdp file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void ImportRdpFiles()
        {
            using (var dialog = new OpenFileDialog { Filter = "Remote Desktop files (*.rdp)|*.rdp", Multiselect = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (var file in dialog.FileNames)
                {
                    try { _state.Connections.Add(RdpFile.Load(file, _state.Defaults)); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"{Path.GetFileName(file)}: {ex.Message}", "Import .rdp file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                RefreshTree();
                SaveState();
                SetSidebar(true);
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

        private IEnumerable<string> Addresses() =>
            _state.RecentAddresses.Concat(_state.Connections.Select(c => c.Address)).Distinct(StringComparer.OrdinalIgnoreCase);

        private ConnectionInfo AddSavedConnection(ConnectionInfo seed)
        {
            if (_tree.SelectedNode?.Tag is string group && string.IsNullOrEmpty(seed.Group)) seed.Group = group;
            using (var dialog = new ConnectionDialog(seed, ConnectionDialogMode.EditSaved, Groups(), Addresses(), SaveDefaults))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                _state.Connections.Add(dialog.Result);
                RefreshTree(dialog.Result);
                SaveState();
                return dialog.Result;
            }
        }

        private void EditSavedConnection(ConnectionInfo info)
        {
            using (var dialog = new ConnectionDialog(info, ConnectionDialogMode.EditSaved, Groups(), Addresses(), SaveDefaults))
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

        private void SaveTabToConnections(RdpSession session)
        {
            var saved = AddSavedConnection(session.Info.Clone(newId: true));
            if (saved != null) session.Info.Id = saved.Id;
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
        }

        // ---- Alt menu --------------------------------------------------------------------

        private ToolStripMenuItem _sidebarItem, _smartSizingItem, _titleBarItem, _altItem;
        private ToolStripMenuItem _themeSystem, _themeDark, _themeLight;

        private static ToolStripMenuItem Item(string text, string shortcut, Action action)
        {
            var item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = shortcut };
            item.Click += (s, e) => action();
            return item;
        }

        private static ToolStripItem WithEnabled(ToolStripItem item, bool enabled)
        {
            item.Enabled = enabled;
            return item;
        }

        private void BuildMenu()
        {
            // --- Connection ---
            var connection = new ToolStripMenuItem("&Connection");
            var saved = new ToolStripMenuItem("Saved connections");
            var recent = new ToolStripMenuItem("Recent");
            var reconnect = Item("Reconnect", "F5", () => CurrentSession?.Reconnect());
            var disconnect = Item("Disconnect", null, () => CurrentSession?.Disconnect());
            var fullScreen = Item("Full screen", "F11", () => CurrentSession?.ToggleFullScreen());
            var duplicate = Item("Duplicate tab", null, () => { if (CurrentSession != null) OpenSession(CurrentSession.Info); });
            var saveTab = Item("Save tab to my connections…", null, () => { if (CurrentSession != null) SaveTabToConnections(CurrentSession); });
            var closeTab = Item("Close tab", "Ctrl+W", () => CurrentSession?.RequestClose());
            connection.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("New connection…", "Ctrl+T", () => NewConnection()),
                Item("Open .rdp file…", "Ctrl+O", OpenRdpFile),
                saved,
                recent,
                new ToolStripSeparator(),
                reconnect, disconnect, fullScreen, duplicate, saveTab,
                new ToolStripSeparator(),
                closeTab,
                Item("Exit", "Alt+F4", Close),
            });
            connection.DropDownOpening += (s, e) =>
            {
                var session = CurrentSession;
                reconnect.Enabled = duplicate.Enabled = saveTab.Enabled = closeTab.Enabled = session != null;
                disconnect.Enabled = session?.IsActive == true;
                fullScreen.Enabled = session?.State == SessionState.Connected;
                saveTab.Enabled = session != null && !_state.Connections.Any(c => c.Id == session.Info.Id);
                FillSavedMenu(saved);
                FillRecentMenu(recent);
            };

            // --- View ---
            var view = new ToolStripMenuItem("&View");
            _sidebarItem = Item("Connections sidebar", "Ctrl+B", () => SetSidebar(_split.Panel1Collapsed));
            _themeSystem = Item("Follow Windows", null, () => SetTheme(ThemeMode.System));
            _themeDark = Item("Dark", null, () => SetTheme(ThemeMode.Dark));
            _themeLight = Item("Light", null, () => SetTheme(ThemeMode.Light));
            var theme = new ToolStripMenuItem("Theme");
            theme.DropDownItems.AddRange(new ToolStripItem[] { _themeSystem, _themeDark, _themeLight });
            _smartSizingItem = Item("Smart sizing (this tab)", null, () =>
            {
                if (CurrentSession != null) CurrentSession.SmartSizing = !CurrentSession.SmartSizing;
            });
            _titleBarItem = Item("Use the standard Windows title bar", null, () =>
            {
                _state.StandardTitleBar = !_state.StandardTitleBar;
                SaveState();
                MessageBox.Show(this, "The title bar style changes the next time Tabbed RDP starts.", "Tabbed RDP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            _altItem = Item("Alt opens this menu inside sessions", null, () =>
            {
                _state.AltMenuInSessions = !_state.AltMenuInSessions;
                SaveState();
                InstallAltHandling();
            });
            view.DropDownItems.AddRange(new ToolStripItem[]
            {
                _sidebarItem, theme, new ToolStripSeparator(), _smartSizingItem, new ToolStripSeparator(), _titleBarItem, _altItem,
            });
            view.DropDownOpening += (s, e) =>
            {
                _sidebarItem.Checked = !_split.Panel1Collapsed;
                _themeSystem.Checked = _state.Theme == ThemeMode.System;
                _themeDark.Checked = _state.Theme == ThemeMode.Dark;
                _themeLight.Checked = _state.Theme == ThemeMode.Light;
                _smartSizingItem.Enabled = CurrentSession != null;
                _smartSizingItem.Checked = CurrentSession?.SmartSizing == true;
                _titleBarItem.Checked = _state.StandardTitleBar;
                _altItem.Checked = _state.AltMenuInSessions;
            };

            // --- Options ---
            var options = new ToolStripMenuItem("&Options");
            var tabOptions = Item("Options for this connection…", null, () => ShowTabOptions(CurrentSession));
            options.DropDownItems.AddRange(new ToolStripItem[]
            {
                tabOptions,
                Item("Default settings for new connections…", null, ShowDefaults),
            });
            options.DropDownOpening += (s, e) => tabOptions.Enabled = CurrentSession != null;

            // --- Help ---
            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("Keyboard shortcuts", null, ShowShortcuts),
                Item("Open launch log", null, () => OpenPath(Log.FilePath)),
                Item("Open settings folder", null, () => OpenPath(AppState.Folder)),
                new ToolStripSeparator(),
                Item("About Tabbed RDP", null, () => MessageBox.Show(this,
                    $"Tabbed RDP {Application.ProductVersion}\n\nRemote Desktop engine: {RdpEngineName()}\nSettings: {AppState.FilePath}",
                    "About Tabbed RDP", MessageBoxButtons.OK, MessageBoxIcon.Information)),
            });

            _menu.Items.AddRange(new ToolStripItem[] { connection, view, options, help });
            _menu.MenuDeactivate += (s, e) => BeginInvoke((Action)HideMenuIfIdle);
            foreach (ToolStripMenuItem top in _menu.Items)
                top.DropDownClosed += (s, e) => BeginInvoke((Action)HideMenuIfIdle);
        }

        private void FillSavedMenu(ToolStripMenuItem parent)
        {
            parent.DropDownItems.Clear();
            var groups = _state.Connections
                .GroupBy(c => string.IsNullOrWhiteSpace(c.Group) ? "" : c.Group.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key.Length == 0 ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                var target = parent;
                if (group.Key.Length > 0)
                {
                    target = new ToolStripMenuItem(group.Key);
                    parent.DropDownItems.Add(target);
                }
                foreach (var c in group.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase))
                {
                    var conn = c;
                    target.DropDownItems.Add(Item(c.DisplayName, c.DisplayName != c.Address ? c.Address : null, () => OpenSession(conn)));
                }
            }
            if (parent.DropDownItems.Count > 0) parent.DropDownItems.Add(new ToolStripSeparator());
            parent.DropDownItems.Add(Item("Show the connections sidebar", "Ctrl+B", () => SetSidebar(true)));
            parent.DropDownItems.Add(Item("Import .rdp files…", null, ImportRdpFiles));
        }

        private void FillRecentMenu(ToolStripMenuItem parent)
        {
            parent.DropDownItems.Clear();
            foreach (var address in _state.RecentAddresses.Take(15))
            {
                var a = address;
                parent.DropDownItems.Add(Item(a, null, () =>
                {
                    ConnectionInfo.TryParseAddress(a, out var host, out var port);
                    if (_state.FindByAddress(host, port) is ConnectionInfo saved) OpenSession(saved);
                    else NewConnection(a);
                }));
            }
            if (parent.DropDownItems.Count == 0) parent.DropDownItems.Add(new ToolStripMenuItem("(empty)") { Enabled = false });
            else
            {
                parent.DropDownItems.Add(new ToolStripSeparator());
                parent.DropDownItems.Add(Item("Clear recent", null, () => { _state.RecentAddresses.Clear(); SaveState(); RefreshWelcome(); }));
            }
        }

        private void ToggleMenu()
        {
            if (_menu.Visible) { HideMenu(); return; }
            // The same Alt press that closed the menu must not reopen it.
            if (unchecked(Environment.TickCount - _menuClosedAt) < 350) return;
            if (WindowState == FormWindowState.Minimized) return;

            _menu.Bounds = new Rectangle(0, _strip.Bottom, ClientSize.Width, _menu.GetPreferredSize(Size.Empty).Height);
            _menu.Visible = true;
            _menu.BringToFront();
            ((ToolStripMenuItem)_menu.Items[0]).ShowDropDown();
            ((ToolStripMenuItem)_menu.Items[0]).DropDown.Focus();
        }

        private void HideMenuIfIdle()
        {
            if (!_menu.Visible) return;
            if (_menu.Items.Cast<ToolStripMenuItem>().Any(i => i.DropDown.Visible)) return;
            HideMenu();
        }

        private void HideMenu()
        {
            foreach (ToolStripMenuItem item in _menu.Items) item.HideDropDown();
            _menu.Visible = false;
            _menuClosedAt = Environment.TickCount;
            // Don't steal the keyboard from a dialog the menu just opened.
            if (ActiveForm == this) CurrentSession?.FocusRemote();
        }

        private void InstallAltHandling()
        {
            if (_altFilter == null)
            {
                _altFilter = new AltMessageFilter(this);
                _altFilter.AltTapped += (s, e) =>
                {
                    if (_altWatcher == null) BeginInvoke((Action)ToggleMenu);
                };
                Application.AddMessageFilter(_altFilter);
            }

            _altWatcher?.Dispose();
            _altWatcher = null;
            if (_state.AltMenuInSessions)
            {
                try
                {
                    var watcher = new AltKeyWatcher(() => IsHandleCreated && GetForegroundWindow() == Handle);
                    if (watcher.IsInstalled)
                    {
                        watcher.AltTapped += (s, e) => BeginInvoke((Action)ToggleMenu);
                        _altWatcher = watcher;
                    }
                    else watcher.Dispose();
                }
                catch (Exception ex)
                {
                    // Fall back to Alt in our own controls only (AltMessageFilter).
                    Log.Write("keyboard hook unavailable: " + ex.Message);
                }
            }
        }

        // ---- context menus ---------------------------------------------------------------

        private void BuildTabMenu(RdpSession session)
        {
            _tabMenu.Items.Clear();
            bool connected = session.State == SessionState.Connected;
            _tabMenu.Items.Add(Item("Reconnect", "F5", session.Reconnect));
            _tabMenu.Items.Add(WithEnabled(Item("Disconnect", null, session.Disconnect), session.IsActive));
            _tabMenu.Items.Add(WithEnabled(Item("Full screen", "F11", session.ToggleFullScreen), connected));
            _tabMenu.Items.Add(new ToolStripMenuItem("Smart sizing", null, (s, e) => session.SmartSizing = !session.SmartSizing) { Checked = session.SmartSizing });
            _tabMenu.Items.Add(Item("Options…", null, () => ShowTabOptions(session)));
            _tabMenu.Items.Add(new ToolStripSeparator());
            _tabMenu.Items.Add(Item("Duplicate tab", null, () => OpenSession(session.Info)));
            if (!_state.Connections.Any(c => c.Id == session.Info.Id))
                _tabMenu.Items.Add(Item("Save to my connections…", null, () => SaveTabToConnections(session)));
            _tabMenu.Items.Add(Item("Copy address", null, () => Clipboard.SetText(session.Info.Address)));
            _tabMenu.Items.Add(new ToolStripSeparator());
            _tabMenu.Items.Add(Item("Close tab", "Ctrl+W", session.RequestClose));
            _tabMenu.Items.Add(WithEnabled(Item("Close other tabs", null, () =>
            {
                foreach (var other in _strip.Tabs.Where(t => t != session).ToList()) other.RequestClose();
            }), _strip.Tabs.Count > 1));
        }

        private void BuildTreeMenu()
        {
            _treeMenu.Items.Clear();
            if (SelectedSaved is ConnectionInfo c)
            {
                var connect = Item("Connect", null, () => OpenSession(c));
                connect.Font = new Font(_treeMenu.Font, FontStyle.Bold);
                _treeMenu.Items.Add(connect);
                _treeMenu.Items.Add(Item("Edit…", "F2", () => EditSavedConnection(c)));
                _treeMenu.Items.Add(Item("Duplicate", null, () =>
                {
                    var copy = c.Clone(newId: true);
                    copy.Name = c.DisplayName + " (copy)";
                    AddSavedConnection(copy);
                }));
                _treeMenu.Items.Add(Item("Copy address", null, () => Clipboard.SetText(c.Address)));
                _treeMenu.Items.Add(Item("Delete", "Del", () => DeleteSavedConnection(c)));
                _treeMenu.Items.Add(new ToolStripSeparator());
            }
            else if (_tree.SelectedNode?.Tag is string group)
            {
                var members = _state.Connections.Where(x => string.Equals((x.Group ?? "").Trim(), group, StringComparison.OrdinalIgnoreCase)).ToList();
                _treeMenu.Items.Add(Item($"Connect all ({members.Count})", null, () => members.ForEach(OpenSession)));
                _treeMenu.Items.Add(new ToolStripSeparator());
            }
            _treeMenu.Items.Add(Item("Add connection…", null, () => AddSavedConnection(_state.Defaults.NewFromTemplate())));
            _treeMenu.Items.Add(Item("Import .rdp files…", null, ImportRdpFiles));
        }

        // ---- keyboard --------------------------------------------------------------------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // While the remote desktop has the keyboard, keystrokes go to the remote machine.
            switch (keyData)
            {
                case Keys.Control | Keys.T:
                case Keys.Control | Keys.N: NewConnection(); return true;
                case Keys.Control | Keys.O: OpenRdpFile(); return true;
                case Keys.Control | Keys.W: CurrentSession?.RequestClose(); return true;
                case Keys.Control | Keys.B: SetSidebar(_split.Panel1Collapsed); return true;
                case Keys.Control | Keys.F: SetSidebar(true); _filter.Focus(); _filter.SelectAll(); return true;
                case Keys.F11: CurrentSession?.ToggleFullScreen(); return true;
                case Keys.F5: CurrentSession?.Reconnect(); return true;
                case Keys.Control | Keys.Tab: _strip.Cycle(1); return true;
                case Keys.Control | Keys.Shift | Keys.Tab: _strip.Cycle(-1); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowShortcuts()
        {
            MessageBox.Show(this,
                "Alt  —  show / hide the menu\n" +
                "Ctrl+T or Ctrl+N  —  new connection\n" +
                "Ctrl+O  —  open .rdp file\n" +
                "Ctrl+Tab / Ctrl+Shift+Tab  —  next / previous tab\n" +
                "Ctrl+W  —  close tab\n" +
                "F5  —  reconnect\n" +
                "F11  —  full screen  (Ctrl+Alt+Break inside a session)\n" +
                "Ctrl+B  —  connections sidebar\n" +
                "Ctrl+F  —  search connections\n\n" +
                "While a session has the keyboard, only Alt works here; other keys go to the remote computer.\n" +
                "Click the tab bar first to use the other shortcuts.",
                "Keyboard shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---- theme -----------------------------------------------------------------------

        private void SetTheme(ThemeMode mode)
        {
            _state.Theme = mode;
            SaveState();
            Theme.SetMode(mode);
            ApplyTheme();
        }

        private void OnThemeChanged(object sender, EventArgs e) => ApplyTheme();

        private void ApplyTheme()
        {
            var p = Theme.Current;
            Theme.ApplyWindow(this);
            BackColor = p.TitleBar;
            _content.BackColor = p.TabActive;
            _welcome.BackColor = p.TabActive;
            _split.BackColor = p.Border;
            _split.Panel2.BackColor = p.TabActive;
            _menu.BackColor = p.Surface;
            _menu.Renderer = new ThemedRenderer();
            _tabMenu.Renderer = new ThemedRenderer();
            _treeMenu.Renderer = new ThemedRenderer();
            if (IsHandleCreated) Theme.SetDarkTitleBar(Handle, p.IsDark);
            RefreshWelcome();
            _strip.Invalidate();
        }

        // ---- window chrome (tabs in the title bar) ---------------------------------------

        private const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
        private const int HTCLIENT = 1, HTCAPTION = 2, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NCCALCSIZE_PARAMS { public RECT Rgrc0, Rgrc1, Rgrc2; public IntPtr Lppos; }

        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private int FrameThickness => GetSystemMetrics(33 /* SM_CYFRAME */) + GetSystemMetrics(92 /* SM_CXPADDEDBORDER */);
        private static int TopResizeBorder => Dpi.Scale(5);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.SetDarkTitleBar(Handle, Theme.Current.IsDark);
            // Re-run WM_NCCALCSIZE so the custom title bar takes effect.
            if (_customChrome) SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0020);
        }

        protected override void WndProc(ref Message m)
        {
            if (_customChrome)
            {
                if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
                {
                    // Keep the side/bottom frame (resize + snap shadow), give the caption area to the client.
                    var p = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(m.LParam);
                    int top = p.Rgrc0.Top;
                    base.WndProc(ref m);
                    p = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(m.LParam);
                    p.Rgrc0.Top = top + (IsZoomed(Handle) ? FrameThickness : 0);
                    Marshal.StructureToPtr(p, m.LParam, false);
                    m.Result = IntPtr.Zero;
                    return;
                }
                if (m.Msg == WM_NCHITTEST)
                {
                    base.WndProc(ref m);
                    if ((int)m.Result == HTCLIENT)
                    {
                        var screen = new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16)));
                        var pt = PointToClient(screen);
                        int border = TopResizeBorder;
                        if (!IsZoomed(Handle) && pt.Y >= 0 && pt.Y < border)
                            m.Result = (IntPtr)(pt.X < border * 3 ? HTTOPLEFT : pt.X >= ClientSize.Width - border * 3 ? HTTOPRIGHT : HTTOP);
                        else if (pt.Y >= 0 && pt.Y < _strip.Bottom && _strip.IsCaptionArea(_strip.PointToClient(screen)))
                            m.Result = (IntPtr)HTCAPTION;
                    }
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_strip == null) return;
            bool maximized = WindowState == FormWindowState.Maximized;
            _strip.WindowMaximized = maximized;
            _strip.TopResizeBorder = _customChrome && !maximized ? TopResizeBorder : 0;
            _strip.Invalidate();
            if (_menu.Visible) _menu.Width = ClientSize.Width;
        }

        // ---- state -----------------------------------------------------------------------

        private void UpdateTitle()
        {
            var session = CurrentSession;
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
            catch (Exception ex) { Log.Write("could not save settings: " + ex.Message); }
        }

        private static void OpenPath(string path)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path)) Directory.CreateDirectory(AppState.Folder);
                Process.Start(new ProcessStartInfo(File.Exists(path) || Directory.Exists(path) ? path : AppState.Folder) { UseShellExecute = true });
            }
            catch { }
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

                // Disconnect cleanly; RemoveSession() closes the form once the last tab is gone.
                _exiting = true;
                _exitTimeout = new Timer { Interval = 6000 };
                _exitTimeout.Tick += (s, a) => { _exitTimeout.Stop(); Close(); };
                _exitTimeout.Start();
                foreach (var session in Sessions.ToList()) session.RequestClose();
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
            _altWatcher?.Dispose();
            if (_altFilter != null) Application.RemoveMessageFilter(_altFilter);
            Theme.Changed -= OnThemeChanged;
            foreach (var session in Sessions.ToList())
            {
                try { session.Dispose(); } catch { }
            }
            base.OnFormClosed(e);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void SetCue(TextBox box, string cue) =>
            box.HandleCreated += (s, e) => SendMessage(box.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, cue);
    }

    /// <summary>The app icon (taskbar / Alt+Tab), drawn at runtime: a blue tile with a monitor.</summary>
    internal static class AppIcon
    {
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

        public static Icon Create()
        {
            try
            {
                using (var bmp = new Bitmap(64, 64))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var path = new GraphicsPath())
                        {
                            path.AddArc(2, 2, 20, 20, 180, 90);
                            path.AddArc(42, 2, 20, 20, 270, 90);
                            path.AddArc(42, 42, 20, 20, 0, 90);
                            path.AddArc(2, 42, 20, 20, 90, 90);
                            path.CloseFigure();
                            using (var brush = new LinearGradientBrush(new Rectangle(0, 0, 64, 64), Color.FromArgb(40, 140, 255), Color.FromArgb(0, 90, 200), 90f))
                                g.FillPath(brush, path);
                        }
                        using (var pen = new Pen(Color.White, 4.5f))
                        {
                            g.DrawRectangle(pen, 14, 15, 36, 24);
                            g.DrawLine(pen, 32, 40, 32, 48);
                            g.DrawLine(pen, 22, 49, 42, 49);
                        }
                        using (var tab = new SolidBrush(Color.FromArgb(255, 200, 60)))
                            g.FillRectangle(tab, 14, 15, 14, 6);
                    }
                    IntPtr handle = bmp.GetHicon();
                    var icon = (Icon)Icon.FromHandle(handle).Clone();
                    DestroyIcon(handle);
                    return icon;
                }
            }
            catch
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
        }
    }
}
