# 更新记录

## v2.6.0

发布日期：2026-06-12

### 修复

- 修复 Windows 关机、注销或系统会话结束时，`ExplorerTabUtility.exe` 可能弹出 `0xe0434352` 应用程序错误的问题。
- 修复退出路径中 `HookManager.Dispose()`、`ExplorerWatcher.DisposeShellObjects()`、`StaTaskScheduler.Dispose()` 重复执行时可能抛出未处理异常的问题。
- 修复系统关机时主窗口 `Closing` 事件仍被当作普通关闭处理，导致应用尝试隐藏到托盘而不是正常退出的问题。
- 修复清理 Shell/COM/STA 资源时，Explorer 退出和应用退出竞态导致的二次释放风险。

### 更新

- 主运行目标从 `.NET 9` 升级到 `.NET 10`。
- 保留 `.NET Framework 4.8.1` 备用构建，方便没有 .NET 10 运行时的环境使用。
- `System.Text.Json` 升级到 `10.0.0`。
- 发布包命名更新为 `Net10.0_FrameworkDependent`。
- GitHub Actions release workflow 更新为 `.NET 10.x`。
- Inno Setup 安装脚本更新为检测、下载和安装 `.NET 10 Desktop Runtime`。
- README 中的运行时说明更新为 `.NET 10`。

### 新增

- 新增简体中文界面资源。
- 系统 UI 语言为 `zh-Hans`、`zh-CN`、`zh-SG`、`zh-MY` 时自动加载简体中文。
- 主窗口、偏好设置、托盘菜单、快捷键配置、Tab 搜索弹窗、确认弹窗、About 页面、枚举下拉项均接入本地化资源。
- 新增中文 README、中文更新记录和中文致谢文件。

### 构建产物

- `ExplorerTabUtility_v2.6.0_x64_Net10.0_FrameworkDependent.zip`
  - SHA256: `60E39AC5659A82BB542AC6B40174A8565900C7F1343B6244167F837CDD0DDC38`
- `ExplorerTabUtility_v2.6.0_x64_NetFW4.8.1.zip`
  - SHA256: `AC1981BB4E4EA2BCC684F674C88D49D9ECE4AC8D85722517A2E5B51E1526F31D`

### 已验证

- `Release` 构建通过：`net10.0-windows` 和 `net481`。
- x64 publish 通过。
- 发布 zip 内容检查通过。
- 简体中文资源字典解析通过。
- `ExplorerTabUtility.runtimeconfig.json` 确认为 `.NET 10`。

## v2.5.1

发布日期：2026-06-12

### 修复

- 初步修复关机路径中的重复释放问题。
- 增强 `StaTaskScheduler`、`HookManager`、`ExplorerWatcher` 的退出清理安全性。
- 保持旧的 `.NET 9` 发布目标，用于过渡验证。

## v2.5.0

原项目发布版本。详见原项目发布页：

<https://github.com/w4po/ExplorerTabUtility/releases>
