using System.Text;
using System.Text.Json;

namespace VideoEnhancer;

/// <summary>迁移并清理旧版本写入非便携目录的、名称可明确识别的文件。</summary>
internal static class LegacyResidueCleaner
{
    private const string LegacyConfigFileName = "videoenhancer.plugin.json";
    private const string LegacyIniFileName = "videoenhancer.ini";
    private static readonly string[] LegacyNativePayloadFiles =
    {
        "FFF.Native.dll", "ass-9.dll", "brotlicommon.dll", "brotlidec.dll", "bz2.dll",
        "freetype.dll", "fribidi-0.dll", "harfbuzz.dll", "libpng16.dll", "z.dll"
    };
    private static readonly string[] LegacyEmbeddedToolFiles =
    {
        "aria2-next.exe", "7za.exe", "rve-ordered-backend.py",
        "inspect_interpolation_models.py", "inspect_upscale_models.py",
        "prepare_rife_tensorrt.py", "rve-image-backend.py", "rve-segmented-backend.py"
    };

    /// <summary>
    /// 先把旧插件配置复制到便携目录，并移除已废弃的 ExePath 字段。
    /// 返回值非空时表示迁移失败，后续清理必须保留该旧配置。
    /// </summary>
    internal static string? MigratePluginConfiguration(string applicationRoot)
    {
        var legacyConfig = LegacyConfigPath();
        if (legacyConfig is null || !File.Exists(legacyConfig)) return null;

        var portableConfig = Path.Combine(Path.GetFullPath(applicationRoot), LegacyConfigFileName);
        if (File.Exists(portableConfig))
        {
            Console.WriteLine("已存在便携插件配置，将保留便携版本。");
            return null;
        }

        var temporary = portableConfig + ".migrating";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(legacyConfig, Encoding.UTF8));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("旧插件配置的根节点不是 JSON 对象");

