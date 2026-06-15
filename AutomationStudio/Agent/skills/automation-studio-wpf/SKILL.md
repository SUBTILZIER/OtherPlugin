# AutomationStudioWpf 开发 Skill

## 项目定位
WPF 可视化节点自动化编辑器，类似 UE4 蓝图。技术栈 C# 12 / .NET 8.0-windows，零外部 NuGet 包。

## 沟通规则
- 默认用中文，回答简洁。
- 中间进度只说：正在做什么、发现什么、下一步。
- 保留准确文件名、类名、方法名、命令，不为了省字改技术名词。
- 不要自动 `git push`。只有用户明确要求推送时才推；推送前确认不包含测试功能相关文件夹。

## 踩坑记录（按时间倒序，新记录追加到顶部）

### 2026-06-09: 多编辑窗口 / session swap

- 多窗口不是 graph JSON 功能；不要改保存 schema。运行期用 `EditorSessionViewModel` 管打开资产窗口。
- 每个打开资产一个 session，持有独立 `GraphEditorService`、`NodeFactory`、`GraphCommandService`、事件图/函数集合和 remembered active graph。重复打开同资产只聚焦已有 session。
- 每个 session 持有自己的完整 `EditorSurfaceControl`，包含图列表、画布、节点菜单和属性面板；`EditorSurfaceContext` 持有该 session 的 graph list/canvas/drag/inspector/pin/palette/import controllers。
- `EditorWindowBar` 在工具栏下方，只显示主窗口标签页 session（非 detached）；右键关闭全部/关闭右侧只作用于可见标签页。拖出主窗口会创建 `DetachedEditorWindow`；拖到主窗口内部只激活标签，不创建画布子窗口。
- 拖动窗口标签时有 `Popup` 跟随预览卡片；越过主窗口边界时提示会变为“释放后成为独立窗口”。
- 主窗口 tab 和 `DetachedEditorWindow` 都直接 host 对应 session 的 `Surface`；detached 窗口始终显示自己的可编辑 surface，不再显示 remembered graph 只读预览。
- `EditorSurfaceContext.Configure(...)` 必须保持幂等；不要在 surface attach/activate 时重建该 session 的 controller，否则 active graph、列表展开和节点显示会被重置。
- surface 事件进入 `EditorSurfaceContext.HandleEvent(...)` 后先分类：明确用户交互才提升 active session；`PinAnchorLoaded/LayoutUpdated`、无按键 `MouseMove`、初始化触发的 `TextChanged/SelectionChanged` 只使用所属 context 或直接忽略，不能改变工具栏编译目标。detached 激活不能覆盖 `_lastMainEditorSession`，主窗口应继续显示最近的主窗口 tab。
- dirty/snapshot 回调必须是 session-scoped：surface controller 通过闭包调用所属 `EditorSessionViewModel` 的 active controller，不要直接用全局 `_activeAssetController` 标脏，否则多资产同开时会把 C 的黄点打到 A。
- 图/函数切换和新增必须走 `SetSessionActiveGraphController(session, controller)`。只写全局 `_activeAssetController` 不够；session 的 `EditorSurfaceContext.ActiveAssetController` 和 remembered active graph 也必须同步，否则函数库切 tab 后可能 snapshot 空 controller 并回到默认 entry/return。
- `HandleEditorSurfaceEvent(...)` 不能在事件结束后把当前全局 `_activeAssetController` 写回 `_activeEditorSession.SurfaceContext`。事件来自非 active surface 时这会污染另一个 session。
- 全局窗口事件必须优先用 `TryGetActiveEditorSurface()` 或事件来源 surface；启动/无资产/detached 切焦点时没有 active surface 应 no-op，不能抛 `No active editor surface is available.`。
- 旧 `MainWindow.EditorSurfaceRegions.cs` 已删除。不要恢复 legacy sidebar/canvas/inspector 区域搬移。
- `DarkContextMenuStyle`、`DarkDropdownListBoxStyle`、`DarkDropdownListBoxItemStyle` 是 `App.xaml` 共享资源；不要在 `MainWindow.xaml` / `EditorSurfaceControl.xaml` 重复定义。
- 关闭 editor session 只 snapshot 回 `ContentAssetViewModel`，不删除内容浏览器资产。删除资产时要关闭所有指向被删 asset id 的 sessions。
- 保存、退出、编译前用 `CommitInspectorAndSnapshotAllSessions()` / `CommitAllSessionsToAssets()`；工具栏编译用 `GraphCompileService.CompileAsset(...)` 编译当前激活资产内全部图，编译前必须先 snapshot 所有打开 session。
- `CompileActiveAsset(...)` 成功后必须把 asset 中被清掉的 `IsCompileDirty` 同步回对应 session 图列表，再刷新窗口栏、section badge 和编译按钮；不要等保存才清 UI 黄点。
- `GraphCompileService` compile 入口复用一次 asset id lookup；新增校验不要在每个 `Validate*` 里重复建索引。
- 内容浏览器搜索使用 `ContentBrowserIndex` 内的扁平 `ContentAssetSearchEntry` 缓存；资产刷新、移动、重命名后要重建 index，避免搜索路径/名字过期。
- session dirty/snapshot/compile helper 在 `MainWindow.EditorSessionState.cs`；保持这里集中，不要把多窗口状态路径重新散回 `MainWindow.xaml.cs`。
- `MainWindow.GraphInputHandlers.cs` 放画布/节点/pin/节点菜单输入，`MainWindow.AssetCommands.cs` 放工具栏资产命令，`MainWindow.ContentBrowserCommands.cs` 放内容浏览器基础 CRUD / 刷新，`MainWindow.InspectorHandlers.cs` 放属性面板事件转发，`MainWindow.LogAndImportHandlers.cs` 放日志/导入入口，`MainWindow.WindowLifecycle.cs` 放关闭流程，`MainWindow.VisualTreeHelpers.cs` 放 visual/focus tree helper；不要回填到 `MainWindow.xaml.cs`。
- `EditorSurfaceControl` 事件直接进入 `EditorSurfaceContext.HandleEvent(...)`，再通过 typed `EditorSurfaceEvent` 激活 session 并复用剩余 `MainWindow` handler；不要恢复字符串反射 `RouteEditorSurfaceEvent`。

### 2026-06-09: ToDo 持久化 / Log 复制 / 内容浏览器导航

