using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace VideoEnhancer;

/// <summary>管理 RTXHDR-RTXVSR 本地 sidecar，并按其稳定 HTTP 协议执行任务。</summary>
internal sealed class RtxVideoBackendClient : IDisposable
{
    internal sealed record Capabilities(
        bool D3d11Available,
        bool RtxSdkFound,
        bool VsrAvailable,
        bool TruehdrAvailable,
        bool NvencH264Available,
        bool NvencHevcMain10Available,
        bool NvencAv1Available,
        string[] Messages);

    internal sealed record JobResult(bool Succeeded, bool Canceled, string Error);

    private readonly Process _process;
    private readonly HttpClient _http;
    private readonly string _sessionId;
    private readonly StringBuilder _diagnostics = new();

    private RtxVideoBackendClient(Process process, HttpClient http, string sessionId)
    {
        _process = process;
        _http = http;
        _sessionId = sessionId;
    }

    internal static string FindBackend(string coreRoot)
    {
        var candidates = new[]
        {
            Path.Combine(coreRoot, "bin", "rtx-video", "vsr_backend.exe"),
            Path.Combine(coreRoot, "bin", "rtx-video", "runtime", "vsr_backend.exe"),
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    internal static async Task<RtxVideoBackendClient> StartAsync(string backendPath, CancellationToken token)
    {
        if (!File.Exists(backendPath))
            throw new FileNotFoundException("未安装 RTX Video 运行组件", backendPath);

        var port = ReservePort();
        var sessionId = Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo
        {
            FileName = backendPath,
            WorkingDirectory = Path.GetDirectoryName(backendPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--app-session-id");
        start.ArgumentList.Add(sessionId);

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException("无法启动 RTX Video sidecar");
        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Add("X-App-Session-Id", sessionId);
        var client = new RtxVideoBackendClient(process, http, sessionId);
        process.OutputDataReceived += (_, e) => client.AppendDiagnostic(e.Data);
        process.ErrorDataReceived += (_, e) => client.AppendDiagnostic(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                if (process.HasExited)
                    throw new InvalidOperationException("RTX Video sidecar 启动后立即退出：" + client.LastDiagnostic());
                try
                {
                    using var response = await http.GetAsync("/api/health", token);
                    if (response.IsSuccessStatusCode) return client;
                }
                catch (HttpRequestException)
                {
                    // sidecar 尚未开始监听，继续等待。
                }
                catch (TaskCanceledException) when (!token.IsCancellationRequested)
                {
                    // 单次健康检查超时，继续使用总启动期限。
                }
                await Task.Delay(150, token);
            }
            throw new TimeoutException("RTX Video sidecar 在 20 秒内未就绪：" + client.LastDiagnostic());
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    internal async Task<Capabilities> GetCapabilitiesAsync(CancellationToken token)
    {
        using var response = await _http.GetAsync("/api/capabilities", token);
        var text = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ApiError(text, response.StatusCode));
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        return new Capabilities(
            ReadBoolean(root, "d3d11Available"),
            ReadBoolean(root, "rtxSdkFound"),
            ReadBoolean(root, "vsrAvailable"),
            ReadBoolean(root, "truehdrAvailable"),
            ReadBoolean(root, "nvencH264Available"),
            ReadBoolean(root, "nvencHevcMain10Available"),
            ReadBoolean(root, "nvencAv1Available"),
            root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array
                ? messages.EnumerateArray().Select(item => item.GetString() ?? "").Where(value => value.Length > 0).ToArray()
                : Array.Empty<string>());
    }

    internal async Task<JobResult> RunAsync(
        string inputPath,
        string outputPath,
        bool vsrEnabled,
        int quality,
        double scale,
        bool hdrEnabled,
        string codec,
        Func<bool> stopRequested,
        CancellationToken token)
    {
        using var createContent = JsonContent(CreateJobJson(inputPath, outputPath, vsrEnabled, quality, scale, hdrEnabled, codec));
        using var create = await _http.PostAsync("/api/jobs", createContent, token);
        var createText = await create.Content.ReadAsStringAsync(token);
        if (!create.IsSuccessStatusCode) return new JobResult(false, false, ApiError(createText, create.StatusCode));
        using var createDocument = JsonDocument.Parse(createText);
        var id = createDocument.RootElement.GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(id)) return new JobResult(false, false, "RTX Video sidecar 未返回任务 ID");

        var cancelSent = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (stopRequested() && !cancelSent)
            {
                cancelSent = true;
                try { using var _ = await _http.PostAsync($"/api/jobs/{Uri.EscapeDataString(id)}/cancel", JsonContent("{}"), token); }
                catch { }
            }

            using var response = await _http.GetAsync($"/api/jobs/{Uri.EscapeDataString(id)}", token);
            var text = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode) return new JobResult(false, cancelSent, ApiError(text, response.StatusCode));
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var state = root.TryGetProperty("state", out var stateElement) ? stateElement.GetString() ?? "" : "";
            var progress = root.TryGetProperty("progress", out var progressElement) && progressElement.TryGetDouble(out var parsedProgress)
                ? Math.Clamp(parsedProgress, 0.0, 1.0)
                : 0.0;
            var framesDone = root.TryGetProperty("framesDone", out var framesElement) && framesElement.TryGetInt64(out var parsedFrames)
                ? parsedFrames
                : 0;
            var fps = root.TryGetProperty("fps", out var fpsElement) && fpsElement.TryGetDouble(out var parsedFps)
                ? parsedFps
                : 0.0;
            var eta = root.TryGetProperty("etaSeconds", out var etaElement) && etaElement.TryGetInt64(out var parsedEta)
                ? parsedEta
                : 0;
            Console.WriteLine($"RTX_PROGRESS|{progress.ToString("0.000", CultureInfo.InvariantCulture)}|{framesDone}|{fps.ToString("0.0", CultureInfo.InvariantCulture)}|{eta}");

            if (state == "succeeded") return new JobResult(true, false, "");
            if (state is "failed" or "canceled")
            {
                var error = "";
                if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
                {
                    var message = errorElement.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? "" : "";
                    var details = errorElement.TryGetProperty("details", out var detailsElement) ? detailsElement.GetString() ?? "" : "";
                    error = string.Join("：", new[] { message, details }.Where(value => !string.IsNullOrWhiteSpace(value)));
                }
                if (string.IsNullOrWhiteSpace(error)) error = LastDiagnostic();
                return new JobResult(false, state == "canceled" || cancelSent, error);
            }
            await Task.Delay(500, token);
        }
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string CreateJobJson(string inputPath, string outputPath, bool vsrEnabled,
        int quality, double scale, bool hdrEnabled, string codec)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("inputPath", inputPath);
            writer.WriteString("outputPath", outputPath);
            writer.WritePropertyName("processing");
            writer.WriteStartObject();
            writer.WritePropertyName("vsr");
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", vsrEnabled);
            writer.WriteNumber("quality", quality);
            writer.WriteNumber("scale", scale);
            writer.WriteEndObject();
            writer.WritePropertyName("hdr");
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", hdrEnabled);
            writer.WriteNumber("contrast", 100);
            writer.WriteNumber("saturation", 100);
            writer.WriteNumber("middleGray", 44);
            writer.WriteNumber("maxLuminance", 1000);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WritePropertyName("output");
            writer.WriteStartObject();
            writer.WriteString("container", "mp4");
            writer.WriteString("videoCodec", codec);
            writer.WriteString("audioMode", "copy");
            writer.WriteString("subtitleMode", "copy-compatible");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    private static bool ReadBoolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string ApiError(string text, System.Net.HttpStatusCode status)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var error = document.RootElement.GetProperty("error");
            var message = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? "" : "";
            var details = error.TryGetProperty("details", out var detailsElement) ? detailsElement.GetString() ?? "" : "";
            var combined = string.Join("：", new[] { message, details }.Where(value => !string.IsNullOrWhiteSpace(value)));
            if (combined.Length > 0) return combined;
        }
        catch (JsonException)
        {
        }
        return $"RTX Video sidecar 返回 HTTP {(int)status}：{text}";
    }

    private void AppendDiagnostic(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_diagnostics)
        {
            _diagnostics.AppendLine(line);
            if (_diagnostics.Length > 32_768) _diagnostics.Remove(0, _diagnostics.Length - 16_384);
        }
    }

    private string LastDiagnostic()
    {
        lock (_diagnostics)
        {
            return _diagnostics.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
                ?? "无诊断信息";
        }
    }

    public void Dispose()
    {
        try { using var _ = _http.PostAsync("/api/app/shutdown", JsonContent("{}")).GetAwaiter().GetResult(); }
        catch { }
        _http.Dispose();
        try
        {
            if (!_process.HasExited && !_process.WaitForExit(3000)) _process.Kill(entireProcessTree: true);
        }
        catch { }
        _process.Dispose();
    }
}
