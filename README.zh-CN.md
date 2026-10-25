# Explorer Tab Utility 中文维护版

[English](README.md) | [简体中文](README.zh-CN.md)

> 让 Windows 11 文件资源管理器的新窗口尽量以标签页方式打开，减少桌面窗口堆叠，让文件管理更清爽。

## 项目说明

本仓库是基于原开源项目 [w4po/ExplorerTabUtility](https://github.com/w4po/ExplorerTabUtility) 的维护版本。原项目用于增强 Windows 11 文件资源管理器标签页体验，支持将新窗口自动转为标签页、复用已有标签页、快捷键操作、标签搜索、恢复已关闭位置等功能。

本维护版主要补充了：

- 修复关机、注销或系统会话结束时可能弹出 `ExplorerTabUtility.exe` 应用程序错误的问题。
- 修复缺少注册表子键时无法添加开机自启动，以及带引号的启动路径被误判为未启用的问题。
- 修复 Shell 初始化、COM 生命周期和托盘恢复时的线程问题，隐藏托盘图标时不再阻塞界面。
- 将主运行目标升级到 `.NET 10`，并继续保留 `.NET Framework 4.8.1` 备用构建。
- 增加简体中文界面资源，系统 UI 语言为简体中文时自动显示中文。
- 补充中文说明、更新记录和致谢，便于中文用户安装、排查和二次维护。

## 系统要求

- Windows 11 22H2 Build 22621 或更新版本。
- 推荐使用 `.NET 10 Desktop Runtime`。
- 如果没有 .NET 10，也可以使用 `.NET Framework 4.8.1` 备用包。

## 下载与运行

从本仓库的 [Releases](https://github.com/Yafeiml/ExplorerTabUtility/releases) 下载与系统架构、运行时匹配的 ZIP。发布包未做代码签名，可用同页的 `SHA256SUMS.txt` 核对下载；构建源码会生成以下 x64 文件：

- `artifacts/ExplorerTabUtility_v2.6.1_x64_Net10.0_FrameworkDependent.zip`
- `artifacts/ExplorerTabUtility_v2.6.1_x64_NetFW4.8.1.zip`

推荐使用 `.NET 10 x64` 包。解压后运行：

```powershell
ExplorerTabUtility\ExplorerTabUtility.exe
```

如果之前安装过旧版本，请先卸载旧版本或退出托盘中的旧进程，再运行新版本。

本机 `D:\app\ExplorerTabUtility` 更新入口：先从托盘退出旧程序，然后在仓库根目录通过 PowerShell 7 执行 `& .\scripts\Update-Local.ps1`。可先加 `-WhatIf` 查看操作；脚本验证暂存文件的散列，保留完整旧安装用于回滚，用户 AppData 设置不变，更新后手动启动安装目录中的程序。该脚本只安装已构建的 `.NET 10 x64` 发布目录，不会编译、强制关闭进程或重启 Explorer。

v2.6.1 的窗口隐藏统一保留 Explorer 原有渲染样式，旧的“我遇到主题问题”开关不再需要。新窗口转成标签页时可能短暂显示；目标标签打开失败时会保留原窗口。关闭历史与持久化历史一致，保留最新 100 条。

开发验证需使用 Visual Studio 2026 的完整 MSBuild（项目包含 COM 类型库引用，`dotnet build` 不支持该任务）：构建 `Tests/ExplorerTabUtility.RegressionTests.csproj` 后运行生成的 DLL；构建 `Tests/StartupRegression/ExplorerTabUtility.StartupRegressionTests.csproj` 和 `Tests/DependencySmoke/ExplorerTabUtility.DependencySmokeTests.csproj` 后分别运行 `.NET 10` DLL 和 `net481` EXE。自启动测试使用唯一的临时 HKCU 子树，不修改真实启动项；托盘测试不创建真实托盘图标。部署脚本检查可执行 `& .\Tests\Update-Local.Tests.ps1`。

开机自启动实际在用户登录后触发。新增命令统一给程序路径加引号，同时兼容旧的未引号命令；任务管理器中禁用的条目仍按禁用处理。更新检查使用本维护仓库。

曾观察到 StartAllBack 增强任务栏菜单底部残影：退出 ETU、关闭亚克力菜单皮肤后仍存在，改用原生任务栏并运行 ETU 后消失。此兼容性现象与自启动修复分开处理，本版本不修改 StartAllBack，也不能保证修复其菜单绘制问题。

如果怀疑长时间运行后出现资源泄漏，可用 PowerShell 7 执行 `& .\scripts\Collect-Resources.ps1`。默认每 30 秒记录一次 ETU/Explorer 的 PID、启动时间、内存、句柄、GDI 和 USER 对象，共 60 分钟，CSV 保存在 `artifacts/diagnostics/`；`-DurationMinutes 0` 只记录一次。脚本不读取窗口标题或文件夹路径，不改变设置或结束进程。比较同一个 PID 在相近操作下的趋势；重启前后或不同程序版本的一次内存差异不足以证明泄漏。可用 `& .\Tests\Collect-Resources.Tests.ps1` 检查采集脚本。

## 主要功能

- 自动将新打开的文件资源管理器窗口重定向为标签页。
- 路径已打开时可复用已有标签页，避免重复打开。
- 支持快捷键打开指定位置、复制当前标签页、重新打开已关闭位置。
- 支持标签搜索弹窗，在打开过的位置之间快速切换。
- 支持恢复之前打开的窗口历史。
- 支持键盘、鼠标快捷键配置。
- 支持隐藏托盘图标，并通过快捷键重新显示应用。

## 本维护版修复了什么

### 关机时报 `0xe0434352` 应用程序错误

旧版本在系统关机、注销或 Explorer 进程结束时，可能出现类似下面的错误：

```text
SystemResourceNotifyWindow: ExplorerTabUtility.exe - 应用程序错误
应用程序发生异常 未知的软件异常 (0xe0434352)
```

事件日志中的真实异常是 `.NET ObjectDisposedException`：应用退出过程中 `HookManager.Dispose()` 可能被 `SessionEnding` 和 `Application.Exit` 两条路径重复调用，第二次释放 `StaTaskScheduler` 时又对已经释放的 `BlockingCollection` 调用 `CompleteAdding()`，导致未处理异常。

本维护版修复方式：

- v2.6.0 将 `StaTaskScheduler.Dispose()` 改为幂等；v2.6.1 已移除该调度器，改用现有 WPF STA 消息循环。
- `HookManager.Dispose()` 改为只执行一次，并注销 `SessionEnding` 事件。
- `ExplorerWatcher` 的 Shell、COM、STA 资源释放改为幂等，避免关机时重复清理导致崩溃。
- 系统关机或显式退出时不再把窗口关闭动作强制改成“隐藏到托盘”。
- 退出前会保存快捷键配置，减少会话结束时的状态丢失。

## 本维护版更新了什么

详见 [CHANGELOG.zh-CN.md](CHANGELOG.zh-CN.md)。

## 致谢

详见 [ACKNOWLEDGEMENTS.zh-CN.md](ACKNOWLEDGEMENTS.zh-CN.md)。

## 开源说明

本项目遵循 MIT License。发布到你自己的 GitHub 时，请保留：

- 原项目许可证 `LICENSE`
- 原作者和原项目链接
- 本维护版的修改说明和更新记录

如果要作为你自己的长期维护版本发布，建议在 GitHub 仓库描述中明确写明“基于 w4po/ExplorerTabUtility 的维护版”，这样对用户和原作者都更清晰。
