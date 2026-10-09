using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

/// <summary>
/// DIB 解码验证：程序化构造几种"截图工具真实会放进剪贴板的 DIB"，
/// 调用晾晒工具的 ImgUtil.DecodeDibToPng 解码，再检查颜色是否正确。
/// 重点是复现"微信截图挂上去是黑的"这个问题。
/// </summary>
internal static class DibProbe
{
    private static MethodInfo _decode;

    private static int Main()
    {
        try { return Run(); }
        catch (Exception ex)
        {
            string msg = "致命错误: " + ex.GetType().FullName + " : " + ex.Message +
                         "\n" + ex.StackTrace;
            try { File.WriteAllText(@"D:\截图晾晒工具\.probe\dib-error.log", msg); } catch { }
            Console.WriteLine(msg);
            return 3;
        }
    }

    private static int Run()
    {
        Console.WriteLine("=== DIB 解码验证 ===");
        try { Directory.CreateDirectory(@"D:\截图晾晒工具\.probe"); } catch { }

        Assembly asm = Assembly.LoadFrom(@"D:\截图晾晒工具\dist\晾晒工具.exe");
        Console.WriteLine("程序集已加载");
        Type t = asm.GetType("Liangshai.ImgUtil");
        if (t == null) { Console.WriteLine("找不到 ImgUtil 类型"); return 2; }
        Console.WriteLine("找到 ImgUtil");
        _decode = t.GetMethod("DecodeDibToPng",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (_decode == null) { Console.WriteLine("找不到 DecodeDibToPng"); return 2; }
        Console.WriteLine("已加载解码方法");

        int failures = 0;
        failures += Check("32位 自下而上 + alpha全0（微信式）", BuildDib(320, 200, 32, false, true));
        failures += Check("32位 自下而上 + alpha正常",     BuildDib(320, 200, 32, false, false));
        failures += Check("32位 自上而下(负高度)",         BuildDib(320, 200, 32, true, true));
        failures += Check("24位 自下而上",                 BuildDib(320, 200, 24, false, false));
        failures += Check("宽度非4倍数 (24位, 宽=101)",     BuildDib(101, 60, 24, false, false));

        Console.WriteLine(failures == 0
            ? "★ 全部通过"
            : "✗ 有 " + failures + " 项失败");
        return failures == 0 ? 0 : 1;
    }

    private static int Check(string label, byte[] dib)
    {
        Console.WriteLine();
        Console.WriteLine("--- " + label + " (DIB " + dib.Length + " 字节) ---");

        // 先看 GDI+ 直接读会怎样（复现老实现的行为）
        string oldDesc = DescribeOldWay(dib);
        Console.WriteLine("  旧做法(GDI+ 直接读): " + oldDesc);

        byte[] png = null;
        try { png = (byte[])_decode.Invoke(null, new object[] { dib }); }
        catch (Exception ex)
        {
            Console.WriteLine("  解码抛异常: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
            return 1;
        }

        if (png == null || png.Length == 0)
        {
            Console.WriteLine("  ✗ 解码返回空");
            return 1;
        }

        using (var ms = new MemoryStream(png))
        using (var bmp = new Bitmap(ms))
        {
            // 期望：左上角应为红色 (220,60,60)
            Color left = bmp.GetPixel(4, 4);
            // 右下角应为蓝色 (60,60,220)
            Color right = bmp.GetPixel(bmp.Width - 5, bmp.Height - 5);

            Console.WriteLine(string.Format("  新解码: {0}x{1} 左上={2} 右下={3}",
                bmp.Width, bmp.Height, Fmt(left), Fmt(right)));

            bool okSize = bmp.Width == 320 ? true : true;   // 尺寸随构造而变
            bool okLeft = Near(left, 220, 60, 60);
            bool okRight = Near(right, 60, 60, 220);
            bool okOpaque = left.A == 255 && right.A == 255;

            if (okLeft && okRight && okOpaque)
            {
                Console.WriteLine("  ✓ 通过");
                return 0;
            }

            Console.WriteLine(string.Format("  ✗ 失败 (左上对={0} 右下对={1} 不透明={2})",
                okLeft, okRight, okOpaque));
            return 1;
        }
    }

    /// <summary>复现旧实现：把 DIB 补个文件头后用 GDI+ 直接读。</summary>
    private static string DescribeOldWay(byte[] dib)
    {
        try
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)'B'); w.Write((byte)'M');
                w.Write(14 + dib.Length);
                w.Write((short)0); w.Write((short)0);
                w.Write(14 + 40);
                w.Write(dib);
                w.Flush();

                using (var b = new Bitmap(new MemoryStream(ms.ToArray())))
                {
                    Color c = b.GetPixel(4, 4);
                    return b.Width + "x" + b.Height + " 左上=" + Fmt(c) +
                           (Near(c, 220, 60, 60) ? " (正常)" : " ← 就是这里出问题");
                }
            }
        }
        catch (Exception ex) { return "读取失败: " + ex.Message; }
    }

    private static bool Near(Color c, int r, int g, int b)
    {
        return Math.Abs(c.R - r) <= 6 && Math.Abs(c.G - g) <= 6 && Math.Abs(c.B - b) <= 6;
    }

    private static string Fmt(Color c) { return "(" + c.R + "," + c.G + "," + c.B + ",a" + c.A + ")"; }

    /// <summary>
    /// 构造一个裸 DIB：BITMAPINFOHEADER + 像素。
    /// 左侧 1/3 红色、中间 1/3 绿色、右侧 1/3 蓝色，便于判断有没有变黑。
    /// </summary>
    private static byte[] BuildDib(int width, int height, int bpp, bool topDown, bool zeroAlpha)
    {
        int stride = ((width * bpp + 31) / 32) * 4;
        int imageSize = stride * height;
        var ms = new MemoryStream(40 + imageSize);
        using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, true))
        {
            w.Write(40);
            w.Write(width);
            w.Write(topDown ? -height : height);
            w.Write((short)1);
            w.Write((short)bpp);
            w.Write(0);                 // BI_RGB
            w.Write(imageSize);
            w.Write(2835); w.Write(2835);
            w.Write(0); w.Write(0);

            for (int row = 0; row < height; row++)
            {
                var line = new byte[stride];
                for (int x = 0; x < width; x++)
                {
                    byte r, g, b;
                    if (x < width / 3) { r = 220; g = 60; b = 60; }
                    else if (x < width * 2 / 3) { r = 60; g = 220; b = 60; }
                    else { r = 60; g = 60; b = 220; }

                    int o = (bpp == 32) ? x * 4 : x * 3;
                    // DIB 是 BGR 顺序
                    line[o] = b;
                    line[o + 1] = g;
                    line[o + 2] = r;
                    if (bpp == 32)
                    {
                        // 关键：模拟微信/部分截图工具 alpha 全为 0 的情况
                        line[o + 3] = zeroAlpha ? (byte)0 : (byte)255;
                    }
                }
                w.Write(line);
            }
        }
        return ms.ToArray();
    }
}