- 编译、保存、运行前必须先应用属性面板并 snapshot 打开的 sessions，保证多窗口里的 VM 内容写入对应 `GraphFileModel` / `ContentAssetViewModel`。
- `InspectorController.ToDoTargetSelected()` 选择目标后要立即写 `TargetNodeTitle`、`TargetNodeNumber`、`TargetNodeId`，刷新描述、标脏并快照 active graph。不要等保存/编译时才读 UI。
- `GraphCompileService.EnsureGraphToDoTargets()` 会用有效 `TargetNodeId` 回填旧数据缺失的 title/number；`target_title` / `target_number` 有输入连线时跳过静态目标必填，但静态下拉值仍要保留。
- 日志面板是只读 `RichTextBox`。全局快捷键必须对 `TextBoxBase` 放行；`LogPanelController` 显式绑定 `ApplicationCommands.Copy` / `SelectAll`，避免 `Ctrl+C` 被节点复制截获。日志级别颜色统一走 `LoggingModule.GetLevelBrush(...)`。
- `Logger.Write(...)` 会合并 UI dispatch，并通过 `RangeObservableCollection.AddRange(...)` 把 pending entries 作为一次 collection add flush 到 `Logger.Entries`。主日志面板和 `LogWindow` 对新增日志做增量追加；只有切过滤器、Reset/Clear 时才全量 `Refresh()`。不要改回每条日志单独 `Dispatcher.InvokeAsync`、逐条 `Entries.Add(...)` 或重建整个 `FlowDocument`。
- 内容浏览器递归模糊搜索已实现，入口在 `MainWindow.NavigationFeatures.cs` 动态安装到 `ContentBrowserHeaderBar`。搜索范围是当前目录及子目录，支持空格关键字、路径片段、不区分大小写和 subsequence 模糊匹配。
- 内容浏览器刷新和搜索要复用 `ContentBrowserIndex` 的 `assetById` / children lookup / path cache / search entries；基础 CRUD 和目录投影放 `MainWindow.ContentBrowserCommands.cs`，多选扩展放 `MainWindow.ContentBrowserMultiSelect.cs`。不要在每个文件夹、每个搜索结果里反复 `ContentBrowserItems.FirstOrDefault/Any` 全表扫描。
- `Ctrl+B` 定位已实现：选中搜索结果/资产时清空搜索并进入真实父目录；无浏览器选中项时定位当前打开资产。
- `FunctionCallNodeViewModel` 双击跳转已实现：按 stable `FunctionId` 找到目标资产和图，打开资产后加载对应函数编辑面板。
- 内容浏览器多选、框选、资产 Ctrl+C/Ctrl+V、拖拽预览、多删除在 `MainWindow.ContentBrowserMultiSelect.cs`；主题弹窗替换在 `MainWindow.ThemedDialogOverrides.cs`。

### 2026-06-08: ToDo 跳转与可复用节点编号

- 非 `Reroute` 节点有可见 `NodeNumber`：事件图 `N###`、函数图 `Fun###`。`GraphEditorService` 分配当前图最小空闲编号，删除节点会释放编号。
- `ToDoNodeViewModel` 用 `TargetNodeTitle + TargetNodeNumber` 双键在当前图内跳转；`TargetNodeId` 只用于编辑器维护引用，目标改名/改号时自动同步字段。
- `ReturnAfterTarget=false` 是 Goto，不走 `ToDo.exec_out`；`true` 会先执行目标链，结束后再走 `ToDo.exec_out`。
- Return 模式目标链如果自然回到源 ToDo，运行时在执行源 ToDo 前停止子链并返回源 `exec_out`；这不是递归错误。直接跳转自身仍然非法。
- ToDo 详情面板有搜索框和结果列表，按节点名或编号过滤；选择项会填入节点名与编号。
- Runtime/validation 必须拒绝空目标、不存在目标、重复目标和直接自跳。编号被删除后可复用，但 ToDo 仍要求节点名也匹配，不能只按编号跳。

### 2026-06-08: 本地文档 / CodeGraph / 连线渲染现状

- 当前项目 skill 源文件是 `AutomationStudio/Agent/skills/automation-studio-wpf/SKILL.md`；旧 `.kimi/skills/automation-studio-wpf/SKILL.md` 已删除，不要恢复。
- CodeGraph 数据库在 `.codegraph/codegraph.db`，`.codegraph/.gitignore` 负责忽略 db/wal/shm/cache/log/dirty 等本机文件。
- 可见连线绑定 `GraphEditorService.ConnectionPaths`；持久化和运行时仍使用 `GraphEditorService.Connections`。
- `ConnectionPathViewModel` 只负责把线性 reroute 链聚合成一条可见路径，链顺序来自真实 `Connections` 拓扑，不按点距离重排。
- 当前可见路径几何由 `ConnectionSplinePlanner` 生成；单段连接是一条 cubic Bezier，多 reroute 链使用按相邻点距离缩放的分段 spline handle。
- 紧凑或反向 reroute 布局当前不复现旧绕圈问题；没有新明确复现前不要重写 `ConnectionSplinePlanner` 线形。本地 smoke 可辅助验证，但不提交。
- `ConnectionPathViewModel.FindNearestConnection` 用可见 Bezier 曲线采样映射 backing `ConnectionViewModel`，不要退回只按端点直线命中。
- `GraphEditorService.RunBatchedEdit(...)` 批量连接变更；批量内只标记 `ConnectionPaths` 脏和 `GraphChanged` pending，最外层退出时统一 flush。
- 运行时查找走 `GraphExecutionPlan` 的 internal lazy `GraphExecutionIndex`；不要改 graph JSON 或 `GraphExecutionPlan(nodes, connections)` 构造形状。
- `ConnectionSettings` / `SplineTangentCalculator` 当前存在但不被 XAML 绑定的可见连线路径调用，除非接线到 `ConnectionSplinePlanner`，否则改它们不会改变画布线形。
- `NodeRegistry.CreateDefaultDefinitions()` 当前有 39 个定义；`NodeKind.Comment` 是历史残留枚举，旧 `comment` 文件节点由 `NodeSerializer` 跳过。

### 2026-06-05: 内容浏览器 UE 风格交互与验证门禁

- 内容浏览器顶部不再放新增按钮；新增资产统一走右侧空白右键菜单，菜单项只保留 `脚本 / 文件夹 / 函数库`。宏库和宏图已移除，不要恢复。
- 函数库内函数默认不出现在其他脚本节点搜索里；只有勾选 `公开到库` (`GraphListItemViewModel.IsPublicToLibrary`) 才显示。`CallableGraphResolver` 是节点菜单、编译同步、运行时查找的统一来源；未公开库项被其他脚本引用时编译失败并保留 dirty。
- 编译错误路径必须打印内容浏览器完整路径：`content/父文件夹/.../资产/图`。函数库在嵌套文件夹里报错时也不能只显示 `资产/图`。
- 自定义事件只属于当前脚本事件图：`CustomEvent` 是入口，`CustomEventCall` 是调用，二者用 `CustomEventId` 绑定。节点菜单只在事件图显示 `本脚本事件`；运行时执行事件链后回到调用节点 `exec_out`，递归用 `custom_event:{id}` 拦截。
- 连线交互：从输入或输出引脚拖到空白画布并抬起，会打开节点菜单；创建节点后自动连接第一个兼容的相反方向引脚。引脚释放判定使用放大半径，不只依赖精确 hit test。
- 参数默认值：`GraphParameterDefinition.DefaultValue` 必须随保存/加载/编译同步/运行时模型传递。函数、自定义事件入口参数未由调用节点输入时使用默认值；函数返回参数未接输入时也使用默认值。
- 右键空白显示新增菜单；右键资产只显示 `重命名 / 删除`。同一个 `ContextMenu` 通过 `ContentBrowserContextMenu_Opened` 切换可见项，避免把带事件的菜单塞进 `ListBoxItem.Style Setter`，那会导致 WPF 启动期 `IStyleConnector` 类型转换崩溃。
- 文件夹树：单击文件夹行进入该文件夹；点击箭头按钮只展开/收起，不进入。`HasFolderChildren` 只统计子文件夹，不因脚本/库资产显示箭头。
- 文件夹树缩进用 `TreeIndent` 像素绑定，不用 `TreeDisplayName` 前置空格。箭头图标样式为 `ContentFolderToggleIconStyle`：收起朝右，展开朝下；默认 `Path.Data` 必须放在 `Style Setter`，不要写本地 `Data`，否则 `DataTrigger` 覆盖不了。
- 内容浏览器左树和右瓦片之间有 `ContentBrowserTreeSplitter`，左树列 `ContentTreeColumn` 默认 180，范围 120-420；右侧瓦片列最小 240，并继续用禁横向滚动的 `WrapPanel` 自动换行。
- 默认验证门禁：`dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck`、`git diff --check`、`dotnet run --project .\AutomationStudioWpf.csproj` 启动崩溃探测、`codegraph.cmd sync`。启动检查不固定等待 20 秒；能正常创建窗口即可。若进程快速退出、输出 `Unhandled exception` 或 WPF 初始化异常，必须收集终端输出/异常栈并先修。
- `Tests/CodexSmoke` 是本地-only 回归辅助，Git 不跟踪、不提交。只有改到对应高风险交互或用户明确要求时才本地运行 broad smoke / optional reroute repro smoke。

