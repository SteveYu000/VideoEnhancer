# TensorRT 倍率、预览与持续降速复验

## 范围与实现

首轮修复基于 maxzrb/VideoEnhancer 的 `f261e37`（1.3.12），保持版本号、模型权重、用户配置和编码质量参数不变；仅修改本地源码与安装，不提交、推送或发布。用户后续授权的 preset 10 诊断对照见追加章节，不写回用户预设。

- AVV3 TensorRT 显示为 `realesr-animevideov3 2/3/4x`，使用已有输出倍率选择。保留官方 4x 权重，2x/3x 的双三次缩放在 GPU 输出图内完成，超过 4x 仍先做 4x 再缩放。上游已有低倍率调度，本轮将支持 `--output-scale` 的转换脚本内嵌随 CLI 同步，防止后端重装后丢失；没有将权重改名冒充原生 2x 网络。
- `PixelPictureBox.Image` 的 setter 在新版 LakeUI 中已移除。实时预览、清空及四宫格改为兼容旧版 `Image` / 新版 `Source` 的统一入口；新入口使用回调图片源，图片仍由调用方管理，换源后再释放旧图，不改界面布局。
- FPS 原先从首条进度重新计时，却将之前已处理帧也计入分子。修正为同一计时区间的新增帧数，ETA 使用同一速率；该修复只纠正启动虚高，不提升实际编码吞吐。

## 测试环境与口径

本机 AMD Ryzen 9 7940HX（16 核 / 32 线程）、RTX 4070 Laptop 8 GB、约 64 GB 内存；用户告知已切换增强模式。运行时 CLI 为 1.3.12，使用宿主的 FFmpeg，SVT 日志版本为 4.2.0。

源片 `D:\Animation Enhance\GBC 108048\OP.mkv`，1920×1080。96 帧短测采用无损片段；960 帧持续测试从原片复制视频流，保留实际画面与帧率，但不包含音频、字幕或附件，因此不等于原片全部流的完整验收。原片未改动。

原生 2x 模型为本机配置中的 `AnimeJaNai-V3-2x-HD-Sharp1-Compact-430K`；均为 TensorRT、FP16、不分块、输出 3840×2160。以下为后端记录的渲染耗时，不包含环境检查、缓存验证或首次 Engine 构建时间。

| 模型 / 编码参数 | 帧数 | 渲染秒数 | 帧数 / 渲染秒数 |
| --- | ---: | ---: | ---: |
| AnimeJaNai 原生 2x / H.264 NVENC p1、CQ 30 | 96 | 9.84 | 9.76 fps |
| AVV3 图内 2x / 同一 NVENC 参数 | 96 | 10.28 | 9.34 fps |
| AnimeJaNai 原生 2x / 同一 NVENC 参数 | 960 | 96.81 | 9.92 fps |
| AnimeJaNai 原生 2x / 用户 SVT-AV1 参数 | 960 | 256.29 | 3.75 fps |
| AnimeJaNai 原生 2x / 用户参数仅请求 preset 10（实际 9） | 960 | 106.72 | 9.00 fps |

SVT 测试保留用户的 `libsvtav1`、preset 6、CRF 12、`yuv444p10le` 及完整 `svtav1-params`：

```text
scd=1:scd-min-keyint=33:keyint=321:complex-hvs=1:enable-tf=0:enable-cdef=1:enable-dlf=2:enable-qm=1:qm-min=4:chroma-qm-min=10:enable-variance-boost=1:variance-boost-strength=2:tile-columns=0:tile-rows=0:film-grain=4:sharpness=1:ac-bias=1
```

流映射、FLAC、字幕复制、元数据、章节和附件选项也传给 CLI；映射按现有后端的双输入规则处理。仅附加 `-progress pipe:2` 诊断编码计数，不改变编码质量参数。完整传入参数记录在测试日志的 `FFmpeg 参数` 与 `FFMPEG WRITE COMMAND` 中。

## 持续降速结论与边界

NVENC 连续测试维持约 10 fps，显存约 3.1 GB；GPU 从启动频率约 2445 MHz 回落至约 2100–2200 MHz，温度最高约 80℃，采样未出现硬件或软件热降频标记。这不能代替不同电源模式或完整长视频的验收。

用户 SVT 参数已复现前快后慢：前期超分约 9.4 fps；约 142.59 秒时超分处理 803 帧，编码只输出 337 帧。编码进程工作集约 23–25 GB，SVT 报告 `Level of Parallelism: 6`、`Number of PPCS 300`；GPU 利用率多次降到 1%–3%，热降频标记未激活。后期超分计数停在最后一批帧，GPU 空闲，FFmpeg 继续编码和封装。最终 FFmpeg 编码计数 960 帧、约 3.77 fps，后端全过程约 3.75 fps。

