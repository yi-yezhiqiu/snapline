// 冒烟测试探针 v2：自己脱离启动应用 + 模拟截图进剪贴板 + 抓屏验证。
// 编译: csc /target:exe Probe.cs
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

internal static class Probe
{
    private const string Exe = @"D:\截图晾晒工具\dist\晾晒工具.exe";
    private static string _dir;

    [STAThread]
    private static int Main(string[] args)
    {
        _dir = @"D:\截图晾晒工具\.probe";
        Directory.CreateDirectory(_dir);
        Log("=== 冒烟测试开始 ===");

        // ---- 1. 确保应用在跑（带 /diag 进入诊断模式） ----
        if (Process.GetProcessesByName("晾晒工具").Length == 0)
        {
            try
            {
                // RedirectStandard* = true 会新建管道，避免子进程继承本进程的控制台句柄，
                // 否则探针退出后父进程的 stdout 管道仍然被占着，会挂住。
                var psi = new ProcessStartInfo(Exe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                Log("1) 已启动应用");
            }
            catch (Exception ex) { Log("1) 启动失败: " + ex.Message); return 2; }
            Thread.Sleep(3500); // 等窗口/托盘/监听就绪
        }
        else
        {
            Log("1) 应用已在运行");
        }
        Log("   存活进程数 = " + Process.GetProcessesByName("晾晒工具").Length);




        // ---- 2. 造测试图 ----
        string png = Path.Combine(_dir, "clip-test.png");
        MakeTestImage(png);
        Log("2) 测试图 " + new FileInfo(png).Length + " 字节");

        // ---- 3. 模拟"微信 Alt+A 截完图"：图片进剪贴板 ----
        if (!SetClipboardImage(png)) { Log("3) 写剪贴板失败"); return 3; }
        Log("3) 图片已进剪贴板, ContainsImage = " + Clipboard.ContainsImage());

        // ---- 4. 等程序捕获 + 绳子滑出（防抖 150ms + 取图 + 280ms 动画） ----
        Thread.Sleep(1200);
        Log("4) 等待 1200ms 完成，抓屏");

        // ---- 5. 抓屏 ----
        Capture("after-capture");
        Thread.Sleep(3600);
        Capture("after-retracted");
        Log("5) 抓屏完成");

        // ---- 6. 收尾：剪贴板还原成文字 ----
        try
        {
            var t = new DataObject();
            t.SetText("liangshai smoke test done");
            Clipboard.SetDataObject(t, true);
            Log("6) 剪贴板已还原");
        }
        catch (Exception ex) { Log("6) 还原失败: " + ex.Message); }

        Log("存活进程数 = " + Process.GetProcessesByName("晾晒工具").Length);
        Log("=== 冒烟测试结束 ===");
        return 0;
    }

    /// <summary>模拟按下 Ctrl+Alt+T，呼出绳子。</summary>
    private static void SendHotkeyToggle()
    {
        const byte VK_CONTROL = 0x11, VK_MENU = 0x12, VK_T = 0x54;
        const uint KEYEVENTF_KEYUP = 0x0002;
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_T, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        keybd_event(VK_T, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Log("   已发送 Ctrl+Alt+T");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static void Capture(string tag)
    {
        Rectangle b = Screen.PrimaryScreen.Bounds;
        using (var full = new Bitmap(b.Width, b.Height))
        {
            using (Graphics g = Graphics.FromImage(full))
            {
                g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size);
            }
            full.Save(Path.Combine(_dir, "screen-" + tag + ".png"), ImageFormat.Png);
            int h = Math.Min(250, b.Height);
            using (var top = full.Clone(new Rectangle(0, 0, b.Width, h), full.PixelFormat))
            {
                top.Save(Path.Combine(_dir, "top-" + tag + ".png"), ImageFormat.Png);
            }
        }
        Log("   抓屏[" + tag + "] " + b.Width + "x" + b.Height);
    }

    private static void MakeTestImage(string path)
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
            using (var f1 = new Font("Microsoft YaHei UI", 38, FontStyle.Bold))
            {
                g.DrawString("CLIPBOARD TEST", f1, Brushes.Black, 50, 68);
            }
            using (var f2 = new Font("Microsoft YaHei UI", 26))
            {
                g.DrawString("这是测试图 720 x 420", f2, Brushes.White, 55, 230);
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }

    private static bool SetClipboardImage(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using (var bmp = new Bitmap(path))
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    var data = new DataObject();
                    data.SetImage(bmp);
                    data.SetData("PNG", new MemoryStream(bytes));
                    // 再补一份 DIB，模拟只写 DIB 的截图工具
                    data.SetData("DeviceIndependentBitmap", new MemoryStream(DibFrom(bmp)));
                    Clipboard.SetDataObject(data, true);
                    return true;
                }
                catch (Exception ex)
                {
                    Log("   剪贴板被占用，重试 " + (attempt + 1) + ": " + ex.Message);
                    Thread.Sleep(250);
                }
            }
        }
        return false;
    }

    /// <summary>把 Bitmap 转成剪贴板用的 DIB 字节（BITMAPINFOHEADER + 像素，无文件头）。</summary>
    private static byte[] DibFrom(Bitmap src)
    {
        using (var bmp = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp)) { g.DrawImage(src, 0, 0, src.Width, src.Height); }

            int w = bmp.Width, h = bmp.Height;
            int stride = w * 4;
            int imageSize = stride * h;
            var ms = new MemoryStream(14 + 40 + imageSize);

            using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, true))
            {
                // BITMAPFILEHEADER
                bw.Write((byte)'B'); bw.Write((byte)'M');
                bw.Write(14 + 40 + imageSize);
                bw.Write((short)0); bw.Write((short)0);
                bw.Write(14 + 40);

                // BITMAPINFOHEADER
                bw.Write(40);            // biSize
                bw.Write(w);             // biWidth
                bw.Write(h);             // biHeight（正数 = 自下而上）
                bw.Write((short)1);      // biPlanes
                bw.Write((short)32);     // biBitCount
                bw.Write(0);             // biCompression = BI_RGB
                bw.Write(imageSize);     // biSizeImage
                bw.Write(2835); bw.Write(2835);
                bw.Write(0); bw.Write(0);

                var rect = new Rectangle(0, 0, w, h);
                var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    byte[] row = new byte[stride];
                    for (int y = h - 1; y >= 0; y--)   // DIB 自下而上
                    {
                        System.Runtime.InteropServices.Marshal.Copy(
                            new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, stride);
                        bw.Write(row);
                    }
                }
                finally { bmp.UnlockBits(data); }
            }
            return ms.ToArray();
        }
    }

    private static void Log(string s)
    {
        Console.WriteLine(s);
        try
        {
            File.AppendAllText(Path.Combine(_dir, "probe.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine,
                System.Text.Encoding.UTF8);
        }
        catch { }
    }
}







