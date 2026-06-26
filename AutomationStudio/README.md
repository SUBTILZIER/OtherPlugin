# AutomationStudioWpf

## Current Notes (2026-06-26)

- 2026-06-11: CodeGraph, project skill, technical docs, README, and agent memory were audited against the current local code. CodeGraph generated database/cache/log files remain local and ignored.
- Visual wires bind to `GraphEditorService.ConnectionPaths`; persisted graph data and runtime execution still use `GraphEditorService.Connections`.
- `ConnectionPathViewModel` aggregates linear reroute chains only for drawing. Reroute order follows the real `Connections` chain, not point distance, so moving route nodes does not reorder or jump the wire.
- `ConnectionSplinePlanner` is the active visible-wire geometry builder. Single backing connections use one cubic Bezier; aggregated reroute chains use per-segment spline handles scaled from neighboring point distance.
- Current tight/backward reroute layouts are covered as no-loop regressions; do not rewrite `ConnectionSplinePlanner` unless a new concrete repro appears.
- Double-clicking a visible wire inserts a reroute node by sampling the visible curve back to the nearest backing `ConnectionViewModel`; Alt-click still removes the nearest backing connection.
- `GraphEditorService.RunBatchedEdit(...)` batches connection mutations so `ConnectionPaths` rebuild and `GraphChanged` fire once per composed edit.
- Runtime lookup uses an internal lazy `GraphExecutionIndex`; `GraphExecutionPlan` constructor/schema stay unchanged.
- Non-reroute nodes get reusable per-graph numbers: event `N###`, function `Fun###`. Deleting a node frees its number for the next created node.
- `ToDo` jumps within the current graph by matching both target node title and node number. The inspector has a search box plus result list, and an option to return to `ToDo.exec_out` after the target chain finishes.
- `ToDo` direct self-jump is invalid. Return-after-target mode executes the target chain with `stopBeforeNodeId` set to the source ToDo, so a target chain that naturally reaches the same ToDo returns to the source `exec_out` instead of looping.
- ToDo inspector selections are committed into the active `GraphFileModel` before compile/save/run. Static dropdown targets persist as `TargetNodeTitle` / `TargetNodeNumber` / `TargetNodeId`; connected `target_title` / `target_number` pins can still override at runtime.
- The main log panel is a read-only `RichTextBox`: drag-select text freely, `Ctrl+A` selects the filtered log text, and `Ctrl+C` copies selected text without triggering graph-node copy. Runtime execution logs are grouped per node with second-level timestamps; multi-line entries keep visual prefix alignment, while copied text stays raw.
- Current content browser supports folder tree, current-folder tiles, multi-select, box select, drag move/copy, copy/paste, rename/delete, double-click asset open, recursive fuzzy search under the current folder, and `Ctrl+B` locate-to-real-folder.
- Content browser folder/tree and search projections batch-refresh `ContentFolderItems` / `ContentVisibleItems` with `RangeObservableCollection.ReplaceAll(...)`, avoiding per-asset UI collection-change storms in large folders.
- Logger UI updates batch pending entries into `Logger.Entries` with `RangeObservableCollection.AddRange(...)`; the main log panel and log window append new paragraphs incrementally instead of rebuilding the whole log per entry.
- Double-clicking a `FunctionCallNodeViewModel` opens the owning script/function-library asset and loads the target function graph by stable id.
- The editor keeps one `EditorSessionViewModel` and one full `EditorSurfaceControl` per opened asset. Detached windows host their own editable surface directly; the main window keeps the last visible tab surface and only shows `EmptyEditorPanel` when no main tab remains.
- Toolbar compile is active-asset scoped: scripts compile all event/function graphs in that asset, and function libraries compile all functions in that library. `执行图谱` enters a running state and blocks repeat clicks until finish/fail/cancel restores it.
- Multi-window dirty, compile, and graph/controller state is session-scoped through `SetSessionActiveGraphController(...)`, so one asset cannot leak yellow dirty markers or snapshots into another.
- Toolbar `显示最终代码` opens a read-only pseudo-code preview for the current active graph snapshot. It expands resolvable function/custom-event bodies, follows static data inputs and function return outputs, and uses recursion/depth guards without changing runtime, JSON, or dirty state.
- Reroute nodes use centered anchors and a UE-style yellow selection glow/ring for click and box selection feedback.
- `GraphCommandService` records graph-edit snapshots for Undo/Redo. Ctrl+Z undoes graph edits; Ctrl+Y or Ctrl+Shift+Z redoes them.
- Visible wires can be selected, highlighted, deleted with Delete/Backspace, or edited through the wire context menu.
- Node palette search now matches display name, category, type key, `NodeKind`, and generated `NodeDefinition.SearchTags`; recent created node kinds appear first.
- Node display names are user-facing and concise: the start node shows `开始运行`, and default node titles omit the redundant `节点` suffix, such as `延迟`, `找图`, `分支`.
- Function-library calls keep the palette grouped by library asset name, but the node title on the canvas shows only the function name, such as `函数233`, not `函数库1/函数233`.
- Node dragging and arrow-key nudging snap to the 20px grid by default; hold Alt for 1px precision movement.
- `多线程` is an execution node with dynamic `线程N` outputs and a distinct `全部完成` output. Connected branches run in parallel; `全部完成` runs after all branches finish. Mouse/keyboard/window nodes are serialized by a global runtime lock when used inside parallel branches.
- `ScriptHotkeyService` registers global low-level keyboard and mouse hooks (WH_KEYBOARD_LL / WH_MOUSE_LL) so scripts can be started or stopped by external hotkeys without the editor window being focused.
- `ScriptRunManager` manages script run lifecycle: compile → execute with optional retry count, loop count, and fixed loop delay. It enforces single-instance-per-asset, reports running status to the toolbar, and handles hotkey dispatch.
- `ScriptPropertiesWindow` is a themed dark WPF dialog for configuring per-script run settings (hotkey chord, retry/loop counts, fixed delay). Hotkey conflicts are validated before save.

