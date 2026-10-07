using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace TabbedRdp
{
    public enum SessionState
    {
        Idle,
        Connecting,
        Connected,
        Disconnected,
    }

    /// <summary>One remote desktop connection: the ActiveX control plus a status/reconnect overlay.</summary>
    public sealed class RdpSession : Panel
    {
        private RdpClientHost _rdp;
        private readonly Panel _overlay;
        private readonly Label _statusLabel;
        private readonly Button _reconnectButton;
        private readonly Button _closeButton;
        private readonly Timer _resizeTimer;
        private readonly Timer _closeTimeout;
        private bool _closing;
        private bool _loggedIn;
        private bool _reconnectAfterDisconnect;

        public ConnectionInfo Info { get; private set; }
        public SessionState State { get; private set; } = SessionState.Idle;
        public string StatusText { get; private set; } = "";

        public event EventHandler StateChanged;
        public event EventHandler CloseRequested;

        public RdpSession(ConnectionInfo info)
        {
            Info = info;
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(32, 32, 32);

            _statusLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 90,
                TextAlign = ContentAlignment.BottomCenter,
                ForeColor = Color.Gainsboro,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 11f),
            };
            _reconnectButton = new Button { Text = "Reconnect", Width = 110, Height = 32, Tag = "primary" };
            _reconnectButton.Click += (s, e) => Connect();
            _closeButton = new Button { Text = "Close tab", Width = 110, Height = 32 };
            _closeButton.Click += (s, e) => RequestClose();

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 50,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 10, 0, 0),
                Tag = "keepBack",
                BackColor = Color.Transparent,
            };
            buttons.Controls.Add(_reconnectButton);
            buttons.Controls.Add(_closeButton);
            buttons.Resize += (s, e) =>
            {
                int total = _reconnectButton.Width + _closeButton.Width + 12;
                buttons.Padding = new Padding(Math.Max(0, (buttons.Width - total) / 2), 10, 0, 0);
            };

            _overlay = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 80, 20, 0) };
            _overlay.Controls.Add(buttons);
            _overlay.Controls.Add(_statusLabel);
            Controls.Add(_overlay);
            ApplyTheme();
            TabbedRdp.Theme.Changed += OnThemeChanged;

            _resizeTimer = new Timer { Interval = 600 };
            _resizeTimer.Tick += (s, e) => { _resizeTimer.Stop(); ApplyWindowSize(); };

            _closeTimeout = new Timer { Interval = 4000 };
            _closeTimeout.Tick += (s, e) => { _closeTimeout.Stop(); CloseRequested?.Invoke(this, EventArgs.Empty); };
        }

        public bool IsActive => State == SessionState.Connecting || State == SessionState.Connected;

        public bool IsFullScreen
        {
            get { try { return _rdp != null && (bool)_rdp.Ocx.FullScreen; } catch { return false; } }
        }

        private void OnThemeChanged(object sender, EventArgs e) => ApplyTheme();

        private void ApplyTheme()
        {
            var p = TabbedRdp.Theme.Current;
            BackColor = p.TabActive;
            _overlay.BackColor = p.TabActive;
            _statusLabel.ForeColor = p.Text;
            foreach (Control c in _overlay.Controls) TabbedRdp.Theme.Apply(c);
        }

        /// <summary>Reconnects, disconnecting cleanly first if the session is still up.</summary>
        public void Reconnect()
        {
            if (_rdp != null && _rdp.IsConnected)
            {
                _reconnectAfterDisconnect = true;
                SetState(SessionState.Connecting, $"Reconnecting to {Info.Address}…");
                Try(() => _rdp.Ocx.Disconnect());
            }
            else
            {
                Connect();
            }
        }

        /// <summary>Applies new settings (Options dialog) and reconnects.</summary>
        public void Reconfigure(ConnectionInfo info)
        {
            Info = info;
            Reconnect();
        }

        public RdpClientHost Client => _rdp;

        public void Connect()
        {
            _closing = false;
            _loggedIn = false;
            DestroyClient();

            try
            {
                _rdp = new RdpClientHost { Dock = DockStyle.Fill };
                ((ISupportInitialize)_rdp).BeginInit();
                Controls.Add(_rdp);
                ((ISupportInitialize)_rdp).EndInit();
                _rdp.CreateControl();

                _rdp.Connecting += (s, e) => SetState(SessionState.Connecting, $"Connecting to {Info.Address}…");
                _rdp.Connected += (s, e) => SetState(SessionState.Connecting, $"Connected to {Info.Address}, logging on…");
                _rdp.LoginComplete += OnLoginComplete;
                _rdp.Disconnected += OnDisconnected;
                _rdp.LeftFullScreen += (s, e) => { _resizeTimer.Stop(); _resizeTimer.Start(); };
                _rdp.FatalError += (s, code) => SetState(SessionState.Disconnected, $"Fatal error in the Remote Desktop control (code {code}).");

                ApplySettings();
                _rdp.Ocx.Connect();
                SetState(SessionState.Connecting, $"Connecting to {Info.Address}…");
                _overlay.Visible = false;
                _rdp.BringToFront();
            }
            catch (Exception ex)
            {
                DestroyClient();
                SetState(SessionState.Disconnected, "Could not start the connection:\n" + ex.Message);
            }
        }

        private void ApplySettings()
        {
            dynamic ocx = _rdp.Ocx;
            ocx.Server = Info.Host;
            if (!string.IsNullOrWhiteSpace(Info.UserName)) ocx.UserName = Info.UserName;
            if (!string.IsNullOrWhiteSpace(Info.Domain)) ocx.Domain = Info.Domain;
            Try(() => ocx.ConnectingText = $"Connecting to {Info.Address}…");
            Try(() => ocx.FullScreenTitle = $"{Info.DisplayName} - Tabbed RDP");

            var size = DesiredDesktopSize();
            ocx.DesktopWidth = size.Width;
            ocx.DesktopHeight = size.Height;
            Try(() => ocx.ColorDepth = Info.ColorDepth);

            // Newest settings interface the control exposes (each one extends the previous).
            dynamic adv = TryGet(() => ocx.AdvancedSettings9) ?? TryGet(() => ocx.AdvancedSettings8) ?? ocx.AdvancedSettings2;

            Try(() => adv.RDPPort = Info.Port <= 0 ? 3389 : Info.Port);
            Try(() => adv.EnableCredSspSupport = Info.UseNla);
            Try(() => adv.AuthenticationLevel = (uint)Math.Max(0, Info.AuthenticationLevel));
            Try(() => adv.SmartSizing = Info.SmartSizing);
            Try(() => adv.EnableAutoReconnect = Info.AutoReconnect);
            Try(() => adv.MaxReconnectAttempts = 20);
            Try(() => adv.BitmapPersistence = 1);
            Try(() => adv.allowBackgroundInput = 1);
            Try(() => adv.GrabFocusOnConnect = true);
            Try(() => adv.RedirectClipboard = Info.RedirectClipboard);
            Try(() => adv.RedirectDrives = Info.RedirectDrives);
            Try(() => adv.RedirectPrinters = Info.RedirectPrinters);
            Try(() => adv.RedirectSmartCards = Info.RedirectSmartCards);
            Try(() => adv.RedirectPorts = Info.RedirectPorts);
            Try(() => adv.AudioCaptureRedirectionMode = Info.AudioCapture);
            Try(() => adv.ConnectToAdministerServer = Info.AdminSession);
            Try(() => adv.EnableWindowsKey = 1);
            Try(() => adv.PerformanceFlags = Info.PerformanceFlags);
            Try(() => adv.DisplayConnectionBar = Info.DisplayConnectionBar);
            Try(() => adv.PinConnectionBar = Info.PinConnectionBar);
            if (!string.IsNullOrWhiteSpace(Info.LoadBalanceInfo)) Try(() => adv.LoadBalanceInfo = Info.LoadBalanceInfo);

            Try(() => ocx.SecuredSettings2.AudioRedirectionMode = (int)Info.Audio);
            Try(() => ocx.SecuredSettings3.KeyboardHookMode = Info.KeyboardHook);
            if (!string.IsNullOrWhiteSpace(Info.AlternateShell)) Try(() => ocx.SecuredSettings.StartProgram = Info.AlternateShell);
            if (!string.IsNullOrWhiteSpace(Info.WorkingDirectory)) Try(() => ocx.SecuredSettings.WorkDir = Info.WorkingDirectory);

            // RD Gateway (.rdp gatewayusagemethod uses the same numbers as the control).
            if (!string.IsNullOrWhiteSpace(Info.GatewayHost) && Info.GatewayUsage != 0 && Info.GatewayUsage != 4)
            {
                Try(() => ocx.TransportSettings.GatewayHostname = Info.GatewayHost);
                Try(() => ocx.TransportSettings.GatewayUsageMethod = (uint)Info.GatewayUsage);
                Try(() => ocx.TransportSettings.GatewayProfileUsageMethod = 1u);
                Try(() => ocx.TransportSettings.GatewayCredsSource = (uint)Info.GatewayCredentialsSource);
                Try(() => ocx.TransportSettings2.GatewayCredSharing = Info.GatewayUseSameCredentials ? 1u : 0u);
            }

            if (Info.FullScreen) Try(() => ocx.FullScreen = true);

            if (!string.IsNullOrEmpty(Info.Password) && !Info.PromptForCredentials)
                _rdp.SetPassword(Info.Password);
            else
                _rdp.EnableCredentialPrompt(FindForm()?.Handle ?? IntPtr.Zero);
        }

        private Size DesiredDesktopSize()
        {
            if (Info.Width > 0 && Info.Height > 0) return new Size(Info.Width, Info.Height);
            if (Info.FullScreen)
            {
                var bounds = Screen.FromControl(this).Bounds;
                return new Size(bounds.Width & ~1, bounds.Height);
            }
            var client = ClientSize;
            if (client.Width < 200 || client.Height < 200)
            {
                var screen = Screen.FromControl(this).WorkingArea;
                return new Size(screen.Width & ~1, screen.Height);
            }
            return new Size(client.Width & ~1, client.Height);
        }

        private void OnLoginComplete(object sender, EventArgs e)
        {
            _loggedIn = true;
            SetState(SessionState.Connected, $"Connected to {Info.Address}" +
                (string.IsNullOrWhiteSpace(Info.UserName) ? "" : $" as {Info.FullUserName}"));
            // Catch up on any resize that happened while logging on.
            _resizeTimer.Stop();
            _resizeTimer.Start();
        }

        private void OnDisconnected(object sender, int reason)
        {
            _resizeTimer.Stop();
            if (_reconnectAfterDisconnect)
            {
                _reconnectAfterDisconnect = false;
                BeginInvoke((Action)Connect);
                return;
            }
            if (_closing)
            {
                _closeTimeout.Stop();
                // Never tear down the ActiveX control from inside one of its own callbacks.
                BeginInvoke((Action)(() => CloseRequested?.Invoke(this, EventArgs.Empty)));
                return;
            }

            string message = _rdp?.DescribeDisconnect(reason) ?? $"Disconnected (code {reason}).";
            // 1 = local disconnect, 2 = remote disconnect by user, 3 = remote disconnect by server
            if (reason == 1 || reason == 2 || reason == 3)
                message = _loggedIn ? $"Disconnected from {Info.Address}." : message;

            SetState(SessionState.Disconnected, message);
            BeginInvoke((Action)(() =>
            {
                _overlay.Visible = true;
                _overlay.BringToFront();
            }));
        }

        public void Disconnect()
        {
            if (_rdp != null && _rdp.IsConnected)
                Try(() => _rdp.Ocx.Disconnect());
        }

        /// <summary>Disconnects cleanly first, then raises CloseRequested.</summary>
        public void RequestClose()
        {
            if (_closing) return;
            _closing = true;
            if (_rdp != null && _rdp.IsConnected)
            {
                _closeTimeout.Start();
                Try(() => _rdp.Ocx.Disconnect());
            }
            else
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        public void ToggleFullScreen()
        {
            if (State != SessionState.Connected) return;
            Try(() => _rdp.Ocx.FullScreen = !(bool)_rdp.Ocx.FullScreen);
        }

        public bool SmartSizing
        {
            get => Info.SmartSizing;
            set
            {
                Info.SmartSizing = value;
                if (_rdp != null) Try(() => _rdp.Ocx.AdvancedSettings2.SmartSizing = value);
                if (!value) ApplyWindowSize();
            }
        }

        public void FocusRemote()
        {
            if (_rdp != null && State == SessionState.Connected) Try(() => _rdp.Focus());
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (State == SessionState.Connected)
            {
                _resizeTimer.Stop();
                _resizeTimer.Start();
            }
        }

        private void ApplyWindowSize()
        {
            if (_rdp == null || !_loggedIn || Info.Width > 0 || Info.SmartSizing) return;
            if (ClientSize.Width < 200 || ClientSize.Height < 200) return;
            try { if ((bool)_rdp.Ocx.FullScreen) return; } catch { }
            _rdp.UpdateSessionSize(ClientSize.Width & ~1, ClientSize.Height);
        }

        private void SetState(SessionState state, string text)
        {
            State = state;
            StatusText = text;
            _statusLabel.Text = text;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DestroyClient()
        {
            if (_rdp == null) return;
            var old = _rdp;
            _rdp = null;
            try
            {
                Controls.Remove(old);
                old.Dispose();
            }
            catch { /* the control may already be gone */ }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                TabbedRdp.Theme.Changed -= OnThemeChanged;
                _resizeTimer.Dispose();
                _closeTimeout.Dispose();
                DestroyClient();
            }
            base.Dispose(disposing);
        }

        private static void Try(Action action)
        {
            try { action(); } catch { /* property not supported by this control version */ }
        }

        private static object TryGet(Func<object> getter)
        {
            try { return getter(); } catch { return null; }
        }
    }
}
