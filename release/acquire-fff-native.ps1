param([Parameter(Mandatory = $true)][string]$CacheDirectory)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$metadataRoot = Join-Path $PSScriptRoot '../cli/third-party/fff-native'
$metadata = Get-Content -LiteralPath (Join-Path $metadataRoot 'runtime.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$cache = [IO.Path]::GetFullPath($CacheDirectory)
[IO.Directory]::CreateDirectory($cache) | Out-Null

function Get-VerifiedFile([string]$RelativePath, [string]$Url, [string]$Hash) {
    $target = Join-Path $cache $RelativePath
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $Hash) {
            throw "FFF 构建缓存校验失败：$target"
        }
        return $target
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    $temporary = "$target.new-$([Guid]::NewGuid().ToString('N'))"
    try {
        Invoke-WebRequest -Uri $Url -OutFile $temporary -MaximumRetryCount 3 -RetryIntervalSec 2
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $Hash) {
            throw "FFF 构建下载校验失败：$Url"
        }
        Move-Item -LiteralPath $temporary -Destination $target
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
    return $target
}

# 仅读取已验证的官方发布容器，不启动播放器，也不打包其 LakeUI 或 FFmpeg 共享库。
# 容器格式依据 dotnet/runtime Microsoft.NET.HostModel/Bundle/Manifest.cs 和 FileEntry.cs。
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Security.Cryptography;
public static class FffBundleReader {
    public static void Extract(string executable, string destination, Dictionary<string, string> required) {
        byte[] bytes = File.ReadAllBytes(executable);
        byte[] signature = Convert.FromHexString("8B1202B96A612038727B930214D7A03213F5B9E6EFAE3318EE3B2DCE24B36AAE");
        int marker = bytes.AsSpan().IndexOf(signature);
        if (marker < 8) throw new InvalidDataException("FFF .NET 容器标识无效");
        long header = BitConverter.ToInt64(bytes, marker - 8);
        if (header < 0 || header >= bytes.Length) throw new InvalidDataException("FFF 容器偏移无效");
        using var reader = new BinaryReader(new MemoryStream(bytes));
        reader.BaseStream.Position = header;
        uint major = reader.ReadUInt32(), minor = reader.ReadUInt32();
        if (major != 6 || minor != 0) throw new InvalidDataException("FFF 容器版本不支持");
        int count = reader.ReadInt32();
        if (count < 1 || count > 10000) throw new InvalidDataException("FFF 容器条目数量无效");
        reader.ReadString();
        reader.BaseStream.Position += 40;
        var found = new HashSet<string>(StringComparer.Ordinal);
        Directory.CreateDirectory(destination);
        for (int i = 0; i < count; i++) {
            long offset = reader.ReadInt64(), size = reader.ReadInt64(), compressed = reader.ReadInt64();
            byte type = reader.ReadByte();
            string path = reader.ReadString();
            if (!required.TryGetValue(path, out string hash)) continue;
            if (type != 2 || !found.Add(path) || path != Path.GetFileName(path) ||
                offset < 0 || size < 1 || size > 16 * 1024 * 1024 || compressed < 0 ||
                (compressed > 0 ? compressed : size) > header - offset)
                throw new InvalidDataException("FFF 原生条目无效：" + path);
            using var content = new MemoryStream(bytes, (int)offset, (int)(compressed > 0 ? compressed : size));
            using var decoded = new MemoryStream();
            if (compressed > 0) { using var inflater = new DeflateStream(content, CompressionMode.Decompress); inflater.CopyTo(decoded); }
            else content.CopyTo(decoded);
            byte[] native = decoded.ToArray();
            if (native.Length != size || !Convert.ToHexString(SHA256.HashData(native)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("FFF DLL 校验失败：" + path);
            string target = Path.Combine(destination, path);
            if (File.Exists(target) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))).Equals(hash, StringComparison.OrdinalIgnoreCase)) continue;
            File.WriteAllBytes(target + ".new", native);
            File.Move(target + ".new", target, true);
        }
        if (found.Count != required.Count) throw new InvalidDataException("FFF 容器缺少必需 DLL");
    }
}
'@
$executable = Get-VerifiedFile 'FFF.Player.exe' $metadata.container.url $metadata.container.sha256
$required = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
foreach ($dll in $metadata.files) { $required.Add($dll.name, $dll.sha256) }
[FffBundleReader]::Extract($executable, (Join-Path $cache 'runtime'), $required)
# 禁止缓存中的额外 DLL 被通配符带入分发包。
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $cache 'runtime') -File) {
    if (-not $required.ContainsKey($file.Name)) { throw "FFF 运行缓存含未声明文件：$($file.Name)" }
}
foreach ($source in $metadata.sources) {
    Get-VerifiedFile "licenses/source/$($source.name)" $source.url $source.sha256 | Out-Null
}
foreach ($notice in $metadata.notices) {
    $path = Join-Path $metadataRoot $notice.name
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $notice.sha256) {
        throw "FFF 许可证校验失败：$path"
    }
    Copy-Item -LiteralPath $path -Destination (Join-Path $cache 'licenses') -Force
}
Copy-Item -LiteralPath (Join-Path $metadataRoot 'SOURCE.txt') -Destination (Join-Path $cache 'licenses') -Force
Copy-Item -LiteralPath (Join-Path $metadataRoot 'runtime.json') -Destination (Join-Path $cache 'licenses') -Force
Write-Host "FFF.Native 构建依赖哈希已校验（目标 API $($metadata.apiVersion)）：$cache"
