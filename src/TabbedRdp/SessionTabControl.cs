using System;
using System.Drawing;
using System.Windows.Forms;

namespace TabbedRdp
{
    /// <summary>Tab strip with a close "×" on every tab, middle-click to close and a status dot.</summary>
    public sealed class SessionTabControl : TabControl
    {
        private const int CloseSize = 14;

        public event EventHandler<TabPage> CloseClicked;
        public event EventHandler<TabPage> TabRightClicked;

        public SessionTabControl()
        {
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Normal;
            Padding = new Point(22, 5);
            ShowToolTips = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        private Rectangle CloseRect(int index)
        {
            var r = GetTabRect(index);
            return new Rectangle(r.Right - CloseSize - 5, r.Top + (r.Height - CloseSize) / 2, CloseSize, CloseSize);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= TabPages.Count) return;
            var page = TabPages[e.Index];
            var bounds = GetTabRect(e.Index);
            bool selected = e.Index == SelectedIndex;

            using (var bg = new SolidBrush(selected ? SystemColors.Window : SystemColors.Control))
                e.Graphics.FillRectangle(bg, bounds);

            // Status dot
            var state = (page.Tag as RdpSession)?.State ?? SessionState.Idle;
            var dotColor = state == SessionState.Connected ? Color.FromArgb(46, 160, 67)
                : state == SessionState.Connecting ? Color.FromArgb(230, 150, 20)
                : state == SessionState.Disconnected ? Color.FromArgb(200, 60, 60)
                : Color.Gray;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var dot = new SolidBrush(dotColor))
                e.Graphics.FillEllipse(dot, bounds.Left + 7, bounds.Top + bounds.Height / 2 - 4, 8, 8);

            var textRect = new Rectangle(bounds.Left + 19, bounds.Top, bounds.Width - 19 - CloseSize - 6, bounds.Height);
            TextRenderer.DrawText(e.Graphics, page.Text, Font, textRect, SystemColors.ControlText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            var close = CloseRect(e.Index);
            using (var pen = new Pen(SystemColors.GrayText, 1.6f))
            {
                int pad = 4;
                e.Graphics.DrawLine(pen, close.Left + pad, close.Top + pad, close.Right - pad, close.Bottom - pad);
                e.Graphics.DrawLine(pen, close.Right - pad, close.Top + pad, close.Left + pad, close.Bottom - pad);
            }
        }

        private int TabAt(Point p)
        {
            for (int i = 0; i < TabPages.Count; i++)
                if (GetTabRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int index = TabAt(e.Location);
            if (index < 0) return;
            var page = TabPages[index];

            if (e.Button == MouseButtons.Middle ||
                (e.Button == MouseButtons.Left && CloseRect(index).Contains(e.Location)))
            {
                CloseClicked?.Invoke(this, page);
            }
            else if (e.Button == MouseButtons.Right)
            {
                SelectedTab = page;
                TabRightClicked?.Invoke(this, page);
            }
        }

        public void RefreshTabs() => Invalidate();
    }
}