### 2026-06-05: 事件图 / 函数画布串图

- 现象：新增事件图、函数后，切换时画布内容串到一起。
- 根因：`GraphListController.Load()` 内会触发持久化；如果加载前 session 的 active controller 仍指向旧 controller，`PersistAssetLibrary()` 或后续 snapshot/compile 会把画布快照写回错图，导致事件图/函数模型互相污染，或函数库切回后变成默认 entry/return。
- 正确顺序：`SnapshotActiveAsset()` -> `SetSessionActiveGraphController(session, targetController, remember: false)` -> `targetController.LoadItem(item, snapshotCurrent: false)` -> `SetSessionActiveGraphController(session, targetController)`。
- 列表导航规则：事件图、函数列表项单击即激活并切换画布；双击只是兼容。右键菜单也要先激活目标项，再做重命名/删除。
- 测试要求：切换 event/function 后检查当前画布节点类型，也检查各自 `GraphFileModel.Nodes` 没有被其它图类型覆盖。
- UI 默认：底部内容浏览器和日志列 50/50，底部行默认 360 高度，避免每次手动拉大内容浏览器。

### 2026-06-03: Event graphs + custom functions v1

- `GraphLibraryState` now has event graphs (`Graphs` legacy field) and `Functions`. Old saved `Graphs` are treated as event graphs.
- Function assets use `FunctionEntry` + `FunctionReturn`.
- Function parameter pin names use stable parameter IDs. User-visible parameter names are display labels only.
- Parameter type mapping v1: `Boolean -> Boolean`, `Vector2D -> Vector2D`, all other parameter types (`Float`, `Vector3D`, `Vector4D`, `ImageAsset`, `String`) map to `String` pins.
- Function calls are synchronous and return through `FunctionReturn`.
- Runtime recursion is blocked by call stack keys (`function:{id}`).
- Only event graphs should be executed directly. Functions are edited as assets and called from event graphs.

### 2026-06-03: Stage-5 common nodes cleaned and optimized

#### Node expansion pattern
- Kept common nodes: `MouseDoubleClick`, `GetMousePosition`, `KeyChord`, `WaitImage`, `WaitImageDisappear`, `Compare`, `BooleanAnd`, `BooleanOr`, `BooleanNot`, `StringConcat`, `WaitWindow`, `CloseWindow`, `WindowExists`, `GetForegroundWindow`, `SaveScreenshot`, `ShowMessage`.
- Removed weak nodes: `MouseDrag`, `InputText`, `KeySequence`, `ClickImageCenter`, `SetVariable`, `Comment`.
- Old graph files containing removed node type keys should skip those nodes with a Warn and ignore broken connections.
- Small nodes use `CommonNodeViewModel` plus a shared generic inspector panel to avoid bloating `InspectorController` with one panel per tiny node.
- Every node still has an explicit `NodeKind` and `NodeRegistry` definition; execution is centralized in `Nodes/Common/CommonNodeExecutors.cs`.
- If a common node becomes complex later, split it into a dedicated ViewModel/Inspector/Executor.

#### Adapter additions
- `IMouseAdapter`: `DoubleClick`, `GetPosition`.
- `IKeyboardAdapter`: `ExecuteChord`.
- `IWindowAdapter`: wait/close/exists/foreground-window APIs.
- `IScreenshotAdapter`: screenshot save API.

#### UI/runtime behavior
- `KeyChord` inspector uses an add-key ComboBox plus an editable chord preview (`Ctrl+C`, `Ctrl+Shift+Esc`, etc.); runtime uses the preview text.
- `WaitImage` has `image_path` input and output. `FindImage` has `image_path` input. Input path wins over local property.
- Image source pins are mode-sensitive: when source mode is `RealtimeScreenshot`, hide/remove `source_image_path`; when `ManualImage`, show it. Switching back to realtime must clear old `source_image_path` connections.
- `SaveScreenshot` uses `Text2` as save mode: `Auto` by default, `Manual` for user path. Auto saves to `AppContext.BaseDirectory/Temp/Screenshots/screenshot_{nodeId}_{timestamp}.png`. The node exposes only one data output: `image_path`.
- Mode fields must be enum ComboBox controls, never free-text mode names. This includes screenshot save mode and image search source mode. If one enum choice makes fields irrelevant, hide those fields instead of disabling them.
- `WaitWindow`/`CloseWindow`/`WindowExists` support manual process name, running-window dropdown, and browsing an exe to derive process name.
- `WaitImage`/`WaitImageDisappear`/`WaitWindow`: timeout `0` means no timeout; negative values fall back to defaults. Long-running waits must log each polling attempt.
- Editor mode must not trigger runtime-heavy logic: no Python checks, no node execution, no process/window enumeration while selecting or dragging. Window/process lists are refreshed only by explicit refresh buttons.
- Any field backed by a connected input pin must show `前置输入`, be disabled, and use gray foreground/background/border.

#### Important pitfalls
- Runtime popup nodes must call `Application.Current.Dispatcher.Invoke`.
- Image wait/disappear reuse `Python/find_image.py`; do not reintroduce OCR/EasyOCR.

### 2026-06-03: Inspector 下沉 + 找图区块识别 + Validator 增强

#### Runtime model cleanup: 不再用别的节点字段存当前节点语义
- `GraphRuntimeNode` now has explicit fields:
  - `ProgramPath`
  - `WaitTimeoutMs`
  - `PrintLogMessage`
  - `WhileLoopMode`
  - `MaxIterations`
- **Rule**: 不要让新 runtime 节点复用 `ImagePath`、`DelayMs`、`ScrollSpeed` 等不相关字段。旧文件兼容读取可以留在 `NodeSerializer.FromFileModel()`，新 runtime 数据必须语义明确。

