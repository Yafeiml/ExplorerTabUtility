# 更新记录

## v2.6.1

发布日期：2026-10-25。

### 开机自启动修复

- 添加自启动时创建缺失的 `Run` 和 `StartupApproved\Run` 子键，避免静默跳过写入。
- 新写入的命令给完整程序路径加引号，同时识别旧的未引号路径、带引号路径及大小写差异；不将其他程序或附带参数的命令误认为自身。
- 保留任务管理器中的禁用状态语义；删除启动项不会新建子键或删除其他程序的值。
- 安装器写入的启动命令同步加引号。更新检查和安装器支持链接改用本维护仓库。
- 关联 [issue #1](https://github.com/Yafeiml/ExplorerTabUtility/issues/1)：已复现并修复启动注册边界问题；截图所示“已启用却未启动”的现场原因仍需登录后进程状态和错误日志确认。

### Shell 与长期使用稳定性

- Shell COM、事件注册、异步轮询与快捷键操作统一到现有 WPF STA 消息循环；初始化失败清理后重试，成功后才停止探测。移除没有消息循环的自建 STA 调度器。
- 导航成功、失败和超时均解绑临时事件，释放短期 Document、Folder、FolderItem 和辅助功能 COM 引用；不提前强制释放仍被异步回调使用的共享 InternetExplorer RCW。
- 窗口关闭、快速开关窗口、Explorer 重启及旧异步回调使用一致的清理与代次检查。进程扫描和释放同步，释放后的排队事件不再投递。
- 移除提前隐藏未知窗口的全局显示钩子及 `seenBefore` Shell 属性；仅处理已确认的 Explorer 源窗口，失败或退出时恢复原位置和样式。
- 不再修改 `WS_EX_LAYERED` 或窗口透明度，移除主题问题开关并兼容旧 JSON 字段。
- 仅在目标标签导航到预期位置并确认有效后关闭源窗口；无有效目标或转换失败时保留源窗口。关闭历史上限为 100 条。
- 托盘恢复改为异步等待，隐藏图标时不会阻塞 UI 约 7 秒；重新显示图标或释放后停止恢复循环。

### 依赖与发布

- ConfigureAwait.Fody 3.3.2 → 3.4.1，Fody 6.9.2 → 6.9.3。
- System.Text.Json 10.0.0 → 10.0.12，Markdig 0.40.0 → 1.4.0；验证原 updater DLL 的 Markdown 渲染兼容性。
- net481 使用框架自带 System.Net.Http，移除旧包带入的 BCL 依赖。
- 升级现有 GitHub Actions，增加发布前回归测试门槛。维护仓库提供不依赖上游 SignPath 的 ZIP 草稿发布流程；公开发布由维护者确认执行。
- 本次只发布 GitHub，不自动向 Chocolatey 或 WinGet 推送。发布 ZIP 未做代码签名，校验值以 Release 附件 `SHA256SUMS.txt` 为准。

### 验证与使用

- 自启动回归覆盖两个目标框架的 11 个隔离测试：缺键、引号、旧命令、大小写、不同程序/参数、禁用状态和删除行为。测试不修改真实启动项。
- STA 轮询、1000 次测试窗口隐藏/恢复、目标窗口回退、关闭历史上限、释放后回调抑制，以及托盘 Dispatcher 响应/释放中断回归。
- 两个目标框架的依赖冒烟测试验证中文 Markdown、标题、加粗、链接、表格、代码块与 HttpClient。
- 本机更新入口为 PowerShell 7 执行 `& .\scripts\Update-Local.ps1`：先从托盘退出旧版本，脚本暂存并核对文件散列、保留旧安装用于回滚；随后启动安装目录中的程序。默认目录为 `D:\app\ExplorerTabUtility`。
- `scripts/Collect-Resources.ps1` 可按同一 PID 记录内存、句柄、GDI/USER 趋势，不改变进程或设置。

### 兼容性与验证边界

- 本机菜单对照中，退出 ETU 及关闭 StartAllBack 亚克力菜单皮肤均未消除残影；使用原生任务栏并运行 ETU 后残影消失。这指向当前增强任务栏菜单路径，尚无具体绘制缺陷或长期泄漏的调用栈证据。
- 本版本保留现有 StartAllBack 文件和设置；不会以 ETU 修复替代第三方菜单修复。
- 新窗口转换时可能短暂闪现；没有做真实注销/登录自动测试、长期 Shell COM 压力测试或 x86/ARM64 原生运行验证。

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