UE4 风格的 WPF 蓝图节点编辑器 — 用于桌面自动化脚本编排。

## 功能

- **节点式编程**: 拖拽节点，连线构建自动化流程
- **多种节点类型**: 鼠标点击/移动/双击、键盘/组合键、滚轮、延迟、找图(OpenCV)、条件分支、循环、多线程、窗口操作、截图、弹窗、找图等待、布尔/字符串逻辑、比较
- **Python 图像识别**: 通过 Python OpenCV `TM_CCOEFF_NORMED` 模板匹配找图
- **资产系统**: 脚本、函数库 — 支持公开到库、私有函数、自定义事件
- **内容浏览器**: 文件夹树 + 瓦片视图，支持递归模糊搜索、多选、框选、复制粘贴、拖拽移动/复制到文件夹、`Ctrl+B` 定位
- **多编辑窗口**: 工具栏下方窗口栏管理主窗口标签页，支持切换、关闭、关闭右侧、关闭所有、拖出为独立窗口；独立窗口不再占用主窗口标签
- **鼠标拾取**: 顶部工具栏可进入全屏拾取模式，鼠标移动时显示坐标和屏幕颜色，支持复制坐标或颜色；复制后自动退出
- **多格式兼容**: 支持鼠标左键/右键/侧键、键盘按键、滚轮方向
- **日志系统**: 内嵌日志面板 + 独立日志窗口，分级过滤(INFO/WARN/ERROR)，增量刷新、自动文件持久化，支持复制，多行日志视觉对齐
- **蓝图编辑器体验**: 框选、组拖动、复制粘贴、对齐、缩放平移、路由节点、边缘自动平移(EdgePan)、快捷键
- **自动环境检测**: 首次执行前后台检测并缓存 Python 环境结果，提供安装指引；找图脚本对 Windows 中文路径做了兼容处理
- **执行前校验**: 检查节点可达性、参数缺失、连线唯一性、循环/坏图
- **ToDo 跳转**: 用节点名 + 编号在同图内跳转，可选目标执行完后返回
- **全局热键启动**: 右键脚本资产 → 属性，配置键盘/鼠标快捷键；无需编辑器前置即可启动/停止脚本
- **执行控制**: 支持设置重试次数、循环次数、固定循环间隔；运行状态实时显示；热键冲突校验

