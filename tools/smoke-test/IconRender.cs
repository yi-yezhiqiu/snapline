using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

/// <summary>
/// 渲染晾晒工具的托盘图标，放大后存成图片，用来肉眼检查图标画得对不对。
/// 使用与 TrayIcon.IconFactory 完全相同的绘制代码。
/// </summary>
internal static class IconRender
{
    private static int Main()
    {
        string outDir = @"D:\截图晾晒工具\.probe";
        Directory.CreateDirectory(outDir);

        // 画成 32x32，再各放大到一个大图里，方便看清
        using (var bmp = new Bitmap(32, 32))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Draw(g);
            }

            bmp.Save(Path.Combine(outDir, "tray-icon-32.png"), ImageFormat.Png);

            // 放大到 256，最近邻，看清每个像素
            using (var big = new Bitmap(256, 256, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.Clear(Color.FromArgb(255, 40, 40, 46));   // 深色任务栏背景
                g.DrawImage(bmp, new Rectangle(0, 0, 256, 256));
            }
            // 需要另存，作用域内保存
            using (var big = new Bitmap(256, 256, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.Clear(Color.FromArgb(255, 243, 243, 243));  // 浅色任务栏背景
                g.DrawImage(bmp, new Rectangle(0, 0, 256, 256));
                big.Save(Path.Combine(outDir, "tray-icon-dark-on-light.png"), ImageFormat.Png);
            }
        }

        // 再模拟成任务栏上的实际大小（16px 和 20px）以便判断辨识度
        using (var bmp = new Bitmap(32, 32))
        {
            using (Graphics g = Graphics.FromImage(bmp)) { Draw(g); }
            foreach (int size in new[] { 16, 20, 24 })
            {
                using (var strip = new Bitmap(size * 4, size, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(strip))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.FromArgb(255, 243, 243, 243));
                    g.DrawImage(bmp, new Rectangle(0, 0, size, size));
                    // 第二份放在深色背景上
                    using (var b2 = new SolidBrush(Color.FromArgb(255, 32, 32, 36)))
                    {
                        g.FillRectangle(b2, size, 0, size * 3, size);
                    }
                    g.DrawImage(bmp, new Rectangle(size, 0, size, size));
                    strip.Save(Path.Combine(outDir, "tray-icon-strip-" + size + ".png"), ImageFormat.Png);
                }
            }
        }

        Console.WriteLine("图标已渲染到 " + outDir);
        return 0;
    }

    /// <summary>与 TrayIcon.IconFactory.Create 内部完全一致的绘制逻辑。</summary>
    private static void Draw(Graphics g)
    {
        const int size = 32;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        using (var pen = new Pen(Color.FromArgb(255, 0x8A, 0x74, 0x55), 2.6f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            g.DrawBezier(pen, new PointF(2, 7), new PointF(10, 11),
                new PointF(22, 11), new PointF(30, 7));
        }

        DrawPhoto(g, new RectangleF(5.5f, 9.5f, 9.5f, 13f), Color.FromArgb(0xFF, 0xE8, 0xA8, 0x3C));
        DrawPhoto(g, new RectangleF(17f, 9.5f, 9.5f, 13f), Color.FromArgb(0xFF, 0x6F, 0xA8, 0xD8));

        using (var clipBrush = new SolidBrush(Color.FromArgb(0xFF, 0xD9, 0xBE, 0x93)))
        {
            g.FillRectangle(clipBrush, 8.5f, 8.2f, 3.4f, 3.4f);
            g.FillRectangle(clipBrush, 20f, 8.2f, 3.4f, 3.4f);
        }
    }

    private static void DrawPhoto(Graphics g, RectangleF r, Color accent)
    {
        using (var path = RoundedRect(r, 1.6f))
        {
            using (var fill = new SolidBrush(Color.White)) { g.FillPath(fill, path); }
            using (var pen = new Pen(Color.FromArgb(0xFF, 0x7A, 0x63, 0x45), 1.1f)) { g.DrawPath(pen, path); }
        }
        var inner = new RectangleF(r.X + 1.6f, r.Y + 1.6f, r.Width - 3.2f, r.Height - 3.2f);
        using (var brush = new SolidBrush(accent)) { g.FillRectangle(brush, inner); }
        using (var brush = new SolidBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)))
        {
            g.FillEllipse(brush, inner.X + inner.Width * 0.5f, inner.Y + inner.Height * 0.12f,
                inner.Width * 0.32f, inner.Width * 0.32f);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
