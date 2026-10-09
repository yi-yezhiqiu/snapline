using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

/// <summary>
/// 独立最小热键探针：自己注册 Ctrl+Alt+F9 并等待 WM_HOTKEY，
/// 用来判定"合成按键注入"在这个环境里到底能不能触发全局热键。
/// </summary>
internal static class HotkeyInjectProbe
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x7001;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint INPUT_KEYBOARD = 1;

    private static volatile bool _gotKeybd, _gotSendInput;
    private static IntPtr _hwnd;

    [STAThread]
    private static int Main()
    {
        var win = new HotkeyWindow();
        _hwnd = win.Handle;

        bool ok = RegisterHotKey(_hwnd, HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x78); // F9
        Console.WriteLine("外层 RegisterHotKey(Ctrl+Alt+F9) = " + ok +
                          " err=" + Marshal.GetLastWin32Error());
        if (!ok) { return 1; }

        // 验证 API 是真的：重复注册应该失败并报 1409
        bool dup = RegisterHotKey(_hwnd, HOTKEY_ID + 1, MOD_CONTROL | MOD_ALT, 0x78);
        Console.WriteLine("重复注册(应该失败) = " + dup +
                          " err=" + Marshal.GetLastWin32Error());

        Console.WriteLine("--- 方式 A: keybd_event ---");
        Press();
        Console.WriteLine("   keybd_event 触发 WM_HOTKEY = " + _gotKeybd);

        Console.WriteLine("--- 方式 B: SendInput ---");
        _pendingSendInput = true;
        SendKeyCombo();
        Console.WriteLine("   SendInput 触发 WM_HOTKEY = " + _gotSendInput);

        Console.WriteLine("结论: " + (_gotKeybd || _gotSendInput
            ? "合成按键可以触发全局热键 → 我的程序需要检查"
            : "合成按键无法触发全局热键 → 这是环境限制，不是程序缺陷"));
        return 0;
    }

    private static volatile bool _pendingSendInput;

    /// <summary>按下并释放 Ctrl+Alt+F9，然后泵消息让 WM_HOTKEY 到达。</summary>
    private static void Press()
    {
        keybd_event(0x11, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x78, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        keybd_event(0x78, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(0x12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(0x11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Pump();
    }

    /// <summary>泵消息队列，让投递到消息窗口的 WM_HOTKEY 真正被处理。</summary>
    private static void Pump()
    {
        for (int i = 0; i < 30; i++)
        {
            Application.DoEvents();
            Thread.Sleep(40);
        }
    }

    private static void SendKeyCombo()
    {
        var inputs = new INPUT[6];
        inputs[0] = Key(0x11, false); // Ctrl down
        inputs[1] = Key(0x12, false); // Alt down
        inputs[2] = Key(0x78, false); // F9 down
        inputs[3] = Key(0x78, true);
        inputs[4] = Key(0x12, true);
        inputs[5] = Key(0x11, true);
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        Pump();
    }

    private static INPUT Key(ushort vk, bool up)
    {
        var i = new INPUT { type = INPUT_KEYBOARD };
        i.u.ki.wVk = vk;
        i.u.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
        return i;
    }

    private sealed class HotkeyWindow : NativeWindow
    {
        public HotkeyWindow()
        {
            var cp = new CreateParams { Caption = "hotkey-inject-probe", Parent = new IntPtr(-3) };
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                if (_pendingSendInput) { _gotSendInput = true; }
                else { _gotKeybd = true; }
            }
            base.WndProc(ref m);
        }
    }
}
