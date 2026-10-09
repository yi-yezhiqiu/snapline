using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

/// <summary>
/// 顶端呼出验证：把光标移到屏幕上边缘，等过 0.25 秒的触发延迟，
/// 抓屏确认绳子是否滑出来；再把光标移开，确认它收回。
/// </summary>
internal static class TopEdgeProbe
{
    private const string Exe = @"D:\截图晾晒工具\dist\晾晒工具.exe";
    private static string _dir = @"D:\截图晾晒工具\.probe";

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [STAThread]
    private static int Main()
    {
        System.IO.Directory.CreateDirectory(_dir);
        Log("=== 顶端呼出验证开始 ===");

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
            Thread.Sleep(4000);
        }
        else { Log("应用已在运行"); }

        POINT original;
        GetCursorPos(out original);
        Log("原始光标位置 = " + original.X + "," + original.Y);

        Rectangle b = Screen.PrimaryScreen.Bounds;
        Log("屏幕 " + b.Width + "x" + b.Height);

        // ---- 1. 光标移到屏幕上边缘正中 ----
        Log("1) 光标移到 (" + (b.Width / 2) + ", 2)");
        SetCursorPos(b.Width / 2, 2);
        Thread.Sleep(120);                 // 还没到 250ms 延迟
        Capture("top-early");              // 应该还没出来
        Thread.Sleep(600);                 // 越过 250ms 延迟 + 动画
        Capture("top-revealed");           // 应该已经滑出

        // ---- 2. 光标移开 ----
        Log("2) 光标移到屏幕中部");
        SetCursorPos(b.Width / 2, b.Height / 2);
        Thread.Sleep(1300);                // 500ms 收回延迟 + 动画
        Capture("top-retracted");          // 应该已经收回

        // ---- 3. 还原光标 ----
        SetCursorPos(original.X, original.Y);

        Log("=== 验证结束 ===");
        return 0;
    }

    private static void Capture(string tag)
    {
        Rectangle b = Screen.PrimaryScreen.Bounds;
        using (var full = new Bitmap(b.Width, b.Height))
        {
            using (Graphics g = Graphics.FromImage(full))
            {
                g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size);
            }
            int h = Math.Min(260, b.Height);
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
            System.IO.File.AppendAllText(System.IO.Path.Combine(_dir, "topedge.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine,
                System.Text.Encoding.UTF8);
        }
        catch { }
    }
}