#### Architecture update: InspectorController 负责完整属性面板逻辑
- **Current**: `Interaction/InspectorController*.cs` 负责节点属性加载、字段自动保存、浏览文件、窗口列表刷新、前置输入锁定和灰态；参数面板在 `InspectorController.Parameters.cs`，通用小节点在 `InspectorController.CommonNodes.cs`，找图/窗口/程序/键盘辅助在 `InspectorController.SystemNodes.cs`，字段锁定在 `InspectorController.Locks.cs`，ToDo 目标选择入口在 `InspectorController.ToDo.cs`。
- **MainWindow rule**: `MainWindow.xaml.cs` 只转发 XAML 事件：`LoadNodeToInspector()`、`ApplyInspectorChanges()`、浏览按钮、窗口模式切换都应调用 controller。
- **Do not**: 不要再把节点属性 switch、字段锁定规则、文件浏览逻辑写回 `MainWindow.xaml.cs`。

#### Feature: 找图节点支持可选区域
- **Fields**:
  - `FindImageNodeViewModel.UseRegion`
  - `RegionX / RegionY / RegionWidth / RegionHeight`
  - File/runtime 字段使用 `UseFindImageRegion`、`FindImageRegionX/Y/Width/Height`
- **Runtime**: `FindImageNodeExecutor` 通过 JSON 传给 `Python/find_image.py`。
- **Python behavior**: 全屏截图后可选 crop；模板匹配结果输出仍为屏幕绝对坐标。
- **Safety**: 区域启用但宽高无效是 `WarnButContinue`，不是 fatal。

#### Validator update: 执行前新增非致命警告
- `GraphValidator` 现在检查不可达执行节点、缺省路径/坐标/按键/进程名、无效延迟、无效找图区。
- 这些都是 Warning；结构性错误才阻止执行。
- `GraphValidator` 还会把同一执行输出多条线、同一数据输入多条线判为 Error。UI 创建线时会替换旧线，但坏 JSON/旧图加载可能绕过规则。

### 2026-06-03: XAML 初始化期间事件早触发导致启动崩溃

#### Problem: `FilterRadio_Checked` 启动时空引用
- **Symptom**: 启动直接崩溃：`NullReferenceException` at `MainWindow.FilterRadio_Checked`。堆栈显示发生在 `InitializeComponent()` 内，RadioButton `IsChecked` 被 XAML 设置时触发 `Checked`。
- **Root cause**: `MainWindow` 构造顺序是 `InitializeComponent()` → `InitializeControllers()`。XAML 加载期间控件事件可能先触发，此时 `_logPanelController` 还没创建。
- **Fix**: 事件入口先判空：`if (_logPanelController is null) return;`
- **Rule**: 所有 XAML 初始化期可能触发的事件，如果依赖 controller/service，必须容忍 controller 为空；或者改成 `InitializeComponent()` 后再动态绑定事件。
- **Applies to**: RadioButton `Checked`、ComboBox `SelectionChanged`、TextBox `TextChanged`、Loaded/LayoutUpdated 等初始化期事件。

### 2026-06-02: EdgePan 边缘自动平移（新增功能）

#### Feature: 拖动节点/连线到视口边界时画布自动滚动
- **Implementation**: `CanvasPanZoomController.EdgePan()` 参考 UE4 `SNodePanel::ComputeEdgePanAmount`
- **Algorithm**: 30px 边界宽容区，非线性加速 `0.15 * distance^0.6`，最大 5px/tick，除以缩放系数
- **Trigger**: `GraphViewport_PreviewMouseMove` 中检测 `_dragNode is not null`（节点拖动）或 `PinConnectionController.IsConnecting`（连线拖拽）
- **Direction note**: `_panTransform` 是屏幕空间（ScaleTransform 之后），与 UE4 `ViewOffset`（图空间）语义相反 → 使用 `-=` 而非 `+=`

### 2026-06-02: Runtime/Adapter/Interaction 解耦重构

#### New architecture: Runtime 只调度，具体能力下沉
- **Runtime**:
  - `Runtime/GraphRuntimeExecutor.cs` 只保留执行链调度、分支/循环、取消、环路步数保护、调用节点执行器。
  - `Runtime/RuntimeContext.cs` 统一保存节点输出，统一解析前置输入。
  - `NodeExecutionResult` 用 `Success / WarnButContinue / FatalStop` 区分“可继续警告”和“致命停止”。
- **Adapters**:
  - 鼠标：`IMouseAdapter` / `Win32MouseAdapter`
  - 键盘：`IKeyboardAdapter` / `Win32KeyboardAdapter`
  - 窗口：`IWindowAdapter` / `Win32WindowAdapter`
  - 进程：`IProcessAdapter` / `ProcessAdapter`
  - Python：`IPythonScriptAdapter` / `PythonScriptAdapter`
- **Nodes**:
  - 节点执行器按分类放入 `Nodes/Core`、`Nodes/Input`、`Nodes/System`、`Nodes/Plugins`、`Nodes/Debug`。
  - 新增节点执行逻辑时优先写对应 `INodeExecutor`，不要再往 `GraphRuntimeExecutor` 塞 switch 分支。
- **Interaction**:
  - `ExecutionController`：执行、取消、校验、Python 环境检查。
  - `GraphListController`：图谱新增、切换、删除、重命名、保存、退出保存提示。
  - `CanvasPanZoomController`：右键平移、滚轮缩放、F 全览、坐标转换。
  - `NodeDragSelectionController`：节点拖动、框选、多选、复制粘贴、对齐。
  - `PinConnectionController`：拖线、连线、断线、预览线、双击连线插入路由节点。
  - `InspectorController`：属性面板加载/保存主分发；参数、通用节点、系统节点辅助、字段锁定、ToDo 面板已拆到 partial。
  - `NodePaletteController`：右键节点菜单，条目来自 `NodeRegistry.Definitions`。
  - `LogPanelController`：日志过滤、增量刷新、清空。
  - `GraphImportDropController`：JSON 图谱拖拽导入。
- **Current OCR status**:
  - 当前软件不包含识字/OCR 节点。
  - 当前软件不依赖 EasyOCR，也不做 EasyOCR 自动安装。

#### Lesson: 前置输入已连接但运行时无值，不能回退本地默认值
- **Risk**: `找图.center -> 鼠标点击.position` 时，如果找图未命中且鼠标节点回退本地坐标，可能误点 `(0,0)` 或旧坐标。
- **Rule**:
  - 未连接输入：允许使用本地属性。
  - 已连接输入但上游无值：当前节点 `WarnButContinue`，跳过本节点，不回退本地属性。
- **Applies to**: 鼠标点击、鼠标移动、打印日志消息输入、选中窗口进程名输入。

#### Lesson: NodeRegistry 是节点菜单和执行器注册的单一入口
- `NodeRegistry.Definitions` 提供节点名称、分类、引脚定义。
- `NodePaletteController` 从 registry 生成菜单，不要在 `MainWindow` 手写节点列表。
- `NodeFactory.CreateNode(NodeKind, x, y)` 负责按 `NodeKind` 创建 ViewModel。

#### Lesson: WPF + WinForms 项目新增 controller 时要避免命名空间歧义
- 项目启用 WinForms，新增 WPF controller 时常见歧义：
  - `Button`
  - `TextBox`
  - `ListBox`
  - `MessageBox`
  - `MouseEventArgs`
  - `KeyEventArgs`
  - `Control`
  - `Brushes`
  - `Color`
  - `Cursors`
