using System;
using System.Text;
using System.Windows.Threading;

namespace Liangshai
{
    /// <summary>
    /// 出问题时用的诊断工具：枚举顶层窗口、检查自己窗口的样式与位置。
    /// 只在命令行带 /diag 时启用，平时完全不参与运行。
    /// </summary>
    internal static class Diagnoser
    {
        public static void Schedule()
        {
            ScheduleAt(1500, "T+1.5s");
            ScheduleAt(6000, "T+6.0s");
            if (App.Instance != null) { InjectTestShots(); }
            App.Trace("[diag] 已安排窗口诊断");
            DumpIcon();
        }

        /// <summary>
        /// 把程序实际使用的托盘图标 dump 成 PNG。
        /// 任务栏图标出问题时，这一张图就能说明是"图标没画对"还是"画对了但没显示"。
        /// </summary>
        private static void DumpIcon()
        {
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "liangshai-icon");
                System.IO.Directory.CreateDirectory(dir);

                using (System.Drawing.Icon icon = AppIcon.Create())
                {
                    foreach (int size in new int[] { 16, 20, 24, 32, 48 })
                    {
                        using (var bmp = new System.Drawing.Bitmap(size, size,
                            System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                        {
                            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                            {
                                g.Clear(System.Drawing.Color.Transparent);
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.DrawIcon(icon, new System.Drawing.Rectangle(0, 0, size, size));
                            }
                            bmp.Save(System.IO.Path.Combine(dir, "icon-" + size + ".png"),
                                System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    App.Trace("[diag] 托盘图标已 dump 到 " + dir + "  尺寸=" + icon.Size);
                }
            }
            catch (Exception ex) { App.Trace("[diag] dump 图标失败: " + ex.Message); }
        }

        /// <summary>诊断模式：造几张彩色测试图挂上绳子，检查多图排版。</summary>
        private static void InjectTestShots()
        {
            try
            {
                App app = App.Instance;
                if (app == null) { return; }

                var colors = new[]
                {
                    System.Drawing.Color.FromArgb(255, 220, 80, 80),
                    System.Drawing.Color.FromArgb(255, 80, 180, 120),
                    System.Drawing.Color.FromArgb(255, 90, 120, 220),
                    System.Drawing.Color.FromArgb(255, 230, 170, 60),
                    System.Drawing.Color.FromArgb(255, 160, 100, 200)
                };
                var names = new[] { "ONE", "TWO", "THREE", "FOUR", "FIVE" };

                for (int i = 0; i < colors.Length; i++)
                {
                    using (var bmp = new System.Drawing.Bitmap(480, 270))
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.Clear(colors[i]);
                        using (var f = new System.Drawing.Font("Microsoft YaHei UI", 44,
                            System.Drawing.FontStyle.Bold))
                        {
                            g.DrawString(names[i], f, System.Drawing.Brushes.White, 30, 90);
                        }
                        using (var ms = new System.IO.MemoryStream())
                        {
                            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            app.InjectShotForDiagnostics(ms.ToArray(), 480, 270);
                        }
                    }
                }
                App.Trace("[diag] 已注入 5 张测试图");
            }
            catch (Exception ex) { App.Trace("[diag] 注入测试图失败: " + ex.Message); }
        }

        private static void ScheduleAt(int ms, string tag)
        {
            var t = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(ms)
            };
            t.Tick += (s, e) =>
            {
                t.Stop();
                App.Trace("[diag] ----- " + tag + " -----");
                Run();
            };
            t.Start();
        }

        private static void Run()
        {
            try { DumpOwnWindow(); }
            catch (Exception ex) { App.Trace("[diag] dump 自身失败: " + ex.Message); }

            try
            {
                EnumWindows((hwnd, lparam) =>
                {
                    try
                    {
                        Win32.RECT r;
                        Win32.GetWindowRect(hwnd, out r);
                        if (r.Top > 5 || r.Width < 200 || r.Bottom < 20) { return true; }

                        var sb = new StringBuilder(256);
                        Win32.GetClassName(hwnd, sb, sb.Capacity);
                        App.Trace(string.Format(
                            "[diag] hwnd=0x{0:X} cls={1} style=0x{2:X8} ex=0x{3:X8} rect=({4},{5})-({6},{7}) visible={8}",
                            hwnd.ToInt64(), sb, Win32.GetWindowLong(hwnd, -16),
                            Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE),
                            r.Left, r.Top, r.Right, r.Bottom, IsWindowVisible(hwnd)));
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { App.Trace("[diag] 枚举失败: " + ex.Message); }
        }

        private static void DumpOwnWindow()
        {
            RopeWindow rope = App.Instance != null ? App.Instance.Rope : null;
            if (rope == null) { App.Trace("[diag] Rope 为 null"); return; }

            IntPtr h = new System.Windows.Interop.WindowInteropHelper(rope).Handle;
            var sb = new StringBuilder(256);
            Win32.GetClassName(h, sb, sb.Capacity);

            App.Trace("[diag] 自己 hwnd=0x" + h.ToInt64().ToString("X") + " cls=" + sb);
            App.Trace("[diag] 自己 style=0x" + Win32.GetWindowLong(h, -16).ToString("X8") +
                      " exstyle=0x" + Win32.GetWindowLong(h, Win32.GWL_EXSTYLE).ToString("X8"));
            App.Trace(string.Format(
                "[diag] 自己 IsVisible={0} Left={1} Top={2} W={3} H={4} Opacity={5} Topmost={6} WindowState={7}",
                rope.IsVisible, rope.Left, rope.Top, rope.Width, rope.Height,
                rope.Opacity, rope.Topmost, rope.WindowState));
            App.Trace("[diag] 自己 IsShown=" + rope.IsShown + " IsLoaded=" + rope.IsLoaded);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc proc, IntPtr lparam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        private delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);
    }
}
