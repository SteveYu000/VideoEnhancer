# 许可证适用范围

根目录 `LICENSE` 的 MIT 许可适用于 VideoEnhancer 原创 C#/VB 代码、文档及原创工具代码。它不覆盖第三方二进制、第三方源码、模型权重，也不替代依赖的原有许可。

`cli/embedded-tools/` 中与 RVE 结合运行的 Python 集成脚本，以及 `cli/Program.cs` 中用于修补 RVE 模块的 Python 代码片段，按 AGPL-3.0-only 提供，完整许可见 `cli/third-party/RVE/AGPL-3.0.txt`。这些 Python 内容及其修改源码随本仓库和独立源码包提供，运行时以独立 Python 进程执行；C#/VB 前端的原创部分继续采用 MIT。独立 Python 后端及其依赖的再分发需一并保留对应许可和完整源码获取方式。

插件通过已取得的 LakeUI 赞助许可证使用 LakeUI（维护者 2026-10-05 确认）。该授权不改变 LakeUI 本身的版权/公开 GPL-3.0 许可，也不随 MIT 源码自动转授；分叉项目公开分发 LakeUI 集成前需自行满足相应授权条件。分发包不附带 LakeUI.dll。

FFF.Native 为 MIT；其动态依赖（尤其 FriBidi 的 LGPL）保持各自许可，允许用户替换 DLL，不限制依法修改、调试或再分发这些组件。aria2-next 与 7za 均作为独立程序通过命令行调用，各自许可、版权和源码义务不受本项目 MIT 限制。详见 `cli/THIRD-PARTY-NOTICES.txt` 与 `cli/third-party/`。

WiX 首次安装器引擎及其标准界面按 MS-RL 提供，对应源码位于独立源码包；自有安装载荷仍为 MIT。.NET 自包含运行库保持其 MIT 及第三方组件许可，构建自动附带实际 runtime pack 的许可与依赖声明。二进制 ZIP/安装器包含许可证和来源说明，项目与第三方完整对应源码合并为一个同版本独立源码 ZIP，仅上传 GitHub Release；ModelScope 只镜像运行文件和更新清单。
