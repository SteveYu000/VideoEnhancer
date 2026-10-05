using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace VideoEnhancer;

/// <summary>所有归档操作使用独立 7za；写盘前检查路径、链接和已有重解析点。</summary>
internal static class NativeSevenZipExtractor
{
    internal static string Executable => Path.Combine(PortablePaths.CoreRoot, "bin", "7zip", "7za.exe");

    internal static bool EnsureAvailable()
    {
        // 单独更新运行 EXE 的用户也能取得解压组件；对应源码独立发布。
        var assembly = typeof(NativeSevenZipExtractor).Assembly;
        foreach (var (name, relative) in new[]
        {
            ("7za.exe", "bin/7zip/7za.exe"), ("License.txt", "licenses/7zip/License.txt"),
            ("SOURCE.txt", "licenses/7zip/SOURCE.txt")
        })
        {
            var target = Path.Combine(PortablePaths.CoreRoot, relative);
            if (File.Exists(target)) continue;
            using var input = assembly.GetManifestResourceStream("VideoEnhancer.Embedded.SevenZip." + name);
            if (input is null) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temporary = target + ".new-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var output = File.Create(temporary)) input.CopyTo(output);
                try { File.Move(temporary, target); }
                catch (IOException) when (File.Exists(target)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return File.Exists(Executable);
    }

    internal static void Extract(string archive, string output, Action<int>? progress = null)
    {
        archive = Path.GetFullPath(archive);
        output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        if (!File.Exists(archive)) throw new FileNotFoundException("压缩文件不存在", archive);
        RequireAvailable();
        var listing = ReadListing(archive);
        ValidateEntries(listing.Entries, output);

        // 7za 对压缩 TAR 先释放外层数据流，再处理 TAR；两层都必须完成预检。
        if (listing.Type is "gzip" or "bzip2" or "xz" or "zstd" &&
            listing.Entries.Count == 1 &&
            listing.Entries[0]["Path"].EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
        {
            var temporary = Path.Combine(PortablePaths.CoreRoot, ".work", "archives", Guid.NewGuid().ToString("N"));
            ValidateExistingPath(temporary, directory: true);
            try
            {
                Directory.CreateDirectory(temporary);
                ExtractChecked(archive, temporary, null);
                var inner = ResolveEntry(temporary, listing.Entries[0]["Path"]);
                var tar = ReadListing(inner);
                if (tar.Type != "tar") throw new InvalidDataException("压缩 TAR 的内部格式无效");
                ValidateEntries(tar.Entries, output);
                Directory.CreateDirectory(output);
                ExtractChecked(inner, output, progress);
            }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
            return;
        }

        Directory.CreateDirectory(output);
        ExtractChecked(archive, output, progress);
    }

    internal static void CreateSevenZip(string sourceDirectory, string archivePath)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory));
        var output = Path.GetFullPath(archivePath);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("归档源目录不存在：" + source);
        if (!Path.GetExtension(output).Equals(".7z", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("归档输出必须使用 .7z 扩展名", nameof(archivePath));
        if (output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("归档输出不能位于源目录内部");
        ValidateExistingPath(source, directory: true);
        ValidateExistingPath(output, directory: false);
        var directories = new Stack<string>();
        directories.Push(source);
        var files = 0;
        while (directories.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                DownloadCancellation.Check();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("归档源包含重解析点：" + entry);
                ResolveEntry(source, Path.GetRelativePath(source, entry));
                if ((attributes & FileAttributes.Directory) != 0) directories.Push(entry);
                else files++;
            }
        }
        if (files == 0) throw new InvalidDataException("归档源目录不包含文件");
        RequireAvailable();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary = output + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            Run(new[] { "a", "-t7z", "-mx=5", "-mmt=on", "-y", "-sccUTF-8", "-bsp0", "-bso0", "-bse2", "--", temporary, ".\\*" },
                source, null);
            File.Move(temporary, output, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RequireAvailable()
    {
        if (!EnsureAvailable()) throw new FileNotFoundException("缺少独立解压组件 7za，请重新安装完整发行包", Executable);
    }

    private sealed record Listing(string Type, List<Dictionary<string, string>> Entries);

    private static Listing ReadListing(string archive)
    {
        var entries = new List<Dictionary<string, string>>();
        var record = new Dictionary<string, string>(StringComparer.Ordinal);
        var inEntries = false;
        var type = "";
        void FinishRecord()
        {
            if (record.Count == 0) return;
            if (!record.ContainsKey("Path"))
            {
                // 匿名单文件压缩流不储存文件名；采用 7za 的去后缀命名规则。
                if (entries.Count != 0 || type is not ("gzip" or "bzip2" or "xz" or "zstd"))
                    throw new InvalidDataException("7za 返回了无路径条目");
                var name = Path.GetFileNameWithoutExtension(archive);
                if (Path.GetExtension(archive).ToLowerInvariant() is ".tgz" or ".tbz" or ".tbz2" or ".txz" or ".tzst")
                    name += ".tar";
                record.Add("Path", name);
            }
            entries.Add(record);
            record = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        Run(new[] { "l", "-slt", "-sccUTF-8", "-bsp0", "-bse2", "-bd", "-p-", "--", archive }, null, line =>
        {
            if (line == "----------") { inEntries = true; return; }
            if (!inEntries)
            {
                if (line.StartsWith("Type = ", StringComparison.Ordinal)) type = line[7..];
                return;
            }
            if (line.Length == 0) { FinishRecord(); return; }
            var separator = line.IndexOf(" = ", StringComparison.Ordinal);
            if (separator <= 0 || !record.TryAdd(line[..separator], line[(separator + 3)..]))
                throw new InvalidDataException("7za 归档清单存在歧义，已拒绝解压");
        });
        FinishRecord();
        if (type.Length == 0) throw new InvalidDataException("无法识别压缩包格式");
        return new Listing(type, entries);
    }

    private static void ValidateEntries(List<Dictionary<string, string>> entries, string output)
    {
        ValidateExistingPath(output, directory: true);
        var destinations = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var checkedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            DownloadCancellation.Check();
            var path = entry["Path"];
            var attributes = entry.GetValueOrDefault("Attributes", "");
            var mode = entry.GetValueOrDefault("Mode", "");
            if (entry.GetValueOrDefault("Encrypted") == "+")
                throw new InvalidDataException("不支持加密压缩项：" + path);
            if (entry.GetValueOrDefault("Anti") == "+" ||
                !string.IsNullOrEmpty(entry.GetValueOrDefault("Symbolic Link")) ||
                !string.IsNullOrEmpty(entry.GetValueOrDefault("Hard Link")) ||
                attributes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(value => value.StartsWith('l')) ||
                (mode.Length > 0 && mode[0] is not ('-' or 'd')) || attributes.Contains('L') ||
                (entry.TryGetValue("Type", out var type) && type is not ("File" or "Directory" or "0" or "5")))
                throw new InvalidDataException("不解压链接或特殊压缩项：" + path);
            var directory = entry.GetValueOrDefault("Folder") == "+" || attributes.StartsWith('D') ||
                entry.GetValueOrDefault("Type") is "Directory" or "5";
            if (directory && path.Replace('\\', '/').TrimEnd('/') == ".") continue;
            var destination = ResolveEntry(output, path);
            if (!destinations.TryAdd(destination, directory))
                throw new InvalidDataException("压缩包包含重复或大小写冲突路径：" + path);
            ValidateExistingPath(destination, directory, checkedDirectories);
        }
        foreach (var destination in destinations.Keys)
        {
            var parent = Path.GetDirectoryName(destination);
            while (parent is not null && !parent.Equals(output, StringComparison.OrdinalIgnoreCase))
            {
                if (destinations.TryGetValue(parent, out var directory) && !directory)
                    throw new InvalidDataException("压缩包中文件与目录路径冲突：" + parent);
                parent = Path.GetDirectoryName(parent);
            }
        }
    }

    private static string ResolveEntry(string root, string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (normalized.StartsWith('/') || Path.IsPathRooted(normalized) || parts.Length == 0 ||
            parts.Any(part => part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                Regex.IsMatch(part.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("压缩项不是安全的相对路径：" + path);
        var destination = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("压缩项越出目标目录：" + path);
        return destination;
    }

    private static void ValidateExistingPath(string path, bool directory, HashSet<string>? checkedDirectories = null)
    {
        var current = path;
        var isLeaf = true;
        while (current is not null)
        {
            if (!isLeaf && checkedDirectories?.Contains(current) == true) break;
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("目标包含重解析点，已拒绝写入：" + current);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                if (isDirectory != (!isLeaf || directory))
                    throw new IOException("目标文件与目录类型冲突：" + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            if (!isLeaf || directory) checkedDirectories?.Add(current);
            isLeaf = false;
            current = Path.GetDirectoryName(current);
        }
    }

    private static void ExtractChecked(string archive, string output, Action<int>? progress)
    {
        var lastPercent = -1;
        Run(new[] { "x", "-o" + output, "-y", "-aoa", "-mmt=on", "-sccUTF-8", "-bsp1", "-bso1", "-bse2", "-bb0", "-p-", "--", archive },
            null, line =>
            {
                var match = Regex.Match(line, @"(?:^|\s)(\d{1,3})%");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var percent) &&
                    percent <= 100 && percent != lastPercent)
                {
                    lastPercent = percent;
                    progress?.Invoke(percent);
                }
            });
        if (lastPercent != 100) progress?.Invoke(100);
    }

    private static void Run(string[] arguments, string? workingDirectory, Action<string>? output)
    {
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.RedirectStandardInput = true;
        if (workingDirectory is not null) start.WorkingDirectory = workingDirectory;
        PortablePaths.ConfigureChildProcess(start);
        foreach (var arg in arguments)
            start.ArgumentList.Add(arg);
        DownloadCancellation.Check();
        using var process = Process.Start(start) ?? throw new IOException("无法启动 7za");
        process.StandardInput.Close();
        using var cancellation = DownloadCancellation.Token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var error = process.StandardError.ReadToEndAsync();
        var line = new StringBuilder();
        var diagnostics = new Queue<string>();
        var buffer = new char[4096];
        int count;
        void EmitLine()
        {
            var value = line.ToString();
            line.Clear();
            output?.Invoke(value);
            if (value.Length > 0) diagnostics.Enqueue(value.Length <= 512 ? value : value[..512]);
            if (diagnostics.Count > 20) diagnostics.Dequeue();
        }
        try
        {
            var previousWasCr = false;
            while ((count = process.StandardOutput.Read(buffer, 0, buffer.Length)) > 0)
                for (var i = 0; i < count; i++)
                {
                    var character = buffer[i];
                    if (character == '\b') continue;
                    if (character == '\n' && previousWasCr) { previousWasCr = false; continue; }
                    previousWasCr = character == '\r';
                    if (character is '\r' or '\n') EmitLine();
                    else
                    {
                        line.Append(character);
                        if (line.Length > 65536) throw new InvalidDataException("7za 清单行超出安全限制");
                    }
                }
            if (line.Length > 0) EmitLine();
            process.WaitForExit();
            var stderr = error.GetAwaiter().GetResult();
            DownloadCancellation.Check();
            if (process.ExitCode != 0)
                throw new IOException($"7za 操作失败（退出码 {process.ExitCode}）：{stderr}\n{string.Join('\n', diagnostics)}");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
