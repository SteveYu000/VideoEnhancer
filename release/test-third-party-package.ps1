param([string]$Package = '', [string]$SourcePackage = '', [string]$ArtifactsRoot = '')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ArtifactsRoot) { $ArtifactsRoot = Join-Path $repo 'Artifacts' }
if (-not $Package) { $Package = Join-Path $ArtifactsRoot 'VideoEnhancer.zip' }
if (-not $SourcePackage) { $SourcePackage = Join-Path $ArtifactsRoot 'VideoEnhancer-Source.zip' }
$metadata = Get-Content -LiteralPath (Join-Path $repo 'cli/third-party/fff-native/runtime.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Package))
$prefix = 'plugin/videoenhancer/'
$script:checked = 0
function Check-ZipHash([string]$Path, [string]$Hash) {
    $entry = $zip.GetEntry($prefix + $Path.Replace('\','/'))
    if (-not $entry) { throw "ZIP 缺少 $Path" }
    $stream = $entry.Open()
    try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
    finally { $stream.Dispose() }
    if ($actual -ne $Hash) { throw "ZIP 哈希错误：$Path" }
    $script:checked++
}
try {
    foreach ($dll in $metadata.files) { Check-ZipHash "bin/fff-native-11/$($dll.name)" $dll.sha256 }
    foreach ($license in $metadata.notices) { Check-ZipHash "licenses/fff-native/$($license.name)" $license.sha256 }
    foreach ($relative in @('SOURCE.txt','runtime.json')) {
        Check-ZipHash "licenses/fff-native/$relative" (Get-FileHash -LiteralPath (Join-Path $repo "cli/third-party/fff-native/$relative")).Hash
    }
    foreach ($relative in @('aria2-next/COPYING','aria2-next/AUTHORS','aria2-next/SOURCE.txt','aria2-next/DEPENDENCY-LICENSES.txt','LakeUI/SOURCE.txt','RVE/AGPL-3.0.txt','RVE/SOURCE.txt')) {
        Check-ZipHash "licenses/$relative" (Get-FileHash -LiteralPath (Join-Path $repo "cli/third-party/$relative")).Hash
    }
    Check-ZipHash 'LICENSE.txt' (Get-FileHash -LiteralPath (Join-Path $repo 'LICENSE')).Hash
    Check-ZipHash 'LICENSE-SCOPE.md' (Get-FileHash -LiteralPath (Join-Path $repo 'LICENSE-SCOPE.md')).Hash
    Check-ZipHash 'THIRD-PARTY-NOTICES.txt' (Get-FileHash -LiteralPath (Join-Path $repo 'cli/THIRD-PARTY-NOTICES.txt')).Hash
    foreach ($relative in @('wix/LICENSE.TXT','wix/OSMFEULA.txt','wix/SOURCE.txt','wix/runtime.json')) {
        Check-ZipHash "licenses/$relative" (Get-FileHash -LiteralPath (Join-Path $repo "cli/third-party/$relative")).Hash
    }
    foreach ($relative in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT','SOURCE.txt')) {
        Check-ZipHash "licenses/dotnet/$relative" (Get-FileHash -LiteralPath (Join-Path $ArtifactsRoot "licenses/dotnet/$relative")).Hash
    }
    Check-ZipHash 'bin/aria2-next/aria2-next.exe' '08AFAF2A44811D38E7CE538DA719AB06D6925BCAAD1231EE7B92C497F58E5AAC'
    Check-ZipHash 'bin/7zip/7za.exe' 'EDBEE35370E14030E4C785CF88200F42DC651C1EB4217C1E3963C38A12F099B0'
    $unexpected = @($zip.Entries | Where-Object { $_.FullName -match 'SharpCompress|LakeUI\.dll|FFF\.Player\.exe|EmbeddedFffNativePayload' })
    if ($unexpected.Count -gt 0) { throw 'ZIP 仍含已移除组件或未授权的播放器/宿主框架' }
    if (@($zip.Entries | Where-Object { $_.FullName -match '\.(?:tar\.(?:gz|xz)|cs|vb|py|ps1)$|/source/' }).Count -gt 0) {
        throw '二进制 ZIP 仍包含源码或源码归档'
    }
} finally { $zip.Dispose() }

# 对应源码在独立资产内逐项验证，且必须包含可重建的项目源码。
$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($SourcePackage))
$prefix = 'third-party/'
try {
    foreach ($source in $metadata.sources) { Check-ZipHash "fff-native/$($source.name)" $source.sha256 }
    Check-ZipHash 'aria2-next/aria2-next-2.8.3-source.tar.gz' '420E31256B5E29DE6AB9B295423ED29431494527B4FDC9AFB3946DFF4A73EAF7'
    Check-ZipHash '7zip/7z2603-src.tar.xz' '9CBDE5099C6DEB73691B0579063DA5827522CCBBCBA3F0020FD04E8C8C16C0D4'
    Check-ZipHash 'wix/wix-6.0.2-source.tar.gz' '9490A024F2DA5140605B87D8A9415E54029F4F2A5D4C7820FDD064A2EE80DD30'
    $prefix = 'VideoEnhancer/'
    foreach ($relative in @('LICENSE','LICENSE-SCOPE.md','VideoEnhancer.slnx','cli/Program.cs','cli/embedded-tools/rve_output_scale.py','VideoEnhancerPlugin/FffNativeRuntime.vb','release/acquire-fff-native.ps1','release/create-source-package.ps1')) {
        Check-ZipHash $relative (Get-FileHash -LiteralPath (Join-Path $repo $relative)).Hash
    }
    if (@($zip.Entries | Where-Object { $_.FullName -match '\.(?:exe|dll|pdb)$|/(?:obj|bin|Artifacts)/' }).Count -gt 0) { throw '源码包包含二进制或本机构建缓存' }
} finally { $zip.Dispose() }
$prefix = 'plugin/videoenhancer/'

# 真实安装内部载荷，比较安装器与 ZIP 的每项哈希，而非只检查打包脚本。
$hostRoot = Join-Path $ArtifactsRoot ('third-party-install-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($hostRoot) | Out-Null
[IO.File]::WriteAllText((Join-Path $hostRoot 'FFmpegFreeUI.exe'), 'test host')
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $ArtifactsRoot '.installer/VideoEnhancerPortablePayload.exe'))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($arg in @('--install-folder',$hostRoot,'--quiet','--skip-legacy-cleanup')) { $start.ArgumentList.Add($arg) }
$process = [Diagnostics.Process]::Start($start)
$stdoutTask = $process.StandardOutput.ReadToEndAsync()
$stderrTask = $process.StandardError.ReadToEndAsync()
if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw '内部安装载荷超时' }
if ($process.ExitCode -ne 0) { throw "内部安装载荷失败：$($stderrTask.GetAwaiter().GetResult())" }
$process.Dispose()
$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Package))
try {
    foreach ($entry in $zip.Entries | Where-Object { $_.FullName.StartsWith($prefix) -and $_.Length -gt 0 }) {
        $target = Join-Path $hostRoot $entry.FullName
        if (-not [IO.File]::Exists($target)) { throw "安装器缺少 $($entry.FullName)" }
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ((Get-FileHash -LiteralPath $target).Hash -ne $hash) { throw "安装器与 ZIP 内容不同：$target" }
        $script:checked++
    }
} finally { $zip.Dispose() }