## 使用

1. 底部内容浏览器打开/创建脚本资产
2. 左侧事件图/函数列表添加节点
3. 右键画布打开节点菜单添加节点
4. 拖拽输出引脚到输入引脚连线
5. 右侧属性面板编辑节点参数
6. 点击"执行图谱"运行；如有未编译图会先自动编译，失败才停止执行；运行中按钮会显示执行中并防止重复点击
7. 按 Esc 停止执行
8. 点击"鼠标拾取"可查看/复制当前屏幕坐标与颜色；左键弹复制选项，复制后退出，取消继续拾取，右键退出
9. 右键脚本资产 → 属性，可配置全局热键、重试次数、循环执行等运行参数
10. 配置热键后，无需编辑器前置即可通过热键启动/停止对应脚本

## 快捷键

| 快捷键 | 功能 |
|--------|------|
| Delete | 删除选中节点 |
| Ctrl+C | 复制选中节点；日志面板焦点内复制选中文本 |
| Ctrl+V | 粘贴节点(到鼠标位置)；内容浏览器焦点内粘贴资产 |
| Ctrl+A | 日志面板焦点内全选当前过滤后的日志文本 |
| Ctrl+B | 内容浏览器焦点内定位选中资产真实目录；无选中结果时定位当前打开资产 |
| Q | 横向对齐(居中对齐Y) |
| Shift+Alt+S | 纵向对齐(居中对齐X) |
| F | 缩放到节点全览 |
| Esc | 取消连线 / 停止执行 / 退出鼠标拾取 |
| Alt+点击连线 | 断开连接 |
| 双击连线 | 生成路由节点 |
| 右键拖动>3px | 平移画布 |
| 滚轮 | 缩放画布 |

## 资产系统

### 脚本 (Script)
- 包含事件图和私有函数
- 事件图可添加自定义事件 (CustomEvent / CustomEventCall)
- 只有事件图能直接执行

### 函数库 (FunctionLibrary)
- 全局库，库内函数勾选"公开到库"后才对其他脚本可见
- 脚本只能调用本脚本私有项 + 已公开的库项
- 节点菜单按函数库资产名分组显示公开函数；添加到画布后的调用节点只显示函数名，不显示库名前缀

### 内容浏览器
- 左侧文件夹树，右侧瓦片视图
- 支持文件夹内新建脚本/函数库/文件夹
- 支持多选、框选、`Ctrl+C` / `Ctrl+V` 复制粘贴资产
- 资产拖拽到文件夹支持移动/复制，拖拽时有半透明预览
- 顶部搜索框按当前目录递归列出匹配资产/文件夹，支持空格关键字、路径片段、模糊匹配和不区分大小写
- 搜索结果可双击打开；选中搜索结果或资产后 `Ctrl+B` 会清空搜索并定位到真实父目录
- 画布中双击函数调用节点，会按 stable id 打开被调用函数所在资产并切到对应编辑面板

## 环境要求

### 必需
- .NET 8.0 Runtime
- Windows 10/11

### 可选（用于找图功能）
- Python 3.11+
- OpenCV Python (`opencv-python`)
- Pillow (`pillow`)
- NumPy (`numpy`)

### 自动安装
首次执行图谱前，程序会在后台检测并缓存 Python 环境结果。如果未安装，会弹出提示窗口，提供可复制的安装命令：

```bash
pip install opencv-python pillow numpy -i https://mirrors.aliyun.com/pypi/simple/
```

## 项目结构

