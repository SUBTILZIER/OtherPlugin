# AutomationStudio Agent Memory

## Communication preference

- 用户偏好中文、简洁说明。中间进度说清“在做什么/发现什么/下一步”，少废话。
- 技术细节保留准确名词、文件名、命令；解释原因时用短句。
- 不要自动 `git push`。只有用户明确要求推送时才推；推送前必须确认不包含测试功能相关文件夹。

## 2026-06-06 reroute connection rendering

- Visual wiring now binds XAML to `GraphEditorService.ConnectionPaths`; persisted/runtime logic still uses `Connections`.
- Reroute chains are aggregated by `ConnectionPathViewModel` and shaped by `ConnectionSplinePlanner`; linear reroute chains keep the persisted `Connections` chain order so moving route nodes never reorders the drawn path. Ambiguous reroute branch/merge falls back to individual connection paths.
- Visible wire double-click/Alt-click must map `ConnectionPathViewModel` back to nearest backing `ConnectionViewModel`; `IsGraphBlankSource` must treat `ConnectionPathViewModel` as non-blank or selection will swallow wire double-click.
- Reroute selection uses a UE-style yellow glow/ring in XAML; keep it visible for click and box selection.
- Optional repro smoke: set `AUTOMATION_STUDIO_REROUTE_GRAPH_JSON` to a graph file such as `C:/Users/Administrator/Desktop/graph.json`.

## 2026-06-06 editor command and wire UX foundation

- `GraphCommandService` is snapshot-based Undo/Redo for graph edits. It captures `GraphFileModel` before/after a command and restores through `GraphEditorService.LoadFromModel(...)`.
- Always capture the active `GraphAssetKind`; function/macro undo must not restore as event graphs. Clear command history when switching content assets or graph/function/macro items.
- Use `Execute(...)` for direct mutations and `RecordApplied(...)` for continuous interactions that already moved nodes. Node drag records one command at drag end.
- Visible wire selection lives on `ConnectionPathViewModel.IsSelected`; Delete/Backspace removes all backing connections in the selected visual path as one undoable command, but reroute nodes remain.
- `NodeDefinition` now has `SearchTags`, `InspectorSchemaKey`, `DefaultValues`, and `ValidationHints`; palette search uses display/category/type key/kind/tags and shows recent node kinds.
- Node movement snaps to the 20px grid by default; Alt keeps precision movement.

## 2026-06-08 git push prep

- User explicitly requested CodeGraph, project skill, TECHNICAL, README, agentmemory refresh and git push; pushing is allowed for this task after verification.
- Track `.codegraph/.gitignore`; do not commit CodeGraph db/wal/shm/log/cache files.
- Track the project skill under `.agents/skills/automationstudio-wpf/`; avoid staging unrelated third-party skill folders unless explicitly requested.
- Before push, run build, smoke, optional external reroute graph smoke, WPF startup probe, and `codegraph.cmd sync`.

## 2026-06-05 graph isolation fix

- Content browser current UX: header has title only; asset creation is only through blank right-click menu with `脚本 / 文件夹 / 函数库 / 宏库`. Do not re-add a standalone `宏` asset menu item.
- Content browser context menu uses one shared menu and toggles visibility in `ContentBrowserContextMenu_Opened`: blank area shows create items; asset right-click shows only rename/delete. Do not put eventful `ContextMenu` objects inside `ListBoxItem.Style Setter`; that caused a startup `IStyleConnector` cast crash.
- Folder tree UX: single-click row enters folder; arrow button only expands/collapses. `HasFolderChildren` counts child folders only. `TreeIndent` controls pixel indentation; `TreeDisplayName` is plain name, no leading spaces.
- Folder tree arrow uses `ContentFolderToggleIconStyle`. Keep default `Path.Data` in style setter so `DataTrigger` can switch expanded state to the down triangle.
- Content browser layout: `ContentTreeColumn` default width 180, min 120, max 420; `ContentBrowserTreeSplitter` separates tree and tile grid; tile grid stays in column 2 and wraps with horizontal scrolling disabled.
- Verification gates now include startup crash probe: build, smoke, run `dotnet run --project .\AutomationStudioWpf.csproj` only long enough to confirm the window starts, then `codegraph.cmd sync`. Do not wait 20s; early exit, WPF startup exception, or `Unhandled exception` is failure and requires collecting stdout/stderr/stack output first.
- `Tests/CodexSmoke` is lightweight regression smoke, not a full test framework. Keep it focused on critical UI/data regressions and do not push test-related folders unless the user explicitly asks.

