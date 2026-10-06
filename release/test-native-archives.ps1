param([string]$Tool = '')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Tool) { $Tool = Join-Path $repo 'Artifacts/videoenhancer.exe' }
$Tool = [IO.Path]::GetFullPath($Tool)
$seven = Join-Path $repo 'cli/obj/third-party/7zip/26.03/extra/x64/7za.exe'
$testRoot = Join-Path $repo ('Artifacts/native-archives-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$script:passed = 0

function Invoke-Cli([string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Tool)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.Encoding]::UTF8
    $start.StandardErrorEncoding = [Text.Encoding]::UTF8
    foreach ($arg in $Arguments) { $start.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($start)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw '归档测试进程超时' }
    $result = @{code=$process.ExitCode; stdout=$stdoutTask.GetAwaiter().GetResult(); stderr=$stderrTask.GetAwaiter().GetResult()}
    $process.Dispose()
    return $result
}
function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $script:passed++
    Write-Host "PASS|$Name"
}
function Extract([string]$Archive, [string]$Destination) {
    $result = Invoke-Cli -Arguments @('--extract-archive', $Archive, '--extract-output', $Destination)
    if ($result.code -ne 0) { throw "解压失败：$Archive`n$($result.stderr)" }
    Check ($result.stdout.Contains('EXTRACT_COMPLETE|') -and $result.stdout.Contains('EXTRACT_PROGRESS|100')) "完成与百分比协议 $([IO.Path]::GetFileName($Archive))"
}
function Compare-Files([string]$Source, [string]$Destination) {
    $sourceFiles = @(Get-ChildItem -LiteralPath $Source -File -Recurse)
    $targetFiles = @(Get-ChildItem -LiteralPath $Destination -File -Recurse)
    if ($sourceFiles.Count -ne $targetFiles.Count) { return $false }
    foreach ($file in $sourceFiles) {
        $target = Join-Path $Destination ([IO.Path]::GetRelativePath($Source, $file.FullName))
        if (-not [IO.File]::Exists($target) -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { return $false }
    }
    return $true
}
function New-Zip([string]$Path, [object[]]$Entries) {
    $stream = [IO.File]::Create($Path)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $Entries) {
            $entry = $zip.CreateEntry($item.path, [IO.Compression.CompressionLevel]::NoCompression)
            if ($item.mode) { $entry.ExternalAttributes = $item.mode }
            $bytes = [Text.Encoding]::UTF8.GetBytes($item.data)
            $output = $entry.Open()
            try { $output.Write($bytes) } finally { $output.Dispose() }
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
    if (@($Entries | Where-Object { $_.mode }).Count -gt 0) {
        # 标记 Unix 创建平台；仅设置权限而保留 DOS 平台不会构成真实 ZIP 符号链接。
        $bytes = [IO.File]::ReadAllBytes($Path)
        for ($index = 0; $index -lt $bytes.Length - 46; $index++) {
            if ($bytes[$index] -eq 0x50 -and $bytes[$index+1] -eq 0x4b -and $bytes[$index+2] -eq 1 -and $bytes[$index+3] -eq 2) {
                $bytes[$index+5] = 3
            }
        }
        [IO.File]::WriteAllBytes($Path, $bytes)
    }
}
function Reject-Zip([string]$Name, [object[]]$Entries) {
    $archive = Join-Path $testRoot "$Name.zip"
    New-Zip $archive $Entries
    $destination = Join-Path $testRoot "reject-$Name"
    $result = Invoke-Cli -Arguments @('--extract-archive', $archive, '--extract-output', $destination)
    Check ($result.code -ne 0 -and -not $result.stdout.Contains('EXTRACT_COMPLETE|') -and
        -not (Test-Path -LiteralPath $destination)) "预检拒绝 $Name，未写目标"
}

$source = Join-Path $testRoot '源文件 with spaces'
[IO.Directory]::CreateDirectory((Join-Path $source '子目录/空目录')) | Out-Null
[IO.File]::WriteAllText((Join-Path $source '子目录/中文.txt'), '中文归档数据', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $source '-option.txt'), 'CRC_TEST_PAYLOAD', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllBytes((Join-Path $source 'empty.txt'), [byte[]]@())
$archive = Join-Path $testRoot 'valid.7z'
$result = Invoke-Cli -Arguments @('--create-7z', $source, $archive)
Check ($result.code -eq 0 -and $result.stdout.Contains('ARCHIVE_CREATE_COMPLETE|')) '7za 创建 7z'
$destination = Join-Path $testRoot '7z-output'
Extract $archive $destination
Check (Compare-Files $source $destination) '7z 文件名、空文件、中文及内容哈希一致'
Check (Test-Path -LiteralPath (Join-Path $destination '子目录/空目录')) '保留空目录'
[IO.File]::WriteAllText((Join-Path $destination '-option.txt'), '旧内容')
Extract $archive $destination
Check (Compare-Files $source $destination) '覆盖已有普通文件'

$zipPath = Join-Path $testRoot 'valid.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($source, $zipPath)
$destination = Join-Path $testRoot 'zip-output'
Extract $zipPath $destination
Check (Compare-Files $source $destination) 'ZIP 内容哈希一致'
$tarPath = Join-Path $testRoot 'valid.tar'
[System.Formats.Tar.TarFile]::CreateFromDirectory($source, $tarPath, $false)
$destination = Join-Path $testRoot 'tar-output'
Extract $tarPath $destination
Check (Compare-Files $source $destination) 'TAR 内容哈希一致'
foreach ($format in @('gzip','bzip2','xz')) {
    $suffix = @{gzip='gz';bzip2='bz2';xz='xz'}[$format]
    $path = Join-Path $testRoot "valid.tar.$suffix"
    & $seven a "-t$format" $path $tarPath -y -bso0 -bsp0
    if ($LASTEXITCODE -ne 0) { throw "不能创建 $format 归档" }
    $destination = Join-Path $testRoot "$format-tar-output"
    Extract $path $destination
    Check (Compare-Files $source $destination) "$format 两层 TAR 内容哈希一致"
}
foreach ($format in @('gzip','bzip2','xz')) {
    $path = Join-Path $testRoot ("plain." + @{gzip='gz';bzip2='bz2';xz='xz'}[$format])
    & $seven a "-t$format" $path (Join-Path $source '-option.txt') -y -bso0 -bsp0
    if ($LASTEXITCODE -ne 0) { throw "不能创建 $format 单文件" }
    $destination = Join-Path $testRoot "$format-plain-output"
    Extract $path $destination
    $files = @(Get-ChildItem -LiteralPath $destination -File -Recurse)
    Check ($files.Count -eq 1 -and (Get-FileHash -LiteralPath $files[0].FullName).Hash -eq (Get-FileHash -LiteralPath (Join-Path $source '-option.txt')).Hash) "$format 单文件内容一致"
}
# 一个标准 Zstd 原始块帧；7za 只需提供解码能力，测试不依赖额外压缩库。
$raw = [Text.Encoding]::UTF8.GetBytes('Zstd 原始块测试')
$frame = [byte[]]@(0x28,0xb5,0x2f,0xfd,0x20,$raw.Length)
$blockHeader = [BitConverter]::GetBytes(($raw.Length -shl 3) -bor 1)
$frame += $blockHeader[0..2]
$frame += $raw
$path = Join-Path $testRoot 'plain.zst'
[IO.File]::WriteAllBytes($path, $frame)
$destination = Join-Path $testRoot 'zstd-output'
Extract $path $destination
Check ([IO.File]::ReadAllText((Get-ChildItem -LiteralPath $destination -File)[0].FullName) -eq 'Zstd 原始块测试') 'Zstd 内容一致'

Reject-Zip 'traversal' @(@{path='ok.txt';data='应保持未写'},@{path='../outside.txt';data='坏路径'})
Reject-Zip 'backslash' @(@{path='..\outside.txt';data='坏路径'})
Reject-Zip 'absolute' @(@{path='/absolute.txt';data='坏路径'})
Reject-Zip 'drive' @(@{path='C:\outside.txt';data='坏路径'})
Reject-Zip 'ads' @(@{path='evil.txt:ads';data='坏路径'})
Reject-Zip 'reserved' @(@{path='folder/CON.txt';data='坏路径'})
Reject-Zip 'trailing-dot' @(@{path='folder/unsafe.';data='坏路径'})
Reject-Zip 'case-conflict' @(@{path='Same.txt';data='A'},@{path='same.txt';data='B'})
Reject-Zip 'parent-file' @(@{path='parent';data='A'},@{path='parent/child';data='B'})
Reject-Zip 'symlink-zip' @(@{path='link';data='../outside';mode=-1577123840})
foreach ($linkType in @('SymbolicLink','HardLink')) {
    $path = Join-Path $testRoot "$linkType.tar"
    $stream = [IO.File]::Create($path)
    $writer = [System.Formats.Tar.TarWriter]::new($stream)
    try {
        $entry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::$linkType, 'link')
        $entry.LinkName = '../outside'
        $writer.WriteEntry($entry)
    } finally { $writer.Dispose(); $stream.Dispose() }
    $destination = Join-Path $testRoot "reject-$linkType"
    $result = Invoke-Cli -Arguments @('--extract-archive', $path, '--extract-output', $destination)
    Check ($result.code -ne 0 -and -not (Test-Path -LiteralPath $destination)) "TAR $linkType 预检拒绝"
}
$outside = Join-Path $testRoot 'junction-target'
[IO.Directory]::CreateDirectory($outside) | Out-Null
$junction = Join-Path $testRoot 'junction-output'
New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
$result = Invoke-Cli -Arguments @('--extract-archive', $archive, '--extract-output', $junction)
Check ($result.code -ne 0 -and @(Get-ChildItem -LiteralPath $outside).Count -eq 0) '已有目录重解析点拒绝写入'
Remove-Item -LiteralPath $junction

