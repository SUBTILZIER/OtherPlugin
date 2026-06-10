# AutomationStudioWpf

## Current Notes (2026-06-09)

- 2026-06-09: CodeGraph, project skill, technical docs, README, and agent memory were audited against the current local code. CodeGraph database files remain local and ignored; only the CodeGraph ignore policy is tracked.
- Visual wires bind to `GraphEditorService.ConnectionPaths`; persisted graph data and runtime execution still use `GraphEditorService.Connections`.
- `ConnectionPathViewModel` aggregates linear reroute chains only for drawing. Reroute order follows the real `Connections` chain, not point distance, so moving route nodes does not reorder or jump the wire.
- `ConnectionSplinePlanner` is the active visible-wire geometry builder. Single backing connections use one cubic Bezier; aggregated reroute chains use per-segment spline handles scaled from neighboring point distance.
- Current tight/backward reroute layouts are covered as no-loop regressions; do not rewrite `ConnectionSplinePlanner` unless a new concrete repro appears.
- Double-clicking a visible wire inserts a reroute node by sampling the visible curve back to the nearest backing `ConnectionViewModel`; Alt-click still removes the nearest backing connection.
- `GraphEditorService.RunBatchedEdit(...)` batches connection mutations so `ConnectionPaths` rebuild and `GraphChanged` fire once per composed edit.
- Runtime lookup uses an internal lazy `GraphExecutionIndex`; `GraphExecutionPlan` constructor/schema stay unchanged.
- Non-reroute nodes get reusable per-graph numbers: event `N###`, function `Fun###`, macro `Mac###`. Deleting a node frees its number for the next created node.
- `ToDo` jumps within the current graph by matching both target node title and node number. The inspector has a search box plus result list, and an option to return to `ToDo.exec_out` after the target chain finishes.
- `ToDo` direct self-jump is invalid. Return-after-target mode executes the target chain with `stopBeforeNodeId` set to the source ToDo, so a target chain that naturally reaches the same ToDo returns to the source `exec_out` instead of looping.
- ToDo inspector selections are committed into the active `GraphFileModel` before compile/save/run. Static dropdown targets persist as `TargetNodeTitle` / `TargetNodeNumber` / `TargetNodeId`; connected `target_title` / `target_number` pins can still override at runtime.
- The main log panel is a read-only `RichTextBox`: drag-select text freely, `Ctrl+A` selects the filtered log text, and `Ctrl+C` copies selected text without triggering graph-node copy.
- Current content browser supports folder tree, current-folder tiles, multi-select, box select, drag move/copy, copy/paste, rename/delete, double-click asset open, recursive fuzzy search under the current folder, and `Ctrl+B` locate-to-real-folder.
- Double-clicking a `FunctionCallNodeViewModel` or `MacroCallNodeViewModel` opens the owning script/function-library/macro-library asset and loads the target function/macro graph by stable id.
- The editor now keeps one `EditorSessionViewModel` per opened asset. Reopening the same asset focuses the existing session instead of replacing it; the main window bar shows only tab sessions, while detached sessions are managed by their own standalone windows.
- Toolbar compile is active-asset scoped: scripts compile all event/function/macro graphs in that asset, and function/macro libraries compile all graphs in that library.
- Reroute nodes use centered anchors and a UE-style yellow selection glow/ring for click and box selection feedback.
- `GraphCommandService` records graph-edit snapshots for Undo/Redo. Ctrl+Z undoes graph edits; Ctrl+Y or Ctrl+Shift+Z redoes them.
- Visible wires can be selected, highlighted, deleted with Delete/Backspace, or edited through the wire context menu.
- Node palette search now matches display name, category, type key, `NodeKind`, and generated `NodeDefinition.SearchTags`; recent created node kinds appear first.
- Node dragging and arrow-key nudging snap to the 20px grid by default; hold Alt for 1px precision movement.

UE4 风格的 WPF 蓝图节点编辑器 — 用于桌面自动化脚本编排。

## 功能

- **节点式编程**: 拖拽节点，连线构建自动化流程
- **多种节点类型**: 鼠标点击/移动/双击、键盘/组合键、滚轮、延迟、找图(OpenCV)、条件分支、循环、窗口操作、截图、弹窗、找图等待、布尔/字符串逻辑、比较
- **Python 图像识别**: 通过 Python OpenCV `TM_CCOEFF_NORMED` 模板匹配找图
- **资产系统**: 脚本、函数库、宏库 — 支持公开到库、私有函数/宏、自定义事件
- **内容浏览器**: 文件夹树 + 瓦片视图，支持递归模糊搜索、多选、框选、复制粘贴、拖拽移动/复制到文件夹、`Ctrl+B` 定位
- **多编辑窗口**: 工具栏下方窗口栏管理主窗口标签页，支持切换、关闭、关闭右侧、关闭所有、拖出为独立窗口；独立窗口不再占用主窗口标签
- **多格式兼容**: 支持鼠标左键/右键/侧键、键盘按键、滚轮方向
- **日志系统**: 内嵌日志面板 + 独立日志窗口，分级过滤(INFO/WARN/ERROR)，自动文件持久化，支持复制
- **蓝图编辑器体验**: 框选、组拖动、复制粘贴、对齐、缩放平移、路由节点、边缘自动平移(EdgePan)、快捷键
- **自动环境检测**: 启动时自动检测 Python 环境，提供一键安装指引
- **执行前校验**: 检查节点可达性、参数缺失、连线唯一性、循环/坏图
- **ToDo 跳转**: 用节点名 + 编号在同图内跳转，可选目标执行完后返回