```
AutomationStudioWpf/
├── Graph/                       # 节点模型层
│   ├── NodeBaseViewModel.cs     # 抽象节点基类
│   ├── InputNodeBase.cs         # 输入类节点基类
│   ├── PinViewModel.cs          # 引脚模型
│   ├── ConnectionViewModel.cs   # 连线模型
│   ├── ConnectionPathViewModel.cs # 可见连线路径聚合
│   ├── ConnectionSplinePlanner.cs # 可见连线几何
│   ├── *NodeViewModel.cs        # 各节点类型实现
│   ├── GraphTypes.cs            # 枚举定义
│   └── GraphFileModel.cs        # 文件模型
├── Runtime/                     # 执行引擎
│   ├── GraphRuntimeExecutor.cs  # 执行调度 + 结构节点
│   └── GraphExecutionModels.cs  # 运行时数据模型 + internal lazy index
├── Services/                    # 业务服务层
│   ├── GraphEditorService.cs    # 图谱编辑核心逻辑 + 批量连接变更
│   ├── GraphCommandService.cs   # Undo/Redo 快照命令
│   ├── GraphLibraryService.cs   # 图谱/资产库持久化
│   ├── GraphCompileService.cs   # 编译同步与校验
│   ├── NodeSerializer.cs        # 节点序列化/反序列化
│   ├── NodeClipboardService.cs  # 复制粘贴服务
│   ├── NodeFactory.cs           # 节点工厂
│   └── PythonAutoInstaller.cs   # Python 环境检测与安装
├── Interaction/                 # 交互控制器
│   ├── ExecutionController.cs   # 执行、取消、校验、Python 检查
│   ├── GraphListController.cs   # 图谱列表、切换、删除、重命名
│   ├── EditorSessionViewModel.cs # 多编辑窗口 session 状态
│   ├── EditorSurfaceContext.cs  # per-session surface 上下文
│   ├── EditorSurfaceHostController.cs # surface 宿主控制器
│   ├── DetachedEditorWindow.cs  # 独立编辑窗口宿主
│   ├── ContentBrowserIndex.cs   # 内容浏览器 lookup/path/search 缓存
│   ├── CanvasPanZoomController.cs  # 平移、缩放、EdgePan
│   ├── NodeDragSelectionController.cs  # 拖动、框选、复制粘贴、对齐
│   ├── PinConnectionController.cs  # 连线、断线、路由节点
│   ├── InspectorController.cs   # 属性面板主入口、Load/Apply 主分发
│   ├── InspectorController.Parameters.cs # 函数/事件参数面板
│   ├── InspectorController.CommonNodes.cs # 通用小节点面板
│   ├── InspectorController.SystemNodes.cs # 找图/键盘/窗口/程序启动辅助
│   ├── InspectorController.Locks.cs # 前置输入锁定与灰态
│   ├── InspectorController.ToDo.cs # ToDo 目标选择面板逻辑
│   ├── NodePaletteController.cs # 右键节点菜单
│   ├── ScriptHotkeyService.cs   # 全局热键服务 (WH_KEYBOARD_LL/WH_MOUSE_LL)
│   ├── ScriptRunManager.cs      # 脚本运行生命周期管理
│   ├── ScriptPropertiesWindow.cs # 脚本运行属性配置窗口
│   ├── LogPanelController.cs    # 日志过滤、增量刷新
│   └── GraphImportDropController.cs  # JSON 图谱拖拽导入
├── Logging/                     # 日志模块
│   ├── Logger.cs                # 存储 + 文件写入
│   ├── LogEntry.cs              # 日志条目模型
│   ├── LogLevel.cs              # 级别枚举
│   └── LoggingModule.cs         # 过滤 + 着色
├── Controls/
│   ├── EditorSurfaceControl.xaml(.cs) # session 自持完整编辑 surface
│   └── EditorSurfaceControl.InspectorEvents.cs # 详情面板事件转发
├── Python/                      # Python 脚本
│   ├── find_image.py            # OpenCV 找图
│   └── Installer/               # Python 安装包
├── Adapters/                    # Win32/Python 能力封装
│   ├── IMouseAdapter.cs         # 鼠标操作
│   ├── IKeyboardAdapter.cs      # 键盘操作
│   ├── IWindowAdapter.cs        # 窗口操作
│   ├── IScreenshotAdapter.cs    # 截图
│   └── PythonScriptAdapter.cs   # Python JSON 文件通信
├── Nodes/                       # 节点注册与分类执行器
│   └── NodeRegistry.cs          # 节点定义 + 执行器入口
├── MainWindow.xaml(.cs)         # 主窗口 + partial 交互扩展
├── MainWindow.AssetCommands.cs  # 新建/打开/保存/编译/运行按钮入口
├── MainWindow.ContentBrowserCommands.cs # 内容浏览器基础 CRUD、目录刷新、路径索引入口
├── MainWindow.InspectorHandlers.cs # 属性面板事件转发
├── MainWindow.GraphInputHandlers.cs # 画布、节点、pin、节点菜单输入事件
├── MainWindow.GraphListHandlers.cs # 事件图/函数列表、分组展开、公开到库入口
├── MainWindow.EditorSurfaceControllers.cs # surface controller 初始化与事件分发
├── MainWindow.EditorSessionWorkflow.cs # 资产打开/切换/关闭与 session 提交
├── MainWindow.GraphModelHelpers.cs # graph/node DTO clone 与入口标题同步 helper
├── MainWindow.LogAndImportHandlers.cs # 日志与拖拽导入入口
├── MainWindow.VisualTreeHelpers.cs # WPF visual/focus tree helpers
├── MainWindow.WindowLifecycle.cs # 窗口关闭与退出流程
├── MainWindow.ScriptRunSettings.cs # 脚本属性窗口入口与热键分发
├── MainWindow.EditorSessions.cs # 多窗口标签/独立窗口交互
├── MainWindow.EditorSessionState.cs # session dirty/snapshot/compile 目标状态
├── MainWindow.EditorSurfaceHost.cs # surface 宿主
├── LogWindow.xaml(.cs)          # 独立日志窗口
└── App.xaml(.cs)                # 应用程序入口
```

