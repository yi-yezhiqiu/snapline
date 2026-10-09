using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Liangshai
{
    /// <summary>
    /// 屏幕顶端的晾衣绳。
    ///
    /// 关键设计（对照 macOS 原版 LinePanel + AppDelegate）：
    /// - 面板本身完全透明，只有一根细线；没有背景色、没有标题栏、没有按钮
    /// - 平时藏到屏幕上边缘之上，像自动隐藏的 Dock 一样滑出来
    /// - 光标停在顶端菜单栏区域并**持续 0.25 秒**才滑出（不是一碰就出）
    /// - 光标离开「面板底边到屏幕上边缘」这个区域**0.5 秒**后才收回
    /// - 除照片以外的区域点击全部穿透到下层窗口
    /// </summary>
    internal sealed class RopeWindow : Window
    {
        /// <summary>光标停在顶端多久才滑出（原版 revealDelay）。</summary>
        private const double RevealDelayMs = 250;

        /// <summary>光标离开后多久收回（原版 retractDelay）。</summary>
        private const double RetractDelayMs = 500;

        /// <summary>滑出 / 收回的动画时长。</summary>
        private const double SlideInMs = 340;
        private const double SlideOutMs = 220;

        private readonly Store _store;
        private readonly Config _cfg;

        private readonly Canvas _board = new Canvas();
        private readonly Path _ropeShadow = new Path();
        private readonly Path _rope = new Path();
        private readonly Path _ropeHighlight = new Path();
        private Border _hint;

        private readonly Dictionary<string, PeggedCard> _cards =
            new Dictionary<string, PeggedCard>();

        private Win32.RECT _monitor;
        private double _visibleTop;
        private double _screenLeft;
        private double _screenRight;
        private double _screenTop;
        private double _screenBottom;

        private DispatcherTimer _tick;        // 30Hz，对应原版 mouseTimer
        private DispatcherTimer _slideTimer;
        private DispatcherTimer _gustTimer;

        private bool _isPresent;              // 窗口是否已显示（可能仍藏在屏幕外）
        private bool _isRevealed;             // 绳子是否已滑入视野
        private bool _pinned;                 // 主动呼出后保持不收回
        private DateTime _peekUntil = DateTime.MinValue;
        private DateTime _hotZoneSince = DateTime.MinValue;
        private DateTime _awaySince = DateTime.MinValue;
        private bool _hasHotZone;
        private bool _hasAway;
        private bool _dragging;

        public bool IsShown { get { return _isRevealed; } }

        public RopeWindow(Store store, Config cfg)
        {
            _store = store;
            _cfg = cfg;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;                 // 需要真正的透明与点击穿透
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Focusable = false;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            Title = "晾晒工具";
            Height = Layout.PanelHeight;
            Width = 1920;

            Content = BuildVisualTree();

            _store.Changed += (s, e) => Dispatcher.BeginInvoke(
                new Action(() => { SyncCards(); }));

            Loaded += OnLoaded;
            SourceInitialized += OnSourceInitialized;

            // 30Hz 轮询，和原版一致
            _tick = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(1000.0 / 30.0)
            };
            _tick.Tick += (s, e) => Tick();

            // 偶尔来一阵风：每 7~16 秒让整条绳子轻轻晃一下
            _gustTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Rand(7, 16)) };
            _gustTimer.Tick += (s, e) =>
            {
                _gustTimer.Interval = TimeSpan.FromSeconds(Rand(7, 16));
                if (_cards.Count > 0 && !_dragging) { Breeze(); }
            };
        }

        private static readonly Random Rng = new Random();
        private static double Rand(double a, double b) { return a + Rng.NextDouble() * (b - a); }

        // ============================================================
        //  视觉结构：一根线，仅此而已
        // ============================================================
        private FrameworkElement BuildVisualTree()
        {
            var root = new Grid { Background = Brushes.Transparent };
            _board.Background = null;   // Canvas 本身不拦鼠标，只让卡片接

            _ropeShadow.StrokeThickness = 1.4;
            _ropeShadow.Stroke = new SolidColorBrush(Color.FromArgb(0x38, 0x00, 0x00, 0x00));
            _ropeShadow.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 1.2 };

            _rope.StrokeThickness = 1.2;
            _rope.Stroke = new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C));

            _ropeHighlight.StrokeThickness = 0.4;
            _ropeHighlight.Stroke = new SolidColorBrush(Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF));

            // 线本身不接收鼠标
            _ropeShadow.IsHitTestVisible = false;
            _rope.IsHitTestVisible = false;
            _ropeHighlight.IsHitTestVisible = false;

            root.Children.Add(_ropeShadow);
            root.Children.Add(_rope);
            root.Children.Add(_ropeHighlight);

            // 空状态提示：挂在绳子中间略下方
            _hint = new Border
            {
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0xF2, 0xF2, 0xF5)),
                BorderThickness = new Thickness(0.75),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = "截一张图，它就会挂在这里",
                    FontSize = 12,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x6B))
                }
            };
            root.Children.Add(_hint);

            root.Children.Add(_board);
            return root;
        }

        /// <summary>把线的几何画成抛物线，并让两端渐隐（像是从屏幕外伸过来的）。</summary>
        private void UpdateRopeGeometry(double width)
        {
            var geo = new PathGeometry();
            var fig = new PathFigure
            {
                StartPoint = new Point(-20, Layout.RopeTop),
                IsClosed = false,
                IsFilled = false
            };
            fig.Segments.Add(new QuadraticBezierSegment(
                new Point(width / 2, Layout.RopeTop + 2 * Layout.Sag(width)),
                new Point(width + 20, Layout.RopeTop),
                true));
            geo.Figures.Add(fig);

            _rope.Data = geo;
            _ropeHighlight.Data = geo;
            _ropeShadow.Data = geo;

            // 两端淡出：中间实、两边透明
            var mask = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            mask.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.00));
            mask.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.08));
            mask.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.92));
            mask.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.00));

            _ropeShadow.OpacityMask = mask;
            _rope.OpacityMask = mask;
            _ropeHighlight.OpacityMask = mask;

            // 空提示挂在线的中间下方
            double hintY = Layout.RopeY(width / 2, width) + 34 - 11;
            Canvas.SetLeft(_hint, width / 2);
            _hint.RenderTransform = new TranslateTransform(-0.5, hintY);
            _hint.HorizontalAlignment = HorizontalAlignment.Left;
            _hint.VerticalAlignment = VerticalAlignment.Top;
        }

        // ============================================================
        //  窗口初始化与定位
        // ============================================================
        private void OnSourceInitialized(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            Win32.MakeToolWindow(hwnd);
            ForceTopMost();
            UpdatePlacement();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdatePlacement();
            SyncCards();
            _tick.Start();
            _gustTimer.Start();
        }

        private void UpdatePlacement()
        {
            // 锚在工作区顶部（任务栏之下），和原版锚在菜单栏之下一致。
            // 锚物理屏幕顶部会让绳子藏在任务栏后面。
            System.Windows.Forms.Screen scr = System.Windows.Forms.Screen.FromPoint(
                System.Windows.Forms.Cursor.Position);
            if (scr == null) { return; }

            System.Drawing.Rectangle wa = scr.WorkingArea;
            System.Drawing.Rectangle b = scr.Bounds;
            if (wa.Width < 200 || wa.Height < 200) { return; }

            _screenLeft = b.Left;
            _screenRight = b.Right;
            _screenTop = b.Top;
            _screenBottom = b.Bottom;
            _visibleTop = wa.Top;                       // 窗口完全滑出时的 Top

            Left = b.Left;
            Width = b.Width;
            Height = Layout.PanelHeight;
            UpdateRopeGeometry(b.Width);
            PresentCards();
        }

        private void ForceTopMost()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) { return; }
                Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                    Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            }
            catch { }
        }

        // ============================================================
        //  显示 / 隐藏：滑出与收回
        // ============================================================
        private void Present()
        {
            if (_isPresent) { return; }
            _isPresent = true;
            if (!IsVisible) { Show(); }
            ForceTopMost();
            UpdatePlacement();
        }

        private void TuckAway()
        {
            if (!_isPresent) { return; }
            _isPresent = false;
            SetRevealed(false);
        }

        /// <summary>滑出（或收回）。直接滑动窗口自身的 Top，内容始终画在同一位置。</summary>
        private void SetRevealed(bool on)
        {
            if (on == _isRevealed) { return; }
            _isRevealed = on;

            if (!on)
            {
                _pinned = false;
                _peekUntil = DateTime.MinValue;
            }

            SlideTo(on ? _visibleTop : _visibleTop - Height, on ? SlideInMs : SlideOutMs);
        }

        /// <summary>窗口隐藏后彻底移出屏幕，避免压住鼠标。</summary>
        private void SlideTo(double targetTop, double ms)
        {
            StopSlide();

            double from = Top;
            if (Math.Abs(from - targetTop) < 0.5)
            {
                Top = targetTop;
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool opening = targetTop > from;

            _slideTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(15)
            };
            _slideTimer.Tick += (s, e) =>
            {
                double t = sw.Elapsed.TotalMilliseconds / ms;
                if (t >= 1.0)
                {
                    StopSlide();
                    Top = targetTop;
                    return;
                }
                // 滑出用缓出，收回用缓入
                double eased = opening ? 1 - Math.Pow(1 - t, 3) : t * t;
                Top = from + (targetTop - from) * eased;
            };
            _slideTimer.Start();
        }

        private void StopSlide()
        {
            if (_slideTimer != null)
            {
                _slideTimer.Stop();
                _slideTimer = null;
            }
        }

        // ============================================================
        //  30Hz 光标检测（对应原版 tick）
        // ============================================================
        private void Tick()
        {
            if (App.Instance == null) { return; }

            Win32.POINT p;
            if (!Win32.GetCursorPos(out p)) { return; }
            DateTime now = DateTime.Now;

            // 换了显示器就把绳子挪过去
            IntPtr mon = Win32.MonitorFromPoint(p, Win32.MONITOR_DEFAULTTONEAREST);
            Win32.RECT rect = Win32.GetMonitorRect(mon);
            if (rect.Width >= 200 && Math.Abs(_screenLeft - rect.Left) > 0.5)
            {
                UpdatePlacement();
            }

            bool blocked = App.Instance.FullScreenActive && _cfg.HideOnFullScreen;

            if (!_isRevealed)
            {
                // 注意：这里绝不能因为"窗口还没显示"就 return，
                // 否则第一次呼出永远不会发生（窗口藏在屏幕外时本身就是"未显示"状态）。
                if (!_isPresent) { Present(); }

                // 光标停在顶端感应带里，持续 RevealDelayMs 才滑出
                double band = TopBandHeight();
                bool xIn = p.X >= _screenLeft && p.X < _screenRight;
                bool yIn = (p.Y - _screenTop) >= 0 && (p.Y - _screenTop) <= band;
                bool inTopBand = xIn && yIn;
                if (inTopBand && !blocked)
                {
                    if (!_hasHotZone)
                    {
                        _hasHotZone = true;
                        _hotZoneSince = now;
                    }
                    else if ((now - _hotZoneSince).TotalMilliseconds >= RevealDelayMs)
                    {
                        _hasHotZone = false;
                        Reveal(false);
                    }
                }
                else
                {
                    _hasHotZone = false;
                }
                return;
            }

            // 已滑出：判断该不该收回。
            // 感应区从面板底边一路延伸到**物理屏幕顶部**（含任务栏），
            // 这样光标往上走永远不会把它收回去。
            bool inside = p.X >= _screenLeft && p.X < _screenRight &&
                          p.Y >= _screenTop && p.Y <= _visibleTop + Layout.PanelHeight;

            if (inside && _pinned)
            {
                // 光标真正进来过一次，就解除"主动呼出"的钉住状态
                _pinned = false;
            }

            bool busy = _pinned || _dragging || AnyCardPressed() || now < _peekUntil;
            if (inside || busy)
            {
                _hasAway = false;
                return;
            }

            if (!_hasAway)
            {
                _hasAway = true;
                _awaySince = now;
                return;
            }

            if ((now - _awaySince).TotalMilliseconds >= RetractDelayMs)
            {
                _hasAway = false;
                SetRevealed(false);
            }
        }

        /// <summary>
        /// 顶端感应带高度（逻辑像素）。
        /// 对应原版的"菜单栏区域"：光标停在这一条里就算顶到边缘了。
        /// Windows 的任务栏可能在侧边或底部，所以这里用固定高度，不去推算。
        /// </summary>
        private const double TopBandLogical = 24;

        /// <summary>顶端感应带的高度（逻辑像素）。</summary>
        private double TopBandHeight()
        {
            return TopBandLogical;
        }

        private bool AnyCardPressed()
        {
            foreach (PeggedCard c in _cards.Values)
            {
                if (c.IsPressed) { return true; }
            }
            return false;
        }

        // ============================================================
        //  对外的显示入口
        // ============================================================
        /// <summary>鼠标碰到顶端时滑出（不钉住，光标离开就收回）。</summary>
        public void Reveal(bool pinned)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _hasAway = false;
                Present();
                if (pinned) { _pinned = true; }
                SetRevealed(true);
            }));
        }

        /// <summary>托盘 / 热键呼出：钉住，直到光标进来又离开。</summary>
        public void ShowRope()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _hasAway = false;
                Present();
                _pinned = true;
                SetRevealed(true);
            }));
        }

        /// <summary>截图后自动滑下来看一眼：停留 ms 毫秒后自动收回。</summary>
        public void Peek(int ms)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ms > 0) { _peekUntil = DateTime.Now.AddMilliseconds(ms); }
                _hasAway = false;
                Present();
                SetRevealed(true);
            }));
        }

        public void HideRope()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _pinned = false;
                _peekUntil = DateTime.MinValue;
                SetRevealed(false);
            }));
        }

        // ============================================================
        //  卡片排版（对照原版 LayoutView）
        // ============================================================
        private void SyncCards()
        {
            var shots = _store.Items.ToList();

            // 移除已经不在数据源里的卡片
            var alive = new HashSet<string>(shots.Select(x => x.Id));
            foreach (string id in _cards.Keys.ToList())
            {
                if (!alive.Contains(id))
                {
                    _board.Children.Remove(_cards[id]);
                    _cards.Remove(id);
                }
            }

            // 补上新卡片
            foreach (Shot shot in shots)
            {
                if (_cards.ContainsKey(shot.Id)) { continue; }

                var card = new PeggedCard(shot);
                PeggedCard captured = card;
                card.Clicked += (s, e) =>
                {
                    LastClickInfo = "单击 " + captured.Data.TimeText;
                    CopyCard(captured);
                };
                card.DoubleClicked += (s, e) =>
                {
                    LastClickInfo = "双击 " + captured.Data.TimeText;
                    OpenPreview(captured.Data);
                };
                card.DiscardRequested += (s, e) =>
                {
                    LastClickInfo = "丢弃 " + captured.Data.TimeText;
                    _store.Remove(captured.Data);
                };
                card.PressChanged += (s, e) =>
                {
                    if (captured.IsPressed) { /* 保持显示 */ }
                };
                card.DragStarted += (s, e) => { _dragging = true; };
                card.DragEnded += (s, e) => { _dragging = false; };

                _cards[shot.Id] = card;
                _board.Children.Add(card);
            }

            _hint.Visibility = shots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PresentCards();
        }

        /// <summary>按原版公式摆放每一张：整组居中、等间距、挂在抛物线上。</summary>
        private void PresentCards()
        {
            var shots = _store.Items.ToList();
            int count = shots.Count;
            if (count == 0) { return; }

            double width = Width;
            if (width < 200) { width = _monitor.Width > 200 ? _monitor.Width : 1920; }

            for (int i = 0; i < count; i++)
            {
                PeggedCard card;
                if (!_cards.TryGetValue(shots[i].Id, out card)) { continue; }

                double cx = Layout.X(i, count, width);
                double ropeY = Layout.RopeY(cx, width);
                // 图钉从 Y 方向探出 pinAbove=9.5，所以卡片整体上移这么多
                double cardTopInWindow = ropeY - 9.5;
                // 卡片元素在窗口坐标系里，窗口本身在屏幕的 _visibleTop
                Canvas.SetLeft(card, cx - Layout.CardWidth / 2);
                Canvas.SetTop(card, cardTopInWindow);
            }
        }

        private void Breeze()
        {
            foreach (PeggedCard c in _cards.Values)
            {
                double delay = Rand(0, 350);
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
                PeggedCard target = c;
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    target.Nudge(Rand(1.6, 3.4));
                };
                t.Start();
            }
        }

        // ============================================================
        //  单张卡片的行为
        // ============================================================
        private void CopyCard(PeggedCard card)
        {
            if (card == null || card.Data == null) { return; }

            LastCopyAt = DateTime.Now;
            LastCopySize = card.Data.PixelWidth + "x" + card.Data.PixelHeight;

            _clipSuppress = true;
            try
            {
                ImgUtil.PutPngOnClipboard(card.Data.Png);
            }
            finally
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                t.Tick += (s, e) => { t.Stop(); _clipSuppress = false; };
                t.Start();
            }
            card.ShowCopied();
            _hasAway = false;
        }

        /// <summary>取图时告诉监听器别把这次剪贴板变化当成新截图。</summary>
        private bool _clipSuppress;
        public bool ClipSuppress { get { return _clipSuppress; } }

        /// <summary>诊断：最近一次从绳子上复制的时间与尺寸。</summary>
        public DateTime LastCopyAt { get; private set; }
        public string LastCopySize { get; private set; }

        /// <summary>诊断：最近一次点击命中的卡片（用于排查点击没反应）。</summary>
        public string LastClickInfo { get; private set; }

        private PreviewWindow _preview;

        private void OpenPreview(Shot shot)
        {
            if (shot == null) { return; }
            if (_preview != null && _preview.IsLoaded)
            {
                try { _preview.Close(); } catch { }
            }
            _preview = new PreviewWindow(shot);
            _preview.Show();
            _preview.Activate();
        }

        protected override void OnClosed(EventArgs e)
        {
            StopSlide();
            if (_tick != null) { _tick.Stop(); }
            if (_gustTimer != null) { _gustTimer.Stop(); }
            base.OnClosed(e);
        }
    }
}


