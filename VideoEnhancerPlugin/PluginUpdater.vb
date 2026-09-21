Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Reflection
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports System.Threading.Tasks

Namespace videoenhancer

    Public NotInheritable Class UpdateManifest
        Public Property SchemaVersion As Integer
        Public Property Channel As String = ""
        Public Property Version As String = ""
        Public Property PublishedAt As String = ""
        Public Property Package As UpdatePackageInfo
        Public Property Notes As String = ""

        ''' <summary>GitHub Release 中与更新包同名的资产下载地址（兜底源）；不属于清单 JSON。</summary>
        <JsonIgnore>
        Public Property GithubPackageUrl As String = ""
    End Class

    Public NotInheritable Class UpdatePackageInfo
        Public Property Path As String = ""
        Public Property Size As Long
        Public Property Sha256 As String = ""
    End Class

    ''' <summary>GitHub releases/latest 响应中需要的字段。</summary>
    Friend NotInheritable Class GithubRelease
        <JsonPropertyName("tag_name")>
        Public Property TagName As String = ""
        <JsonPropertyName("assets")>
        Public Property Assets As List(Of GithubAsset)
    End Class

    Friend NotInheritable Class GithubAsset
        <JsonPropertyName("name")>
        Public Property Name As String = ""
        <JsonPropertyName("browser_download_url")>
        Public Property BrowserDownloadUrl As String = ""
    End Class

    ''' <summary>读取独立发行清单并执行更新；版本检查 GitHub 首选，更新包 ModelScope 首选。</summary>
    Public NotInheritable Class PluginUpdater

        Private Const DefaultGithubRepo As String = "maxzrb/VideoEnhancer"
        Private Const DefaultDataset As String = "AerithDream/VideoEnhancer-Releases"
        Private Const ManifestAssetName As String = "stable.json"
        Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {
            .PropertyNameCaseInsensitive = True
        }
        ''' <summary>由 VideoEnhancerPlugin.vbproj 的 Version 生成程序集元数据后读取。</summary>
        Public Shared ReadOnly Property CurrentVersion As String = ResolveCurrentVersion()

        Private Sub New()
        End Sub

        Private Shared Function ResolveCurrentVersion() As String
            Dim assembly = GetType(PluginUpdater).Assembly
            Dim informational = assembly.
                GetCustomAttribute(Of AssemblyInformationalVersionAttribute)()?.InformationalVersion
            If Not String.IsNullOrWhiteSpace(informational) Then
                ' SDK 可能附加 "+提交哈希"；更新清单只使用纯版本号。
                Dim plus = informational.IndexOf("+"c)
                Return If(plus > 0, informational.Substring(0, plus), informational)
            End If
            Dim assemblyVersion = assembly.GetName().Version
            Return If(assemblyVersion Is Nothing, "0.0.0", assemblyVersion.ToString(3))
        End Function

        ''' <summary>优先读取 GitHub 最新 Release；GitHub 不可达时读取 ModelScope stable.json。</summary>
        Public Shared Async Function FetchLatestManifestAsync() As Task(Of UpdateManifest)
            Dim githubError As Exception = Nothing
            Try
                Return Await FetchGithubManifestAsync()
            Catch ex As Exception
                githubError = ex
            End Try

            Try
                Return Await FetchModelScopeManifestAsync()
            Catch modelScopeError As Exception
                Throw New IOException("更新检查失败（GitHub 与 ModelScope 均不可用）：" &
                    Environment.NewLine & "GitHub：" & If(githubError IsNot Nothing, githubError.Message, "未知错误") &
                    Environment.NewLine & "ModelScope：" & modelScopeError.Message, modelScopeError)
            End Try
        End Function

        Private Shared Async Function FetchGithubManifestAsync() As Task(Of UpdateManifest)
            Dim release As GithubRelease
            Using client = CreateGithubClient(TimeSpan.FromSeconds(15))
                Dim api = "https://api.github.com/repos/" & GithubRepo & "/releases/latest"
                Using response = Await client.GetAsync(api)
                    If response.StatusCode = HttpStatusCode.NotFound Then
                        Throw New InvalidDataException("远端 GitHub 仓库尚无稳定版 Release：" & GithubRepo)
                    End If
                    response.EnsureSuccessStatusCode()
                    Dim json = Await response.Content.ReadAsStringAsync()
                    release = JsonSerializer.Deserialize(Of GithubRelease)(json, JsonOptions)
                End Using
            End Using
            If release Is Nothing OrElse String.IsNullOrWhiteSpace(release.TagName) OrElse release.Assets Is Nothing Then
                Throw New InvalidDataException("GitHub Release 响应格式无效")
            End If

            Dim tagText = release.TagName.Trim()
            If tagText.StartsWith("v", StringComparison.OrdinalIgnoreCase) Then tagText = tagText.Substring(1)
            Dim tagVersion As Version = Nothing
            If Not Version.TryParse(tagText, tagVersion) Then
                Throw New InvalidDataException("GitHub Release 标签不是有效版本号：" & release.TagName)
            End If

            Dim manifestUrl = release.Assets.FirstOrDefault(
                Function(asset) asset.Name.Equals(ManifestAssetName, StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl
            If String.IsNullOrWhiteSpace(manifestUrl) Then
                Throw New InvalidDataException("GitHub Release 缺少 " & ManifestAssetName & " 清单资产")
            End If

            Dim manifestJson As String
            Using client = CreateGithubClient(TimeSpan.FromSeconds(30))
                manifestJson = Await client.GetStringAsync(manifestUrl)
            End Using
            Dim manifest = ParseManifest(manifestJson)

            Dim manifestVersion As Version = Nothing
            If Not Version.TryParse(manifest.Version, manifestVersion) OrElse manifestVersion <> tagVersion Then
                Throw New InvalidDataException("GitHub 标签 " & release.TagName &
                    " 与清单版本 " & manifest.Version & " 不一致")
            End If

            Dim packageFileName = Path.GetFileName(manifest.Package.Path.Replace("/"c, Path.DirectorySeparatorChar))
            Dim packageUrl = release.Assets.FirstOrDefault(
                Function(asset) asset.Name.Equals(packageFileName, StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl
            If Not String.IsNullOrWhiteSpace(packageUrl) Then
                manifest.GithubPackageUrl = packageUrl
            End If
            Return manifest
        End Function

        Private Shared Async Function FetchModelScopeManifestAsync() As Task(Of UpdateManifest)
            Dim manifestJson As String
            Using client = CreateClient(TimeSpan.FromSeconds(30))
                manifestJson = Await client.GetStringAsync(BuildResolveUrl(ManifestAssetName))
            End Using
            Dim manifest = ParseManifest(manifestJson)
            ' ModelScope 清单没有 GitHub API 返回的资产地址，后续下载阶段会按版本拼接 GitHub Release URL。
            manifest.GithubPackageUrl = BuildGithubPackageUrl(manifest)
            Return manifest
        End Function

        Public Shared Function HasUpdate(manifest As UpdateManifest) As Boolean
            Dim installedVersion As Version = Nothing
            Dim remoteVersion As Version = Nothing
            If Not Version.TryParse(CurrentVersion, installedVersion) OrElse
                Not Version.TryParse(manifest.Version, remoteVersion) Then Return False
            ' 清单哈希描述的是 Burn 安装器，不再与已安装的运行 EXE 比较。
            Return remoteVersion > installedVersion
        End Function

        Public Shared Async Function DownloadPackageAsync(
                manifest As UpdateManifest,
                progress As Action(Of Integer)) As Task(Of String)
            ValidateRelativePath(manifest.Package.Path)
            Dim updateDirectory = Path.Combine(
                PortableRuntime.UpdateRoot, "packages", manifest.Version)
            Directory.CreateDirectory(updateDirectory)
            Dim fileName = Path.GetFileName(manifest.Package.Path.Replace("/"c, Path.DirectorySeparatorChar))
            Dim destination = Path.Combine(updateDirectory, fileName)
            Dim temporary = destination & ".download"
            Try
                ' 版本清单确认后，更新包优先走 ModelScope；ModelScope 不可达或校验失败时回退 GitHub。
                ' 两源都校验大小与 SHA-256。
                Dim downloaded As Boolean = False
                Dim modelScopeError As Exception = Nothing
                Try
                    Await DownloadToFileAsync(BuildResolveUrl(manifest.Package.Path), manifest, progress, temporary, False)
                    downloaded = True
                Catch ex As Exception
                    modelScopeError = ex
                End Try
                If Not downloaded Then
                    Try
                        Dim githubUrl = manifest.GithubPackageUrl
                        If String.IsNullOrWhiteSpace(githubUrl) Then githubUrl = BuildGithubPackageUrl(manifest)
                        Await DownloadToFileAsync(githubUrl, manifest, progress, temporary, True)
                        downloaded = True
                    Catch githubError As Exception
                        Throw New IOException("更新包下载失败（ModelScope 与 GitHub 均未成功）：" &
                            Environment.NewLine & "ModelScope：" &
                            If(modelScopeError IsNot Nothing, modelScopeError.Message, "未知错误") &
                            Environment.NewLine & "GitHub：" & githubError.Message, githubError)
                    End Try
                End If
                If Not downloaded Then
                    Throw New IOException("更新包下载失败：" & If(modelScopeError IsNot Nothing, modelScopeError.Message, "未知错误"))
                End If
                File.Move(temporary, destination, True)
                Return destination
            Catch
                Try
                    If File.Exists(temporary) Then File.Delete(temporary)
                Catch
                End Try
                Throw
            End Try
        End Function

        Public Shared Sub StartUpdate(packagePath As String, pluginRoot As String)
            Dim canonicalPluginRoot = Path.GetFullPath(PluginConfig.PluginRoot)
            If Not Path.GetFullPath(pluginRoot).Equals(canonicalPluginRoot, StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidOperationException("更新目标必须是当前插件 DLL 所在目录")
            End If
            Dim installer = Path.GetFullPath(packagePath)
            If Not File.Exists(installer) Then Throw New FileNotFoundException("更新安装器不存在", installer)
            If Not Path.GetExtension(installer).Equals(".exe", StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidDataException("更新包必须是 Burn EXE 安装器")
            End If
            Dim hostRoot = Directory.GetParent(canonicalPluginRoot)?.FullName
            If String.IsNullOrWhiteSpace(hostRoot) Then
                Throw New InvalidOperationException("无法从 Plugin 目录确定 3FUI 根目录")
            End If

            Dim startInfo As New ProcessStartInfo With {
                .FileName = installer,
                .UseShellExecute = True,
                .WorkingDirectory = Path.GetDirectoryName(installer)
            }
            ' Burn 允许通过 Variable=Value 覆盖变量；ArgumentList 会正确处理含空格的目录。
            startInfo.ArgumentList.Add("INSTALLFOLDER=" & hostRoot)
            If Process.Start(startInfo) Is Nothing Then
                Throw New InvalidOperationException("无法启动 VideoEnhancer 安装器")
            End If
        End Sub

        Private Shared Function ParseManifest(json As String) As UpdateManifest
            Dim manifest = JsonSerializer.Deserialize(Of UpdateManifest)(json, JsonOptions)
            If manifest Is Nothing OrElse manifest.SchemaVersion <> 1 OrElse
                String.IsNullOrWhiteSpace(manifest.Version) OrElse manifest.Package Is Nothing Then
                Throw New InvalidDataException("更新清单格式无效")
            End If
            Dim parsed As Version = Nothing
            If Not Version.TryParse(manifest.Version, parsed) Then
                Throw New InvalidDataException("更新清单版本号不是有效 SemVer：" & manifest.Version)
            End If
            ValidateRelativePath(manifest.Package.Path)
            If manifest.Package.Size <= 0 OrElse manifest.Package.Sha256.Length <> 64 Then
                Throw New InvalidDataException("更新清单缺少有效的文件大小或 SHA-256")
            End If
            Return manifest
        End Function

        Private Shared Async Function DownloadToFileAsync(url As String, manifest As UpdateManifest,
                progress As Action(Of Integer), temporary As String, useGithubAuth As Boolean) As Task
            Dim client As HttpClient = If(useGithubAuth,
                CreateGithubClient(TimeSpan.FromMinutes(30)),
                CreateClient(TimeSpan.FromMinutes(30)))
            Try
                Using response = Await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                    response.EnsureSuccessStatusCode()
                    Using source = Await response.Content.ReadAsStreamAsync()
                        Using output As New FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)
                            Dim buffer(1024 * 128 - 1) As Byte
                            Dim total As Long = 0
                            Dim lastPercent = -1
                            Do
                                Dim count = Await source.ReadAsync(buffer.AsMemory(0, buffer.Length))
                                If count <= 0 Then Exit Do
                                Await output.WriteAsync(buffer.AsMemory(0, count))
                                total += count
                                Dim percent = CInt(Math.Min(100, total * 100L \ manifest.Package.Size))
                                If percent <> lastPercent Then
                                    lastPercent = percent
                                    If progress IsNot Nothing Then progress(percent)
                                End If
                            Loop
                        End Using
                    End Using
                End Using
            Finally
                client.Dispose()
            End Try

            Dim info As New FileInfo(temporary)
            If info.Length <> manifest.Package.Size Then
                Throw New InvalidDataException("更新包大小校验失败")
            End If
            Using stream = File.OpenRead(temporary)
                Dim actual = Convert.ToHexString(SHA256.HashData(stream))
                If Not actual.Equals(manifest.Package.Sha256, StringComparison.OrdinalIgnoreCase) Then
                    Throw New InvalidDataException("更新包 SHA-256 校验失败")
                End If
            End Using
        End Function

        Private Shared Function CreateClient(timeout As TimeSpan) As HttpClient
            Dim client As New HttpClient With {.Timeout = timeout}
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VideoEnhancer/" & CurrentVersion)
            Return client
        End Function

        Private Shared Function CreateGithubClient(timeout As TimeSpan) As HttpClient
            Dim client As New HttpClient With {.Timeout = timeout}
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VideoEnhancer/" & CurrentVersion)
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json")
            ' 私有仓库或需要提高 API 限频时使用；公开仓库不需要。
            Dim token = Environment.GetEnvironmentVariable("VIDEOENHANCER_UPDATE_GITHUB_TOKEN")
            If Not String.IsNullOrWhiteSpace(token) Then
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " & token.Trim())
            End If
            Return client
        End Function

        ''' <summary>版本检查仓库（GitHub，唯一标准）；可用 VIDEOENHANCER_UPDATE_GITHUB_REPO=owner/name 覆盖。</summary>
        Private Shared ReadOnly Property GithubRepo As String
            Get
                Dim repo = Environment.GetEnvironmentVariable("VIDEOENHANCER_UPDATE_GITHUB_REPO")
                If String.IsNullOrWhiteSpace(repo) Then repo = DefaultGithubRepo
                Dim parts = repo.Trim().Trim("/"c).Split("/"c)
                If parts.Length <> 2 OrElse parts.Any(Function(part) String.IsNullOrWhiteSpace(part)) Then
                    Throw New InvalidDataException("VIDEOENHANCER_UPDATE_GITHUB_REPO 必须是 owner/name")
                End If
                Return String.Join("/", parts.Select(Function(part) Uri.EscapeDataString(part.Trim())))
            End Get
        End Property

        Private Shared Function BuildResolveUrl(relativePath As String) As String
            ValidateRelativePath(relativePath)
            Dim dataset = Environment.GetEnvironmentVariable("VIDEOENHANCER_UPDATE_DATASET")
            If String.IsNullOrWhiteSpace(dataset) Then dataset = DefaultDataset
            Dim datasetParts = dataset.Trim().Trim("/"c).Split("/"c)
            If datasetParts.Length <> 2 OrElse datasetParts.Any(Function(part) String.IsNullOrWhiteSpace(part)) Then
                Throw New InvalidDataException("VIDEOENHANCER_UPDATE_DATASET 必须是 owner/name")
            End If
            Dim escapedDataset = String.Join("/", datasetParts.Select(Function(part) Uri.EscapeDataString(part)))
            Dim escapedPath = String.Join("/", relativePath.Replace("\"c, "/"c).Split("/"c).
                Select(Function(part) Uri.EscapeDataString(part)))
            Return "https://www.modelscope.cn/datasets/" & escapedDataset & "/resolve/master/" & escapedPath
        End Function

        Private Shared Function BuildGithubPackageUrl(manifest As UpdateManifest) As String
            If manifest Is Nothing OrElse manifest.Package Is Nothing Then
                Throw New InvalidDataException("更新清单缺少更新包信息")
            End If
            Dim fileName = Path.GetFileName(manifest.Package.Path.Replace("/"c, Path.DirectorySeparatorChar))
            If String.IsNullOrWhiteSpace(fileName) Then Throw New InvalidDataException("更新包文件名为空")
            Return "https://github.com/" & GithubRepo & "/releases/download/v" &
                Uri.EscapeDataString(manifest.Version.Trim()) & "/" & Uri.EscapeDataString(fileName)
        End Function

        Private Shared Sub ValidateRelativePath(relativePath As String)
            If String.IsNullOrWhiteSpace(relativePath) Then Throw New InvalidDataException("更新文件路径为空")
            Dim normalized = relativePath.Replace("\"c, "/"c)
            If normalized.StartsWith("/", StringComparison.Ordinal) OrElse
                normalized.Split("/"c).Any(Function(part) part.Length = 0 OrElse part = "." OrElse part = "..") Then
                Throw New InvalidDataException("更新文件路径不安全：" & relativePath)
            End If
        End Sub

    End Class

End Namespace
