using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Liangshai
{
    /// <summary>双击缩略图后的大图预览窗。</summary>
    internal sealed class PreviewWindow : Window
    {
        private readonly Image _image = new Image();
        private readonly ScaleTransform _zoom = new ScaleTransform(1, 1);
        private readonly TextBlock _status = new TextBlock();
        private double _scale = 1.0;

        public PreviewWindow(Shot shot)
        {
            Title = "预览 - " + shot.Time.ToString("yyyy-MM-dd HH:mm:ss") + "  " + shot.SizeText;
            Width = 1100;
            Height = 740;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1E));
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var root = new DockPanel();

            // ---- 顶部工具条 ----
            var bar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2B)),
                Padding = new Thickness(10, 7, 10, 7)
            };
            var barStack = new StackPanel { Orientation = Orientation.Horizontal };

            barStack.Children.Add(MakeButton("复制到剪贴板", () =>
            {
                ImgUtil.PutPngOnClipboard(shot.Png);
                _status.Text = "已复制到剪贴板";
            }));
            barStack.Children.Add(MakeButton("另存为…", () => SaveAs(shot)));
            barStack.Children.Add(MakeButton("缩小", () => SetScale(_scale / 1.25)));
            barStack.Children.Add(MakeButton("原始大小", () => SetScale(1.0)));
            barStack.Children.Add(MakeButton("放大", () => SetScale(_scale * 1.25)));
            barStack.Children.Add(MakeButton("适应窗口", FitToWindow));

            _status.Text = shot.SizeText + "   滚轮缩放，拖动查看";
            _status.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA2));
            _status.FontSize = 11.5;
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.Margin = new Thickness(16, 0, 0, 0);
            barStack.Children.Add(_status);

            bar.Child = barStack;
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);

            // ---- 图片区 ----
            _image.Source = shot.Image;
            _image.Stretch = Stretch.None;
            _image.HorizontalAlignment = HorizontalAlignment.Center;
            _image.VerticalAlignment = VerticalAlignment.Center;
            _image.RenderTransformOrigin = new Point(0.5, 0.5);
            _image.RenderTransform = _zoom;
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);

            var scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x17)),
                Content = _image
            };
            scroller.PreviewMouseWheel += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    SetScale(e.Delta > 0 ? _scale * 1.1 : _scale / 1.1);
                }
                else
                {
                    SetScale(e.Delta > 0 ? _scale * 1.1 : _scale / 1.1);
                }
                e.Handled = true;
            };
            root.Children.Add(scroller);

            Content = root;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { Close(); }
                else if (e.Key == Key.D0) { SetScale(1.0); }
                else if (e.Key == Key.OemPlus || e.Key == Key.Add) { SetScale(_scale * 1.25); }
                else if (e.Key == Key.OemMinus || e.Key == Key.Subtract) { SetScale(_scale / 1.25); }
            };

            Loaded += (s, e) => FitToWindow();
        }

        private static Button MakeButton(string text, Action action)
        {
            var b = new Button
            {
                Content = text,
                FontSize = 11.5,
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40)),
                BorderThickness = new Thickness(0)
            };
            b.Click += (s, e) => { try { action(); } catch { } };
            return b;
        }

        private void SetScale(double s)
        {
            _scale = Math.Max(0.05, Math.Min(8.0, s));
            _zoom.ScaleX = _scale;
            _zoom.ScaleY = _scale;
            _status.Text = "缩放 " + Math.Round(_scale * 100) + "%";
        }

        private void FitToWindow()
        {
            BitmapSource src = _image.Source as BitmapSource;
            if (src == null || src.PixelWidth == 0) { return; }
            double availW = Math.Max(100, ActualWidth - 60);
            double availH = Math.Max(100, ActualHeight - 120);
            double s = Math.Min(availW / src.PixelWidth, availH / src.PixelHeight);
            if (s > 1.0) { s = 1.0; }
            SetScale(s);
        }

        private void SaveAs(Shot shot)
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
                        using (var ms = new System.IO.MemoryStream(shot.Png))
                        using (var bmp = new System.Drawing.Bitmap(ms))
                        {
                            bmp.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Jpeg);
                        }
                    }
                    else
                    {
                        System.IO.File.WriteAllBytes(dlg.FileName, shot.Png);
                    }
                    _status.Text = "已保存：" + dlg.FileName;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存失败：" + ex.Message, "晾晒工具",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
}
