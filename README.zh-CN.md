# Explorer Tab Utility 中文维护版

[English](README.md) | [简体中文](README.zh-CN.md)

> 让 Windows 11 文件资源管理器的新窗口尽量以标签页方式打开，减少桌面窗口堆叠，让文件管理更清爽。

## 项目说明

本仓库是基于原开源项目 [w4po/ExplorerTabUtility](https://github.com/w4po/ExplorerTabUtility) 的维护版本。原项目用于增强 Windows 11 文件资源管理器标签页体验，支持将新窗口自动转为标签页、复用已有标签页、快捷键操作、标签搜索、恢复已关闭位置等功能。

本维护版主要补充了：

- 修复关机、注销或系统会话结束时可能弹出 `ExplorerTabUtility.exe` 应用程序错误的问题。
- 将主运行目标升级到 `.NET 10`，并继续保留 `.NET Framework 4.8.1` 备用构建。
- 增加简体中文界面资源，系统 UI 语言为简体中文时自动显示中文。
- 补充中文说明、更新记录和致谢，便于中文用户安装、排查和二次维护。

## 系统要求

- Windows 11 22H2 Build 22621 或更新版本。
- 推荐使用 `.NET 10 Desktop Runtime`。
- 如果没有 .NET 10，也可以使用 `.NET Framework 4.8.1` 备用包。

## 下载与运行

当前本地已生成的发布包位于：

- `artifacts/ExplorerTabUtility_v2.6.0_x64_Net10.0_FrameworkDependent.zip`
- `artifacts/ExplorerTabUtility_v2.6.0_x64_NetFW4.8.1.zip`

推荐使用 `.NET 10 x64` 包。解压后运行：

```powershell
ExplorerTabUtility\ExplorerTabUtility.exe
```

如果之前安装过旧版本，请先卸载旧版本或退出托盘中的旧进程，再运行新版本。

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

- `StaTaskScheduler.Dispose()` 改为幂等，重复释放直接返回。
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
