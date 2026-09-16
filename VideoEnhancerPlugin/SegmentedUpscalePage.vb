Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
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
        Private ReadOnly _switchSegmented As New LakeUI.BooleanSwitch()
        Private ReadOnly _lblSegmentedSwitch As New HtmlColorLabel()
        Private ReadOnly _btnSegmentRefresh As New ModernButton()
        Private ReadOnly _btnSegmentAdd As New ModernButton()
        Private ReadOnly _segmentRowsPanel As New ModernPanel()
        Private ReadOnly _lblSegmentStatus As New HtmlColorLabel()
        Private ReadOnly _segmentVideoPaths As New List(Of String)()
        Private ReadOnly _segmentModelChoices As New List(Of SegmentModelChoice)()
        Private _segmentModelsLoaded As Boolean
        Private _segmentModelsLoading As Boolean
        Private _segmentVideosLoading As Boolean
        Private _segmentSync As Boolean

        Private NotInheritable Class SegmentModelChoice
            Public Property Backend As String = ""
            Public Property Model As String = ""
            Public Property DisplayName As String = ""
            Public Property Scale As Integer

            Public Overrides Function ToString() As String
                Return SegmentBackendDisplayName(Backend) & " · " & DisplayName & " · " & Scale & "x"
            End Function
        End Class

        Private NotInheritable Class SegmentRowControls
            Public Property Index As Integer
            Public Property StartBox As ModernTextBox
            Public Property EndBox As ModernTextBox
            Public Property ModelBox As WheelLockedComboBox
            Public Property Choices As List(Of SegmentModelChoice)
        End Class

        Private Shared Function SegmentBackendDisplayName(backend As String) As String
            Select Case If(backend, "").ToLowerInvariant()
                Case "cuda" : Return "CUDA"
                Case "tensorrt" : Return "TensorRT"
                Case "onnx" : Return "ONNX"
                Case Else : Return "NCNN"
            End Select
        End Function

        Private Sub BuildOfficialSegmentedPage()
            _pageSegmented.Dock = DockStyle.Fill
            _pageSegmented.LayoutMode = ModernPanel.LayoutModeEnum.Absolute
            ' 绝对布局子项只在根 SetBounds 触发 Layout 时重排；根必须像超分工作台
            ' 一样显式同步宽度，否则子项停留在构建瞬间的尺寸上，出现贴边和裁切。
            Dim root As New ModernPanel With {
                .Dock = DockStyle.None,
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left,
                .AutoSize = False,
                .MinimumSize = New Size(0, 700),
                .Height = 700,
                .BackColor = Color.Transparent,
                .BackColor1 = Color.Transparent,
                .LayoutMode = ModernPanel.LayoutModeEnum.Absolute,
                .BorderSize = 0
            }
            _segmentRoot = root
            AddHandler _pageSegmented.ClientSizeChanged, Sub(sender, e) SyncSegmentedRootBounds()
            AddHandler _pageSegmented.SizeChanged, Sub(sender, e) SyncSegmentedRootBounds()
            AddWorkbenchRow(root, CreateOfficialSectionHeading(
                "分段超分设置", "仅支持 NCNN / CUDA / TensorRT / ONNX 单帧模型；第一段会锁定后端类别和倍率"), 12, 42)

            _cmbSegmentVideo.WaterText = "切换到本页后读取 3FUI 添加文件列表…"
            ConfigureCombo(_cmbSegmentVideo)
            AddHandler _cmbSegmentVideo.SelectedIndexChanged, AddressOf OnSegmentVideoSelected
            Dim videoField = CreateOfficialField("视频", _cmbSegmentVideo)
            ConfigureSecondaryButton(_btnSegmentRefresh)
            _btnSegmentRefresh.Text = "刷新视频列表"
            _btnSegmentRefresh.Dock = DockStyle.Fill
            _btnSegmentRefresh.Margin = New Padding(0, 6, 0, 6)
            AddHandler _btnSegmentRefresh.Click, Sub(sender, e) RefreshSegmentedVideos()
            AddWorkbenchControl(root, videoField, 66, 76, 0.0F, 0.78F, 0, -12)
            AddWorkbenchControl(root, _btnSegmentRefresh, 66, 76, 0.78F, 1.0F)

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
            _btnSegmentAdd.Text = "＋ 添加分段"
            _btnSegmentAdd.Dock = DockStyle.Fill
            _btnSegmentAdd.Margin = New Padding(0, 6, 0, 6)
            AddHandler _btnSegmentAdd.Click, AddressOf OnAddSegment
            switchRow.AddColumn(switchCaption, 0)
            switchRow.AddColumn(_switchSegmented, 2)
            switchRow.AddColumn(_lblSegmentedSwitch, 4)
            switchRow.AddColumn(_btnSegmentAdd, 6)
            AddWorkbenchRow(root, switchRow, 150, 54)

            Dim header As New ModernHorizontalPanel(110.0F, 12.0F, 110.0F, 12.0F, -1.0F, 12.0F, 90.0F)
            For Each caption In New String() {"入点帧", "出点帧", "模型（首段锁定后端与倍率）", "操作"}
                Dim label = CreateOfficialCaption(caption)
                label.Dock = DockStyle.Fill
                label.TextAlign = ContentAlignment.MiddleLeft
                header.AddColumn(label, header.Controls.Count * 2)
            Next
            AddWorkbenchRow(root, header, 216, 34)

            _segmentRowsPanel.BackColor = Color.Transparent
            _segmentRowsPanel.BackColor1 = Color.Transparent
            _segmentRowsPanel.BorderColor = Color.FromArgb(52, 52, 52)
            _segmentRowsPanel.BorderSize = 1
            _segmentRowsPanel.BorderRadius = 6
            _segmentRowsPanel.LayoutMode = ModernPanel.LayoutModeEnum.Absolute
            _segmentRowsPanel.AutoScroll = True
            AddWorkbenchRow(root, _segmentRowsPanel, 250, 354)
            AddHandler _segmentRowsPanel.ClientSizeChanged, AddressOf OnSegmentRowsPanelResized

            _lblSegmentStatus.AutoSize = False
            _lblSegmentStatus.TextAlign = HtmlColorLabel.TextAlignEnum.MiddleLeft
            _lblSegmentStatus.Text = "<font color=#888888>切换到本页后会检测添加文件列表中的视频和准确帧数。</font>"
            AddWorkbenchRow(root, CreateOfficialValueBox(_lblSegmentStatus), 620, 62)
            _pageSegmented.Controls.Add(root)
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
            If root.Left <> 0 OrElse root.Top <> 0 OrElse root.Width <> width OrElse root.Height <> 700 Then
                root.SetBounds(0, 0, width, 700)
            End If
        End Sub

        Private Sub ActivateSegmentedPage()
            LoadSegmentModelCatalogs()
            RefreshSegmentedVideos()
        End Sub

        Private Async Sub LoadSegmentModelCatalogs()
            If _segmentModelsLoaded OrElse _segmentModelsLoading Then Return
            Dim exePath = PluginConfig.ResolveInstalledExePath()
            If String.IsNullOrWhiteSpace(exePath) OrElse Not File.Exists(exePath) Then Return
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
                _segmentModelChoices.Clear()
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
            _lblSegmentStatus.Text = "<font color=#B8B8B8>正在检测添加文件列表中的视频和准确帧数…</font>"
            Try
                Dim paths = QueueHook.GetCurrentPrepareFilePaths().
                    Where(Function(path) Not String.IsNullOrWhiteSpace(path) AndAlso File.Exists(path)).
                    Select(Function(filePath) IO.Path.GetFullPath(filePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                Dim exePath = PluginConfig.ResolveInstalledExePath()
                Dim ffprobe = If(String.IsNullOrWhiteSpace(exePath), "", Path.Combine(Path.GetDirectoryName(exePath), "bin", "ffmpeg", "ffprobe.exe"))
                Dim probed = Await Task.Run(Function()
                    Dim result As New List(Of KeyValuePair(Of String, Long))()
                    For Each filePath As String In paths
                        Dim frames = ProbeSegmentFrameCount(ffprobe, filePath)
                        If frames > 0 Then result.Add(New KeyValuePair(Of String, Long)(filePath, frames))
                    Next
                    Return result
                End Function)
                Dim previousPath = SelectedSegmentVideoPath()
                _segmentSync = True
                _cmbSegmentVideo.Items.Clear()
                _segmentVideoPaths.Clear()
                For Each item In probed
                    _segmentVideoPaths.Add(item.Key)
                    _cmbSegmentVideo.Items.Add(Path.GetFileName(item.Key) & "　（" & item.Value & " 帧）")
                    EnsureSegmentVideoConfig(item.Key, item.Value)
                Next
                Dim selectedIndex = If(previousPath.Length = 0, -1, _segmentVideoPaths.FindIndex(
                    Function(path) String.Equals(path, previousPath, StringComparison.OrdinalIgnoreCase)))
                If selectedIndex < 0 AndAlso _segmentVideoPaths.Count > 0 Then selectedIndex = 0
                _cmbSegmentVideo.SelectedIndex = selectedIndex
                _segmentSync = False
                _config.Save()
                RefreshSegmentSelection()
                If probed.Count = 0 Then
                    _lblSegmentStatus.Text = "<font color=#E07878>添加文件列表中没有可读取帧数的视频。</font>"
                End If
            Catch ex As Exception
                _segmentSync = False
                _lblSegmentStatus.Text = "<font color=#E07878>视频检测失败：" & EscapeHtml(ex.Message) & "</font>"
            Finally
                _segmentVideosLoading = False
                _btnSegmentRefresh.Enabled = True
            End Try
        End Sub

        Private Shared Function ProbeSegmentFrameCount(ffprobe As String, source As String) As Long
            If String.IsNullOrWhiteSpace(ffprobe) OrElse Not File.Exists(ffprobe) Then Return 0
            Dim info As New ProcessStartInfo With {
                .FileName = ffprobe,
                .UseShellExecute = False,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .CreateNoWindow = True,
                .StandardOutputEncoding = Encoding.UTF8,
                .StandardErrorEncoding = Encoding.UTF8
            }
            PortableRuntime.ConfigureProcess(info)
            For Each argument In New String() {"-v", "error", "-select_streams", "v:0", "-count_frames", "-show_entries", "stream=nb_read_frames,nb_frames", "-of", "json", source}
                info.ArgumentList.Add(argument)
            Next
            Using child = Process.Start(info)
                If child Is Nothing Then Return 0
                Dim output = child.StandardOutput.ReadToEnd()
                child.WaitForExit(300000)
                If child.ExitCode <> 0 Then Return 0
                Using document = JsonDocument.Parse(output)
                    Dim streams = document.RootElement.GetProperty("streams")
                    If streams.GetArrayLength() = 0 Then Return 0
                    Dim stream = streams(0)
                    For Each propertyName In New String() {"nb_read_frames", "nb_frames"}
                        Dim value As JsonElement
                        If Not stream.TryGetProperty(propertyName, value) Then Continue For
                        Dim frames As Long
                        If value.ValueKind = JsonValueKind.Number AndAlso value.TryGetInt64(frames) Then Return frames
                        If value.ValueKind = JsonValueKind.String AndAlso Long.TryParse(value.GetString(), frames) Then Return frames
                    Next
                End Using
            End Using
            Return 0
        End Function

        Private Function EnsureSegmentVideoConfig(path As String, frames As Long) As SegmentedVideoConfig
            If _config.SegmentedVideos Is Nothing Then _config.SegmentedVideos = New List(Of SegmentedVideoConfig)()
            Dim config = _config.SegmentedVideos.FirstOrDefault(
                Function(item) String.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
            If config Is Nothing Then
                config = New SegmentedVideoConfig With {.Path = path, .FrameCount = frames}
                config.Segments.Add(New SegmentedUpscaleRange With {.Start = 1, .End = frames})
                _config.SegmentedVideos.Add(config)
            Else
                config.Path = path
                If config.Segments Is Nothing Then config.Segments = New List(Of SegmentedUpscaleRange)()
                If config.Segments.Count = 0 Then config.Segments.Add(New SegmentedUpscaleRange With {.Start = 1, .End = frames})
                If config.FrameCount <> frames Then
                    config.FrameCount = frames
                    config.Segments(config.Segments.Count - 1).[End] = frames
                End If
            End If
            Return config
        End Function

        Private Function SelectedSegmentVideoPath() As String
            If _cmbSegmentVideo.SelectedIndex < 0 OrElse _cmbSegmentVideo.SelectedIndex >= _segmentVideoPaths.Count Then Return ""
            Return _segmentVideoPaths(_cmbSegmentVideo.SelectedIndex)
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
            _btnSegmentAdd.Enabled = config IsNot Nothing AndAlso config.FrameCount > 1
            _segmentSync = False
            RefreshSegmentSwitchText()
            RenderSegmentRows()
            ValidateAndShowSegmentConfig()
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

        Private Sub OnAddSegment(sender As Object, e As EventArgs)
            Dim config = SelectedSegmentConfig()
            If config Is Nothing OrElse config.Segments.Count = 0 Then Return
            Dim last = config.Segments(config.Segments.Count - 1)
            If last.[End] <= last.Start Then
                ShowStatus("最后一段只有一帧，不能继续拆分", True)
                Return
            End If
            Dim oldEnd = last.[End]
            Dim split = last.Start + (oldEnd - last.Start) \ 2
            last.[End] = split
            config.Segments.Add(New SegmentedUpscaleRange With {
                .Start = split + 1,
                .End = oldEnd,
                .Backend = last.Backend,
                .Model = last.Model,
                .DisplayName = last.DisplayName,
                .Scale = last.Scale
            })
            _config.Save()
            RenderSegmentRows()
            ValidateAndShowSegmentConfig()
        End Sub

        Private Sub RenderSegmentRows()
            _segmentRowsPanel.Controls.Clear()
            Dim config = SelectedSegmentConfig()
            If config Is Nothing Then Return
            _segmentSync = True
            Try
                For index = 0 To config.Segments.Count - 1
                    Dim segment = config.Segments(index)
                    Dim row As New SegmentRowControls With {.Index = index}
                    row.StartBox = New ModernTextBox()
                    row.EndBox = New ModernTextBox()
                    row.ModelBox = New WheelLockedComboBox()
                    ConfigureOfficialTextBox(row.StartBox, "入点")
                    ConfigureOfficialTextBox(row.EndBox, "出点")
                    row.StartBox.Text = segment.Start.ToString()
                    row.EndBox.Text = segment.[End].ToString()
                    row.StartBox.Enabled = index > 0
                    row.EndBox.Enabled = index < config.Segments.Count - 1
                    row.StartBox.Tag = row
                    row.EndBox.Tag = row
                    AddHandler row.StartBox.Leave, AddressOf OnSegmentStartLeave
                    AddHandler row.EndBox.Leave, AddressOf OnSegmentEndLeave

                    ConfigureCombo(row.ModelBox)
                    row.ModelBox.WaterText = If(index = 0, "选择单帧模型并锁定后端/倍率…", "选择同后端同倍率模型…")
                    row.Choices = SegmentChoicesForRow(config, index)
                    For Each choice In row.Choices
                        row.ModelBox.Items.Add(choice.ToString())
                    Next
                    Dim choiceIndex = row.Choices.FindIndex(Function(choice)
                        Return String.Equals(choice.Backend, segment.Backend, StringComparison.OrdinalIgnoreCase) AndAlso
                            String.Equals(choice.Model, segment.Model, StringComparison.OrdinalIgnoreCase)
                    End Function)
                    row.ModelBox.SelectedIndex = choiceIndex
                    row.ModelBox.Enabled = index = 0 OrElse row.Choices.Count > 0
                    row.ModelBox.Tag = row
                    AddHandler row.ModelBox.SelectedIndexChanged, AddressOf OnSegmentModelSelected

                    Dim deleteButton As New ModernButton()
                    ConfigureSecondaryButton(deleteButton)
                    deleteButton.Text = "删除"
                    deleteButton.Dock = DockStyle.Fill
                    deleteButton.Margin = New Padding(0, 6, 0, 6)
                    deleteButton.Enabled = config.Segments.Count > 1
                    deleteButton.Tag = index
                    AddHandler deleteButton.Click, AddressOf OnDeleteSegment

                    Dim rowPanel As New ModernHorizontalPanel(110.0F, 12.0F, 110.0F, 12.0F, -1.0F, 12.0F, 90.0F) With {
                        .Location = New Point(8, 8 + index * 58),
                        .Height = 54,
                        .Width = Math.Max(600, _segmentRowsPanel.ClientSize.Width - 24),
                        .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
                    }
                    row.StartBox.Dock = DockStyle.Fill : row.StartBox.Margin = New Padding(0, 6, 0, 6)
                    row.EndBox.Dock = DockStyle.Fill : row.EndBox.Margin = New Padding(0, 6, 0, 6)
                    row.ModelBox.Dock = DockStyle.Fill : row.ModelBox.Margin = New Padding(0, 6, 0, 6)
                    rowPanel.AddColumn(row.StartBox, 0)
                    rowPanel.AddColumn(row.EndBox, 2)
                    rowPanel.AddColumn(row.ModelBox, 4)
                    rowPanel.AddColumn(deleteButton, 6)
                    _segmentRowsPanel.Controls.Add(rowPanel)
                Next
                _segmentRowsPanel.AutoScrollMinSize = New Size(0, 16 + config.Segments.Count * 58)
            Finally
                _segmentSync = False
            End Try
        End Sub

        Private Function SegmentChoicesForRow(config As SegmentedVideoConfig, index As Integer) As List(Of SegmentModelChoice)
            If index = 0 Then
                Return _segmentModelChoices.ToList()
            End If
            If config.Segments.Count = 0 OrElse String.IsNullOrWhiteSpace(config.Segments(0).Model) Then
                Return New List(Of SegmentModelChoice)()
            End If
            Dim first = config.Segments(0)
            Return _segmentModelChoices.Where(
                Function(choice) String.Equals(choice.Backend, first.Backend, StringComparison.OrdinalIgnoreCase) AndAlso
                    choice.Scale = first.Scale).ToList()
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
            If row.Index = 0 Then
                For index = 1 To config.Segments.Count - 1
                    Dim other = config.Segments(index)
                    If Not String.Equals(other.Backend, choice.Backend, StringComparison.OrdinalIgnoreCase) OrElse other.Scale <> choice.Scale Then
                        other.Backend = "" : other.Model = "" : other.DisplayName = "" : other.Scale = 0
                    End If
                Next
            End If
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
            Dim value As Long
            If Not Long.TryParse(box.Text, value) OrElse value <= config.Segments(row.Index - 1).Start OrElse value > config.Segments(row.Index).[End] Then
                ShowStatus("入点必须是上一段入点之后、当前出点之前的唯一帧号", True)
                RenderSegmentRows() : Return
            End If
            config.Segments(row.Index).Start = value
            config.Segments(row.Index - 1).[End] = value - 1
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
            Dim value As Long
            If Not Long.TryParse(box.Text, value) OrElse value < config.Segments(row.Index).Start OrElse value >= config.Segments(row.Index + 1).[End] Then
                ShowStatus("出点必须位于当前入点之后、下一段出点之前，且不能与其他边界重复", True)
                RenderSegmentRows() : Return
            End If
            config.Segments(row.Index).[End] = value
            config.Segments(row.Index + 1).Start = value + 1
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
            If index = 0 Then
                config.Segments(0).Start = 1
            Else
                config.Segments(index - 1).[End] = removed.[End]
            End If
            config.Segments(config.Segments.Count - 1).[End] = config.FrameCount
            _config.Save()
            RenderSegmentRows()
            ValidateAndShowSegmentConfig()
        End Sub

        Private Sub OnSegmentRowsPanelResized(sender As Object, e As EventArgs)
            For Each control As Control In _segmentRowsPanel.Controls
                control.Width = Math.Max(600, _segmentRowsPanel.ClientSize.Width - 24)
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
            Dim first = config.Segments(0)
            _lblSegmentStatus.Text = "<font color=#96D2A0>已连续覆盖 1-" & config.FrameCount & " 帧；" &
                EscapeHtml(SegmentBackendDisplayName(first.Backend)) & " / " & first.Scale & "x；配置已自动保存。</font>"
        End Sub

        Private Shared Function ValidateSegmentRanges(config As SegmentedVideoConfig) As String
            If config.FrameCount <= 0 Then Return "视频帧数无效"
            If config.Segments Is Nothing OrElse config.Segments.Count = 0 Then Return "至少添加一个分段"
            Dim expected As Long = 1
            Dim firstBackend = If(config.Segments(0).Backend, "")
            Dim firstScale = config.Segments(0).Scale
            For index = 0 To config.Segments.Count - 1
                Dim segment = config.Segments(index)
                If segment.Start <> expected OrElse segment.[End] < segment.Start Then Return $"第 {index + 1} 段应从第 {expected} 帧开始"
                If String.IsNullOrWhiteSpace(segment.Model) Then Return $"第 {index + 1} 段尚未选择模型"
                If index > 0 AndAlso Not String.Equals(segment.Backend, firstBackend, StringComparison.OrdinalIgnoreCase) Then Return "后端类别必须与第一段一致"
                If index > 0 AndAlso segment.Scale <> firstScale Then Return "放大倍率必须与第一段一致"
                expected = segment.[End] + 1
            Next
            If config.Segments(0).Start <> 1 OrElse config.Segments(config.Segments.Count - 1).[End] <> config.FrameCount Then Return "必须覆盖全部帧"
            Return ""
        End Function

    End Class

End Namespace
