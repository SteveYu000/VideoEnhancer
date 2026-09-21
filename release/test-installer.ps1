param(
    [string]$Installer = '',
    [string]$Msi = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Installer)) {
    $Installer = Join-Path $root 'Artifacts\VideoEnhancerInstaller.exe'
}
if ([string]::IsNullOrWhiteSpace($Msi)) {
    $Msi = Join-Path $root 'installer\Package\bin\x64\Release\VideoEnhancer.msi'
}
$Installer = [System.IO.Path]::GetFullPath($Installer)
$Msi = [System.IO.Path]::GetFullPath($Msi)

foreach ($required in @($Installer, $Msi)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "缺少待测安装资产：$required"
    }
}

function Get-ProjectValue([string]$projectPath, [string]$propertyName) {
    $document = [System.Xml.XmlDocument]::new()
    $document.Load($projectPath)
    $nodes = @($document.SelectNodes("/Project/PropertyGroup/$propertyName"))
    if ($nodes.Count -ne 1 -or [string]::IsNullOrWhiteSpace($nodes[0].InnerText)) {
        throw "项目必须声明且只能声明一个 $propertyName：$projectPath"
    }
    return $nodes[0].InnerText.Trim()
}

function Invoke-NativeProcess([string]$filePath, [string[]]$argumentList) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $filePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $argumentList) { $startInfo.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) { throw "无法启动进程：$filePath" }
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

function Assert-FileHash([string]$actualPath, [string]$expectedPath, [string]$description) {
    if (-not (Test-Path -LiteralPath $actualPath -PathType Leaf)) {
        throw "MSI 管理映像缺少 $description：$actualPath"
    }
    if (-not (Test-Path -LiteralPath $expectedPath -PathType Leaf)) {
        throw "构建输出缺少 $description：$expectedPath"
    }
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $actualPath).Hash
    $expected = (Get-FileHash -Algorithm SHA256 -LiteralPath $expectedPath).Hash
    if ($actual -ne $expected) { throw "$description 的 MSI 载荷哈希与构建输出不一致" }
}

function Assert-SourceContains([string]$path, [string[]]$needles) {
    $source = Get-Content -Raw -Encoding UTF8 $path
    foreach ($needle in $needles) {
        if (-not $source.Contains($needle, [System.StringComparison]::Ordinal)) {
            throw "安装器源文件缺少契约 '$needle'：$path"
        }
    }
}

$cliProject = Join-Path $root 'cli\VideoEnhancer.csproj'
$version = Get-ProjectValue $cliProject 'Version'
$aria2NextVersion = Get-ProjectValue $cliProject 'Aria2NextVersion'
$runtimeExe = Join-Path $root 'cli\bin\Release\net10.0-windows\win-x64\publish\videoenhancer.exe'
$pluginDll = Join-Path $root 'VideoEnhancerPlugin\obj\plugin-artifact\videoenhancer.3fui.dll'
$aria2Next = Join-Path $root "cli\obj\third-party\aria2-next\$aria2NextVersion\aria2-next.exe"
$packageSource = Join-Path $root 'installer\Package\Package.wxs'
$bundleSource = Join-Path $root 'installer\Bundle\Bundle.wxs'

foreach ($required in @($runtimeExe, $pluginDll, $aria2Next, $packageSource, $bundleSource)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "缺少安装门禁输入：$required"
    }
}

