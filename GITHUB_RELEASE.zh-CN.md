# ExplorerTabUtility v2.6.0 中文维护版

这是基于 [w4po/ExplorerTabUtility](https://github.com/w4po/ExplorerTabUtility) 的维护版本，重点修复关机时报错问题，并升级到 .NET 10，同时增加简体中文界面支持。

## 推荐下载

如果你已经安装 `.NET 10 Desktop Runtime`，推荐下载：

- `ExplorerTabUtility_v2.6.0_x64_Net10.0_FrameworkDependent.zip`

如果你的环境暂时无法使用 .NET 10，可以下载备用包：

- `ExplorerTabUtility_v2.6.0_x64_NetFW4.8.1.zip`

## 修复了什么

修复 Windows 关机、注销或系统会话结束时，可能弹出如下错误的问题：

```text
SystemResourceNotifyWindow: ExplorerTabUtility.exe - 应用程序错误
应用程序发生异常 未知的软件异常 (0xe0434352)
```

事件日志中的真实原因是 `.NET ObjectDisposedException`。应用退出过程中，`HookManager.Dispose()` 可能被 `SessionEnding` 和 `Application.Exit` 重复调用，第二次释放 `StaTaskScheduler` 时再次访问已经释放的 `BlockingCollection`，导致未处理异常。

本版本已将相关退出清理路径改为幂等：

- `StaTaskScheduler.Dispose()` 重复调用时安全返回。
- `HookManager.Dispose()` 只执行一次，并注销 `SessionEnding` 事件。
- `ExplorerWatcher` 的 Shell、COM、STA 资源清理支持重复调用。
- 系统关机或显式退出时不再把关闭动作误处理为“隐藏到托盘”。
- 退出前保存快捷键配置。

## 更新了什么

- 主运行目标升级到 `.NET 10`。
- 保留 `.NET Framework 4.8.1` 备用构建。
- `System.Text.Json` 升级到 `10.0.0`。
- GitHub Actions 构建矩阵更新为 `net10.0-windows`。
- Inno Setup 安装脚本更新为检测和安装 `.NET 10 Desktop Runtime`。
- 发布包命名更新为 `Net10.0_FrameworkDependent`。

## 新增内容

- 增加简体中文界面资源。
- 系统 UI 语言为 `zh-Hans`、`zh-CN`、`zh-SG`、`zh-MY` 时自动加载中文。
- 主窗口、托盘菜单、快捷键配置、Tab 搜索、确认弹窗、About 页面和枚举下拉项均接入本地化。
- 新增中文 README、中文更新记录和中文致谢。

## 校验值

- `ExplorerTabUtility_v2.6.0_x64_Net10.0_FrameworkDependent.zip`
  - SHA256: `60E39AC5659A82BB542AC6B40174A8565900C7F1343B6244167F837CDD0DDC38`
- `ExplorerTabUtility_v2.6.0_x64_NetFW4.8.1.zip`
  - SHA256: `AC1981BB4E4EA2BCC684F674C88D49D9ECE4AC8D85722517A2E5B51E1526F31D`

## 致谢

感谢原作者 **w4po / Abd-Alrahman Ghanem** 创建并维护 Explorer Tab Utility。这个维护版保留原项目 MIT License，并在原架构基础上进行小范围修复、本地化和运行时升级。

也感谢以下开源项目：

- H.Hooks
- Hardcodet.NotifyIcon.Wpf
- Autoupdater.NET
- Autoupdater.NET.Extended.Markdown
- Fody / ConfigureAwait.Fody
- .NET / WPF / Windows Shell COM APIs