- Event graph / function / macro share one editor canvas instance, but data must stay isolated in separate `GraphListItemViewModel.Graph` models.
- Critical rule: before loading a graph from another section, first `SnapshotActiveAsset()`, then set `_activeAssetController` to the target controller, then call `LoadItem(..., snapshotCurrent: false)`.
- Do not call `GraphListController.Load()` while `_activeAssetController` still points at the old controller. `Load()` persists library; if active controller is stale, `PersistAssetLibrary()` can snapshot the newly-loaded canvas into the old graph and mix event/function/macro contents.
- Graph/function/macro list items now activate on single left click through `ActivateGraphListItem(...)`; double-click remains supported but is not required for navigation.
- Right-click graph/function/macro items also activates the item before opening the context menu so rename/delete target the correct controller.
- Bottom panel default: content browser and log split 50/50; bottom row height starts at 360 with min 180.
- Smoke test must cover event/function/macro switching and assert graph models stay event-only/function-only/macro-only.
- FunctionLibrary/MacroLibrary entries have `IsPublicToLibrary` (`公开到库`). `CallableGraphResolver` is the single source for palette, compile sync, and runtime lookup. Other scripts can only use public library functions/macros; old private-library calls fail compile and keep dirty.
- Compile issue paths must be full content-browser paths: `content/父文件夹/.../资产/图`. This includes function/macro library graphs under nested folders.
- Custom events are event-graph local: `CustomEventNodeViewModel` + `CustomEventCallNodeViewModel`, bound by `CustomEventId`. Palette lists calls under `本脚本事件` only while editing an event graph. Runtime executes target event chain then returns to caller `exec_out`; recursion key is `custom_event:{id}`.
- Pin wiring UX: dragging from either input or output pin to blank canvas opens the node palette. The next created node auto-connects its first compatible opposite pin. `TryGetPinAtPosition` also has an expanded near-hit radius for easier pin drops.
- Parameter defaults: `GraphParameterDefinition.DefaultValue` persists through file/runtime models. Function/macro/custom event entry defaults are used when call inputs are unconnected; return/macro output defaults are used when return inputs are unconnected.

## 2026-06-04 recovery state

- Content browser source of truth is `ContentBrowserItems`; left folder tree binds `ContentFolderItems`; right tile grid binds `ContentVisibleItems`.
- Folder tree uses `HasFolderChildren` for `>` so empty folders do not show an expander. Current-folder filtering uses `_currentContentFolderId`.
- Folder commands use `_contentFolderSelectionActive`; do not let folder `Delete` / `F2` / context menu fall back to stale selected asset tiles.
- Graph sidebar has three independent controllers: event graphs, functions, macros. Empty sections collapse; add expands; delete-last clears canvas through `GraphEditorService.ClearGraph()`.
- Section collapsed state is runtime-only per content asset. `*SectionHasState` distinguishes explicit collapsed state from default auto-expand.
- New graph/function/macro list items start `IsCompileDirty = true`. `MarkLogicDirty()` sets compile dirty; `MarkLayoutDirty()` only sets save dirty.
- Compile sync updates function/macro call nodes, clears graph compile dirty, and only marks assets changed by call-reference sync as save dirty.
- Save may prompt compile. Run is blocked while compile-dirty changes exist.
- Shared dark context menu style is `DarkContextMenuStyle`; content browser/tree and graph lists must keep it attached.
- Isolated tests can set `AUTOMATION_STUDIO_LIBRARY_DIR`; default library path remains `%APPDATA%/AutomationStudioWpf`.
- Verification gates: `dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck`, `dotnet run --project .\Tests\CodexSmoke\AutomationStudioSmoke.csproj --no-restore`, launch crash probe `dotnet run --project .\AutomationStudioWpf.csproj` without fixed 20s wait, then `codegraph.cmd sync`.
