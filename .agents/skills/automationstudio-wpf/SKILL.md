---
name: automationstudio-wpf
description: Project-specific workflow for AutomationStudioWpf. Use when working in E:\UE4work\OtherPlugin\AutomationStudio on WPF graph editor code, reroute/connection rendering, graph persistence/runtime behavior, docs, validation gates, CodeGraph sync, or project onboarding.
---

# AutomationStudioWpf

Use this skill for project-specific work in `E:\UE4work\OtherPlugin\AutomationStudio`.

## Workflow

- Read `agentmemory.md` first for durable project decisions.
- Prefer `rg`/`rg --files` for discovery. Use CodeGraph when symbol/caller impact beats plain search.
- Keep `.codegraph/.gitignore` tracked, but do not commit CodeGraph database, wal/shm, cache, pid, or log files.
- Preserve dirty user changes. Do not reset/revert unrelated files.
- Keep runtime/persistence behavior separate from visual graph rendering.
- Route graph edit mutations through `GraphCommandService` when they should be undoable.
- When asked to update project docs before push, refresh README, TECHNICAL, `agentmemory.md`, this skill, run CodeGraph sync, then verify before committing.

## Architecture Notes

- `GraphEditorService.Connections` is the persisted/runtime source of truth.
- `GraphEditorService.ConnectionPaths` is the visual wire surface used by `MainWindow.xaml`.
- `ConnectionPathViewModel` aggregates linear reroute chains for drawing only.
- `ConnectionSplinePlanner` builds Bezier/spline geometry. Do not reorder reroute nodes by distance; route order must follow the actual `Connections` chain so moving a reroute node never jumps the line order.
- Ambiguous reroute branch/merge falls back to individual connection paths.
- `PinConnectionController` owns pin drag, wire preview, Alt-click disconnect, and double-click reroute insertion.
- `GraphCommandService` snapshots `GraphFileModel` before/after graph edits for Undo/Redo. Capture the active `GraphAssetKind`; clear command history on graph/function/macro/content switches.
- `NodeDefinition` metadata includes `SearchTags`, `InspectorSchemaKey`, `DefaultValues`, and `ValidationHints`; palette search should stay metadata-driven.

## Reroute/Wire Rules

- Double-click a visible wire inserts a `RerouteNodeViewModel` by splitting the nearest backing `ConnectionViewModel`.
- Visible wire hit tests use `ConnectionPathViewModel.FindNearestConnection(...)` to map a visual path back to a logical connection.
- `IsGraphBlankSource(...)` must treat `ConnectionPathViewModel` as non-blank; otherwise wire double-click can be swallowed by selection start.
- Reroute node anchors are centered through `RerouteNodeViewModel.GetPinAnchor(...)`.
- Reroute selection should stay visibly highlighted; the current XAML uses a yellow ring/glow like UE blueprint.
- Visible wire selection lives on `ConnectionPathViewModel.IsSelected`; Delete/Backspace removes all backing connections for the selected visual path and leaves reroute nodes in place.
- Node movement snaps to the 20px grid by default; hold Alt for precision movement.

## Validation

Run these before finishing code changes:

```powershell
dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck
dotnet run --project .\Tests\CodexSmoke\AutomationStudioSmoke.csproj --no-restore
$env:AUTOMATION_STUDIO_REROUTE_GRAPH_JSON='C:\Users\Administrator\Desktop\graph.json'; dotnet run --project .\Tests\CodexSmoke\AutomationStudioSmoke.csproj --no-restore
```

Then run a short WPF startup probe. If it exits quickly, prints `Unhandled exception`, or throws a XAML/WPF init error, collect stdout/stderr and fix before finalizing.

Finish with:

```powershell
& 'C:\Users\Administrator\nodejs\node-v20.18.1-win-x64\codegraph.cmd' sync
```

Do not run `git push` unless the user explicitly asks.
