using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Liangshai
{
    /// <summary>
    /// 一张挂在绳子上的截图：金属小夹子 + 玻璃边框卡片。
    /// 全部魅力都在这里：落到绳子上、随风摆、被拿走时晃一下。
    /// 视觉与交互对照 macOS 原版 PeggedView。
    /// </summary>
    internal sealed class PeggedCard : Grid
    {
        private readonly Shot _shot;

        /// <summary>随机歪斜：每张照片都挂得有点不正，像真的晾着。</summary>
        public double Tilt { get; private set; }

        private readonly RotateTransform _swing = new RotateTransform(0);
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private readonly TranslateTransform _lift = new TranslateTransform(0, 0);
        private readonly Border _card;
        private Grid _closeChip;
        private readonly Border _copyChip;
        private readonly DropShadowEffect _shadow;
        private readonly double _cardW;
        private readonly double _cardH;

        private bool _hovering;
        private bool _arrived;
        private bool _pressed;

        private static readonly Random Rng = new Random();

        // ---------- 事件 ----------
        public event EventHandler Clicked;
        public event EventHandler DoubleClicked;
        public event EventHandler DiscardRequested;
        public event EventHandler PressChanged;
        public event EventHandler DragStarted;
        public event EventHandler DragEnded;

        public Shot Data { get { return _shot; } }
        public bool IsPressed { get { return _pressed; } }

        public PeggedCard(Shot shot)
        {
            _shot = shot;
            Tilt = Rng.NextDouble() * 5.0 - 2.5;   // -2.5 ~ +2.5 度

            double cw, ch;
            Layout.CardSize(shot.PixelWidth, shot.PixelHeight, out cw, out ch);
            _cardW = cw;
            _cardH = ch;

            Width = Layout.CardWidth;
            Height = Layout.PanelHeight - Layout.RopeTop - Layout.CardOffsetBelowTop + 60;
            Background = null;                  // 只有卡片本体可点，其余点击穿透
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            // 放在 Canvas 里要按左上角定位，不能被拉伸
            Margin = new Thickness(0);
            ToolTip = shot.TooltipText;
            ToolTipService.SetInitialShowDelay(this, 500);
            ToolTipService.SetShowDuration(this, 30000);
            Cursor = Cursors.Hand;

            RenderTransformOrigin = new Point(0.5, 0);

            // 金属小夹子
            var pin = BuildClothespin();
            pin.HorizontalAlignment = HorizontalAlignment.Center;
            pin.VerticalAlignment = VerticalAlignment.Top;

            // 照片 + 玻璃边框
            _shadow = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 10,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = 0.18
            };

            _card = BuildCard();
            _card.HorizontalAlignment = HorizontalAlignment.Center;
            _card.VerticalAlignment = VerticalAlignment.Top;
            _card.Margin = new Thickness(0, Layout.CardOffsetBelowTop, 0, 0);
            _card.Effect = _shadow;

            _closeChip = BuildCloseChip();

            // 卡片本体 + 左上角 ✕ 放在同一层，✕ 覆盖在卡片上
            var cardLayer = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Width = _cardW,
                Margin = new Thickness(0, Layout.CardOffsetBelowTop, 0, 0),
                // 显式透明背景，确保这一层参与命中测试（null 背景不参与）
                Background = Brushes.Transparent
            };
            cardLayer.Children.Add(_card);
            _closeChip.HorizontalAlignment = HorizontalAlignment.Left;
            _closeChip.VerticalAlignment = VerticalAlignment.Top;
            _closeChip.Margin = new Thickness(3, 3, 0, 0);
            cardLayer.Children.Add(_closeChip);

            var group = new TransformGroup();
            group.Children.Add(_swing);
            group.Children.Add(_scale);
            group.Children.Add(_lift);
            cardLayer.RenderTransformOrigin = new Point(0.5, 0);
            cardLayer.RenderTransform = group;

            Children.Add(pin);
            Children.Add(cardLayer);

            // 复制反馈芯片（卡片正下方）
            _copyChip = BuildCopyChip(cw);
            _copyChip.HorizontalAlignment = HorizontalAlignment.Center;
            _copyChip.VerticalAlignment = VerticalAlignment.Top;
            _copyChip.Margin = new Thickness(0, Layout.CardOffsetBelowTop + ch + 8, 0, 0);
            _copyChip.Opacity = 0;
            _copyChip.IsHitTestVisible = false;
            Children.Add(_copyChip);

            // 入场：从上方 46px 落下并淡入
            Opacity = 0;
            _lift.Y = -46;

            Loaded += (s, e) => Arrive();
            MouseEnter += (s, e) => SetHover(true);
            MouseLeave += (s, e) => SetHover(false);

            MouseLeftButtonDown += OnMouseDown;
            MouseLeftButtonUp += OnMouseUp;
            MouseMove += OnMouseMove;
            MouseRightButtonUp += (s, e) => { e.Handled = true; ShowContextMenu(); };
        }

        // ============================================================
        //  外观：与原版逐项对应
        // ============================================================

        /// <summary>极简铝制夹子：拉丝金属小圆柱，中间一道穿线槽。</summary>
        private static FrameworkElement BuildClothespin()
        {
            var metal = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            metal.GradientStops.Add(new GradientStop(Color.FromRgb(0xB3, 0xB3, 0xB3), 0.00));
            metal.GradientStops.Add(new GradientStop(Color.FromRgb(0xED, 0xED, 0xED), 0.35));
            metal.GradientStops.Add(new GradientStop(Color.FromRgb(0xD1, 0xD1, 0xD1), 0.65));
            metal.GradientStops.Add(new GradientStop(Color.FromRgb(0x9E, 0x9E, 0x9E), 1.00));

            var edge = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), 0.0));
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(0x2E, 0x00, 0x00, 0x00), 1.0));

            var body = new Border
            {
                Width = 9,
                Height = 26,
                CornerRadius = new CornerRadius(3.5),
                Background = metal,
                BorderBrush = edge,
                BorderThickness = new Thickness(0.6),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 2,
                    ShadowDepth = 1.5,
                    Direction = 270,
                    Opacity = 0.30
                }
            };

            // 绳子穿过的槽
            var slot = new Border
            {
                Width = 5,
                Height = 1.4,
                CornerRadius = new CornerRadius(0.7),
                Background = new SolidColorBrush(Color.FromArgb(0x52, 0x00, 0x00, 0x00)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8.5, 0, 0)
            };

            var host = new Grid { Width = 9, Height = 26, IsHitTestVisible = false };
            host.Children.Add(body);
            host.Children.Add(slot);
            return host;
        }

        /// <summary>照片 + 玻璃边框：照片圆角比边框小一圈，像系统嵌套圆角那样。</summary>
        private Border BuildCard()
        {
            double pw, ph;
            Layout.PhotoSize(_shot.PixelWidth, _shot.PixelHeight, out pw, out ph);

            var img = new Image
            {
                Source = _shot.Image,
                Width = pw,
                Height = ph,
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            // 照片本身：圆角 + 一圈极淡的白色描边
            var photoBorder = new Border
            {
                Width = pw,
                Height = ph,
                CornerRadius = new CornerRadius(Layout.FrameRadius - Layout.FrameInset),
                ClipToBounds = true,
                BorderThickness = new Thickness(0.5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
                Child = img
            };

            // 玻璃边框：极淡高光从上方打下来
            var glassStroke = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            glassStroke.GradientStops.Add(new GradientStop(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF), 0.0));
            glassStroke.GradientStops.Add(new GradientStop(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF), 1.0));

            return new Border
            {
                Width = _cardW,
                Height = _cardH,
                CornerRadius = new CornerRadius(Layout.FrameRadius),
                Padding = new Thickness(Layout.FrameInset),
                // 原版用 macOS 的 ultraThinMaterial；WPF 没有等价物，
                // 这里用半透明亮面近似"毛玻璃"的观感。
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0.75),
                BorderBrush = glassStroke,
                Child = photoBorder
            };
        }

        /// <summary>左上角的 ✕（原版在左上角，不是右上角），只在悬停时出现。</summary>
        private Grid BuildCloseChip()
        {
            var ring = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0.75),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)),
                Child = new TextBlock
                {
                    Text = "✕",
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0x1A, 0x1A, 0x1A)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var host = new Grid
            {
                Width = 20,
                Height = 20,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Opacity = 0,
                ToolTip = "丢掉这一张"
            };
            host.Children.Add(ring);
            host.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                EventHandler h = DiscardRequested;
                if (h != null) { h(this, EventArgs.Empty); }
            };
            return host;
        }

        /// <summary>"已复制" 反馈，出现在卡片正下方。</summary>
        private static Border BuildCopyChip(double cardW)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = "✓",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = "已复制",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            return new Border
            {
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(10, 5, 10, 5),
                Background = new SolidColorBrush(Color.FromArgb(0xD9, 0xF7, 0xF7, 0xF9)),
                BorderThickness = new Thickness(0.75),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)),
                Child = stack
            };
        }

        // ============================================================
        //  动画
        // ============================================================

        /// <summary>落到绳子上的入场：从上方落下 + 摆动衰减到静止。</summary>
        private void Arrive()
        {
            if (_arrived) { return; }
            _arrived = true;

            _swing.Angle = Tilt + 16;

            var fade = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fade);

            var drop = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
            };
            _lift.BeginAnimation(TranslateTransform.YProperty, drop);

            Nudge(0);
        }

        /// <summary>晃一下再弹回（原版：0.3 秒缓出到目标角度，再插值弹簧回正）。</summary>
        public void Nudge(double degrees)
        {
            double target = Tilt + degrees;

            var out1 = new DoubleAnimation(target, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            out1.Completed += (s, e) =>
            {
                // 用关键帧近似原版的 interpolatingSpring(stiffness:38, damping:2.4)
                var spring = new DoubleAnimationUsingKeyFrames
                {
                    FillBehavior = FillBehavior.HoldEnd
                };
                spring.KeyFrames.Add(new EasingDoubleKeyFrame(Tilt,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520)),
                    new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 }));
                _swing.BeginAnimation(RotateTransform.AngleProperty, spring);
            };
            _swing.BeginAnimation(RotateTransform.AngleProperty, out1);
        }

        private void SetHover(bool on)
        {
            if (_hovering == on) { return; }
            _hovering = on;

            // 悬停：阴影变深、轻微放大
            AnimateShadow(on ? 0.26 : 0.18, on ? 14 : 10, on ? 8 : 5);
            Animate(_scale, ScaleTransform.ScaleXProperty, _pressed ? 0.95 : (on ? 1.035 : 1.0), 180);
            Animate(_scale, ScaleTransform.ScaleYProperty, _pressed ? 0.95 : (on ? 1.035 : 1.0), 180);

            _closeChip.BeginAnimation(OpacityProperty,
                new DoubleAnimation(on ? 1.0 : 0.0, TimeSpan.FromMilliseconds(140)));
        }

        private void SetPressed(bool on)
        {
            if (_pressed == on) { return; }
            _pressed = on;

            double target = on ? 0.95 : (_hovering ? 1.035 : 1.0);
            // 按住时慢慢压下去，像在酝酿什么；松开时弹回
            int ms = on ? 450 : 220;
            Animate(_scale, ScaleTransform.ScaleXProperty, target, ms, on);
            Animate(_scale, ScaleTransform.ScaleYProperty, target, ms, on);

            EventHandler h = PressChanged;
            if (h != null) { h(this, EventArgs.Empty); }
        }

        private void AnimateShadow(double opacity, double radius, double depth)
        {
            var o = new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(180));
            var r = new DoubleAnimation(radius, TimeSpan.FromMilliseconds(180));
            var d = new DoubleAnimation(depth, TimeSpan.FromMilliseconds(180));
            _shadow.BeginAnimation(DropShadowEffect.OpacityProperty, o);
            _shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, r);
            _shadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty, d);
        }

        private static void Animate(Animatable target, DependencyProperty prop, double to, int ms,
                                    bool easeInOut = false)
        {
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = easeInOut
                    ? (IEasingFunction)new CubicEase { EasingMode = EasingMode.EaseInOut }
                    : new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            target.BeginAnimation(prop, anim);
        }

        /// <summary>闪一下"已复制"芯片，1.2 秒后消失（与原版一致）。</summary>
        public void ShowCopied()
        {
            var inAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200));
            _copyChip.BeginAnimation(OpacityProperty, inAnim);
            Nudge(3);

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1200)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var outAnim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250));
                _copyChip.BeginAnimation(OpacityProperty, outAnim);
            };
            timer.Start();
        }

        // ============================================================
        //  鼠标：单击复制 / 双击打开 / 拖出去 / 右键菜单
        // ============================================================
        private Point _downPoint;
        private bool _dragging;
        private DateTime _lastClick = DateTime.MinValue;

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (IsOverCloseChip(e)) { return; }   // 交给 ✕ 处理

            if (e.ClickCount == 2)
            {
                _downPoint = new Point(double.NaN, double.NaN);
                e.Handled = true;
                EventHandler h = DoubleClicked;
                if (h != null) { h(this, EventArgs.Empty); }
                return;
            }

            _downPoint = e.GetPosition(this);
            _dragging = false;
            SetPressed(true);
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (IsOverCloseChip(e)) { SetPressed(false); return; }

            bool wasDown = !double.IsNaN(_downPoint.X);
            SetPressed(false);
            _downPoint = new Point(double.NaN, double.NaN);

            if (!wasDown || _dragging) { return; }

            // 单双击区分：350ms 内第二次算双击
            DateTime now = DateTime.Now;
            if ((now - _lastClick).TotalMilliseconds < 350) { return; }
            _lastClick = now;

            EventHandler h = Clicked;
            if (h != null) { h(this, EventArgs.Empty); }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragging) { return; }
            if (double.IsNaN(_downPoint.X)) { return; }

            Point now = e.GetPosition(this);
            if (Math.Abs(now.X - _downPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(now.Y - _downPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _dragging = true;
            SetPressed(false);
            Opacity = 0.45;

            EventHandler started = DragStarted;
            if (started != null) { started(this, EventArgs.Empty); }

            try
            {
                var data = new DataObject();
                data.SetData("PNG", new System.IO.MemoryStream(_shot.Png));

                System.Windows.Media.Imaging.BitmapSource bmp = _shot.Image;
                if (bmp != null) { data.SetImage(bmp); }

                string temp = EnsureTempFile();
                if (!string.IsNullOrEmpty(temp))
                {
                    data.SetFileDropList(new System.Collections.Specialized.StringCollection { temp });
                }

                DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
            }
            catch { }
            finally
            {
                _dragging = false;
                Opacity = 1.0;
                EventHandler ended = DragEnded;
                if (ended != null) { ended(this, EventArgs.Empty); }
            }
        }

        private bool IsOverCloseChip(MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(_closeChip);
            return p.X >= 0 && p.Y >= 0 && p.X <= _closeChip.Width && p.Y <= _closeChip.Height;
        }

        /// <summary>拖到文件夹时要落地成文件，这里给一个临时文件路径。</summary>
        private string EnsureTempFile()
        {
            if (!string.IsNullOrEmpty(_shot.FilePath) && System.IO.File.Exists(_shot.FilePath))
            {
                return _shot.FilePath;
            }
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Liangshai");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir,
                    "截图_" + _shot.Time.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                if (!System.IO.File.Exists(path))
                {
                    System.IO.File.WriteAllBytes(path, _shot.Png);
                }
                return path;
            }
            catch { return null; }
        }

        private void ShowContextMenu()
        {
            var menu = new ContextMenu { FontSize = 12 };
            menu.Items.Add(MenuItemFor("复制到剪贴板", () =>
            {
                EventHandler h = Clicked;
                if (h != null) { h(this, EventArgs.Empty); }
            }));
            menu.Items.Add(MenuItemFor("大图预览", () =>
            {
                EventHandler h = DoubleClicked;
                if (h != null) { h(this, EventArgs.Empty); }
            }));
            menu.Items.Add(MenuItemFor("另存为文件…", () =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "保存这张截图",
                    FileName = ImgUtil.SuggestFileName(),
                    Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg)|*.jpg"
                };
                if (dlg.ShowDialog() == true)
                {
                    try
                    {
                        if (dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                        {
                            using (var ms = new System.IO.MemoryStream(_shot.Png))
                            using (var b = new System.Drawing.Bitmap(ms))
                            {
                                b.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Jpeg);
                            }
                        }
                        else
                        {
                            System.IO.File.WriteAllBytes(dlg.FileName, _shot.Png);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("保存失败：" + ex.Message, "晾晒工具",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }));
            if (!string.IsNullOrEmpty(_shot.FilePath) && System.IO.File.Exists(_shot.FilePath))
            {
                menu.Items.Add(MenuItemFor("在文件夹中显示",
                    () => ImgUtil.RevealInExplorer(_shot.FilePath)));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("丢掉这一张", () =>
            {
                EventHandler h = DiscardRequested;
                if (h != null) { h(this, EventArgs.Empty); }
            }));

            menu.PlacementTarget = this;
            menu.IsOpen = true;
        }

        private static MenuItem MenuItemFor(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, e) => { try { action(); } catch { } };
            return item;
        }

    }
}

