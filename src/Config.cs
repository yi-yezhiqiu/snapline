using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Liangshai
{
    /// <summary>应用配置。持久化到 %APPDATA%\Liangshai\config.ini（无需 JSON 库）。</summary>
    internal sealed class Config
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Liangshai");

        private static readonly string FilePath = Path.Combine(Dir, "config.ini");

        /// <summary>绳子最多挂多少张。0 表示不限制。</summary>
        public int MaxItems = 200;

        /// <summary>是否把图片保存到磁盘（默认关闭：只放内存，退出即清空）。</summary>
        public bool SaveToDisk = false;

        /// <summary>磁盘保存目录。</summary>
        public string SaveFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "晾晒工具");

        /// <summary>截图后绳子自动滑下来展示的毫秒数。0 表示不自动展示。</summary>
        public int AutoShowMs = 2500;

        /// <summary>启动时自动开始接管剪贴板。</summary>
        public bool WatchEnabled = true;

        /// <summary>全屏应用（游戏/视频/演示）时自动隐藏绳子。</summary>
        public bool HideOnFullScreen = true;

        /// <summary>全局热键开关。</summary>
        public bool HotkeysEnabled = true;

        public static Config Load()
        {
            var cfg = new Config();
            try
            {
                if (!File.Exists(FilePath)) { return cfg; }
                foreach (string rawLine in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') { continue; }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) { continue; }
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    cfg.Apply(key, val);
                }
            }
            catch
            {
                // 配置损坏时退回默认值，绝不因为读配置失败而崩溃
            }
            return cfg;
        }

        private void Apply(string key, string val)
        {
            switch (key.ToLowerInvariant())
            {
                case "maxitems": MaxItems = ParseInt(val, MaxItems); break;
                case "savetodisk": SaveToDisk = ParseBool(val, SaveToDisk); break;
                case "savefolder": if (val.Length > 0) { SaveFolder = val; } break;
                case "autoshowms": AutoShowMs = ParseInt(val, AutoShowMs); break;
                case "watchenabled": WatchEnabled = ParseBool(val, WatchEnabled); break;
                case "hideonfullscreen": HideOnFullScreen = ParseBool(val, HideOnFullScreen); break;
                case "hotkeysenabled": HotkeysEnabled = ParseBool(val, HotkeysEnabled); break;
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("# 晾晒工具配置");
                sb.AppendLine("MaxItems=" + MaxItems);
                sb.AppendLine("SaveToDisk=" + SaveToDisk);
                sb.AppendLine("SaveFolder=" + SaveFolder);
                sb.AppendLine("AutoShowMs=" + AutoShowMs);
                sb.AppendLine("WatchEnabled=" + WatchEnabled);
                sb.AppendLine("HideOnFullScreen=" + HideOnFullScreen);
                sb.AppendLine("HotkeysEnabled=" + HotkeysEnabled);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 保存失败不影响使用
            }
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }

        private static bool ParseBool(string s, bool fallback)
        {
            if (string.IsNullOrEmpty(s)) { return fallback; }
            switch (s.Trim().ToLowerInvariant())
            {
                case "1": case "true": case "yes": case "on": return true;
                case "0": case "false": case "no": case "off": return false;
                default: return fallback;
            }
        }
    }

    /// <summary>开机自启（写 HKCU 的 Run 项，不需要管理员权限）。</summary>
    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Liangshai";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) { return false; }
                    return k.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }

        public static void Set(bool enabled)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) { return; }
                    if (enabled)
                    {
                        string exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                        k.SetValue(ValueName, "\"" + exe + "\"");
                    }
                    else
                    {
                        if (k.GetValue(ValueName) != null) { k.DeleteValue(ValueName, false); }
                    }
                }
            }
            catch
            {
                // 无权限时静默失败，不影响其他功能
            }
        }
    }
}

