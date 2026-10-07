using System;
using System.Runtime.Serialization;

namespace TabbedRdp
{
    public enum AudioMode
    {
        PlayLocally = 0,
        PlayOnRemote = 1,
        DoNotPlay = 2,
    }

    [DataContract]
    public class ConnectionInfo
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Group { get; set; }
        [DataMember] public string Host { get; set; }
        [DataMember] public int Port { get; set; }
        [DataMember] public string UserName { get; set; }
        [DataMember] public string Domain { get; set; }
        [DataMember] public bool SavePassword { get; set; }
        /// <summary>DPAPI-protected (current Windows user only), base64.</summary>
        [DataMember] public string ProtectedPassword { get; set; }

        /// <summary>0 = fit to window.</summary>
        [DataMember] public int Width { get; set; }
        [DataMember] public int Height { get; set; }
        [DataMember] public int ColorDepth { get; set; }
        [DataMember] public bool SmartSizing { get; set; }
        [DataMember] public bool AdminSession { get; set; }
        [DataMember] public bool UseNla { get; set; }
        [DataMember] public bool RedirectClipboard { get; set; }
        [DataMember] public bool RedirectDrives { get; set; }
        [DataMember] public bool RedirectPrinters { get; set; }
        [DataMember] public bool RedirectSmartCards { get; set; }
        [DataMember] public AudioMode Audio { get; set; }
        [DataMember] public string Notes { get; set; }

        // ---- the rest of the standard .rdp settings ----
        [DataMember] public bool FullScreen { get; set; }
        [DataMember] public bool RedirectPorts { get; set; }
        [DataMember] public bool AudioCapture { get; set; }
        [DataMember] public int AuthenticationLevel { get; set; }
        [DataMember] public bool PromptForCredentials { get; set; }
        [DataMember] public bool AutoReconnect { get; set; }
        [DataMember] public bool DisplayConnectionBar { get; set; }
        [DataMember] public bool PinConnectionBar { get; set; }
        /// <summary>0 = this computer, 1 = remote computer, 2 = remote only in full screen.</summary>
        [DataMember] public int KeyboardHook { get; set; }
        [DataMember] public string AlternateShell { get; set; }
        [DataMember] public string WorkingDirectory { get; set; }
        [DataMember] public string LoadBalanceInfo { get; set; }
        [DataMember] public string GatewayHost { get; set; }
        /// <summary>.rdp gatewayusagemethod: 0/4 = don't use, 1 = always, 2 = bypass for local.</summary>
        [DataMember] public int GatewayUsage { get; set; }
        [DataMember] public int GatewayCredentialsSource { get; set; }
        [DataMember] public bool GatewayUseSameCredentials { get; set; }
        [DataMember] public bool DisableWallpaper { get; set; }
        [DataMember] public bool DisableFullWindowDrag { get; set; }
        [DataMember] public bool DisableMenuAnimations { get; set; }
        [DataMember] public bool DisableThemes { get; set; }
        [DataMember] public bool DisableCursorSettings { get; set; }
        [DataMember] public bool FontSmoothing { get; set; }
        [DataMember] public bool DesktopComposition { get; set; }

        /// <summary>RDP "PerformanceFlags" bitmask built from the visual-experience settings.</summary>
        public int PerformanceFlags =>
            (DisableWallpaper ? 0x1 : 0) | (DisableFullWindowDrag ? 0x2 : 0) | (DisableMenuAnimations ? 0x4 : 0) |
            (DisableThemes ? 0x8 : 0) | (DisableCursorSettings ? 0x20 | 0x40 : 0) |
            (FontSmoothing ? 0x80 : 0) | (DesktopComposition ? 0x100 : 0);

        /// <summary>Plain-text password, held in memory only.</summary>
        [IgnoreDataMember] public string Password { get; set; }

        public ConnectionInfo() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext _) => SetDefaults();

        private void SetDefaults()
        {
            Id = Guid.NewGuid().ToString("N");
            Port = 3389;
            ColorDepth = 32;
            UseNla = true;
            RedirectClipboard = true;
            Audio = AudioMode.PlayLocally;
            AuthenticationLevel = 2;
            AutoReconnect = true;
            DisplayConnectionBar = true;
            PinConnectionBar = true;
            KeyboardHook = 2;
            FontSmoothing = true;
            DesktopComposition = true;
        }

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Address : Name.Trim();

        public string Address => Port == 3389 || Port <= 0 ? Host : $"{Host}:{Port}";

        public string FullUserName =>
            string.IsNullOrWhiteSpace(Domain) ? UserName : $@"{Domain}\{UserName}";

        public ConnectionInfo Clone(bool newId = false)
        {
            var copy = (ConnectionInfo)MemberwiseClone();
            if (newId) copy.Id = Guid.NewGuid().ToString("N");
            return copy;
        }

        /// <summary>A new connection that starts from these (default) settings, without any identity.</summary>
        public ConnectionInfo NewFromTemplate()
        {
            var c = Clone(newId: true);
            c.Host = null;
            c.Port = 3389;
            c.Name = null;
            c.Group = null;
            c.UserName = null;
            c.Domain = null;
            c.Password = null;
            c.SavePassword = false;
            c.ProtectedPassword = null;
            c.Notes = null;
            c.LoadBalanceInfo = null;
            return c;
        }

        /// <summary>Parses "host", "host:port", "[ipv6]:port".</summary>
        public static bool TryParseAddress(string text, out string host, out int port)
        {
            host = null;
            port = 3389;
            text = (text ?? "").Trim();
            if (text.Length == 0) return false;

            if (text.StartsWith("["))
            {
                int end = text.IndexOf(']');
                if (end < 0) return false;
                host = text.Substring(1, end - 1);
                string rest = text.Substring(end + 1);
                if (rest.StartsWith(":") && !int.TryParse(rest.Substring(1), out port)) return false;
                return host.Length > 0;
            }

            int colon = text.LastIndexOf(':');
            if (colon > 0 && text.IndexOf(':') == colon)
            {
                host = text.Substring(0, colon);
                return int.TryParse(text.Substring(colon + 1), out port) && port > 0 && port < 65536;
            }

            host = text;
            return true;
        }

        /// <summary>Splits "DOMAIN\user" into its parts (user@domain is passed through as-is).</summary>
        public void SetFullUserName(string value)
        {
            value = (value ?? "").Trim();
            int slash = value.IndexOf('\\');
            if (slash > 0)
            {
                Domain = value.Substring(0, slash);
                UserName = value.Substring(slash + 1);
            }
            else
            {
                Domain = "";
                UserName = value;
            }
        }
    }
}
