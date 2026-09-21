# VideoEnhancer Releases

VideoEnhancer 的公开更新源，仅用于分发插件运行文件，不包含模型、Python 环境或 PotPlayer。

- 版本检查以 GitHub `maxzrb/VideoEnhancer` 的 Release 为首选标准；GitHub 不可达时本数据集提供 `stable.json` 兜底，更新包下载默认优先使用本数据集，失败时回退 GitHub Release。
- `stable.json`：稳定通道结构化更新清单（与 GitHub Release 附带的清单资产内容一致）。
- `releases/<version>/VideoEnhancer-<version>-win-x64.exe`：经大小与 SHA-256 校验的 WiX Burn 安装器，内嵌标准 MSI、插件 DLL、纯运行 EXE 和独立组件。
- 插件把当前 3FUI 根目录传给安装器并退出 3FUI；用户确认后由 MSI 完成升级、旧布局迁移和旧配置残留清理，安装完成后手动重新启动 3FUI。

项目采用独立 SemVer；上游版本仅作为同步基线记录，不参与自动更新比较。
