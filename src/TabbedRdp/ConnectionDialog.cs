using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TabbedRdp
{
    /// <summary>Create / edit a connection (the "Show Options" part of mstsc).</summary>
    public sealed class ConnectionDialog : Form
    {
        private static readonly (string Label, int W, int H)[] Resolutions =
        {
            ("Fit to window (auto-resize)", 0, 0),
            ("3840 x 2160", 3840, 2160),
            ("2560 x 1440", 2560, 1440),
            ("1920 x 1080", 1920, 1080),
            ("1680 x 1050", 1680, 1050),
            ("1600 x 900", 1600, 900),
            ("1440 x 900", 1440, 900),
            ("1366 x 768", 1366, 768),
            ("1280 x 1024", 1280, 1024),
            ("1280 x 720", 1280, 720),
            ("1024 x 768", 1024, 768),
        };

        private readonly TextBox _name = new TextBox();
        private readonly ComboBox _group = new ComboBox();
        private readonly TextBox _host = new TextBox();
        private readonly NumericUpDown _port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 3389, Width = 90 };
        private readonly TextBox _user = new TextBox();
        private readonly TextBox _password = new TextBox { UseSystemPasswordChar = true };
        private readonly CheckBox _savePassword = new CheckBox { Text = "Remember password (encrypted for your Windows account)", AutoSize = true };
        private readonly ComboBox _resolution = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox _colors = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _smartSizing = new CheckBox { Text = "Smart sizing (scale instead of resize)", AutoSize = true };
        private readonly CheckBox _admin = new CheckBox { Text = "Connect to admin / console session (/admin)", AutoSize = true };
        private readonly CheckBox _nla = new CheckBox { Text = "Network Level Authentication (CredSSP)", AutoSize = true };
        private readonly CheckBox _clipboard = new CheckBox { Text = "Clipboard", AutoSize = true };
        private readonly CheckBox _drives = new CheckBox { Text = "Local drives", AutoSize = true };
        private readonly CheckBox _printers = new CheckBox { Text = "Printers", AutoSize = true };
        private readonly CheckBox _smartCards = new CheckBox { Text = "Smart cards", AutoSize = true };
        private readonly CheckBox _ports = new CheckBox { Text = "Serial ports (COM)", AutoSize = true };
        private readonly CheckBox _microphone = new CheckBox { Text = "Microphone", AutoSize = true };
        private readonly CheckBox _fullScreen = new CheckBox { Text = "Start in full screen", AutoSize = true };
        private readonly TextBox _gateway = new TextBox();
        private readonly ComboBox _audio = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _notes = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 70 };
        private readonly CheckBox _saveToList = new CheckBox { Text = "Save to connections list", AutoSize = true };

        public ConnectionInfo Result { get; private set; }
        public bool SaveToList => _saveToList.Checked;

        /// <param name="okText">"Connect" or "Save".</param>
        /// <param name="showSaveToList">Show the "save to list" box (for ad-hoc connections).</param>
        public ConnectionDialog(ConnectionInfo info, IEnumerable<string> groups, string okText, bool showSaveToList)
        {
            Text = string.IsNullOrWhiteSpace(info.Host) ? "New connection" : $"Connection - {info.DisplayName}";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(500, 520);

            _group.Items.AddRange(groups.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g).ToArray<object>());
            _resolution.Items.AddRange(Resolutions.Select(r => (object)r.Label).ToArray());
            _colors.Items.AddRange(new object[] { "32-bit (highest quality)", "24-bit", "16-bit", "15-bit" });
            _audio.Items.AddRange(new object[] { "Play on this computer", "Play on remote computer", "Do not play" });

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildGeneralPage());
            tabs.TabPages.Add(BuildDisplayPage());
            tabs.TabPages.Add(BuildResourcesPage());

            var ok = new Button { Text = okText, DialogResult = DialogResult.None, Width = 90, Height = 28 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 28 };
            ok.Click += (s, e) => Accept();
            AcceptButton = ok;
            CancelButton = cancel;

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(6, 8, 6, 6) };
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(ok);
            _saveToList.Visible = showSaveToList;
            _saveToList.Margin = new Padding(3, 7, 30, 3);
            bottom.Controls.Add(_saveToList);

            Controls.Add(tabs);
            Controls.Add(bottom);

            Populate(info);
            Result = info;
        }

        private TabPage BuildGeneralPage()
        {
            var table = NewTable();
            var hostRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            _host.Width = 230;
            hostRow.Controls.Add(_host);
            hostRow.Controls.Add(new Label { Text = "Port:", AutoSize = true, Margin = new Padding(10, 6, 3, 0) });
            hostRow.Controls.Add(_port);

            AddRow(table, "Computer:", hostRow);
            AddRow(table, "User name:", _user);
            AddRow(table, "", Hint("DOMAIN\\user, user@domain or .\\localadmin"));
            AddRow(table, "Password:", _password);
            AddRow(table, "", _savePassword);
            AddRow(table, "Display name:", _name);
            AddRow(table, "Group / client:", _group);
            AddRow(table, "Notes:", _notes);
            return new TabPage("General") { Controls = { table }, Padding = new Padding(8) };
        }

        private TabPage BuildDisplayPage()
        {
            var table = NewTable();
            AddRow(table, "Resolution:", _resolution);
            AddRow(table, "Colors:", _colors);
            AddRow(table, "", _fullScreen);
            AddRow(table, "", _smartSizing);
            AddRow(table, "", Hint("\"Fit to window\" resizes the remote desktop when you resize the tab\n(Windows 8.1 / Server 2012 R2 or newer)."));
            AddRow(table, "", _admin);
            AddRow(table, "", _nla);
            AddRow(table, "RD Gateway:", _gateway);
            AddRow(table, "", Hint("Leave empty to connect directly."));
            return new TabPage("Display") { Controls = { table }, Padding = new Padding(8) };
        }

        private TabPage BuildResourcesPage()
        {
            var table = NewTable();
            AddRow(table, "Redirect:", _clipboard);
            AddRow(table, "", _drives);
            AddRow(table, "", _printers);
            AddRow(table, "", _smartCards);
            AddRow(table, "", _ports);
            AddRow(table, "", _microphone);
            AddRow(table, "Remote audio:", _audio);
            return new TabPage("Local resources") { Controls = { table }, Padding = new Padding(8) };
        }

        private static TableLayoutPanel NewTable()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }

        private static void AddRow(TableLayoutPanel table, string label, Control control)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (!string.IsNullOrEmpty(label))
                table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, 0, row);
            if (!(control is CheckBox) && !(control is Label) && !(control is FlowLayoutPanel))
                control.Dock = DockStyle.Fill;
            table.Controls.Add(control, 1, row);
        }

        private static Label Hint(string text) =>
            new Label { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 6) };

        private void Populate(ConnectionInfo info)
        {
            _name.Text = info.Name ?? "";
            _group.Text = info.Group ?? "";
            _host.Text = info.Host ?? "";
            _port.Value = Math.Max(1, Math.Min(65535, info.Port <= 0 ? 3389 : info.Port));
            _user.Text = info.FullUserName ?? "";
            _password.Text = info.Password ?? "";
            _savePassword.Checked = info.SavePassword;

            int res = Array.FindIndex(Resolutions, r => r.W == info.Width && r.H == info.Height);
            if (res < 0 && info.Width > 0)
            {
                _resolution.Items.Add($"{info.Width} x {info.Height}");
                res = _resolution.Items.Count - 1;
            }
            _resolution.SelectedIndex = Math.Max(0, res);

            _colors.SelectedIndex = info.ColorDepth == 24 ? 1 : info.ColorDepth == 16 ? 2 : info.ColorDepth == 15 ? 3 : 0;
            _smartSizing.Checked = info.SmartSizing;
            _admin.Checked = info.AdminSession;
            _nla.Checked = info.UseNla;
            _clipboard.Checked = info.RedirectClipboard;
            _drives.Checked = info.RedirectDrives;
            _printers.Checked = info.RedirectPrinters;
            _smartCards.Checked = info.RedirectSmartCards;
            _ports.Checked = info.RedirectPorts;
            _microphone.Checked = info.AudioCapture;
            _fullScreen.Checked = info.FullScreen;
            _gateway.Text = info.GatewayUsage != 0 && info.GatewayUsage != 4 ? info.GatewayHost ?? "" : "";
            _audio.SelectedIndex = (int)info.Audio;
            _notes.Text = info.Notes ?? "";
        }

        private void Accept()
        {
            if (!ConnectionInfo.TryParseAddress(_host.Text, out var host, out var portFromHost))
            {
                MessageBox.Show(this, "Enter a computer name or IP address.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _host.Focus();
                return;
            }

            var info = Result.Clone();
            info.Host = host;
            // A port typed as "host:port" wins over the Port box.
            info.Port = portFromHost != 3389 ? portFromHost : (int)_port.Value;
            info.Name = _name.Text.Trim();
            info.Group = _group.Text.Trim();
            info.SetFullUserName(_user.Text);
            info.Password = _password.Text;
            info.SavePassword = _savePassword.Checked && _password.Text.Length > 0;

            int res = _resolution.SelectedIndex;
            if (res < Resolutions.Length)
            {
                info.Width = Resolutions[res].W;
                info.Height = Resolutions[res].H;
            }

            info.ColorDepth = new[] { 32, 24, 16, 15 }[Math.Max(0, _colors.SelectedIndex)];
            info.SmartSizing = _smartSizing.Checked;
            info.AdminSession = _admin.Checked;
            info.UseNla = _nla.Checked;
            info.RedirectClipboard = _clipboard.Checked;
            info.RedirectDrives = _drives.Checked;
            info.RedirectPrinters = _printers.Checked;
            info.RedirectSmartCards = _smartCards.Checked;
            info.RedirectPorts = _ports.Checked;
            info.AudioCapture = _microphone.Checked;
            info.FullScreen = _fullScreen.Checked;
            info.GatewayHost = _gateway.Text.Trim();
            if (info.GatewayHost.Length == 0) info.GatewayUsage = 0;
            else if (info.GatewayUsage == 0 || info.GatewayUsage == 4) info.GatewayUsage = 1;
            info.Audio = (AudioMode)Math.Max(0, _audio.SelectedIndex);
            info.Notes = _notes.Text;

            Result = info;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>Small "Enter your credentials" prompt used by the quick-connect bar.</summary>
    public sealed class CredentialDialog : Form
    {
        private readonly TextBox _user = new TextBox { Width = 260 };
        private readonly TextBox _password = new TextBox { Width = 260, UseSystemPasswordChar = true };
        private readonly CheckBox _save = new CheckBox { Text = "Save this connection (password encrypted)", AutoSize = true };

        public string UserName => _user.Text.Trim();
        public string Password => _password.Text;
        public bool SaveConnection => _save.Checked;

        public CredentialDialog(string address, string userName)
        {
            Text = "Enter your credentials";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            Font = SystemFonts.MessageBoxFont;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(12), Dock = DockStyle.Fill };
            table.Controls.Add(new Label { Text = $"These credentials will be used to connect to {address}.", AutoSize = true, Margin = new Padding(3, 3, 3, 12) }, 0, 0);
            table.SetColumnSpan(table.GetControlFromPosition(0, 0), 2);
            table.Controls.Add(new Label { Text = "User name:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, 0, 1);
            table.Controls.Add(_user, 1, 1);
            table.Controls.Add(new Label { Text = "Password:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, 0, 2);
            table.Controls.Add(_password, 1, 2);
            table.Controls.Add(_save, 1, 3);

            var ok = new Button { Text = "Connect", DialogResult = DialogResult.OK, Width = 90, Height = 28 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 28 };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            table.Controls.Add(buttons, 0, 4);
            table.SetColumnSpan(buttons, 2);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(table);

            _user.Text = userName ?? "";
            Shown += (s, e) => { if (_user.Text.Length > 0) _password.Focus(); else _user.Focus(); };
        }
    }
}