$runtimeVersion = ((& $runtimeExe --version) | Select-Object -First 1).Trim()
if ($runtimeVersion -ne $version) {
    throw "videoenhancer.exe 报告版本 '$runtimeVersion'，与项目版本 $version 不一致"
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('VideoEnhancerWixTest-' + [guid]::NewGuid().ToString('N'))
$resolvedTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$resolvedTest = [System.IO.Path]::GetFullPath($testRoot)
if (-not $resolvedTest.StartsWith($resolvedTemp, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "测试目录不在系统临时目录：$resolvedTest"
}

New-Item -ItemType Directory -Force -Path $resolvedTest | Out-Null
try {
    # Burn 的无副作用布局模式验证引导程序可启动、解析链并导出完整的压缩安装包。
    $layoutRoot = Join-Path $resolvedTest 'burn-layout'
    New-Item -ItemType Directory -Force -Path $layoutRoot | Out-Null
    $layoutExitCode = Invoke-NativeProcess $Installer @(
        '-quiet', '-norestart', '-layout', $layoutRoot,
        "INSTALLFOLDER=$(Join-Path $resolvedTest 'selected-host')",
        'SKIPLEGACYCLEANUP=1')
    if ($layoutExitCode -ne 0) { throw "Burn layout 失败，退出码：$layoutExitCode" }
    $laidOutInstaller = Join-Path $layoutRoot ([System.IO.Path]::GetFileName($Installer))
    if (-not (Test-Path -LiteralPath $laidOutInstaller -PathType Leaf)) {
        throw "Burn layout 未生成完整安装包：$laidOutInstaller"
    }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $laidOutInstaller).Hash -ne
        (Get-FileHash -Algorithm SHA256 -LiteralPath $Installer).Hash) {
        throw 'Burn layout 导出的安装包哈希不一致'
    }

    # MSI 管理安装只展开文件，不写注册表、不注册产品，也不会执行旧配置清理动作。
    $adminRoot = Join-Path $resolvedTest 'msi-admin-image'
    New-Item -ItemType Directory -Force -Path $adminRoot | Out-Null
    $msiExitCode = Invoke-NativeProcess 'msiexec.exe' @(
        '/a', $Msi, '/qn', '/norestart', "TARGETDIR=$adminRoot")
    if ($msiExitCode -ne 0) { throw "MSI 管理安装失败，退出码：$msiExitCode" }

    $installedHostRoot = Join-Path $adminRoot 'PFiles64\FFmpegFreeUI'
    $installedPluginRoot = Join-Path $installedHostRoot 'Plugin'
    $installedApplicationRoot = Join-Path $installedPluginRoot 'videoenhancer'
    Assert-FileHash (Join-Path $installedPluginRoot 'videoenhancer.3fui.dll') $pluginDll '插件 DLL'
    Assert-FileHash (Join-Path $installedApplicationRoot 'videoenhancer.exe') $runtimeExe '运行程序'
    Assert-FileHash (Join-Path $installedApplicationRoot 'bin\aria2-next\aria2-next.exe') $aria2Next 'aria2-next'
    Assert-FileHash (Join-Path $installedApplicationRoot 'THIRD-PARTY-NOTICES.txt') (Join-Path $root 'cli\THIRD-PARTY-NOTICES.txt') '第三方声明'
    Assert-FileHash (Join-Path $installedApplicationRoot 'licenses\aria2-next\COPYING') (Join-Path $root 'cli\third-party\aria2-next\COPYING') 'aria2-next GPL 许可证'
    Assert-FileHash (Join-Path $installedApplicationRoot 'licenses\aria2-next\AUTHORS') (Join-Path $root 'cli\third-party\aria2-next\AUTHORS') 'aria2-next 作者声明'
    Assert-FileHash (Join-Path $installedApplicationRoot 'licenses\aria2-next\SOURCE.txt') (Join-Path $root 'cli\third-party\aria2-next\SOURCE.txt') 'aria2-next 源码说明'
    Assert-FileHash (Join-Path $installedApplicationRoot 'licenses\SharpCompress\LICENSE.txt') (Join-Path $root 'cli\third-party\SharpCompress\LICENSE.txt') 'SharpCompress 许可证'

    # 直接调用 MSI 使用的隐藏入口，验证旧平铺数据迁移到便携目录且外部旧残留只被清理、不产生新文件。
    $legacyPluginRoot = Join-Path $resolvedTest 'legacy-host\Plugin'
    $legacyApplicationRoot = Join-Path $legacyPluginRoot 'videoenhancer'
    $legacyLocalAppData = Join-Path $resolvedTest 'legacy-local-app-data'
    $legacyTempRoot = Join-Path $resolvedTest 'legacy-temp'
    foreach ($directory in @(
            (Join-Path $legacyPluginRoot 'models\User'),
            (Join-Path $legacyPluginRoot 'python\backend'),
            (Join-Path $legacyPluginRoot 'bin\ffmpeg'),
            (Join-Path $legacyPluginRoot 'cache'),
            (Join-Path $legacyApplicationRoot 'models'),
            (Join-Path $legacyLocalAppData 'FFmpegFreeUI\VideoEnhancer\updater\old'),
            (Join-Path $legacyTempRoot 'videoenhancer.3fui\fff-native-11'))) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
    [System.IO.File]::WriteAllText((Join-Path $legacyPluginRoot 'models\User\model.pth'), 'model')
    [System.IO.File]::WriteAllText((Join-Path $legacyPluginRoot 'models\duplicate.bin'), 'same')
    [System.IO.File]::WriteAllText((Join-Path $legacyApplicationRoot 'models\duplicate.bin'), 'same')
    [System.IO.File]::WriteAllText((Join-Path $legacyPluginRoot 'python\backend\custom.py'), 'python')
    [System.IO.File]::WriteAllText((Join-Path $legacyPluginRoot 'bin\ffmpeg\ffmpeg.exe'), 'ffmpeg')
    [System.IO.File]::WriteAllText((Join-Path $legacyPluginRoot 'cache\capabilities.json'), 'cache')
    [System.IO.File]::WriteAllText((Join-Path $legacyPluginRoot 'ffmpeg_log.txt'), 'log')
    [System.IO.File]::WriteAllText((Join-Path $legacyPluginRoot 'python_legacy.7z'), 'archive')
    [System.IO.File]::WriteAllText((Join-Path $legacyPluginRoot 'videoenhancer.exe'), 'old-runtime')
    [System.IO.File]::WriteAllText((Join-Path $legacyPluginRoot 'videoenhancer.ini'), 'core-path=D:\old')
    [System.IO.File]::WriteAllText(
        (Join-Path $legacyLocalAppData 'FFmpegFreeUI\videoenhancer.plugin.json'),
        '{"ExePath":"D:\\old\\videoenhancer.exe","Enabled":true}')
    [System.IO.File]::WriteAllText(
        (Join-Path $legacyLocalAppData 'FFmpegFreeUI\VideoEnhancer\updater\old\videoenhancer-updater.exe'),
        'old-updater')
    [System.IO.File]::WriteAllText(
        (Join-Path $legacyTempRoot 'videoenhancer.3fui\fff-native-11\FFF.Native.dll'),
        'old-native')

    $cleanupExitCode = Invoke-NativeProcess $runtimeExe @(
        '--cleanup-legacy-residue',
        '--plugin-root', $legacyPluginRoot,
        '--legacy-local-app-data', $legacyLocalAppData,
        '--legacy-temp-root', $legacyTempRoot)
    if ($cleanupExitCode -ne 0) { throw "旧插件残留清理入口失败，退出码：$cleanupExitCode" }
    foreach ($relative in @(
            'models\User\model.pth',
            'models\duplicate.bin',
            'python\backend\custom.py',
            'bin\ffmpeg\ffmpeg.exe',
            'cache\capabilities.json',
            'ffmpeg_log.txt',
            'python_legacy.7z',
            'videoenhancer.plugin.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $legacyApplicationRoot $relative) -PathType Leaf)) {
            throw "旧插件文件未迁移到便携目录：$relative"
        }
    }
    $migratedConfig = Get-Content -Raw -Encoding UTF8 (Join-Path $legacyApplicationRoot 'videoenhancer.plugin.json')
    if ($migratedConfig -match '"ExePath"' -or $migratedConfig -notmatch '"Enabled": true') {
        throw '旧 AppData 配置未正确迁移或仍包含 ExePath'
    }
    foreach ($obsolete in @(
            (Join-Path $legacyPluginRoot 'videoenhancer.exe'),
            (Join-Path $legacyPluginRoot 'videoenhancer.ini'),
            (Join-Path $legacyPluginRoot 'models'),
            (Join-Path $legacyPluginRoot 'python'),
            (Join-Path $legacyPluginRoot 'bin'),
            (Join-Path $legacyPluginRoot 'cache'),
            (Join-Path $legacyLocalAppData 'FFmpegFreeUI\videoenhancer.plugin.json'),
            (Join-Path $legacyLocalAppData 'FFmpegFreeUI\VideoEnhancer\updater\old\videoenhancer-updater.exe'),
            (Join-Path $legacyTempRoot 'videoenhancer.3fui\fff-native-11\FFF.Native.dll'))) {
        if (Test-Path -LiteralPath $obsolete) { throw "旧插件残留未清理：$obsolete" }
    }

    Assert-SourceContains $packageSource @(
        'Scope="perMachine"',
        'UpgradeCode="A9404409-2388-428B-95CC-AA456B7378BA"',
        'InstallDirectory="THREEFUIROOT"',
        '<CreateFolder />',
        'Name="InstallRoot"',
        '<RemoveRegistryKey Id="RemoveVideoEnhancerRegistryKey"',
        'ExeCommand="[CustomActionData]"',
        'Execute="commit"',
        'Impersonate="no"',
        '--legacy-local-app-data',
        '--legacy-temp-root',
        'Id="CleanupCurrentUserRegistryResidue"',
        'ExeCommand="--cleanup-registry-residue"',
        'REMOVE~=&quot;ALL&quot; AND NOT UPGRADINGPRODUCTCODE',
        'ACTION &lt;&gt; &quot;ADMIN&quot;',
        'NOT SKIPLEGACYCLEANUP')
    Assert-SourceContains $bundleSource @(
        'UpgradeCode="519D1BAF-FB98-4C44-8EBE-753B0451ABD5"',
        'Name="INSTALLFOLDER"',
        'Persisted="yes"',
        'bal:Overridable="yes"',
        '<MsiProperty Name="THREEFUIROOT"',
        'Compressed="yes"')

    Write-Host 'INSTALLER_TESTS_PASS|burn-layout|msi-admin-image|payload-hashes|legacy-plugin-migration|registry-uninstall-cleanup'
} finally {
    if (Test-Path -LiteralPath $resolvedTest) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force
    }
}
