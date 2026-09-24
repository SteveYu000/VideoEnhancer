# VideoEnhancer Releases

VideoEnhancer 的公开更新源，仅用于分发插件运行文件，不包含模型、Python 环境或 PotPlayer。

- 版本检查以 GitHub `maxzrb/VideoEnhancer` 的 Release 为首选标准；GitHub 不可达时本数据集提供 `stable.json` 兜底，更新包下载默认优先使用本数据集，失败时回退 GitHub Release。
- `stable.json`：稳定通道结构化更新清单（与 GitHub Release 附带的清单资产内容一致）。
- `releases/<version>/VideoEnhancer-<version>-win-x64.exe`：经大小与 SHA-256 校验的运行时更新包，内嵌插件 DLL。
- `releases/<version>/VideoEnhancerInstaller-<version>-win-x64.exe`：首次安装用的便携安装器，用户选择 3FUI 根目录后复制插件及独立组件。
- 插件自身下载运行时更新包，等待 3FUI 退出后由 `videoenhancer.exe` 替换 EXE/DLL 并重启宿主；更新不依赖安装器。

项目采用独立 SemVer；上游版本仅作为同步基线记录，不参与自动更新比较。