## 开发

```bash
# 构建
dotnet build

# 运行
dotnet run

# 发布（单文件）
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true

# 必要验证
dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck
git diff --check
dotnet run --project .\AutomationStudioWpf.csproj

# Tests/CodexSmoke 是本地-only，可按需保留，但 Git 不跟踪、不提交

# CodeGraph sync
& 'C:\Users\Administrator\nodejs\node-v20.18.1-win-x64\codegraph.cmd' sync
```

## 日志位置

日志文件保存在程序目录下的 `saved/log/` 文件夹中，按时间命名：
```
saved/log/Log_2026_05_28_22_11.txt
```

## 最近更新

### v1.2.11 (2026-06-26)
- **Added**: Global hotkey service (`ScriptHotkeyService`) with low-level keyboard/mouse hooks — start or stop scripts without focusing the editor window.
- **Added**: Per-script run settings window (`ScriptPropertiesWindow`) for configuring hotkey chords, retry counts, loop counts, and fixed loop delay.
- **Added**: Script run lifecycle manager (`ScriptRunManager`) with single-instance enforcement, toolbar status reporting, and hotkey conflict validation.
- **Added**: Inspector controller split into focused partial files: `InspectorController.Parameters.cs`, `InspectorController.CommonNodes.cs`.
- **Added**: MainWindow partials: `MainWindow.AssetCommands.cs`, `MainWindow.GraphInputHandlers.cs`, `MainWindow.ScriptRunSettings.cs`.
- **Changed**: Runtime logs improved; node display names refined for clarity.
- **Changed**: Editor/runtime flows stabilized and UI resources polished after multi-window session refactor.