该测试的瓶颈是 SVT 编码吞吐及缓冲反压，不是模型始终占满 GPU 却降到 4 fps。没有擅自降低 CRF、改变 preset、改成 NVENC、改 4:2:0 或覆写用户预设；也没有声称 TensorRT 修改能在这组相同参数下将整链提升至 9 fps。尚未进行 1.3.0 同环境同参数的历史对照，不能凭此排除历史 FFmpeg、模型或电源设置差异。

## 追加：用户授权 preset 10 持续对照

同一 960 帧片段、模型、精度、尺寸、宿主 FFmpeg 和其余编码参数，仅将传入的 `-preset:v:0 6` 改为 `-preset:v:0 10`，不写回用户预设或插件配置。输入本次测试命令前确认无其他 3FUI/VideoEnhancer/FFmpeg 任务，运行 EXE 与后端未更换。

本机 SVT 4.2.0 明确警告：4K及以上、Random Access 模式最高支持 M9，自动将 M10 改为 M9；实际配置日志为 preset 9。因此该结果必须标为“请求 p10 / 实际 p9”，不是原生 p10 测试。日志同时提示 M9 及以上关闭屏幕内容检测工具，film grain 在大于 preset 6 时有明显计算开销；其余传入参数保持，不擅自移除 `film-grain=4`。

持续处理约 9.3–9.5 fps；103.04 秒采样时超分842帧、编码724帧，相差118帧，未像 p6 那样持续扩大到约470帧。GPU多为约90%–97%，编码工作集约12GB（SVT PPCS156，p6为300）；采样无软件/硬件热降频标记。960帧渲染106.72秒，整链约9.00fps，FFmpeg最后960帧/约9.05fps，输出约232.86MB。该片段未再次降到4fps，不能代替整片长时间验收。

这表明新版的快速编码预设可以跟上当前模型，而不代表已解释或修复“旧版本 preset 6 也能维持9fps”的差异。只读核查 v1.3.0 也有首条进度重新计时但未减首帧计数的旧FPS公式；没有运行旧版同环境对照，不能据此将旧反馈归结为显示虚高，仍需核对旧软件/后端/FFmpeg、真实编码参数和输出像素格式。

日志、GPU/编码 CSV 与编码器日志使用 `fixed-janai-svt-p10-960.*` 独立命名，保留 p6 基线；ffprobe完整解码确认AV1、3840×2160、yuv444p10le、960帧。没有覆盖原片、旧结果、配置或程序。

## 验证与本机证据

- LakeUI 5.110 / 5.112：图片赋值、替换、清空、调用方所有权及实际插件 `OnPreviewFrameReady` 回调通过；未自动启动 3FUI，实际宿主可视预览仍待用户复验。
- 38 项 Python 回归通过，含新增 CPU / CUDA 输出图尺寸、设备、数值及可导出图验证；倍率 UI 的 2x/3x GPU 输出与 >4x 后缩放提示、FPS 初始帧偏移/暂停/ETA 回归通过。
- AVV3 使用新转换脚本在 80×64 输入上实际构建 2x、3x、4x Engine，分别输出 160×128、240×192、320×256；6x 复用 4x Engine 后输出 480×384，四项均完整 12 帧。Engine 元数据确认 2x/3x 输出尺寸，不依赖模型名称猜测。
- 解决方案 publish 及安装/自更新的哈希、独立组件保持、故障回滚门禁通过。初次安装器测试与 publish 并行，恰逢安装器重建而未找到文件；等待构建结束后重试通过。小尺寸初测 NVENC 因低于最小尺寸失败，改用无损 FFV1 验证倍率；均未改产品代码绕过测试失败。
- 960 帧 SVT 输出实测 3840×2160、`yuv444p10le`、960 帧；不是仅凭进度文字判定成功。
- 本机日志与测试输出：`C:\PortableSoft\FFmpegFreeUI ReadyToRun x64\plugin\videoenhancer\.work\verification\20261006-trt-preview`。`baseline-*` 为修改前的已安装 1.3.12；`fixed-janai-960.*` 为 NVENC 持续测试；`fixed-janai-svt-960.*` 为用户参数持续测试，包含 GPU/编码计数 CSV 和完整编码器日志。
- 复验脚本：`release/tests/MeasureTensorRt.ps1`；可用 `-EncoderArguments` 传入原编码设置，输出独立日志和 CSV。它不会修改用户配置或电源模式。

## 追加：MyGO / AVV3 2x / 用户修订参数，实际运行约三分钟

