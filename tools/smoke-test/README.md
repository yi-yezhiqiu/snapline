# 自动化冒烟测试

这套工具用来在**无人值守**的情况下验证「晾晒工具」的核心链路是否正常：
模拟一次微信截图（把图片放进剪贴板）→ 抓屏 → 检查绳子有没有挂上图并滑出来。

## 为什么需要它

晾晒工具的核心行为都是 GUI 交互，肉眼看屏幕才能确认。这个探针把「截图 → 捕获 → 挂上绳子 → 滑出」
整条链路跑一遍并留下截图证据，改动代码后可以快速回归。

## 文件

| 文件 | 作用 |
|---|---|
| `Probe.cs` | 主冒烟测试：启动应用 → 造测试图 → 放进剪贴板 → 抓屏 → 还原剪贴板 |
| `HotkeyProbe.cs` | 热键探针：枚举应用窗口、投递 `WM_HOTKEY` |
| `HotkeyInjectProbe.cs` | 独立判定探针：自己注册一个全局热键，测试合成按键能否触发它 |

## 编译

用到系统自带的编译器，不需要装任何东西：

```powershell
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
& "$fw\csc.exe" /nologo /target:exe /platform:anycpu /out:probe.exe `
  "/r:$fw\System.dll" "/r:$fw\System.Drawing.dll" "/r:$fw\System.Windows.Forms.dll" `
  Probe.cs
```

## 运行

```powershell
.\probe.exe
```

**注意**：必须在**同一个 PowerShell 会话**里运行，因为应用进程会随父进程退出而被回收。
如果应用已经在跑，探针会直接复用它。

产物落在 `..\..\.probe\`：

| 文件 | 说明 |
|---|---|
| `probe.log` | 每一步的时间戳与结果 |
| `screen-after-capture.png` | 截图进剪贴板 1.2 秒后的全屏截图 |
| `top-after-capture.png` | 顶部 250px 裁切（用来看绳子） |
| `clip-test.png` | 造的测试图 |

## 判定标准

`top-after-capture.png` 里应该能看到：木色面板、麻绳、晾衣夹、以及那张写着
`CLIPBOARD TEST` 的黄色/蓝色测试图挂在绳子上。

同时 `%TEMP%\liangshai-trace.log` 里应该出现：

```
[clip] 抓到图片 8935 字节
[app] OnImageCaptured 8935 字节
[app] 已挂上绳子，共 1 张；AutoShowMs=2000
[app] 已请求滑出绳子
```

## 已知的环境限制

在**自动化 / 沙箱会话**里跑时：

- **合成按键（`keybd_event` / `SendInput`）无法触发全局热键**。
  `HotkeyInjectProbe.cs` 可以独立证明这一点：它自己注册 `Ctrl+Alt+F9` 成功
  （重复注册会正确报 `ERROR_HOTKEY_ALREADY_REGISTERED`），但注入按键后收不到 `WM_HOTKEY`。
  **这是环境限制，不是程序缺陷**——真实桌面会话里热键正常工作。
- **子进程会随父 PowerShell 退出而被回收**，所以应用和探针必须在同一个会话里启动。