### v1.2.10 (2026-06-12)
- **Fixed**: Global window handlers now use safe active-surface lookup or no-op when no editor surface exists, avoiding startup/no-session crashes.
- **Changed**: Main window graph input handlers and asset command handlers were split into focused partial files without changing graph JSON, connection routing, or function-library save semantics.
- **Changed**: Inspector parameter/common/system-node/field-lock helpers were split into focused `InspectorController.*.cs` partials; `InspectorController.cs` keeps load/apply dispatch.
- **Optimized**: Compile validation reuses a per-run asset lookup, and content browser search caches flattened searchable text/path until the asset browser refreshes.
- **Optimized**: Content browser lookup/tree/path/search data now goes through internal `ContentBrowserIndex`; log colors are centralized in `LoggingModule.GetLevelBrush(...)`.
- **Changed**: Content browser base commands and folder/tree refresh logic moved from `MainWindow.xaml.cs` to `MainWindow.ContentBrowserCommands.cs`; inspector event forwarding moved to `MainWindow.InspectorHandlers.cs`; multi-select remains in `MainWindow.ContentBrowserMultiSelect.cs`.
- **Changed**: Graph/function list handlers moved from `MainWindow.xaml.cs` to `MainWindow.GraphListHandlers.cs`; shared editor surface colors now use `App.xaml` brush resources where possible.
- **Changed**: Editor surface controller setup, session open/close workflow, and graph DTO helpers moved out of `MainWindow.xaml.cs` into focused partial files.

### v1.2.9 (2026-06-12)
- **Fixed**: Function-library sessions now keep their active function controller in the owning `EditorSurfaceContext`, so switching to another asset no longer drops unsaved function nodes or reloads default entry/return graphs.
- **Fixed**: Active-asset compile/run checks now resolve the current graph controller from the active editor session, avoiding stale global `_activeAssetController` state in multi-window workflows.
- **Changed**: Session dirty/snapshot/compile helpers moved to `MainWindow.EditorSessionState.cs`; ToDo inspector and editor-surface inspector event forwarding now live in small partial files.
- **Changed**: Dark context menu and dropdown list styles are shared from `App.xaml`, not duplicated per window/surface.

### v1.2.8 (2026-06-11)
- **Changed**: Documentation, project skill, agent memory, and CodeGraph were refreshed against current code after the latest `main` pull.
- **Changed**: Each editor session now owns a complete `EditorSurfaceControl`; detached windows host their own editable surface directly, and inactive detached preview/legacy region moving has been removed from the active path.
- **Fixed**: Surface activation is now lightweight and per-session controller state is preserved, so main tabs and detached windows no longer steal each other's graph/list/canvas state.
- **Fixed**: Detaching a session no longer blanks the main host; the main window keeps the last main tab surface, and the empty fallback panel only shows when no main tabs remain.
- **Changed**: Project skill source is now `AutomationStudio/Agent/skills/automation-studio-wpf/SKILL.md`; the old `.kimi/skills/...` path is deleted.

### v1.2.7 (2026-06-09)
- **Added**: UE-style editor window bar. Open assets stay as sessions; reopening an asset focuses the existing session instead of replacing the current editor.
- **Added**: Session right-click actions: close, detach, close all, close right. Detached sessions move the active editor surface into a standalone WPF window and are hidden from the main window bar.
- **Changed**: Removed main-window MDI frames; dragging a tab inside the main window keeps it as a tab, dragging outside creates a standalone window.
- **Changed**: Toolbar compile is active-asset scoped; non-active assets keep `IsCompileDirty` until compiled or saved via compile-all.

### v1.2.6 (2026-06-09)
- **Added**: Content browser recursive fuzzy search under the current folder, including keyword/path matching and search-result double-click open.
- **Added**: `Ctrl+B` content browser locate. It clears search, enters the asset's real parent folder, selects the asset, and can also locate the currently opened asset.
- **Added**: Function call-node double-click navigation by stable graph id. Same-asset functions switch panels directly; library targets open the owning library asset before loading the target graph.
- **Improved**: Content browser UE-style interaction now includes multi-select, box select, asset copy/paste, multi-delete, drag preview, and themed dialogs.

