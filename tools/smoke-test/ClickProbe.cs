using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

/// <summary>
/// 点击复制验证：把一张测试图挂上绳子，用真实鼠标点击卡片，
/// 再检查剪贴板里是否出现了那张图 —— 验证这条最核心的交互链路。
/// </summary>
internal static class ClickProbe
{
    private const string Exe = @"D:\截图晾晒工具\dist\晾晒工具.exe";
    private static string _dir = @"D:\截图晾晒工具\.probe";

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [STAThread]
    private static int Main()
    {
        System.IO.Directory.CreateDirectory(_dir);
        Log("=== 点击复制验证开始 ===");

        bool started = false;
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
            started = true;
            Log("已启动应用");
            Thread.Sleep(4000);
        }
        else { Log("应用已在运行"); }

        Rectangle scr = Screen.PrimaryScreen.Bounds;
        Log("屏幕 " + scr.Width + "x" + scr.Height);

        // ---- 1. 先清空剪贴板为文字，避免误判 ----
        SetClipboardText("liangshai-click-probe-before");
        Thread.Sleep(200);
        Log("1) 剪贴板已置为文字, ContainsImage=" + Clipboard.ContainsImage());

        // ---- 2. 把测试图放进剪贴板，让它挂上绳子 ----
        string png = System.IO.Path.Combine(_dir, "click-test.png");
        MakeTestImage(png, "CLICK ME");
        SetClipboardImage(png);
        Log("2) 测试图已进剪贴板");
        Thread.Sleep(1200);   // 等挂上 + 绳子滑出

        // ---- 3. 卡片应该挂在屏幕中央 ----
        // 独立一张卡片时，原版公式：x = width/2
        // 注意：SetCursorPos 用的是"已缩放"的坐标（125% 缩放下 ×1.25），
        // 所以这里要把卡片的逻辑坐标换算回去。
        int cardX = (int)((scr.Width / 2) / 1.25) - 8;
        int cardY = 90;
        Log("3) 准备点击卡片中心 (" + cardX + "," + cardY + ")");

        POINT original;
        GetCursorPos(out original);

        SetCursorPos(cardX, cardY);
        Thread.Sleep(250);
        Capture("hover");

        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(70);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(350);
        Capture("after-click");

        // ---- 4. 检查剪贴板 ----
        bool hasImage = Clipboard.ContainsImage();
        Log("4) 点击后剪贴板 ContainsImage = " + hasImage);

        if (hasImage)
        {
            using (System.Drawing.Image img = Clipboard.GetImage())
            {
                Log("   剪贴板图片尺寸 = " + img.Width + "x" + img.Height);
                bool match = img.Width == 720 && img.Height == 420;
                Log("   尺寸匹配测试图(720x420) = " + match);
            }
        }

        SetCursorPos(original.X, original.Y);
        Log(hasImage ? "★ 结论：点击复制链路正常" : "✗ 结论：点击后剪贴板没有图片");
        Log("=== 验证结束 ===");
        return hasImage ? 0 : 1;
    }

    private static void MakeTestImage(string path, string label)
    {
        using (var bmp = new Bitmap(720, 420))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(255, 32, 90, 160));
            using (var accent = new SolidBrush(Color.FromArgb(255, 255, 196, 60)))
            {
                g.FillRectangle(accent, 40, 40, 640, 120);
            }
            using (var f1 = new Font("Microsoft YaHei UI", 40, FontStyle.Bold))
            {
                g.DrawString(label, f1, Brushes.Black, 50, 68);
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }

    private static void SetClipboardText(string s)
    {
        for (int i = 0; i < 8; i++)
        {
            try
            {
                var d = new DataObject();
                d.SetText(s);
                Clipboard.SetDataObject(d, true);
                return;
            }
            catch { Thread.Sleep(200); }
        }
    }

    private static void SetClipboardImage(string path)
    {
        byte[] bytes = System.IO.File.ReadAllBytes(path);
        using (var bmp = new Bitmap(path))
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var d = new DataObject();
                    d.SetImage(bmp);
                    d.SetData("PNG", new System.IO.MemoryStream(bytes));
                    Clipboard.SetDataObject(d, true);
                    return;
                }
                catch { Thread.Sleep(250); }
            }
        }
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
            int h = Math.Min(300, b.Height);
            using (var top = full.Clone(new Rectangle(0, 0, b.Width, h), full.PixelFormat))
            {
                top.Save(System.IO.Path.Combine(_dir, "click-" + tag + ".png"), ImageFormat.Png);
            }
        }
        Log("   抓屏[" + tag + "]");
    }

    private static void Log(string s)
    {
        Console.WriteLine(s);
        try
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(_dir, "click.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine,
                System.Text.Encoding.UTF8);
        }
        catch { }
    }
}

