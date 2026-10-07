using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace TabbedRdp
{
    /// <summary>Everything persisted between runs: saved connections, recent hosts, window layout.</summary>
    [DataContract]
    public class AppState
    {
        [DataMember] public List<ConnectionInfo> Connections { get; set; } = new List<ConnectionInfo>();
        [DataMember] public List<string> RecentAddresses { get; set; } = new List<string>();
        [DataMember] public string LastUserName { get; set; }
        [DataMember] public bool ShowSidebar { get; set; }
        [DataMember] public int SidebarWidth { get; set; } = 240;
        [DataMember] public ThemeMode Theme { get; set; }
        /// <summary>Use the normal Windows title bar instead of tabs-in-title-bar.</summary>
        [DataMember] public bool StandardTitleBar { get; set; }
        /// <summary>Tapping Alt shows the menu even while a remote session has the keyboard.</summary>
        [DataMember] public bool AltMenuInSessions { get; set; } = true;
        /// <summary>Keep the menu bar docked between the tabs and the session instead of showing it on Alt.</summary>
        [DataMember] public bool PinMenuBar { get; set; }
        /// <summary>Settings every new connection starts from (Options → Save as default).</summary>
        [DataMember] public ConnectionInfo Defaults { get; set; } = new ConnectionInfo();
        [DataMember] public int WindowX { get; set; }
        [DataMember] public int WindowY { get; set; }
        [DataMember] public int WindowWidth { get; set; }
        [DataMember] public int WindowHeight { get; set; }
        [DataMember] public bool WindowMaximized { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext _)
        {
            AltMenuInSessions = true;
            SidebarWidth = 240;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext _)
        {
            Defaults = Defaults ?? new ConnectionInfo();
            Connections = Connections ?? new List<ConnectionInfo>();
            RecentAddresses = RecentAddresses ?? new List<string>();
            if (SidebarWidth < 120) SidebarWidth = 240;
            foreach (var c in Connections) c.Password = Unprotect(c.ProtectedPassword);
        }

        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabbedRDP");

        public static string FilePath => Path.Combine(Folder, "settings.json");

        public static AppState Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var stream = File.OpenRead(FilePath))
                        return (AppState)new DataContractJsonSerializer(typeof(AppState)).ReadObject(stream);
                }
            }
            catch
            {
                // Keep a copy of the unreadable file rather than silently overwriting it.
                try { File.Copy(FilePath, FilePath + ".bad", true); } catch { }
            }
            return new AppState();
        }

        public void Save()
        {
            foreach (var c in Connections)
                c.ProtectedPassword = c.SavePassword ? Protect(c.Password) : null;

            Directory.CreateDirectory(Folder);
            string tmp = FilePath + ".tmp";
            using (var stream = File.Create(tmp))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, true, true, "  "))
            {
                new DataContractJsonSerializer(typeof(AppState)).WriteObject(writer, this);
            }
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }

        public void AddRecent(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return;
            RecentAddresses.RemoveAll(a => string.Equals(a, address, StringComparison.OrdinalIgnoreCase));
            RecentAddresses.Insert(0, address.Trim());
            if (RecentAddresses.Count > 25) RecentAddresses.RemoveRange(25, RecentAddresses.Count - 25);
        }

        public ConnectionInfo FindByAddress(string host, int port) =>
            Connections.FirstOrDefault(c =>
                string.Equals(c.Host, host, StringComparison.OrdinalIgnoreCase) && c.Port == port);

        private static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        private static string Unprotect(string protectedBase64)
        {
            if (string.IsNullOrEmpty(protectedBase64)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return null; }
        }
    }
}