- **Fix**: 在 controller 文件里使用别名或完整限定名，例如：
  - `using WpfTextBox = System.Windows.Controls.TextBox;`
  - `using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;`
  - `System.Windows.MessageBox.Show(...)`

#### UI change: 工具栏删除“删除所选节点 / 清除待连接引脚”
- 顶部工具栏只保留图谱文件操作和执行图谱。
- 删除节点仍走键盘 `Delete`。
- 清除待连接引脚仍走 `Esc` 取消连线。
- 删除按钮时必须同时移除 XAML `Click` handler 和 `MainWindow.xaml.cs` 对应方法，否则 WPF 编译会因事件找不到失败。

#### Current verified state
- `dotnet build .\AutomationStudioWpf.csproj` 通过：0 warning / 0 error。
- CodeGraph 已同步，`codegraph.cmd sync` 正常。
- PowerShell 执行策略可能拦截 `codegraph.ps1`，用 `codegraph.cmd sync`。

### 2026-05-30: 键盘输入在游戏窗口无效 + SendInput 结构体布局

#### Problem: 键盘节点在普通应用正常，游戏窗口完全无效
- **Symptom**: Space/A 等按键在浏览器/记事本正常触发，在游戏窗口毫无作用，但手动按键盘正常
- **Root cause (3 layers)**:
  1. **API 废弃**: 原代码使用 `keybd_event`（Win95 API），现代 Windows 上不可靠，改为 `SendInput`
  2. **结构体布局错误**: `INPUT` 含 union，C# `LayoutKind.Sequential` 在 x64 上对 `ki` 加了 8 字节对齐填充，导致 `SendInput` 读到错位内存。`sizeof(INPUT)` = 40 bytes（x64），`ki` 在 offset 8（非 offset 4）。修复：`LayoutKind.Explicit, Size = 40` + `FieldOffset(8)` + `FieldOffset(0)`
  3. **虚拟键码 vs 扫描码**: `SendInput` 默认用虚拟键码（wVk），但游戏使用 DirectInput/RawInput 直接从硬件读扫描码。修复：`MapVirtualKey(vkCode, 0)` 转换 → `wVk = 0, wScan = scanCode, dwFlags = KEYEVENTF_SCANCODE`
- **Lesson**:
  - `keybd_event` 已废弃，永远使用 `SendInput`
  - Win32 union 结构体必须用 `LayoutKind.Explicit` + `FieldOffset`，不能用 `Sequential`
  - x64 上 `sizeof(INPUT) = 40`，`Marshal.SizeOf` 必须返回 40，否则 `SendInput` 静默失败
  - 游戏输入模拟 = `SendInput` + 扫描码模式 + 扩展键标志（`KEYEVENTF_EXTENDEDKEY`）
  - 扩展键（方向键/Insert/Delete/Home/End/PgUp/PgDn/Numpad/WinKey/Apps）必须有 `KEYEVENTF_EXTENDEDKEY`

### 2026-05-30: C#↔Python 中文传参编码 + JSON BOM + 循环检测

#### Problem 1: 找图节点传中文文件路径到 Python 后变成乱码
- **Symptom**: `find_image.py` 收到 `'征神之路.png'` → `'寰佺涔嬭矾.png'`
- **Root cause**: C# `ProcessStartInfo.Arguments` 通过 Windows 命令行传参，中文被系统编码（GBK/ACP）破坏
- **Fix**: 改用 JSON 临时文件传参。C# 端 `File.WriteAllText(path, JsonSerializer.Serialize(data), new UTF8Encoding(false))`；Python 端 `json.load(open(path, encoding="utf-8"))`
- **Lesson**: **永远不要通过命令行参数传递中文**（或任何非 ASCII 文本）。C#→Python 通信统一用 JSON 临时文件 + UTF-8 without BOM

#### Problem 2: JSON 文件被 Python `json.load` 报 `Unexpected UTF-8 BOM`
- **Symptom**: C# 写的 JSON 文件 Python 读报错 `JSONDecodeError: Unexpected UTF-8 BOM`
- **Root cause**: `System.Text.Encoding.UTF8` 默认在文件头写入 BOM（`EF BB BF`），Python `json.load` 不接受 BOM
- **Fix**: 使用 `new System.Text.UTF8Encoding(false)` —— `false` 表示不写 BOM
- **Lesson**: 跨语言 JSON 交换时，C# 永远用 `new UTF8Encoding(false)`，不要用 `Encoding.UTF8`

#### Problem 3: 环路检测阻止循环体内节点重复执行
- **Symptom**: 找图节点放在 For/While 循环内，执行报"检测到执行环路"
- **Root cause**: `ExecuteChain` 用 `visitedNodes` HashSet 阻止同一节点被访问两次，但循环体每次迭代都应重新执行
- **Fix**: 移除 `visitedNodes` 检查。For/While 循环每次迭代创建新的执行上下文，不会无限递归。真有死循环用户用停止按钮取消
- **Lesson**: 环路检测不应阻止循环体内的合法重复执行。循环节点（ForLoop/WhileLoop）本身已有迭代次数和退出条件来保证终止

#### Problem 4: 执行图谱前 Python 环境检查卡顿
- **Symptom**: 每次点击执行都要等好几秒才开始跑
- **Root cause**: `EnsurePythonAsync` 每次同步检查 `cv2` / `PIL` / `numpy`，且历史实现没有缓存；超时 import 进程也可能残留
- **Fix**: 首次检查在后台线程执行并缓存 `PythonEnvironmentResult`，后续执行复用缓存；并发检查通过 `SemaphoreSlim` 合并；缺环境提示同一进程只弹一次；超时 probe 会 kill 进程树
- **Lesson**: 环境检测只做一次。当前是在首次执行前检查，不是 App 启动时自动检查

#### Problem 5: 新增图谱时旧图谱节点丢失
- **Symptom**: 编辑图表1→保存→新增图表2→编辑→保存→退出→重启→图表1节点全部丢失
- **Root cause**: `AddGraphListItem` 先调 `CreateDefaultGraphModel`（内部 `NewGraph()` 清空编辑器），再在 `LoadGraphListItem` 里 `SnapshotActiveGraph()`，结果把空画布覆盖到旧图谱上
- **Fix**: `AddGraphListItem` 开头先 `SnapshotActiveGraph()` 存档当前图谱，再创建新图谱
- **Lesson**: 切换/新增图谱前必须先快照当前编辑器状态（`ExportGraphModel`），顺序反了数据就丢了

### 2026-05-29: 图谱列表、右键菜单、选中窗口节点

#### Problem: 切换图谱时不应弹保存提示
- **Symptom**: 双击图谱切换时触发 `ConfirmSaveCurrentGraphIfDirty()`，频繁打断编辑流
- **Fix**: 切换图谱只调用 `SnapshotActiveGraph()` 静默把当前编辑器状态写回当前列表项，不弹窗；只在 `Window_Closing` 时统一提示是否保存
- **Lesson**: 图谱列表切换是编辑导航，不是文件关闭。保存确认只能放在退出或用户显式保存动作上

