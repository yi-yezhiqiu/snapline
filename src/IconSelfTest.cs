using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Liangshai
{
    /// <summary>
    /// 托盘图标自检（命令行 /icontest）。
    ///
    /// 回答一个问题：手工写的 ICO 字节流，.NET 到底能不能正确读出来？
    /// 判据是"颜色是否成片"——正常的图标颜色种类很少，
    /// 如果读出几百种颜色，说明被当成原始像素误解了，也就是噪点。
    /// </summary>
    internal static class IconSelfTest
    {
        public static void Run()
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("=== 托盘图标自检 ===");

            byte[] ico = null;
            try
            {
                ico = IcoWriter.Build(new int[] { 16, 20, 24, 32, 48, 64 });
                log.AppendLine("已生成 ICO " + ico.Length + " 字节");
            }
            catch (Exception ex)
            {
                log.AppendLine("生成失败: " + ex.Message);
                App.Trace(log.ToString());
                return;
            }

            int[] checkSizes = new int[] { 16, 32, 48 };
            int ok = 0, bad = 0;
            foreach (int size in checkSizes)
            {
                if (Describe(log, size, ico)) { ok++; } else { bad++; }
            }

            log.AppendLine();
            log.AppendLine("通过 " + ok + " / 失败 " + bad);
            log.AppendLine(bad == 0
                ? "★ ICO 读写链路正常"
                : "✗ ICO 读回来是噪点，这个写法在本环境不可用");

            // dump 出来供肉眼确认
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "liangshai-icon");
                Directory.CreateDirectory(dir);
                using (var icon = new Icon(new MemoryStream(ico)))
                {
                    foreach (int size in new int[] { 16, 24, 32, 48 })
                    {
                        using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                        {
                            using (Graphics g = Graphics.FromImage(bmp))
                            {
                                g.Clear(Color.Transparent);
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                                g.DrawIcon(icon, new Rectangle(0, 0, size, size));
                            }
                            bmp.Save(Path.Combine(dir, "icon-" + size + ".png"), ImageFormat.Png);
                        }
                    }
                }
                log.AppendLine("已 dump 到 " + dir);
            }
            catch (Exception ex) { log.AppendLine("dump 失败: " + ex.Message); }

            App.Trace(log.ToString().TrimEnd());
        }

        private static bool Describe(System.Text.StringBuilder log, int size, byte[] ico)
        {
            try
            {
                using (var icon = new Icon(new MemoryStream(ico), size, size))
                using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.DrawIcon(icon, new Rectangle(0, 0, size, size));
                    }

                    var seen = new System.Collections.Generic.HashSet<int>();
                    int opaque = 0, total = 0;
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            Color c = bmp.GetPixel(x, y);
                            total++;
                            if (c.A < 16) { continue; }
                            opaque++;
                            seen.Add((c.R >> 3 << 10) | (c.G >> 3 << 5) | (c.B >> 3));
                        }
                    }

                    // 噪点判据：颜色种类超过像素数的四分之一
                    bool noisy = seen.Count > total / 4;
                    log.AppendLine(string.Format("  {0}px: 颜色种类={1} 不透明像素={2}/{3} -> {4}",
                        size, seen.Count, opaque, total, noisy ? "✗ 噪点" : "✓ 正常"));

                    if (noisy)
                    {
                        bmp.Save(Path.Combine(Path.GetTempPath(), "liangshai-icon",
                            "noisy-" + size + ".png"), ImageFormat.Png);
                    }
                    return !noisy;
                }
            }
            catch (Exception ex)
            {
                log.AppendLine("  " + size + "px: ✗ 解析异常 " + ex.Message);
                return false;
            }
        }
    }
}
