using System.Windows;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Nodes;
using AutomationStudioWpf.Runtime;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf.Interaction;

internal sealed record CallableNodeCatalog(
    IReadOnlyList<CallableGraphItem> Functions,
    IReadOnlyList<CallableCustomEventItem> CustomEvents);

internal sealed record EditorSurfaceHostServices(
    Window Owner,
    GraphLibraryService LibraryService,
    NodeClipboardService ClipboardService,
    NodeRegistry NodeRegistry,
    Action PersistAll,
    Action SnapshotActiveAsset,
    Action MarkDirty,
    Action MarkLayoutDirty,
    Action EnsureCanvasLargeEnough,
    Action<string> SetStatus,
    Func<CallableNodeCatalog> GetCallableCatalog,
    Func<string, bool?>? GetInspectorSectionState = null,
    Action<string, bool>? SetInspectorSectionState = null,
    Func<NodeBaseViewModel, string>? GetInspectorSchemaKey = null);
