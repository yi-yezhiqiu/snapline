using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Liangshai
{
    /// <summary>托盘图标：一根绳，挂着两张照片。全部用代码画。</summary>
    internal static class IconDrawing
    {
        /// <summary>
        /// 按 size 画一张图标。用 0~1 相对坐标，16px 到 256px 都能一致地画出来。
        /// 小尺寸下细节会糊，所以绳子加粗、照片放大，优先保证"一眼能认出"。
        /// </summary>
        public static void Draw(Graphics g, int size)
        {
            float S = size;
            float ropeY = S * 0.22f;
            float ropeSag = S * 0.13f;

            // 绳子：深棕，两端略超出画面，显得是从外面牵过来的
            using (var pen = new Pen(Color.FromArgb(255, 0x8A, 0x74, 0x55),
                       Math.Max(1.8f, S * 0.085f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawBezier(pen,
                    new PointF(-S * 0.05f, ropeY - S * 0.06f),
                    new PointF(S * 0.32f, ropeY + ropeSag),
                    new PointF(S * 0.68f, ropeY + ropeSag),
                    new PointF(S * 1.05f, ropeY - S * 0.06f));
            }

            // 两张照片，挂在绳子的下垂处
            float pw = S * 0.34f;
            float ph = S * 0.46f;
            float gap = S * 0.045f;
            float totalW = pw * 2 + gap;
            float x0 = (S - totalW) / 2f;
            float py = S * 0.34f;

            DrawPhoto(g, new RectangleF(x0, py, pw, ph), Color.FromArgb(255, 0xE8, 0xA8, 0x3C), S);
            DrawPhoto(g, new RectangleF(x0 + pw + gap, py, pw, ph),
                Color.FromArgb(255, 0x6F, 0xA8, 0xD8), S);

            // 两个小夹子，压在绳子与照片交界处
            float cw = Math.Max(2.0f, S * 0.115f);
            float ch = Math.Max(2.5f, S * 0.14f);
            using (var brush = new SolidBrush(Color.FromArgb(255, 0xE0, 0xC8, 0x9E)))
            using (var edge = new Pen(Color.FromArgb(255, 0x8A, 0x74, 0x55),
                       Math.Max(0.6f, S * 0.022f)))
            {
                var c1 = new RectangleF(x0 + pw * 0.30f, py - ch * 0.55f, cw, ch);
                var c2 = new RectangleF(x0 + pw + gap + pw * 0.30f, py - ch * 0.55f, cw, ch);
                g.FillRectangle(brush, c1);
                g.DrawRectangle(edge, c1.X, c1.Y, c1.Width, c1.Height);
                g.FillRectangle(brush, c2);
                g.DrawRectangle(edge, c2.X, c2.Y, c2.Width, c2.Height);
            }
        }

        /// <summary>一张照片：白边 + 彩色画面 + 一点高光。</summary>
        private static void DrawPhoto(Graphics g, RectangleF r, Color accent, float S)
        {
            float radius = Math.Max(1.0f, S * 0.05f);

            using (var path = RoundedRect(r, radius))
            {
                using (var fill = new SolidBrush(Color.White)) { g.FillPath(fill, path); }
                using (var pen = new Pen(Color.FromArgb(255, 0x7A, 0x63, 0x45),
                           Math.Max(0.7f, S * 0.028f)))
                {
                    g.DrawPath(pen, path);
                }
            }

            float inset = Math.Max(1.0f, S * 0.055f);
            var inner = new RectangleF(r.X + inset, r.Y + inset,
                r.Width - inset * 2, r.Height - inset * 2);
            if (inner.Width <= 0 || inner.Height <= 0) { return; }

            using (var brush = new SolidBrush(accent)) { g.FillRectangle(brush, inner); }

            if (S >= 24)
            {
                using (var brush = new SolidBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)))
                {
                    float d = inner.Width * 0.34f;
                    g.FillEllipse(brush, inner.X + inner.Width * 0.48f,
                        inner.Y + inner.Height * 0.12f, d, d);
                }
            }
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>把画好的图标打包成 .NET 能正确读取的 ICO。</summary>
    internal static class IcoWriter
    {
        /// <summary>
        /// 生成多尺寸 ICO。
        /// 用经典 BMP 帧（BITMAPINFOHEADER + BGRA + AND 掩码）而不是 PNG 帧：
        /// PNG 帧虽然体积小，但在部分运行环境里 new Icon(stream) 会读成噪点。
        /// </summary>
        public static byte[] Build(int[] sizes)
        {
            var frames = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++) { frames[i] = BmpFrame(sizes[i]); }

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int count = frames.Length;
                w.Write((ushort)0);      // reserved
                w.Write((ushort)1);      // type = icon
                w.Write((ushort)count);

                int dataOffset = 6 + count * 16;
                for (int i = 0; i < count; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((ushort)1);      // planes
                    w.Write((ushort)32);     // bpp
                    w.Write(frames[i].Length);
                    w.Write(dataOffset);
                    dataOffset += frames[i].Length;
                }
                for (int i = 0; i < count; i++) { w.Write(frames[i]); }
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>单帧：BITMAPINFOHEADER（高度两倍）+ BGRA 自下而上 + 1 位 AND 掩码。</summary>
        private static byte[] BmpFrame(int size)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);
                    IconDrawing.Draw(g, size);
                }

                int stride = size * 4;
                int maskStride = ((size + 31) / 32) * 4;
                var ms = new MemoryStream(40 + stride * size + maskStride * size);
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(40);
                    w.Write(size);
                    w.Write(size * 2);        // XOR 图 + AND 图
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(0);               // BI_RGB
                    w.Write(stride * size);
                    w.Write(0); w.Write(0);
                    w.Write(0); w.Write(0);

                    BitmapData bd = bmp.LockBits(new Rectangle(0, 0, size, size),
                        ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    var alpha = new byte[size * size];
                    try
                    {
                        var row = new byte[stride];
                        for (int y = size - 1; y >= 0; y--)    // DIB 自下而上
                        {
                            System.Runtime.InteropServices.Marshal.Copy(
                                new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row, 0, stride);
                            w.Write(row);
                            for (int x = 0; x < size; x++) { alpha[y * size + x] = row[x * 4 + 3]; }
                        }
                    }
                    finally { bmp.UnlockBits(bd); }

                    // AND 掩码：alpha < 128 视为透明
                    var mask = new byte[maskStride];
                    for (int y = size - 1; y >= 0; y--)
                    {
                        Array.Clear(mask, 0, mask.Length);
                        for (int x = 0; x < size; x++)
                        {
                            if (alpha[y * size + x] < 128) { mask[x / 8] |= (byte)(0x80 >> (x % 8)); }
                        }
                        w.Write(mask);
                    }
                }
                return ms.ToArray();
            }
        }
    }
}
