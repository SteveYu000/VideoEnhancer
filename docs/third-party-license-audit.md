# 第三方许可与分发审查（2026-10-05）

本次保留原创 C#/VB 前端与安装逻辑的 MIT 许可，按用户确认采用 LakeUI 赞助许可证。RVE 相关 Python 内容单列 AGPL-3.0-only，第三方程序、DLL 和运行库保留原有许可。完整范围见根目录 [LICENSE-SCOPE.md](../LICENSE-SCOPE.md)；本报告记录分发依据和验收范围，后续操作状态只记入 `docs/codex/STATUS.md`。

## 本体直接引用或分发的组件

| 组件 | 版本/来源 | 许可与处理 |
| --- | --- | --- |
| LakeUI | [5.110.0](https://www.nuget.org/packages/LakeUI/5.110.0)；[上游收费标准](https://github.com/Lake1059/LakeUI#收费标准) | 用户已确认取得本项目赞助许可；不修改上游 GPL，不公开私密凭据，不转授其他发布者。NuGet 仅编译，运行由 3FUI 提供，包内没有 LakeUI.dll。 |
| FFF.Native | [FFF_Project 2026.8.19](https://github.com/Lake1059/FFF_Project/releases/tag/2026.8.19)，提交 `c43614ca5c77f40cb3cb2b2254a54bd4fea2e614` | MIT；官方容器与十个 DLL 的 SHA256 锁定在 `cli/third-party/fff-native/runtime.json`。仅解析容器，不启动或分发播放器及其 LakeUI/FFmpeg。真实 DLL API 为 11；2026.8.20 发布二进制实际为 API 12，故未采用。 |
| FFF 字幕/字体动态依赖 | vcpkg 基线 `e03dc9b29710050cd1018bc5674688108658d327`；FriBidi 1.0.16 | libass 0.17.4 ISC、Brotli 1.2.0 MIT、bzip2 1.0.8 bzip2-1.0.6、FreeType 2.14.3 选择 FTL、HarfBuzz 14.2.0 MIT、libpng 1.6.58 libpng-2.0、zlib 1.3.2 Zlib；FriBidi LGPL-2.1-or-later。保留各全文与 FreeType 署名，独立 DLL 可替换，不禁止调试 LGPL 修改所需的逆向工程。 |
| aria2-next | [v2.8.3](https://github.com/AnInsomniacy/aria2-next/releases/tag/v2.8.3)，提交 `f58a2d9463b3b549ca19039b055df1448e1b8c46` | GPL-2.0-or-later，未修改官方二进制，独立进程调用。保留 GPL、AUTHORS、SOURCE、静态依赖声明。源码包含 FFmpeg、GPAC、OpenSSL、libtorrent、Boost、curl 等 `third_party` 目录及构建脚本。 |
| 7za | [7-Zip 26.03](https://github.com/ip7z/7zip/releases/tag/26.03) Extra x64 | LGPL-2.1-or-later 与 BSD 部分；保留官方 License.txt 和精确源码 SHA256。所有归档预检/解压/7z 创建统一调用独立 7za，完整移除 SharpCompress。 |
| .NET 自包含运行库 | 实际 SDK 解析的 runtime pack；本轮 10.0.12 | MIT 及其第三方许可；构建从所用 pack 自动复制并内嵌 LICENSE.TXT、THIRD-PARTY-NOTICES.TXT，生成版本和源码说明，避免运行库变化后沿用旧声明。 |
| WiX Burn 与标准界面 | [v6.0.2](https://github.com/wixtoolset/wix/tree/v6.0.2)，提交 `b3f340393117094a75ea8ced77f2357e4aa095e7` | MS-RL；安装器保留许可与源码获取方式，独立源码包提供整个对应归档和上游构建脚本。官方预编译构建工具还有 OSMFEULA 维护费协议：非营收用途豁免，营收用途由发布者核对维护费/支持协议；不能据此声称 WiX 引擎为 MIT。 |
| RVE 集成 Python | [REAL-Video-Enhancer](https://github.com/TNTwise/REAL-Video-Enhancer/blob/v2-main/LICENSE) | 上游 AGPL v3；本项目 `cli/embedded-tools/*.py` 及 `Program.cs` 内改写 RVE 的 Python 片段按 AGPL-3.0-only 提供，并保留完整源码。C#/VB 原创部分继续 MIT，后端通过独立 Python 进程运行。 |

各 DLL/工具的校验值、许可原文与重建路径集中在 `cli/third-party/`。FFF 原生 MIT 许可的上游模板版权占位符按原文保留，同时在来源说明中明确作者 Lake1059；没有替上游捏造版权年份或名称。

## 源码与二进制分开打包

`dotnet publish VideoEnhancer.slnx -c Release` 生成运行 EXE、`VideoEnhancer.zip`、`VideoEnhancerInstaller.exe` 和独立 `VideoEnhancer-Source.zip`。安装包与二进制 ZIP 包含运行文件、许可证和来源说明；没有第三方源码归档。运行 EXE 中原先的 7-Zip 源码资源也已移除。Python 集成脚本文本属于运行所需资源，仍随 CLI 释放供 Python 执行，其可修改源码也在独立源码包中。

独立源码包包含 `VideoEnhancer/` 下的可重建项目源码，以及 `third-party/aria2-next`、`7zip`、`fff-native`、`wix` 下固定 SHA256 的对应归档。其中包括 FriBidi 源码、vcpkg 基线与补丁。发布脚本把源码包命名为 `VideoEnhancer-<version>-source.zip`，与二进制在同一次 GitHub Release 和 ModelScope 镜像上传，不依赖第三方主页长期存活。已发布的 1.3.12 仍保留独立 aria2 对应源码资产，不覆盖旧发行。

## 可选后端与模型

本体 ZIP/安装器不携带完整 RVE/Python/PyTorch、FFmpeg、RTX SDK、模型权重或 mkvtoolnix。它们由独立资源仓库按需下载，不能套用本体 MIT。FFmpeg 按实际构建采用 LGPL/GPL，RTX 的 NVIDIA DLL 保留专有 SDK 条款；模型权重应按每个作者的实际许可核对，已有未确认项目仍保留 README 的状态标记。

本轮没有修改或发布这些外部二进制/权重，没有把未核查权重标为已授权，也没有将整个 Python 环境重新许可为 MIT。独立资源发布者仍应为每个实际包保留许可、版权和所需完整对应源码。

## issue #6 的核查依据

采用 [issue #6](https://github.com/maxzrb/VideoEnhancer/issues/6) 的独立组件方案。原内嵌 aria2 已在主线移除，1.3.12 的 Release ZIP 已包含 GPL/作者/来源/依赖声明；其源码资产实下载 SHA256 为 `420E31256B5E29DE6AB9B295423ED29431494527B4FDC9AFB3946DFF4A73EAF7`。归档实检含完整 `third_party` 依赖和 `.github/workflows/release.yml` 等构建材料。

本轮进一步统一许可边界并验证二进制 ZIP 与真实安装载荷逐文件一致。下载检查运行真实独立 aria2，涵盖公共下载、已有文件续传的 Range、HTTP 失败及再次下载、哈希错误阻止后端解压安装。私有 ModelScope 分支使用生产客户端与本机 HTTP 服务、虚构令牌检查认证、401、临时文件原子替换及拒绝自动重定向；这属于协议回归，没有登录实际私有数据集。

下载回归同时修复两项实际发现：新版 aria2 续传 SQLite 状态转入插件缓存；HTTP/HTTPS 分支限制无关 BitTorrent 公网监听并关闭 DHT/发现/端口映射，避免监听阻塞拖住进程退出。不修改官方 GPL 二进制。

验收脚本：`release/test-native-archives.ps1`（40 项，包括路径越界、链接、重解析点、加密、CRC 与取消），`test-third-party-package.ps1`（110 项含实际 API 11、安装一致性与源码分离），`test-download-integration.ps1`（8 项协议场景）。既有安装/自更新/回滚、后端更新 6 项和发布门禁 5 项通过。未完成宿主 GPU 播放与所有模型的实际运行测试，不将 DLL 加载验收扩大为完整预览验收。