### v1.2.5 (2026-06-08)
- **Added**: Reusable per-graph node numbers (`N###` / `Fun###`) shown in node headers and inspector.
- **Added**: `ToDo` jump node resolves targets by node title + node number, with inspector search/pick UI and optional return-after-target mode.
- **Improved**: Runtime/validation reachability understands static ToDo jump targets; compile/save/run commits inspector edits first, and compile can backfill static ToDo title/number from `TargetNodeId`.
- **Improved**: Log panel uses read-only `RichTextBox` selection so `Ctrl+A` / `Ctrl+C` copy log text instead of graph nodes.
- **Improved**: ToDo return-after-target mode stops the target sub-chain before re-entering the source ToDo, then continues from the source `exec_out`; direct self-jump remains invalid.

### v1.2.4 (2026-06-08)
- **Improved**: Visible wire hit-testing now samples the rendered Bezier geometry before mapping back to backing connections.
- **Improved**: Connection add/remove/reroute edits batch `ConnectionPaths` rebuild and `GraphChanged` notifications through `GraphEditorService.RunBatchedEdit(...)`.
- **Improved**: Runtime execution/input lookup uses an internal lazy `GraphExecutionIndex` without changing graph JSON or `GraphExecutionPlan` construction.

### v1.2.3 (2026-06-08)
- **Changed**: Audited CodeGraph, project skill, technical documentation, README, and agent memory against current local code.
- **Note**: CodeGraph runtime database/log files remain local through `.codegraph/.gitignore`; they are synced but not committed.

### v1.2.2 (2026-06-06)
- **Added**: `GraphCommandService` snapshot-based Undo/Redo for graph edit actions.
- **Added**: Selectable visual wire paths with blue UE-style highlight, Delete/Backspace removal, and right-click actions for delete/add reroute.
- **Improved**: Node move UX with 20px grid snapping, arrow-key nudging, Shift fast nudge, and Alt precision movement.
- **Added**: `NodeDefinition` metadata for search tags, inspector schema key, default values, and validation hints.
- **Improved**: Node palette search now matches category/type key/kind/tags and shows recent node kinds.

### v1.2.1 (2026-06-06)
- **Changed**: Reroute-backed wires aggregate into visible paths; tight/backward layouts currently do not reproduce the old loop issue.
- **Changed**: Visual wire rendering uses `ConnectionPaths`; graph persistence/runtime still use `Connections`.
- **Changed**: Reroute chain draw order follows the actual connection chain, not distance sorting.
- **Fixed**: Double-clicking aggregated visual wires inserts a reroute node again.
- **Improved**: Reroute selection now has a stronger UE-style glow/ring.
- **Note**: Optional local reroute repro helpers can load an external `graph.json`; they remain local-only and untracked.

### v1.2.0 (2026-06-05)
- **新增**: 内容浏览器 — 文件夹树 + 瓦片视图，资产拖拽管理
- **新增**: 脚本/函数库资产系统，支持"公开到库"硬隔离
- **新增**: 自定义事件 (CustomEvent / CustomEventCall)
- **新增**: 执行前校验 — 节点可达性、参数缺失、连线唯一性
- **新增**: 边缘自动平移 (EdgePan) — 拖动到视口边界自动滚动
- **新增**: 22 个阶段 5 常用节点（鼠标双击/位置、组合键、等待图、等待窗口、布尔/字符串逻辑、截图、弹窗等）
- **新增**: 找图节点支持可选识别区域
- **重构**: 属性面板下沉到 InspectorController
- **重构**: 9 个 Interaction Controller 解耦 UI 与业务
- **优化**: 删除 6 个冗余节点 (MouseDrag/InputText/KeySequence/ClickImageCenter/SetVariable/Comment)
- **修复**: 事件图/函数画布隔离
- **修复**: XAML 初始化事件 NullReference
- **修复**: WPF/WinForms 类型歧义

### v1.1.0 (2026-05-28)
- **重构**: MainWindow.xaml.cs 拆分职责到 Services 层
- **新增**: Python 环境自动检测与安装指引
- **新增**: 日志可复制功能
- **优化**: 警告日志显示为黄色，更醒目
- **优化**: 使用阿里云 PyPI 镜像，国内访问更快
- **修复**: 节点属性命名混乱问题（DelayMs 误用）
- **修复**: 旧日志窗口重复创建问题

## 许可证

MIT License