## 使用

1. 底部内容浏览器打开/创建脚本资产
2. 左侧事件图/函数/宏列表添加节点
3. 右键画布打开节点菜单添加节点
4. 拖拽输出引脚到输入引脚连线
5. 右侧属性面板编辑节点参数
6. 点击"执行图谱"运行
7. 按 Esc 停止执行

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
| Esc | 取消连线 / 停止执行 |
| Alt+点击连线 | 断开连接 |
| 双击连线 | 生成路由节点 |
| 右键拖动>3px | 平移画布 |
| 滚轮 | 缩放画布 |

## 资产系统

### 脚本 (Script)
- 包含事件图、私有函数、私有宏
- 事件图可添加自定义事件 (CustomEvent / CustomEventCall)
- 只有事件图能直接执行

### 函数库 (FunctionLibrary) / 宏库 (MacroLibrary)
- 全局库，库内函数/宏勾选"公开到库"后才对其他脚本可见
- 脚本只能调用本脚本私有项 + 已公开的库项

### 内容浏览器
- 左侧文件夹树，右侧瓦片视图
- 支持文件夹内新建脚本/函数库/宏库/文件夹
- 支持多选、框选、`Ctrl+C` / `Ctrl+V` 复制粘贴资产
- 资产拖拽到文件夹支持移动/复制，拖拽时有半透明预览
- 顶部搜索框按当前目录递归列出匹配资产/文件夹，支持空格关键字、路径片段、模糊匹配和不区分大小写
- 搜索结果可双击打开；选中搜索结果或资产后 `Ctrl+B` 会清空搜索并定位到真实父目录
- 画布中双击函数/宏调用节点，会按 stable id 打开被调用函数/宏所在资产并切到对应编辑面板

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
首次启动时，程序会自动检测 Python 环境。如果未安装，会弹出提示窗口，提供可复制的安装命令：

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
│   ├── DetachedEditorWindow.cs  # 独立编辑窗口宿主
│   ├── CanvasPanZoomController.cs  # 平移、缩放、EdgePan
│   ├── NodeDragSelectionController.cs  # 拖动、框选、复制粘贴、对齐
│   ├── PinConnectionController.cs  # 连线、断线、路由节点
│   ├── InspectorController.cs   # 属性面板、字段锁定
│   ├── NodePaletteController.cs # 右键节点菜单
│   ├── LogPanelController.cs    # 日志过滤、刷新
│   └── GraphImportDropController.cs  # JSON 图谱拖拽导入
├── Logging/                     # 日志模块
│   ├── Logger.cs                # 存储 + 文件写入
│   ├── LogEntry.cs              # 日志条目模型
│   ├── LogLevel.cs              # 级别枚举
│   └── LoggingModule.cs         # 过滤 + 着色
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
├── Tests/CodexSmoke/            # UI smoke 门禁
├── MainWindow.xaml(.cs)         # 主窗口 + partial 交互扩展
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

# Smoke 验证
dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck
dotnet run --project .\Tests\CodexSmoke\AutomationStudioSmoke.csproj --no-restore

# Optional reroute repro smoke
$env:AUTOMATION_STUDIO_REROUTE_GRAPH_JSON='C:\Users\Administrator\Desktop\graph.json'
dotnet run --project .\Tests\CodexSmoke\AutomationStudioSmoke.csproj --no-restore

