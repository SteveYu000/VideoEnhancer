param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$InputVideo,
    [Parameter(Mandatory)][string]$OutputVideo,
    [Parameter(Mandatory)][string]$Model,
    [Parameter(Mandatory)][string]$LogPrefix,
    [string]$EncoderArguments = '-c:v h264_nvenc -preset p1 -cq 30 -an',
    [int]$OutputScale = 0,
    [int]$StopAfterSeconds = 0
)
$ErrorActionPreference = 'Stop'
$arguments = @('-i', ('"' + $InputVideo + '"'), '-backend', 'tensorrt', '-modelpath', ('"' + $Model + '"'),
    '-upscale-precision', 'float16', '-tile-size', '0', '-ffmpeg-settings',
    ('"' + $EncoderArguments + ' -progress pipe:2 \"' + $OutputVideo + '\""'))
if ($OutputScale -gt 0) { $arguments += @('-output-scale', $OutputScale) }
$stopMapping = $null
$stopAccessor = $null
if ($StopAfterSeconds -gt 0) {
    $stopName = 'VideoEnhancerBenchmarkStop_' + [guid]::NewGuid().ToString('N')
    $stopMapping = [IO.MemoryMappedFiles.MemoryMappedFile]::CreateNew($stopName, 1)
    $stopAccessor = $stopMapping.CreateViewAccessor(0, 1)
    $stopAccessor.Write(0, [byte]0)
    $arguments += @('-stop-shm', $stopName)
}
$started = [DateTime]::UtcNow
$worker = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput ($LogPrefix + '.log') -RedirectStandardError ($LogPrefix + '.err')
$samples = [System.Collections.Generic.List[object]]::new()
$previousCpu = 0.0
$previousSeconds = 0.0
$renderStarted = $null
$stopRequested = $false
do {
    $seconds = ([DateTime]::UtcNow - $started).TotalSeconds
    $progress = Get-Content -LiteralPath ($LogPrefix + '.log') -Tail 10 | Select-String 'FPS: ([0-9.]+) Current Frame: (\d+)' | Select-Object -Last 1
    $frame = if ($progress) { [int]$progress.Matches[0].Groups[2].Value } else { 0 }
    $gpu = & nvidia-smi --query-gpu=temperature.gpu,clocks.gr,power.draw,utilization.gpu,memory.used,clocks_event_reasons.sw_thermal_slowdown,clocks_event_reasons.hw_thermal_slowdown,clocks_event_reasons.sw_power_cap --format=csv,noheader,nounits
    $values = $gpu.Split(',').Trim()
    $encoder = Get-CimInstance Win32_Process -Filter "Name='ffmpeg.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($OutputVideo) } | Select-Object -First 1
    $cpuPercent = 0.0
    $workingMiB = 0
    $encodedFrame = 0
    if ($encoder) {
        if ($null -eq $renderStarted) { $renderStarted = [DateTime]::UtcNow }
        $encoderProcess = Get-Process -Id $encoder.ProcessId -ErrorAction SilentlyContinue
        if ($encoderProcess) {
            $cpu = $encoderProcess.TotalProcessorTime.TotalSeconds
            $cpuPercent = [math]::Round(($cpu - $previousCpu) / [math]::Max(0.001, $seconds - $previousSeconds) / [Environment]::ProcessorCount * 100, 1)
            $previousCpu = $cpu
            $previousSeconds = $seconds
            $workingMiB = [math]::Round($encoderProcess.WorkingSet64 / 1MB)
        }
        $encoderLog = Join-Path (Split-Path $Executable) 'ffmpeg_log.txt'
        $encoded = Get-Content -LiteralPath $encoderLog -Tail 20 -ErrorAction SilentlyContinue | Select-String '^frame=(\d+)' | Select-Object -Last 1
        if ($encoded) { $encodedFrame = [int]$encoded.Matches[0].Groups[1].Value }
    }
    $renderSeconds = if ($null -ne $renderStarted) { ([DateTime]::UtcNow - $renderStarted).TotalSeconds } else { 0 }
    if ($StopAfterSeconds -gt 0 -and $renderSeconds -ge $StopAfterSeconds -and -not $stopRequested) {
        $stopAccessor.Write(0, [byte]1)
        $stopRequested = $true
        Write-Output ('STOP_REQUESTED|renderSeconds=' + [math]::Round($renderSeconds, 2))
    }
    $samples.Add([pscustomobject]@{ Seconds = [math]::Round($seconds, 2); RenderSeconds = [math]::Round($renderSeconds, 2); StopRequested = $stopRequested; Frame = $frame; EncodedFrame = $encodedFrame; EncoderCpuPercent = $cpuPercent; EncoderWorkingMiB = $workingMiB; Temperature = $values[0]; ClockMHz = $values[1]; PowerW = $values[2]; Utilization = $values[3]; MemoryMiB = $values[4]; SoftwareThermal = $values[5]; HardwareThermal = $values[6]; PowerCap = $values[7] })
    Write-Output ('PERF|' + $samples[-1].Seconds + '|frame=' + $frame + '|encoded=' + $encodedFrame + '|encoderCPU=' + $cpuPercent + '%|encoderRAM=' + $workingMiB + 'MiB|' + $gpu)
    if (-not $worker.HasExited) { Start-Sleep -Seconds 5 }
    $worker.Refresh()
} while (-not $worker.HasExited)
$samples | Export-Csv -LiteralPath ($LogPrefix + '.gpu.csv') -NoTypeInformation -Encoding utf8
$worker.WaitForExit()
if ($null -ne $stopAccessor) { $stopAccessor.Dispose(); $stopMapping.Dispose() }
Copy-Item -LiteralPath (Join-Path (Split-Path $Executable) 'ffmpeg_log.txt') -Destination ($LogPrefix + '.encoder.log') -ErrorAction SilentlyContinue
Get-Content -LiteralPath ($LogPrefix + '.log') -Tail 8
Get-Content -LiteralPath ($LogPrefix + '.err') -Tail 8
if ($worker.ExitCode -ne 0 -and -not ($stopRequested -and $worker.ExitCode -eq 130)) { throw ('处理失败，退出码：' + $worker.ExitCode) }