$encrypted = Join-Path $testRoot 'encrypted.7z'
& $seven a $encrypted (Join-Path $source '-option.txt') -psecret -y -bso0 -bsp0
$result = Invoke-Cli -Arguments @('--extract-archive', $encrypted, '--extract-output', (Join-Path $testRoot 'encrypted-output'))
Check ($result.code -ne 0 -and -not $result.stdout.Contains('EXTRACT_COMPLETE|')) '拒绝加密项且不等待密码输入'
$crcZip = Join-Path $testRoot 'crc.zip'
New-Zip $crcZip @(@{path='file.txt';data='CRC_TEST_PAYLOAD'})
$bytes = [IO.File]::ReadAllBytes($crcZip)
$dataOffset = 30 + [BitConverter]::ToUInt16($bytes,26) + [BitConverter]::ToUInt16($bytes,28)
$bytes[$dataOffset] = $bytes[$dataOffset] -bxor 1
[IO.File]::WriteAllBytes($crcZip, $bytes)
$result = Invoke-Cli -Arguments @('--extract-archive', $crcZip, '--extract-output', (Join-Path $testRoot 'crc-output'))
Check ($result.code -ne 0 -and -not $result.stdout.Contains('EXTRACT_COMPLETE|')) '数据 CRC 错误不报告完成'

