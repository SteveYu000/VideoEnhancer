Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Reflection
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FFmpegFreeUI
Imports LakeUI

Namespace videoenhancer

    Public Partial Class PluginPanel
        Private Shared Function BeginnerTutorialMarkdown() As String
            Return String.Join(Environment.NewLine, New String() {
                "# 使用教程",
                "",
                "如果你第一次使用视频超分或补帧，请按下面的顺序一步一步来。第一次不要同时打开所有功能，先把程序路径、模型和后端对应关系设置正确，再处理完整视频。",
                "",
                "## 先认识这几个概念",
                "- **视频超分**：把每一帧的宽和高放大，同时尝试补回纹理。例如 2x 是宽度和高度各变成 2 倍，最终像素数量约变成 4 倍；4x 的像素数量约是原来的 16 倍，所以更慢、更吃显存。",
                "- **运动补帧**：在原视频帧之间生成新帧，让运动看起来更顺滑。2 倍不是增加 2 帧，而是让输出帧率约变成原来的 2 倍。",
                "- **推理后端**：决定模型由哪套运行引擎执行，不代表画质等级。模型文件格式必须与后端匹配。",
                "- **模型**：决定画面适合什么内容。真人、动漫、插画、去噪和时序视频模型不是一回事，模型名右侧的悬浮提示会告诉你用途、倍率和后端。",
                "",
                "## 第 1 步：连接处理程序",
                "1. 在 3FUI 打开本插件的 **超分工作台**。",
                "2. 处理程序固定为插件目录下的 `videoenhancer\\videoenhancer.exe`。如果该文件缺失，请重新运行安装程序或按手动安装包的目录结构放置文件。",
                "3. 开启 **插件总开关**。如果路径正确，状态区会开始检查运行环境；请等它结束，不要在检查过程中反复切换后端。",
                "4. 看到环境检查通过后，再开启 **视频超分** 或 **运动补帧**。如果检查失败，先看状态区的具体文字，不要直接换模型，因为程序可能连 Python、显卡驱动或后端都还没有找到。",
                "",
                "## 第 2 步：准备模型",
                "### 方法 A：下载内置模型",
                "1. 切换到 **模型下载** 页，点击 **刷新资源**。列表中的资源按用途分组，模型通常会标明 NCNN、CUDA/PyTorch、TensorRT 或 ONNX 所需格式。",
                "2. 第一次只下载一个模型，不要点击 **下载全部**。下载并安装完成后，回到 **超分工作台**，再打开对应模型下拉框；如果列表还没有刷新，重新开启该功能或再次刷新模型。",
                "3. 当前后端只会列出它能使用的模型。比如选了 ONNX，就应选择 ONNX 模型；选了 NCNN，就应选择带 `.param/.bin` 的模型目录；不能拿 PTH 文件硬套到 ONNX 或 NCNN。",
                "",
                "### 方法 B：导入自己的模型",
                "1. 切换到 **模型导入** 页，选择模型文件或直接拖入文件、文件夹或压缩包。",
                "2. 填写或确认任务类型、架构和倍率，然后点击 **预检并导入模型**。预检未通过时不要强行使用，先按错误文字修正格式、架构或倍率。",
                "3. 导入成功后回到工作台，选择与导入结果显示的后端相同的后端。用户模型会和内置模型一起出现在对应的架构菜单中，并标注 `[用户]`。",
                "",
                "## 第 3 步：只做视频超分",
                "### 3.1 先选推理后端",
                "- **NCNN (Vulkan)**：不依赖 CUDA，使用显卡驱动提供的 Vulkan；适合没有 NVIDIA/CUDA 环境、想先跑通流程的人。它要使用 Param-Bin 模型目录。",
                "- **CUDA (PyTorch)**：需要 NVIDIA 显卡和可用驱动，使用 PTH/PT/PKL 权重；模型覆盖较广。电脑有 NVIDIA 显卡时，第一次建议从它开始。",
                "- **TensorRT (NVIDIA)**：也需要 NVIDIA 显卡；通常适合已经确认 CUDA 能正常运行、希望进一步提高速度的人。第一次使用某个模型和输入尺寸时可能要构建 Engine，请耐心等待；Engine 与显卡和输入设置有关，不能随便从别的电脑复制。",
                "- **ONNX Runtime**：只能选择 ONNX 模型；适合已经下载或导入 ONNX 文件的情况。看到模型列表为空时，先检查文件格式和模型目录，不要把空列表当成模型损坏。",
                "- **FlashVSR / BasicVSR++**：这是利用连续视频帧的时序超分模型，不是普通单帧放大模型。它们更适合视频素材；BasicVSR++ 当前不能再叠加运动补帧。",
                "",
                "### 3.2 再选放大模型",
                "1. 点击 **放大模型**，先进入一级架构分类，再在第二级点击具体模型。鼠标停在具体模型上会显示简短说明；说明中的倍率是模型固定输出倍率，不需要另填。",
                "2. **真人、风景、普通网络视频**：先找 `RealESRGAN-General-x4v3` 做基准。它是通用 4x，不代表任何视频都必须放大 4 倍；如果最终只需要 2x，应优先选明确标注 2x 的模型或后续调整输出尺寸。",
                "3. **动漫视频**：先看 `RealESRGAN-AnimeVideoV3` 的 2x/3x/4x 版本；原片还清楚时从 2x 开始，低分辨率且确实需要大画面时再试 4x。",
                "4. **动漫截图、插画、线稿**：可从 AnimeJaNai 的 Balanced、Waifu2x 或 SPAN 类模型开始。Balanced 适合普通情况，Sharp1 更强调边缘，Noise 版本按噪声强弱选择。",
                "5. **只想去掉压缩噪声、不想放大**：选择 `DenoiseH264` 或 `DnCNN` 这类 1x 模型。1x 只修画面，不改变宽高；不要因为名称里有模型家族名就把它当成 2x/4x 放大模型。",
                "6. 如果不确定，先看悬浮提示，再根据素材的脸部、字幕、线稿、快速运动和重复纹理选择模型；不要只按某一帧是否更锐来判断。",
                "",
                "### 3.3 半精度和分块怎么选",
                "- **半精度推理**默认开启时，CUDA/TensorRT 会优先尝试 FP16，不兼容时自动回退 FP32。第一次使用保持开启即可；如果出现黑帧、花屏或模型报不支持，再关闭它强制 FP32。超分和补帧的半精度开关彼此独立。",
                "- **超分分块尺寸**先保持 `RVE 默认（0）`。如果任务报显存不足，按 `512 px → 384 px → 256 px → 128 px` 逐级尝试；数值越小越省显存，但需要更多块，速度会变慢。不要为了追求更大的数字而忽略显存。",
                "- FlashVSR 等后端不支持分块设置，选择时该选项会变灰。",
                "",
                "## 第 4 步：只做运动补帧",
                "### 4.1 选择补帧后端和模型",
                "- **NCNN**：适合不使用 CUDA 的 RIFE 模型目录。",
                "- **CUDA**：适合 RIFE、GMFSS 和 GIMM 的 PyTorch 权重；GMFSS/GIMM 在当前程序中会使用 CUDA。",
                "- **TensorRT**：当前主要用于 RIFE 权重自动构建 Engine；GIMM 和 GMFSS 不要强行选 TensorRT。",
                "- 第一次使用先选通用 RIFE 和 2 倍。RIFE heavy 会消耗更多显存和时间，适合普通版本不够稳定时再试；GMFSS AnimeRun 更适合动漫运动，GMFSS Base 适合先做通用基准。",
                "",
                "### 4.2 补帧倍率怎么选",
                "- `2 倍`：最稳妥的起点。例如输入 24 fps，输出约 48 fps；建议第一次使用。",
                "- `3 倍`：适合想比 2 倍更顺滑、又不想承担 4 倍开销的情况。",
                "- `4 倍`：适合高刷新率播放或慢动作需求，但计算量和运动错误风险都会增加。",
                "- `8 倍`：只建议在已经确认模型、素材和显存都稳定后使用；快速运动、遮挡和镜头切换更容易出现不自然的中间帧。",
                "选择倍率后不需要再去 3FUI 的视频参数里手动填写输出帧率；插件会把倍率交给处理程序。",
                "",
                "### 4.3 转场阈值怎么选",
                "- 先使用 **标准 4.0**。阈值越低，程序越敏感，越容易在镜头切换处跳过补帧；阈值越高，程序越不敏感，可能把切换前后的画面误认为连续运动。",
                "- 如果视频剪辑很多、转场处出现鬼影或两幅画面混在一起，改用 `2.0` 或 `3.5`；如果镜头很连续但不希望轻微变化被当成转场，可试 `6.0`。",
                "- `1.0` 很敏感，`8.0/10.0` 很宽松。它们不是画质档位，而是转场判断灵敏度；不要为了让画面更锐而调高阈值。",
                "",
                "### 4.4 动态光流尺度",
                "- 默认关闭即可。普通素材、固定机位和缓慢运动先不要改。",
                "- CUDA 下遇到大幅运动、镜头速度变化或运动尺度差异明显时，可以开启；它会增加计算量，不保证所有素材都更好。",
                "",
                "## 第 5 步：同时超分和补帧时怎么选",
                "1. 同时开启 **视频超分** 和 **运动补帧** 后，才会出现 **组合处理顺序**。第一次建议保持 **画质优先：先超分，再补帧**；先把画面放大，再在更大的画面上计算运动，便于观察细节。",
                "2. 如果显存或速度压力较大，可以试 **速度/算力优先：先补帧，再超分**。先在较小画面上补帧，再统一放大，通常更省算力，但要留意快速运动和细线。",
                "3. 两个阶段使用同一个后端时，程序会在一个 RVE 进程内逐帧传递，不会因为换顺序生成整段临时视频。小白优先使用同后端，例如 CUDA + CUDA。",
                "4. 两个阶段使用不同后端时，程序会生成隐藏的 `.videoenhancer-*.mkv` 无损中间文件。它需要额外磁盘空间，4K 或高帧率视频可能很大；任务结束后会自动清理，FFV1 只是阶段间传递格式，不是最终输出格式。",
                "5. BasicVSR++ 当前不能与运动补帧组合；选择它时，补帧开关不可用。",
                "",
                "## 第 6 步：确认设置并加入队列",
                "1. 确认输入视频包含你关心的内容，例如人物、字幕、细线、快速运动或镜头转场。不同内容会影响模型和参数的选择。",
                "2. 确认插件总开关、需要的功能开关、处理程序路径、后端和模型都已选好。下拉框请用鼠标左键打开和选择；鼠标滚轮经过显示区域不会再悄悄改变单选值，打开后的列表仍可在列表区域滚动。",
                "3. 回到 3FUI 的文件列表，点击 **加入编码队列**。插件会接管这次任务并通过 `videoenhancer.exe` 执行；处理期间不要移动或删除模型、Python 后端和输入文件。",
                "4. 在 **实时预览** 查看处理中或已完成的画面。重点留意原片与输出的脸部、字幕、线稿、运动边缘、转场和颜色。",
                "",
                "## 常见问题",
                "### 模型列表是空的",
                "先确认 `videoenhancer.exe` 路径存在，再确认当前后端和模型格式匹配；下载或导入后回到工作台重新打开模型菜单。如果选了 BasicVSR++、FlashVSR、ONNX 等特殊后端，不要期待它显示其他后端的模型。",
                "",
                "### 任务报显存不足",
                "先把超分分块调小，关闭不必要的超分/补帧阶段，补帧倍率退回 2 倍；CUDA/TensorRT 还可以暂时关闭对应的半精度开关做稳定性对比。不要一边保留 4x/8x，一边把分块调到最大。",
                "",
                "### 画面过锐、噪声变多或细线消失",
                "这是模型与素材不匹配的常见表现。动漫线稿可换 Balanced、Noise0/1 或较温和的 2x 模型；噪声很重时才逐步使用 Noise2/3 或强模型。每次只改一个选项，方便判断变化原因。",
                "",
                "### 补帧出现鬼影或转场撕裂",
                "先把倍率降到 2 倍，转场阈值改为 2.0/3.5，并确认素材不是大量快速剪辑。再尝试另一个补帧模型；不要只把阈值调到最大，因为过高可能让程序跨越真正的镜头切换。",
                "",
                "### 第一次 TensorRT 很慢",
                "正常。TensorRT 可能正在为当前显卡、输入尺寸、倍率、分块和精度构建 Engine；后续相同设置会复用缓存。换显卡、分块、倍率或精度后，出现新的构建过程也是正常的。",
                "",
                "### 10-bit 输出是不是 10-bit 推理",
                "不是。当前 RVE 的 SDR 内部帧仍是 8-bit RGB；最终选择 10-bit 输出只影响编码格式，不等于模型以 10-bit 精度推理。PQ/HLG HDR 目前只允许 CUDA/PyTorch 或 TensorRT，其他后端会明确拒绝。"
            })
        End Function

        Private Sub BuildMarkdownPage(page As ModernPanel, markdown As String)
            page.Dock = DockStyle.Fill
            page.BackColor = Color.Transparent
            page.BackColor1 = Color.Transparent
            page.BackgroundSource = ModernPanel1
            page.BorderSize = 0
            page.Padding = New Padding(0, 8, 0, 0)
            _markdownSources(page) = If(markdown, "")
        End Sub

        Private Sub EnsureMarkdownPage(page As ModernPanel)
            If page Is Nothing OrElse _markdownReady.Contains(page) Then Return
            Dim markdown As String = ""
            If Not _markdownSources.TryGetValue(page, markdown) Then Return
            Dim viewer As New MarkDownViewer With {
                .Dock = DockStyle.Fill,
                .Margin = Padding.Empty,
                .Padding = New Padding(10, 8, 10, 12),
                .BackColor = Color.Transparent,
                .BackgroundSource = ModernPanel1,
                .BorderSize = 0
            }
            viewer.ScrollBarWidth = 10
            viewer.ScrollBarTrackColor = Color.FromArgb(18, 18, 18)
            viewer.ScrollBarColor = Color.FromArgb(72, 72, 72)
            viewer.ScrollBarHoverColor = Color.FromArgb(104, 104, 104)
            viewer.HeadingColor = UiText
            viewer.BoldColor = UiText
            viewer.LinkColor = UiAccent
            viewer.CodeBackColor = Color.FromArgb(44, 44, 48)
            viewer.CodeBlockBackColor = Color.FromArgb(32, 34, 38)
            viewer.CodeBlockForeColor = UiTextSecondary
            AddHandler viewer.LinkClicked,
                Sub(sender, args)
                    Try
                        If args Is Nothing OrElse String.IsNullOrWhiteSpace(args.LinkText) Then Return
                        Process.Start(New ProcessStartInfo With {
                            .FileName = args.LinkText,
                            .UseShellExecute = True})
                    Catch
                        ' 外部链接无法打开时不影响教程页面和插件主流程。
                    End Try
                End Sub
            viewer.SetMarkdownImmediate(markdown)
            page.Controls.Add(viewer)
            _markdownReady.Add(page)
        End Sub
    End Class

End Namespace
