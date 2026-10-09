using System;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace Liangshai
{
    public sealed class ImageCapturedEventArgs : EventArgs
    {
        public byte[] Png;
        public DateTime Time;
        public int PixelWidth;
        public int PixelHeight;
    }

    /// <summary>
    /// 剪贴板图片监听器。
    /// 微信截图 (Alt+A) / PrtScn / Win+Shift+S 最终都会把图放进剪贴板，
    /// 这里通过 AddClipboardFormatListener 收到系统通知，再把图片取走。
    /// </summary>
    internal sealed class ClipWatcher : NativeWindow, IDisposable
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int WM_DESTROY = 0x0002;

        private System.Threading.Timer _debounce;
        private readonly object _gate = new object();
        private bool _busy;
        private byte[] _lastHash;

        /// <summary>Png 为 null 表示剪贴板有了新内容但不是图片。</summary>
        public event EventHandler<ImageCapturedEventArgs> ImageCaptured;

        /// <summary>为 true 时忽略剪贴板变化（我们自己写回剪贴板时用）。</summary>
        public volatile bool Suppress;

        public bool IsListening { get; private set; }

        public ClipWatcher()
        {
            var cp = new CreateParams();
            cp.Caption = "Liangshai.ClipWatcher";
            cp.Parent = new IntPtr(-3); // HWND_MESSAGE：只收消息的消息窗口
            CreateHandle(cp);
        }

        /// <summary>开始监听。返回是否成功。</summary>
        public bool Start()
        {
            if (IsListening) { return true; }
            try
            {
                bool ok = Win32.AddClipboardFormatListener(Handle);
                if (ok) { IsListening = true; }
                return ok;
            }
            catch { return false; }
        }

        /// <summary>暂停监听（托盘"暂停接管"）。</summary>
        public void Stop()
        {
            if (!IsListening) { return; }
            try { Win32.RemoveClipboardFormatListener(Handle); }
            catch { }
            IsListening = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                OnClipboardChanged();
            }
            else if (m.Msg == WM_DESTROY)
            {
                Stop();
            }
            base.WndProc(ref m);
        }

        private void OnClipboardChanged()
        {
            if (Suppress) { return; }

            // 剪贴板常常被连续更新好几次（延迟渲染），做 150ms 防抖
            lock (_gate)
            {
                if (_debounce == null)
                {
                    _debounce = new System.Threading.Timer(DebounceElapsed, null,
                        Timeout.Infinite, Timeout.Infinite);
                }
                _debounce.Change(150, Timeout.Infinite);
            }
        }

        private void DebounceElapsed(object state)
        {
            lock (_gate)
            {
                if (_busy) { return; }
                _busy = true;
            }

            // 关键：剪贴板读取是 OLE 调用，必须在 STA 线程上做。
            // 定时器回调跑在 MTA 线程池线程上，直接读会抛 ThreadStateException，
            // 所以这里把活交给 UI 线程（WPF 主线程就是 STA）。
            try
            {
                if (Suppress) { return; }

                var dispatcher = System.Windows.Application.Current != null
                    ? System.Windows.Application.Current.Dispatcher
                    : null;

                if (dispatcher == null)
                {
                    Diag("没有 Dispatcher，无法继续");
                    return;
                }

                if (dispatcher.CheckAccess())
                {
                    ReadClipboard();
                }
                else
                {
                    dispatcher.BeginInvoke(new Action(ReadClipboard));
                }
            }
            catch (Exception ex)
            {
                Diag("派发到 UI 线程失败: " + ex.Message);
                lock (_gate) { _busy = false; }
            }
        }

        /// <summary>在 UI(STA) 线程上真正读取剪贴板并通知上层。</summary>
        private void ReadClipboard()
        {
            ReadClipboardAttempt(0);
        }

        /// <summary>剪贴板可能被别的程序占着，用 DispatcherTimer 重试，避免卡住 UI 线程。</summary>
        private void ReadClipboardAttempt(int attempt)
        {
            byte[] png = null;
            try
            {
                if (Suppress) { lock (_gate) { _busy = false; } return; }
                png = ImgUtil.CaptureClipboardImagePng();
            }
            catch (Exception ex)
            {
                Diag("取图异常: " + ex.GetType().Name + " " + ex.Message);
                png = null;
            }

            if (png == null)
            {
                if (attempt < 5)
                {
                    var t = new System.Windows.Threading.DispatcherTimer(
                        System.Windows.Threading.DispatcherPriority.Background)
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    t.Tick += (s, e) =>
                    {
                        t.Stop();
                        ReadClipboardAttempt(attempt + 1);
                    };
                    t.Start();
                    return;
                }

                Diag("剪贴板有新内容但不是图片，忽略");
                lock (_gate) { _busy = false; }
                return;
            }

            try
            {
                Diag("抓到图片 " + png.Length + " 字节");

                // 诊断模式：把抓到的图存一份出来，方便外部工具核对颜色
                if (App.DiagMode)
                {
                    try
                    {
                        System.IO.File.WriteAllBytes(
                            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "liangshai-last-capture.png"),
                            png);
                        Diag("已 dump 到 %TEMP%\\liangshai-last-capture.png");
                    }
                    catch { }
                }

                // 同一张图重复进剪贴板（很多程序会发多次通知）只处理一次
                byte[] hash = Sha1(png);
                if (_lastHash != null && ByteEquals(_lastHash, hash))
                {
                    Diag("与上一张相同，跳过");
                    return;
                }
                _lastHash = hash;

                int w = 0, h = 0;
                try
                {
                    using (var ms = new System.IO.MemoryStream(png))
                    using (var bmp = new System.Drawing.Bitmap(ms))
                    {
                        w = bmp.Width;
                        h = bmp.Height;
                    }
                }
                catch { }

                EventHandler<ImageCapturedEventArgs> handler = ImageCaptured;
                if (handler != null)
                {
                    handler(this, new ImageCapturedEventArgs
                    {
                        Png = png,
                        Time = DateTime.Now,
                        PixelWidth = w,
                        PixelHeight = h
                    });
                }
            }
            catch (Exception ex)
            {
                Diag("处理图片失败: " + ex.Message);
            }
            finally
            {
                lock (_gate) { _busy = false; }
            }
        }

        private static byte[] Sha1(byte[] data)
        {
            using (var sha = SHA1.Create()) { return sha.ComputeHash(data); }
        }

        /// <summary>诊断日志（写临时目录，用于排查抓不到图的问题）。</summary>
        internal static void Diag(string message)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "liangshai-trace.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  [clip] " + message + Environment.NewLine,
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private static bool ByteEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) { return false; }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) { return false; }
            }
            return true;
        }

        /// <summary>让下一次剪贴板变化被当作全新图片处理（例如托盘"清空绳子"之后）。</summary>
        public void ForgetLastHash()
        {
            _lastHash = null;
        }

        public void Dispose()
        {
            Stop();
            lock (_gate)
            {
                if (_debounce != null)
                {
                    _debounce.Dispose();
                    _debounce = null;
                }
            }
            try { if (Handle != IntPtr.Zero) { DestroyHandle(); } }
            catch { }
        }
    }
}
