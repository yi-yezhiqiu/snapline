using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Liangshai
{
    /// <summary>
    /// DIB 解码自检（命令行 /selftest）。
    ///
    /// 目的：验证"微信截图挂上去是黑的"这个问题确实被修掉。
    /// 程序化构造几种截图工具真实会放进剪贴板的 DIB 变体，
    /// 走一遍生产代码的 DecodeDibToPng，再检查解出来的颜色对不对。
    /// </summary>
    internal static class DibSelfTest
    {
        public static void Run()
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("=== DIB 解码自检 ===");

            int failures = 0;

            failures += Check(log, "32位 自下而上 + alpha全0（微信/截图工具典型）",
                BuildDib(320, 200, 32, false, true));
            failures += Check(log, "32位 自下而上 + alpha正常",
                BuildDib(320, 200, 32, false, false));
            failures += Check(log, "32位 自上而下（biHeight 为负）",
                BuildDib(320, 200, 32, true, true));
            failures += Check(log, "24位 自下而上",
                BuildDib(320, 200, 24, false, false));
            failures += Check(log, "宽度非4倍数（24位，宽=101）",
                BuildDib(101, 60, 24, false, false));
            failures += Check(log, "32位 BI_BITFIELDS（标准 BGRA 掩码）",
                BuildBitfieldsDib(320, 200));
            failures += Check(log, "8位 调色板索引色",
                BuildIndexedDib(64, 40));
            failures += Check(log, "RLE 压缩（应安全回退，不崩不黑）",
                BuildRleDib(64, 40), true);

            log.AppendLine();
            log.AppendLine(failures == 0 ? "★ 全部通过" : "✗ 有 " + failures + " 项失败");

            App.Trace(log.ToString().TrimEnd());
        }

        private static int Check(System.Text.StringBuilder log, string label, byte[] dib)
        {
            return Check(log, label, dib, false);
        }

        /// <param name="expectFallback">true 表示允许解码器主动放弃（返回 null），
        /// 由调用链回退到 GDI+；只要不是"解出一张黑图"就算通过。</param>
        private static int Check(System.Text.StringBuilder log, string label, byte[] dib,
                                 bool expectFallback)
        {
            log.AppendLine();
            log.AppendLine("--- " + label + " (DIB " + dib.Length + " 字节) ---");
            log.AppendLine("  旧做法(GDI+ 直接读): " + DescribeOldWay(dib));

            byte[] png;
            try { png = ImgUtil.DecodeDibToPng(dib); }
            catch (Exception ex)
            {
                log.AppendLine("  ✗ 解码抛异常: " + ex.Message);
                return 1;
            }

            if (png == null || png.Length == 0)
            {
                if (expectFallback)
                {
                    log.AppendLine("  ✓ 主动放弃解码（会回退到 GDI+），符合预期");
                    return 0;
                }
                log.AppendLine("  ✗ 解码返回空");
                return 1;
            }

            try
            {
                using (var ms = new MemoryStream(png))
                using (var bmp = new Bitmap(ms))
                {
                    Color left = bmp.GetPixel(Math.Min(4, bmp.Width - 1), Math.Min(4, bmp.Height - 1));
                    Color right = bmp.GetPixel(bmp.Width - 1, bmp.Height - 1);

                    log.AppendLine(string.Format("  新解码: {0}x{1} 左上={2} 右下={3}",
                        bmp.Width, bmp.Height, Fmt(left), Fmt(right)));

                    // 构造时左边是红(220,60,60)，右边是蓝(60,60,220)
                    bool okLeft = Near(left, 220, 60, 60);
                    bool okRight = Near(right, 60, 60, 220);
                    bool okOpaque = left.A == 255 && right.A == 255;

                    if (okLeft && okRight && okOpaque)
                    {
                        log.AppendLine("  ✓ 通过");
                        return 0;
                    }
                    log.AppendLine(string.Format("  ✗ 失败 (左上对={0} 右下对={1} 不透明={2})",
                        okLeft, okRight, okOpaque));
                    return 1;
                }
            }
            catch (Exception ex)
            {
                log.AppendLine("  ✗ 结果不是有效 PNG: " + ex.Message);
                return 1;
            }
        }

        /// <summary>复现旧实现：补个文件头后交给 GDI+ 读，看它会不会出错。</summary>
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
                               (Near(c, 220, 60, 60) ? " (正常)" : " ← 这里就是黑的成因");
                    }
                }
            }
            catch (Exception ex) { return "读取失败: " + ex.Message; }
        }

        private static bool Near(Color c, int r, int g, int b)
        {
            return Math.Abs(c.R - r) <= 6 && Math.Abs(c.G - g) <= 6 && Math.Abs(c.B - b) <= 6;
        }

        private static string Fmt(Color c)
        {
            return "(" + c.R + "," + c.G + "," + c.B + ",a" + c.A + ")";
        }

        /// <summary>
        /// 构造裸 DIB：BITMAPINFOHEADER + 像素。
        /// 左 1/3 红、中 1/3 绿、右 1/3 蓝，方便判断有没有变黑。
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
                WritePixels(w, width, height, stride, bpp, zeroAlpha);
            }
            return ms.ToArray();
        }

        /// <summary>BI_BITFIELDS：头部后面紧跟 R/G/B（+A）掩码。</summary>
        private static byte[] BuildBitfieldsDib(int width, int height)
        {
            const int bpp = 32;
            int stride = width * 4;
            int imageSize = stride * height;
            var ms = new MemoryStream(40 + 4 * 4 + imageSize);
            using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, true))
            {
                w.Write(40);
                w.Write(width);
                w.Write(height);
                w.Write((short)1);
                w.Write((short)bpp);
                w.Write(3);                 // BI_BITFIELDS
                w.Write(imageSize);
                w.Write(2835); w.Write(2835);
                w.Write(0); w.Write(0);
                w.Write(0x00FF0000);        // R
                w.Write(0x0000FF00);        // G
                w.Write(0x000000FF);        // B
                w.Write(0xFF000000u);       // A
                WritePixels(w, width, height, stride, bpp, true);
            }
            return ms.ToArray();
        }

        /// <summary>8 位索引色：调色板三项（红/绿/蓝）。</summary>
        private static byte[] BuildIndexedDib(int width, int height)
        {
            const int bpp = 8;
            int stride = ((width * bpp + 31) / 32) * 4;
            int imageSize = stride * height;
            const int paletteEntries = 3;
            var ms = new MemoryStream(40 + paletteEntries * 4 + imageSize);
            using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, true))
            {
                w.Write(40); w.Write(width); w.Write(height);
                w.Write((short)1); w.Write((short)bpp);
                w.Write(0); w.Write(imageSize);
                w.Write(2835); w.Write(2835);
                w.Write(paletteEntries); w.Write(0);

                // DIB 调色板是 BGRX
                w.Write(new byte[] { 60, 60, 220, 0 });     // 索引0 = 红
                w.Write(new byte[] { 60, 200, 90, 0 });     // 索引1 = 绿
                w.Write(new byte[] { 220, 60, 60, 0 });     // 索引2 = 蓝

                for (int y = 0; y < height; y++)
                {
                    var line = new byte[stride];
                    for (int x = 0; x < width; x++)
                    {
                        line[x] = (byte)(x < width / 3 ? 0 : (x < width * 2 / 3 ? 1 : 2));
                    }
                    w.Write(line);
                }
            }
            return ms.ToArray();
        }

        /// <summary>BI_RLE8：解不了，只要求安全回退而不是画出一张黑图。</summary>
        private static byte[] BuildRleDib(int width, int height)
        {
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, true))
            {
                w.Write(40); w.Write(width); w.Write(height);
                w.Write((short)1); w.Write((short)8);
                w.Write(1);                 // BI_RLE8
                w.Write(64);
                w.Write(2835); w.Write(2835);
                w.Write(0); w.Write(0);
                for (int i = 0; i < 64; i++) { w.Write((byte)0x33); }
            }
            return ms.ToArray();
        }

        private static void WritePixels(BinaryWriter w, int width, int height, int stride,
                                        int bpp, bool zeroAlpha)
        {
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
                    line[o] = b;            // DIB 是 BGR 顺序
                    line[o + 1] = g;
                    line[o + 2] = r;
                    if (bpp == 32) { line[o + 3] = zeroAlpha ? (byte)0 : (byte)255; }
                }
                w.Write(line);
            }
        }
    }
}
