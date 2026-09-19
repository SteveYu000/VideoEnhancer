using System.Diagnostics;

namespace VideoEnhancer;

/// <summary>
/// VideoEnhancer 的便携目录约定。除用户明确选择的输入、输出外，运行期文件只写入 EXE 同目录。
/// </summary>
internal static class PortablePaths
{
    internal static string ApplicationRoot { get; } = AppContext.BaseDirectory.TrimEnd(
        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // 新安装固定使用 EXE 同目录；只读兼容旧版 videoenhancer.ini，避免升级后外置后端立即失效。
    internal static string CoreRoot { get; } = ResolveCoreRoot();

    internal static string CacheRoot => Path.Combine(ApplicationRoot, "cache");
    internal static string WorkRoot => Path.Combine(ApplicationRoot, ".work");
    internal static string UpdateRoot => Path.Combine(ApplicationRoot, ".update");

    internal static string EmbeddedToolsRoot(string version) =>
        Path.Combine(ApplicationRoot, "bin", "embedded-tools", version);

    private static string ResolveCoreRoot()
    {
        var iniPath = Path.Combine(ApplicationRoot, "videoenhancer.ini");
        if (!File.Exists(iniPath)) return ApplicationRoot;
        try
        {
            foreach (var rawLine in File.ReadLines(iniPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
                var separator = line.IndexOf('=');
                if (separator <= 0 || !line[..separator].Trim().Equals(
                        "core-path", StringComparison.OrdinalIgnoreCase)) continue;
                var value = line[(separator + 1)..].Trim().Trim('"');
                if (value.Length == 0) break;
                var resolved = Path.GetFullPath(Path.IsPathRooted(value)
                    ? value
                    : Path.Combine(ApplicationRoot, value));
                if (Directory.Exists(resolved))
                    return resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                break;
            }
        }
        catch
        {
            // 旧配置无效时回退到新的固定便携目录，由环境检查报告具体缺失项。
        }
        return ApplicationRoot;
    }

    internal static string CreateWorkDirectory(string purpose)
    {
        var safePurpose = string.Concat((purpose ?? "task").Where(
            character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
        if (safePurpose.Length == 0) safePurpose = "task";
        var path = Path.Combine(WorkRoot, safePurpose + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    internal static string CreateWorkFilePath(string purpose, string extension)
    {
        Directory.CreateDirectory(WorkRoot);
        var safeExtension = extension.StartsWith('.') ? extension : "." + extension;
        return Path.Combine(WorkRoot, purpose + "-" + Guid.NewGuid().ToString("N") + safeExtension);
    }

    /// <summary>把子进程可能产生的临时文件和常见计算缓存重定向到便携目录。</summary>
    internal static void ConfigureChildProcess(ProcessStartInfo startInfo)
    {
        if (startInfo.UseShellExecute) return;

        var temporaryRoot = Path.Combine(WorkRoot, "tmp");
        Directory.CreateDirectory(temporaryRoot);
        Directory.CreateDirectory(CacheRoot);

        startInfo.Environment["TEMP"] = temporaryRoot;
        startInfo.Environment["TMP"] = temporaryRoot;
        startInfo.Environment["VIDEOENHANCER_WORK_DIR"] = WorkRoot;
        startInfo.Environment["XDG_CACHE_HOME"] = CacheRoot;
        startInfo.Environment["HF_HOME"] = Path.Combine(CacheRoot, "huggingface");
        startInfo.Environment["TORCH_HOME"] = Path.Combine(CacheRoot, "torch");
        startInfo.Environment["PIP_CACHE_DIR"] = Path.Combine(CacheRoot, "pip");
        startInfo.Environment["UV_CACHE_DIR"] = Path.Combine(CacheRoot, "uv");
        startInfo.Environment["CUDA_CACHE_PATH"] = Path.Combine(CacheRoot, "cuda");
        startInfo.Environment["NUMBA_CACHE_DIR"] = Path.Combine(CacheRoot, "numba");
        startInfo.Environment["MPLCONFIGDIR"] = Path.Combine(CacheRoot, "matplotlib");
        startInfo.Environment["PYTHONPYCACHEPREFIX"] = Path.Combine(CacheRoot, "pycache");
    }
}
