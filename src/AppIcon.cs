using System;
using System.Drawing;

namespace Liangshai
{
    /// <summary>
    /// 托盘图标。
    ///
    /// 图标全部用代码画（见 IconDrawing），并且自己写标准 ICO 字节流（见 IcoWriter）。
    /// 不用 Icon.FromHandle + Clone：那种做法对带透明通道的图标不可靠，
    /// 透明区域在深色任务栏上有时会变成黑块，看起来就像"图标不见了"。
    /// </summary>
    internal static class AppIcon
    {
        /// <summary>任务栏常用的几种尺寸，一次都给全，让系统自己挑最清楚的。</summary>
        private static readonly int[] Sizes = new int[] { 16, 20, 24, 32, 48, 64 };

        private static Icon _cached;

        public static Icon Create()
        {
            if (_cached != null) { return _cached; }
            try
            {
                byte[] ico = IcoWriter.Build(Sizes);
                _cached = new Icon(new System.IO.MemoryStream(ico));
                return _cached;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }
    }
}
