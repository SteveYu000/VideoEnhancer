# PR #10 / #11 审核记录

审核日期：2026-10-06；审核者：Codex。仅审核与隔离验证，未合并、提交、推送、部署或发布。

## 审核对象与结论

| 对象 | 固定提交 | 结论 |
| --- | --- | --- |
| [PR #10](https://github.com/maxzrb/VideoEnhancer/pull/10) | `ea3776f7898677ac61b1ba3b1f7aba71cc972eb9` | 未发现阻止合并的新缺陷；主要处理依赖、许可材料、打包及归档操作。 |
| [PR #11](https://github.com/maxzrb/VideoEnhancer/pull/11) | `6f0756bbcf188f7804e11f46fb0b7d662be43178` | 未发现阻止合并的新缺陷；预览异常修复有实际回调对照，性能结论需保留条件。 |

共同基线为 `main / origin/main f261e375eb1ca5fca286a9ac52aa9e816add540f`。本轮查询明确指定 `--repo maxzrb/VideoEnhancer`，避免 gh 默认选择 fork 的上游仓库。两个 PR 均为 OPEN，远端没有自动检查结果，因此以本轮本地检查为依据。

## UI 与 AVV3 结论

1. **预览异常确认修复。** 使用 PR #10 的插件作为未修复对照（它未修改预览回调），在 LakeUI 5.112 下实际调用 `PluginPanel.OnPreviewFrameReady`，复现与反馈一致的 `MissingMethodException: PixelPictureBox.set_Image`。同一回调换用 PR #11，在 LakeUI 5.110、5.112 下均通过赋图、替换、清空与图像所有权检查。兼容入口位于 `VideoEnhancerPlugin/PixelPreviewImage.vb:36`，实际回调位于 `VideoEnhancerPlugin/Pages/PluginPanel.PreviewPage.vb:301`。未做完整宿主窗口绘制验收。
2. **此前低倍率修复已经生效。** 基线 CLI 已按输出倍率构建并选择 TensorRT Engine；本机安装的转换脚本也已有 `FinalOutputScale`。PR #11 新增内嵌转换脚本并按内容同步到后端，补上后端重装/替换之后脚本可能丢失的保障。官方 AVV3 权重仍为 4x，2x/3x 仍为 4x 网络结果在 GPU 输出图内双三次缩小，并未换成独立训练的 2x/3x 网络。新增 FPS 公式纠正首条进度前帧数被计入分子的问题，只改变统计。
3. **不能据此宣布原来 9→3.9 FPS 的问题已完全解决。** 作者文档报告 RTX 4070 Laptop 上的 NVENC 与 SVT 参数对照；其中 SVT 的 `enable-cdef=1` 改为自动策略后吞吐提高。这些数据来自作者机器和参数，原始日志不在本仓库，本轮未独立复测。作者也明确没有完成旧版同环境 A/B、没有验证画质不变。此前本机 RTX 3060 Laptop 6GB 测速已有功耗/温控限制证据，不能将两台机器的 FPS 直接对比。
4. **1080p 原生 4x 保存校验显存不足仍是既有未解决问题。** 本机此前在 `torch_tensorrt.save → torch.jit.trace → assert_close` 校验中复现 OOM。新增 `cli/embedded-tools/convert_tensorrt.py:114` 仍使用相同保存调用，本 PR 未处理该显存峰值；小尺寸 2/3/4x 成功不能覆盖这个场景。本项作为已知限制记录，不认定为本 PR 新引入的回归；本轮遵循停止测速的要求，没有再次跑大尺寸构建或测速。
5. **未恢复四宫格入口。** PR #11 只替换遗留窗体中同类赋图调用，仍没有新增按钮或事件入口。

## PR #10 的范围与合并事项

- 归档读写改为独立 7za，并在写盘前检查路径、链接、已有重解析点、大小写冲突等；取消 RAR 支持，文档已有说明。这是兼容范围变化，应在后续发行说明保留。
- 独立分发 FFF API 11 DLL，移除 Base64 载荷；本轮在项目内的模拟安装目录实测加载 DLL、API 版本及所需导出函数，通过。
- 运行包与独立源码包的组成、哈希、内部安装载荷均通过检查。许可材料的打包通过不等同于对未公开的商业授权作独立法律确认。
- `git merge-tree --write-tree` 表明两 PR 产品代码可自动合并，`docs/codex/STATUS.md` 与 `version/工作进度.md` 存在内容冲突。需人工合并两边历史及本地新增审核记录，不能整份择一覆盖。未实际合并，也未运行组合版本。

## 本轮验证

所有构建和模拟安装均位于项目内忽略目录 `Artifacts/pr10-review-current`、`Artifacts/pr11-review-current`，未改本机正式安装。

| 检查 | 结果 |
| --- | --- |
| PR #10 CLI Release build、完整 solution publish | 通过；构建无警告/错误，生成 EXE、ZIP、安装器、183 项独立源码包。 |
| PR #10 `release/test-native-archives.ps1` | 40 项通过，覆盖常用归档、路径/链接拒绝、CRC 错误和取消。 |
| PR #10 `release/test-third-party-package.ps1` | 110 项通过，含真实项目内安装载荷和 FFF API 11 导出验证。 |
| PR #11 CLI 与插件 Release build | 均通过，0 警告、0 错误。 |
| LakeUI 5.112 未修复插件对照 | 成功复现反馈中的 `MissingMethodException / set_Image`，预期失败。 |
| PR #11 PixelPreview probe，LakeUI 5.110 / 5.112 | 两版本均通过，含实际插件回调。 |
| PR #11 FPS probe | 初始帧偏移、暂停时间及 ETA 通过。 |
| PR #11 `test_tensorrt_output_scale.py` | CPU/CUDA 两项通过，含形状、设备、数值与导出图验证；不是速度测试。 |
| PR #11 ModelMetadataUi probe | 18 项通过，含 2x/3x GPU 输出与 >4x 提示；未打开宿主或显示窗口。 |
| 两 PR 各自 worktree 的 `git diff --check` | 通过；PR #10 通过属性保留上游许可原文中的空白。 |

建议接纳两个 PR，合并前解决记录冲突；仍保留低显存 4x 保存问题与原速度反馈同条件对比的 TODO。当前主工作区仅审核报告及两份记录变化，非干净，切换工具或继续合并前可考虑 Git 提交。
