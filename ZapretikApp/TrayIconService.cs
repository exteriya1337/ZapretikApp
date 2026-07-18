using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DrawingIcon = System.Drawing.Icon;

namespace ZapretikApp
{
    /// <summary>
    /// System tray icon: LMB opens app, RMB shows control menu.
    /// </summary>
    internal sealed class TrayIconService : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private DrawingIcon _icon;
        private IntPtr _iconHandle = IntPtr.Zero;
        private bool _disposed;

        public event Action OpenRequested;
        public event Action ExitRequested;
        public event Action InstallRequested;
        public event Action StopRequested;
        public event Action<BatFileItem> StrategyRequested;

        public TrayIconService()
        {
            _icon = CreateAppIcon(out _iconHandle);

            _menu = new ContextMenuStrip();
            _menu.Renderer = new ToolStripProfessionalRenderer(new DarkTrayColorTable());
            _menu.ForeColor = Color.FromArgb(240, 240, 242);
            _menu.BackColor = Color.FromArgb(43, 43, 46);
            _menu.Font = new Font("Segoe UI", 9f);
            _menu.ShowImageMargin = false;
            _menu.Opening += Menu_Opening;

            _notifyIcon = new NotifyIcon
            {
                Icon = _icon,
                Text = "Zapretik",
                Visible = true,
                ContextMenuStrip = _menu
            };

            _notifyIcon.MouseClick += NotifyIcon_MouseClick;
            BuildStaticMenu(null, isOnline: false, activeName: null, selectedName: null);
            UpdateTooltip(isOnline: false, activeBypass: null);
        }

        /// <summary>
        /// Hover tooltip under tray icon (max ~63 chars on Windows).
        /// </summary>
        public void UpdateTooltip(bool isOnline, string activeBypass)
        {
            if (_disposed) return;

            string text;
            if (isOnline)
            {
                if (!string.IsNullOrWhiteSpace(activeBypass))
                    text = "Онлайн · " + activeBypass.Trim();
                else
                    text = "Zapretik · Онлайн";
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(activeBypass))
                    text = "Отключен · выбрано: " + activeBypass.Trim();
                else
                    text = "Zapretik · Отключен";
            }