#### Problem: WPF ContextMenu 出现默认白边/白色 chrome
- **Symptom**: 图谱列表右键菜单周围出现莫名白边，与 Rider/UE 蓝图暗色 UI 不一致
- **Root cause**: `ContextMenu` / `MenuItem` 默认 ControlTemplate 仍在生效，仅设置 `Background/BorderBrush` 不会完全覆盖系统 chrome
- **Fix**: 给 `ContextMenu.Template` 和 `MenuItem.Template` 全量自绘；`ContextMenu` 用暗色 `Border + DropShadowEffect + StackPanel IsItemsHost`；`MenuItem` 用自定义 `Border/Grid/ContentPresenter`，hover 只改自绘背景
- **Lesson**: WPF 菜单要做 UE 风格暗色外观，必须覆盖 Template。只改属性会残留默认主题边框

#### Problem: ContextMenu 不支持 Popup 的部分属性
- **Symptom**: 给 `ContextMenu` 写 `AllowsTransparency` / `PopupAnimation` 编译报 `MC3072`
- **Root cause**: 这些属性属于 `Popup`，不是 `ContextMenu` 公开属性
- **Fix**: 移除这些属性；需要透明/动画时改用真正 `Popup` 或自绘 Canvas 浮层
- **Lesson**: `ContextMenu` 不是 `Popup`，不能混用 Popup 属性

#### New node: 选中窗口
- **Files**: `Graph/SelectWindowNodeViewModel.cs`、`Graph/GraphTypes.cs`、`Services/NodeFactory.cs`、`Services/NodeSerializer.cs`、`Runtime/GraphRuntimeExecutor.cs`
- **Pins**: `exec_in` / `exec_out`，`process_name` string 输入，`process_name` string 输出，`result` bool 输出
- **Runtime**: `Process.GetProcessesByName()` 找主窗口，`ShowWindow(SW_RESTORE)` 恢复，`SetWindowPos(HWND_TOP)` 置前，`SetForegroundWindow()` 尝试设为前台
- **Behavior**: 进程名为空或未找到窗口记 `Warn + continue`，Win32 调用异常才 `Error + stop`
- **Lesson**: Windows 可能因前台窗口权限限制导致 `SetForegroundWindow` 返回 false；这不是崩溃，按可退化结果处理

#### Build note: 沙箱构建可能误报 obj 文件 Access denied
- **Symptom**: `dotnet build` 在沙箱内报 `Access to ... obj\Debug\net8.0-windows\App.g.cs is denied`
- **Fix**: 使用本机正常权限执行 `dotnet build .\AutomationStudioWpf.csproj`
- **Lesson**: WPF MarkupCompile 会删除/重写 `obj` 生成文件；若沙箱或 Rider/dotnet 占用，先查进程，必要时用非沙箱构建验证代码

### 2026-05-29: 项目拓展后运行时风险审计

#### Problem: 找图未命中后，下游鼠标节点可能回退到本地坐标继续点击
- **Symptom**: `找图.center -> 鼠标点击.position` 已连接时，如果找图未命中，只写入 `result=false`，不会写入 `center`；鼠标节点因为看到 `position` 已连接，会跳过本地坐标有效性检查，最后回退到 `PositionX/Y`
- **Risk**: 可能点击 `(0,0)` 或旧坐标，属于自动化执行高风险行为
- **Fix target**: `ResolveMouseTargetPoint()` 不应在“输入 pin 已连接但运行时值缺失”时回退到本地坐标；应 `Warn + skip + continue`

#### Problem: 新增节点复用旧 DTO 字段，保存模型开始漂移
- **Symptom**: `StartProgram.ProgramPath` 写入 `NodeFileModel.ImagePath`，`WaitTimeoutMs` 写入 `DelayMs`，`RetryCount` 写入 `ScrollSpeed`；`PrintLog.Message` 也写入 `ImagePath`
- **Risk**: 文件语义越来越难维护，后续节点字段容易互相污染
- **Fix target**: `NodeFileModel` 增加明确字段：`ProgramPath`、`WaitTimeoutMs`、`RetryCount`、`PrintLogMessage`；旧字段只保留兼容读取

#### Problem: 打开旧/坏图谱时不补默认开始节点
- **Symptom**: `NewGraph()` 会创建开始节点，但 `LoadFromModel()` 只按文件内容加载；文件缺少 `start` 时 UI 可打开，执行时才失败
- **Fix target**: `LoadFromModel()` 加载后若无 `StartNodeViewModel`，自动补一个开始节点，保持单入口约束

#### Current verified state
- `dotnet build AutomationStudioWpf.csproj` 当前通过：0 warning / 0 error
- CodeGraph 当前可用；本轮 `codegraph.cmd sync` 已成功。若 PowerShell 策略拦截 `.ps1`，继续优先使用 `codegraph.cmd sync`。
- `.git/index.lock` 存在，后续提交前需要确认无 Git/Rider 进程占用后清理

### 2026-05-29: ComboBox disabled text not gray unlike TextBox

#### Problem: If/While condition ComboBox shows "前置输入" when locked but text remains black instead of gray
- **Symptom**: MouseClick/MouseMove TextBox correctly turns gray when input pin is connected, but If/While ComboBox stays black
- **Root cause**: WPF default template behavior is inconsistent across controls:
  - `TextBox.IsEnabled = false` automatically renders text in gray via `GrayTextBrushKey`
  - `ComboBox.IsEnabled = false` only changes border/background in Disabled visual state; the `ContentPresenter` Foreground is NOT automatically modified
- **Fix**: Manually set `cb.Foreground = Brushes.Gray` when locked, and `cb.ClearValue(Control.ForegroundProperty)` when unlocked in `LockConditionCombo()`
- **Lesson**: Do not assume all WPF controls have consistent disabled visual states. ComboBox requires manual Foreground management for grayed-out text.

### 2026-05-29: Preview event tunneling blocks Button Click inside popup

#### Problem: Clicking a node in the right-click palette does nothing - no node created, palette not closed
- **Symptom**: Node palette opens on right-click, but clicking a node inside it has no effect
- **Root cause**: `GraphViewport_PreviewMouseLeftButtonDown` is a Preview (tunneling) event, which fires *before* the Button Click event. `IsGraphBlankSource` walks up the visual tree and hits `GraphViewport`, returning true. The handler then sets `e.Handled = true`, which swallows the event and prevents the Button Click from ever firing.
- **Fix**: Add an early-exit check at the top of `GraphViewport_PreviewMouseLeftButtonDown`: if the palette is visible and the click position is inside the palette bounds, return immediately without handling the event.
- **Lesson**: Preview (tunneling) events on a parent container will intercept child element interactions unless explicitly guarded. Always check if the event target is inside a floating panel before handling it at the container level.

### 2026-05-29：中文乱码批量修复

#### 问题：项目文件中出现大量中文乱码
- **现象**：注释、region 标签、MessageBox 标题、SetStatus 文本全部变成乱码
- **根因（两层）**：
  1. **历史遗留**：项目早期在 GBK 编码环境下编写，后转为 UTF-8 但未重新编码，导致原有中文被错误解码为乱码字符
  2. **工具边界**：`StrReplaceFile` 在替换包含中文的多行字符串时，如果 `old` 字符串跨越了中文字符的 UTF-8 字节边界，会破坏相邻字符的编码，产生新的乱码