# CodeGraph sync
& 'C:\Users\Administrator\nodejs\node-v20.18.1-win-x64\codegraph.cmd' sync
```

## 日志位置

日志文件保存在程序目录下的 `saved/log/` 文件夹中，按时间命名：
```
saved/log/Log_2026_05_28_22_11.txt
```

## 最近更新

### v1.2.7 (2026-06-09)
- **Added**: UE-style editor window bar. Open assets stay as sessions; reopening an asset focuses the existing session instead of replacing the current editor.
- **Added**: Session right-click actions: close, detach, close all, close right. Detached sessions move the active editor surface into a standalone WPF window and are hidden from the main window bar.
- **Changed**: Removed main-window MDI frames; dragging a tab inside the main window keeps it as a tab, dragging outside creates a standalone window.
- **Changed**: Toolbar compile is active-asset scoped; non-active assets keep `IsCompileDirty` until compiled or saved via compile-all.

### v1.2.6 (2026-06-09)
- **Added**: Content browser recursive fuzzy search under the current folder, including keyword/path matching and search-result double-click open.
- **Added**: `Ctrl+B` content browser locate. It clears search, enters the asset's real parent folder, selects the asset, and can also locate the currently opened asset.
- **Added**: Function/macro call-node double-click navigation by stable graph id. Same-asset functions/macros switch panels directly; library targets open the owning library asset before loading the target graph.
- **Improved**: Content browser UE-style interaction now includes multi-select, box select, asset copy/paste, multi-delete, drag preview, and themed dialogs.

### v1.2.5 (2026-06-08)
- **Added**: Reusable per-graph node numbers (`N###` / `Fun###` / `Mac###`) shown in node headers and inspector.
- **Added**: `ToDo` jump node resolves targets by node title + node number, with inspector search/pick UI and optional return-after-target mode.
- **Improved**: Runtime/validation reachability understands static ToDo jump targets; compile/save/run commits inspector edits first, and compile can backfill static ToDo title/number from `TargetNodeId`.
- **Improved**: Log panel uses read-only `RichTextBox` selection so `Ctrl+A` / `Ctrl+C` copy log text instead of graph nodes.
- **Improved**: ToDo return-after-target mode stops the target sub-chain before re-entering the source ToDo, then continues from the source `exec_out`; direct self-jump remains invalid.

### v1.2.4 (2026-06-08)
- **Improved**: Visible wire hit-testing now samples the rendered Bezier geometry before mapping back to backing connections.
- **Improved**: Connection add/remove/reroute edits batch `ConnectionPaths` rebuild and `GraphChanged` notifications through `GraphEditorService.RunBatchedEdit(...)`.
- **Improved**: Runtime execution/input lookup uses an internal lazy `GraphExecutionIndex` without changing graph JSON or `GraphExecutionPlan` construction.
- **Added**: Smoke coverage for visible-curve hit mapping, pin connection state refresh, no-loop reroute regressions, and batched connection edits.

### v1.2.3 (2026-06-08)
- **Changed**: Audited CodeGraph, project skill, technical documentation, README, and agent memory against current local code.
- **Note**: CodeGraph runtime database/log files remain local through `.codegraph/.gitignore`; they are synced but not committed.

### v1.2.2 (2026-06-06)
- **Added**: `GraphCommandService` snapshot-based Undo/Redo for graph edit actions.
- **Added**: Selectable visual wire paths with blue UE-style highlight, Delete/Backspace removal, and right-click actions for delete/add reroute.
- **Improved**: Node move UX with 20px grid snapping, arrow-key nudging, Shift fast nudge, and Alt precision movement.
- **Added**: `NodeDefinition` metadata for search tags, inspector schema key, default values, and validation hints.
- **Improved**: Node palette search now matches category/type key/kind/tags and shows recent node kinds.
- **Added**: Smoke coverage for command undo/redo, selected wire deletion, and definition metadata search.

### v1.2.1 (2026-06-06)
- **Changed**: Reroute-backed wires aggregate into visible paths, with tight/backward layouts covered by no-loop smoke regressions.
- **Changed**: Visual wire rendering uses `ConnectionPaths`; graph persistence/runtime still use `Connections`.
- **Changed**: Reroute chain draw order follows the actual connection chain, not distance sorting.
- **Fixed**: Double-clicking aggregated visual wires inserts a reroute node again.
- **Improved**: Reroute selection now has a stronger UE-style glow/ring.
- **Added**: Smoke coverage for reroute chain geometry, movement stability, visual wire hit-testing, and optional external `graph.json` repro.

### v1.2.0 (2026-06-05)
- **新增**: 内容浏览器 — 文件夹树 + 瓦片视图，资产拖拽管理
- **新增**: 脚本/函数库/宏库资产系统，支持"公开到库"硬隔离
- **新增**: 自定义事件 (CustomEvent / CustomEventCall)
- **新增**: 执行前校验 — 节点可达性、参数缺失、连线唯一性
- **新增**: 边缘自动平移 (EdgePan) — 拖动到视口边界自动滚动
- **新增**: 22 个阶段 5 常用节点（鼠标双击/位置、组合键、等待图、等待窗口、布尔/字符串逻辑、截图、弹窗等）
- **新增**: 找图节点支持可选识别区域
- **重构**: 属性面板下沉到 InspectorController
- **重构**: 9 个 Interaction Controller 解耦 UI 与业务
- **优化**: 删除 6 个冗余节点 (MouseDrag/InputText/KeySequence/ClickImageCenter/SetVariable/Comment)
- **修复**: 事件图/函数/宏画布隔离
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