$cancelArchive = Join-Path $testRoot 'cancel.zip'
New-Zip $cancelArchive @(0..4999 | ForEach-Object { @{path="folder/file$_.txt";data='x'} })
$marker = Join-Path $testRoot 'cancel.marker'
$start = [Diagnostics.ProcessStartInfo]::new($Tool)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['VIDEOENHANCER_CANCEL_FILE'] = $marker
foreach ($arg in @('--extract-archive',$cancelArchive,'--extract-output',(Join-Path $testRoot 'cancel-output'))) { $start.ArgumentList.Add($arg) }
$process = [Diagnostics.Process]::Start($start)
$stderrTask = $process.StandardError.ReadToEndAsync()
$first = $process.StandardOutput.ReadLine()
[IO.File]::WriteAllText($marker,'cancel')
$stdoutTask = $process.StandardOutput.ReadToEndAsync()
if (-not $process.WaitForExit(10000)) { $process.Kill($true); throw '取消归档操作超时' }
Check ($first.StartsWith('EXTRACT_START|') -and $process.ExitCode -ne 0 -and
    $stderrTask.GetAwaiter().GetResult().Contains('DOWNLOAD_CANCELLED|') -and
    -not $stdoutTask.GetAwaiter().GetResult().Contains('EXTRACT_COMPLETE|')) '运行中取消不报告安装成功'
$process.Dispose()
Write-Host "NATIVE_ARCHIVES_PASS|$script:passed|$testRoot"
