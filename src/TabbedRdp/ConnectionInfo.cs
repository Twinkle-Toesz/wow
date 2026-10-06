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
