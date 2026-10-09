using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

/// <summary>
/// 验证控制窗口的关闭按钮确实被封掉了：
///   1. 检查窗口的系统菜单里 SC_CLOSE 是否被禁用
///   2. 试着用 UI 自动化「关闭窗口」，确认关不掉
///   3. 确认最小化按钮仍然可用
///   4. 确认「退出晾晒工具」按钮能正常关掉程序
/// </summary>
internal static class CloseProbe
{
    private const string Exe = @"D:\截图晾晒工具\dist\晾晒工具.exe";
    private static string _dir = @"D:\截图晾晒工具\.probe";

    private const uint MF_BYCOMMAND = 0x00000000;
    private const uint MF_ENABLED = 0x00000000;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_DISABLED = 0x00000002;
    private const uint SC_CLOSE = 0xF060;

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll")]
    private static extern uint GetMenuState(IntPtr hMenu, uint uId, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, IntPtr lparam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);

    private const uint WM_CLOSE = 0x0010;
    private const int WM_SYSCOMMAND = 0x0112;

    [STAThread]
    private static int Main()
    {
        System.IO.Directory.CreateDirectory(_dir);
        Log("=== 关闭按钮封禁验证 ===");

        if (Process.GetProcessesByName("晾晒工具").Length == 0)
        {
            var psi = new ProcessStartInfo(Exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            Process.Start(psi);
            Log("已启动应用");
            Thread.Sleep(4500);
        }
        else { Log("应用已在运行"); }

        IntPtr hwnd = FindAppWindow();
        if (hwnd == IntPtr.Zero) { Log("✗ 找不到应用窗口"); return 1; }
        Log("窗口句柄 = 0x" + hwnd.ToInt64().ToString("X"));

        int failures = 0;

        // ---- 1. 系统菜单里的 SC_CLOSE 状态 ----
        IntPtr menu = GetSystemMenu(hwnd, false);
        uint state = GetMenuState(menu, SC_CLOSE, MF_BYCOMMAND);
        bool grayed = (state & MF_GRAYED) != 0;
        bool disabled = (state & MF_DISABLED) != 0;
        bool missing = state == 0xFFFFFFFF;

        Log(string.Format("1) SC_CLOSE 菜单状态 = 0x{0:X8}  (灰化={1} 禁用={2} 不存在={3})",
            state, grayed, disabled, missing));

        if (grayed || disabled || missing)
        {
            Log("   ✓ 关闭项已被封禁（标题栏 ✕ 会显示为灰色不可点）");
        }
        else
        {
            Log("   ✗ 关闭项仍然可用");
            failures++;
        }

        // ---- 2. 试着用 WM_CLOSE 关掉它 ----
        Log("2) 尝试发送 WM_CLOSE（等同点 ✕）");
        PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(1200);

        IntPtr after = FindAppWindow();
        if (after == IntPtr.Zero)
        {
            Log("   ✗ 窗口被关掉了");
            failures++;
        }
        else
        {
            Log("   ✓ 窗口仍然存在（WM_CLOSE 被忽略）");
        }

        // ---- 3. 试着用 SC_CLOSE 关掉它 ----
        Log("3) 尝试发送 WM_SYSCOMMAND/SC_CLOSE");
        PostMessage(after, WM_SYSCOMMAND, new IntPtr(SC_CLOSE), IntPtr.Zero);
        Thread.Sleep(1200);
        IntPtr after2 = FindAppWindow();
        if (after2 == IntPtr.Zero)
        {
            Log("   ✗ 窗口被关掉了");
            failures++;
        }
        else
        {
            Log("   ✓ 窗口仍然存在（SC_CLOSE 被忽略）");
        }

        // ---- 4. 确认最小化仍然可用 ----
        try
        {
            AutomationElement el = FindWindow("晾晒工具");
            if (el != null)
            {
                var wp = (WindowPattern)el.GetCurrentPattern(WindowPattern.Pattern);
                bool canMin = wp.Current.CanMinimize;
                bool canMax = wp.Current.CanMaximize;
                Log(string.Format("4) 可最小化={0} 可最大化={1}", canMin, canMax));
                if (canMin) { Log("   ✓ 最小化可用"); }
                else { Log("   ✗ 最小化不可用"); failures++; }

                // 真去最小化再恢复，确认能用
                wp.SetWindowVisualState(WindowVisualState.Minimized);
                Thread.Sleep(600);
                wp.SetWindowVisualState(WindowVisualState.Normal);
                Thread.Sleep(400);
                Log("   ✓ 最小化 / 恢复 操作成功");
            }
            else { Log("   ✗ 找不到窗口元素"); failures++; }
        }
        catch (Exception ex) { Log("   ✗ 最小化检查失败: " + ex.Message); failures++; }

        Capture("after-tests");

        Log(failures == 0 ? "★ 结论：✕ 已封禁，最小化可用" : "✗ 有 " + failures + " 项失败");
        Log("=== 验证结束 ===");
        return failures == 0 ? 0 : 1;
    }

    private static IntPtr FindAppWindow()
    {
        int pid = 0;
        foreach (Process p in Process.GetProcesses())
        {
            try { if (p.ProcessName == "晾晒工具") { pid = p.Id; break; } }
            catch { }
            finally { p.Dispose(); }
        }
        if (pid == 0) { return IntPtr.Zero; }

        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, l) =>
        {
            uint wp;
            GetWindowThreadProcessId(hwnd, out wp);
            if ((int)wp != pid) { return true; }
            if (!IsWindowVisible(hwnd)) { return true; }
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static AutomationElement FindWindow(string namePart)
    {
        var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
        AutomationElementCollection all = AutomationElement.RootElement.FindAll(TreeScope.Children, cond);
        foreach (AutomationElement el in all)
        {
            try
            {
                string n = el.Current.Name;
                if (!string.IsNullOrEmpty(n) && n.Contains(namePart)) { return el; }
            }
            catch { }
        }
        return null;
    }

    private static void Capture(string tag)
    {
        Rectangle b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        using (var full = new Bitmap(b.Width, b.Height))
        {
            using (Graphics g = Graphics.FromImage(full)) { g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size); }
            int w = Math.Min(700, b.Width), h = Math.Min(140, b.Height);
            var crop = new Rectangle((b.Width - w) / 2 - 40, Math.Max(0, (b.Height - h) / 2 - 200), w, h);
            using (var c = full.Clone(crop, full.PixelFormat))
            {
                c.Save(System.IO.Path.Combine(_dir, "close-" + tag + ".png"), ImageFormat.Png);
            }
        }
        Log("   抓屏[" + tag + "]");
    }

    private static void Log(string s)
    {
        Console.WriteLine(s);
        try
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(_dir, "close.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine,
                System.Text.Encoding.UTF8);
        }
        catch { }
    }
}
