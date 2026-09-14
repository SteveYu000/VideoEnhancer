$ErrorActionPreference = 'Stop'

$proj = Join-Path $PSScriptRoot 'VideoEnhancer.csproj'
$stage = Join-Path $PSScriptRoot '.publish'
$releaseRoot = Split-Path -Parent $PSScriptRoot

# 清理旧的临时发布目录
if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}

# 单文件自包含发布
dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $stage

# 只复制单个 exe 到当前 1.4 发布目录
$exe = Join-Path $stage 'videoenhancer.exe'
$dest = Join-Path $releaseRoot 'videoenhancer.exe'
Copy-Item -LiteralPath $exe -Destination $dest -Force

# 同时产出“手动安装包”zip：目录结构与实机安装布局完全一致，
# 用户把解压出的 plugin 文件夹整体复制到 3FUI 根目录（与 FFmpegFreeUI.exe 同级）
# 覆盖即可完成安装，无需运行安装器。
# 注意：发布流程对本 zip 计算哈希时以最终产物为准。
$pluginDll = Join-Path $releaseRoot 'VideoEnhancerPlugin\out\videoenhancer.dll'
if (-not (Test-Path -LiteralPath $pluginDll -PathType Leaf)) {
    throw "未找到插件 DLL，无法生成手动安装包：$pluginDll"
}
else {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipPath = Join-Path $releaseRoot 'videoenhancer-manual-install.zip'
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        # 用纯实例方法写 zip 条目，避免 PS 对扩展方法/重载的绑定差异。
        foreach ($pair in @(
            @($pluginDll, 'plugin/videoenhancer.3fui.dll'),
            @($dest, 'plugin/videoenhancer/videoenhancer.exe')
        )) {
            $entry = $zip.CreateEntry($pair[1])
            $source = [System.IO.File]::OpenRead($pair[0])
            try {
                $target = $entry.Open()
                try {
                    $source.CopyTo($target)
                } finally {
                    $target.Dispose()
                }
            } finally {
                $source.Dispose()
            }
        }
        $readme = @(
            'VideoEnhancer 手动安装包',
            '========================',
            '',
            '本包内的目录结构与 3FUI 安装布局一致，安装只需一步：',
            '',
            '把解压出的 plugin 文件夹整体复制到 3FUI 根目录（与 FFmpegFreeUI.exe 同级），',
            '如提示已有同名文件选择覆盖即可。完成后启动 3FUI，在插件里执行一次环境检查。',
            '',
        '模型与后端（Python 环境等）体积较大，不在本包内；',
            '装好插件后在模型下载页按需获取。',
            '',
            '说明：双击 videoenhancer.exe 也可以走交互式安装流程，两种方式效果相同。'
        ) -join "`r`n"
        $entry = $zip.CreateEntry('手动安装说明.txt')
        $writer = New-Object System.IO.StreamWriter($entry.Open(), (New-Object System.Text.UTF8Encoding($true)))
        $writer.Write($readme)
        $writer.Dispose()
    } finally {
        $zip.Dispose()
    }
    Write-Host ("手动安装包: " + $zipPath)
}

Write-Host "OK: $dest"
