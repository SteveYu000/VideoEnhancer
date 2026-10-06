# SVT 参数短片逐项对照（2026-10-06）

## 结论

本机这份 2160p、4:4:4 10-bit 素材的主要降速项是显式指定 `enable-cdef=1`。完整参数约 4.73 fps；仅改为用户要求测试的 `enable-cdef=-1` 为 10.69 fps，约提高 2.26 倍。仅删掉该项为 10.00 fps。反向验证：快速组合只增加 `enable-cdef=1`，从 11.81 降至 4.88 fps。

这是短片因果对照，不是全片持续速度承诺，也没有证明旧版 1.3.0 与新版的所有差异已经解决。未测画质指标，不能宣称修改 CDEF 策略后画质或码率不变。

## 固定条件

- 输入：`D:\RebellionHEVC444p10-700MBPS-SoftwareLast.mkv`，HEVC、3840×2160、yuv444p10le、24000/1001 fps，容器时长约 94.052 秒。原片未修改。
- 直接编码，不超分：本机宿主 `C:\PortableSoft\FFmpegFreeUI ReadyToRun x64\ffmpeg.exe`，SVT-AV1 4.2.0，preset 6、CRF 12、yuv444p10le。
- 同一开头 240 帧，约 10 秒素材；仅视频，`-an -sn -dn`，不跑完整原片。所有编码串行，无其他 FFmpeg 编码任务参与。
- 速度为 `240 / FFmpeg benchmark rtime`，包含短片启动和最后排空，不使用脚本每五秒轮询的尾部等待时间，也不将处理中某一条累计 FPS 当作稳定速度。
- 快速组合保留 `scd=1:scd-min-keyint=33:keyint=1025:film-grain=4:sharpness=1`，避免不同 GOP 设置混入对照。

## 实测

下表未特别说明的项目都只改变完整参数中的一项。

| 测试日志前缀 | 改动 | 耗时（秒） | 短片吞吐（fps） |
| --- | --- | ---: | ---: |
| full-240 | 用户完整参数 | 50.778 | 4.726 |
| known-240 | 快速组合 | 20.326 | 11.808 |
| full-ac0-240 | ac-bias=0 | 47.222 | 5.082 |
| full-dlf1-240 | enable-dlf=1 | 47.922 | 5.008 |
| full-qm0-240 | enable-qm=0 | 48.598 | 4.938 |
| full-tf1-240 | enable-tf=1 | 49.344 | 4.864 |
| full-var0-240 | enable-variance-boost=0 | 46.574 | 5.153 |
| full-cdef-auto-240 | 删去 enable-cdef=1 | 23.995 | 10.002 |
| full-dlf-auto-240 | 删去 enable-dlf=2 | 48.904 | 4.908 |
| known-cdef1-240 | 快速组合仅加 enable-cdef=1 | 49.208 | 4.877 |
| full-cdef-minus1-240 | enable-cdef=-1 | 22.454 | 10.689 |

关闭 AC bias、量化矩阵、variance boost，或修改 DLF、TF，均未单独恢复到快速组合水平。约 3%–9% 的短片差异未做重复统计，不能精确排序这些次要项的成本。删去 CDEF 与显式 -1 属于同类自动策略，两者的约 7% 差异也不能解读为不同模式的固定性能差。

## CDEF 语义和建议

SVT 4.2.0 的源码把 `enable-cdef` 解析到整数 `cdef_level`，允许 `-1` 表示自动；默认初始化使用自动值。因此显式 `1` 不应视为与 preset 自动策略完全等价的普通布尔开关。依据：[4.2.0 参数解析和初始化源码](https://github.com/AOMediaCodec/SVT-AV1/blob/v4.2.0/Source/Lib/Globals/enc_settings.c)。CDEF 包含方向滤波及候选强度搜索，复杂度随策略变化，见[官方 CDEF 说明](https://github.com/AOMediaCodec/SVT-AV1/blob/v4.2.0/Docs/Appendix-CDEF.md)。源码说明仅用于解释策略；本机实际速度结论来自上表，不假定官方标签包含本机全部 4:4:4 扩展。

若采用本轮结果，只需将原串中的 `enable-cdef=1` 改为 `enable-cdef=-1`，其余参数保持：

```text
scd=1:scd-min-keyint=33:keyint=1025:enable-tf=0:enable-cdef=-1:enable-dlf=2:enable-qm=1:qm-min=4:chroma-qm-min=10:enable-variance-boost=1:variance-boost-strength=2:tile-columns=0:tile-rows=0:film-grain=4:sharpness=1:ac-bias=1
```

没有替用户写回该参数或预设，没有修改安装程序、产品代码、版本、模型或电源模式。宿主已打开但不需要关闭；本轮未覆盖任何 DLL/EXE。

## 复现和证据

新增 [MeasureSvtParameters.ps1](../release/tests/MeasureSvtParameters.ps1)，调用示例：

```powershell
& .\release\tests\MeasureSvtParameters.ps1 -InputVideo 'D:\RebellionHEVC444p10-700MBPS-SoftwareLast.mkv' -Ffmpeg 'C:\PortableSoft\FFmpegFreeUI ReadyToRun x64\ffmpeg.exe' -OutputRoot '<新的输出目录>' -CaseName 'cdef-auto' -Parameters '<上面的参数串>' -Frames 240
```

脚本不覆盖已有同名输出，检查退出码、末尾 240 帧和 `progress=end`，保留 `.encoder.log`、`.progress.log`、测试 MKV 和 `results.csv`。本机证据目录：`C:\PortableSoft\FFmpegFreeUI ReadyToRun x64\plugin\videoenhancer\.work\verification\20261006-svt-ablation`。

完整参数、快速组合、快速组合加CDEF1、删CDEF、显式CDEF-1五份关键输出已分别用 `ffprobe -count_frames` 完整读取：均为AV1、3840×2160、yuv444p10le、24000/1001、240帧。全部编码和核验已结束；脚本语法检查及 `git diff --check` 通过。本轮期间两次读取插件配置哈希均为F5380A5FAEAD9F3DB6A1FC9863BFF0A8F76AC9FB816D38AEB4CF985854CCE988；宿主当前已打开，不将此前会话的配置哈希当作本轮基线。

快速组合与完整参数的第一帧码流均为 tile_cols_log2=0、tile_rows_log2=0，未发现自动 tile 数差异。trace_headers 同时显示 CodecPrivate 与真正码流中的两组序列头，不将前者尚未初始化的 enable_cdef=0 当作实际关闭滤波的证据。
