using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Liangshai
{
    /// <summary>设置窗口：落盘开关、数量上限、开机自启等。</summary>
    internal sealed class SettingsWindow : Window
    {
        private readonly Config _cfg;
        private readonly CheckBox _chkSave = new CheckBox();
        private readonly TextBox _txtFolder = new TextBox();
        private readonly Button _btnBrowse = new Button();
        private readonly TextBox _txtMax = new TextBox();
        private readonly CheckBox _chkAutoStart = new CheckBox();
        private readonly CheckBox _chkFullScreen = new CheckBox();
        private readonly CheckBox _chkPeek = new CheckBox();
        private readonly TextBox _txtPeekMs = new TextBox();
        private readonly TextBlock _hint = new TextBlock();

        public SettingsWindow(Config cfg)
        {
            _cfg = cfg;

            Title = "晾晒工具 · 设置";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xF7, 0xF2));
            FontSize = 12.5;
            UseLayoutRounding = true;
            // 程序平时没有可见窗口，任务栏和 Alt+Tab 里都找不到它。
            // 让设置窗口出现在任务栏上，用户就有一条"看得见"的退出路径。
            ShowInTaskbar = true;

            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };

            root.Children.Add(Header("历史保留"));
            root.Children.Add(Hint(
                "默认只在内存里放，程序一关就清空。打开下面的开关后，截图会保存到磁盘，\n" +
                "重启电脑后绳子上的图还在。"));

            _chkSave.Content = "把截图保存到磁盘（开启后重启仍能看到历史截图）";
            _chkSave.IsChecked = cfg.SaveToDisk;
            _chkSave.Margin = new Thickness(0, 6, 0, 8);
            _chkSave.Checked += (s, e) => UpdateEnabled();
            _chkSave.Unchecked += (s, e) => UpdateEnabled();
            root.Children.Add(_chkSave);

            var folderRow = new Grid { Margin = new Thickness(22, 0, 0, 14) };
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _txtFolder.Text = cfg.SaveFolder;
            _txtFolder.Padding = new Thickness(6, 3, 6, 3);
            _txtFolder.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_txtFolder, 0);
            folderRow.Children.Add(_txtFolder);

            _btnBrowse.Content = "浏览…";
            _btnBrowse.Padding = new Thickness(10, 3, 10, 3);
            _btnBrowse.Margin = new Thickness(8, 0, 0, 0);
            _btnBrowse.Cursor = Cursors.Hand;
            _btnBrowse.Click += (s, e) => BrowseFolder();
            Grid.SetColumn(_btnBrowse, 1);
            folderRow.Children.Add(_btnBrowse);
            root.Children.Add(folderRow);

            root.Children.Add(Header("绳子容量"));
            var maxRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
            maxRow.Children.Add(new TextBlock
            {
                Text = "绳子最多挂",
                VerticalAlignment = VerticalAlignment.Center
            });
            _txtMax.Text = cfg.MaxItems.ToString();
            _txtMax.Width = 70;
            _txtMax.Margin = new Thickness(8, 0, 8, 0);
            _txtMax.Padding = new Thickness(6, 3, 6, 3);
            _txtMax.TextAlignment = TextAlignment.Center;
            maxRow.Children.Add(_txtMax);
            maxRow.Children.Add(new TextBlock
            {
                Text = "张（超出后自动丢掉最旧的，0 = 不限制）",
                VerticalAlignment = VerticalAlignment.Center
            });
            root.Children.Add(maxRow);

            root.Children.Add(Header("截图后的反馈"));
            _chkPeek.Content = "截完图让绳子自动滑下来看一眼";
            _chkPeek.IsChecked = cfg.AutoShowMs > 0;
            _chkPeek.Margin = new Thickness(0, 4, 0, 6);
            _chkPeek.Checked += (s, e) => UpdateEnabled();
            _chkPeek.Unchecked += (s, e) => UpdateEnabled();
            root.Children.Add(_chkPeek);

            var peekRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 0, 0, 0) };
            peekRow.Children.Add(new TextBlock
            {
                Text = "停留",
                VerticalAlignment = VerticalAlignment.Center
            });
            _txtPeekMs.Text = (cfg.AutoShowMs > 0 ? cfg.AutoShowMs : 2500).ToString();
            _txtPeekMs.Width = 70;
            _txtPeekMs.Margin = new Thickness(8, 0, 8, 0);
            _txtPeekMs.Padding = new Thickness(6, 3, 6, 3);
            _txtPeekMs.TextAlignment = TextAlignment.Center;
            peekRow.Children.Add(_txtPeekMs);
            peekRow.Children.Add(new TextBlock
            {
                Text = "毫秒后自动收回",
                VerticalAlignment = VerticalAlignment.Center
            });
            root.Children.Add(peekRow);

            root.Children.Add(Header("其他"));
            _chkFullScreen.Content = "全屏应用（游戏 / 视频 / 演示）时自动隐藏绳子";
            _chkFullScreen.IsChecked = cfg.HideOnFullScreen;
            _chkFullScreen.Margin = new Thickness(0, 4, 0, 6);
            root.Children.Add(_chkFullScreen);

            _chkAutoStart.Content = "开机自动启动，一直在后台待命";
            _chkAutoStart.IsChecked = AutoStart.IsEnabled();
            root.Children.Add(_chkAutoStart);

            _hint.Text = "快捷键：Ctrl+Alt+T 显示/隐藏绳子    Ctrl+Alt+C 复制最近一张    Esc 关本窗口";
            _hint.FontSize = 11;
            _hint.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x80, 0x76));
            _hint.Margin = new Thickness(0, 14, 0, 0);
            _hint.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(_hint);

            // ---- 按钮 ----
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };

            var btnOpen = MakeButton("打开截图文件夹", () =>
            {
                try
                {
                    string dir = _txtFolder.Text.Trim();
                    if (!System.IO.Directory.Exists(dir)) { System.IO.Directory.CreateDirectory(dir); }
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
                }
                catch { }
            });
            btnOpen.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(btnOpen);

            var btnCancel = MakeButton("取消", () => Close());
            btnCancel.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(btnCancel);

            var btnSave = MakeButton("保存", () => SaveAndClose());
            btnSave.FontWeight = FontWeights.SemiBold;
            btnSave.Background = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0x4F));
            btnSave.Foreground = Brushes.White;
            buttons.Children.Add(btnSave);

            root.Children.Add(buttons);
            Content = root;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { Close(); }
            };

            UpdateEnabled();
        }

        private static TextBlock Header(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0x22, 0x18)),
                Margin = new Thickness(0, 14, 0, 2)
            };
        }

        private static TextBlock Hint(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x82, 0x78)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2)
            };
        }

        private static Button MakeButton(string text, Action action)
        {
            var b = new Button
            {
                Content = text,
                FontSize = 12.5,
                Padding = new Thickness(14, 5, 14, 5),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xDF, 0xD3)),
                BorderThickness = new Thickness(0)
            };
            b.Click += (s, e) => { try { action(); } catch { } };
            return b;
        }

        private void UpdateEnabled()
        {
            bool save = _chkSave.IsChecked == true;
            _txtFolder.IsEnabled = save;
            _btnBrowse.IsEnabled = save;
            _txtPeekMs.IsEnabled = _chkPeek.IsChecked == true;
        }

        private void BrowseFolder()
        {
            // 用 FolderBrowserDialog（System.Windows.Forms）选目录
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "选择截图保存到哪个文件夹";
                dlg.ShowNewFolderButton = true;
                try
                {
                    if (System.IO.Directory.Exists(_txtFolder.Text))
                    {
                        dlg.SelectedPath = _txtFolder.Text;
                    }
                }
                catch { }
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _txtFolder.Text = dlg.SelectedPath;
                }
            }
        }

        private void SaveAndClose()
        {
            _cfg.SaveToDisk = _chkSave.IsChecked == true;

            string folder = _txtFolder.Text.Trim();
            if (folder.Length > 0) { _cfg.SaveFolder = folder; }

            int max;
            if (int.TryParse(_txtMax.Text.Trim(), out max) && max >= 0)
            {
                _cfg.MaxItems = max;
            }

            int peek;
            if (int.TryParse(_txtPeekMs.Text.Trim(), out peek) && peek >= 0)
            {
                _cfg.AutoShowMs = _chkPeek.IsChecked == true ? Math.Max(300, peek) : 0;
            }
            else
            {
                _cfg.AutoShowMs = _chkPeek.IsChecked == true ? 2500 : 0;
            }

            _cfg.HideOnFullScreen = _chkFullScreen.IsChecked == true;

            bool wantAutoStart = _chkAutoStart.IsChecked == true;
            if (wantAutoStart != AutoStart.IsEnabled())
            {
                AutoStart.Set(wantAutoStart);
            }

            _cfg.Save();
            Close();
        }
    }
}

