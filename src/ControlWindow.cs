using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Liangshai
{
    /// <summary>
    /// 控制窗口。
    ///
    /// 为什么要它：程序平时没有可见窗口，只能靠右下角托盘图标操作，
    /// 而 Windows 11 默认会把新出现的托盘图标折叠进隐藏区 —— 结果就是
    /// "找不到程序、也关不掉它"。这个窗口出现在任务栏上，给用户一条
    /// 看得见、点得到的路子，不必依赖托盘图标。
    /// </summary>
    internal sealed class ControlWindow : Window
    {
        public event EventHandler ToggleRopeRequested;
        public event EventHandler QuitRequested;
        public event EventHandler SettingsRequested;

        private readonly Button _ropeButton;
        private readonly TextBlock _ropeButtonTitle;

        public ControlWindow(Config cfg, int itemCount)
        {
            Title = "晾晒工具";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            // CanMinimize：禁止缩放，但**保留最小化按钮**
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xF7, 0xF2));
            FontSize = 12.5;
            UseLayoutRounding = true;
            // 出现在任务栏上，这样程序始终有一条"看得见"的入口
            ShowInTaskbar = true;
            // 置顶：确保一启动就能被看到，不会被别的窗口盖住
            Topmost = true;
            ShowActivated = true;

            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };

            // ---- 标题 + 图标 ----
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            try
            {
                head.Children.Add(new Image
                {
                    Source = ToImageSource(AppIcon.Create().ToBitmap()),
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            catch { }

            var titleBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleBox.Children.Add(new TextBlock
            {
                Text = "晾晒工具正在后台待命",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0x22, 0x18))
            });
            titleBox.Children.Add(new TextBlock
            {
                Text = "绳子上现在有 " + itemCount + " 张截图",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x82, 0x78)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            head.Children.Add(titleBox);
            root.Children.Add(head);

            // ---- 说明 ----
            root.Children.Add(new TextBlock
            {
                Text = "平时它没有窗口，只待在右下角的托盘里（Windows 11 默认会把它折叠进 ∧ 隐藏区）。\n" +
                       "如果你在托盘里看不到它，用这个窗口就够了。\n" +
                       "这个窗口只能最小化 —— 想彻底关掉后台程序，点最下面的「退出晾晒工具」。",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x66, 0x5C)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 0)
            });

            // ---- 操作按钮 ----
            var buttons = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };

            // 显示/隐藏绳子：做成一个按钮，点一次切换一次。
            // 这个窗口本身**不会消失**，所以你永远有地方可以点。
            _ropeButtonTitle = new TextBlock
            {
                Text = "显示绳子",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0x22, 0x18))
            };
            _ropeButton = BigButton(_ropeButtonTitle,
                "把屏幕顶端的绳子拉下来 / 收回去（也可以直接把鼠标停在屏幕最上边缘）",
                () => Raise(ToggleRopeRequested));
            buttons.Children.Add(_ropeButton);

            buttons.Children.Add(BigButton("设置…", "落盘开关、开机自启、截图后的提示等",
                () => Raise(SettingsRequested)));
            buttons.Children.Add(BigButton("退出晾晒工具", "彻底关闭后台程序，之后截图不再被接管",
                () => Raise(QuitRequested), true));

            root.Children.Add(buttons);

            // ---- 底部提示 ----
            root.Children.Add(new TextBlock
            {
                Text = "快捷键：Ctrl+Alt+T 显示/隐藏绳子    Ctrl+Alt+C 复制最近一张\n" +
                       "最小化后找不到它？重新双击 晾晒工具.exe 就能再打开；不想留后台就跑 晾晒工具.exe /quit",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x92, 0x88)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0)
            });

            Content = root;

            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { Close(); } };

            // 去掉标题栏的 ✕，只保留最小化。
            // 理由：这个窗口一关，程序还在后台跑，但用户就再没有入口了
            // （托盘图标在 Win11 上默认被折叠进隐藏区）。所以关窗这条路直接封掉，
            // 想退出只能走下面那个「退出晾晒工具」按钮，或者 /quit。
            SourceInitialized += (s, e) =>
            {
                HideCloseButton();
            };
        }

        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_CLOSE = 0xF060;

        /// <summary>程序要退出时置 true，这时才允许窗口真的关闭。</summary>
        private bool _allowClose;

        /// <summary>允许本窗口关闭（由 App 在退出流程里调用）。</summary>
        public void AllowClose() { _allowClose = true; }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var source = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(this);
                if (source != null) { source.AddHook(WndProc); }
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SYSCOMMAND && !_allowClose)
            {
                int cmd = wParam.ToInt32() & 0xFFF0;
                if (cmd == SC_CLOSE)
                {
                    // 直接吃掉"关闭"，只放行最小化等其它系统命令
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            return IntPtr.Zero;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 兜底：万一还有别的路径触发关闭，这里也挡掉
            if (!_allowClose && !_appQuitting)
            {
                e.Cancel = true;
            }
            base.OnClosing(e);
        }

        private bool _appQuitting;

        /// <summary>App 正在退出。</summary>
        public void MarkAppQuitting()
        {
            _appQuitting = true;
            _allowClose = true;
        }

        /// <summary>
        /// 把系统菜单里的"关闭"项删掉，✕ 按钮就会变灰不能点。
        /// 同时禁用 Alt+F4 / 标题栏右键里的关闭。
        /// </summary>
        private void HideCloseButton()
        {
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) { return; }

                IntPtr menu = GetSystemMenu(hwnd, false);
                if (menu != IntPtr.Zero)
                {
                    // 0xF060 = SC_CLOSE
                    DeleteMenu(menu, 0xF060, MF_BYCOMMAND);
                    EnableMenuItem(menu, 0xF060, MF_BYCOMMAND | MF_GRAYED);
                    DrawMenuBar(hwnd);
                }
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DrawMenuBar(IntPtr hWnd);

        private const uint MF_BYCOMMAND = 0x00000000;
        private const uint MF_GRAYED = 0x00000001;

        /// <summary>同步绳子当前是展开还是收起，让按钮文字与实际一致。</summary>
        public void SetRopeShown(bool shown)
        {
            if (_ropeButtonTitle == null) { return; }
            _ropeButtonTitle.Text = shown ? "隐藏绳子" : "显示绳子";
            if (_ropeButton != null)
            {
                try
                {
                    System.Windows.Automation.AutomationProperties.SetName(
                        _ropeButton, _ropeButtonTitle.Text);
                }
                catch { }
            }
        }

        private static void Raise(EventHandler h)
        {
            if (h != null) { h(null, EventArgs.Empty); }
        }

        private Button BigButton(string title, string subtitle, Action action, bool danger = false)
        {
            return BigButton(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = danger
                    ? new SolidColorBrush(Color.FromRgb(0xA8, 0x36, 0x2E))
                    : new SolidColorBrush(Color.FromRgb(0x2B, 0x22, 0x18))
            }, subtitle, action);
        }

        private Button BigButton(TextBlock titleBlock, string subtitle, Action action)
        {
            var stack = new StackPanel();
            stack.Children.Add(titleBlock);
            stack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x82, 0x78)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });

            var btn = new Button
            {
                Content = stack,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xE9, 0xDD)),
                BorderThickness = new Thickness(0)
            };
            // 按钮内容是多行 StackPanel，UI 自动化（含读屏软件）读不出文字，
            // 所以显式给一个无障碍名称。
            System.Windows.Automation.AutomationProperties.SetName(btn, titleBlock.Text);
            System.Windows.Automation.AutomationProperties.SetHelpText(btn, subtitle);
            btn.Click += (s, e) => { try { action(); } catch { } };
            return btn;
        }

        private static System.Windows.Media.Imaging.BitmapSource ToImageSource(System.Drawing.Bitmap bmp)
        {
            IntPtr h = bmp.GetHbitmap();
            try
            {
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    h, IntPtr.Zero, Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { DeleteObject(h); }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
