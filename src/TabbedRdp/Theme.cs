using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TabbedRdp
{
    public enum ThemeMode
    {
        System = 0,
        Dark = 1,
        Light = 2,
    }

    public sealed class Palette
    {
        public bool IsDark;
        public Color TitleBar;      // tab strip background
        public Color TabActive;     // selected tab = content colour
        public Color TabHover;
        public Color Window;        // dialog / sidebar background
        public Color Surface;       // nav list, menus
        public Color Input;         // text boxes, combos
        public Color Border;
        public Color Text;
        public Color TextMuted;
        public Color Accent;
        public Color AccentText;
        public Color Hover;
        public Color Selected;

        public static readonly Palette Dark = new Palette
        {
            IsDark = true,
            TitleBar = Color.FromArgb(28, 28, 28),
            TabActive = Color.FromArgb(48, 48, 48),
            TabHover = Color.FromArgb(40, 40, 40),
            Window = Color.FromArgb(32, 32, 32),
            Surface = Color.FromArgb(43, 43, 43),
            Input = Color.FromArgb(55, 55, 55),
            Border = Color.FromArgb(75, 75, 75),
            Text = Color.FromArgb(242, 242, 242),
            TextMuted = Color.FromArgb(160, 160, 160),
            Accent = Color.FromArgb(76, 160, 255),
            AccentText = Color.FromArgb(16, 16, 16),
            Hover = Color.FromArgb(62, 62, 62),
            Selected = Color.FromArgb(70, 70, 70),
        };

        public static readonly Palette Light = new Palette
        {
            IsDark = false,
            TitleBar = Color.FromArgb(222, 225, 230),
            TabActive = Color.FromArgb(255, 255, 255),
            TabHover = Color.FromArgb(236, 238, 241),
            Window = Color.FromArgb(249, 249, 249),
            Surface = Color.FromArgb(243, 243, 243),
            Input = Color.White,
            Border = Color.FromArgb(200, 200, 200),
            Text = Color.FromArgb(26, 26, 26),
            TextMuted = Color.FromArgb(105, 105, 105),
            Accent = Color.FromArgb(0, 103, 192),
            AccentText = Color.White,
            Hover = Color.FromArgb(229, 229, 229),
            Selected = Color.FromArgb(214, 226, 240),
        };
    }

    /// <summary>
    /// Light/dark theming for the whole app. Controls opt out with Tag = "noTheme";
    /// labels use Tag = "muted" / "title", buttons Tag = "primary".
    /// </summary>
    public static class Theme
    {
        public static ThemeMode Mode { get; private set; } = ThemeMode.System;
        public static Palette Current { get; private set; } = Palette.Light;
        public static event EventHandler Changed;

        static Theme()
        {
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (Mode == ThemeMode.System && e.Category == UserPreferenceCategory.General) SetMode(Mode);
            };
        }

        public static void SetMode(ThemeMode mode)
        {
            Mode = mode;
            var next = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemPrefersDark()) ? Palette.Dark : Palette.Light;
            bool changed = next != Current;
            Current = next;
            ToolStripManager.Renderer = new ThemedRenderer();
            if (changed) Changed?.Invoke(null, EventArgs.Empty);
        }

        public static bool SystemPrefersDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
            }
            catch { return false; }
        }

        /// <summary>Themes a form (incl. its title bar on Windows 10 1809+ / 11) and all children.</summary>
        public static void ApplyWindow(Form form)
        {
            Apply(form);
            if (form.IsHandleCreated) SetDarkTitleBar(form.Handle, Current.IsDark);
            else form.HandleCreated += (s, e) => SetDarkTitleBar(form.Handle, Current.IsDark);
        }

        public static void Apply(Control control)
        {
            var p = Current;
            string tag = control.Tag as string;
            if (tag == "noTheme" || control is RdpSession || control is AxHost) return;

            switch (control)
            {
                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.UseVisualStyleBackColor = false;
                    bool primary = tag == "primary";
                    b.BackColor = primary ? p.Accent : p.Surface;
                    b.ForeColor = primary ? p.AccentText : p.Text;
                    b.FlatAppearance.BorderColor = primary ? p.Accent : p.Border;
                    b.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(p.Accent, 0.2f) : p.Hover;
                    b.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(p.Accent, 0.1f) : p.Selected;
                    break;
                case TextBox t:
                    t.BackColor = t.ReadOnly ? p.Surface : p.Input;
                    t.ForeColor = p.Text;
                    t.BorderStyle = BorderStyle.FixedSingle;
                    DarkScrollbars(t);
                    break;
                case ComboBox c:
                    c.BackColor = p.Input;
                    c.ForeColor = p.Text;
                    c.FlatStyle = p.IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                    DarkScrollbars(c);
                    break;
                case NumericUpDown n:
                    n.BackColor = p.Input;
                    n.ForeColor = p.Text;
                    n.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ListBox l:
                    l.BackColor = p.Surface;
                    l.ForeColor = p.Text;
                    DarkScrollbars(l);
                    break;
                case TreeView tv:
                    tv.BackColor = p.Window;
                    tv.ForeColor = p.Text;
                    tv.LineColor = p.Border;
                    DarkScrollbars(tv);
                    break;
                case ToolStrip ts:
                    ts.BackColor = p.Window;
                    ts.ForeColor = p.Text;
                    ts.Renderer = new ThemedRenderer();
                    break;
                case CheckBox cb:
                    cb.ForeColor = p.Text;
                    cb.BackColor = Color.Transparent;
                    break;
                case RadioButton rb:
                    rb.ForeColor = p.Text;
                    rb.BackColor = Color.Transparent;
                    break;
                case LinkLabel ll:
                    ll.LinkColor = p.Accent;
                    ll.ActiveLinkColor = p.Accent;
                    ll.ForeColor = p.Text;
                    ll.BackColor = Color.Transparent;
                    break;
                case Label lb:
                    lb.ForeColor = tag == "muted" ? p.TextMuted : p.Text;
                    lb.BackColor = Color.Transparent;
                    break;
                case Form f:
                    f.BackColor = p.Window;
                    f.ForeColor = p.Text;
                    break;
                default:
                    if (tag != "keepBack") control.BackColor = tag == "surface" ? p.Surface : p.Window;
                    control.ForeColor = p.Text;
                    break;
            }

            foreach (Control child in control.Controls) Apply(child);
        }

        // ---- native bits -------------------------------------------------------------------

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
        {
            int value = dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(hwnd, 20, ref value, 4) != 0)   // DWMWA_USE_IMMERSIVE_DARK_MODE
                    DwmSetWindowAttribute(hwnd, 19, ref value, 4);        // pre-20H1 value
            }
            catch { /* Windows 7/8: no dark title bars */ }
        }

        private static void DarkScrollbars(Control c)
        {
            void Set() { try { SetWindowTheme(c.Handle, Current.IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { } }
            if (c.IsHandleCreated) Set();
            else c.HandleCreated += (s, e) => Set();
        }
    }

    /// <summary>System DPI scale factor (the app is system-DPI aware).</summary>
    public static class Dpi
    {
        private static float? _factor;

        public static float Factor
        {
            get
            {
                if (_factor == null)
                {
                    try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) _factor = g.DpiX / 96f; }
                    catch { _factor = 1f; }
                }
                return _factor.Value;
            }
        }

        public static int Scale(int px) => (int)Math.Round(px * Factor);
    }

    /// <summary>Menu / toolbar renderer that follows the current palette.</summary>
    public sealed class ThemedRenderer : ToolStripProfessionalRenderer
    {
        public ThemedRenderer() : base(new ThemedColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Current.Text : Theme.Current.TextMuted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? Theme.Current.Text : Theme.Current.TextMuted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            r.Inflate(-1, -1);
            using (var pen = new Pen(Theme.Current.Text, 1.8f))
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.55f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.75f),
                    new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.3f),
                });
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                using (var pen = new Pen(Theme.Current.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
            // no border line under menu bars / tool bars
        }
    }

    public sealed class ThemedColors : ProfessionalColorTable
    {
        private static Palette P => Theme.Current;
        public ThemedColors() { UseSystemColors = false; }

        public override Color MenuStripGradientBegin => P.Surface;
        public override Color MenuStripGradientEnd => P.Surface;
        public override Color ToolStripGradientBegin => P.Window;
        public override Color ToolStripGradientMiddle => P.Window;
        public override Color ToolStripGradientEnd => P.Window;
        public override Color ToolStripBorder => P.Window;
        public override Color ToolStripDropDownBackground => P.Surface;
        public override Color ImageMarginGradientBegin => P.Surface;
        public override Color ImageMarginGradientMiddle => P.Surface;
        public override Color ImageMarginGradientEnd => P.Surface;
        public override Color MenuBorder => P.Border;
        public override Color MenuItemBorder => P.Hover;
        public override Color MenuItemSelected => P.Hover;
        public override Color MenuItemSelectedGradientBegin => P.Hover;
        public override Color MenuItemSelectedGradientEnd => P.Hover;
        public override Color MenuItemPressedGradientBegin => P.Selected;
        public override Color MenuItemPressedGradientMiddle => P.Selected;
        public override Color MenuItemPressedGradientEnd => P.Selected;
        public override Color ButtonSelectedHighlight => P.Hover;
        public override Color ButtonSelectedGradientBegin => P.Hover;
        public override Color ButtonSelectedGradientMiddle => P.Hover;
        public override Color ButtonSelectedGradientEnd => P.Hover;
        public override Color ButtonSelectedBorder => P.Hover;
        public override Color ButtonPressedGradientBegin => P.Selected;
        public override Color ButtonPressedGradientMiddle => P.Selected;
        public override Color ButtonPressedGradientEnd => P.Selected;
        public override Color ButtonPressedBorder => P.Selected;
        public override Color ButtonCheckedGradientBegin => P.Selected;
        public override Color ButtonCheckedGradientMiddle => P.Selected;
        public override Color ButtonCheckedGradientEnd => P.Selected;
        public override Color CheckBackground => P.Surface;
        public override Color CheckSelectedBackground => P.Hover;
        public override Color CheckPressedBackground => P.Hover;
        public override Color SeparatorDark => P.Border;
        public override Color SeparatorLight => P.Border;
    }
}