2026-10-06 13:44。用户明确要求实际运行约三分钟，不是处理三分钟的视频内容。输入 `D:\Animation Enhance\MyGO BDRemux\01.mkv`，实测H.264、1920×1080、yuv420p、24000/1001 fps，时长约1487.51秒。保持preset6、此前CRF12，用户的“y410”按前述4:4:4 10-bit输出 `yuv444p10le` 理解；禁用音频、字幕和数据轨。超分使用安装版1.3.12、官方AVV3 PTH、TensorRT FP16、无分块、GPU输出图内2x，复用已验证1920×1080/scale2缓存，没有重新构建Engine。

完整编码参数保持用户新串，不删减或替换：

```text
-c:v libsvtav1 -preset 6 -crf 12 -pix_fmt yuv444p10le -an -sn -dn -svtav1-params tune=0:scd=1:scd-min-keyint=33:keyint=1025:enable-tf=0:enable-cdef=-1:enable-dlf=2:scm=3:enable-qm=1:qm-min=2:chroma-qm-min=4:enable-variance-boost=1:variance-boost-strength=1:film-grain=5:adaptive-film-grain=0:sharpness=1:ac-bias=1:lp=4
```

SVT4.2.0日志确认preset6/VQ（tune0）/CRF12/YUV444/10-bit、GOP1025、variance strength1、film-grain5且adaptive False、AC bias1、Level of Parallelism4、PPCS102。`lp=4`是并行级别，不是限制到四个线程，见[官方参数说明](https://github.com/AOMediaCodec/SVT-AV1/blob/v4.2.0/Docs/Parameters.md#1-thread-management-parameters)。CLI仍显示源权重4x，但选用缓存名scale2、后端实际Model Scale2、写管道3840×2160，不能凭权重显示4x误判输出倍率。

脚本从首次观察到编码进程起计时，在181.69秒写入独立停止共享内存，通过程序正常停止入口收尾；CLI启动以来约197.78秒（环境检查和缓存验证不计入三分钟渲染），随后排空已送入编码器的帧。区间速度使用CSV帧数差/时间差，避免启动虚高及不同累计时钟。五秒采样的实际边界如下：

| 名义渲染区间 | 实际采样边界（秒） | 超分fps | 编码fps | 起/止积压帧 |
| --- | --- | ---: | ---: | ---: |
| 20–60秒（避开启动） | 23.32–58.01 | 7.821 | 7.330 | 118 / 135 |
| 60–120秒 | 63.85–116.74 | 8.196 | 8.499 | 137 / 121 |
| 120–180秒 | 122.68–175.72 | 8.694 | 8.882 | 115 / 105 |

三分钟内未出现持续降到4fps：开头累计显示约9fps，填充后有7–9fps波动，后段区间吞吐回升而非继续下降；停止前累计超分8.44fps。GPU采样平均约90.7%、多数87%–100%，有一次37%；温度最高84℃，软件/硬件热降频标记均未激活，功耗限制标记激活不能等同热降频。编码工作集启动后主要约9–10GB、采样最大10396MiB，积压约140帧后缩小至约100帧，未持续扩大到此前数百帧。

输出正常封装，FFmpeg末尾frame1575/progress=end/8.26fps；ffprobe整文件读取1575视频包，唯一流为AV1、3840×2160、yuv444p10le、24000/1001，时长65.691秒、139659317字节，无音频或字幕；首两帧实际解码成功。这里是包数+编码计数+首帧解码核验，没有把它写成整文件逐帧解码验收。渲染停止时采样1559帧，随后还有少量帧送入管道，不能要求最终1575与该瞬间完全相同。

本机证据：`C:\PortableSoft\FFmpegFreeUI ReadyToRun x64\plugin\videoenhancer\.work\verification\20261006-mygo-avv3-svt`。`mygo-avv3-2x-p6-cdefauto-lp4-run.log/.err/.gpu.csv/.encoder.log`及同名不带run的MKV保留。第一次因诊断脚本未为含空格的完整模型路径加引号，在启动渲染前报未知参数ReadyToRun；仅修正测速脚本引号，失败日志独立保留，第二次正常执行。

`MeasureTensorRt.ps1`新增可选OutputScale和StopAfterSeconds、独立停止共享内存、RenderSeconds及StopRequested采样，并按用户停止的退出码130接受正常收尾；默认不定时停止，既有测试调用保持。语法和git diff --check通过。插件配置本轮两次读取SHA256均A071F8678223A21E4B48F4130E186C63B673AF00858E2ECADE9DC152FE993C5C，未写预设/配置，未更换程序或模型，版本仍1.3.12，未commit/push/PR/Release。

本轮只说明这份素材开头、这组完整参数及环境在约三分钟内没有持续掉速；没有测整集、不同场景或画质等价性，也不是与旧参数的同素材单变量对照，不能将全部改善归因于lp或某一个参数。
