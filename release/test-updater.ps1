param(
    [string]$Version = '',
    [string]$Package = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Get-ProjectVersion([string]$projectPath) {
    $document = [System.Xml.XmlDocument]::new()
    $document.Load($projectPath)
    $nodes = @($document.SelectNodes('/Project/PropertyGroup/Version'))
    if ($nodes.Count -ne 1 -or [string]::IsNullOrWhiteSpace($nodes[0].InnerText)) {
        throw "项目必须声明且只能声明一个 Version：$projectPath"
    }
    return $nodes[0].InnerText.Trim()
}

function Assert-Contains([string]$source, [string[]]$needles, [string]$description) {
    foreach ($needle in $needles) {
        if (-not $source.Contains($needle, [System.StringComparison]::Ordinal)) {
            throw "$description 缺少契约：$needle"
        }
    }
}

function Assert-NotContains([string]$source, [string[]]$needles, [string]$description) {
    foreach ($needle in $needles) {
        if ($source.Contains($needle, [System.StringComparison]::Ordinal)) {
            throw "$description 仍包含已废弃协议：$needle"
        }
    }
}

function Invoke-BurnLayout([string]$installer, [string]$layoutRoot, [string]$hostRoot) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $installer
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
            '-quiet', '-norestart', '-layout', $layoutRoot,
            "INSTALLFOLDER=$hostRoot", 'SKIPLEGACYCLEANUP=1')) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) { throw "无法启动更新安装器：$installer" }
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = (($stdout.GetAwaiter().GetResult(), $stderr.GetAwaiter().GetResult()) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join [Environment]::NewLine
        if (-not [string]::IsNullOrWhiteSpace($output)) { Write-Host $output }
        return $process.ExitCode
    } finally {
        $process.Dispose()
    }
}

$pluginVersion = Get-ProjectVersion (Join-Path $root 'VideoEnhancerPlugin\VideoEnhancerPlugin.vbproj')
$cliVersion = Get-ProjectVersion (Join-Path $root 'cli\VideoEnhancer.csproj')
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $pluginVersion }
if ($pluginVersion -ne $Version -or $cliVersion -ne $Version) {
    throw "项目版本不一致：插件=$pluginVersion，CLI=$cliVersion，测试版本=$Version"
}
if ([string]::IsNullOrWhiteSpace($Package)) {
    $Package = Join-Path $root 'Artifacts\VideoEnhancerInstaller.exe'
}
$Package = [System.IO.Path]::GetFullPath($Package)
if (-not (Test-Path -LiteralPath $Package -PathType Leaf)) {
    throw "缺少待测更新安装器：$Package"
}

$updaterPath = Join-Path $root 'VideoEnhancerPlugin\PluginUpdater.vb'
$programPath = Join-Path $root 'cli\Program.cs'
$bundlePath = Join-Path $root 'installer\Bundle\Bundle.wxs'
$msiPath = Join-Path $root 'installer\Package\Package.wxs'
$updater = Get-Content -Raw -Encoding UTF8 $updaterPath
$program = Get-Content -Raw -Encoding UTF8 $programPath
$bundle = Get-Content -Raw -Encoding UTF8 $bundlePath
$msi = Get-Content -Raw -Encoding UTF8 $msiPath

Assert-Contains $updater @(
    'Return remoteVersion > installedVersion',
    '.UseShellExecute = True',
    'startInfo.ArgumentList.Add("INSTALLFOLDER=" & hostRoot)',
    'Path.GetExtension(installer).Equals(".exe"',
    'SHA256.HashData(stream)') '插件更新器'
Assert-NotContains $updater @(
    '--apply-update',
    '--update-package',
    '--update-target',
    '--wait-pid',
    '--restart-exe',
    'update-result.txt') '插件更新器'
Assert-NotContains $program @(
    '--create-installer-bundle',
    '--apply-update',
    '--update-package',
    '--update-target',
    '--wait-pid',
    '--restart-exe') 'CLI'
Assert-Contains $bundle @(
    'Name="INSTALLFOLDER"',
    'Persisted="yes"',
    'bal:Overridable="yes"',
    '<MsiProperty Name="THREEFUIROOT"',
    'Value="[INSTALLFOLDER]"') 'Burn Bundle'
Assert-Contains $msi @(
    'Name="InstallRoot"',
    'Value="[THREEFUIROOT]"',
    '<RemoveRegistryKey Id="RemoveVideoEnhancerRegistryKey"',
    '--cleanup-legacy-residue --plugin-root',
    'ACTION &lt;&gt; &quot;ADMIN&quot;') 'MSI'

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('VideoEnhancerBurnUpdateTest-' + [guid]::NewGuid().ToString('N'))
$resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
if (-not $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "测试目录不在系统临时目录：$resolvedTest"
}
New-Item -ItemType Directory -Force -Path $resolvedTest | Out-Null
try {
    $layoutRoot = Join-Path $resolvedTest 'layout'
    $selectedHost = Join-Path $resolvedTest 'FFmpegFreeUI with spaces'
    New-Item -ItemType Directory -Force -Path $layoutRoot, $selectedHost | Out-Null
    $exitCode = Invoke-BurnLayout $Package $layoutRoot $selectedHost
    if ($exitCode -ne 0) { throw "带 INSTALLFOLDER 的 Burn layout 失败，退出码：$exitCode" }
    $laidOutPackage = Join-Path $layoutRoot ([System.IO.Path]::GetFileName($Package))
    if (-not (Test-Path -LiteralPath $laidOutPackage -PathType Leaf)) {
        throw "Burn 更新安装器没有生成布局输出：$laidOutPackage"
    }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $laidOutPackage).Hash -ne
        (Get-FileHash -Algorithm SHA256 -LiteralPath $Package).Hash) {
        throw 'Burn 更新安装器的布局输出哈希不一致'
    }

    Write-Host 'UPDATER_TESTS_PASS|version-only-check|sha256-download|burn-launch|install-folder-forwarding|legacy-protocol-removed'
} finally {
    if (Test-Path -LiteralPath $resolvedTest) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force
    }
}
