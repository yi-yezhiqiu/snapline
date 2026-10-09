using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

/// <summary>
/// 微信式截图验证：剪贴板里**只放 DIB**（没有 PNG 格式），
/// 而且 32 位 alpha 全是 0 —— 这正是"微信截图挂上去是黑的"的典型形态。
/// 跑完后检查程序实际抓到的图颜色是否正确。
/// </summary>
internal static class WeChatProbe
{
    private const string Exe = @"D:\截图晾晒工具\dist\晾晒工具.exe";
    private static string _dir = @"D:\截图晾晒工具\.probe";
    private static string _dump = Path.Combine(Path.GetTempPath(), "liangshai-last-capture.png");

    [STAThread]
    private static int Main()
    {
        Directory.CreateDirectory(_dir);
        Log("=== 微信式 DIB 验证开始 ===");

        if (File.Exists(_dump)) { File.Delete(_dump); }

        if (Process.GetProcessesByName("晾晒工具").Length == 0)
        {
            var psi = new ProcessStartInfo(Exe, "/diag")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            Process.Start(psi);
            Log("已启动应用 (/diag)");
            Thread.Sleep(4000);
        }
        else { Log("应用已在运行"); }

        // ---- 1. 构造一张只有 DIB 的图，alpha 全 0 ----
        const int W = 400, H = 240;
        byte[] dib = BuildWeChatDib(W, H, zeroAlpha: true);
        Log("1) 构造 DIB " + dib.Length + " 字节, " + W + "x" + H + ", alpha全0");

        // ---- 2. 只把 DIB 放进剪贴板（故意不放 PNG）----
        SetClipboardDibOnly(dib);
        Log("2) 已放入剪贴板（只提供 DIB）");
        Log("   ContainsImage = " + Clipboard.ContainsImage());
        Log("   ContainsData(PNG) = " + Clipboard.ContainsData("PNG"));
        Log("   ContainsData(DeviceIndependentBitmap) = " + Clipboard.ContainsData("DeviceIndependentBitmap"));

        // ---- 3. 等程序抓取 ----
        Thread.Sleep(1600);

        // ---- 4. 检查程序抓到的结果 ----
        if (!File.Exists(_dump))
        {
            Log("✗ 程序没有 dump 出捕获结果（没抓到图？）");
            return 1;
        }

        using (var bmp = new Bitmap(_dump))
        {
            Log("3) 程序抓到的图: " + bmp.Width + "x" + bmp.Height);

            // 抓到的图应该是四色块，抽样检查每一块的中心
            Color left = At(bmp, 0.12, 0.5);
            Color mid = At(bmp, 0.50, 0.5);
            Color right = At(bmp, 0.88, 0.5);

            Log("   左=(" + Fmt(left) + ")  中=(" + Fmt(mid) + ")  右=(" + Fmt(right) + ")");

            bool okLeft = Near(left, 220, 60, 60);
            bool okMid = Near(mid, 60, 200, 90);
            bool okRight = Near(right, 60, 60, 220);
            bool notBlack = (left.R + left.G + left.B) > 60;

            // 顺便存一张对比图
            bmp.Save(Path.Combine(_dir, "wechat-captured.png"), ImageFormat.Png);

            if (okLeft && okMid && okRight && notBlack)
            {
                Log("★ 结论：微信式 DIB（alpha全0）解码正确，没有变黑");
                return 0;
            }
            Log(string.Format("✗ 结论：颜色不对 (左对={0} 中对={1} 右对={2} 非黑={3})",
                okLeft, okMid, okRight, notBlack));
            return 1;
        }
    }

    private static Color At(Bitmap b, double fx, double fy)
    {
        int x = Math.Max(0, Math.Min(b.Width - 1, (int)(b.Width * fx)));
        int y = Math.Max(0, Math.Min(b.Height - 1, (int)(b.Height * fy)));
        return b.GetPixel(x, y);
    }

    private static string Fmt(Color c) { return c.R + "," + c.G + "," + c.B + ",a" + c.A; }

    private static bool Near(Color c, int r, int g, int b)
    {
        return Math.Abs(c.R - r) <= 8 && Math.Abs(c.G - g) <= 8 && Math.Abs(c.B - b) <= 8;
    }

    /// <summary>按微信/截图工具的典型形态构造 DIB：32 位、BI_RGB、自下而上。</summary>
    private static byte[] BuildWeChatDib(int width, int height, bool zeroAlpha)
    {
        const int bpp = 32;
        int stride = width * 4;
        int imageSize = stride * height;
        var ms = new MemoryStream(40 + imageSize);
        using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, true))
        {
            w.Write(40); w.Write(width); w.Write(height);
            w.Write((short)1); w.Write((short)bpp);
            w.Write(0); w.Write(imageSize);
            w.Write(2835); w.Write(2835); w.Write(0); w.Write(0);

            for (int y = 0; y < height; y++)
            {
                var line = new byte[stride];
                for (int x = 0; x < width; x++)
                {
                    byte r, g, b;
                    if (x < width / 3) { r = 220; g = 60; b = 60; }
                    else if (x < width * 2 / 3) { r = 60; g = 200; b = 90; }
                    else { r = 60; g = 60; b = 220; }

                    int o = x * 4;
                    line[o] = b; line[o + 1] = g; line[o + 2] = r;
                    line[o + 3] = zeroAlpha ? (byte)0 : (byte)255;
                }
                w.Write(line);
            }
        }
        return ms.ToArray();
    }

    private static void SetClipboardDibOnly(byte[] dib)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var d = new DataObject();
                // 关键：只给 DIB，不给 PNG、不给 Bitmap
                d.SetData("DeviceIndependentBitmap", new MemoryStream(dib));
                Clipboard.SetDataObject(d, true);
                return;
            }
            catch (Exception ex)
            {
                Log("   剪贴板被占用，重试 " + (attempt + 1) + ": " + ex.Message);
                Thread.Sleep(250);
            }
        }
    }

    private static void Log(string s)
    {
        Console.WriteLine(s);
        try
        {
            File.AppendAllText(Path.Combine(_dir, "wechat.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine,
                System.Text.Encoding.UTF8);
        }
        catch { }
    }
}
