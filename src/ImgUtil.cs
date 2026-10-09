using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Liangshai
{
    /// <summary>图片转换工具：剪贴板 DIB / GDI+ / WPF 之间的搬运。</summary>
    internal static class ImgUtil
    {
        /// <summary>把剪贴板里的 "Bitmap" 格式字节（DIB，无文件头）补上 BITMAPFILEHEADER。</summary>
        public static byte[] DibToBmpFile(byte[] dib)
        {
            if (dib == null || dib.Length < 40) { return null; }

            int headerSize = BitConverter.ToInt32(dib, 0);
            if (headerSize < 12 || headerSize > dib.Length) { return null; }

            int bitCount = 0;
            int clrUsed = 0;
            int paletteEntries = 0;
            if (headerSize >= 40)
            {
                bitCount = BitConverter.ToInt16(dib, 14);
                clrUsed = BitConverter.ToInt32(dib, 32);
                if (bitCount <= 8)
                {
                    paletteEntries = clrUsed > 0 ? clrUsed : (1 << bitCount);
                }
            }

            int pixelOffset = 14 + headerSize + paletteEntries * 4;

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)'B');
                w.Write((byte)'M');
                w.Write(14 + dib.Length);
                w.Write((short)0);
                w.Write((short)0);
                w.Write(pixelOffset);
                w.Write(dib);
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>剪贴板 "Bitmap" 格式 → GDI+ 位图。</summary>
        public static Bitmap ClipboardDibToBitmap(byte[] dib)
        {
            byte[] bmp = DibToBmpFile(dib);
            if (bmp == null) { return null; }
            try
            {
                using (var ms = new MemoryStream(bmp))
                {
                    // 复制一份，避免 Bitmap 依赖 MemoryStream 的生命周期
                    return new Bitmap(ms);
                }
            }
            catch { return null; }
        }

        /// <summary>GDI+ 位图 → PNG 字节（同时保留 alpha 通道）。</summary>
        public static byte[] BitmapToPng(Bitmap bmp)
        {
            if (bmp == null) { return null; }
            try
            {
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
            catch { return null; }
        }

        /// <summary>PNG 字节 → WPF 图像源（已冻结，可跨线程使用）。</summary>
        public static BitmapSource BytesToImageSource(byte[] png)
        {
            if (png == null || png.Length == 0) { return null; }
            try
            {
                using (var ms = new MemoryStream(png))
                {
                    var decoder = new PngBitmapDecoder(ms,
                        BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0) { return null; }
                    BitmapSource src = decoder.Frames[0];
                    src.Freeze();
                    return src;
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// 抓剪贴板里的图片。优先 PNG 格式（保留透明与像素完美），
        /// 退回到 DIB("Bitmap")，再退回到 WPF 自带的转换。
        /// </summary>
        public static byte[] CaptureClipboardImagePng()
        {
            // 1) 剪贴板直接给了 PNG（最理想，像素完美）
            byte[] png = TryPngFormat();
            if (png != null) { return Sanitize(png, "PNG"); }

            // 2) DIB（系统 PrtScn / Win+Shift+S / 微信常用）
            png = TryDibFormat();
            if (png != null) { return Sanitize(png, "DIB"); }

            // 3) GDI+ 直接读剪贴板（覆盖面广，走的是系统自己的解码）
            png = TryGdiImage();
            if (png != null) { return Sanitize(png, "GDI+"); }

            // 4) 通过 WPF 拿（最后一次兜底）
            png = TryWpfFormat();
            return png == null ? null : Sanitize(png, "WPF");
        }

        /// <summary>
        /// 最后一道防线：剪贴板里的图常常带一个坏掉的 alpha 通道
        /// （全是 0）。这种图挂到绳子上会显示成一片黑，看起来就像"截图失败了"。
        /// 这里检测"几乎全透明"的情况，直接把 alpha 拉满。
        /// </summary>
        private static byte[] Sanitize(byte[] png, string from)
        {
            try
            {
                using (var ms = new MemoryStream(png))
                using (var bmp = new Bitmap(ms))
                {
                    if (!HasAlpha(bmp)) { return png; }

                    int stepX = Math.Max(1, bmp.Width / 40);
                    int stepY = Math.Max(1, bmp.Height / 40);
                    int transparent = 0, total = 0;
                    for (int y = 0; y < bmp.Height; y += stepY)
                    {
                        for (int x = 0; x < bmp.Width; x += stepX)
                        {
                            total++;
                            if (bmp.GetPixel(x, y).A < 16) { transparent++; }
                        }
                    }

                    // 95% 以上像素透明 → alpha 通道是坏的，强制不透明
                    if (total > 0 && transparent * 100 / total >= 95)
                    {
                        ClipWatcher.Diag("  [" + from + "] 检测到 alpha 全透明(" +
                            transparent + "/" + total + ")，强制转为不透明");
                        return ForceOpaque(bmp);
                    }
                }
            }
            catch (Exception ex) { ClipWatcher.Diag("  [" + from + "] 校验异常: " + ex.Message); }
            return png;
        }

        private static bool HasAlpha(Bitmap bmp)
        {
            PixelFormat f = bmp.PixelFormat;
            return f == PixelFormat.Format32bppArgb || f == PixelFormat.Format32bppPArgb ||
                   f == PixelFormat.Format64bppArgb || f == PixelFormat.Format64bppPArgb;
        }

        /// <summary>把整张图的 alpha 拉满，再把 R/B 通道换回正常顺序。</summary>
        private static byte[] ForceOpaque(Bitmap src)
        {
            using (var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
            {
                BitmapData sd = src.LockBits(new Rectangle(0, 0, src.Width, src.Height),
                    ImageLockMode.ReadOnly, src.PixelFormat);
                BitmapData dd = dst.LockBits(new Rectangle(0, 0, src.Width, src.Height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new byte[Math.Abs(sd.Stride)];
                    var outRow = new byte[Math.Abs(dd.Stride)];
                    for (int y = 0; y < src.Height; y++)
                    {
                        Marshal.Copy(new IntPtr(sd.Scan0.ToInt64() + (long)y * sd.Stride), row, 0, row.Length);
                        for (int x = 0; x < src.Width; x++)
                        {
                            int o = x * 4;
                            outRow[o] = row[o];
                            outRow[o + 1] = row[o + 1];
                            outRow[o + 2] = row[o + 2];
                            outRow[o + 3] = 255;
                        }
                        Marshal.Copy(outRow, 0, new IntPtr(dd.Scan0.ToInt64() + (long)y * dd.Stride),
                            outRow.Length);
                    }
                }
                finally
                {
                    src.UnlockBits(sd);
                    dst.UnlockBits(dd);
                }
                return BitmapToPng(dst);
            }
        }

        /// <summary>
        /// 用 GDI+ 的 Clipboard.GetImage 取图。
        /// 它走的是系统自带解码，对我们的手工 DIB 解析是很重要的互补。
        /// </summary>
        private static byte[] TryGdiImage()
        {
            try
            {
                if (!System.Windows.Forms.Clipboard.ContainsImage()) { return null; }
                using (System.Drawing.Image img = System.Windows.Forms.Clipboard.GetImage())
                {
                    if (img == null) { return null; }
                    using (var bmp = new Bitmap(img))
                    {
                        byte[] png = BitmapToPng(bmp);
                        if (png != null) { ClipWatcher.Diag("  兜底: GDI+ GetImage 成功"); }
                        return png;
                    }
                }
            }
            catch (Exception ex) { ClipWatcher.Diag("  GDI+ GetImage 异常: " + ex.Message); }
            return null;
        }

        private static byte[] TryPngFormat()
        {
            try
            {
                if (!System.Windows.Forms.Clipboard.ContainsData("PNG")) { return null; }

                object o = System.Windows.Forms.Clipboard.GetData("PNG");
                if (o == null) { return null; }

                // 有的程序直接给 Image/Bitmap 对象
                var img = o as System.Drawing.Image;
                if (img != null)
                {
                    using (img)
                    using (var bmp = new Bitmap(img))
                    {
                        return BitmapToPng(bmp);
                    }
                }

                // 有的是字节数组
                var arr = o as byte[];
                if (arr != null && arr.Length > 8) { return arr; }

                var ms = o as MemoryStream;
                if (ms != null)
                {
                    byte[] raw = ms.ToArray();
                    if (raw.Length > 8) { return raw; }
                }
            }
            catch (Exception ex) { ClipWatcher.Diag("  PNG 格式异常: " + ex.Message); }
            return null;
        }

        private static byte[] TryDibFormat()
        {
            foreach (string name in new[] { "DeviceIndependentBitmap", "DIB", "DIBV5" })
            {
                try
                {
                    if (!System.Windows.Forms.Clipboard.ContainsData(name)) { continue; }

                    object o = System.Windows.Forms.Clipboard.GetData(name);
                    if (o == null) { continue; }

                    // (a) 有的程序直接给 Bitmap 对象（旧代码漏了这种情况）
                    var direct = o as System.Drawing.Bitmap;
                    if (direct != null)
                    {
                        using (direct)
                        {
                            byte[] enc = BitmapToPng(direct);
                            if (enc != null)
                            {
                                ClipWatcher.Diag("  " + name + " 拿到 Bitmap 对象");
                                return enc;
                            }
                        }
                    }

                    // (b) 直接给 Image 对象
                    var directImg = o as System.Drawing.Image;
                    if (directImg != null && direct == null)
                    {
                        using (directImg)
                        using (var b = new Bitmap(directImg))
                        {
                            byte[] enc = BitmapToPng(b);
                            if (enc != null) { return enc; }
                        }
                    }

                    // (c) 裸 DIB 字节流
                    var ms = o as MemoryStream;
                    if (ms == null) { continue; }

                    byte[] dib = ms.ToArray();
                    LogDibHeader(name, dib);

                    byte[] png = DecodeDibToPng(dib);
                    if (png != null) { return png; }

                    // 自己解不了（RLE 压缩等）→ 回退到 GDI+ 的解析
                    ClipWatcher.Diag("  " + name + " 自解失败，回退 GDI+");
                }
                catch (Exception ex) { ClipWatcher.Diag("  " + name + " 异常: " + ex.Message); }
            }
            return null;
        }

        /// <summary>
        /// 把 DIB 头写进诊断日志。用户下次遇到异常图片时，
        /// 日志里就能直接看到是哪种格式，不用再靠猜。
        /// </summary>
        internal static void LogDibHeader(string formatName, byte[] dib)
        {
            try
            {
                if (dib == null || dib.Length < 40) { return; }
                int headerSize = BitConverter.ToInt32(dib, 0);
                int w = BitConverter.ToInt32(dib, 4);
                int h = BitConverter.ToInt32(dib, 8);
                int bpp = BitConverter.ToInt16(dib, 14);
                int comp = BitConverter.ToInt32(dib, 16);
                int sizeImg = BitConverter.ToInt32(dib, 20);
                ClipWatcher.Diag(string.Format(
                    "  DIB头[{0}]: size={1} w={2} h={3} bpp={4} comp={5} sizeImage={6} 总字节={7}",
                    formatName, headerSize, w, h, bpp, comp, sizeImg, dib.Length));
            }
            catch { }
        }

        // ============================================================
        //  DIB 解码
        //
        //  剪贴板里的 "DeviceIndependentBitmap" 是 BITMAPINFOHEADER + 像素，
        //  没有文件头。直接用 GDI+ 读会有两个坑：
        //   1) 32 位 DIB 的 alpha 通道常常全是 0（微信、部分截图工具的典型行为），
        //      当成预乘 alpha 解释就会得到一张纯黑图；
        //   2) biHeight 可能是负数（自上而下），照搬会上下颠倒。
        //  所以这里自己解析头、自己拷贝像素，32 位一律按不透明处理。
        // ============================================================
        internal static byte[] DecodeDibToPng(byte[] dib)
        {
            if (dib == null || dib.Length < 40) { return null; }

            int headerSize = BitConverter.ToInt32(dib, 0);
            if (headerSize < 12 || headerSize > dib.Length) { return null; }

            int width = BitConverter.ToInt32(dib, 4);
            int heightRaw = BitConverter.ToInt32(dib, 8);
            int bitCount = BitConverter.ToInt16(dib, 14);
            int compression = BitConverter.ToInt32(dib, 16);
            int clrUsed = BitConverter.ToInt32(dib, 32);

            bool topDown = heightRaw < 0;
            int height = Math.Abs(heightRaw);
            if (width <= 0 || height <= 0) { return null; }

            // 调色板（8 位及以下）
            int paletteEntries = 0;
            if (bitCount <= 8) { paletteEntries = clrUsed > 0 ? clrUsed : (1 << bitCount); }

            int offset = headerSize + paletteEntries * 4;
            if (offset >= dib.Length) { return null; }

            int srcStride = ((width * bitCount + 31) / 32) * 4;
            if (offset + (long)srcStride * height > dib.Length) { return null; }

            PixelFormat fmt;
            switch (bitCount)
            {
                case 32: fmt = PixelFormat.Format32bppRgb; break;   // 强制不透明，避开全 0 的 alpha
                case 24: fmt = PixelFormat.Format24bppRgb; break;
                case 8: fmt = PixelFormat.Format8bppIndexed; break;
                case 4: fmt = PixelFormat.Format4bppIndexed; break;
                case 1: fmt = PixelFormat.Format1bppIndexed; break;
                default: return null;
            }

            // 压缩值含义: 0=BI_RGB, 3=BI_BITFIELDS, 6=BI_ALPHABITFIELDS
            // 其他（RLE 等）不自己解，交回 GDI+ 兜底
            if (compression != 0 && compression != 3 && compression != 6) { return null; }

            // 带通道掩码的：只认标准的 BGRA 布局，其余交给 GDI+ 处理，
            // 免得把颜色通道解释错了反而画出一张怪图。
            if (compression == 3 || compression == 6)
            {
                // 掩码紧跟在头部之后（BITMAPINFOHEADER 也会带掩码，所以不能用 headerSize>=52 判断）
                int maskBase = headerSize >= 52 ? 40 : headerSize;
                if (maskBase + 12 > dib.Length) { return null; }

                uint maskR = BitConverter.ToUInt32(dib, maskBase);
                uint maskG = BitConverter.ToUInt32(dib, maskBase + 4);
                uint maskB = BitConverter.ToUInt32(dib, maskBase + 8);
                if (maskR != 0x00FF0000 || maskG != 0x0000FF00 || maskB != 0x000000FF)
                {
                    ClipWatcher.Diag(string.Format(
                        "  BI_BITFIELDS 非标准掩码 R=0x{0:X8} G=0x{1:X8} B=0x{2:X8}，交给 GDI+",
                        maskR, maskG, maskB));
                    return null;
                }

                // 标准掩码：颜色布局等同于 BGRA，只是还带个 alpha 掩码，忽略即可
                offset = maskBase + 12 + paletteEntries * 4;
                if (offset >= dib.Length) { return null; }
            }

            using (var bmp = new Bitmap(width, height, fmt))
            {
                if (bitCount <= 8) { ApplyPalette(bmp, dib, headerSize, paletteEntries); }

                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly, bmp.PixelFormat);
                try
                {
                    IntPtr dst = bd.Scan0;
                    for (int row = 0; row < height; row++)
                    {
                        // 自下而上的 DIB 要倒着读
                        int srcRow = topDown ? row : (height - 1 - row);
                        int srcOff = offset + srcRow * srcStride;
                        CopyRow(dib, srcOff, dst, row * bd.Stride, width, bitCount);
                    }
                }
                finally { bmp.UnlockBits(bd); }

                // 自检：解出来一片黑、但源数据并非全黑 → 说明解错了，换别的解码器
                if (LooksAllBlack(bmp) && SourceHasColor(dib, offset, srcStride, height, bitCount))
                {
                    ClipWatcher.Diag("  自检发现整张图变黑，放弃这次解码，改用其他途径");
                    return null;
                }

                return BitmapToPng(bmp);
            }
        }

        /// <summary>抽样检查位图是不是一片纯黑。</summary>
        private static bool LooksAllBlack(Bitmap bmp)
        {
            try
            {
                int stepX = Math.Max(1, bmp.Width / 24);
                int stepY = Math.Max(1, bmp.Height / 24);
                int dark = 0, total = 0;
                for (int y = 0; y < bmp.Height; y += stepY)
                {
                    for (int x = 0; x < bmp.Width; x += stepX)
                    {
                        Color c = bmp.GetPixel(x, y);
                        total++;
                        if (c.R < 12 && c.G < 12 && c.B < 12) { dark++; }
                    }
                }
                return total > 0 && dark == total;
            }
            catch { return false; }
        }

        /// <summary>抽样检查源像素里是否存在明显非黑的颜色。</summary>
        private static bool SourceHasColor(byte[] src, int offset, int stride, int height, int bitCount)
        {
            try
            {
                int bytesPerPixel = bitCount / 8;
                if (bytesPerPixel < 3) { return false; }   // 索引色不参与这个判断
                int rowStep = Math.Max(1, height / 12);
                for (int row = 0; row < height; row += rowStep)
                {
                    int rowOff = offset + row * stride;
                    for (int x = 0; x < stride - bytesPerPixel; x += bytesPerPixel * 7)
                    {
                        int p = rowOff + x;
                        if (p + 2 >= src.Length) { break; }
                        if (src[p] > 24 || src[p + 1] > 24 || src[p + 2] > 24) { return true; }
                    }
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>按位深把一行像素拷进目标位图；32 位顺手把 alpha 拉满。</summary>
        private static void CopyRow(byte[] src, int srcOff, IntPtr dstBase, int dstOff,
                                    int width, int bitCount)
        {
            if (bitCount == 32)
            {
                var row = new byte[width * 4];
                Buffer.BlockCopy(src, srcOff, row, 0, row.Length);
                for (int i = 3; i < row.Length; i += 4) { row[i] = 255; }  // BD_32 的 alpha 不可信
                Marshal.Copy(row, 0, new IntPtr(dstBase.ToInt64() + dstOff), row.Length);
            }
            else if (bitCount == 24)
            {
                var row = new byte[width * 3];
                Buffer.BlockCopy(src, srcOff, row, 0, row.Length);
                Marshal.Copy(row, 0, new IntPtr(dstBase.ToInt64() + dstOff), row.Length);
            }
            else
            {
                // 索引色：整行原样拷，格式一致
                int bytes = ((width * bitCount + 31) / 32) * 4;
                var row = new byte[bytes];
                Buffer.BlockCopy(src, srcOff, row, 0, bytes);
                Marshal.Copy(row, 0, new IntPtr(dstBase.ToInt64() + dstOff), bytes);
            }
        }

        /// <summary>把 DIB 里的调色板搬到位图上（索引色 DIB 需要）。</summary>
        private static void ApplyPalette(Bitmap bmp, byte[] dib, int headerSize, int entries)
        {
            try
            {
                ColorPalette pal = bmp.Palette;
                int max = Math.Min(entries, pal.Entries.Length);
                for (int i = 0; i < max; i++)
                {
                    int p = headerSize + i * 4;
                    if (p + 3 >= dib.Length) { break; }
                    // DIB 调色板是 BGRX 顺序
                    pal.Entries[i] = Color.FromArgb(255, dib[p + 2], dib[p + 1], dib[p]);
                }
                bmp.Palette = pal;
            }
            catch { }
        }

        private static byte[] TryWpfFormat()
        {
            try
            {
                if (!System.Windows.Clipboard.ContainsImage()) { return null; }

                BitmapSource src = System.Windows.Clipboard.GetImage();
                if (src == null) { return null; }

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(src));
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    byte[] raw = ms.ToArray();
                    if (raw.Length > 8) { return raw; }
                }
            }
            catch (Exception ex) { ClipWatcher.Diag("  WPF 取图异常: " + ex.GetType().Name + " " + ex.Message); }
            return null;
        }

        /// <summary>把 PNG 写回剪贴板：同时提供 Bitmap 与 PNG，兼容微信、Word、画图等目标。</summary>
        public static void PutPngOnClipboard(byte[] png)
        {
            if (png == null || png.Length == 0) { return; }

            Bitmap bmp = null;
            try
            {
                using (var ms = new MemoryStream(png)) { bmp = new Bitmap(ms); }
            }
            catch { bmp = null; }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var data = new System.Windows.Forms.DataObject();
                    if (bmp != null) { data.SetImage(bmp); }
                    data.SetData("PNG", new MemoryStream(png));
                    System.Windows.Forms.Clipboard.SetDataObject(data, true);
                    return;
                }
                catch
                {
                    System.Threading.Thread.Sleep(60);
                }
            }
        }

        /// <summary>把图片另存为文件。</summary>
        public static bool SavePngToFile(byte[] png, string path)
        {
            if (png == null || png.Length == 0) { return false; }
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); }
                File.WriteAllBytes(path, png);
                return true;
            }
            catch { return false; }
        }

        /// <summary>生成"截图_20250101_120000.png"这类不重名的文件名。</summary>
        public static string SuggestFileName()
        {
            return "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png";
        }

        /// <summary>在资源管理器里定位到一个文件。</summary>
        public static void RevealInExplorer(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
                else
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
                    }
                }
            }
            catch { }
        }
    }
}
