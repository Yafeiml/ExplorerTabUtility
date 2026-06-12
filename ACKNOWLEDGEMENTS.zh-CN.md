# 致谢

## 原项目与作者

本维护版基于 [w4po/ExplorerTabUtility](https://github.com/w4po/ExplorerTabUtility)。

感谢原作者 **w4po / Abd-Alrahman Ghanem** 创建并维护 Explorer Tab Utility。这个项目解决了 Windows 11 文件资源管理器标签页体验中的一个实际痛点：新窗口太容易堆满桌面，而标签页才更符合现代文件管理工作流。

如果这个维护版对你有帮助，也建议支持原项目：

- GitHub: <https://github.com/w4po/ExplorerTabUtility>
- GitHub Sponsors: <https://github.com/sponsors/w4po>

## 开源依赖

感谢以下开源项目和库：

- [H.Hooks](https://github.com/HavenDV/H.Hooks)：键盘和鼠标底层钩子支持。
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon)：WPF 系统托盘图标支持。
- [Autoupdater.NET](https://github.com/ravibpatel/AutoUpdater.NET)：应用更新检查和更新信息解析。
- [Autoupdater.NET.Extended.Markdown](https://www.nuget.org/packages/Autoupdater.NET.Extended.Markdown)：Markdown 更新日志展示支持。
- [Fody](https://github.com/Fody/Fody) 和 ConfigureAwait.Fody：构建期代码织入支持。
- .NET / WPF / Windows Shell COM APIs：应用运行和资源管理器交互的基础能力。

## 用户反馈

感谢实际使用中反馈问题的用户。此次维护版的核心修复来自真实关机场景下的崩溃现象：

- 关机时出现 `SystemResourceNotifyWindow: ExplorerTabUtility.exe` 应用程序错误。
- Windows 事件日志显示 `.NET ObjectDisposedException`。
- 堆栈指向 `StaTaskScheduler.Dispose()` 和 `ExplorerWatcher.DisposeShellObjects()` 的重复释放路径。

这些信息帮助定位到真实原因，而不是只停留在 `0xe0434352` 这种泛化错误码上。

## 维护版说明

本维护版的重点不是重写原项目，而是在尽量尊重原架构的基础上做小范围修复和本地化：

- 保留原功能和交互习惯。
- 修复退出和关机时的资源释放问题。
- 升级到 `.NET 10`。
- 增加简体中文界面资源。
- 补充中文说明、更新记录和致谢。

发布到个人 GitHub 时，请继续保留原作者、原项目链接和 MIT License。