            if (text.Length > 63)
                text = text.Substring(0, 60) + "...";
            _notifyIcon.Text = text;
        }

        /// <param name="activeName">Currently running bypass (service strategy).</param>
        /// <param name="selectedName">Bat selected in the main list (for offline install).</param>
        public void RebuildMenu(IList<BatFileItem> strategies, bool isOnline, string activeName, string selectedName)
        {
            if (_disposed) return;
            BuildStaticMenu(strategies, isOnline, activeName, selectedName);
        }

        public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
        {
            if (_disposed) return;
            try
            {
                _notifyIcon.BalloonTipTitle = title ?? "Zapretik";
                _notifyIcon.BalloonTipText = text ?? string.Empty;
                _notifyIcon.BalloonTipIcon = icon;
                _notifyIcon.ShowBalloonTip(2500);
            }
            catch
            {
            }
        }

        private void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var handler = OpenRequested;
                if (handler != null)
                    handler();
            }
        }

        private void Menu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Keep menu freshest right before open — parent rebuilds on status changes too.
        }

        private void BuildStaticMenu(
            IList<BatFileItem> strategies,
            bool isOnline,
            string activeName,
            string selectedName)
        {
            _menu.Items.Clear();

            var open = new ToolStripMenuItem("Открыть Zapretik");
            open.Font = new Font(open.Font, FontStyle.Bold);
            open.Click += (s, e) => Raise(OpenRequested);
            _menu.Items.Add(open);

            _menu.Items.Add(new ToolStripSeparator());

            // Current bypass — always visible in the menu
            var bypassLabel = isOnline
                ? (string.IsNullOrWhiteSpace(activeName) ? "Обход: (не определён)" : "Обход: " + activeName)
                : "Обход: не запущен";
            var bypassItem = new ToolStripMenuItem(bypassLabel);
            bypassItem.Enabled = false;
            if (isOnline && !string.IsNullOrWhiteSpace(activeName))
                bypassItem.ForeColor = Color.FromArgb(111, 207, 151);
            _menu.Items.Add(bypassItem);

            if (!string.IsNullOrWhiteSpace(selectedName) &&
                (string.IsNullOrWhiteSpace(activeName) ||
                 !string.Equals(selectedName, activeName, StringComparison.OrdinalIgnoreCase)))
            {
                var sel = new ToolStripMenuItem("Выбрано в списке: " + selectedName);
                sel.Enabled = false;
                _menu.Items.Add(sel);
            }

            var status = new ToolStripMenuItem(isOnline ? "Статус: Онлайн" : "Статус: Отключен");
            status.Enabled = false;
            _menu.Items.Add(status);

            _menu.Items.Add(new ToolStripSeparator());

            if (isOnline)
            {
                var stop = new ToolStripMenuItem("Снять службу");
                stop.Click += (s, e) => Raise(StopRequested);
                _menu.Items.Add(stop);

                var install = new ToolStripMenuItem("Сменить на выбранную в окне");
                install.Click += (s, e) => Raise(InstallRequested);
                _menu.Items.Add(install);
            }
            else
            {
                var install = new ToolStripMenuItem("Установить выбранную в окне");
                install.Click += (s, e) => Raise(InstallRequested);
                _menu.Items.Add(install);
            }

            // Submenu with all strategies for one-click switch
            var change = new ToolStripMenuItem("Сменить батник");
            if (strategies != null && strategies.Count > 0)
            {
                foreach (var bat in strategies)
                {
                    var item = bat;
                    var mi = new ToolStripMenuItem(item.Name);

                    var isActive = !string.IsNullOrEmpty(activeName) &&
                                   string.Equals(item.Name, activeName, StringComparison.OrdinalIgnoreCase);
                    var isSelected = !string.IsNullOrEmpty(selectedName) &&
                                     string.Equals(item.Name, selectedName, StringComparison.OrdinalIgnoreCase);

                    if (isActive)
                    {
                        mi.Text = "● " + item.Name + "  (активен)";
                        mi.Font = new Font(mi.Font, FontStyle.Bold);
                        mi.ForeColor = Color.FromArgb(111, 207, 151);
                    }
                    else if (isSelected)
                    {
                        mi.Text = "○ " + item.Name + "  (выбран)";
                    }

                    mi.Click += (s, e) =>
                    {
                        var h = StrategyRequested;
                        if (h != null)
                            h(item);
                    };
                    change.DropDownItems.Add(mi);
                }
            }
            else
            {
                var empty = new ToolStripMenuItem("(нет стратегий — укажите папку Zapret)");
                empty.Enabled = false;
                change.DropDownItems.Add(empty);
            }
            _menu.Items.Add(change);

            _menu.Items.Add(new ToolStripSeparator());

            var exit = new ToolStripMenuItem("Выход");
            exit.Click += (s, e) => Raise(ExitRequested);
            _menu.Items.Add(exit);
        }

        private static void Raise(Action handler)
        {
            if (handler != null)
                handler();
        }

        private static DrawingIcon CreateAppIcon(out IntPtr handle)
        {
            handle = IntPtr.Zero;

            // Prefer embedded app.ico (same Z on purple as the .exe / taskbar).
            try
            {
                var uri = new Uri("pack://application:,,,/app.ico", UriKind.Absolute);
                var info = System.Windows.Application.GetResourceStream(uri);
                if (info != null && info.Stream != null)
                {
                    using (info.Stream)
                    using (var ms = new MemoryStream())
                    {
                        info.Stream.CopyTo(ms);
                        ms.Position = 0;
                        using (var tmp = new DrawingIcon(ms, 32, 32))
                        {
                            return (DrawingIcon)tmp.Clone();
                        }
                    }
                }
            }
            catch
            {
                // Fall back to runtime-drawn icon.
            }

            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    g.Clear(Color.Transparent);

                    using (var brush = new LinearGradientBrush(
                        new Rectangle(0, 0, 32, 32),
                        Color.FromArgb(255, 124, 140, 255),
                        Color.FromArgb(255, 90, 106, 224),
                        45f))
                    {
                        // Match app.ico rounded square look.
                        using (var path = RoundedRect(new Rectangle(1, 1, 30, 30), 8))
                        {
                            g.FillPath(brush, path);
                        }
                    }

                    using (var font = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    })
                    using (var tb = new SolidBrush(Color.White))
                    {
                        g.DrawString("Z", font, tb, new RectangleF(0, 1, 32, 32), sf);
                    }
                }

                handle = bmp.GetHicon();
                // Clone so the Icon owns a copy of the data; we still track handle for DestroyIcon.
                using (var tmp = DrawingIcon.FromHandle(handle))
                {
                    return (DrawingIcon)tmp.Clone();
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch
            {
            }

            try
            {
                if (_icon != null)
                    _icon.Dispose();
            }
            catch
            {
            }

            if (_iconHandle != IntPtr.Zero)
            {
                DestroyIcon(_iconHandle);
                _iconHandle = IntPtr.Zero;
            }

            try
            {
                _menu.Dispose();
            }
            catch
            {
            }
        }

        private sealed class DarkTrayColorTable : ProfessionalColorTable
        {
            public override Color MenuBorder
            {
                get { return Color.FromArgb(74, 74, 82); }
            }

            public override Color MenuItemBorder
            {
                get { return Color.FromArgb(124, 140, 255); }
            }

            public override Color MenuItemSelected
            {
                get { return Color.FromArgb(62, 62, 68); }
            }

            public override Color MenuItemSelectedGradientBegin
            {
                get { return Color.FromArgb(62, 62, 68); }
            }

            public override Color MenuItemSelectedGradientEnd
            {
                get { return Color.FromArgb(62, 62, 68); }
            }

            public override Color ToolStripDropDownBackground
            {
                get { return Color.FromArgb(43, 43, 46); }
            }

            public override Color ImageMarginGradientBegin
            {
                get { return Color.FromArgb(43, 43, 46); }
            }

            public override Color ImageMarginGradientMiddle
            {
                get { return Color.FromArgb(43, 43, 46); }
            }

            public override Color ImageMarginGradientEnd
            {
                get { return Color.FromArgb(43, 43, 46); }
            }

            public override Color SeparatorDark
            {
                get { return Color.FromArgb(74, 74, 82); }
            }

            public override Color SeparatorLight
            {
                get { return Color.FromArgb(74, 74, 82); }
            }
        }
    }
}
