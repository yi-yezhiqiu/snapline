using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Liangshai
{
    internal sealed class App : Application
    {
        private const string MutexName = @"Local\Liangshai_ScreenshotLine_SingleInstance";

        /// <summary>
        /// 用来让第二个实例通知正在运行的实例"退出"。
        /// 程序平时没有可见窗口，任务栏和 Alt+Tab 里都找不到，
        /// 所以必须留一条命令行退路： 晾晒工具.exe /quit
        /// </summary>
        private const string QuitEventName = @"Local\Liangshai_ScreenshotLine_Quit";

        /// <summary>第二个实例用来通知正在运行的实例"把控制窗口显示出来"。</summary>
        private const string ShowEventName = @"Local\Liangshai_ScreenshotLine_Show";

        private Mutex _mutex;
        private Config _cfg;
        private Store _store;
        private RopeWindow _rope;
        private ClipWatcher _clip;
        private TrayIcon _tray;
        private HotkeyWindow _hotkeys;
        private FullScreenWatcher _fullScreen;
        private SettingsWindow _settings;
        private ControlWindow _control;

        private EventWaitHandle _quitEvent;
        private Thread _quitThread;
        private EventWaitHandle _showEvent;
        private Thread _showThread;
        private DispatcherTimer _stateSync;

        public bool FullScreenActive { get; private set; }

        /// <summary>诊断模式下暴露窗口对象。</summary>
        public RopeWindow Rope { get { return _rope; } }

        /// <summary>诊断模式（命令行 /diag）：会打印窗口枚举与自检信息。</summary>
        public static bool DiagMode;

        /// <summary>自检模式（命令行 /selftest）：跑 DIB 解码自检后正常启动。</summary>
        public static bool SelfTest;

        /// <summary>静默模式（命令行 /quiet）：不弹任何确认框。</summary>
        public static bool Quiet;

        /// <summary>图标自检模式（命令行 /icontest）。</summary>
        public static bool IconTest;

        /// <summary>拿到当前的 App 实例（App.Current 的类型是基类 Application）。</summary>
        public static App Instance { get { return Current as App; } }

        public IntPtr MainWindowHandle
        {
            get
            {
                if (_rope == null) { return IntPtr.Zero; }
                try
                {
                    return new System.Windows.Interop.WindowInteropHelper(_rope).Handle;
                }
                catch { return IntPtr.Zero; }
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            bool wantQuit = false;
            bool wantHelp = false;
            foreach (string a in args)
            {
                if (string.Equals(a, "/diag", StringComparison.OrdinalIgnoreCase)) { DiagMode = true; }
                if (string.Equals(a, "/selftest", StringComparison.OrdinalIgnoreCase)) { SelfTest = true; }
                if (string.Equals(a, "/icontest", StringComparison.OrdinalIgnoreCase)) { IconTest = true; }
                if (string.Equals(a, "/quit", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/exit", StringComparison.OrdinalIgnoreCase)) { wantQuit = true; }
                if (string.Equals(a, "/help", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/?", StringComparison.OrdinalIgnoreCase)) { wantHelp = true; }
                if (string.Equals(a, "/quiet", StringComparison.OrdinalIgnoreCase)) { Quiet = true; }
            }

            if (wantHelp)
            {
                MessageBox.Show(
                    "晾晒工具\n\n" +
                    "它平时没有窗口，只待在右下角的托盘里（可能在 ∧ 折叠区里）。\n\n" +
                    "退出方法（任选一种）：\n" +
                    "  1. 右键右下角托盘图标 → 退出\n" +
                    "  2. 托盘右键 → 设置… → 关闭设置窗口后再右键任务栏图标 → 关闭窗口\n" +
                    "  3. 命令行运行：晾晒工具.exe /quit\n\n" +
                    "显示绳子：鼠标停在屏幕最上边缘，或按 Ctrl+Alt+T，或点托盘图标。",
                    "晾晒工具 · 帮助", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // /quit：通知已经在跑的那个实例退出
            if (wantQuit)
            {
                bool sent = false;
                try
                {
                    EventWaitHandle ev;
                    if (EventWaitHandle.TryOpenExisting(QuitEventName, out ev))
                    {
                        using (ev) { ev.Set(); }
                        sent = true;
                    }
                }
                catch { }

                if (!Quiet)
                {
                    MessageBox.Show(sent ? "已通知晾晒工具退出。" : "晾晒工具当前没有在运行。",
                        "晾晒工具", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    if (!DiagMode)
                    {
                        // 已经有实例在跑：让那个实例把控制窗口恢复出来，
                        // 这样即使用户把它最小化了，重新双击 exe 也能找回来。
                        bool signaled = false;
                        try
                        {
                            EventWaitHandle ev;
                            if (EventWaitHandle.TryOpenExisting(ShowEventName, out ev))
                            {
                                using (ev) { ev.Set(); }
                                signaled = true;
                            }
                        }
                        catch { }

                        if (!signaled)
                        {
                            MessageBox.Show(
                                "晾晒工具已经在运行了。\n\n" +
                                "它待在一个已经打开的窗口里，请到任务栏上找「晾晒工具」（或点托盘图标）。\n" +
                                "想退出的话：运行  晾晒工具.exe /quit",
                                "晾晒工具", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    return;
                }

                var app = new App();
                app._mutex = mutex;
                app.Run();
            }
        }

        /// <summary>等待"退出"信号的后台线程。</summary>
        private void StartQuitListener()
        {
            try
            {
                _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
                _quitThread = new Thread(() =>
                {
                    try
                    {
                        while (true)
                        {
                            if (!_quitEvent.WaitOne()) { continue; }
                            Dispatcher.BeginInvoke(new Action(Quit));
                        }
                    }
                    catch { }
                });
                _quitThread.IsBackground = true;
                _quitThread.Name = "quit-listener";
                _quitThread.Start();
            }
            catch (Exception ex) { LogError(ex); }

            // 第二个实例启动时，把控制窗口恢复出来（用户最小化后靠这个找回来）
            try
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                _showThread = new Thread(() =>
                {
                    try
                    {
                        while (true)
                        {
                            if (!_showEvent.WaitOne()) { continue; }
                            Dispatcher.BeginInvoke(new Action(RestoreControl));
                        }
                    }
                    catch { }
                });
                _showThread.IsBackground = true;
                _showThread.Name = "show-listener";
                _showThread.Start();
            }
            catch (Exception ex) { LogError(ex); }
        }

        /// <summary>把控制窗口从最小化状态恢复出来并置前。</summary>
        private void RestoreControl()
        {
            try
            {
                if (_control == null || !_control.IsLoaded)
                {
                    OpenControl();
                    return;
                }

                if (_control.WindowState == WindowState.Minimized)
                {
                    _control.WindowState = WindowState.Normal;
                }
                _control.Show();
                _control.Activate();
            }
            catch (Exception ex) { LogError(ex); }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Trace("OnStartup 开始");
            DispatcherUnhandledException += (s, args) =>
            {
                LogError(args.Exception);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                LogError(args.ExceptionObject as Exception);
            };

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // 让 /quit 能把这个实例关掉（程序没有可见窗口，需要命令行退路）
            StartQuitListener();

            Trace("读取配置");
            _cfg = Config.Load();

            Trace("建立数据源");
            _store = new Store { Config = _cfg };
            if (_cfg.SaveToDisk) { _store.LoadFromDisk(); }

            // --- 顶端晾衣绳 ---
            Trace("建立晾衣绳窗口");
            _rope = new RopeWindow(_store, _cfg);
            MainWindow = _rope;
            _rope.Show();   // 构造时就定位在屏幕上方之外，所以显示出来也是看不见的
            Trace("窗口已显示");

            // --- 全屏检测 ---
            Trace("建立全屏检测");
            _fullScreen = new FullScreenWatcher();
            _fullScreen.Changed += (s, args) =>
            {
                FullScreenActive = _fullScreen.IsFullScreen;
                if (FullScreenActive && _cfg.HideOnFullScreen && _rope != null)
                {
                    _rope.HideRope();
                }
            };
            _fullScreen.Start();

            // --- 剪贴板监听 ---
            Trace("建立剪贴板监听");
            _clip = new ClipWatcher();
            _clip.ImageCaptured += OnImageCaptured;
            Trace("启动剪贴板监听");
            if (_cfg.WatchEnabled && !_clip.Start())
            {
                Trace("  AddClipboardFormatListener 失败");
            }
            Trace("剪贴板监听 OK");

            // --- 托盘 ---
            Trace("建立托盘图标");
            _tray = new TrayIcon(_cfg);
            _tray.ShowRequested += (s, args) => _rope.ShowRope();
            _tray.CopyLastRequested += (s, args) => CopyNewest();
            _tray.PauseToggled += (s, args) => TogglePause();
            _tray.SettingsRequested += (s, args) => OpenSettings();
            _tray.ClearRequested += (s, args) => ClearAll();
            _tray.QuitRequested += (s, args) => Quit();
            _tray.SaveToDiskToggled += (s, args) => ToggleSaveToDisk();
            _tray.OpenControlRequested += (s, args) => OpenControl();
            Trace("托盘 OK");

            // --- 全局热键 ---
            Trace("注册全局热键");
            _hotkeys = new HotkeyWindow();
            _hotkeys.TogglePressed += (s, args) => ToggleRope();
            _hotkeys.CopyPressed += (s, args) => CopyNewest();
            RegisterHotkeys();
            Trace("热键 OK");

            // --- 告诉用户程序在哪、怎么退出 ---
            // 程序没有可见窗口，任务栏和 Alt+Tab 里都找不到，只能靠托盘图标；
            // 而 Win11 默认会把新图标折叠进隐藏区，所以每次启动都提示一次。
            _tray.ShowBalloon("晾晒工具已经在待命",
                "它待在右下角的托盘里（可能折叠在 ∧ 里面），右键那个图标可以设置或退出。\n" +
                "微信 Alt+A 截的图会自动挂到屏幕顶端的绳子上。");
            MarkFirstRunDone();

            Trace("OnStartup 完成");

            if (DiagMode) { Diagnoser.Schedule(); }
            if (SelfTest) { DibSelfTest.Run(); }
            if (IconTest) { IconSelfTest.Run(); }

            // 定时同步控制窗口的按钮状态（绳子会自动收回，按钮文字要跟上）
            _stateSync = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _stateSync.Tick += (ts, te) => SyncControlState();
            _stateSync.Start();

            // --- 启动时把控制窗口显示出来 ---
            // 程序平时没有可见窗口，只能靠托盘图标操作，而 Win11 默认会把新出现的
            // 托盘图标折叠进隐藏区 —— 用户因此"找不到程序、也关不掉它"。
            // 所以启动时给一个出现在任务栏上的窗口，从这里就能显示绳子或退出。
            if (!DiagMode) { OpenControl(); }
        }

        /// <summary>打开控制窗口（托盘图标不好找时的退路）。</summary>
        private void OpenControl()
        {
            try
            {
                if (_control != null && _control.IsLoaded)
                {
                    _control.Show();
                    _control.Activate();
                    return;
                }

                _control = new ControlWindow(_cfg, _store.Count);
                // 切换显示/隐藏绳子。注意：这个窗口**不能隐藏**，
                // 否则用户点完就没了入口（之前就是这么出的问题）。
                _control.ToggleRopeRequested += (s, e) =>
                {
                    if (_rope.IsShown) { _rope.HideRope(); }
                    else { _rope.ShowRope(); }
                    ControlWindow w = _control;
                    if (w != null) { w.SetRopeShown(_rope.IsShown); }
                };
                _control.SettingsRequested += (s, e) => OpenSettings();
                _control.QuitRequested += (s, e) =>
                {
                    ControlWindow w = _control;
                    if (w != null) { w.AllowClose(); }
                    Quit();
                };
                _control.Closed += (s, e) =>
                {
                    // 用户关掉控制窗口 = 让它安静待命。托盘图标 / 重启程序都能再叫出来。
                    _control = null;
                };
                _control.Show();
                _control.Activate();
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        /// <summary>诊断模式：直接往绳子上塞一张测试图（用来检查多图排版）。</summary>
        public void InjectShotForDiagnostics(byte[] png, int w, int h)
        {
            if (_store == null || png == null) { return; }
            _store.Add(png, DateTime.Now, w, h);
        }

        /// <summary>启动过程追踪，出问题时用来定位卡在哪一步。</summary>
        internal static void Trace(string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "liangshai-trace.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch { }
        }

        // ============================================================
        //  剪贴板新图片 → 挂上绳子
        // ============================================================
        private void OnImageCaptured(object sender, ImageCapturedEventArgs e)
        {
            if (e == null || e.Png == null) { return; }
            Trace("[app] OnImageCaptured " + e.Png.Length + " 字节");
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Shot shot = _store.Add(e.Png, e.Time, e.PixelWidth, e.PixelHeight);
                    Trace("[app] 已挂上绳子，共 " + _store.Count + " 张；AutoShowMs=" + _cfg.AutoShowMs);
                    if (_cfg.AutoShowMs > 0 && !FullScreenActive)
                    {
                        _rope.Peek(_cfg.AutoShowMs);
                        Trace("[app] 已请求滑出绳子");
                    }
                }
                catch (Exception ex) { LogError(ex); }
            }));
        }

        // ============================================================
        //  各种菜单动作
        // ============================================================
        private void ToggleRope()
        {
            if (_rope.IsShown) { _rope.HideRope(); }
            else { _rope.ShowRope(); }
        }

        /// <summary>
        /// 让控制窗口上的按钮文字与实际状态一致。
        /// 绳子会因为鼠标离开而自动收回，这时按钮要变回"显示绳子"。
        /// </summary>
        private bool _lastRopeShown;

        private void SyncControlState()
        {
            ControlWindow w = _control;
            if (w == null || !w.IsLoaded) { return; }

            bool shown = _rope.IsShown;
            if (shown == _lastRopeShown) { return; }
            _lastRopeShown = shown;
            try { w.SetRopeShown(shown); } catch { }
        }

        private void CopyNewest()
        {
            Shot shot = _store.Newest;
            if (shot == null)
            {
                _tray.ShowBalloon("晾晒工具", "绳子上还没有截图。");
                return;
            }
            _clip.Suppress = true;
            try { ImgUtil.PutPngOnClipboard(shot.Png); }
            finally
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                t.Tick += (s, e) => { _clip.Suppress = false; t.Stop(); };
                t.Start();
            }
            _tray.ShowBalloon("晾晒工具", "最近一张截图已复制到剪贴板，去 Ctrl+V 吧。");
        }

        private void TogglePause()
        {
            _cfg.WatchEnabled = !_cfg.WatchEnabled;
            if (_cfg.WatchEnabled)
            {
                _clip.Start();
            }
            else
            {
                _clip.Stop();
            }
            _cfg.Save();
            _tray.Refresh();
        }

        private void ToggleSaveToDisk()
        {
            _cfg.SaveToDisk = !_cfg.SaveToDisk;
            _store.Config = _cfg;
            if (_cfg.SaveToDisk)
            {
                try { Directory.CreateDirectory(_cfg.SaveFolder); } catch { }
                _store.LoadFromDisk();
            }
            _cfg.Save();
            _tray.Refresh();
            _tray.ShowBalloon("晾晒工具",
                _cfg.SaveToDisk
                    ? "已开启保存：以后截图会存到 " + _cfg.SaveFolder
                    : "已关闭保存：新截图只放在内存里，退出即清空。");
        }

        private void ClearAll()
        {
            if (_store.Count == 0) { return; }
            MessageBoxResult r = MessageBox.Show(
                "确定要丢掉绳子上全部 " + _store.Count + " 张图吗？\n" +
                (_cfg.SaveToDisk ? "（已保存到磁盘的图也会被删除）" : "（这些图只在内存里，删了就找不回来了）"),
                "晾晒工具", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (r != MessageBoxResult.OK) { return; }

            _store.Clear();
            _clip.ForgetLastHash();
        }

        private void OpenSettings()
        {
            if (_settings != null && _settings.IsLoaded)
            {
                _settings.Activate();
                return;
            }
            _settings = new SettingsWindow(_cfg);
            _settings.Closed += (s, e) =>
            {
                _store.Config = _cfg;
                if (_cfg.SaveToDisk) { _store.LoadFromDisk(); }
                RegisterHotkeys();
                _tray.Refresh();
                _rope.ShowRope();
            };
            _settings.Show();
        }

        private void RegisterHotkeys()
        {
            _hotkeys.UnregisterAll();
            if (!_cfg.HotkeysEnabled) { return; }
            if (!_hotkeys.RegisterAll())
            {
                _tray.ShowBalloon("晾晒工具", "Ctrl+Alt+T 快捷键被别的程序占用了，用托盘图标也能打开绳子。");
            }
        }

        private void Quit()
        {
            try
            {
                // 允许控制窗口真正关闭，然后才走退出流程
                ControlWindow cw = _control;
                if (cw != null) { try { cw.MarkAppQuitting(); } catch { } }

                _cfg.Save();
                if (_clip != null) { _clip.Dispose(); }
                if (_hotkeys != null) { _hotkeys.UnregisterAll(); }
                if (_fullScreen != null) { _fullScreen.Stop(); }
                if (_tray != null) { _tray.Dispose(); }
            }
            catch { }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { if (_tray != null) { _tray.Dispose(); } } catch { }
            try { if (_clip != null) { _clip.Dispose(); } } catch { }
            base.OnExit(e);
        }

        // ============================================================
        //  辅助
        // ============================================================
        private static string DataDir
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Liangshai");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        private static string FirstRunFlag { get { return Path.Combine(DataDir, "welcomed.flag"); } }

        private static bool FirstRunDone() { return File.Exists(FirstRunFlag); }

        private static void MarkFirstRunDone()
        {
            try { File.WriteAllText(FirstRunFlag, DateTime.Now.ToString("u")); } catch { }
        }

        public static void LogError(Exception ex)
        {
            if (ex == null) { return; }
            try
            {
                File.AppendAllText(Path.Combine(DataDir, "error.log"),
                    DateTime.Now.ToString("u") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }
}

