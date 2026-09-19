Imports System
Imports System.IO
Imports System.Text.Json

Namespace videoenhancer

    ''' <summary>插件配置，持久化到 %LocalAppData%\FFmpegFreeUI\videoenhancer.plugin.json。</summary>
    Public Class PluginConfig

        Public Property ExePath As String = ""
        Public Property Model As String = ""
        Public Property Enabled As Boolean = False
        ''' <summary>超分开关：是否将"加入编码队列"hook 到 videoenhancer.exe 中转。</summary>
        Public Property UpscaleEnabled As Boolean = True
        ''' <summary>补帧开关：启用 RIFE、GIMM-VFI 或 GMFSS 补帧，可与超分组合。</summary>
        Public Property InterpEnabled As Boolean = False
        ''' <summary>补帧模型：优先使用 models\Frame-Interpolation 下的架构相对路径；旧 models\RIFE 继续兼容。</summary>
        Public Property InterpModel As String = ""
        ''' <summary>补帧倍率（RIFE --interpolate_factor，默认 2；须为大于 1 的数字）。</summary>
        Public Property InterpFactor As Double = 2.0
        ''' <summary>RIFE 动态光流尺度；仅 CUDA/PyTorch 有效，TensorRT 由 RVE 自动禁用。</summary>
        Public Property InterpDynamicScaledOpticalFlow As Boolean = False
        ''' <summary>RIFE 转场检测阈值；数值越低越容易判定为转场。</summary>
        Public Property SceneDetectThreshold As Double = 4.0
        ''' <summary>超分分块边长；0 表示使用 RVE 默认处理，不按显存自动试探。</summary>
        Public Property UpscaleTileSize As Integer = 0
        ''' <summary>超分优先使用半精度；关闭时对支持精度控制的后端强制 FP32。</summary>
        Public Property UpscaleHalfPrecision As Boolean = True
        ''' <summary>超分推理后端：ncnn、cuda、tensorrt、onnx 或 flashvsr。</summary>
        Public Property Backend As String = "ncnn"
        ''' <summary>补帧后端：ncnn、cuda（PyTorch 权重）或 tensorrt（RIFE 权重自动构建 Engine）。</summary>
        Public Property InterpBackend As String = "ncnn"
        ''' <summary>补帧优先使用半精度；关闭时对 CUDA/TensorRT 强制 FP32。</summary>
        Public Property InterpHalfPrecision As Boolean = True
        ''' <summary>组合处理顺序：upscale-first（画质优先，默认）或 interp-first（速度/算力优先）。</summary>
        Public Property ProcessOrder As String = "upscale-first"
        ''' <summary>对视频启用 NVIDIA RTX Video HDR 映射。</summary>
        Public Property RtxHdrEnabled As Boolean = False
        ''' <summary>RTX HDR 对比度（0-200，默认 100）。</summary>
        Public Property RtxHdrContrast As Integer = 100
        ''' <summary>RTX HDR 饱和度（0-200，默认 100）。</summary>
        Public Property RtxHdrSaturation As Integer = 100
        ''' <summary>RTX HDR 中灰度（10-100，默认 44）。</summary>
        Public Property RtxHdrMiddleGray As Integer = 44
        ''' <summary>RTX HDR 最大亮度（400-2000 nit，默认 1000）。</summary>
        Public Property RtxHdrMaxLuminance As Integer = 1000
        ''' <summary>RTX VSR 输出规格：倍率或按横竖方向映射的目标边。</summary>
        Public Property RtxTarget As String = "2x"
        ''' <summary>RTX VSR 质量等级（1-4）。</summary>
        Public Property RtxQuality As Integer = 3
        ''' <summary>资源管理器“超分辨率”级联菜单中启用的模型。</summary>
        Public Property ShellModels As New Collections.Generic.List(Of ShellUpscaleModel)()
        ''' <summary>按完整视频路径保存的分段超分配置；默认秒级关键帧断点，兼容精确帧。</summary>
        Public Property SegmentedVideos As New Collections.Generic.List(Of SegmentedVideoConfig)()
        Public Property ImageOutput As String = ""
        Public Property ImageOutputOriginal As Boolean = False
        Public Property ImagePng As Boolean = True
        Public Property ImageSuffix As String = "timestamp"
        ''' <summary>插件页面首次加载后是否在后台检查稳定版更新。</summary>
        Public Property AutoCheckUpdates As Boolean = True

        Private Shared Function GetConfigDir() As String
            ' 支持环境变量覆盖（测试/便携部署用），默认 %LocalAppData%\FFmpegFreeUI
            Dim overrideDir = Environment.GetEnvironmentVariable("VIDEOENHANCER_CONFIG_DIR")
            If Not String.IsNullOrWhiteSpace(overrideDir) Then
                Return overrideDir
            End If
            Return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFmpegFreeUI")
        End Function

        Private Shared ReadOnly ConfigDir As String = GetConfigDir()
        Private Shared ReadOnly ConfigPath As String = Path.Combine(ConfigDir, "videoenhancer.plugin.json")

        Public Shared Function Load() As PluginConfig
            Dim cfg As PluginConfig = Nothing
            Dim configChanged As Boolean = False
            Try
                If File.Exists(ConfigPath) Then
                    cfg = JsonSerializer.Deserialize(Of PluginConfig)(File.ReadAllText(ConfigPath))
                End If
            Catch
                ' 配置损坏时回退到默认
            End Try
            If cfg Is Nothing Then cfg = New PluginConfig()
            configChanged = cfg.NormalizeRtxHdrParameters()
            Dim detected = ResolveInstalledExePath(cfg.ExePath)
            If Not String.Equals(cfg.ExePath, detected, StringComparison.OrdinalIgnoreCase) Then
                cfg.ExePath = detected
                If Not String.IsNullOrWhiteSpace(detected) Then configChanged = True
            End If
            If configChanged Then cfg.Save()
            Return cfg
        End Function

        ''' <summary>
        ''' 将旧配置缺少的 HDR 字段保留为属性默认值，并把越界值钳制到 sidecar 合法范围。
        ''' 返回是否发生修正，供 Load() 决定是否回写配置文件。
        ''' </summary>
        Public Function NormalizeRtxHdrParameters() As Boolean
            Dim changed As Boolean = False
            Dim contrast = ClampRtxHdrContrast(RtxHdrContrast)
            If contrast <> RtxHdrContrast Then RtxHdrContrast = contrast : changed = True
            Dim saturation = ClampRtxHdrSaturation(RtxHdrSaturation)
            If saturation <> RtxHdrSaturation Then RtxHdrSaturation = saturation : changed = True
            Dim middleGray = ClampRtxHdrMiddleGray(RtxHdrMiddleGray)
            If middleGray <> RtxHdrMiddleGray Then RtxHdrMiddleGray = middleGray : changed = True
            Dim maxLuminance = ClampRtxHdrMaxLuminance(RtxHdrMaxLuminance)
            If maxLuminance <> RtxHdrMaxLuminance Then RtxHdrMaxLuminance = maxLuminance : changed = True
            Return changed
        End Function

        Public Shared Function ClampRtxHdrContrast(value As Integer) As Integer
            Return Math.Max(0, Math.Min(200, value))
        End Function

        Public Shared Function ClampRtxHdrSaturation(value As Integer) As Integer
            Return Math.Max(0, Math.Min(200, value))
        End Function

        Public Shared Function ClampRtxHdrMiddleGray(value As Integer) As Integer
            Return Math.Max(10, Math.Min(100, value))
        End Function

        Public Shared Function ClampRtxHdrMaxLuminance(value As Integer) As Integer
            Return Math.Max(400, Math.Min(2000, value))
        End Function

        ''' <summary>
        ''' 配置丢失或旧平铺路径失效时，优先从 Plugin\videoenhancer 子目录自动发现，再兼容旧路径。
        ''' </summary>
        Public Shared Function ResolveInstalledExePath(Optional configuredPath As String = "") As String
            If Not String.IsNullOrWhiteSpace(configuredPath) Then
                Try
                    Dim configuredFullPath = Path.GetFullPath(configuredPath)
                    Dim configuredDirectory = Path.GetDirectoryName(configuredFullPath)
                    If Not String.IsNullOrWhiteSpace(configuredDirectory) AndAlso
                       Not Path.GetFileName(configuredDirectory).Equals("videoenhancer", StringComparison.OrdinalIgnoreCase) Then
                        Dim migratedPath = Path.Combine(configuredDirectory, "videoenhancer", "videoenhancer.exe")
                        If File.Exists(migratedPath) Then Return Path.GetFullPath(migratedPath)
                    End If
                    If File.Exists(configuredFullPath) Then Return configuredFullPath
                Catch
                End Try
            End If
            Dim candidates As New Collections.Generic.List(Of String)()
            Try
                AddLayoutCandidates(candidates, AppContext.BaseDirectory)
            Catch
            End Try
            Try
                Dim assemblyDir = Path.GetDirectoryName(GetType(PluginConfig).Assembly.Location)
                If Not String.IsNullOrWhiteSpace(assemblyDir) Then
                    AddLayoutCandidates(candidates, assemblyDir)
                    Dim hostDir = Directory.GetParent(assemblyDir)
                    If hostDir IsNot Nothing Then AddLayoutCandidates(candidates, hostDir.FullName)
                End If
            Catch
            End Try
            Try
                Dim processPath = Environment.ProcessPath
                If Not String.IsNullOrWhiteSpace(processPath) Then
                    AddLayoutCandidates(candidates, Path.GetDirectoryName(processPath))
                End If
            Catch
            End Try
            Try
                AddLayoutCandidates(candidates, Environment.CurrentDirectory)
            Catch
            End Try
            For Each candidate In candidates
                Try
                    If File.Exists(candidate) Then Return Path.GetFullPath(candidate)
                Catch
                End Try
            Next
            Return ""
        End Function

        ''' <summary>由已安装 EXE 反推出承载 DLL 的 Plugin 根目录，同时兼容旧平铺布局。</summary>
        Public Shared Function ResolvePluginRoot(exePath As String) As String
            If String.IsNullOrWhiteSpace(exePath) Then Return ""
            Try
                Dim exeDirectory = Path.GetDirectoryName(Path.GetFullPath(exePath))
                If String.IsNullOrWhiteSpace(exeDirectory) Then Return ""
                If Path.GetFileName(exeDirectory).Equals("videoenhancer", StringComparison.OrdinalIgnoreCase) Then
                    Dim parent = Directory.GetParent(exeDirectory)
                    If parent IsNot Nothing Then Return parent.FullName
                End If
                Return exeDirectory
            Catch
                Return ""
            End Try
        End Function

        Private Shared Sub AddLayoutCandidates(candidates As Collections.Generic.List(Of String), root As String)
            If String.IsNullOrWhiteSpace(root) Then Return
            candidates.Add(Path.Combine(root, "videoenhancer", "videoenhancer.exe"))
            candidates.Add(Path.Combine(root, "videoenhancer.exe"))
        End Sub

        Public Sub Save()
            Try
                Directory.CreateDirectory(ConfigDir)
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Me, New JsonSerializerOptions With {.WriteIndented = True}))
            Catch
            End Try
        End Sub

    End Class

    Public Class ShellUpscaleModel
        Public Property Backend As String = "ncnn"
        Public Property Model As String = ""
        Public Property DisplayName As String = ""
    End Class

    Public Class SegmentedVideoConfig
        Public Property Path As String = ""
        Public Property FrameCount As Long
        Public Property DurationSeconds As Double
        Public Property SourceWidth As Integer
        Public Property SourceHeight As Integer
        Public Property BoundaryMode As String = ""
        Public Property AllowMixedModelBackends As Boolean = False
        Public Property Enabled As Boolean
        Public Property Segments As New Collections.Generic.List(Of SegmentedUpscaleRange)()
    End Class

    Public Class SegmentedUpscaleRange
        Public Property Start As Long
        Public Property [End] As Long
        Public Property StartSeconds As Double
        Public Property EndSeconds As Double
        Public Property Backend As String = ""
        Public Property Model As String = ""
        Public Property DisplayName As String = ""
        Public Property Scale As Integer
        Public Property TargetWidth As Integer
        Public Property TargetHeight As Integer
    End Class

End Namespace
