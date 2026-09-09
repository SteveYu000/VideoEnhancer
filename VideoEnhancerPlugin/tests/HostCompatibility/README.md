# 宿主兼容回归

使用真实宿主程序集，在独立测试进程中检查插件的二进制接口及队列行为。测试不启动真实编码任务，也不修改实际宿主队列或配置。

先用需要兼容的最低宿主版本构建插件，再将**同一个 DLL**分别放入旧、新宿主程序集环境执行，避免只在重新编译后验证而漏掉二进制兼容问题。

```powershell
./VideoEnhancerPlugin/build.ps1 -HostBin '<旧宿主程序集目录>' -SkipInstall
dotnet run --project VideoEnhancerPlugin/tests/HostCompatibility -- '<旧宿主程序集目录>' '<插件DLL绝对路径>'
dotnet run --no-build --project VideoEnhancerPlugin/tests/HostCompatibility -- '<新宿主程序集目录>' '<同一个插件DLL绝对路径>'
```

宿主程序集目录需要包含 `FFmpegFreeUI.dll`、`LakeUI.dll` 及其依赖，可使用官方源码构建输出或单文件宿主的解包目录。测试要求 Windows 和 .NET 10 SDK。成功时输出 `HOST_COMPATIBILITY_PASS`，失败时返回非零退出码。

覆盖范围：直接宿主/LakeUI 成员签名解析、四个组件按 ID 获取任务、预览活动任务枚举、真实宿主 JSON 事件序列化与订阅、进度回写、暂停/恢复共享内存、停止共享内存及手动停止标记、终态遥测清理。为避免触发自动调度，测试仅在自己的进程中向宿主私有队列及索引注入测试任务；这部分测试夹具随宿主内部结构变化可能需要更新。

2026-09-09 已验证同一 DLL 在 3FUI 6.2.3 / LakeUI 5.3 与 3FUI 6.2.16 / LakeUI 5.9 上通过，388 个直接成员引用均可解析。当前安装旧 DLL 在 6.2.16 上会因 `get_队列()` 返回类型变化而失败，确认测试能捕获原始问题。

测试不替代实机按钮交互、长视频处理、输出封装或 GPU 显存压力测试；停止用例只验证信号和标记，不启动或终止真实子进程。
