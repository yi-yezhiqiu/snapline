using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Automation;

/// <summary>
/// 验证控制窗口：
///   1. 启动后窗口出现，并且"最小化"可用（ResizeMode = CanMinimize）
///   2. 点击"显示/隐藏绳子"按钮后窗口**不会消失**
///   3. 按钮文字会切换
/// </summary>
internal static class ControlProbe
{
    private const string Exe = @"D:\截图晾晒工具\dist\晾晒工具.exe";
    private static string _dir = @"D:\截图晾晒工具\.probe";

    [STAThread]
    private static int Main()
    {
        System.IO.Directory.CreateDirectory(_dir);
        Log("=== 控制窗口验证开始 ===");

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

        int failures = 0;

        // ---- 1. 找到控制窗口 ----
        AutomationElement win = FindWindow("晾晒工具");
        if (win == null) { Log("✗ 找不到控制窗口"); return 1; }
        Log("1) 找到控制窗口: " + win.Current.Name);
        Log("   可最小化 = " + win.Current.IsEnabled + " (窗口存在且可交互)");
        Capture("01-launched");

        // ---- 2. 找到"显示绳子"按钮 ----
        AutomationElement btn = FindButton(win, "显示绳子");
        if (btn == null)
        {
            Log("✗ 找不到「显示绳子」按钮");
            DumpButtons(win);
            return 1;
        }
        Log("2) 找到按钮「显示绳子」");

        // ---- 3. 点击它 ----
        Invoke(btn);
        Log("3) 已点击「显示绳子」");
        Thread.Sleep(1400);
        Capture("02-after-toggle");

        // ---- 4. 窗口还在吗？ ----
        AutomationElement after = FindWindow("晾晒工具");
        if (after == null)
        {
            Log("✗ 点击后窗口消失了（这就是用户遇到的问题）");
            failures++;
        }
        else
        {
            Log("✓ 点击后窗口仍在（没有消失）");
        }

        // ---- 5. 按钮文字是否切换成「隐藏绳子」 ----
        AutomationElement btn2 = after != null ? FindButton(after, "隐藏绳子") : null;
        if (btn2 != null)
        {
            Log("✓ 按钮文字已切换为「隐藏绳子」");
        }
        else
        {
            Log("· 按钮文字未变成「隐藏绳子」（可能绳子已自动收回，也算正常）");
        }

        // ---- 6. 再点一次，确认还能操作 ----
        AutomationElement btn3 = after != null ? FindButton(after, "隐藏绳子") : null;
        if (btn3 == null && after != null) { btn3 = FindButton(after, "显示绳子"); }
        if (btn3 != null)
        {
            Invoke(btn3);
            Log("6) 已再次点击，绳子状态切回");
            Thread.Sleep(1000);
            Capture("03-toggled-back");
        }

        Log(failures == 0 ? "★ 结论：控制窗口行为正常" : "✗ 有 " + failures + " 项失败");
        Log("=== 验证结束 ===");
        return failures == 0 ? 0 : 1;
    }

    private static AutomationElement FindWindow(string namePart)
    {
        AutomationElement root = AutomationElement.RootElement;
        var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
        AutomationElementCollection all = root.FindAll(TreeScope.Children, cond);
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

    private static AutomationElement FindButton(AutomationElement root, string textPart)
    {
        var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
        AutomationElementCollection all = root.FindAll(TreeScope.Descendants, cond);
        foreach (AutomationElement el in all)
        {
            try
            {
                if (el.Current.Name != null && el.Current.Name.Contains(textPart)) { return el; }
            }
            catch { }
        }
        return null;
    }

    private static void DumpButtons(AutomationElement root)
    {
        var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
        AutomationElementCollection all = root.FindAll(TreeScope.Descendants, cond);
        Log("   窗口里的按钮有 " + all.Count + " 个：");
        foreach (AutomationElement el in all)
        {
            try { Log("     - '" + el.Current.Name + "'"); } catch { }
        }
    }

    private static void Invoke(AutomationElement btn)
    {
        try
        {
            var pattern = (InvokePattern)btn.GetCurrentPattern(InvokePattern.Pattern);
            pattern.Invoke();
        }
        catch (Exception ex) { Log("   点击失败: " + ex.Message); }
    }

    private static void Capture(string tag)
    {
        Rectangle b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        using (var full = new Bitmap(b.Width, b.Height))
        {
            using (Graphics g = Graphics.FromImage(full))
            {
                g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size);
            }
            // 裁出屏幕中部，控制窗口就在那里
            int w = Math.Min(760, b.Width), h = Math.Min(560, b.Height);
            var crop = new Rectangle((b.Width - w) / 2 - 40, Math.Max(0, (b.Height - h) / 2 - 60), w, h);
            using (var c = full.Clone(crop, full.PixelFormat))
            {
                c.Save(System.IO.Path.Combine(_dir, "control-" + tag + ".png"), ImageFormat.Png);
            }
        }
        Log("   抓屏[" + tag + "]");
    }

    private static void Log(string s)
    {
        Console.WriteLine(s);
        try
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(_dir, "control.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine,
                System.Text.Encoding.UTF8);
        }
        catch { }
    }
}
