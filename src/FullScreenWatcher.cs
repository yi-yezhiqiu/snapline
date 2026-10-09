using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Liangshai
{
    /// <summary>检测当前是否有全屏应用在前台（游戏 / 视频 / 演示）。</summary>
    internal sealed class FullScreenWatcher
    {
        private const string ShellClass = "Shell_TrayWnd";
        private const string ProgmanClass = "Progman";
        private const string WorkerWClass = "WorkerW";

        private readonly DispatcherTimer _timer;

        /// <summary>当前是否处于全屏状态。</summary>
        public bool IsFullScreen { get; private set; }

        public event EventHandler Changed;

        public FullScreenWatcher()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(700)
            };
            _timer.Tick += (s, e) => Poll();
        }

        public void Start() { _timer.Start(); }
        public void Stop() { _timer.Stop(); }

        private void Poll()
        {
            bool fs = Compute();
            if (fs == IsFullScreen) { return; }
            IsFullScreen = fs;
            EventHandler h = Changed;
            if (h != null) { h(this, EventArgs.Empty); }
        }

        private static bool Compute()
        {
            try
            {
                IntPtr fg = Win32.GetForegroundWindow();
                if (fg == IntPtr.Zero) { return false; }
                if (fg == Win32.GetDesktopWindow() || fg == Win32.GetShellWindow()) { return false; }

                var sb = new StringBuilder(256);
                Win32.GetClassName(fg, sb, sb.Capacity);
                string cls = sb.ToString();
                if (cls == ShellClass || cls == ProgmanClass || cls == WorkerWClass) { return false; }

                // 自己不是全屏应用
                if (App.Instance != null && fg == App.Instance.MainWindowHandle) { return false; }

                Win32.RECT r;
                if (!Win32.GetWindowRect(fg, out r)) { return false; }
                if (r.Width <= 0 || r.Height <= 0) { return false; }

                IntPtr mon = Win32.MonitorFromWindow(fg, Win32.MONITOR_DEFAULTTONEAREST);
                Win32.RECT m = Win32.GetMonitorRect(mon);

                // 窗口几乎铺满整个显示器 = 全屏
                bool coversW = r.Left <= m.Left + 2 && r.Right >= m.Right - 2;
                bool coversH = r.Top <= m.Top + 2 && r.Bottom >= m.Bottom - 2;
                return coversW && coversH;
            }
            catch { return false; }
        }
    }

    /// <summary>接收全局热键消息的消息窗口。</summary>
    internal sealed class HotkeyWindow : System.Windows.Forms.NativeWindow
    {
        public const int WM_HOTKEY = 0x0312;
        public const int HOTKEY_TOGGLE = 0x4C53;   // "LS"
        public const int HOTKEY_COPY = 0x4C54;

        public event EventHandler TogglePressed;
        public event EventHandler CopyPressed;

        public HotkeyWindow()
        {
            var cp = new System.Windows.Forms.CreateParams();
            cp.Caption = "Liangshai.Hotkeys";
            cp.Parent = new IntPtr(-3); // HWND_MESSAGE
            CreateHandle(cp);
        }

        public bool RegisterAll()
        {
            bool a = false, b = false;
            try
            {
                a = Win32.RegisterHotKey(Handle, HOTKEY_TOGGLE,
                    Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, 0x54); // T
                b = Win32.RegisterHotKey(Handle, HOTKEY_COPY,
                    Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, 0x43); // C
            }
            catch { }
            return a || b;
        }

        public void UnregisterAll()
        {
            try { Win32.UnregisterHotKey(Handle, HOTKEY_TOGGLE); } catch { }
            try { Win32.UnregisterHotKey(Handle, HOTKEY_COPY); } catch { }
        }

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_TOGGLE)
                {
                    EventHandler h = TogglePressed;
                    if (h != null) { h(this, EventArgs.Empty); }
                }
                else if (id == HOTKEY_COPY)
                {
                    EventHandler h = CopyPressed;
                    if (h != null) { h(this, EventArgs.Empty); }
                }
            }
            base.WndProc(ref m);
        }
    }
}
