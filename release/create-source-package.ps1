param(
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [Parameter(Mandatory = $true)][string]$Aria2Source,
    [Parameter(Mandatory = $true)][string]$SevenZipSource,
    [Parameter(Mandatory = $true)][string]$FffNativeCache
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$output = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$temporary = "$output.new-$([Guid]::NewGuid().ToString('N'))"
$metadata = Get-Content -LiteralPath (Join-Path $repo 'cli/third-party/fff-native/runtime.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$files = [Collections.Generic.List[object]]::new()

function Add-Source([string]$Path, [string]$Name, [string]$ExpectedHash = '') {
    if (-not [IO.File]::Exists($Path)) { throw "源码包缺少文件：$Path" }
    if ($ExpectedHash -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $ExpectedHash) {
        throw "源码包校验失败：$Path"
    }
    $files.Add(@{ Path = $Path; Name = $Name.Replace('\','/') })
}

# 使用明确的源码目录，跳过本机缓存；源码快照构建不依赖 .git 目录。
foreach ($name in @('LICENSE','LICENSE-SCOPE.md','README.md','VideoEnhancer.slnx','deploy.ps1','.gitignore','.gitattributes')) {
    Add-Source (Join-Path $repo $name) "VideoEnhancer/$name"
}
Add-Source (Join-Path $repo 'docs/third-party-license-audit.md') 'VideoEnhancer/docs/third-party-license-audit.md'
foreach ($name in @('cli','VideoEnhancerPlugin','installer','release')) {
    $directories = [Collections.Generic.Stack[string]]::new()
    $directories.Push((Join-Path $repo $name))
    while ($directories.Count -gt 0) {
        $directory = $directories.Pop()
        foreach ($entry in Get-ChildItem -LiteralPath $directory -Force) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "源码目录包含重解析点：$($entry.FullName)" }
            if ($entry.PSIsContainer) {
                if ($entry.Name -notin @('bin','obj','dist','out','build','publish','__pycache__','.vs')) {
                    $directories.Push($entry.FullName)
                }
            } elseif ($entry.Extension -notin @('.exe','.dll','.pdb','.pyc','.user') -and $entry.Name -notmatch '^ffmpeg-.*\.log$') {
                Add-Source $entry.FullName ('VideoEnhancer/' + [IO.Path]::GetRelativePath($repo, $entry.FullName))
            }
        }
    }
}
$project = [xml](Get-Content -LiteralPath (Join-Path $repo 'cli/VideoEnhancer.csproj') -Encoding UTF8 -Raw)
$ariaHash = $project.SelectSingleNode('/Project/PropertyGroup/Aria2NextSourceSha256').InnerText
Add-Source $Aria2Source ('third-party/aria2-next/' + [IO.Path]::GetFileName($Aria2Source)) $ariaHash
Add-Source $SevenZipSource ('third-party/7zip/' + [IO.Path]::GetFileName($SevenZipSource)) '9CBDE5099C6DEB73691B0579063DA5827522CCBBCBA3F0020FD04E8C8C16C0D4'
foreach ($source in $metadata.sources) {
    Add-Source (Join-Path $FffNativeCache "licenses/source/$($source.name)") "third-party/fff-native/$($source.name)" $source.sha256
}
$wix = Get-Content -LiteralPath (Join-Path $repo 'cli/third-party/wix/runtime.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$wixProject = [xml](Get-Content -LiteralPath (Join-Path $repo 'installer/Bundle/VideoEnhancer.Bundle.wixproj') -Encoding UTF8 -Raw)
if ($wixProject.Project.Sdk -ne "WixToolset.Sdk/$($wix.version)") { throw 'WiX 构建版本与对应源码锁定值不一致' }
$wixSource = Join-Path $repo "cli/obj/third-party/wix/$($wix.version)/$($wix.name)"
if (-not [IO.File]::Exists($wixSource)) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($wixSource)) | Out-Null
    $download = "$wixSource.new-$([Guid]::NewGuid().ToString('N'))"
    try {
        Invoke-WebRequest -Uri $wix.url -OutFile $download -MaximumRetryCount 3 -RetryIntervalSec 2
        if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $wix.sha256) { throw 'WiX 对应源码下载校验失败' }
        Move-Item -LiteralPath $download -Destination $wixSource
    } finally { if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download } }
}
Add-Source $wixSource "third-party/wix/$($wix.name)" $wix.sha256
try {
    $zip = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files | Sort-Object Name) {
            $compression = if ($file.Name.StartsWith('third-party/')) { [IO.Compression.CompressionLevel]::NoCompression } else { [IO.Compression.CompressionLevel]::Optimal }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.Path, $file.Name, $compression) | Out-Null
        }
    } finally { $zip.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $output -Force
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
Write-Host "独立源码包：$output（$($files.Count) 项；不进入二进制 ZIP 或安装器）"