            Directory.CreateDirectory(Path.GetDirectoryName(portableConfig)!);
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Name.Equals("ExePath", StringComparison.OrdinalIgnoreCase)) continue;
                    property.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            File.Move(temporary, portableConfig, true);
            Console.WriteLine("已将旧插件设置迁移到：" + portableConfig);
            return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("旧插件配置迁移失败，将保留原文件：" + ex.Message);
            return legacyConfig;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    internal static void Clean(string pluginRoot, string? protectedLegacyConfig)
    {
        var root = Path.GetFullPath(pluginRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var appRoot = ApplicationLayoutManager.ApplicationRoot(root);
        var deleted = 0;
        var failures = new List<string>();

        var legacyConfig = LegacyConfigPath();
        if (legacyConfig is not null && !PathsEqual(legacyConfig, protectedLegacyConfig))
            DeleteKnownFile(legacyConfig, ref deleted, failures);
        DeleteKnownFile(Path.Combine(root, LegacyIniFileName), ref deleted, failures);
        DeleteKnownFile(Path.Combine(appRoot, LegacyIniFileName), ref deleted, failures);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var oldUpdaterRoot = Path.Combine(localAppData, "FFmpegFreeUI", "VideoEnhancer");
            DeleteKnownFile(Path.Combine(oldUpdaterRoot, "update-result.txt"), ref deleted, failures);
            DeleteFilesInChildDirectories(
                Path.Combine(oldUpdaterRoot, "updater"), new[] { "videoenhancer-updater.exe" },
                ref deleted, failures);
            DeleteMatchingFilesInChildDirectories(
                Path.Combine(oldUpdaterRoot, "updates"), ref deleted, failures);

            var oldCacheRoot = Path.Combine(localAppData, "VideoEnhancer");
            DeleteKnownFile(Path.Combine(oldCacheRoot, "cache", "interpolation-capabilities-v1.json"),
                ref deleted, failures);
            DeleteFilesInChildDirectories(
                Path.Combine(oldCacheRoot, "tools"), LegacyEmbeddedToolFiles,
                ref deleted, failures);
            RemoveEmptyTree(oldUpdaterRoot);
            RemoveEmptyTree(oldCacheRoot);
        }

        var oldNativeRoot = Path.Combine(Path.GetTempPath(), "videoenhancer.3fui", "fff-native-11");
        foreach (var fileName in LegacyNativePayloadFiles)
            DeleteKnownFile(Path.Combine(oldNativeRoot, fileName), ref deleted, failures);
        RemoveEmptyTree(Path.GetDirectoryName(oldNativeRoot)!);

        Console.WriteLine($"旧配置残留清理完成：删除 {deleted} 个已知文件。");
        foreach (var failure in failures)
            Console.Error.WriteLine("[清理失败] " + failure);
    }

    /// <summary>升级后移除旧版本从主程序资源释放的 aria2-next/7za 副本；后端脚本继续保留。</summary>
    internal static void CleanObsoleteEmbeddedTools(string applicationRoot)
    {
        var embeddedToolsRoot = Path.Combine(Path.GetFullPath(applicationRoot), "bin", "embedded-tools");
        var deleted = 0;
        var failures = new List<string>();
        DeleteFilesInChildDirectories(
            embeddedToolsRoot,
            new[] { "aria2-next.exe", "7za.exe" },
            ref deleted,
            failures);
        RemoveEmptyTree(embeddedToolsRoot);
        if (deleted > 0)
            Console.WriteLine($"已移除 {deleted} 个旧版内嵌工具副本。");
        foreach (var failure in failures)
            Console.Error.WriteLine("[清理失败] " + failure);
    }

    private static string? LegacyConfigPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData)
            ? null
            : Path.Combine(localAppData, "FFmpegFreeUI", LegacyConfigFileName);
    }

    private static void DeleteFilesInChildDirectories(
        string parent, IEnumerable<string> fileNames, ref int deleted, ICollection<string> failures)
    {
        if (!Directory.Exists(parent) || IsReparsePoint(parent)) return;
        try
        {
            foreach (var child in Directory.EnumerateDirectories(parent, "*", SearchOption.TopDirectoryOnly))
            {
                if (IsReparsePoint(child)) continue;
                foreach (var fileName in fileNames)
                    DeleteKnownFile(Path.Combine(child, fileName), ref deleted, failures);
            }
        }
        catch (Exception ex)
        {
            failures.Add(parent + "：" + ex.Message);
        }
    }

    private static void DeleteMatchingFilesInChildDirectories(
        string parent, ref int deleted, ICollection<string> failures)
    {
        if (!Directory.Exists(parent) || IsReparsePoint(parent)) return;
        try
        {
            foreach (var child in Directory.EnumerateDirectories(parent, "*", SearchOption.TopDirectoryOnly))
            {
                if (IsReparsePoint(child)) continue;
                foreach (var file in Directory.EnumerateFiles(child, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    if ((name.StartsWith("VideoEnhancer-", StringComparison.OrdinalIgnoreCase)
                         && name.EndsWith("-win-x64.exe", StringComparison.OrdinalIgnoreCase))
                        || name.EndsWith(".download", StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteKnownFile(file, ref deleted, failures);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add(parent + "：" + ex.Message);
        }
    }

    private static void DeleteKnownFile(
        string path, ref int deleted, ICollection<string> failures)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath) || IsReparsePoint(fullPath)) return;
            File.Delete(fullPath);
            deleted++;
            Console.WriteLine("已删除旧文件：" + fullPath);
        }
        catch (Exception ex)
        {
            failures.Add(path + "：" + ex.Message);
        }
    }

    /// <summary>只在给定根目录内部自底向上移除空目录，不触碰它的父目录。</summary>
    private static void RemoveEmptyTree(string start)
    {
        var root = Path.GetFullPath(start);
        if (!Directory.Exists(root) || IsReparsePoint(root)) return;
        try
        {
            foreach (var child in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                RemoveEmptyTree(child);
            }
            if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
        }
        catch
        {
            // 非空、被占用或无权限时保留，不影响安装。
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
