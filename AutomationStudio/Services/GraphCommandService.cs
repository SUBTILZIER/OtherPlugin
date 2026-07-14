using System.Text.Json;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;

namespace AutomationStudioWpf.Services;

public sealed class GraphCommandService
{
    private const int MaxHistoryEntries = 100;
    private const long MaxHistoryBytes = 64L * 1024 * 1024;
    private readonly GraphEditorService _editorService;
    private readonly Func<GraphAssetKind> _getActiveGraphKind;
    private readonly Action _afterRestore;
    private readonly Action<string> _setStatus;
    private readonly JsonSerializerOptions _jsonOptions = new();
    private readonly LinkedList<GraphCommandSnapshot> _undoHistory = [];
    private readonly LinkedList<GraphCommandSnapshot> _redoHistory = [];
    private long _undoBytes;
    private long _redoBytes;
    private bool _isExecutingCommand;
    private bool _isRestoring;

    public GraphCommandService(
        GraphEditorService editorService,
        Func<GraphAssetKind> getActiveGraphKind,
        Action afterRestore,
        Action<string> setStatus)
    {
        _editorService = editorService;
        _getActiveGraphKind = getActiveGraphKind;
        _afterRestore = afterRestore;
        _setStatus = setStatus;
    }

    public bool IsRestoring => _isRestoring;

    public bool CanUndo => _undoHistory.Count > 0;

    public bool CanRedo => _redoHistory.Count > 0;

    public event Action? StateChanged;

    public GraphFileModel Capture() => _editorService.ExportGraphModel("command", _getActiveGraphKind());

    public void Execute(string name, Action action)
    {
        if (_isRestoring)
        {
            action();
            return;
        }

        if (_isExecutingCommand)
        {
            action();
            return;
        }

        string beforeJson = CaptureJson();
        _isExecutingCommand = true;
        try
        {
            action();
        }
        finally
        {
            _isExecutingCommand = false;
        }

        Record(name, beforeJson, CaptureJson());
    }

    public void RecordApplied(string name, GraphFileModel before, GraphFileModel after)
    {
        if (_isRestoring || _isExecutingCommand)
            return;

        Record(name, Serialize(before), Serialize(after));
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            _setStatus("Nothing to undo.");
            return false;
        }

        var command = _undoHistory.Last!.Value;
        if (!TryRestore(command.Before, "撤销"))
            return false;

        RemoveLast(_undoHistory, ref _undoBytes);
        AddBounded(_redoHistory, command, ref _redoBytes);
        StateChanged?.Invoke();
        _setStatus($"已撤销：{command.Name}");
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            _setStatus("Nothing to redo.");
            return false;
        }

        var command = _redoHistory.Last!.Value;
        if (!TryRestore(command.After, "重做"))
            return false;

        RemoveLast(_redoHistory, ref _redoBytes);
        AddBounded(_undoHistory, command, ref _undoBytes);
        StateChanged?.Invoke();
        _setStatus($"已重做：{command.Name}");
        return true;
    }

    public void Clear()
    {
        _undoHistory.Clear();
        _redoHistory.Clear();
        _undoBytes = 0;
        _redoBytes = 0;
        StateChanged?.Invoke();
    }

    private void Record(string name, string before, string after)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return;

        var snapshot = new GraphCommandSnapshot(name, before, after, EstimateBytes(before, after));
        AddBounded(_undoHistory, snapshot, ref _undoBytes);
        _redoHistory.Clear();
        _redoBytes = 0;
        StateChanged?.Invoke();
    }

    private void Restore(string snapshotJson)
    {
        _isRestoring = true;
        try
        {
            GraphFileModel snapshot = JsonSerializer.Deserialize<GraphFileModel>(snapshotJson, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to restore graph command snapshot.");
            _editorService.LoadFromModel(snapshot);
            _afterRestore();
        }
        finally
        {
            _isRestoring = false;
        }
    }

    private string CaptureJson() => Serialize(Capture());

    private string Serialize(GraphFileModel model) => JsonSerializer.Serialize(model, _jsonOptions);

    private static long EstimateBytes(string before, string after) =>
        checked(((long)before.Length + after.Length) * sizeof(char));

    private static void AddBounded(
        LinkedList<GraphCommandSnapshot> history,
        GraphCommandSnapshot snapshot,
        ref long historyBytes)
    {
        history.AddLast(snapshot);
        historyBytes += snapshot.EstimatedBytes;
        while (history.Count > MaxHistoryEntries || historyBytes > MaxHistoryBytes)
        {
            if (history.First is not { } first)
                break;
            historyBytes -= first.Value.EstimatedBytes;
            history.RemoveFirst();
        }
    }

    private bool TryRestore(string targetJson, string operation)
    {
        string currentJson = string.Empty;
        try
        {
            currentJson = CaptureJson();
            Restore(targetJson);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"{operation}失败：{ex.Message}");
            try
            {
                if (!string.IsNullOrEmpty(currentJson))
                    Restore(currentJson);
            }
            catch (Exception rollbackError)
            {
                Logger.Error($"{operation}失败后的画布回滚也失败：{rollbackError.Message}");
            }

            _setStatus($"{operation}失败，历史记录已保留。");
            return false;
        }
    }

    private static GraphCommandSnapshot RemoveLast(
        LinkedList<GraphCommandSnapshot> history,
        ref long historyBytes)
    {
        LinkedListNode<GraphCommandSnapshot> node = history.Last
            ?? throw new InvalidOperationException("Command history is empty.");
        history.RemoveLast();
        historyBytes -= node.Value.EstimatedBytes;
        return node.Value;
    }

    private sealed record GraphCommandSnapshot(
        string Name,
        string Before,
        string After,
        long EstimatedBytes);
}
