using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TabbedRdp
{
    /// <summary>
    /// Browser-style tab strip that doubles as the window title bar: app/menu button, tabs, "+",
    /// empty drag area and (with the custom title bar) minimize / maximize / close.
    /// </summary>
    public sealed class TabStripBar : Control
    {
        private enum Part { None, Menu, Tab, TabClose, Plus, Minimize, Maximize, Close }

        private readonly List<RdpSession> _tabs = new List<RdpSession>();
        private readonly List<Rectangle> _tabRects = new List<Rectangle>();
        private readonly ToolTip _toolTip = new ToolTip { InitialDelay = 600, ReshowDelay = 200 };
        private Rectangle _menuRect, _plusRect, _minRect, _maxRect, _closeRect;
        private RdpSession _selected;
        private (Part Part, int Index) _hover = (Part.None, -1);
        private (Part Part, int Index) _pressed = (Part.None, -1);
        private int _dragIndex = -1;
        private int _dragStartX;
        private bool _dragging;
        private string _toolTipText;

        public event EventHandler SelectedChanged;
        public event EventHandler NewTabClicked;
        public event EventHandler MenuClicked;
        public event EventHandler<RdpSession> CloseTabClicked;
        public event EventHandler<RdpSession> TabRightClicked;
        public event EventHandler MinimizeClicked;
        public event EventHandler MaximizeClicked;
        public event EventHandler CloseWindowClicked;

        public TabStripBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Dock = DockStyle.Top;
            Height = Scale(38);
            Theme.Changed += OnThemeChanged;
        }

        /// <summary>Draw our own minimize / maximize / close (custom title bar mode).</summary>
        public bool CaptionButtons { get; set; }

        /// <summary>Height of the invisible top resize border (0 when maximized or with a standard title bar).</summary>
        public int TopResizeBorder { get; set; }

        public bool WindowMaximized { get; set; }

        public IReadOnlyList<RdpSession> Tabs => _tabs;

        public RdpSession Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Add(RdpSession session)
        {
            int at = _selected == null ? _tabs.Count : _tabs.IndexOf(_selected) + 1;
            _tabs.Insert(at, session);
            DoLayout();
            Invalidate();
        }

        public void Remove(RdpSession session)
        {
            int index = _tabs.IndexOf(session);
            if (index < 0) return;
            _tabs.RemoveAt(index);
            DoLayout();
            if (_selected == session)
                Selected = _tabs.Count == 0 ? null : _tabs[Math.Min(index, _tabs.Count - 1)];
            Invalidate();
        }

        public void Cycle(int delta)
        {
            if (_tabs.Count < 2) return;
            int i = _tabs.IndexOf(_selected);
            Selected = _tabs[((i + delta) % _tabs.Count + _tabs.Count) % _tabs.Count];
        }

        public void RefreshTabs()
        {
            DoLayout();
            Invalidate();
        }

        /// <summary>True when the point is empty title-bar space (window drag / double-click maximize).</summary>
        public bool IsCaptionArea(Point p) => HitTest(p).Part == Part.None;

        private static int Scale(int px) => Dpi.Scale(px);

        // ---- layout ----------------------------------------------------------------------

        private void DoLayout()
        {
            int h = Height;
            int x = 0;
            _menuRect = new Rectangle(x, 0, Scale(42), h);
            x = _menuRect.Right + Scale(2);

            int captionWidth = CaptionButtons ? Scale(46) * 3 : 0;
            int plusWidth = Scale(34);
            int dragSpace = Scale(CaptionButtons ? 48 : 8);
            int available = Math.Max(0, Width - x - captionWidth - plusWidth - dragSpace);

            int tabWidth = _tabs.Count == 0 ? 0 : Math.Max(Scale(70), Math.Min(Scale(220), available / _tabs.Count));
            int top = Scale(5);
            _tabRects.Clear();
            foreach (var _ in _tabs)
            {
                _tabRects.Add(new Rectangle(x, top, tabWidth, h - top));
                x += tabWidth;
            }

            _plusRect = new Rectangle(x + Scale(3), top + Scale(3), plusWidth - Scale(6), h - top - Scale(6));

            int bw = Scale(46);
            _closeRect = CaptionButtons ? new Rectangle(Width - bw, 0, bw, h) : Rectangle.Empty;
            _maxRect = CaptionButtons ? new Rectangle(Width - 2 * bw, 0, bw, h) : Rectangle.Empty;
            _minRect = CaptionButtons ? new Rectangle(Width - 3 * bw, 0, bw, h) : Rectangle.Empty;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            DoLayout();
        }

        private (Part Part, int Index) HitTest(Point p)
        {
            if (CaptionButtons)
            {
                if (_closeRect.Contains(p)) return (Part.Close, -1);
                if (_maxRect.Contains(p)) return (Part.Maximize, -1);
                if (_minRect.Contains(p)) return (Part.Minimize, -1);
            }
            if (p.Y < TopResizeBorder) return (Part.None, -1);
            if (_menuRect.Contains(p)) return (Part.Menu, -1);
            if (_plusRect.Contains(p)) return (Part.Plus, -1);
            for (int i = 0; i < _tabRects.Count; i++)
            {
                if (!_tabRects[i].Contains(p)) continue;
                return CloseRect(_tabRects[i]).Contains(p) && ShowClose(i) ? (Part.TabClose, i) : (Part.Tab, i);
            }
            return (Part.None, -1);
        }

        private Rectangle CloseRect(Rectangle tab)
        {
            int s = Scale(18);
            return new Rectangle(tab.Right - s - Scale(8), tab.Top + (tab.Height - s) / 2, s, s);
        }

        private bool ShowClose(int i) =>
            _tabs[i] == _selected || _hover.Index == i || _tabRects[i].Width >= Scale(120);

        // ---- painting --------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(p.TitleBar);

            DrawMenuButton(g, p);

            for (int i = 0; i < _tabs.Count; i++) DrawTab(g, p, i);

            // "+"
            if (_hover.Part == Part.Plus) FillRound(g, p.TabHover, _plusRect, Scale(6));
            DrawPlus(g, p.Text, _plusRect, Scale(5));

            if (CaptionButtons) DrawCaptionButtons(g, p);
        }

        private void DrawMenuButton(Graphics g, Palette p)
        {
            if (_hover.Part == Part.Menu) FillRound(g, p.TabHover, Rectangle.Inflate(_menuRect, -Scale(5), -Scale(5)), Scale(6));
            int s = Scale(20);
            var r = new Rectangle(_menuRect.Left + (_menuRect.Width - s) / 2, (_menuRect.Height - s) / 2, s, s);
            FillRound(g, p.Accent, r, Scale(4));
            // tiny monitor glyph
            using (var pen = new Pen(p.AccentText, Math.Max(1.2f, Scale(1))))
            {
                var screen = new Rectangle(r.Left + Scale(4), r.Top + Scale(5), r.Width - Scale(8), r.Height - Scale(11));
                g.DrawRectangle(pen, screen);
                g.DrawLine(pen, r.Left + r.Width / 2, screen.Bottom, r.Left + r.Width / 2, r.Bottom - Scale(4));
                g.DrawLine(pen, r.Left + Scale(6), r.Bottom - Scale(4), r.Right - Scale(6), r.Bottom - Scale(4));
            }
        }

        private void DrawTab(Graphics g, Palette p, int i)
        {
            var r = _tabRects[i];
            var session = _tabs[i];
            bool selected = session == _selected;
            bool hover = _hover.Index == i && (_hover.Part == Part.Tab || _hover.Part == Part.TabClose);

            if (selected || hover)
            {
                using (var path = TopRounded(r, Scale(8)))
                using (var brush = new SolidBrush(selected ? p.TabActive : p.TabHover))
                    g.FillPath(brush, path);
            }
            else if (i + 1 < _tabs.Count && _tabs[i + 1] != _selected)
            {
                using (var pen = new Pen(p.Border))
                    g.DrawLine(pen, r.Right - 1, r.Top + Scale(8), r.Right - 1, r.Bottom - Scale(8));
            }

            // status dot
            var dot = StateColor(session.State);
            int d = Scale(8);
            using (var brush = new SolidBrush(dot))
                g.FillEllipse(brush, r.Left + Scale(12), r.Top + (r.Height - d) / 2, d, d);

            var close = CloseRect(r);
            bool showClose = ShowClose(i);
            int textLeft = r.Left + Scale(12) + d + Scale(8);
            int textRight = showClose ? close.Left - Scale(4) : r.Right - Scale(8);
            var textRect = new Rectangle(textLeft, r.Top, Math.Max(0, textRight - textLeft), r.Height);
            TextRenderer.DrawText(g, session.Info.DisplayName, Font, textRect, selected ? p.Text : p.TextMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            if (showClose)
            {
                bool closeHover = _hover.Part == Part.TabClose && _hover.Index == i;
                if (closeHover) FillRound(g, p.Hover, close, close.Width / 2);
                DrawX(g, closeHover ? p.Text : p.TextMuted, Rectangle.Inflate(close, -Scale(5), -Scale(5)), Math.Max(1.2f, Scale(1) * 1.3f));
            }
        }

        private void DrawCaptionButtons(Graphics g, Palette p)
        {
            g.SmoothingMode = SmoothingMode.None;
            foreach (var (part, rect) in new[] { (Part.Minimize, _minRect), (Part.Maximize, _maxRect), (Part.Close, _closeRect) })
            {
                bool hover = _hover.Part == part;
                bool isClose = part == Part.Close;
                if (hover)
                {
                    using (var b = new SolidBrush(isClose ? Color.FromArgb(232, 17, 35) : p.TabHover))
                        g.FillRectangle(b, rect);
                }
                var color = hover && isClose ? Color.White : p.Text;
                int s = Scale(10);
                var glyph = new Rectangle(rect.Left + (rect.Width - s) / 2, rect.Top + (rect.Height - s) / 2, s, s);
                using (var pen = new Pen(color, Math.Max(1f, Scale(1))))
                {
                    switch (part)
                    {
                        case Part.Minimize:
                            g.DrawLine(pen, glyph.Left, glyph.Top + s / 2, glyph.Right, glyph.Top + s / 2);
                            break;
                        case Part.Maximize:
                            if (WindowMaximized)
                            {
                                int o = Scale(2);
                                g.DrawRectangle(pen, glyph.Left, glyph.Top + o, s - o, s - o);
                                g.DrawLine(pen, glyph.Left + o, glyph.Top, glyph.Right, glyph.Top);
                                g.DrawLine(pen, glyph.Right, glyph.Top, glyph.Right, glyph.Bottom - o);
                            }
                            else g.DrawRectangle(pen, glyph);
                            break;
                        case Part.Close:
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.DrawLine(pen, glyph.Left, glyph.Top, glyph.Right, glyph.Bottom);
                            g.DrawLine(pen, glyph.Right, glyph.Top, glyph.Left, glyph.Bottom);
                            g.SmoothingMode = SmoothingMode.None;
                            break;
                    }
                }
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }

        public static Color StateColor(SessionState state) =>
            state == SessionState.Connected ? Color.FromArgb(46, 175, 80)
            : state == SessionState.Connecting ? Color.FromArgb(240, 160, 30)
            : state == SessionState.Disconnected ? Color.FromArgb(220, 70, 70)
            : Color.Gray;

        private static void DrawX(Graphics g, Color color, Rectangle r, float width)
        {
            using (var pen = new Pen(color, width))
            {
                g.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom);
                g.DrawLine(pen, r.Right, r.Top, r.Left, r.Bottom);
            }
        }

        private void DrawPlus(Graphics g, Color color, Rectangle r, int inset)
        {
            using (var pen = new Pen(color, Math.Max(1.3f, Scale(1) * 1.4f)))
            {
                int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2, len = Math.Min(r.Width, r.Height) / 2 - inset + Scale(2);
                g.DrawLine(pen, cx - len, cy, cx + len, cy);
                g.DrawLine(pen, cx, cy - len, cx, cy + len);
            }
        }

        private static void FillRound(Graphics g, Color color, Rectangle r, int radius)
        {
            using (var path = Rounded(r, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath TopRounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
            path.CloseFigure();
            return path;
        }

        // ---- mouse -----------------------------------------------------------------------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_dragIndex >= 0 && e.Button == MouseButtons.Left)
            {
                if (!_dragging && Math.Abs(e.X - _dragStartX) > Scale(6)) _dragging = true;
                if (_dragging)
                {
                    for (int i = 0; i < _tabRects.Count; i++)
                    {
                        if (i == _dragIndex || !_tabRects[i].Contains(new Point(e.X, _tabRects[i].Top + 1))) continue;
                        var moving = _tabs[_dragIndex];
                        _tabs.RemoveAt(_dragIndex);
                        _tabs.Insert(i, moving);
                        _dragIndex = i;
                        Invalidate();
                        break;
                    }
                    return;
                }
            }

            var hit = HitTest(e.Location);
            if (hit != _hover)
            {
                _hover = hit;
                Invalidate();
                UpdateToolTip();
            }
        }

        private void UpdateToolTip()
        {
            string text = null;
            switch (_hover.Part)
            {
                case Part.Tab:
                    var s = _tabs[_hover.Index];
                    text = s.Info.Address + (string.IsNullOrWhiteSpace(s.Info.UserName) ? "" : "  •  " + s.Info.FullUserName) +
                           (string.IsNullOrEmpty(s.StatusText) ? "" : "\n" + s.StatusText);
                    break;
                case Part.TabClose: text = "Close tab (Ctrl+W)"; break;
                case Part.Plus: text = "New connection (Ctrl+T)"; break;
                case Part.Menu: text = "Menu (Alt)"; break;
            }
            if (text == _toolTipText) return;
            _toolTipText = text;
            _toolTip.SetToolTip(this, text);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = (Part.None, -1);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var hit = HitTest(e.Location);
            _pressed = hit;
            if (e.Button == MouseButtons.Left && hit.Part == Part.Tab)
            {
                Selected = _tabs[hit.Index];
                _dragIndex = hit.Index;
                _dragStartX = e.X;
                _dragging = false;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasDragging = _dragging;
            _dragIndex = -1;
            _dragging = false;
            if (wasDragging) { DoLayout(); Invalidate(); return; }

            var hit = HitTest(e.Location);
            var pressed = _pressed;
            _pressed = (Part.None, -1);

            if (e.Button == MouseButtons.Middle && (hit.Part == Part.Tab || hit.Part == Part.TabClose))
            {
                CloseTabClicked?.Invoke(this, _tabs[hit.Index]);
                return;
            }
            if (e.Button == MouseButtons.Right && (hit.Part == Part.Tab || hit.Part == Part.TabClose))
            {
                Selected = _tabs[hit.Index];
                TabRightClicked?.Invoke(this, _tabs[hit.Index]);
                return;
            }
            if (e.Button != MouseButtons.Left || hit != pressed) return;

            switch (hit.Part)
            {
                case Part.TabClose: CloseTabClicked?.Invoke(this, _tabs[hit.Index]); break;
                case Part.Plus: NewTabClicked?.Invoke(this, EventArgs.Empty); break;
                case Part.Menu: MenuClicked?.Invoke(this, EventArgs.Empty); break;
                case Part.Minimize: MinimizeClicked?.Invoke(this, EventArgs.Empty); break;
                case Part.Maximize: MaximizeClicked?.Invoke(this, EventArgs.Empty); break;
                case Part.Close: CloseWindowClicked?.Invoke(this, EventArgs.Empty); break;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, HTTRANSPARENT = -1;
            if (m.Msg == WM_NCHITTEST && CaptionButtons)
            {
                // Let the form treat empty strip space as its title bar (drag, snap, double-click, system menu).
                var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
                if (IsCaptionArea(p))
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private void OnThemeChanged(object sender, EventArgs e) => Invalidate();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Theme.Changed -= OnThemeChanged;
                _toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
