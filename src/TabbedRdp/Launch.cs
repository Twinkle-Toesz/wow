using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace TabbedRdp
{
    /// <summary>What one launch of the exe asks for — parsed from mstsc-style arguments.</summary>
    [DataContract]
    public sealed class LaunchRequest
    {
        [DataMember] public List<ConnectionInfo> Connections { get; set; } = new List<ConnectionInfo>();
        /// <summary>Passwords travel separately because ConnectionInfo.Password is never serialized.</summary>
        [DataMember] public List<string> Passwords { get; set; } = new List<string>();
        [DataMember] public bool NewWindow { get; set; }

        public void Add(ConnectionInfo info)
        {
            Connections.Add(info);
            Passwords.Add(info.Password);
        }

        public IEnumerable<ConnectionInfo> Resolve()
        {
            for (int i = 0; i < Connections.Count; i++)
            {
                if (i < Passwords.Count) Connections[i].Password = Passwords[i];
                yield return Connections[i];
            }
        }
    }

    /// <summary>Parses the same arguments mstsc.exe accepts, so web/agent connectors can call this exe instead.</summary>
    public static class LaunchParser
    {
        /// <summary>
        /// mstsc switches the embedded control can't reproduce: the launch is handed to the real mstsc.exe.
        /// </summary>
        private static readonly string[] MstscOnlySwitches =
        {
            "shadow", "control", "noconsentprompt", "span", "multimon", "restrictedadmin", "remoteguard",
            "edit", "migrate", "l", "public", "wsa",
        };

        public sealed class Result
        {
            public LaunchRequest Request = new LaunchRequest();
            /// <summary>Non-null: run mstsc.exe with the original arguments, for this reason.</summary>
            public string DelegateReason;
            public List<string> Errors = new List<string>();
        }

        /// <param name="defaults">The user's default settings, used for anything the launch doesn't specify.</param>
        public static Result Parse(string[] args, ConnectionInfo defaults = null)
        {
            var result = new Result();
            if (args == null || args.Length == 0) return result;

            ConnectionInfo current = null;
            string pendingHost = null;
            int? width = null, height = null;
            bool fullScreen = false, admin = false, prompt = false;
            string gateway = null, user = null, password = null;

            foreach (var raw in args)
            {
                string arg = (raw ?? "").Trim().Trim('"');
                if (arg.Length == 0) continue;

                if (!File.Exists(arg) && (arg.StartsWith("/") || (arg.StartsWith("-") && arg.Length > 1)))
                {
                    string body = arg.Substring(1);
                    int colon = body.IndexOf(':');
                    string key = (colon < 0 ? body : body.Substring(0, colon)).ToLowerInvariant();
                    string value = colon < 0 ? null : body.Substring(colon + 1);

                    switch (key)
                    {
                        case "v": pendingHost = value; break;
                        case "w": if (int.TryParse(value, out var w)) width = w; break;
                        case "h": if (int.TryParse(value, out var h)) height = h; break;
                        case "f": fullScreen = true; break;
                        case "admin": case "console": admin = true; break;
                        case "prompt": prompt = true; break;
                        case "g": gateway = value; break;
                        // Not mstsc switches, but handy for connectors that can't write files.
                        case "u": case "user": user = value; break;
                        case "p": case "password": password = value; break;
                        case "newwindow": result.Request.NewWindow = true; break;
                        default:
                            if (MstscOnlySwitches.Contains(key)) result.DelegateReason = result.DelegateReason ?? "/" + key;
                            break; // unknown switches are ignored, like mstsc does
                    }
                    continue;
                }

                // Bare argument: an .rdp file, or a host name.
                if (File.Exists(arg))
                {
                    try
                    {
                        var values = RdpFile.ReadValues(arg);
                        Log.Write($"  rdp {arg}: {RdpFile.Describe(values)}");
                        result.DelegateReason = result.DelegateReason ?? RdpFile.NeedsMstsc(values);
                        // Connectors usually write throw-away files to %TEMP%: name those tabs after the host instead.
                        bool temp = Path.GetFullPath(arg).StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase);
                        current = RdpFile.FromValues(values, temp ? null : Path.GetFileNameWithoutExtension(arg), defaults);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"{Path.GetFileName(arg)}: {ex.Message}");
                    }
                }
                else if (!arg.EndsWith(".rdp", StringComparison.OrdinalIgnoreCase))
                {
                    pendingHost = pendingHost ?? arg;
                }
                else
                {
                    result.Errors.Add($"File not found: {arg}");
                }
            }

            if (current == null && pendingHost == null) return result;
            if (current == null) current = defaults?.NewFromTemplate() ?? new ConnectionInfo();

            // Command-line switches override the .rdp file, exactly like mstsc.
            if (pendingHost != null && ConnectionInfo.TryParseAddress(pendingHost, out var host, out var port))
            {
                if (!string.Equals(current.Host, host, StringComparison.OrdinalIgnoreCase)) current.Name = null;
                current.Host = host;
                current.Port = port;
            }
            if (width > 0 && height > 0 && !fullScreen) { current.Width = width.Value; current.Height = height.Value; }
            if (fullScreen) { current.FullScreen = true; current.Width = current.Height = 0; }
            if (admin) current.AdminSession = true;
            if (gateway != null) { current.GatewayHost = gateway; current.GatewayUsage = 1; }
            if (user != null) current.SetFullUserName(user);
            if (password != null) current.Password = password;
            if (prompt) { current.PromptForCredentials = true; current.Password = null; }

            if (string.IsNullOrWhiteSpace(current.Host))
                result.Errors.Add("No computer to connect to (missing /v: or \"full address\").");
            else
                result.Request.Add(current);
            return result;
        }

        /// <summary>Hands the exact original arguments to the real Remote Desktop client.</summary>
        public static void RunMstsc(string[] args)
        {
            string mstsc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");
            string commandLine = string.Join(" ", args.Select(Quote));
            Log.Write($"  -> mstsc.exe {Mask(commandLine)}");
            Process.Start(new ProcessStartInfo(mstsc, commandLine) { UseShellExecute = false });
        }

        private static string Quote(string arg) =>
            arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? arg : "\"" + arg.Replace("\"", "\\\"") + "\"";

        public static string Mask(string commandLine)
        {
            var parts = commandLine.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                string lower = parts[i].TrimStart('"').ToLowerInvariant();
                if (lower.StartsWith("/p:") || lower.StartsWith("/password:") || lower.StartsWith("-p:"))
                    parts[i] = parts[i].Substring(0, parts[i].IndexOf(':') + 1) + "***";
            }
            return string.Join(" ", parts);
        }
    }

    /// <summary>Saved Remote Desktop credentials (Windows Credential Manager "TERMSRV/host"), like mstsc uses.</summary>
    public static class SavedCredentials
    {
        private const int CRED_TYPE_GENERIC = 1;
        private const int CRED_TYPE_DOMAIN_PASSWORD = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        /// <summary>
        /// Fills in user name / password from Credential Manager. Generic credentials (cmdkey /generic:TERMSRV/host)
        /// give both; domain credentials (cmdkey /add, or "Remember me" in mstsc) only expose the user name to apps.
        /// </summary>
        public static void Apply(ConnectionInfo info)
        {
            if (!string.IsNullOrEmpty(info.Password) || string.IsNullOrWhiteSpace(info.Host)) return;

            var targets = new List<string> { "TERMSRV/" + info.Host };
            if (info.Port != 3389) targets.Insert(0, $"TERMSRV/{info.Host}:{info.Port}");

            foreach (var target in targets)
            foreach (var type in new[] { CRED_TYPE_GENERIC, CRED_TYPE_DOMAIN_PASSWORD })
            {
                if (!CredRead(target, type, 0, out var ptr)) continue;
                try
                {
                    var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                    if (string.IsNullOrWhiteSpace(info.UserName) && !string.IsNullOrWhiteSpace(cred.UserName))
                        info.SetFullUserName(cred.UserName);
                    if (cred.CredentialBlob != IntPtr.Zero && cred.CredentialBlobSize > 0)
                    {
                        info.Password = Marshal.PtrToStringUni(cred.CredentialBlob, cred.CredentialBlobSize / 2).TrimEnd('\0');
                        Log.Write($"  credentials for {info.Host} taken from Credential Manager ({target})");
                        return;
                    }
                }
                finally { CredFree(ptr); }
            }
        }
    }

    /// <summary>%APPDATA%\TabbedRDP\launch.log — what connectors sent us (passwords masked).</summary>
    public static class Log
    {
        public static string FilePath => Path.Combine(AppState.Folder, "launch.log");

        public static void Write(string line)
        {
            try
            {
                Directory.CreateDirectory(AppState.Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > 512 * 1024)
                    File.Copy(FilePath, FilePath + ".old", true);
                bool append = !(file.Exists && file.Length > 512 * 1024);
                using (var writer = new StreamWriter(FilePath, append, Encoding.UTF8))
                    writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Process.GetCurrentProcess().Id}] {line}");
            }
            catch { /* logging must never break a connection */ }
        }
    }
}
