using System.Text.Json;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Services;

public sealed class GraphCommandService
{
    private readonly GraphEditorService _editorService;
    private readonly Func<GraphAssetKind> _getActiveGraphKind;
    private readonly Action _afterRestore;
    private readonly Action<string> _setStatus;
    private readonly JsonSerializerOptions _jsonOptions = new();
    private readonly Stack<GraphCommandSnapshot> _undoStack = [];
    private readonly Stack<GraphCommandSnapshot> _redoStack = [];
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

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public event Action? StateChanged;

    public GraphFileModel Capture() => Clone(_editorService.ExportGraphModel("command", _getActiveGraphKind()));

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

        var before = Capture();
        _isExecutingCommand = true;
        try
        {
            action();
        }
        finally
        {
            _isExecutingCommand = false;
        }

        Record(name, before, Capture());
    }

    public void RecordApplied(string name, GraphFileModel before, GraphFileModel after)
    {
        if (_isRestoring || _isExecutingCommand)
            return;

        Record(name, Clone(before), Clone(after));
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            _setStatus("Nothing to undo.");
            return false;
        }

        var command = _undoStack.Pop();
        Restore(command.Before);
        _redoStack.Push(command);
        StateChanged?.Invoke();
        _setStatus($"Undo: {command.Name}");
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            _setStatus("Nothing to redo.");
            return false;
        }

        var command = _redoStack.Pop();
        Restore(command.After);
        _undoStack.Push(command);
        StateChanged?.Invoke();
        _setStatus($"Redo: {command.Name}");
        return true;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke();
    }

    private void Record(string name, GraphFileModel before, GraphFileModel after)
    {
        if (SameSnapshot(before, after))
            return;

        _undoStack.Push(new GraphCommandSnapshot(name, before, after));
        _redoStack.Clear();
        StateChanged?.Invoke();
    }

    private void Restore(GraphFileModel snapshot)
    {
        _isRestoring = true;
        try
        {
            _editorService.LoadFromModel(Clone(snapshot));
            _afterRestore();
        }
        finally
        {
            _isRestoring = false;
        }
    }

    private bool SameSnapshot(GraphFileModel left, GraphFileModel right) =>
        JsonSerializer.Serialize(left, _jsonOptions) == JsonSerializer.Serialize(right, _jsonOptions);

    private static GraphFileModel Clone(GraphFileModel model)
    {
        var json = JsonSerializer.Serialize(model);
        return JsonSerializer.Deserialize<GraphFileModel>(json)
            ?? throw new InvalidOperationException("Failed to clone graph command snapshot.");
    }

    private sealed record GraphCommandSnapshot(string Name, GraphFileModel Before, GraphFileModel After);
}
