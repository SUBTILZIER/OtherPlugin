using System.IO;
using System.Text.Json;

namespace AutomationStudioWpf.Services;

/// <summary>
/// Persistence boundary for the graph library. It owns file recovery and write blocking;
/// ViewModel mapping stays in GraphLibraryService until the next extraction step.
/// </summary>
internal sealed class GraphLibraryRepository
{
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _writesBlockedByLoadFailure;

    public GraphLibraryRepository(string path, JsonSerializerOptions jsonOptions)
    {
        LibraryPath = path ?? throw new ArgumentNullException(nameof(path));
        _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
    }

    public string LibraryPath { get; }

    public GraphLibraryState Load()
    {
        if (!File.Exists(LibraryPath) && !File.Exists(LibraryPath + ".bak"))
            return new GraphLibraryState();

        try
        {
            AtomicJsonReadResult<GraphLibraryState> readResult =
                AtomicJsonFileStore.Read<GraphLibraryState>(LibraryPath, _jsonOptions);
            _writesBlockedByLoadFailure = readResult.RepairError is not null;
            if (readResult.RecoveredFromBackup && readResult.PrimaryFileRepaired)
                Logging.Logger.Warn($"资产库已从备份恢复，并修复主文件：{LibraryPath}");
            else if (readResult.RepairError is not null)
                Logging.Logger.Error($"资产库已从备份读取，但主文件修复失败，本次运行禁止覆盖：{readResult.RepairError.Message}");
            return readResult.Value;
        }
        catch (Exception ex)
        {
            _writesBlockedByLoadFailure = true;
            Logging.Logger.Error($"资产库读取失败，已阻止覆盖原文件：{ex.Message}");
            return new GraphLibraryState();
        }
    }

    public void Save(GraphLibraryState state)
    {
        if (_writesBlockedByLoadFailure)
            throw new InvalidOperationException($"资产库主文件无法安全恢复。为避免覆盖原数据，本次运行已禁止保存：{LibraryPath}");

        AtomicJsonFileStore.Write(LibraryPath, state, _jsonOptions);
    }
}
