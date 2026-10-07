using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ITypeLib = System.Runtime.InteropServices.ComTypes.ITypeLib;
using ITypeInfo = System.Runtime.InteropServices.ComTypes.ITypeInfo;
using TYPEATTR = System.Runtime.InteropServices.ComTypes.TYPEATTR;
using TYPEKIND = System.Runtime.InteropServices.ComTypes.TYPEKIND;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TabbedRdp
{
    /// <summary>
    /// The subset of the mstscax event dispinterface we listen to. DISPIDs come from the
    /// MSTSCLib type library; events we don't declare are simply ignored by the CLR.
    /// </summary>
    [ComImport, Guid("336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface IMsTscAxEvents
    {
        [DispId(1)] void OnConnecting();
        [DispId(2)] void OnConnected();
        [DispId(3)] void OnLoginComplete();
        [DispId(4)] void OnDisconnected(int discReason);
        [DispId(6)] void OnLeaveFullScreenMode();
        [DispId(10)] void OnFatalError(int errorCode);
    }

    /// <summary>Only the first vtable slot (ClearTextPassword setter) is needed.</summary>
    [ComImport, Guid("C1E6743A-41C1-4A74-832A-0DD06C1C7A0E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMsTscNonScriptable
    {
        string ClearTextPassword { [param: MarshalAs(UnmanagedType.BStr)] set; }
    }

    /// <summary>
    /// IMsRdpClientNonScriptable3 with its inherited slots spelled out (vtable order matters, names don't).
    /// Used for PromptForCredentials and the parent window of that prompt.
    /// </summary>
    [ComImport, Guid("B3378D90-0728-45C7-8ED7-B6159FB92219"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMsRdpClientNonScriptable3
    {
        // IMsTscNonScriptable
        void put_ClearTextPassword([MarshalAs(UnmanagedType.BStr)] string value);
        void put_PortablePassword([MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string get_PortablePassword();
        void put_PortableSalt([MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string get_PortableSalt();
        void put_BinaryPassword([MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string get_BinaryPassword();
        void put_BinarySalt([MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string get_BinarySalt();
        void ResetPassword();
        // IMsRdpClientNonScriptable
        void NotifyRedirectDeviceChange(IntPtr wParam, IntPtr lParam);
        void SendKeys(int numKeys, IntPtr pbArrayKeyUp, IntPtr plKeyData);
        // IMsRdpClientNonScriptable2
        void put_UIParentWindowHandle(IntPtr hwnd);
        IntPtr get_UIParentWindowHandle();
        // IMsRdpClientNonScriptable3
        void put_ShowRedirectionWarningDialog([MarshalAs(UnmanagedType.VariantBool)] bool value);
        [return: MarshalAs(UnmanagedType.VariantBool)] bool get_ShowRedirectionWarningDialog();
        void put_PromptForCredentials([MarshalAs(UnmanagedType.VariantBool)] bool value);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class RdpEventSink : IMsTscAxEvents
    {
        private readonly RdpClientHost _host;
        internal RdpEventSink(RdpClientHost host) { _host = host; }

        public void OnConnecting() => _host.RaiseConnecting();
        public void OnConnected() => _host.RaiseConnected();
        public void OnLoginComplete() => _host.RaiseLoginComplete();
        public void OnDisconnected(int discReason) => _host.RaiseDisconnected(discReason);
        public void OnLeaveFullScreenMode() => _host.RaiseLeftFullScreen();
        public void OnFatalError(int errorCode) => _host.RaiseFatalError(errorCode);
    }

    /// <summary>
    /// Hosts the Remote Desktop ActiveX control (mstscax.dll — the same engine mstsc.exe uses)
    /// without needing generated interop assemblies. The newest "MsRdpClientNNotSafeForScripting"
    /// class available on the machine is discovered from the type library at runtime.
    /// </summary>
    public sealed class RdpClientHost : AxHost
    {
        private static string _clsid;
        private AxHost.ConnectionPointCookie _cookie;

        public event EventHandler Connecting;
        public event EventHandler Connected;
        public event EventHandler LoginComplete;
        public event EventHandler<int> Disconnected;
        public event EventHandler<int> FatalError;
        public event EventHandler LeftFullScreen;

        public RdpClientHost() : base(ResolveClsid()) { }

        public static string ClassName { get; private set; } = "unknown";

        /// <summary>Late-bound access to the control's default IMsRdpClientN interface.</summary>
        public dynamic Ocx => GetOcx();

        public bool IsConnected
        {
            get { try { return (int)Ocx.Connected != 0; } catch { return false; } }
        }

        public void SetPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return;
            try
            {
                ((IMsTscNonScriptable)GetOcx()).ClearTextPassword = password;
            }
            catch
            {
                Ocx.AdvancedSettings2.ClearTextPassword = password;
            }
        }

        public string DescribeDisconnect(int reason)
        {
            try
            {
                uint ext = 0;
                try { ext = Convert.ToUInt32(Ocx.ExtendedDisconnectReason); } catch { }
                string text = Ocx.GetErrorDescription((uint)reason, ext);
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            catch { }
            return $"Disconnected (code {reason}).";
        }

        /// <summary>Resizes the remote desktop to match the window (RDP 8.1+ servers).</summary>
        public bool UpdateSessionSize(int width, int height)
        {
            try
            {
                Ocx.UpdateSessionDisplaySettings((uint)width, (uint)height, (uint)width, (uint)height, 0u, 100u, 100u);
                return true;
            }
            catch { return false; }
        }

        protected override void CreateSink()
        {
            try { _cookie = new AxHost.ConnectionPointCookie(GetOcx(), new RdpEventSink(this), typeof(IMsTscAxEvents)); }
            catch { _cookie = null; }
        }

        protected override void DetachSink()
        {
            try { _cookie?.Disconnect(); } catch { }
            _cookie = null;
        }

        internal void RaiseConnecting() => Connecting?.Invoke(this, EventArgs.Empty);
        internal void RaiseConnected() => Connected?.Invoke(this, EventArgs.Empty);
        internal void RaiseLoginComplete() => LoginComplete?.Invoke(this, EventArgs.Empty);
        internal void RaiseDisconnected(int reason) => Disconnected?.Invoke(this, reason);
        internal void RaiseFatalError(int code) => FatalError?.Invoke(this, code);
        internal void RaiseLeftFullScreen() => LeftFullScreen?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Lets the control show the standard Windows credential prompt (like mstsc) when no password
        /// was supplied, instead of failing the logon.
        /// </summary>
        public void EnableCredentialPrompt(IntPtr parentWindow)
        {
            try
            {
                var ns = (IMsRdpClientNonScriptable3)GetOcx();
                if (parentWindow != IntPtr.Zero) ns.put_UIParentWindowHandle(parentWindow);
                ns.put_PromptForCredentials(true);
            }
            catch { /* older control: the server-side logon screen is used instead */ }
        }

        // ---- CLSID discovery -------------------------------------------------------------

        // Last-resort fallbacks if the type library can't be read.
        private static readonly (string Name, string Clsid)[] Fallbacks =
        {
            ("MsRdpClient9NotSafeForScripting", "8B918B82-7985-4C24-89DF-C33AD2BBFBCD"),
            ("MsTscAxNotSafeForScripting", "A41A4187-5A86-4E26-B40A-856F9035D9CB"),
        };

        private static string ResolveClsid()
        {
            if (_clsid != null) return _clsid;

            var tried = new List<string>();
            var candidates = FindClassesInTypeLib().Concat(Fallbacks.Select(f => (f.Name, new Guid(f.Clsid))));
            foreach (var (name, guid) in candidates.GroupBy(c => c.Item2).Select(g => g.First()))
            {
                if (!IsRegistered(guid)) continue;
                // A class can be listed and registered yet still refuse to instantiate
                // (CLASS_E_CLASSNOTAVAILABLE), so prove it works before handing it to AxHost.
                if (!CanCreate(guid, out string error))
                {
                    tried.Add($"{name}: {error}");
                    continue;
                }
                ClassName = name;
                return _clsid = guid.ToString();
            }
            throw new InvalidOperationException(
                "No usable Remote Desktop ActiveX control (mstscax.dll) was found on this machine." +
                (tried.Count > 0 ? "\n\nTried:\n" + string.Join("\n", tried) : ""));
        }

        private static bool CanCreate(Guid clsid, out string error)
        {
            error = null;
            object instance = null;
            try
            {
                instance = Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, true));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
            }
        }

        private static bool IsRegistered(Guid clsid)
        {
            using (var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid:B}\InprocServer32"))
                return key != null;
        }

        [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void LoadTypeLibEx(string szFile, int regkind, out ITypeLib pptlib);

        private static IEnumerable<(string Name, Guid Clsid)> FindClassesInTypeLib()
        {
            var found = new List<(int Rank, string Name, Guid Clsid)>();
            try
            {
                string path = Path.Combine(Environment.SystemDirectory, "mstscax.dll");
                LoadTypeLibEx(path, 2 /* REGKIND_NONE */, out ITypeLib typeLib);
                // MsRdpClient<N>NotSafeForScripting (preferred), MsRdpClient<N>, and the base MsTscAx classes.
                var pattern = new Regex(@"^(?:MsRdpClient(\d*)|MsTscAx)(NotSafeForScripting)?$");

                int count = typeLib.GetTypeInfoCount();
                for (int i = 0; i < count; i++)
                {
                    typeLib.GetTypeInfoType(i, out TYPEKIND kind);
                    if (kind != TYPEKIND.TKIND_COCLASS) continue;

                    typeLib.GetDocumentation(i, out string name, out _, out _, out _);
                    var m = pattern.Match(name ?? "");
                    if (!m.Success) continue;

                    typeLib.GetTypeInfo(i, out ITypeInfo info);
                    info.GetTypeAttr(out IntPtr attrPtr);
                    try
                    {
                        var attr = Marshal.PtrToStructure<TYPEATTR>(attrPtr);
                        int version = name.StartsWith("MsTscAx") ? 0
                            : m.Groups[1].Value.Length == 0 ? 1 : int.Parse(m.Groups[1].Value);
                        bool notSafe = m.Groups[2].Success;
                        found.Add(((notSafe ? 1000 : 0) + version, name, attr.guid));
                    }
                    finally { info.ReleaseTypeAttr(attrPtr); }
                }
            }
            catch { /* fall back to hard-coded CLSIDs */ }

            return found.OrderByDescending(f => f.Rank).Select(f => (f.Name, f.Clsid)).ToList();
        }
    }
}
