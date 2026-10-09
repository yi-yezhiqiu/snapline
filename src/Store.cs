using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace Liangshai
{
    /// <summary>绳子上挂着的一张截图。</summary>
    internal sealed class Shot : INotifyPropertyChanged
    {
        public string Id;
        public byte[] Png;
        public DateTime Time;
        public int PixelWidth;
        public int PixelHeight;

        /// <summary>落盘后的完整路径（未开启保存时为 null）。</summary>
        public string FilePath { get; set; }

        private BitmapSource _image;
        private bool _imageTried;

        public BitmapSource Image
        {
            get
            {
                if (!_imageTried)
                {
                    _imageTried = true;
                    _image = ImgUtil.BytesToImageSource(Png);
                }
                return _image;
            }
        }

        public string TimeText { get { return Time.ToString("HH:mm:ss"); } }

        public string SizeText
        {
            get
            {
                if (PixelWidth > 0 && PixelHeight > 0)
                {
                    return PixelWidth + " × " + PixelHeight;
                }
                return "";
            }
        }

        public string TooltipText
        {
            get
            {
                return Time.ToString("yyyy-MM-dd HH:mm:ss") + "\n" +
                       SizeText + "\n" +
                       "单击=复制   双击=大图   右键=更多";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void Refresh()
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null) { h(this, new PropertyChangedEventArgs(null)); }
        }
    }

    /// <summary>
    /// 绳子的数据源。默认只放内存（退出即清空）；
    /// 打开"保存到磁盘"后图片会落到指定文件夹，下次启动自动挂回来。
    /// </summary>
    internal sealed class Store
    {
        private readonly ObservableCollection<Shot> _items = new ObservableCollection<Shot>();
        private readonly object _gate = new object();

        public ReadOnlyObservableCollection<Shot> Items { get; private set; }

        /// <summary>列表发生变化（新增/删除/清空）。</summary>
        public event EventHandler Changed;

        private Config _cfg;

        public Store()
        {
            Items = new ReadOnlyObservableCollection<Shot>(_items);
        }

        public Config Config
        {
            get { return _cfg; }
            set
            {
                _cfg = value;
                if (_cfg != null && _cfg.SaveToDisk)
                {
                    try { Directory.CreateDirectory(_cfg.SaveFolder); }
                    catch { }
                }
            }
        }

        public int Count
        {
            get { lock (_gate) { return _items.Count; } }
        }

        public Shot Newest
        {
            get { lock (_gate) { return _items.Count > 0 ? _items[0] : null; } }
        }

        /// <summary>新截图挂到绳子最左边（最新在左，和原版一致）。</summary>
        public Shot Add(byte[] png, DateTime time, int w, int h)
        {
            var shot = new Shot
            {
                Id = Guid.NewGuid().ToString("N"),
                Png = png,
                Time = time,
                PixelWidth = w,
                PixelHeight = h
            };

            if (_cfg != null && _cfg.SaveToDisk)
            {
                try
                {
                    string path = Path.Combine(_cfg.SaveFolder,
                        "截图_" + time.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                    if (ImgUtil.SavePngToFile(png, path)) { shot.FilePath = path; }
                }
                catch { }
            }

            lock (_gate) { _items.Insert(0, shot); }
            Trim();
            RaiseChanged();
            return shot;
        }

        /// <summary>从磁盘文件夹把历史截图挂回绳子。</summary>
        public void LoadFromDisk()
        {
            if (_cfg == null || !_cfg.SaveToDisk) { return; }
            try
            {
                if (!Directory.Exists(_cfg.SaveFolder)) { return; }

                var files = new DirectoryInfo(_cfg.SaveFolder)
                    .GetFiles("*.png", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(Math.Max(1, _cfg.MaxItems <= 0 ? 500 : _cfg.MaxItems))
                    .OrderBy(f => f.LastWriteTimeUtc)
                    .ToList();

                foreach (FileInfo f in files)
                {
                    byte[] png;
                    try { png = File.ReadAllBytes(f.FullName); }
                    catch { continue; }

                    int w = 0, h = 0;
                    try
                    {
                        using (var ms = new MemoryStream(png))
                        using (var bmp = new System.Drawing.Bitmap(ms))
                        {
                            w = bmp.Width;
                            h = bmp.Height;
                        }
                    }
                    catch { continue; }

                    var shot = new Shot
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Png = png,
                        Time = f.LastWriteTime,
                        PixelWidth = w,
                        PixelHeight = h,
                        FilePath = f.FullName
                    };
                    lock (_gate) { _items.Insert(0, shot); }
                }
                RaiseChanged();
            }
            catch { }
        }

        public void Remove(Shot shot)
        {
            if (shot == null) { return; }
            bool removed;
            lock (_gate) { removed = _items.Remove(shot); }
            if (!removed) { return; }

            // 落过盘的顺手删掉，避免占空间
            if (!string.IsNullOrEmpty(shot.FilePath))
            {
                try { if (File.Exists(shot.FilePath)) { File.Delete(shot.FilePath); } }
                catch { }
            }
            RaiseChanged();
        }

        public void Clear()
        {
            List<Shot> snapshot;
            lock (_gate) { snapshot = _items.ToList(); }
            foreach (Shot s in snapshot) { Remove(s); }
            RaiseChanged();
        }

        /// <summary>超出上限时丢掉最旧的（绳子右端）。</summary>
        private void Trim()
        {
            int max = _cfg == null ? 200 : _cfg.MaxItems;
            if (max <= 0) { return; }

            List<Shot> drop = new List<Shot>();
            lock (_gate)
            {
                while (_items.Count > max && _items.Count > 0)
                {
                    Shot last = _items[_items.Count - 1];
                    _items.RemoveAt(_items.Count - 1);
                    drop.Add(last);
                }
            }

            foreach (Shot s in drop)
            {
                if (!string.IsNullOrEmpty(s.FilePath))
                {
                    try { if (File.Exists(s.FilePath)) { File.Delete(s.FilePath); } }
                    catch { }
                }
            }
        }

        private void RaiseChanged()
        {
            EventHandler h = Changed;
            if (h != null) { h(this, EventArgs.Empty); }
        }
    }
}
