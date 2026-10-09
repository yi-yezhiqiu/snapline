using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

/// <summary>
/// 热键验证探针：直接把 WM_HOTKEY 投递给应用主窗口，再抓屏，
/// 确认 Ctrl+Alt+T「显示/隐藏绳子」这条链路真的通。
/// </summary>
internal static class HotkeyProbe
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_TOGGLE = 0x4C53;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static string _dir = @"D:\截图晾晒工具\.hotkey-probe";

    [STAThread]
    private static int Main()
    {
        System.IO.Directory.CreateDirectory(_dir);
        Log("=== 热键验证开始 ===");

        IntPtr hwnd = FindAppWindow();
        if (hwnd == IntPtr.Zero) { Log("找不到应用窗口，请先启动晾晒工具"); return 1; }
        Log("选中的窗口 hwnd = 0x" + hwnd.ToInt64().ToString("X"));

        // 先把该进程的所有顶层窗口都列出来，方便比对
        int targetPid = AppPid();
        Log("--- 进程 " + targetPid + " 的所有顶层窗口 ---");
        EnumWindows((h, l) =>
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if ((int)pid != targetPid) { return true; }
            var cls = new System.Text.StringBuilder(256);
            GetClassName(h, cls, cls.Capacity);
            var txt = new System.Text.StringBuilder(256);
            GetWindowText(h, txt, txt.Capacity);
            Log(string.Format("    hwnd=0x{0:X} cls={1} title='{2}' visible={3}",
                h.ToInt64(), cls, txt, IsWindowVisible(h)));
            return true;
        }, IntPtr.Zero);

        // --- 1. 投递一次 Ctrl+Alt+T：应该把绳子拉下来 ---
        bool ok = PostMessage(hwnd, WM_HOTKEY, new IntPtr(HOTKEY_TOGGLE), IntPtr.Zero);
        Log("1) 已投递 WM_HOTKEY(显示)，PostMessage=" + ok);
        Thread.Sleep(1100);
        Capture("hotkey-show");

        // --- 2. 再投递一次：应该把绳子收回去 ---
        ok = PostMessage(hwnd, WM_HOTKEY, new IntPtr(HOTKEY_TOGGLE), IntPtr.Zero);
        Log("2) 已投递 WM_HOTKEY(隐藏)，PostMessage=" + ok);
        Thread.Sleep(1100);
        Capture("hotkey-hide");

        Log("=== 热键验证结束 ===");
        return 0;
    }

    private static int AppPid()
    {
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (p.ProcessName == "晾晒工具") { return p.Id; }
            }
            catch { }
            finally { p.Dispose(); }
        }
        return 0;
    }

    private static IntPtr FindAppWindow()
    {
        int targetPid = AppPid();
        if (targetPid == 0) { return IntPtr.Zero; }

        // 注意：不能用 Process.MainWindowHandle —— 绳子的窗口藏在屏幕上方，
        // .NET 认为它"不算主窗口"，会返回 0。这里按进程 ID 枚举顶层窗口。
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, lparam) =>
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if ((int)pid != targetPid) { return true; }
            if (!IsWindowVisible(hwnd)) { return true; }
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, IntPtr lparam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);

    private static void Capture(string tag)
    {
        Rectangle b = Screen.PrimaryScreen.Bounds;
        using (var full = new Bitmap(b.Width, b.Height))
        {
            using (Graphics g = Graphics.FromImage(full))
            {
                g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size);
            }
            int h = Math.Min(250, b.Height);
            using (var top = full.Clone(new Rectangle(0, 0, b.Width, h), full.PixelFormat))
            {
                top.Save(System.IO.Path.Combine(_dir, "top-" + tag + ".png"), ImageFormat.Png);
            }
        }
        Log("   抓屏[" + tag + "]");
    }

    private static void Log(string s)
    {
        Console.WriteLine(s);
        try
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(_dir, "hotkey.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine,
                System.Text.Encoding.UTF8);
        }
        catch { }
    }
}
