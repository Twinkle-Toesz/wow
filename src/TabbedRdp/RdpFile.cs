using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TabbedRdp
{
    /// <summary>Reads a standard mstsc .rdp file into a <see cref="ConnectionInfo"/>.</summary>
    public static class RdpFile
    {
        /// <summary>Raw "name → value" pairs of a .rdp file (names lower-case).</summary>
        public static Dictionary<string, string> ReadValues(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // mstsc writes UTF-16 LE with BOM; generated files are often UTF-8/ANSI. ReadAllLines detects the BOM.
            foreach (var raw in File.ReadAllLines(path))
            {
                // Format: "name:type:value", e.g. "full address:s:server01:3390"
                var parts = raw.Split(new[] { ':' }, 3);
                if (parts.Length == 3) values[parts[0].Trim().ToLowerInvariant()] = parts[2].Trim();
            }
            return values;
        }

        public static ConnectionInfo Load(string path) => FromValues(ReadValues(path), Path.GetFileNameWithoutExtension(path));

        public static ConnectionInfo FromValues(Dictionary<string, string> v, string name)
        {
            var info = new ConnectionInfo { Name = name };

            // --- target ---
            string address = Str(v, "full address") ?? Str(v, "alternate full address");
            if (address != null && ConnectionInfo.TryParseAddress(address, out var host, out var port))
            {
                info.Host = host;
                info.Port = port;
            }
            if (Int(v, "server port") is int serverPort && serverPort > 0 && info.Port == 3389) info.Port = serverPort;
            if (string.IsNullOrWhiteSpace(info.Host))
                throw new InvalidDataException("The .rdp file has no \"full address\".");

            // --- credentials ---
            if (Str(v, "username") is string user) info.SetFullUserName(user);
            if (Str(v, "domain") is string domain) info.Domain = domain;
            if (Str(v, "password 51") is string blob) info.Password = DecryptPassword(blob);
            else if (Str(v, "password") is string plain) info.Password = plain; // non-standard, but some tools write it
            if (Int(v, "prompt for credentials") is int prompt) info.PromptForCredentials = prompt != 0;
            if (Int(v, "enablecredsspsupport") is int nla) info.UseNla = nla != 0;
            if (Int(v, "authentication level") is int auth) info.AuthenticationLevel = auth;
            if (Int(v, "administrative session") is int admin) info.AdminSession = admin != 0;
            info.LoadBalanceInfo = Str(v, "loadbalanceinfo");

            // --- display ---
            // screen mode id: 1 = window, 2 = full screen
            info.FullScreen = Int(v, "screen mode id") == 2;
            bool dynamic = (Int(v, "dynamic resolution") ?? 1) != 0;
            info.SmartSizing = (Int(v, "smart sizing") ?? 0) != 0;
            int w = Int(v, "desktopwidth") ?? 0, h = Int(v, "desktopheight") ?? 0;
            // Dynamic resolution = follow the tab size; otherwise keep the fixed size from the file.
            if (w > 0 && h > 0 && (!dynamic || info.SmartSizing) && !info.FullScreen)
            {
                info.Width = w;
                info.Height = h;
            }
            if (Int(v, "session bpp") is int bpp) info.ColorDepth = bpp;
            if (Int(v, "displayconnectionbar") is int bar) info.DisplayConnectionBar = bar != 0;
            if (Int(v, "pinconnectionbar") is int pin) info.PinConnectionBar = pin != 0;

            // --- local resources ---
            if (Int(v, "audiomode") is int audio && audio >= 0 && audio <= 2) info.Audio = (AudioMode)audio;
            if (Int(v, "audiocapturemode") is int mic) info.AudioCapture = mic != 0;
            if (Int(v, "keyboardhook") is int hook && hook >= 0 && hook <= 2) info.KeyboardHook = hook;
            if (Int(v, "redirectclipboard") is int clip) info.RedirectClipboard = clip != 0;
            if (Int(v, "redirectprinters") is int printers) info.RedirectPrinters = printers != 0;
            if (Int(v, "redirectsmartcards") is int cards) info.RedirectSmartCards = cards != 0;
            if (Int(v, "redirectcomports") is int com) info.RedirectPorts = com != 0;
            if (v.TryGetValue("drivestoredirect", out var drives)) info.RedirectDrives = !string.IsNullOrWhiteSpace(drives);
            else if (Int(v, "redirectdrives") is int rd) info.RedirectDrives = rd != 0;

            // --- program / experience ---
            info.AlternateShell = Str(v, "alternate shell");
            info.WorkingDirectory = Str(v, "shell working directory");
            if (Int(v, "autoreconnection enabled") is int reconnect) info.AutoReconnect = reconnect != 0;
            if (Int(v, "disable wallpaper") is int wp) info.DisableWallpaper = wp != 0;
            if (Int(v, "disable full window drag") is int drag) info.DisableFullWindowDrag = drag != 0;
            if (Int(v, "disable menu anims") is int menu) info.DisableMenuAnimations = menu != 0;
            if (Int(v, "disable themes") is int themes) info.DisableThemes = themes != 0;
            if (Int(v, "disable cursor setting") is int cursor) info.DisableCursorSettings = cursor != 0;
            if (Int(v, "allow font smoothing") is int font) info.FontSmoothing = font != 0;
            if (Int(v, "allow desktop composition") is int comp) info.DesktopComposition = comp != 0;

            // --- RD Gateway ---
            info.GatewayHost = Str(v, "gatewayhostname");
            if (Int(v, "gatewayusagemethod") is int gwUse) info.GatewayUsage = gwUse;
            if (Int(v, "gatewaycredentialssource") is int gwCreds) info.GatewayCredentialsSource = gwCreds;
            if (Int(v, "promptcredentialonce") is int once) info.GatewayUseSameCredentials = once != 0;

            return info;
        }

        /// <summary>
        /// Settings the embedded RDP control cannot do; such files are handed to the real mstsc.exe.
        /// Returns null when the tabbed client can handle the file.
        /// </summary>
        public static string NeedsMstsc(Dictionary<string, string> v)
        {
            if (Int(v, "use multimon") == 1) return "use all monitors";
            if (Int(v, "span monitors") == 1) return "span monitors";
            if (Int(v, "remoteapplicationmode") == 1) return "RemoteApp";
            if (Int(v, "enablerdsaadauth") == 1) return "Microsoft Entra ID authentication";
            return null;
        }

        /// <summary>Decrypts mstsc's "password 51:b:" value (DPAPI, current user, UTF-16).</summary>
        public static string DecryptPassword(string hex)
        {
            try
            {
                hex = hex.Trim();
                var bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                var plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return Encoding.Unicode.GetString(plain).TrimEnd('\0');
            }
            catch
            {
                return null; // encrypted for another user/machine — fall back to Credential Manager / prompt
            }
        }

        /// <summary>The file's settings with secrets masked, for the launch log.</summary>
        public static string Describe(Dictionary<string, string> v) =>
            string.Join("; ", v.Select(kv => kv.Key + "=" + (kv.Key.StartsWith("password") ? "***" : kv.Value)));

        private static string Str(Dictionary<string, string> v, string key) =>
            v.TryGetValue(key, out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

        private static int? Int(Dictionary<string, string> v, string key) =>
            v.TryGetValue(key, out var s) && int.TryParse(s, out var n) ? n : (int?)null;
    }
}
