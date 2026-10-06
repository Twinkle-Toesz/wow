using System;
using System.Collections.Generic;
using System.IO;

namespace TabbedRdp
{
    /// <summary>Reads the common settings out of a standard mstsc .rdp file.</summary>
    public static class RdpFile
    {
        public static ConnectionInfo Load(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in File.ReadAllLines(path))
            {
                // Format: "name:type:value", e.g. "full address:s:server01:3390"
                var parts = raw.Split(new[] { ':' }, 3);
                if (parts.Length == 3) values[parts[0].Trim()] = parts[2].Trim();
            }

            var info = new ConnectionInfo { Name = Path.GetFileNameWithoutExtension(path) };

            if (values.TryGetValue("full address", out var address) &&
                ConnectionInfo.TryParseAddress(address, out var host, out var port))
            {
                info.Host = host;
                info.Port = port;
            }
            if (Int(values, "server port") is int serverPort && serverPort > 0 && info.Port == 3389)
                info.Port = serverPort;

            if (values.TryGetValue("username", out var user)) info.SetFullUserName(user);
            if (values.TryGetValue("domain", out var domain) && !string.IsNullOrWhiteSpace(domain)) info.Domain = domain;

            // screen mode id: 1 = windowed, 2 = full screen. Use fixed size only for windowed files that specify one.
            if (Int(values, "screen mode id") == 1 && Int(values, "desktopwidth") is int w && Int(values, "desktopheight") is int h && w > 0 && h > 0)
            {
                info.Width = w;
                info.Height = h;
            }

            if (Int(values, "session bpp") is int bpp) info.ColorDepth = bpp;
            if (Int(values, "smart sizing") is int smart) info.SmartSizing = smart != 0;
            if (Int(values, "administrative session") is int admin) info.AdminSession = admin != 0;
            if (Int(values, "enablecredsspsupport") is int nla) info.UseNla = nla != 0;
            if (Int(values, "redirectclipboard") is int clip) info.RedirectClipboard = clip != 0;
            if (Int(values, "redirectprinters") is int printers) info.RedirectPrinters = printers != 0;
            if (Int(values, "redirectsmartcards") is int cards) info.RedirectSmartCards = cards != 0;
            if (Int(values, "audiomode") is int audio && audio >= 0 && audio <= 2) info.Audio = (AudioMode)audio;
            if (values.TryGetValue("drivestoredirect", out var drives)) info.RedirectDrives = !string.IsNullOrWhiteSpace(drives);

            if (string.IsNullOrWhiteSpace(info.Host))
                throw new InvalidDataException("The .rdp file has no \"full address\".");
            return info;
        }

        private static int? Int(Dictionary<string, string> values, string key) =>
            values.TryGetValue(key, out var s) && int.TryParse(s, out var v) ? v : (int?)null;
    }
}
