using System;
using System.Runtime.InteropServices;

namespace Liangshai
{
    /// <summary>Win32 互操作声明集中处。</summary>
    internal static class Win32
    {
        // ---------- 剪贴板监视 ----------
        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_DISPLAYCHANGE = 0x007E;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        // ---------- 全局热键 ----------
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        // ---------- 窗口样式 ----------
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        // ---------- 前台窗口 / 全屏检测 ----------
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder buffer, int maxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        public const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        // ---------- 鼠标位置 ----------
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT pt);

        // ---------- 窗口置顶 ----------
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
            int x, int y, int cx, int cy, uint flags);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;

        // ---------- 结构体 ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        public const uint MONITORINFOF_PRIMARY = 1;

        /// <summary>取得指定显示器（含虚拟屏幕坐标）的显示器矩形。</summary>
        public static RECT GetMonitorRect(IntPtr monitor)
        {
            var info = new MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (GetMonitorInfo(monitor, ref info)) { return info.rcMonitor; }
            return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        }

        /// <summary>取得鼠标当前所在显示器的矩形。</summary>
        public static RECT GetMonitorRectFromCursor()
        {
            POINT pt;
            if (!GetCursorPos(out pt)) { pt = new POINT(0, 0); }
            IntPtr mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            return GetMonitorRect(mon);
        }

        /// <summary>把窗口变成不抢焦点、不出现在 Alt+Tab 的工具窗口。</summary>
        public static void MakeToolWindow(IntPtr hwnd)
        {
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            ex |= WS_EX_TOOLWINDOW;
            ex |= WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
    }
}
