## Snapline · 晾晒工具 v1.0.0

截图不再堆在桌面上 —— 挂到屏幕顶端的晾衣绳上。
*Stop screenshots piling up on your desktop.*

![截图挂在屏幕顶端的晾衣绳上](https://raw.githubusercontent.com/yi-yezhiqiu/snapline/main/docs-line.png)

### 下载 / Download

下载下面的 **`Snapline.exe`**，双击即用。（中文名是「晾晒工具」，GitHub 附件名只支持 ASCII，所以文件名叫 Snapline.exe）

- 单文件 **103 KB**，**零依赖** —— 不需要装 .NET、Python 或任何运行环境
- 编译目标 .NET Framework 4.8，Windows 10 / 11 自带
- 首次运行会弹出一个控制窗口，点「设置…」可以勾上开机自启

> 无需安装，也没有安装包。想卸载就直接删掉 exe，
> 再顺手删掉 `%APPDATA%\Liangshai`（配置）即可。

---

### 这一版有什么

- **微信 `Alt+A` / `PrtScn` / `Win+Shift+S` 都能接住** —— 只要图进了剪贴板就会挂上绳子
- **界面只有一根细线**：没有背景板、没有标题栏，不挡你看东西
- **点击穿透**：绳子以外的地方鼠标照常操作下层窗口，只有照片可点
- **完全离线**：不联网、无账号、无遥测
- **全屏应用时自动隐藏**：玩游戏、看视频、做演示时不会冒出来
- 照片按抛物线居中排开，带金属小夹子和玻璃边框，每张轻微歪斜、偶尔随风摆一下

### 怎么用

| 我想… | 操作 |
|---|---|
| 看绳子上的图 | 鼠标移到**屏幕最上边缘**停约 0.25 秒 |
| 把图粘到聊天框 | 照片上**点一下** → 去聊天框 `Ctrl+V` |
| 看大图 | **双击**照片 |
| 拖到微信 / Word | 按住照片**往外拖** |
| 另存为文件 | 照片上**点右键** |
| 丢掉一张 | 鼠标移上去，**左上角**出现 ✕ |
| 显示 / 隐藏绳子 | `Ctrl + Alt + T` |
| 复制最近一张 | `Ctrl + Alt + C` |
| **退出程序** | 控制窗口的「退出晾晒工具」按钮，或 `晾晒工具.exe /quit` |

### 已知限制

- **没有标注功能**。原版长按调起 macOS 的 Markup 标注器，Windows 没有对应组件。
  建议直接用微信截图自带的编辑（`Alt+A` 之后就能画框、打码、写字）。
- **分不清剪贴板图片的来源**。从网页或 Word 复制的图也会上绳，误收的点 ✕ 丢掉即可。
- **从绳子取图会覆盖你当前的剪贴板** —— 这是「点一下就粘」的必然代价。
- `Ctrl+Alt+T` / `Ctrl+Alt+C` 通过系统 `RegisterHotKey` 注册（注册返回成功），
  但**未在自动化环境中验证按键效果**（测试环境会拦截合成按键）。如果按了没反应，
  多半是该组合被别的软件占用；鼠标碰屏幕上边缘和托盘/控制窗口不受影响。
- **仍待实测**：双击预览、右键菜单、拖到其他程序、托盘菜单各项。这些需要真实鼠标交互，
  自动化环境无法覆盖。

### 从源码编译

只需要 Windows 自带的 C# 编译器，**不需要 Visual Studio，也不需要 .NET SDK**：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

产物在 `dist\晾晒工具.exe`（发布时重命名为 `Snapline.exe`）。

---

### English

Download **`Snapline.exe`** below and double-click it. A single 103 KB file with
**zero dependencies** — no .NET install, no Python, no runtime. Compiled against the
.NET Framework 4.8 that ships with Windows 10 / 11.

Take a screenshot the way you always do; it never touches your desktop. Instead it hangs on
a clothesline along the top edge of the screen. Move the pointer to the top edge and the line
slides down. Click a photo to put it back on your clipboard, then paste as usual.

Build from source with `build.ps1` — only the in-box `csc.exe` is needed.

Inspired by [Tendedero](https://github.com/alejandrobujan/tendedero) for macOS (MIT).
This is a from-scratch C# / WPF reimplementation sharing no source code; the original's
name and icon are not covered by its license, so this port ships its own.


