Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports LakeUI

Namespace videoenhancer

    Public Partial Class PluginPanel

        Private ReadOnly _pageSegmented As New ModernPanel()
        Private _segmentRoot As ModernPanel
        Private ReadOnly _cmbSegmentVideo As New WheelLockedComboBox()
        Private ReadOnly _cmbSegmentMode As New WheelLockedComboBox()
        Private ReadOnly _switchSegmented As New LakeUI.BooleanSwitch()
        Private ReadOnly _lblSegmentedSwitch As New HtmlColorLabel()
        Private ReadOnly _switchMixedSegmentBackends As New LakeUI.BooleanSwitch()
        Private ReadOnly _lblMixedSegmentBackends As New HtmlColorLabel()
        Private ReadOnly _btnSegmentRefresh As New ModernButton()
        Private ReadOnly _btnSegmentAdd As New ModernButton()
        Private ReadOnly _segmentRowsPanel As New ModernPanel()
        Private ReadOnly _lblSegmentStatus As New HtmlColorLabel()
        Private ReadOnly _segmentVideoPaths As New List(Of String)()
        Private ReadOnly _segmentModelChoices As New List(Of SegmentModelChoice)()
        Private ReadOnly _segmentVideoProbes As New Dictionary(Of String, SegmentVideoProbe)(StringComparer.OrdinalIgnoreCase)
        Private _segmentModelsLoaded As Boolean
        Private _segmentModelsLoading As Boolean
        Private _segmentVideosLoading As Boolean
        Private _segmentSync As Boolean

        Private NotInheritable Class SegmentVideoProbe
            Public Property Path As String = ""
            Public Property FrameCount As Long
            Public Property DurationSeconds As Double
            Public Property Width As Integer
            Public Property Height As Integer
            Public Property FrameRate As Double
            Public Property Keyframes As New List(Of Double)()
        End Class

        Private NotInheritable Class SegmentModelChoice
            Public Property Backend As String = ""
            Public Property Model As String = ""
            Public Property DisplayName As String = ""
            Public Property Scale As Integer

            Public Overrides Function ToString() As String
                If Scale > 0 Then
                    Return SegmentBackendDisplayName(Backend) & " · " & DisplayName & " · " & Scale & "x"
                End If
                Return SegmentBackendDisplayName(Backend) & " · " & DisplayName & " · 自定义分辨率"
            End Function
        End Class

        Private NotInheritable Class SegmentRowControls
            Public Property Index As Integer
            Public Property StartBox As ModernTextBox
            Public Property EndBox As ModernTextBox
            Public Property ModelBox As WheelLockedComboBox
            Public Property WidthBox As ModernTextBox
            Public Property HeightBox As ModernTextBox
            Public Property Choices As List(Of SegmentModelChoice)
        End Class

        Private Shared Function SegmentBackendDisplayName(backend As String) As String
            Select Case If(backend, "").ToLowerInvariant()
                Case "cuda" : Return "CUDA"
                Case "tensorrt" : Return "TensorRT"
                Case "onnx" : Return "ONNX"
                Case "ffmpeg" : Return "FFmpeg 硬拉"
                Case "anime4k" : Return "Anime4K"
                Case Else : Return "NCNN"
            End Select
        End Function

        Private Shared Function IsSegmentModelBackend(backend As String) As Boolean
            Dim value = If(backend, "").Trim().ToLowerInvariant()
            Return value = "ncnn" OrElse value = "cuda" OrElse value = "tensorrt" OrElse value = "onnx"
        End Function

        Private Shared Function IsSegmentCustomBackend(backend As String) As Boolean
            Dim value = If(backend, "").Trim().ToLowerInvariant()
            Return value = "ffmpeg" OrElse value = "anime4k"
        End Function

        Private Sub BuildOfficialSegmentedPage()
            _pageSegmented.Dock = DockStyle.Fill
            _pageSegmented.LayoutMode = ModernPanel.LayoutModeEnum.Absolute
            Dim root As New ModernPanel With {
                .Dock = DockStyle.None,
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left,
                .AutoSize = False,
                .MinimumSize = New Size(0, 884),
                .Height = 884,
                .BackColor = Color.Transparent,
                .BackColor1 = Color.Transparent,
                .LayoutMode = ModernPanel.LayoutModeEnum.Absolute,
                .BorderSize = 0
            }
            _segmentRoot = root
            AddHandler _pageSegmented.ClientSizeChanged, Sub(sender, e) SyncSegmentedRootBounds()
            AddHandler _pageSegmented.SizeChanged, Sub(sender, e) SyncSegmentedRootBounds()
            AddWorkbenchRow(root, CreateOfficialSectionHeading(
                "分段超分设置",
                "默认按秒分段并自动吸附附近关键帧；固定倍率模型优先决定全片输出尺寸，FFmpeg / Anime4K 自动跟随；仅自定义处理时可设置目标宽高"), 12, 46)

            _cmbSegmentVideo.WaterText = "切换到本页后读取 3FUI 添加文件列表…"
            ConfigureCombo(_cmbSegmentVideo)
            AddHandler _cmbSegmentVideo.SelectedIndexChanged, AddressOf OnSegmentVideoSelected
            Dim videoField = CreateOfficialField("视频", _cmbSegmentVideo)
            ConfigureSecondaryButton(_btnSegmentRefresh)
            _btnSegmentRefresh.Text = "刷新视频列表"
            _btnSegmentRefresh.Dock = DockStyle.Fill
            _btnSegmentRefresh.Margin = New Padding(0, 6, 0, 6)
            AddHandler _btnSegmentRefresh.Click, Sub(sender, e) RefreshSegmentedVideos()
            AddWorkbenchControl(root, videoField, 70, 76, 0.0F, 0.78F, 0, -12)
            AddWorkbenchControl(root, _btnSegmentRefresh, 70, 76, 0.78F, 1.0F)

            ConfigureCombo(_cmbSegmentMode)
            _cmbSegmentMode.Items.Add("按秒（默认，断点自动吸附关键帧）")
            _cmbSegmentMode.Items.Add("精确帧（兼容旧模式）")
            AddHandler _cmbSegmentMode.SelectedIndexChanged, AddressOf OnSegmentModeChanged
            AddWorkbenchControl(root, CreateOfficialField("分段计数模式", _cmbSegmentMode), 150, 72, 0.0F, 1.0F)

            ConfigureDpiSwitch(_switchSegmented)
            AddHandler _switchSegmented.CheckedChanged, AddressOf OnSegmentedSwitchChanged
            _lblSegmentedSwitch.AutoSize = False
            _lblSegmentedSwitch.TextAlign = HtmlColorLabel.TextAlignEnum.MiddleLeft
            Dim switchRow As New ModernHorizontalPanel(150.0F, 12.0F, 60.0F, 16.0F, -1.0F, 12.0F, 150.0F)
            Dim switchCaption = CreateOfficialCaption("分段总开关")
            switchCaption.Dock = DockStyle.Fill
            switchCaption.TextAlign = ContentAlignment.MiddleLeft
            _switchSegmented.Dock = DockStyle.None
            _switchSegmented.Anchor = AnchorStyles.Left
            _switchSegmented.Margin = New Padding(0, 16, 0, 0)
            ConfigurePrimaryButton(_btnSegmentAdd)
            _btnSegmentAdd.Text = "＋ 添加断点"
            _btnSegmentAdd.Dock = DockStyle.Fill
            _btnSegmentAdd.Margin = New Padding(0, 6, 0, 6)
            AddHandler _btnSegmentAdd.Click, AddressOf OnAddSegment
            switchRow.AddColumn(switchCaption, 0)
            switchRow.AddColumn(_switchSegmented, 2)
            switchRow.AddColumn(_lblSegmentedSwitch, 4)
            switchRow.AddColumn(_btnSegmentAdd, 6)
            AddWorkbenchRow(root, switchRow, 226, 54)

            ConfigureDpiSwitch(_switchMixedSegmentBackends)
            AddHandler _switchMixedSegmentBackends.CheckedChanged, AddressOf OnMixedSegmentBackendsChanged
            _lblMixedSegmentBackends.AutoSize = False
            _lblMixedSegmentBackends.TextAlign = HtmlColorLabel.TextAlignEnum.MiddleLeft
            Dim mixedBackendRow As New ModernHorizontalPanel(210.0F, 12.0F, 60.0F, 16.0F, -1.0F)
            Dim mixedBackendCaption = CreateOfficialCaption("测试功能：跨模型后端混用")
            mixedBackendCaption.Dock = DockStyle.Fill
            mixedBackendCaption.TextAlign = ContentAlignment.MiddleLeft
            _switchMixedSegmentBackends.Dock = DockStyle.None
            _switchMixedSegmentBackends.Anchor = AnchorStyles.Left
            _switchMixedSegmentBackends.Margin = New Padding(0, 16, 0, 0)
            mixedBackendRow.AddColumn(mixedBackendCaption, 0)
            mixedBackendRow.AddColumn(_switchMixedSegmentBackends, 2)
            mixedBackendRow.AddColumn(_lblMixedSegmentBackends, 4)
            AddWorkbenchRow(root, mixedBackendRow, 290, 54)

            Dim header As New ModernHorizontalPanel(90.0F, 10.0F, 90.0F, 10.0F, -1.0F, 10.0F, 90.0F, 10.0F, 90.0F, 10.0F, 74.0F)
            For Each caption In New String() {"入点（秒/帧）", "出点（秒/帧）", "处理方式", "目标宽", "目标高", "操作"}
                Dim label = CreateOfficialCaption(caption)
                label.Dock = DockStyle.Fill
                label.TextAlign = ContentAlignment.MiddleLeft
                header.AddColumn(label, header.Controls.Count * 2)
            Next
            AddWorkbenchRow(root, header, 354, 34)

            _segmentRowsPanel.BackColor = Color.Transparent
            _segmentRowsPanel.BackColor1 = Color.Transparent
            _segmentRowsPanel.BorderColor = Color.FromArgb(52, 52, 52)
            _segmentRowsPanel.BorderSize = 1
            _segmentRowsPanel.BorderRadius = 6
            _segmentRowsPanel.LayoutMode = ModernPanel.LayoutModeEnum.Absolute
            _segmentRowsPanel.AutoScroll = True
            AddWorkbenchRow(root, _segmentRowsPanel, 388, 396)
            AddHandler _segmentRowsPanel.ClientSizeChanged, AddressOf OnSegmentRowsPanelResized

            _lblSegmentStatus.AutoSize = False
            _lblSegmentStatus.TextAlign = HtmlColorLabel.TextAlignEnum.MiddleLeft
            _lblSegmentStatus.Text = "<font color=#888888>切换到本页后会读取视频时长、尺寸和关键帧。</font>"
            AddWorkbenchRow(root, CreateOfficialValueBox(_lblSegmentStatus), 802, 70)
            _pageSegmented.Controls.Add(root)
            EnsureBuiltinSegmentChoices()
            SyncSegmentedRootBounds()
        End Sub

        Private Sub SyncSegmentedRootBounds()
            Dim root = _segmentRoot
            If root Is Nothing OrElse root.IsDisposed OrElse
               _pageSegmented Is Nothing OrElse _pageSegmented.IsDisposed Then Return
            Dim availableWidth = Math.Max(_pageSegmented.Width, _pageSegmented.ClientSize.Width)
            availableWidth = Math.Max(availableWidth, Math.Max(_tabs.Width, _tabs.ClientSize.Width))
            If ModernPanel1 IsNot Nothing AndAlso Not ModernPanel1.IsDisposed Then
                availableWidth = Math.Max(availableWidth,
                    ModernPanel1.ClientSize.Width - ModernPanel1.Padding.Left - ModernPanel1.Padding.Right)
            End If
            Dim width = Math.Max(0, availableWidth - _pageSegmented.ScrollBarWidth - 2)
            If root.Left <> 0 OrElse root.Top <> 0 OrElse root.Width <> width OrElse root.Height <> 884 Then
                root.SetBounds(0, 0, width, 884)
            End If
        End Sub

        Private Sub ActivateSegmentedPage()
            LoadSegmentModelCatalogs()
            RefreshSegmentedVideos()
        End Sub

        Private Sub EnsureBuiltinSegmentChoices()
            If Not _segmentModelChoices.Any(Function(choice) choice.Backend = "ffmpeg") Then
                For Each pair In New (String, String)() {
                    ("lanczos", "Lanczos"),
                    ("bicubic", "Bicubic"),
                    ("bilinear", "Bilinear"),
                    ("neighbor", "Nearest Neighbour"),
                    ("area", "Area"),
                    ("spline", "Spline"),
                    ("fast_bilinear", "Fast Bilinear"),
                    ("bicublin", "Bicubic Luma / Bilinear Chroma")
                }
                    _segmentModelChoices.Add(New SegmentModelChoice With {
                        .Backend = "ffmpeg", .Model = pair.Item1, .DisplayName = pair.Item2, .Scale = 0
                    })
                Next
            End If
            If Not _segmentModelChoices.Any(Function(choice) choice.Backend = "anime4k") Then
                For Each pair In New (String, String)() {
                    ("anime4k-v4-a.glsl", "Anime4K v4 Mode A"),
                    ("anime4k-v4-a+a.glsl", "Anime4K v4 Mode A+A"),
                    ("anime4k-v4-b.glsl", "Anime4K v4 Mode B"),
                    ("anime4k-v4-b+b.glsl", "Anime4K v4 Mode B+B"),
                    ("anime4k-v4-c.glsl", "Anime4K v4 Mode C"),
                    ("anime4k-v4-c+a.glsl", "Anime4K v4 Mode C+A"),
                    ("anime4k-v4.1-gan.glsl", "Anime4K v4.1 GAN")
                }
                    _segmentModelChoices.Add(New SegmentModelChoice With {
                        .Backend = "anime4k", .Model = pair.Item1, .DisplayName = pair.Item2, .Scale = 0
                    })
                Next
            End If
        End Sub

        Private Async Sub LoadSegmentModelCatalogs()
            EnsureBuiltinSegmentChoices()
            If _segmentModelsLoaded OrElse _segmentModelsLoading Then Return
            Dim exePath = PluginConfig.ResolveInstalledExePath(_config.ExePath)
            If String.IsNullOrWhiteSpace(exePath) OrElse Not File.Exists(exePath) Then
                RenderSegmentRows()
                Return
            End If
            _segmentModelsLoading = True
            Try
                Dim choices = Await Task.Run(Function()
                    Dim result As New List(Of SegmentModelChoice)()
                    For Each backend In New String() {"ncnn", "cuda", "tensorrt", "onnx"}
                        For Each item In RunModelCatalog(exePath, "--list-model-catalog", "-backend", backend)
                            Dim scale = If(item.Scale > 0, item.Scale, InferSegmentScale(item.Id))
                            If scale <= 0 Then Continue For
                            result.Add(New SegmentModelChoice With {
                                .Backend = backend,
                                .Model = item.Id,
                                .DisplayName = If(String.IsNullOrWhiteSpace(item.DisplayName), item.Id, item.DisplayName),
                                .Scale = scale
                            })
                        Next
                    Next
                    Return result
                End Function)
                _segmentModelChoices.RemoveAll(Function(choice) IsSegmentModelBackend(choice.Backend))
                _segmentModelChoices.AddRange(choices)
                _segmentModelsLoaded = True
                RenderSegmentRows()
            Finally
                _segmentModelsLoading = False
            End Try
        End Sub

        Private Shared Function InferSegmentScale(model As String) As Integer
            Dim match = Regex.Match(If(model, ""), "(?:^|[-_])(\d+)x(?:[-_]|$)|(?:^|[-_])x(\d+)(?:[-_]|$)", RegexOptions.IgnoreCase)
            If Not match.Success Then Return 0
            Dim text = If(match.Groups(1).Success, match.Groups(1).Value, match.Groups(2).Value)
            Dim value As Integer
            Return If(Integer.TryParse(text, value), value, 0)
        End Function

        Private Async Sub RefreshSegmentedVideos()
            If _segmentVideosLoading Then Return
            _segmentVideosLoading = True
            _btnSegmentRefresh.Enabled = False
            _lblSegmentStatus.Text = "<font color=#B8B8B8>正在读取视频时长、尺寸和关键帧…</font>"
            Try
                Dim paths = QueueHook.GetCurrentPrepareFilePaths().
                    Where(Function(path) Not String.IsNullOrWhiteSpace(path) AndAlso File.Exists(path)).
                    Select(Function(filePath) IO.Path.GetFullPath(filePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                Dim exePath = PluginConfig.ResolveInstalledExePath(_config.ExePath)
                Dim ffprobe = ResolveSegmentFfprobe(exePath)
                Dim probed = Await Task.Run(Function()
                    Dim result As New List(Of SegmentVideoProbe)()
                    For Each filePath As String In paths
                        Dim probe = ProbeSegmentVideo(ffprobe, filePath)
                        If probe IsNot Nothing Then result.Add(probe)
                    Next
                    Return result
                End Function)

                Dim previousPath = SelectedSegmentVideoPath()
                _segmentSync = True
                _cmbSegmentVideo.Items.Clear()
                _segmentVideoPaths.Clear()
                _segmentVideoProbes.Clear()
                For Each probe In probed
                    _segmentVideoPaths.Add(probe.Path)
                    _segmentVideoProbes(probe.Path) = probe
                    _cmbSegmentVideo.Items.Add(
                        Path.GetFileName(probe.Path) & "　（" &
                        FormatSegmentSeconds(probe.DurationSeconds) & " 秒 · " &
                        probe.Width & "×" & probe.Height & " · 关键帧 " & probe.Keyframes.Count & "）")
                    EnsureSegmentVideoConfig(probe)
                Next
                Dim selectedIndex = If(previousPath.Length = 0, -1, _segmentVideoPaths.FindIndex(
                    Function(path) String.Equals(path, previousPath, StringComparison.OrdinalIgnoreCase)))
                If selectedIndex < 0 AndAlso _segmentVideoPaths.Count > 0 Then selectedIndex = 0
                _cmbSegmentVideo.SelectedIndex = selectedIndex
                _segmentSync = False
                _config.Save()
                RefreshSegmentSelection()
                If probed.Count = 0 Then
                    _lblSegmentStatus.Text = If(String.IsNullOrWhiteSpace(ffprobe),
                        "<font color=#E07878>未找到 ffprobe，无法读取分段视频信息。</font>",
                        "<font color=#E07878>添加文件列表中没有可读取的视频。</font>")
                End If
            Catch ex As Exception
                _segmentSync = False
                _lblSegmentStatus.Text = "<font color=#E07878>视频检测失败：" & EscapeHtml(ex.Message) & "</font>"
            Finally
                _segmentVideosLoading = False
                _btnSegmentRefresh.Enabled = True
            End Try
        End Sub

        Private Shared Function ResolveSegmentFfprobe(exePath As String) As String
            Dim candidates As New List(Of String)()
            If Not String.IsNullOrWhiteSpace(exePath) Then
                Try
                    Dim directory = Path.GetDirectoryName(Path.GetFullPath(exePath))
                    For depth = 0 To 3
                        If String.IsNullOrWhiteSpace(directory) Then Exit For
                        candidates.Add(Path.Combine(directory, "ffprobe.exe"))
                        candidates.Add(Path.Combine(directory, "bin", "ffmpeg", "ffprobe.exe"))
                        Dim parent = IO.Directory.GetParent(directory)
                        directory = If(parent Is Nothing, "", parent.FullName)
                    Next
                Catch
                End Try
            End If
            Dim pathValue = Environment.GetEnvironmentVariable("PATH")
            If Not String.IsNullOrWhiteSpace(pathValue) Then
                For Each directory In pathValue.Split(Path.PathSeparator)
                    If Not String.IsNullOrWhiteSpace(directory) Then candidates.Add(Path.Combine(directory.Trim(), "ffprobe.exe"))
                Next
            End If
            For Each candidate In candidates
                Try
                    If File.Exists(candidate) Then Return Path.GetFullPath(candidate)
                Catch
                End Try
            Next
            Return ""
        End Function

        Private Shared Function ProbeSegmentVideo(ffprobe As String, source As String) As SegmentVideoProbe
            If String.IsNullOrWhiteSpace(ffprobe) OrElse Not File.Exists(ffprobe) Then Return Nothing
            Dim metadata = RunSegmentProbe(ffprobe, New String() {
                "-v", "error", "-select_streams", "v:0",
                "-show_entries", "stream=width,height,nb_frames,avg_frame_rate,r_frame_rate,duration:format=duration",
                "-of", "json", source
            }, 120000)
            If String.IsNullOrWhiteSpace(metadata) Then Return Nothing
            Dim result As New SegmentVideoProbe With {.Path = Path.GetFullPath(source)}
            Using document = JsonDocument.Parse(metadata)
                Dim streams = document.RootElement.GetProperty("streams")
                If streams.GetArrayLength() = 0 Then Return Nothing
                Dim stream = streams(0)
                result.Width = stream.GetProperty("width").GetInt32()
                result.Height = stream.GetProperty("height").GetInt32()
                result.FrameRate = ParseSegmentRate(GetProbeString(stream, "avg_frame_rate"))
                If result.FrameRate <= 0 Then result.FrameRate = ParseSegmentRate(GetProbeString(stream, "r_frame_rate"))
                result.DurationSeconds = ParseProbeDouble(GetProbeString(stream, "duration"))
                If result.DurationSeconds <= 0 Then
                    Dim format As JsonElement
                    If document.RootElement.TryGetProperty("format", format) Then
                        result.DurationSeconds = ParseProbeDouble(GetProbeString(format, "duration"))
                    End If
                End If
                Dim frames As Long
                Dim frameText = GetProbeString(stream, "nb_frames")
                If Long.TryParse(frameText, NumberStyles.Integer, CultureInfo.InvariantCulture, frames) Then
                    result.FrameCount = frames
                End If
            End Using
            If result.Width <= 0 OrElse result.Height <= 0 OrElse result.DurationSeconds <= 0 OrElse result.FrameRate <= 0 Then Return Nothing
            If result.FrameCount <= 0 Then result.FrameCount = Math.Max(1, CLng(Math.Round(result.DurationSeconds * result.FrameRate)))

            Dim keyframeJson = RunSegmentProbe(ffprobe, New String() {
                "-v", "error", "-skip_frame", "nokey", "-select_streams", "v:0",
                "-show_frames", "-show_entries", "frame=best_effort_timestamp_time,pts_time,pkt_dts_time",
                "-of", "json", source
            }, 300000)
            result.Keyframes.Add(0)
            If Not String.IsNullOrWhiteSpace(keyframeJson) Then
                Using document = JsonDocument.Parse(keyframeJson)
                    Dim frames = document.RootElement.GetProperty("frames")
                    For Each frame In frames.EnumerateArray()
                        Dim timestamp As Double = -1
                        For Each propertyName In New String() {"best_effort_timestamp_time", "pts_time", "pkt_dts_time"}
                            timestamp = ParseProbeDouble(GetProbeString(frame, propertyName))
                            If timestamp >= 0 Then Exit For
                        Next
                        If timestamp >= 0 AndAlso timestamp < result.DurationSeconds Then result.Keyframes.Add(timestamp)
                    Next
                End Using
            End If
            result.Keyframes = result.Keyframes.Distinct().
                OrderBy(Function(value) value).ToList()
            Return result
        End Function

        Private Shared Function RunSegmentProbe(executable As String, arguments As IEnumerable(Of String), timeoutMs As Integer) As String
            Dim info As New ProcessStartInfo With {
                .FileName = executable,
                .UseShellExecute = False,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .CreateNoWindow = True,
                .StandardOutputEncoding = Encoding.UTF8,
                .StandardErrorEncoding = Encoding.UTF8
            }
            For Each argument In arguments
                info.ArgumentList.Add(argument)
            Next
            Using child = Process.Start(info)
                If child Is Nothing Then Return ""
                Dim output = child.StandardOutput.ReadToEnd()
                Dim errorText = child.StandardError.ReadToEnd()
                If Not child.WaitForExit(timeoutMs) Then
                    Try
                        child.Kill(True)
                    Catch
                    End Try
                    Return ""
                End If
                If child.ExitCode <> 0 Then
                    Trace.WriteLine("[VideoEnhancer][分段] ffprobe 失败：" & errorText)
                    Return ""
                End If
                Return output
            End Using
        End Function

        Private Shared Function GetProbeString(element As JsonElement, propertyName As String) As String
            Dim value As JsonElement
            If Not element.TryGetProperty(propertyName, value) Then Return ""
            If value.ValueKind = JsonValueKind.String Then Return If(value.GetString(), "")
            If value.ValueKind = JsonValueKind.Number Then Return value.GetRawText()
            Return ""
        End Function

        Private Shared Function ParseProbeDouble(value As String) As Double
            Dim result As Double
            Return If(Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, result), result, -1)
        End Function

        Private Shared Function ParseSegmentRate(value As String) As Double
            If String.IsNullOrWhiteSpace(value) Then Return 0
            Dim parts = value.Split("/"c)
            If parts.Length = 2 Then
                Dim numerator As Double
                Dim denominator As Double
                If Double.TryParse(parts(0), NumberStyles.Float, CultureInfo.InvariantCulture, numerator) AndAlso
                   Double.TryParse(parts(1), NumberStyles.Float, CultureInfo.InvariantCulture, denominator) AndAlso
                   denominator <> 0 Then Return numerator / denominator
            End If
            Return Math.Max(0, ParseProbeDouble(value))
        End Function

        Private Shared Function ProbeSegmentFrameCount(ffprobe As String, source As String) As Long
            If String.IsNullOrWhiteSpace(ffprobe) OrElse Not File.Exists(ffprobe) Then Return 0
            Dim output = RunSegmentProbe(ffprobe, New String() {
                "-v", "error", "-select_streams", "v:0", "-count_frames",
                "-show_entries", "stream=nb_read_frames,nb_frames", "-of", "json", source
            }, 300000)
            If String.IsNullOrWhiteSpace(output) Then Return 0
            Using document = JsonDocument.Parse(output)
                Dim streams = document.RootElement.GetProperty("streams")
                If streams.GetArrayLength() = 0 Then Return 0
                Dim stream = streams(0)
                For Each propertyName In New String() {"nb_read_frames", "nb_frames"}
                    Dim frames As Long
                    If Long.TryParse(GetProbeString(stream, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, frames) AndAlso frames > 0 Then
                        Return frames
                    End If
                Next
            End Using
            Return 0
        End Function

        Private Function EnsureSegmentVideoConfig(probe As SegmentVideoProbe) As SegmentedVideoConfig
            If _config.SegmentedVideos Is Nothing Then _config.SegmentedVideos = New List(Of SegmentedVideoConfig)()
            Dim config = _config.SegmentedVideos.FirstOrDefault(
                Function(item) String.Equals(item.Path, probe.Path, StringComparison.OrdinalIgnoreCase))
            If config Is Nothing Then
                config = New SegmentedVideoConfig With {
                    .Path = probe.Path,
                    .FrameCount = probe.FrameCount,
                    .DurationSeconds = probe.DurationSeconds,
                    .SourceWidth = probe.Width,
                    .SourceHeight = probe.Height,
                    .BoundaryMode = "seconds"
                }
                config.Segments.Add(New SegmentedUpscaleRange With {
                    .StartSeconds = 0,
                    .EndSeconds = probe.DurationSeconds,
                    .TargetWidth = probe.Width * 2,
                    .TargetHeight = probe.Height * 2
                })
                _config.SegmentedVideos.Add(config)
                Return config
            End If

            config.Path = probe.Path
            config.DurationSeconds = probe.DurationSeconds
            config.SourceWidth = probe.Width
            config.SourceHeight = probe.Height
            If config.FrameCount <= 0 Then config.FrameCount = probe.FrameCount
            If config.Segments Is Nothing Then config.Segments = New List(Of SegmentedUpscaleRange)()
            If String.IsNullOrWhiteSpace(config.BoundaryMode) Then
                config.BoundaryMode = If(config.Segments.Any(
                    Function(segment) segment.Start > 0 OrElse segment.[End] > 0), "frames", "seconds")
            End If
            If config.Segments.Count = 0 Then
                If String.Equals(config.BoundaryMode, "frames", StringComparison.OrdinalIgnoreCase) Then
                    config.Segments.Add(New SegmentedUpscaleRange With {.Start = 1, .End = config.FrameCount})
                Else
                    config.Segments.Add(New SegmentedUpscaleRange With {
                        .StartSeconds = 0, .EndSeconds = probe.DurationSeconds,
                        .TargetWidth = probe.Width * 2, .TargetHeight = probe.Height * 2
                    })
                End If
            End If
            If String.Equals(config.BoundaryMode, "seconds", StringComparison.OrdinalIgnoreCase) Then
                config.Segments(0).StartSeconds = 0
                config.Segments(config.Segments.Count - 1).EndSeconds = probe.DurationSeconds
                SnapAllSegmentBoundaries(config, probe)
                ApplySegmentResolutionRule(config)
            Else
                config.Segments(0).Start = 1
                config.Segments(config.Segments.Count - 1).[End] = config.FrameCount
            End If
            Return config
        End Function

        Private Function SelectedSegmentVideoPath() As String
            If _cmbSegmentVideo.SelectedIndex < 0 OrElse _cmbSegmentVideo.SelectedIndex >= _segmentVideoPaths.Count Then Return ""
            Return _segmentVideoPaths(_cmbSegmentVideo.SelectedIndex)
        End Function

        Private Function SelectedSegmentProbe() As SegmentVideoProbe
            Dim path = SelectedSegmentVideoPath()
            Dim probe As SegmentVideoProbe = Nothing
            If path.Length > 0 Then _segmentVideoProbes.TryGetValue(path, probe)
            Return probe
        End Function

        Private Function SelectedSegmentConfig() As SegmentedVideoConfig
            Dim path = SelectedSegmentVideoPath()
            If path.Length = 0 OrElse _config.SegmentedVideos Is Nothing Then Return Nothing
            Return _config.SegmentedVideos.FirstOrDefault(
                Function(item) String.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
        End Function

        Private Sub OnSegmentVideoSelected(sender As Object, e As EventArgs)
            If _segmentSync Then Return
            RefreshSegmentSelection()
        End Sub

        Private Sub RefreshSegmentSelection()
            Dim config = SelectedSegmentConfig()
            _segmentSync = True
            _switchSegmented.Enabled = config IsNot Nothing AndAlso _config.Enabled
            _switchSegmented.Checked = config IsNot Nothing AndAlso config.Enabled
            _switchMixedSegmentBackends.Enabled = config IsNot Nothing
            _switchMixedSegmentBackends.Checked = config IsNot Nothing AndAlso config.AllowMixedModelBackends
            _btnSegmentAdd.Enabled = config IsNot Nothing AndAlso
                (config.DurationSeconds > 0 OrElse config.FrameCount > 1)
            If config Is Nothing Then
                _cmbSegmentMode.SelectedIndex = -1
            Else
                _cmbSegmentMode.SelectedIndex = If(String.Equals(config.BoundaryMode, "frames", StringComparison.OrdinalIgnoreCase), 1, 0)
            End If
            _cmbSegmentMode.Enabled = config IsNot Nothing
            _segmentSync = False
            RefreshSegmentSwitchText()
            RefreshMixedSegmentBackendText()
            RenderSegmentRows()
            ValidateAndShowSegmentConfig()
        End Sub

        Private Async Sub OnSegmentModeChanged(sender As Object, e As EventArgs)
            If _segmentSync Then Return
            Dim config = SelectedSegmentConfig()
            Dim probe = SelectedSegmentProbe()
            If config Is Nothing OrElse probe Is Nothing OrElse _cmbSegmentMode.SelectedIndex < 0 Then Return
            Dim requestedMode = If(_cmbSegmentMode.SelectedIndex = 1, "frames", "seconds")
            If String.Equals(config.BoundaryMode, requestedMode, StringComparison.OrdinalIgnoreCase) Then Return

            If requestedMode = "frames" Then
                _cmbSegmentMode.Enabled = False
                _lblSegmentStatus.Text = "<font color=#B8B8B8>正在读取精确帧数…</font>"
                Dim exePath = PluginConfig.ResolveInstalledExePath(_config.ExePath)
                Dim ffprobe = ResolveSegmentFfprobe(exePath)
                Dim exactFrames = Await Task.Run(Function() ProbeSegmentFrameCount(ffprobe, config.Path))
                _cmbSegmentMode.Enabled = True
                If exactFrames <= 0 Then
                    _segmentSync = True
                    _cmbSegmentMode.SelectedIndex = 0
                    _segmentSync = False
                    _lblSegmentStatus.Text = "<font color=#E07878>无法读取精确帧数，已保留按秒模式。</font>"
                    Return
                End If
                config.FrameCount = exactFrames
                ConvertSegmentSecondsToFrames(config)
                config.BoundaryMode = "frames"
            Else
                config.BoundaryMode = "seconds"
                ConvertSegmentFramesToSeconds(config, probe)
            End If
            _config.Save()
            RenderSegmentRows()
            ValidateAndShowSegmentConfig()
        End Sub

        Private Sub ConvertSegmentSecondsToFrames(config As SegmentedVideoConfig)
            If config.FrameCount <= 0 OrElse config.DurationSeconds <= 0 Then Return
            Dim expected As Long = 1
            For index = 0 To config.Segments.Count - 1
                Dim segment = config.Segments(index)
                segment.Start = expected
                If index = config.Segments.Count - 1 Then
                    segment.[End] = config.FrameCount
                Else
                    segment.[End] = Math.Max(expected,
                        Math.Min(config.FrameCount - 1,
                            CLng(Math.Round(segment.EndSeconds / config.DurationSeconds * config.FrameCount))))
                End If
                expected = segment.[End] + 1
            Next
        End Sub

        Private Sub ConvertSegmentFramesToSeconds(config As SegmentedVideoConfig, probe As SegmentVideoProbe)
            If config.FrameCount <= 0 OrElse probe.DurationSeconds <= 0 Then Return
            Dim expected As Double = 0
            For index = 0 To config.Segments.Count - 1
                Dim segment = config.Segments(index)
                segment.StartSeconds = expected
                If index = config.Segments.Count - 1 Then
                    segment.EndSeconds = probe.DurationSeconds
                Else
                    Dim desired = CDbl(segment.[End]) / config.FrameCount * probe.DurationSeconds
                    Dim snapped = SnapSegmentBoundary(probe, desired, expected, probe.DurationSeconds)
                    If snapped < 0 Then snapped = desired
                    segment.EndSeconds = snapped
                End If
                expected = segment.EndSeconds
            Next
            config.DurationSeconds = probe.DurationSeconds
            config.SourceWidth = probe.Width
            config.SourceHeight = probe.Height
            SnapAllSegmentBoundaries(config, probe)
            ApplySegmentResolutionRule(config)
        End Sub

        Private Sub OnSegmentedSwitchChanged(sender As Object, e As EventArgs)
            If _segmentSync Then Return
            Dim config = SelectedSegmentConfig()
            If config Is Nothing Then Return
            If _switchSegmented.Checked AndAlso Not _config.Enabled Then
                _segmentSync = True
                _switchSegmented.Checked = False
                _segmentSync = False
                ShowStatus("请先开启「插件总开关」", True)
                Return
            End If
            config.Enabled = _switchSegmented.Checked
            _config.Save()
            RefreshSegmentSwitchText()
            ValidateAndShowSegmentConfig()
            UpdateHookState()
        End Sub

        Private Function HasEnabledSegmentedVideo() As Boolean
            Return _config.SegmentedVideos IsNot Nothing AndAlso
                _config.SegmentedVideos.Any(Function(item) item IsNot Nothing AndAlso item.Enabled)
        End Function

        Private Sub RefreshSegmentSwitchText()
            _lblSegmentedSwitch.Text = If(_switchSegmented.Checked,
                "<font color=#479CFF><b>已开启</b></font>", "<font color=#888888>关闭</font>")
        End Sub

        Private Sub OnMixedSegmentBackendsChanged(sender As Object, e As EventArgs)
            If _segmentSync Then Return
            Dim config = SelectedSegmentConfig()
            If config Is Nothing Then Return
            config.AllowMixedModelBackends = _switchMixedSegmentBackends.Checked
            _config.Save()
            RefreshMixedSegmentBackendText()
            RenderSegmentRows()
            ValidateAndShowSegmentConfig()
        End Sub

        Private Sub RefreshMixedSegmentBackendText()
            _lblMixedSegmentBackends.Text = If(_switchMixedSegmentBackends.Checked,
                "<font color=#E8B45A><b>已开启实验功能</b>：仅建议熟悉各后端依赖的用户使用</font>",
                "<font color=#888888>默认关闭；NCNN / CUDA / TensorRT / ONNX 之间不允许跨后端混用</font>")
        End Sub

        Private Sub OnAddSegment(sender As Object, e As EventArgs)
            Dim config = SelectedSegmentConfig()
            Dim probe = SelectedSegmentProbe()
            If config Is Nothing OrElse config.Segments.Count = 0 Then Return
            Dim last = config.Segments(config.Segments.Count - 1)
            Dim newSegment As New SegmentedUpscaleRange With {
                .Backend = last.Backend,
                .Model = last.Model,
                .DisplayName = last.DisplayName,
                .Scale = last.Scale,
                .TargetWidth = last.TargetWidth,
                .TargetHeight = last.TargetHeight
            }
            If String.Equals(config.BoundaryMode, "seconds", StringComparison.OrdinalIgnoreCase) Then
                If probe Is Nothing OrElse last.EndSeconds - last.StartSeconds <= 0.002 Then
                    ShowStatus("最后一段没有可用的秒级区间，不能继续添加断点", True)
                    Return
                End If
                Dim oldEnd = last.EndSeconds
                Dim desired = last.StartSeconds + (oldEnd - last.StartSeconds) / 2
                Dim split = SnapSegmentBoundary(probe, desired, last.StartSeconds, oldEnd)
                If split < 0 Then
                    ShowStatus("最后一段附近没有可用的内部关键帧，不能继续拆分", True)
                    Return
                End If
                last.EndSeconds = split
                newSegment.StartSeconds = split
                newSegment.EndSeconds = oldEnd
            Else
                If last.[End] <= last.Start Then
                    ShowStatus("最后一段只有一帧，不能继续拆分", True)
                    Return
                End If
                Dim oldEnd = last.[End]
                Dim split = last.Start + (oldEnd - last.Start) \ 2
                last.[End] = split
                newSegment.Start = split + 1
                newSegment.[End] = oldEnd
            End If
            config.Segments.Add(newSegment)
            ApplySegmentResolutionRule(config)
            _config.Save()
            RenderSegmentRows()
            ValidateAndShowSegmentConfig()
        End Sub

        Private Sub RenderSegmentRows()
            _segmentRowsPanel.Controls.Clear()
            Dim config = SelectedSegmentConfig()
            If config Is Nothing Then Return
            Dim secondsMode = String.Equals(config.BoundaryMode, "seconds", StringComparison.OrdinalIgnoreCase)
            Dim fixedScale = SegmentFixedScale(config)
            _segmentSync = True
            Try
                For index = 0 To config.Segments.Count - 1
                    Dim segment = config.Segments(index)
                    Dim row As New SegmentRowControls With {.Index = index}
                    row.StartBox = New ModernTextBox()
                    row.EndBox = New ModernTextBox()
                    row.ModelBox = New WheelLockedComboBox()
                    row.WidthBox = New ModernTextBox()
                    row.HeightBox = New ModernTextBox()
                    ConfigureOfficialTextBox(row.StartBox, "入点")
                    ConfigureOfficialTextBox(row.EndBox, "出点")
                    ConfigureOfficialTextBox(row.WidthBox, "宽")
                    ConfigureOfficialTextBox(row.HeightBox, "高")
                    If secondsMode Then
                        row.StartBox.Text = FormatSegmentSeconds(segment.StartSeconds)
                        row.EndBox.Text = FormatSegmentSeconds(segment.EndSeconds)
                    Else
                        row.StartBox.Text = segment.Start.ToString(CultureInfo.InvariantCulture)
                        row.EndBox.Text = segment.[End].ToString(CultureInfo.InvariantCulture)
                    End If
                    row.StartBox.Enabled = index > 0
                    row.EndBox.Enabled = index < config.Segments.Count - 1
                    row.StartBox.Tag = row
                    row.EndBox.Tag = row
                    AddHandler row.StartBox.Leave, AddressOf OnSegmentStartLeave
                    AddHandler row.EndBox.Leave, AddressOf OnSegmentEndLeave

                    ConfigureCombo(row.ModelBox)
                    row.ModelBox.WaterText = "选择模型 / FFmpeg / Anime4K…"
                    row.Choices = SegmentChoicesForRow(config, index)
                    For Each choice In row.Choices
                        row.ModelBox.Items.Add(choice.ToString())
                    Next
                    Dim choiceIndex = row.Choices.FindIndex(Function(choice)
                        Return String.Equals(choice.Backend, segment.Backend, StringComparison.OrdinalIgnoreCase) AndAlso
                            String.Equals(choice.Model, segment.Model, StringComparison.OrdinalIgnoreCase)
                    End Function)
                    row.ModelBox.SelectedIndex = choiceIndex
                    row.ModelBox.Tag = row
                    AddHandler row.ModelBox.SelectedIndexChanged, AddressOf OnSegmentModelSelected

                    row.WidthBox.Text = If(segment.TargetWidth > 0, segment.TargetWidth.ToString(CultureInfo.InvariantCulture), "")
                    row.HeightBox.Text = If(segment.TargetHeight > 0, segment.TargetHeight.ToString(CultureInfo.InvariantCulture), "")
                    Dim customSizeEnabled = fixedScale = 0 AndAlso IsSegmentCustomBackend(segment.Backend)
                    row.WidthBox.Enabled = customSizeEnabled
                    row.HeightBox.Enabled = customSizeEnabled
                    row.WidthBox.Tag = row
                    row.HeightBox.Tag = row
                    AddHandler row.WidthBox.Leave, AddressOf OnSegmentTargetSizeLeave
                    AddHandler row.HeightBox.Leave, AddressOf OnSegmentTargetSizeLeave

                    Dim deleteButton As New ModernButton()
                    ConfigureSecondaryButton(deleteButton)
                    deleteButton.Text = "删除"
                    deleteButton.Dock = DockStyle.Fill
                    deleteButton.Margin = New Padding(0, 6, 0, 6)
                    deleteButton.Enabled = config.Segments.Count > 1
                    deleteButton.Tag = index
                    AddHandler deleteButton.Click, AddressOf OnDeleteSegment

                    Dim rowPanel As New ModernHorizontalPanel(90.0F, 10.0F, 90.0F, 10.0F, -1.0F, 10.0F, 90.0F, 10.0F, 90.0F, 10.0F, 74.0F) With {
                        .Location = New Point(8, 8 + index * 58),
                        .Height = 54,
                        .Width = Math.Max(720, _segmentRowsPanel.ClientSize.Width - 24),
                        .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
                    }
                    For Each textBox In New ModernTextBox() {row.StartBox, row.EndBox, row.WidthBox, row.HeightBox}
                        textBox.Dock = DockStyle.Fill
                        textBox.Margin = New Padding(0, 6, 0, 6)
                    Next
                    row.ModelBox.Dock = DockStyle.Fill
                    row.ModelBox.Margin = New Padding(0, 6, 0, 6)
                    rowPanel.AddColumn(row.StartBox, 0)
                    rowPanel.AddColumn(row.EndBox, 2)
                    rowPanel.AddColumn(row.ModelBox, 4)
                    rowPanel.AddColumn(row.WidthBox, 6)
                    rowPanel.AddColumn(row.HeightBox, 8)
                    rowPanel.AddColumn(deleteButton, 10)
                    _segmentRowsPanel.Controls.Add(rowPanel)
                Next
                _segmentRowsPanel.AutoScrollMinSize = New Size(0, 16 + config.Segments.Count * 58)
            Finally
                _segmentSync = False
            End Try
        End Sub

        Private Function SegmentChoicesForRow(config As SegmentedVideoConfig, index As Integer) As List(Of SegmentModelChoice)
            Dim current = config.Segments(index)
            Dim otherModels = config.Segments.
                Where(Function(segment, currentIndex) currentIndex <> index AndAlso IsSegmentModelBackend(segment.Backend) AndAlso segment.Scale > 0).
                ToList()
            If otherModels.Count = 0 Then Return _segmentModelChoices.ToList()
            Dim lockedScale = otherModels(0).Scale
            Dim lockedBackend = otherModels(0).Backend
            Return _segmentModelChoices.Where(
                Function(choice)
                    If choice.Scale = 0 Then Return True
                    If choice.Scale <> lockedScale Then Return False
                    If config.AllowMixedModelBackends Then Return True
                    If String.Equals(choice.Backend, lockedBackend, StringComparison.OrdinalIgnoreCase) Then Return True
                    Return String.Equals(choice.Backend, current.Backend, StringComparison.OrdinalIgnoreCase) AndAlso
                        String.Equals(choice.Model, current.Model, StringComparison.OrdinalIgnoreCase)
                End Function).ToList()
        End Function

        Private Sub OnSegmentModelSelected(sender As Object, e As EventArgs)
            If _segmentSync Then Return
            Dim combo = TryCast(sender, WheelLockedComboBox)
            Dim row = TryCast(If(combo Is Nothing, Nothing, combo.Tag), SegmentRowControls)
            Dim config = SelectedSegmentConfig()
            If combo Is Nothing OrElse row Is Nothing OrElse config Is Nothing OrElse
                combo.SelectedIndex < 0 OrElse combo.SelectedIndex >= row.Choices.Count Then Return
            Dim choice = row.Choices(combo.SelectedIndex)
            Dim segment = config.Segments(row.Index)
            segment.Backend = choice.Backend
            segment.Model = choice.Model
            segment.DisplayName = choice.DisplayName
            segment.Scale = choice.Scale
            ApplySegmentResolutionRule(config)
            _config.Save()
            RenderSegmentRows()
            ValidateAndShowSegmentConfig()
        End Sub

        Private Sub OnSegmentTargetSizeLeave(sender As Object, e As EventArgs)
            If _segmentSync Then Return
            Dim box = TryCast(sender, ModernTextBox)
            Dim row = TryCast(If(box Is Nothing, Nothing, box.Tag), SegmentRowControls)
            Dim config = SelectedSegmentConfig()
            If box Is Nothing OrElse row Is Nothing OrElse config Is Nothing OrElse SegmentFixedScale(config) > 0 Then Return
            Dim segment = config.Segments(row.Index)
            If Not IsSegmentCustomBackend(segment.Backend) Then Return
            Dim value As Integer
            If Not Integer.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, value) OrElse value <= 0 Then
                ShowStatus("目标宽高必须是正整数", True)
                RenderSegmentRows()
                Return
            End If
            Dim width = If(ReferenceEquals(box, row.WidthBox), value, segment.TargetWidth)
            Dim height = If(ReferenceEquals(box, row.HeightBox), value, segment.TargetHeight)
            If width <= 0 Then width = Math.Max(1, config.SourceWidth * 2)
            If height <= 0 Then height = Math.Max(1, config.SourceHeight * 2)
            For Each item In config.Segments
                item.TargetWidth = width
                item.TargetHeight = height
            Next
            _config.Save()
            RenderSegmentRows()
            ValidateAndShowSegmentConfig()
        End Sub

        Private Sub OnSegmentStartLeave(sender As Object, e As EventArgs)
            If _segmentSync Then Return
            Dim box = TryCast(sender, ModernTextBox)
            Dim row = TryCast(If(box Is Nothing, Nothing, box.Tag), SegmentRowControls)
            Dim config = SelectedSegmentConfig()
            If row Is Nothing OrElse config Is Nothing OrElse row.Index <= 0 Then Return
            If String.Equals(config.BoundaryMode, "seconds", StringComparison.OrdinalIgnoreCase) Then
                Dim probe = SelectedSegmentProbe()
                Dim value As Double
                If probe Is Nothing OrElse Not TryParseSegmentSeconds(box.Text, value) Then
                    ShowStatus("入点秒数无效", True) : RenderSegmentRows() : Return
                End If
                Dim previous = config.Segments(row.Index - 1)
                Dim current = config.Segments(row.Index)
                Dim snapped = SnapSegmentBoundary(probe, value, previous.StartSeconds, current.EndSeconds)
                If snapped < 0 Then
                    ShowStatus("附近没有可用的内部关键帧", True) : RenderSegmentRows() : Return
                End If
                previous.EndSeconds = snapped
                current.StartSeconds = snapped
            Else
                Dim value As Long
                If Not Long.TryParse(box.Text, value) OrElse value <= config.Segments(row.Index - 1).Start OrElse value > config.Segments(row.Index).[End] Then
                    ShowStatus("入点必须是上一段入点之后、当前出点之前的唯一帧号", True)
                    RenderSegmentRows() : Return
                End If
                config.Segments(row.Index).Start = value
                config.Segments(row.Index - 1).[End] = value - 1
            End If
            _config.Save()
            RenderSegmentRows()
            ValidateAndShowSegmentConfig()
        End Sub

        Private Sub OnSegmentEndLeave(sender As Object, e As EventArgs)
            If _segmentSync Then Return
            Dim box = TryCast(sender, ModernTextBox)
            Dim row = TryCast(If(box Is Nothing, Nothing, box.Tag), SegmentRowControls)
            Dim config = SelectedSegmentConfig()
            If row Is Nothing OrElse config Is Nothing OrElse row.Index >= config.Segments.Count - 1 Then Return
            If String.Equals(config.BoundaryMode, "seconds", StringComparison.OrdinalIgnoreCase) Then
                Dim probe = SelectedSegmentProbe()
                Dim value As Double
                If probe Is Nothing OrElse Not TryParseSegmentSeconds(box.Text, value) Then
                    ShowStatus("出点秒数无效", True) : RenderSegmentRows() : Return
                End If
                Dim current = config.Segments(row.Index)
                Dim following = config.Segments(row.Index + 1)
                Dim snapped = SnapSegmentBoundary(probe, value, current.StartSeconds, following.EndSeconds)
                If snapped < 0 Then
                    ShowStatus("附近没有可用的内部关键帧", True) : RenderSegmentRows() : Return
                End If
                current.EndSeconds = snapped
                following.StartSeconds = snapped
            Else
                Dim value As Long
                If Not Long.TryParse(box.Text, value) OrElse value < config.Segments(row.Index).Start OrElse value >= config.Segments(row.Index + 1).[End] Then
                    ShowStatus("出点必须位于当前入点之后、下一段出点之前，且不能与其他边界重复", True)
                    RenderSegmentRows() : Return
                End If
                config.Segments(row.Index).[End] = value
                config.Segments(row.Index + 1).Start = value + 1
            End If
            _config.Save()
            RenderSegmentRows()
            ValidateAndShowSegmentConfig()
        End Sub

        Private Sub OnDeleteSegment(sender As Object, e As EventArgs)
            Dim button = TryCast(sender, ModernButton)
            Dim config = SelectedSegmentConfig()
            If button Is Nothing OrElse config Is Nothing OrElse config.Segments.Count <= 1 Then Return
            Dim index = CInt(button.Tag)
            Dim removed = config.Segments(index)
            config.Segments.RemoveAt(index)
            If String.Equals(config.BoundaryMode, "seconds", StringComparison.OrdinalIgnoreCase) Then
                If index = 0 Then
                    config.Segments(0).StartSeconds = 0
                Else
                    config.Segments(index - 1).EndSeconds = removed.EndSeconds
                End If
                config.Segments(config.Segments.Count - 1).EndSeconds = config.DurationSeconds
            Else
                If index = 0 Then
                    config.Segments(0).Start = 1
                Else
                    config.Segments(index - 1).[End] = removed.[End]
                End If
                config.Segments(config.Segments.Count - 1).[End] = config.FrameCount
            End If
            ApplySegmentResolutionRule(config)
            _config.Save()
            RenderSegmentRows()
            ValidateAndShowSegmentConfig()
        End Sub

        Private Sub OnSegmentRowsPanelResized(sender As Object, e As EventArgs)
            For Each control As Control In _segmentRowsPanel.Controls
                control.Width = Math.Max(720, _segmentRowsPanel.ClientSize.Width - 24)
            Next
        End Sub

        Private Shared Function FormatSegmentSeconds(value As Double) As String
            Return value.ToString("0.###", CultureInfo.InvariantCulture)
        End Function

        Private Shared Function TryParseSegmentSeconds(text As String, ByRef value As Double) As Boolean
            If Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, value) Then Return True
            Return Double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, value)
        End Function

        Private Shared Function SnapSegmentBoundary(
            probe As SegmentVideoProbe, desired As Double, minimum As Double, maximum As Double) As Double
            If probe Is Nothing OrElse probe.Keyframes Is Nothing Then Return -1
            Dim candidates = probe.Keyframes.Where(
                Function(value) value > minimum + 0.001 AndAlso value < maximum - 0.001).ToList()
            If candidates.Count = 0 Then Return -1
            Return candidates.OrderBy(Function(value) Math.Abs(value - desired)).First()
        End Function

        Private Shared Sub SnapAllSegmentBoundaries(config As SegmentedVideoConfig, probe As SegmentVideoProbe)
            If config Is Nothing OrElse probe Is Nothing OrElse config.Segments Is Nothing OrElse config.Segments.Count < 2 Then Return
            config.Segments(0).StartSeconds = 0
            For index = 0 To config.Segments.Count - 2
                Dim current = config.Segments(index)
                Dim following = config.Segments(index + 1)
                Dim minimum = current.StartSeconds
                Dim maximum = If(index + 1 = config.Segments.Count - 1, config.DurationSeconds, following.EndSeconds)
                Dim desired = If(current.EndSeconds > minimum AndAlso current.EndSeconds < maximum,
                    current.EndSeconds, minimum + (maximum - minimum) / 2)
                Dim snapped = SnapSegmentBoundary(probe, desired, minimum, maximum)
                If snapped >= 0 Then
                    current.EndSeconds = snapped
                    following.StartSeconds = snapped
                End If
            Next
            config.Segments(config.Segments.Count - 1).EndSeconds = config.DurationSeconds
        End Sub

        Private Shared Function SegmentFixedScale(config As SegmentedVideoConfig) As Integer
            If config Is Nothing OrElse config.Segments Is Nothing Then Return 0
            Return config.Segments.Where(
                Function(segment) IsSegmentModelBackend(segment.Backend) AndAlso segment.Scale > 0).
                Select(Function(segment) segment.Scale).FirstOrDefault()
        End Function

        Private Shared Sub ApplySegmentResolutionRule(config As SegmentedVideoConfig)
            If config Is Nothing OrElse config.Segments Is Nothing OrElse config.Segments.Count = 0 Then Return
            Dim fixedScale = SegmentFixedScale(config)
            Dim width As Integer
            Dim height As Integer
            If fixedScale > 0 AndAlso config.SourceWidth > 0 AndAlso config.SourceHeight > 0 Then
                width = config.SourceWidth * fixedScale
                height = config.SourceHeight * fixedScale
            Else
                Dim existing = config.Segments.FirstOrDefault(
                    Function(segment) segment.TargetWidth > 0 AndAlso segment.TargetHeight > 0)
                If existing IsNot Nothing Then
                    width = existing.TargetWidth
                    height = existing.TargetHeight
                Else
                    width = Math.Max(1, config.SourceWidth * 2)
                    height = Math.Max(1, config.SourceHeight * 2)
                End If
            End If
            For Each segment In config.Segments
                segment.TargetWidth = width
                segment.TargetHeight = height
            Next
        End Sub

        Private Sub ValidateAndShowSegmentConfig()
            Dim config = SelectedSegmentConfig()
            If config Is Nothing Then Return
            Dim errorText = ValidateSegmentRanges(config)
            If errorText.Length > 0 Then
                _lblSegmentStatus.Text = "<font color=#E07878>配置未通过：" & EscapeHtml(errorText) & "</font>"
                Return
            End If
            Dim fixedScale = SegmentFixedScale(config)
            Dim sizeText As String
            If fixedScale > 0 Then
                sizeText = "模型倍率优先 " & fixedScale & "x → " &
                    config.Segments(0).TargetWidth & "×" & config.Segments(0).TargetHeight &
                    "；FFmpeg / Anime4K 自动跟随"
            Else
                sizeText = "自定义统一输出 " & config.Segments(0).TargetWidth & "×" & config.Segments(0).TargetHeight
            End If
            Dim coverage = If(String.Equals(config.BoundaryMode, "seconds", StringComparison.OrdinalIgnoreCase),
                "已自动覆盖 0-" & FormatSegmentSeconds(config.DurationSeconds) & " 秒；内部断点吸附关键帧",
                "已连续覆盖 1-" & config.FrameCount & " 帧")
            _lblSegmentStatus.Text = "<font color=#96D2A0>" & EscapeHtml(coverage & "；" & sizeText & "；配置已自动保存。") & "</font>"
        End Sub

        Private Shared Function ValidateSegmentRanges(config As SegmentedVideoConfig) As String
            If config.Segments Is Nothing OrElse config.Segments.Count = 0 Then Return "至少添加一个分段"
            Dim secondsMode = String.Equals(config.BoundaryMode, "seconds", StringComparison.OrdinalIgnoreCase)
            If secondsMode AndAlso config.DurationSeconds <= 0 Then Return "视频时长无效"
            If Not secondsMode AndAlso config.FrameCount <= 0 Then Return "视频帧数无效"
            Dim expectedFrame As Long = 1
            Dim expectedSeconds As Double = 0
            Dim fixedScale As Integer = 0
            Dim firstModelBackend As String = ""
            Dim targetWidth As Integer = 0
            Dim targetHeight As Integer = 0
            For index = 0 To config.Segments.Count - 1
                Dim segment = config.Segments(index)
                If secondsMode Then
                    If Math.Abs(segment.StartSeconds - expectedSeconds) > 0.002 OrElse segment.EndSeconds <= segment.StartSeconds Then
                        Return $"第 {index + 1} 段秒级边界不连续"
                    End If
                    expectedSeconds = segment.EndSeconds
                Else
                    If segment.Start <> expectedFrame OrElse segment.[End] < segment.Start Then Return $"第 {index + 1} 段应从第 {expectedFrame} 帧开始"
                    expectedFrame = segment.[End] + 1
                End If
                If String.IsNullOrWhiteSpace(segment.Model) Then Return $"第 {index + 1} 段尚未选择处理方式"
                If IsSegmentModelBackend(segment.Backend) Then
                    If segment.Scale <= 0 Then Return $"第 {index + 1} 段模型倍率无效"
                    Dim currentBackend = segment.Backend.Trim().ToLowerInvariant()
                    If firstModelBackend.Length = 0 Then
                        firstModelBackend = currentBackend
                    ElseIf Not config.AllowMixedModelBackends AndAlso
                           Not String.Equals(currentBackend, firstModelBackend, StringComparison.OrdinalIgnoreCase) Then
                        Return "跨 NCNN / CUDA / TensorRT / ONNX 混用是测试功能，请先手动开启跨模型后端混用开关"
                    End If
                    If fixedScale = 0 Then
                        fixedScale = segment.Scale
                    ElseIf segment.Scale <> fixedScale Then
                        Return "固定倍率模型必须使用相同倍率"
                    End If
                ElseIf Not IsSegmentCustomBackend(segment.Backend) Then
                    Return $"第 {index + 1} 段处理方式不受支持"
                End If
                If targetWidth = 0 Then
                    targetWidth = segment.TargetWidth
                    targetHeight = segment.TargetHeight
                ElseIf segment.TargetWidth <> targetWidth OrElse segment.TargetHeight <> targetHeight Then
                    Return "整片视频必须使用统一输出分辨率"
                End If
            Next
            If secondsMode Then
                If Math.Abs(config.Segments(0).StartSeconds) > 0.002 OrElse
                   Math.Abs(config.Segments(config.Segments.Count - 1).EndSeconds - config.DurationSeconds) > 0.002 Then Return "必须自动覆盖完整视频时长"
            ElseIf config.Segments(0).Start <> 1 OrElse config.Segments(config.Segments.Count - 1).[End] <> config.FrameCount Then
                Return "必须覆盖全部帧"
            End If
            If targetWidth <= 0 OrElse targetHeight <= 0 Then Return "目标分辨率无效"
            If fixedScale > 0 AndAlso config.SourceWidth > 0 AndAlso config.SourceHeight > 0 AndAlso
               (targetWidth <> config.SourceWidth * fixedScale OrElse targetHeight <> config.SourceHeight * fixedScale) Then
                Return "固定倍率模型存在时，输出分辨率必须服从模型倍率"
            End If
            Return ""
        End Function

    End Class

End Namespace
