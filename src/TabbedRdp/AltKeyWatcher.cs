using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TabbedRdp
{
    /// <summary>
    /// Detects a lone tap of the left Alt key (press and release with nothing in between), like
    /// Explorer's hidden menu bar. A low-level hook is needed because a focused remote session
    /// consumes keystrokes before our window would see them. Keys are never swallowed.
    /// </summary>
    public sealed class AltKeyWatcher : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        private const int VK_LMENU = 0xA4;
        private const int LLKHF_INJECTED = 0x10;
        private const int MaxTapMs = 800;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);

        private readonly LowLevelKeyboardProc _proc; // keep the delegate alive
        private readonly Func<bool> _isActive;
        private IntPtr _hook;
        private bool _altDown;
        private bool _clean;
        private int _downAt;

        public event EventHandler AltTapped;

        /// <param name="isActive">Only report taps while this returns true (e.g. our window is in the foreground).</param>
        public AltKeyWatcher(Func<bool> isActive)
        {
            _isActive = isActive;
            _proc = Callback;
            using (var module = Process.GetCurrentProcess().MainModule)
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
        }

        public bool IsInstalled => _hook != IntPtr.Zero;

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    int msg = (int)wParam;
                    bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                    bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;

                    if (kb.vkCode == VK_LMENU)
                    {
                        if (down && !_altDown)
                        {
                            _altDown = true;
                            _clean = (kb.flags & LLKHF_INJECTED) == 0;
                            _downAt = Environment.TickCount;
                        }
                        else if (up)
                        {
                            bool tap = _altDown && _clean && unchecked(Environment.TickCount - _downAt) < MaxTapMs;
                            _altDown = false;
                            _clean = false;
                            if (tap && _isActive()) AltTapped?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    else if (down)
                    {
                        _clean = false; // Alt+something is a shortcut, not a tap
                    }
                }
                catch { /* never break the keyboard */ }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>Call when a mouse button is used while Alt is held (Alt+click is not a tap).</summary>
        public void CancelTap() => _clean = false;

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Stops Windows from entering system-menu mode when Alt is tapped in one of our own controls
    /// (that would swallow the next key), and reports the tap when the hook isn't in use.
    /// </summary>
    public sealed class AltMessageFilter : IMessageFilter
    {
        private const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105, VK_MENU = 0x12;
        private readonly Form _form;
        private bool _clean;
        public event EventHandler AltTapped;

        public AltMessageFilter(Form form) { _form = form; }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN)
            {
                bool repeat = ((long)m.LParam & (1L << 30)) != 0;
                if ((int)m.WParam == VK_MENU) { if (!repeat) _clean = true; }
                else _clean = false;
                return false;
            }
            if (m.Msg != WM_SYSKEYUP || (int)m.WParam != VK_MENU) return false;

            var control = Control.FromHandle(m.HWnd);
            if (control == null || control.FindForm() != _form) return false; // remote session windows, dialogs
            if (_clean) AltTapped?.Invoke(this, EventArgs.Empty);
            _clean = false;
            return true;
        }
    }
}
