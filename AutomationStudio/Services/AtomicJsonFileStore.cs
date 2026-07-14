using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace AutomationStudioWpf.Services;

internal static class AtomicJsonFileStore
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly ConcurrentDictionary<string, string> BlockedWritePaths = new(StringComparer.OrdinalIgnoreCase);

    public static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        string fullPath = Path.GetFullPath(path);
        if (BlockedWritePaths.TryGetValue(fullPath, out string? reason))
            throw new InvalidOperationException($"该 JSON 主文件恢复失败，为避免覆盖备份，本次运行禁止写入：{fullPath}。{reason}");

        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"保存路径缺少目录：{path}");
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        string backupPath = fullPath + ".bak";
        byte[] bytes = Utf8NoBom.GetBytes(JsonSerializer.Serialize(value, options));

        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
                File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, fullPath);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public static AtomicJsonReadResult<T> Read<T>(string path, JsonSerializerOptions options)
    {
        string fullPath = Path.GetFullPath(path);
        string backupPath = fullPath + ".bak";

        if (!File.Exists(fullPath))
        {
            if (!File.Exists(backupPath))
                throw new FileNotFoundException("JSON 文件不存在。", fullPath);

            try
            {
                T recovered = Deserialize<T>(backupPath, options);
                return RepairPrimary(fullPath, recovered, options);
            }
            catch (Exception backupError) when (IsReadFailure(backupError))
            {
                BlockWrites(fullPath, backupError);
                throw new InvalidDataException($"JSON 主文件缺失且备份无法读取：{fullPath}", backupError);
            }
        }

        try
        {
            T value = Deserialize<T>(fullPath, options);
            BlockedWritePaths.TryRemove(fullPath, out _);
            return new AtomicJsonReadResult<T>(value, false, false, null);
        }
        catch (Exception primaryError) when (IsReadFailure(primaryError))
        {
            if (!File.Exists(backupPath))
            {
                BlockWrites(fullPath, primaryError);
                throw new InvalidDataException($"JSON 文件损坏且没有可用备份：{fullPath}", primaryError);
            }

            try
            {
                T recovered = Deserialize<T>(backupPath, options);
                return RepairPrimary(fullPath, recovered, options);
            }
            catch (Exception backupError) when (IsReadFailure(backupError))
            {
                BlockWrites(fullPath, backupError);
                throw new InvalidDataException($"JSON 主文件和备份均无法读取：{fullPath}", new AggregateException(primaryError, backupError));
            }
        }
    }

    private static AtomicJsonReadResult<T> RepairPrimary<T>(
        string fullPath,
        T recovered,
        JsonSerializerOptions options)
    {
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"恢复路径缺少目录：{fullPath}");
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.repair.tmp");

        try
        {
            byte[] bytes = Utf8NoBom.GetBytes(JsonSerializer.Serialize(recovered, options));
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
                File.Replace(tempPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, fullPath);

            BlockedWritePaths.TryRemove(fullPath, out _);
            return new AtomicJsonReadResult<T>(recovered, true, true, null);
        }
        catch (Exception repairError)
        {
            BlockWrites(fullPath, repairError);
            return new AtomicJsonReadResult<T>(recovered, true, false, repairError);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static bool IsReadFailure(Exception error) =>
        error is JsonException or IOException or UnauthorizedAccessException;

    private static void BlockWrites(string fullPath, Exception error) =>
        BlockedWritePaths[fullPath] = $"恢复错误：{error.Message}";

    private static T Deserialize<T>(string path, JsonSerializerOptions options)
    {
        string json = File.ReadAllText(path, Utf8NoBom);
        return JsonSerializer.Deserialize<T>(json, options)
            ?? throw new JsonException($"JSON 内容为空：{path}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}

internal sealed record AtomicJsonReadResult<T>(
    T Value,
    bool RecoveredFromBackup,
    bool PrimaryFileRepaired,
    Exception? RepairError);
