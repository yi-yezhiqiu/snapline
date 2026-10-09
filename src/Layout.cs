using System;

namespace Liangshai
{
    /// <summary>
    /// 布局常量与几何计算。
    /// 数值与 macOS 原版 Tendedero 的 Layout 一一对应，改动前请先对照原版。
    /// </summary>
    internal static class Layout
    {
        /// <summary>整条绳子占据的窗口高度。</summary>
        public const double PanelHeight = 210;

        /// <summary>绳子在窗口内的 Y 位置。</summary>
        public const double RopeTop = 10;

        /// <summary>相邻两张照片的中心间距。</summary>
        public const double Spacing = 174;

        /// <summary>卡片宽度。</summary>
        public const double CardWidth = 150;

        /// <summary>照片在图钉顶端之下多少像素开始（26 是图钉总高，12 是叠压量）。</summary>
        public const double CardOffsetBelowTop = 26 - 12;

        /// <summary>卡片玻璃边框的圆角与外边距。</summary>
        public const double FrameRadius = 16;
        public const double FrameInset = 4;

        /// <summary>照片显示区域的最大尺寸。</summary>
        public const double PhotoMaxW = CardWidth - 14;
        public const double PhotoMaxH = 104;

        /// <summary>照片在图钉范围内的垂直偏移（14 - 12）。</summary>
        public const double PhotoTopWithinPeg = CardOffsetBelowTop - (FrameInset - 12);

        /// <summary>绳子下垂量：最多 30，或宽度的 1.8%。</summary>
        public static double Sag(double width)
        {
            return Math.Min(30, width * 0.018);
        }

        /// <summary>绳子的抛物线：两端在 RopeTop，中间下垂 2×sag 的控制点。</summary>
        public static double RopeY(double x, double width)
        {
            if (width <= 0) { return RopeTop; }
            double f = x / width;
            return RopeTop + 4 * Sag(width) * f * (1 - f);
        }

        /// <summary>第 index 张卡片的中心 X：整组居中、等间距。</summary>
        public static double X(int index, int count, double width)
        {
            double total = Math.Max(count - 1, 0) * Spacing;
            return width / 2 - total / 2 + index * Spacing;
        }

        /// <summary>按可用宽度决定最多挂几张（原版：宽度减 200 再除以间距，限制在 3~12）。</summary>
        public static int Capacity(double width)
        {
            double usable = width - 200;
            int n = (int)(usable / Spacing);
            return Math.Max(3, Math.Min(12, n));
        }

        /// <summary>照片按比例缩放后的显示尺寸。</summary>
        public static void PhotoSize(double srcW, double srcH, out double w, out double h)
        {
            if (srcW <= 0 || srcH <= 0)
            {
                w = PhotoMaxW;
                h = PhotoMaxH;
                return;
            }
            double scale = Math.Min(PhotoMaxW / srcW, PhotoMaxH / srcH);
            w = srcW * scale;
            h = srcH * scale;
        }

        /// <summary>卡片整体尺寸 = 照片 + 两侧玻璃内边距。</summary>
        public static void CardSize(double srcW, double srcH, out double w, out double h)
        {
            double pw, ph;
            PhotoSize(srcW, srcH, out pw, out ph);
            w = pw + FrameInset * 2;
            h = ph + FrameInset * 2;
        }
    }
}