- **修复**：用 Python 脚本扫描所有含中文的行，建立 `garbled -> correct` 映射表批量替换；单行乱码直接按行号覆写
- **教训**：
  - **绝不**在 `StrReplaceFile` 的 `old/new` 参数中使用中文——改用英文代码定位或行号覆写
  - 新增中文文本优先用 `WriteFile` 完整写入新文件，或写入独立资源文件
  - 每次修改后运行检测脚本验证中文完整性

**检测脚本（保存备用）**：
```python
with open('File.cs', 'r', encoding='utf-8') as f:
    for i, line in enumerate(f, 1):
        if any(0x4e00 <= ord(c) <= 0x9fff for c in line):
            # 检查是否包含常见乱码字符
            garbled = set(['鏈','鍒','鎷','鐘','妭','瀹','垯','粙','妸','墽','鏉'])
            if any(c in line for c in garbled):
                print(f'Line {i}: {line.strip()}')
```

### 2026-05-29：右键节点菜单替代左侧工具箱

#### 问题 1：右键菜单与画布平移的冲突
- **现象**：右键点击画布需要同时支持两种行为——弹出菜单（点击）和平移画布（拖动）
- **根因**：右键按下时无法立即判断用户意图是点击还是拖动
- **修复**：采用"延迟判断"策略——右键按下仅记录起始位置，在 MouseMove 中检测移动距离是否超过阈值（3px），超过则转为平移；未超过则在 MouseUp 时弹出菜单
- **教训**：WPF 中区分点击和拖动需要在 MouseDown 时记录状态，在 MouseMove 中根据位移阈值决定行为转换

#### 问题 2：WPF `Popup` vs 自定义 `Border` 的选择
- **现象**：最初考虑用 `Popup` 实现节点菜单，但 Popup 是独立窗口层，定位和外部位检测复杂
- **修复**：使用自定义 `Border` 作为菜单容器，放在外层 `Canvas` 内（和 `GraphSurface` 同级），通过 `Canvas.Left/Top` 定位。菜单关闭通过 `Window.PreviewMouseDown` 检测点击位置是否在菜单边界内
- **教训**：需要精确定位且要与画布坐标系解耦的浮动面板，用 Canvas 内的 Border 比 Popup 更可控

#### 问题 3：`Button` 和 `HorizontalAlignment` 的命名空间歧义
- **现象**：编译报错 `Button` 是 `System.Windows.Controls.Button` 和 `System.Windows.Forms.Button` 之间的歧义引用
- **根因**：项目 `UseWindowsForms` 为 true，同时引用了 WPF 和 WinForms 的命名空间
- **修复**：在动态创建 UI 的代码中使用完整限定名：`System.Windows.Controls.Button`、`System.Windows.HorizontalAlignment.Left`
- **教训**：当项目同时引用 WPF 和 WinForms 时，任何 UI 控件都应使用完整命名空间避免歧义

### 2026-05-29：无限画布网格背景 + 事件接收边界问题

#### 问题 1：Canvas 被 RenderTransform 平移后，露出区域无法接收鼠标事件
- **现象**：画布往右拖动到边界后，左边露出黑色区域，无法右键平移或左键框选
- **根因**：`PreviewMouseLeftButtonDown` / `PreviewMouseMove` / `PreviewMouseRightButtonDown` 等事件绑定在 `Canvas` 上。当 Canvas 的 `RenderTransform` 将其移开后，事件源（Canvas）也跟着移走了，露出区域是父 Border 的背景，不接收事件
- **修复**：将 5 个 Preview 鼠标事件绑定到视口容器（`Border`）上，同时将 `CaptureMouse()` / `ReleaseMouseCapture()` 的目标从 `Canvas` 改为 `Border`
- **教训**：在 WPF 中，如果一个子元素通过 `RenderTransform` 平移，其上的鼠标事件也会跟着平移。需要把事件绑定到不会移动的容器上

#### 问题 2：画布边界露出黑色区域，没有网格背景
- **现象**：往右拖动后左边是纯黑色，没有网格线
- **根因**：网格 `DrawingBrush` 是 `Canvas.Background`，只覆盖 Canvas 内部。Canvas 被平移后露出父 Border 的纯色背景
- **修复**：将网格背景移到 `GraphViewport`（Border）上，`DrawingBrush.Transform` 分别绑定 `ScaleTransform` 和 `TranslateTransform`（不能直接绑定 `RenderTransform`，因为它不是 DependencyObject）
- **教训**：参考 UE4 蓝图编辑器的 `PaintBackgroundAsLines` 思路——网格应该覆盖整个视口并跟随变换，而不是作为 Canvas 背景

#### 问题 3：Reroute 节点连线锚点位置错误
- **现象**：路由节点的连线锚点位置不对
- **根因**：`RerouteNodeViewModel.GetPinAnchor` 用了 `new` 关键字隐藏基类方法。`ConnectionViewModel` 通过基类 `NodeBaseViewModel.GetPinAnchor()` 调用时，永远走不到子类的 `(10,10)` 逻辑
- **修复**：`NodeBaseViewModel.GetPinAnchor` 改为 `virtual`，`RerouteNodeViewModel.GetPinAnchor` 改为 `override`
- **教训**：C# 中 `new` 是编译时静态绑定，`override` 是运行时动态绑定。多态调用时 `new` 不会生效

#### 问题 4：GraphEditorService 死代码与重复 ID
- **现象**：`GraphEditorService` 维护 `_nodeSequence` 和 `CreateNodeId()`，但 `MainWindow` 实际使用的是 `NodeFactory`。`_nodeSequence = 1` 与硬编码的 `"node_001"` 冲突
- **修复**：移除 `_nodeSequence`、`CreateNodeId()` 及相关同步逻辑，ID 生成完全由 `NodeFactory` 负责
- **教训**：避免在多个地方维护同一份状态（SSOT 原则）

#### 问题 5：节点粘贴时属性丢失
- **现象**：新增字段后粘贴节点可能漏属性
- **根因**：`NodeClipboardService.PasteNodesAt` 手动逐字段复制 `NodeFileModel`
- **修复**：`JsonSerializer.Deserialize<NodeFileModel>(JsonSerializer.Serialize(source))` 深拷贝
- **教训**：扁平 DTO 的深拷贝用 JSON 序列化最可靠，新增字段自动同步

### 2026-05-28：WPF 中 Win32 API 的坐标系陷阱
- `SetCursorPos` 使用屏幕坐标（多显示器 aware）
- `mouse_event` 的 `dx/dy` 参数在 `MOUSEEVENTF_ABSOLUTE` 模式下是 0~65535 归一化坐标，但项目使用的是相对模式（dx=0, dy=0），所以只需先 `SetCursorPos` 再 `mouse_event` 即可

## 核心架构速查

