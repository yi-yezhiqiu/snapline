using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Liangshai
{
    /// <summary>系统托盘图标与右键菜单。图标全部用代码画出来，不依赖外部图片文件。</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly Config _cfg;

        private readonly ToolStripMenuItem _miShow = new ToolStripMenuItem("显示绳子");
        private readonly ToolStripMenuItem _miCopyLast = new ToolStripMenuItem("复制最近一张到剪贴板");
        private readonly ToolStripMenuItem _miPause = new ToolStripMenuItem("暂停接管截图");
        private readonly ToolStripMenuItem _miSaveDisk = new ToolStripMenuItem("保存到磁盘");
        private readonly ToolStripMenuItem _miOpenFolder = new ToolStripMenuItem("打开截图文件夹");
        private readonly ToolStripMenuItem _miAutoStart = new ToolStripMenuItem("开机自动启动");

        public event EventHandler ShowRequested;
        public event EventHandler CopyLastRequested;
        public event EventHandler PauseToggled;
        public event EventHandler SettingsRequested;
        public event EventHandler ClearRequested;
        public event EventHandler QuitRequested;
        public event EventHandler SaveToDiskToggled;
        public event EventHandler OpenControlRequested;

        public TrayIcon(Config cfg)
        {
            _cfg = cfg;

            Icon trayIcon;
            try
            {
                trayIcon = AppIcon.Create();
            }
            catch
            {
                trayIcon = SystemIcons.Application;
            }

            var menu = new ContextMenuStrip { ShowImageMargin = false };

            _miShow.Font = new Font(_miShow.Font, FontStyle.Bold);
            _miShow.Click += (s, e) => Raise(ShowRequested);
            menu.Items.Add(_miShow);

            var miControl = new ToolStripMenuItem("打开控制窗口（显示绳子 / 退出）");
            miControl.Click += (s, e) => Raise(OpenControlRequested);
            menu.Items.Add(miControl);

            _miCopyLast.Click += (s, e) => Raise(CopyLastRequested);
            menu.Items.Add(_miCopyLast);

            menu.Items.Add(new ToolStripSeparator());

            _miPause.Click += (s, e) => Raise(PauseToggled);
            menu.Items.Add(_miPause);

            _miSaveDisk.Click += (s, e) => Raise(SaveToDiskToggled);
            menu.Items.Add(_miSaveDisk);

            _miOpenFolder.Click += (s, e) => OpenFolder();
            menu.Items.Add(_miOpenFolder);

            menu.Items.Add(new ToolStripSeparator());

            var miSettings = new ToolStripMenuItem("设置…");
            miSettings.Click += (s, e) => Raise(SettingsRequested);
            menu.Items.Add(miSettings);

            var miHelp = new ToolStripMenuItem("怎么退出？");
            miHelp.Click += (s, e) => ShowHelp();
            menu.Items.Add(miHelp);

            _miAutoStart.Click += (s, e) => ToggleAutoStart();
            menu.Items.Add(_miAutoStart);

            menu.Items.Add(new ToolStripSeparator());

            var miClear = new ToolStripMenuItem("清空绳子");
            miClear.Click += (s, e) => Raise(ClearRequested);
            menu.Items.Add(miClear);

            var miQuit = new ToolStripMenuItem("退出");
            miQuit.Click += (s, e) => Raise(QuitRequested);
            menu.Items.Add(miQuit);

            _icon = new NotifyIcon
            {
                Icon = trayIcon,
                Text = "晾晒工具 — 截图挂起来，桌面清干净",
                ContextMenuStrip = menu,
                Visible = true
            };
            _icon.DoubleClick += (s, e) => Raise(ShowRequested);
            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) { Raise(ShowRequested); }
            };

            Refresh();
        }

        private static void Raise(EventHandler h)
        {
            if (h != null) { h(null, EventArgs.Empty); }
        }

        /// <summary>根据当前状态刷新菜单勾选与文字。</summary>
        public void Refresh()
        {
            _miPause.Checked = !_cfg.WatchEnabled;
            _miSaveDisk.Checked = _cfg.SaveToDisk;
            _miAutoStart.Checked = AutoStart.IsEnabled();
            _miOpenFolder.Enabled = true;
        }

        private void ToggleAutoStart()
        {
            bool want = !AutoStart.IsEnabled();
            AutoStart.Set(want);
            Refresh();
        }

        private void OpenFolder()
        {
            try
            {
                string dir = _cfg.SaveFolder;
                if (!System.IO.Directory.Exists(dir)) { System.IO.Directory.CreateDirectory(dir); }
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch { }
        }

        public void ShowBalloon(string title, string text)
        {
            try
            {
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipText = text;
                _icon.BalloonTipIcon = ToolTipIcon.Info;
                _icon.ShowBalloonTip(6000);
            }
            catch { }
        }

        /// <summary>告诉用户程序在哪、怎么关掉它。</summary>
        private static void ShowHelp()
        {
            MessageBox.Show(
                "晾晒工具平时没有窗口，只待在右下角的托盘里（可能被折叠在 ∧ 里面）。\n\n" +
                "退出方法（任选一种）：\n" +
                "  1. 右键右下角托盘图标 → 退出\n" +
                "  2. 托盘右键 → 设置…（设置窗口会出现在任务栏上，右键任务栏图标可关闭）\n" +
                "  3. 命令行运行：晾晒工具.exe /quit\n\n" +
                "显示绳子：鼠标停在屏幕最上边缘，或按 Ctrl+Alt+T，或点托盘图标。",
                "晾晒工具 · 怎么退出", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void Dispose()
        {
            try
            {
                _icon.Visible = false;
                _icon.Dispose();
            }
            catch { }
        }
    }
}
