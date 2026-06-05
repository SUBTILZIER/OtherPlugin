# AutomationStudio Agent Memory

## Communication preference

- 用户偏好中文、简洁说明。中间进度说清“在做什么/发现什么/下一步”，少废话。
- 技术细节保留准确名词、文件名、命令；解释原因时用短句。

## 2026-06-05 graph isolation fix

- Content browser current UX: header has title only; asset creation is only through blank right-click menu with `脚本 / 文件夹 / 函数库 / 宏库`. Do not re-add a standalone `宏` asset menu item.
- Content browser context menu uses one shared menu and toggles visibility in `ContentBrowserContextMenu_Opened`: blank area shows create items; asset right-click shows only rename/delete. Do not put eventful `ContextMenu` objects inside `ListBoxItem.Style Setter`; that caused a startup `IStyleConnector` cast crash.
- Folder tree UX: single-click row enters folder; arrow button only expands/collapses. `HasFolderChildren` counts child folders only. `TreeIndent` controls pixel indentation; `TreeDisplayName` is plain name, no leading spaces.
- Folder tree arrow uses `ContentFolderToggleIconStyle`. Keep default `Path.Data` in style setter so `DataTrigger` can switch expanded state to the down triangle.
- Content browser layout: `ContentTreeColumn` default width 180, min 120, max 420; `ContentBrowserTreeSplitter` separates tree and tile grid; tile grid stays in column 2 and wraps with horizontal scrolling disabled.
- Verification gates now include startup check: build, smoke, launch `dotnet run --project .\AutomationStudioWpf.csproj` for ~20s, then `codegraph.cmd sync`. Timeout during launch is OK if no exception appears; early exit or `Unhandled exception` is failure.

- Event graph / function / macro share one editor canvas instance, but data must stay isolated in separate `GraphListItemViewModel.Graph` models.
- Critical rule: before loading a graph from another section, first `SnapshotActiveAsset()`, then set `_activeAssetController` to the target controller, then call `LoadItem(..., snapshotCurrent: false)`.
- Do not call `GraphListController.Load()` while `_activeAssetController` still points at the old controller. `Load()` persists library; if active controller is stale, `PersistAssetLibrary()` can snapshot the newly-loaded canvas into the old graph and mix event/function/macro contents.
- Graph/function/macro list items now activate on single left click through `ActivateGraphListItem(...)`; double-click remains supported but is not required for navigation.
- Right-click graph/function/macro items also activates the item before opening the context menu so rename/delete target the correct controller.
- Bottom panel default: content browser and log split 50/50; bottom row height starts at 360 with min 180.
- Smoke test must cover event/function/macro switching and assert graph models stay event-only/function-only/macro-only.
- FunctionLibrary/MacroLibrary entries have `IsPublicToLibrary` (`公开到库`). Node palette only lists public library functions/macros for other scripts; script-local functions/macros remain searchable inside their own script. Runtime lookup still includes hidden library ids so existing calls do not break.
- Custom events are event-graph local: `CustomEventNodeViewModel` + `CustomEventCallNodeViewModel`, bound by `CustomEventId`. Palette lists calls under `本脚本事件` only while editing an event graph. Runtime executes target event chain then returns to caller `exec_out`; recursion key is `custom_event:{id}`.

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
- Verification gates: `dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck`, `dotnet run --project .\bin\CodexSmoke\AutomationStudioSmoke.csproj --no-restore`, launch check `dotnet run --project .\AutomationStudioWpf.csproj`, then `codegraph.cmd sync`.
