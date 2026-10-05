param(
    [Parameter(Mandatory = $true)][string]$PythonExe,
    [string]$Executable = '',
    [string]$AssemblyPath = ''
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Executable) { $Executable = Join-Path $repo 'Artifacts/videoenhancer.exe' }
if (-not $AssemblyPath) { $AssemblyPath = Join-Path $repo 'cli/bin/Release/net10.0-windows/win-x64/videoenhancer.dll' }
$testRoot = Join-Path $repo ('Artifacts/download-integration-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory((Join-Path $testRoot 'bin/aria2-next')) | Out-Null
Copy-Item -LiteralPath $Executable -Destination (Join-Path $testRoot 'videoenhancer.exe')
Copy-Item -LiteralPath (Join-Path $repo 'cli/obj/third-party/aria2-next/2.8.3/aria2-next.exe') -Destination (Join-Path $testRoot 'bin/aria2-next/aria2-next.exe')
$fixture = [byte[]]::new(524288)
for ($i = 0; $i -lt $fixture.Length; $i++) { $fixture[$i] = $i % 256 }
$expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($fixture))
$trace = Join-Path $testRoot 'http-trace.jsonl'
$start = [Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($PythonExe))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($arg in @((Join-Path $PSScriptRoot 'tests/download-http-fixture.py'),'--trace',$trace)) { $start.ArgumentList.Add($arg) }
$server = [Diagnostics.Process]::Start($start)
$errors = $server.StandardError.ReadToEndAsync()
$script:checked = 0

function Assert-Test([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "下载检查失败：$Name" }
    $script:checked++
    Write-Host "PASS|$Name"
}
function Invoke-Cli([string[]]$Arguments) {
    $processStart = [Diagnostics.ProcessStartInfo]::new((Join-Path $testRoot 'videoenhancer.exe'))
    $processStart.UseShellExecute = $false
    $processStart.CreateNoWindow = $true
    $processStart.RedirectStandardOutput = $true
    $processStart.RedirectStandardError = $true
    # 公开下载分支的测试不借用运行环境中的真实令牌。
    foreach ($name in @('VIDEOENHANCER_MODELSCOPE_TOKEN','MODELSCOPE_API_TOKEN','VIDEOENHANCER_CANCEL_FILE')) { $processStart.Environment.Remove($name) | Out-Null }
    foreach ($arg in $Arguments) { $processStart.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($processStart)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try {
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw ('下载测试超时：' + $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult())
        }
        return @{ Code = $process.ExitCode; Text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult() }
    } finally { $process.Dispose() }
}
try {
    $portTask = $server.StandardOutput.ReadLineAsync()
    if (-not $portTask.Wait(10000)) { throw '本机测试服务启动超时' }
    $port = 0
    if (-not [int]::TryParse($portTask.Result, [ref]$port)) { throw '本机测试服务未返回端口' }
    $url = "http://127.0.0.1:$port"
    $public = Join-Path $testRoot 'public.bin'
    $result = Invoke-Cli @('--download-url',"$url/public",'--download-output',$public)
    Assert-Test ($result.Code -eq 0 -and (Get-FileHash -LiteralPath $public).Hash -eq $expected -and $result.Text.Contains('DOWNLOAD_COMPLETE|')) '真实 aria2 公共下载及内容哈希'

    $resumed = Join-Path $testRoot 'resume.bin'
    [IO.File]::WriteAllBytes($resumed, [byte[]]$fixture[0..131071])
    $result = Invoke-Cli @('--download-url',"$url/resume",'--download-output',$resumed)
    $requests = @(Get-Content -LiteralPath $trace -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Test ($result.Code -eq 0 -and (Get-FileHash -LiteralPath $resumed).Hash -eq $expected -and @($requests | Where-Object { $_.path -eq '/resume' -and $_.range -match '^bytes=131072-\d*$' }).Count -gt 0) '真实 aria2 断点续传发送正确 Range'

    $retried = Join-Path $testRoot 'retry.bin'
    $result = Invoke-Cli @('--download-url',"$url/retry",'--download-output',$retried)
    Assert-Test ($result.Code -ne 0 -and -not $result.Text.Contains('DOWNLOAD_COMPLETE|')) 'HTTP 失败不报告下载完成'
    $result = Invoke-Cli @('--download-url',"$url/retry",'--download-output',$retried)
    Assert-Test ($result.Code -eq 0 -and (Get-FileHash -LiteralPath $retried).Hash -eq $expected) '下载失败后再次调用成功'

    # 直接执行生产 ModelScope 客户端，用虚构令牌验证私有认证分支和临时文件行为。
    $assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($AssemblyPath))
    Add-Type -TypeDefinition @'
using System;
using System.Reflection;
public static class PrivateDownloadProbe {
    public static int Download(Assembly assembly, string coreRoot, string token, string url, string destination) {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var repositoryType = assembly.GetType("VideoEnhancer.ModelRepositoryClient", true);
        var repository = repositoryType.GetConstructors(flags)[0].Invoke(new object[] {"fixture/data", token, "1.3.12"});
        var managerType = assembly.GetType("VideoEnhancer.ModelDownloadManager", true);
        Func<string,string,bool,int> unused = (a,b,c) => throw new Exception("此场景不应调用 aria2 或解压");
        Func<string,int,int> fail = (message,code) => code;
        var manager = managerType.GetConstructors(flags)[0].Invoke(new object[] {coreRoot, token, "1.3.12", repository, unused, unused, fail});
        return (int)managerType.GetMethod("DownloadModelScopeFile", flags).Invoke(manager, new object[] {url, destination});
    }
}
'@
    $private = Join-Path $testRoot 'private.bin'
    $code = [PrivateDownloadProbe]::Download($assembly,$testRoot,'fixture-token',"$url/private",$private)
    Assert-Test ($code -eq 0 -and (Get-FileHash -LiteralPath $private).Hash -eq $expected -and -not [IO.File]::Exists("$private.part")) 'ModelScope 认证下载及原子替换（本机服务）'
    $denied = Join-Path $testRoot 'denied.bin'
    $code = [PrivateDownloadProbe]::Download($assembly,$testRoot,'invalid-fixture-token',"$url/private",$denied)
    Assert-Test ($code -ne 0 -and -not [IO.File]::Exists($denied) -and -not [IO.File]::Exists("$denied.part")) '无效认证被拒绝且无半成品'
    $redirected = Join-Path $testRoot 'redirected.bin'
    $code = [PrivateDownloadProbe]::Download($assembly,$testRoot,'fixture-token',"$url/redirect",$redirected)
    Assert-Test ($code -ne 0 -and -not [IO.File]::Exists($redirected)) '认证下载拒绝自动重定向'

    $result = Invoke-Cli @('--update-backend','--force-backend-full','--backend-channel',"$url/sha-channel.json")
    Assert-Test ($result.Code -ne 0 -and $result.Text.Contains('SHA256') -and -not $result.Text.Contains('BACKEND_UPDATE_COMPLETE|') -and -not [IO.Directory]::Exists((Join-Path $testRoot 'python'))) '真实后端下载哈希错误阻止解压安装'
    Write-Host "DOWNLOAD_INTEGRATION_PASS|$script:checked|$testRoot"
} finally {
    if (-not $server.HasExited) { $server.Kill($true); $server.WaitForExit() }
    $server.Dispose()
}
