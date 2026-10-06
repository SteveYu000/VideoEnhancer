param(
    [Parameter(Mandatory)][string]$InputVideo,
    [Parameter(Mandatory)][string]$Ffmpeg,
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$CaseName,
    [Parameter(Mandatory)][string]$Parameters,
    [int]$Frames = 240
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$prefix = Join-Path $OutputRoot $CaseName
$output = $prefix + '.mkv'
if (Test-Path -LiteralPath $output) { throw ('测试输出已存在，未覆盖：' + $output) }
$arguments = @('-hide_banner', '-nostdin', '-n', '-benchmark', '-i', ('"' + $InputVideo + '"'),
    '-map', '0:v:0', '-an', '-sn', '-dn', '-frames:v', $Frames, '-c:v', 'libsvtav1',
    '-preset', '6', '-crf', '12', '-pix_fmt', 'yuv444p10le', '-svtav1-params', $Parameters,
    '-progress', 'pipe:1', '-stats_period', '1', ('"' + $output + '"'))
$timer = [Diagnostics.Stopwatch]::StartNew()
$process = Start-Process -FilePath $Ffmpeg -ArgumentList $arguments -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput ($prefix + '.progress.log') -RedirectStandardError ($prefix + '.encoder.log')
$peakWorkingMiB = 0
do {
    $process.Refresh()
    if (-not $process.HasExited) { $peakWorkingMiB = [math]::Max($peakWorkingMiB, [math]::Round($process.WorkingSet64 / 1MB)) }
    $lines = Get-Content -LiteralPath ($prefix + '.progress.log') -Tail 15 -ErrorAction SilentlyContinue
    $frame = ($lines | Select-String '^frame=' | Select-Object -Last 1).Line
    $fps = ($lines | Select-String '^fps=' | Select-Object -Last 1).Line
    Write-Output ('SVT_SAMPLE|' + $CaseName + '|seconds=' + [math]::Round($timer.Elapsed.TotalSeconds, 2) + '|' + $frame + '|' + $fps + '|peakRAMMiB=' + $peakWorkingMiB)
    if (-not $process.HasExited) { Start-Sleep -Seconds 5 }
    $process.Refresh()
} while (-not $process.HasExited)
$process.WaitForExit()
$timer.Stop()
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath ($prefix + '.encoder.log') -Tail 18
    throw ('编码失败：' + $CaseName + '，退出码：' + $process.ExitCode)
}
$log = Get-Content -LiteralPath ($prefix + '.encoder.log') -Raw -Encoding utf8
$timeMatch = [regex]::Match($log, 'bench: utime=([\d.]+)s stime=([\d.]+)s rtime=([\d.]+)s')
if (-not $timeMatch.Success) { throw '缺少 FFmpeg benchmark 完整耗时' }
$seconds = [double]::Parse($timeMatch.Groups[3].Value, [Globalization.CultureInfo]::InvariantCulture)
$progress = Get-Content -LiteralPath ($prefix + '.progress.log') -Tail 15
$frameLine = ($progress | Select-String '^frame=(\d+)' | Select-Object -Last 1)
if (-not $frameLine -or [int]$frameLine.Matches[0].Groups[1].Value -ne $Frames -or -not ($progress -contains 'progress=end')) { throw '编码帧数不完整' }
$result = [pscustomobject]@{ Case = $CaseName; Frames = $Frames; Seconds = $seconds; Fps = [math]::Round($Frames / $seconds, 3); PeakWorkingMiB = $peakWorkingMiB; Parameters = $Parameters; Output = $output }
$result | Export-Csv -LiteralPath (Join-Path $OutputRoot 'results.csv') -Append -NoTypeInformation -Encoding utf8
Write-Output ('SVT_RESULT|' + ($result | ConvertTo-Json -Compress))