# 从真实安装的插件调用加载桥，并检查 API 版本和所有桥接出口。
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $hostRoot 'plugin/videoenhancer.3fui.dll'))
if ($assembly.GetType('videoenhancer.EmbeddedFffNativePayload')) { throw '插件仍含内嵌 DLL 模块' }
$binding = [Reflection.BindingFlags]'NonPublic,Static'
$native = $assembly.GetType('videoenhancer.FffNativeRuntime')
$handle = $native.GetMethod('LoadLibrary', $binding).Invoke($null, @())
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FffApiProbe {
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true)]
    public static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate uint GetApiVersion();
    public static uint Version(IntPtr module) => Marshal.GetDelegateForFunctionPointer<GetApiVersion>(GetProcAddress(module,"FFF3FP_GetApiVersion"))();
}
'@
$actualVersion = [FffApiProbe]::Version($handle)
if ($actualVersion -ne 11) { throw "FFF.Native API 版本不兼容：$actualVersion" }
foreach ($name in @('GetApiVersion','Create','Open','Play','Pause','DiscardAudioOutput','SetVolume','Seek','SetOutputWindow','GetSnapshot','Destroy')) {
    if ([FffApiProbe]::GetProcAddress($handle, "FFF3FP_$name") -eq [IntPtr]::Zero) { throw "FFF 出口缺失：$name" }
    $script:checked++
}
$deps = Get-Content -LiteralPath (Join-Path $repo 'cli/bin/Release/net10.0-windows/win-x64/videoenhancer.deps.json') -Encoding UTF8 -Raw
if ($deps.Contains('SharpCompress')) { throw 'CLI 仍引用 SharpCompress' }
# 普通 CLI 构建的资源清单不能隐藏 7-Zip 源码归档。
$stream = [IO.File]::OpenRead((Join-Path $repo 'cli/bin/Release/net10.0-windows/win-x64/videoenhancer.dll'))
$pe = [Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    foreach ($resourceHandle in $reader.ManifestResources) {
        $name = $reader.GetString($reader.GetManifestResource($resourceHandle).Name)
        if ($name -match '\.tar\.(gz|xz)$') { throw '运行 EXE 仍内嵌源码归档' }
    }
} finally { $pe.Dispose(); $stream.Dispose() }
Write-Host "THIRD_PARTY_PACKAGE_PASS|$script:checked|API11|$hostRoot"