```
MainWindow (View)
    ├─ GraphViewport (Border) ← 鼠标事件绑定在这里
    │   ├─ Background = DrawingBrush (网格，绑定 Transform)
    │   └─ Canvas
    │       ├─ GraphSurface (Canvas, 400000×400000)
    │       │   ├─ ItemsControl (Connections)
    │       │   ├─ PreviewConnectionPath
    │       │   └─ ItemsControl (Nodes)
    │       └─ NodePalette (Border) ← 右键菜单，Canvas.Left/Top 定位
    └─ Inspector Panel

Services
    ├─ GraphEditorService    ← 节点/Connections/ConnectionPaths、批量连接变更、保存加载、执行计划
    ├─ GraphCommandService   ← Undo/Redo 快照命令
    ├─ NodeFactory           ← ID 生成 + 节点创建
    ├─ NodeSerializer        ← ViewModel ↔ FileModel ↔ RuntimeModel
    ├─ NodeClipboardService  ← 复制粘贴（JSON 深拷贝）
    └─ PythonAutoInstaller   ← Python 环境检测

Graph (ViewModel)
    ├─ NodeBaseViewModel     ← abstract, virtual GetPinAnchor
    ├─ PinViewModel          ← Direction + Kind
    ├─ ConnectionViewModel   ← IDisposable, 监听 Node.PropertyChanged
    ├─ ConnectionPathViewModel ← 可见连线路径聚合
    ├─ ConnectionSplinePlanner ← 当前 XAML 可见连线几何
    └─ ObservableObject      ← INotifyPropertyChanged 基类

Runtime
    ├─ GraphRuntimeExecutor  ← 顺序执行，最大步数环路保护
    └─ GraphExecutionModels  ← record，扁平化运行时数据 + internal lazy GraphExecutionIndex
```

## 开发规范

1. **零外部 NuGet**：所有功能自研，保持项目轻量
2. **ID 生成唯一入口**：`NodeFactory.CreateNodeId()`
3. **序列化深拷贝**：用 `JsonSerializer` 而非手动逐字段复制
4. **虚方法优先**：需要多态时使用 `virtual`/`override`，避免 `new`
5. **事件绑定在容器**：涉及 RenderTransform 平移的元素，鼠标事件应绑在父容器
6. **网格背景在视口**：参考 UE4 PaintBackgroundAsLines，网格覆盖视口而非 Canvas
7. **浮动面板用 Canvas 定位**：需要跟随鼠标位置的面板，放在外层 Canvas 内用 Canvas.Left/Top 定位，避免 Popup 的窗口层复杂性
8. **点击 vs 拖动延迟判断**：右键同时承载菜单和平移时，用位移阈值（如 3px）在 MouseMove 中决定行为转换
9. **WinForms + WPF 混合项目用完整限定名**：`System.Windows.Controls.Button`、`System.Windows.HorizontalAlignment` 等避免歧义
10. **不在 StrReplaceFile 中使用中文**：`old`/`new` 参数仅使用 ASCII 字符；中文文本通过 WriteFile 或按行号覆写注入
### 2026-06-04: Content Browser + Script / Function Library assets

- Bottom panel now has a UE-style content browser on the left and log panel on the right, separated by a `GridSplitter`.
- Startup does not open graph editing UI by default. `EditorSurfaceHostRoot` is hidden and `EmptyEditorPanel` asks the user to open an asset from the content browser; opening an asset hosts that session's `EditorSurfaceControl`.
- New content asset kinds:
  - `Folder`: content browser placeholder/category item for now.
  - `Script`: blueprint-like executable asset with its own event graphs and private functions.
  - `FunctionLibrary`: global function library; functions can be searched/called by scripts.
- `ContentAssetViewModel` owns `EventGraphs` and `Functions`.
- `GraphLibraryService.SaveContentLibrary()` writes the new `ContentAssets` model.
- Old `graph-library.json` compatibility:
  - old `Graphs` -> default script event graphs.
  - old `Functions` -> default script private functions.
  - old `Macros` / `MacroLibrary` -> ignored; macro libraries and macro graphs are removed.
- Function call visibility:
  - scripts can call their own private functions.
  - scripts can call public functions from function libraries.
  - scripts cannot call private functions from other scripts.
- `CallableGraphItem` is the bridge DTO used by `NodePaletteController` and `ExecutionController`; do not return raw global `GraphListItemViewModel` lists for callable assets anymore.
- Important: call `SnapshotActiveAsset()` before opening/filtering node palette or executing. Otherwise just-edited function parameters may not be in `GraphFileModel`, causing call nodes to miss pins.
- `GraphListController.LoadItem(item, snapshotCurrent: false)` is used when `MainWindow` already snapshots the active asset. Do not let each list controller snapshot cross-asset by itself, or event/function canvases can get mixed.
- Direct execution is only valid for a script event graph. Function libraries and private functions are edit/call-only.
# 2026-06-04 恢复记录

- 远端 `be3b34f` 没有上一轮未提交恢复内容；如果用户说“回退到 git 版了”，要在当前提交上补回功能，不做 reset。
- 编译系统文件：`Services/GraphCompileService.cs`、`Services/GraphCallReferenceSyncService.cs`。
- `GraphListItemViewModel.IsCompileDirty` 表示逻辑需要编译；布局移动只保存脏，不编译脏。
- `GraphListController.MarkLogicDirty()` 用于参数/连线/节点逻辑变化；`MarkLayoutDirty()` 用于节点移动。
- 左侧栏两块：事件图表、函数。空列表折叠，新建资产默认空，用户点 `+` 才创建图表。
# 2026-06-04 recovery implementation notes

- Content browser source of truth: `ContentBrowserItems`; visible panes: `ContentFolderItems` (folder tree) and `ContentVisibleItems` (current folder tiles). The visible panes use `RangeObservableCollection.ReplaceAll(...)` for batch Reset refresh; do not return to `Clear()+Add` loops for directory/search projection updates.
- New content assets inherit `_currentContentFolderId`. Double-click folder enters it; double-click non-folder opens editor.
- Drag/drop asset onto a folder asks move/copy/cancel. Copy must allocate new content/graph IDs and clone graph DTOs.
- Event/function lists remain separate `GraphListController` instances. Do not merge selections or load one kind through another controller.
- Section collapse state is runtime-only per content asset. Use `*SectionHasState` so a user-collapsed non-empty section does not auto-expand on reopen.
- Deleting the last graph/function must call `GraphEditorService.ClearGraph()` and must not recreate a default event graph/start node.
- Compile-dirty graph sections show orange header badges; compile-dirty items show `*` and orange row highlighting.
- Dirty split: graph logic/signature/connection/node changes call `MarkLogicDirty()`; layout-only moves call `MarkLayoutDirty()`.
- New graph/function list items must start compile-dirty. Isolated tooling can set `AUTOMATION_STUDIO_LIBRARY_DIR` instead of writing to `%APPDATA%/AutomationStudioWpf`.
- Compile sync clears compile dirty and should only mark assets touched by call-reference updates as save dirty.
- Global `Delete` / `F2` routing lives in `Window_PreviewKeyDown`; never intercept while a `TextBox` has focus.
- Content tree commands must preserve `_contentFolderSelectionActive`; otherwise folder right-click/`Delete`/`F2` can accidentally use stale tile selection.
- Verify with `dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck` and require `0 warning / 0 error`.
