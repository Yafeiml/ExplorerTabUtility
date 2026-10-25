# ExplorerTabUtility v2.6.1 中文维护版

合并开机自启动、Shell/COM 生命周期、窗口转换和托盘响应修复。基于 [w4po/ExplorerTabUtility](https://github.com/w4po/ExplorerTabUtility)，保留 MIT 许可证及原作者致谢。

## 下载

- Windows x64 且已安装 .NET 10 Desktop Runtime：`ExplorerTabUtility_v2.6.1_x64_Net10.0_FrameworkDependent.zip`。
- 使用 .NET Framework 4.8.1：`ExplorerTabUtility_v2.6.1_x64_NetFW4.8.1.zip`。
- x86、ARM64 使用文件名中对应架构的 ZIP。

完整解压后运行 `ExplorerTabUtility\ExplorerTabUtility.exe`。先退出旧版托盘进程再替换文件，用户 AppData 设置继续保留。ZIP 未做代码签名，下载后可对照附件 `SHA256SUMS.txt` 检查 SHA256。

## 修复

- 创建缺失的开机启动注册表子键，新命令正确引用完整程序路径，兼容旧的未引号/带引号命令，保持任务管理器禁用状态和其他启动项不变。对应 [issue #1](https://github.com/Yafeiml/ExplorerTabUtility/issues/1) 的已确认注册缺陷；报告者登录后的具体失败原因尚待日志确认。
- Shell 初始化、COM 访问及异步回调统一到 WPF STA；初始化失败后清理重试，避免一次失败永久停止初始化。
- 清理临时导航事件和 COM 引用，限制关闭历史为 100 条，退出后不再处理排队回调。
- 窗口转换失败保留源窗口，成功确认目标位置后才关闭源窗口；隐藏/恢复保留 Explorer 原有渲染样式。
- 隐藏托盘图标后的恢复循环使用异步延迟，避免 UI 冻结约 7 秒。
- 更新源改为本维护仓库，并合并前轮依赖升级；详见 [更新记录](https://github.com/Yafeiml/ExplorerTabUtility/blob/main/CHANGELOG.zh-CN.md)。

## 验证与限制

新增两个框架的 11 项自启动隔离测试，以及托盘 Dispatcher 响应、恢复中断测试；同时保留窗口隐藏/恢复、历史上限、释放回调和依赖渲染回归。测试不写真实启动项或操作真实 Explorer/托盘。

尚未进行真实注销/登录验证、长期 Shell COM 压力测试或 x86/ARM64 原生运行验证。新窗口转换时可能短暂显示。StartAllBack 增强任务栏的独立菜单残影仍属于兼容性问题，本版本不更改该软件。
