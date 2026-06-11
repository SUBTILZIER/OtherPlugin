# 技术文档

## 架构设计

### 当前模块边界（2026-06-02）

项目已从“大 `MainWindow` + 大 Runtime switch”拆为多层：

```
UI / MainWindow
    ├─ 只做窗口装配、Binding 暴露、XAML 事件转发、日志显示、关闭提示
    └─ 不直接调用 Win32 / Python / Runtime 具体能力

Interaction
    ├─ ExecutionController       ← 执行、取消、校验、Python 环境检查
    ├─ GraphListController       ← 图谱列表、新增、切换、删除、重命名、保存
    ├─ CanvasPanZoomController   ← 右键平移、滚轮缩放、F 全览、坐标转换
    ├─ NodeDragSelectionController ← 节点拖动、框选、多选、复制粘贴、对齐
    ├─ PinConnectionController   ← 拖线、连线、断线、预览线、路由节点插入
    ├─ InspectorController       ← 属性面板加载、自动保存、浏览对话框、窗口列表、字段锁定和灰态
    ├─ NodePaletteController     ← 右键节点菜单，来自 NodeRegistry.Definitions
    ├─ LogPanelController        ← 日志过滤、刷新、清空
    └─ GraphImportDropController ← JSON 图谱拖拽导入

GraphCore / Services
    ├─ GraphValidator            ← 执行前图谱校验
    ├─ GraphEditorService        ← 节点/Connections/ConnectionPaths、执行计划构建
    ├─ GraphCommandService       ← Undo/Redo 快照命令
    ├─ GraphLibraryService       ← 图谱列表本地持久化
    ├─ NodeFactory               ← ID 生成 + ViewModel 创建
    └─ NodeSerializer            ← ViewModel/FileModel/RuntimeModel 转换

Runtime / Nodes / Adapters
    ├─ GraphRuntimeExecutor      ← 只调度执行链
    ├─ RuntimeContext            ← 统一保存/解析节点输出
    ├─ NodeRegistry              ← 节点定义 + 执行器注册
    ├─ INodeExecutor             ← 每个节点的执行入口
    └─ Adapters                  ← 鼠标、键盘、窗口、进程、Python 能力封装
```

### 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                      Presentation Layer                      │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  │
│  │ MainWindow   │  │ LogWindow    │  │ PythonInstaller  │  │
│  └──────────────┘  └──────────────┘  └──────────────────┘  │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                       Service Layer                          │
│  ┌──────────────────┐  ┌──────────────────┐                 │
│  │ GraphEditor      │  │ NodeClipboard    │                 │
│  │ Service          │  │ Service          │                 │
│  └──────────────────┘  └──────────────────┘                 │
│  ┌──────────────────┐  ┌──────────────────┐                 │
│  │ NodeSerializer   │  │ NodeFactory      │                 │
│  └──────────────────┘  └──────────────────┘                 │
│  ┌──────────────────┐                                       │
│  │ PythonAuto       │                                       │
│  │ Installer        │                                       │
│  └──────────────────┘                                       │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                        Domain Layer                          │
│  ┌──────────────────┐  ┌──────────────────┐                 │
│  │ Graph/           │  │ Runtime/         │                 │
│  │ (节点模型)        │  │ (执行引擎)        │                 │
│  └──────────────────┘  └──────────────────┘                 │
│  ┌──────────────────┐  ┌──────────────────┐                 │
│  │ Logging/         │  │ Python/          │                 │
│  │ (日志系统)        │  │ (图像识别脚本)     │                 │
│  └──────────────────┘  └──────────────────┘                 │
└─────────────────────────────────────────────────────────────┘
```

> 上图是历史分层图。当前实际代码以“Interaction + GraphCore + Runtime/Nodes/Adapters”边界为准。

## 核心模块详解

### 0. 资产系统：事件图 / 自定义函数 / 宏

- `Graphs` 是旧字段，当前语义为事件图；新增 `Functions` 和 `Macros`。
- 函数默认包含 `FunctionEntry` 和 `FunctionReturn`，同步执行并把返回节点输入复制到调用节点输出。
- 宏默认包含 `MacroEntry` 和 `MacroOutput`，运行期调用；到达哪个宏输出节点，就从调用节点对应执行出口继续。
- 参数使用稳定 ID 作为 pin name；重命名只改显示名，不应破坏连线。
- 参数类型第一版映射：`Boolean`、`Vector2D` 使用原生 pin，其余类型先映射为 `String`。

### 1. Services 层（新增）

#### GraphLibraryService
负责随软件启动自动加载/保存图谱列表：
- 保存位置：`%AppData%\AutomationStudioWpf\graph-library.json`
- 保存内容：图谱列表、每个图谱的节点/连线、最后选中的图谱 ID
- 工具栏 `保存` 是保存所有图谱，不是只保存当前图谱
- 切换图谱时只静默快照当前编辑器状态到当前图谱项，不弹保存提示
- 退出软件时，如果存在未保存图谱，统一弹窗询问是否保存

#### GraphEditorService
负责图谱的核心编辑逻辑：
- 节点和连接的增删改查
- 可见连线路径 `ConnectionPaths` 的重建；批量连接编辑通过 `RunBatchedEdit(...)` 合并重建
- 图谱的加载和保存
- 执行计划的构建
- 引脚连接状态管理
- 当前图内非 `Reroute` 节点编号分配。编号按图类型使用 `N###` / `Fun###` / `Mac###`，删除节点后空出的最小编号可复用
- 监听节点 `Title` / `NodeNumber` 变化，并同步已通过 `TargetNodeId` 维护引用到该节点的 `ToDoNodeViewModel`

```csharp
public class GraphEditorService
{
    public ObservableCollection<NodeBaseViewModel> Nodes { get; }
    public ObservableCollection<ConnectionViewModel> Connections { get; }
    public ObservableCollection<ConnectionPathViewModel> ConnectionPaths { get; }
    
    public void NewGraph()
    public void SaveGraph(string path)
    public void LoadGraph(string path)
    public GraphExecutionPlan BuildExecutionPlan()
    public void RunBatchedEdit(Action action)
    public void RemoveConnections(IEnumerable<ConnectionViewModel> connections)
}
```

#### Node numbering and ToDo jump
- `NodeBaseViewModel.NodeNumber` 是可见、持久化的当前图内编号；`Reroute` 不分配编号。
- `GraphEditorService.AddNode(...)` 和 `LoadFromModel(...)` 会给缺失、前缀错误、重复的编号重新分配当前图最小空闲值。
- `ToDoNodeViewModel` 保存 `TargetNodeTitle`、`TargetNodeNumber`、`TargetNodeId`、`ReturnAfterTarget`。运行时优先用连入 `target_title` / `target_number` pin 的动态值；无连线时用静态 `TargetNodeTitle + TargetNodeNumber` 解析。
- `InspectorController` 的 ToDo 面板提供搜索框和结果列表，可按节点名或编号过滤并填入双键目标。
- `MainWindow.CommitInspectorAndSnapshotAllSessions()` 在编译、保存、运行前统一应用属性面板并 snapshot 所有打开 session，避免多窗口里非 active 资产的修改未进入 `ContentAssetViewModel`。
- `InspectorController.ToDoTargetSelected()` 选择结果后立即写入 VM 的 `TargetNodeTitle`、`TargetNodeNumber`、`TargetNodeId`，刷新描述，标脏，并触发 active graph snapshot。
- `GraphCompileService.EnsureGraphToDoTargets()` 会在 `TargetNodeId` 有效但 title/number 缺失或变旧时，从同图目标节点回填 `TargetNodeTitle` / `TargetNodeNumber`。
- `GraphCompileService.ValidateToDoTargets()` 只有在两个目标输入 pin 都未连接，且静态 title/number 也为空或无效时才报错。

#### Content browser search and callable navigation
- 内容浏览器源数据是 `ContentBrowserItems`；左树投影 `ContentFolderItems`，右侧瓦片投影 `ContentVisibleItems`。
- `MainWindow.NavigationFeatures.cs` 在窗口 `Loaded` 后动态给 `ContentBrowserHeaderBar` 安装搜索框。搜索范围是当前 `_currentContentFolderId` 及全部子文件夹；根目录时搜索全部内容资产。
- 搜索支持空格关键字、路径片段、`DisplayName` / `Kind`、不区分大小写和 subsequence 模糊匹配。搜索结果直接替换 `ContentVisibleItems`，文件夹和资产都会进入结果。
- 搜索结果双击仍走内容浏览器现有打开逻辑：文件夹进入目录，脚本/函数库/宏库调用 `OpenContentAsset(asset)`。
- `Ctrl+B` 由 `MainWindow_NavigationPreviewKeyDown` / `ContentBrowserListBox_NavigationPreviewKeyDown` 处理：有选中资产时清空搜索、进入真实父目录、选中并滚动到资产；没有内容浏览器选中项时定位当前打开资产。
- 画布中双击 `FunctionCallNodeViewModel` / `MacroCallNodeViewModel` 会按 stable `FunctionId` / `MacroId` 查找目标图，打开目标所在脚本/函数库/宏库资产，然后通过对应 `GraphListController` 加载目标 `GraphListItemViewModel`。
- 双击跳转先 `SaveVisibleGraphsToActiveContent()`；随后走 `OpenOrActivateAsset(target.Asset, target.Graph, kind)` 聚焦已有 session 或创建新 session。不要靠显示名解析调用目标。
- `MainWindow.ContentBrowserMultiSelect.cs` 扩展内容浏览器为 UE 风格多选：Ctrl 多选、Shift 区间、框选、Ctrl+C/Ctrl+V 复制粘贴资产、多删除、拖拽预览和移动/复制到文件夹。

#### Editor sessions and window bar
- 多编辑窗口由 `EditorSessionViewModel` 表示：每个打开资产一个 session，持有自己的 `GraphEditorService`、`NodeFactory`、`GraphCommandService`、事件图/函数/宏集合和当前图记忆。
- 每个 `EditorSessionViewModel` 持有自己的完整 `EditorSurfaceControl`，其中包含图列表、画布、节点菜单和属性面板；`EditorSurfaceContext` 持有该 session 的 graph list/canvas/drag/inspector/pin/palette/import controllers。`MainWindow` 只镜像 active context controllers 以支撑尚未拆完的 handler。
- `OpenContentAsset(...)` 现在是 `OpenOrActivateAsset(...)` wrapper。重复打开同一资产只聚焦已有 session，不重置到第一个事件图；函数/宏调用节点双击会打开或聚焦目标资产 session，再加载目标 graph id。
- 工具栏下方 `EditorWindowBar` 绑定主窗口内的 `MainEditorSessions`，不显示 `DockMode.Detached` 的独立窗口；窗口栏右键的关闭全部/关闭右侧只作用于主窗口标签页。全量 `EditorSessions` 仍包含 detached，供保存、退出、compile-all 使用。拖出主窗口会创建 `DetachedEditorWindow`；拖到主窗口内部只激活标签，不创建画布子窗口。
- 拖动窗口标签时会显示跟随预览卡片，越过主窗口边界后提示释放/继续拖出为独立窗口。
- 主窗口标签页和 `DetachedEditorWindow` 都直接 host 对应 session 的 `Surface`；detached 窗口不再显示只读 preview，也不再要求“激活后在这里编辑”。
- `EditorSurfaceContext.Configure(...)` 是幂等的：同一个 session 的 controller 不因 host attach/activate 反复重建。surface 事件只做轻量 active-session 切换并复用该 context 自己的 controller，避免 tab 切换回第一个图、列表折叠或 detached/main 互相污染。
- detached session 激活时只更新全局工具栏/运行/保存目标，不覆盖 `_lastMainEditorSession`；主窗口继续显示最近的主窗口 tab surface。
- `MainWindow.EditorSurfaceRegions.cs` 和 legacy region reparent hooks 已删除。不要恢复 `AttachLegacyEditorRegionsToSessionSurface()` 的区域搬移逻辑。
- session 关闭只 snapshot 回 `ContentAssetViewModel` 并移除编辑窗口，不删除资产。删除内容浏览器资产时会关闭所有指向该资产的 session，避免悬空编辑窗口。
- 保存、退出、编译前使用 `CommitInspectorAndSnapshotAllSessions()` / `CommitAllSessionsToAssets()`，保证多窗口编辑内容参与引用同步和校验。
- 工具栏编译是 active-asset scoped，走 `GraphCompileService.CompileAsset(...)`：脚本会编译该资产内事件图、函数、宏；函数库/宏库会编译该库内全部图。`GraphCompileService.CompileGraph(...)` 仍保留为 current-graph scoped 内部能力，但工具栏不使用它。

#### NodeSerializer
负责节点与持久化模型之间的转换：
- `NodeBaseViewModel` ↔ `NodeFileModel`
- `NodeBaseViewModel` ↔ `GraphRuntimeNode`

支持版本兼容性处理，如旧版 `DelayMs` 字段的迁移。

#### NodeClipboardService
处理复制粘贴逻辑：
- 序列化选中节点到剪贴板
- 反序列化并创建新节点
- 恢复节点间的内部连接

#### NodeFactory
统一创建各类节点，管理节点 ID 生成：
```csharp
public class NodeFactory
{
    public string CreateNodeId() => $"node_{++_counter:000}";
    public NodeBaseViewModel CreateNode(NodeKind kind, double x, double y);
    public FindImageNodeViewModel CreateFindImageNode(...)
    public MouseClickNodeViewModel CreateMouseClickNode(...)
    // ...
}
```

#### NodeRegistry
统一管理节点定义和执行器注册：
- `Definitions`：节点菜单分类、显示名、引脚定义、搜索标签和属性面板 schema key。
- `TryGetExecutor`：Runtime 对普通能力节点按 `NodeKind` 找到对应 `INodeExecutor`。
- `GraphRuntimeExecutor` 仍直接处理结构节点：`Start`、`Reroute`、`If`、`ForLoop`、`WhileLoop`、函数/宏/自定义事件入口与调用节点。
- 右键节点菜单由 `NodePaletteController` 读取 `NodeRegistry.Definitions` 生成，禁止再在 `MainWindow` 手写菜单列表。

新增节点时至少更新：
1. `GraphTypes.NodeKind`
2. 对应 `ViewModel`
3. `NodeFactory.CreateNode`
4. `NodeSerializer`
5. 对应 `INodeExecutor`
6. `NodeRegistry.CreateDefaultDefinitions()` 和执行器注册

#### PythonAutoInstaller
Python 环境检测与安装指引：
- 检测 Python 安装位置
- 检查必要依赖库（cv2, PIL, numpy）
- 弹出可复制的安装命令对话框
- 支持阿里云 PyPI 镜像

### 2. Graph 层（节点模型）

#### 节点基类设计
```csharp
public abstract class NodeBaseViewModel : ObservableObject
{
    public string Id { get; init; }
    public abstract NodeKind NodeKind { get; }
    public abstract string NodeTypeKey { get; }
    
    public ObservableCollection<PinViewModel> InputPins { get; }
    public ObservableCollection<PinViewModel> OutputPins { get; }
    
    public abstract void RefreshDescription();
}
```

#### 引脚系统
- **PinDirection**: Input / Output
- **PinKind**: Execution / Boolean / Vector2D / String
- 支持动态引脚位置计算

#### 当前节点定义 (39 个)

`NodeRegistry.CreateDefaultDefinitions()` 当前注册 39 个菜单/运行时定义；`NodeKind.Comment` 仍是历史残留枚举，但不在 `NodeRegistry.Definitions`，旧 `comment` 图节点由 `NodeSerializer.IsRemovedNodeType()` 丢弃。

| 节点 | NodeKind | 分类 | 引脚 |
|------|----------|------|------|
| Start | Start | - | exec_out |
| FindImage | FindImage | 插件节点 | exec, result(bool), center(V2D)，支持可选屏幕区域 |
| MouseClick | MouseClick | 输入节点 | exec, position(V2D in), result(bool) |
| MouseMove | MouseMove | 输入节点 | exec, position(V2D in), result(bool) |
| Keyboard | Keyboard | 输入节点 | exec, result(bool) |
| ScrollWheel | ScrollWheel | 输入节点 | exec, result(bool) |
| Delay | Delay | 逻辑节点 | exec |
| If | If | 逻辑节点 | exec, condition(bool in), exec_true/false |
| ForLoop | ForLoop | 逻辑节点 | exec, end_condition(bool in), exec_loop_body/completed |
| WhileLoop | WhileLoop | 逻辑节点 | exec, condition(bool in), exec_loop_body/completed |
| StartProgram | StartProgram | 功能节点 | exec, process_name(String out), result(bool) |
| SelectWindow | SelectWindow | 功能节点 | exec, process_name(String in/out), result(bool) |
| PrintLog | PrintLog | 调试 | exec, message(String in) |
| Reroute | Reroute | 连线 | in/out (同类型透传) |
| Stage-5 Mouse | MouseDoubleClick/GetMousePosition | 输入/鼠标 | 双击、输出当前位置 |
| Stage-5 Keyboard | KeyChord | 输入/键盘 | 组合键，属性面板支持添加按键 + 组合预览 |
| Stage-5 Image | WaitImage/WaitImageDisappear | 插件/图像识别 | 等待图片、等待消失；WaitImage 输出 image_path/center/result；实时截屏时隐藏 source_image_path 输入 |
| Stage-5 Logic | Compare/BooleanAnd/BooleanOr/BooleanNot/StringConcat | 逻辑 | 比较、布尔、字符串拼接 |
| Stage-5 Window | WaitWindow/CloseWindow/WindowExists/GetForegroundWindow | 系统/窗口 | 等待/关闭/存在/前台窗口 |
| Stage-5 Debug | SaveScreenshot/ShowMessage | 调试 | 截图保存、弹窗；SaveScreenshot 保存模式用枚举下拉，默认 Auto 保存到 `Temp/Screenshots`，只输出 image_path |

### 3. Runtime 层（执行引擎）

#### 执行流程
```
StartNode → FindImageNode → MouseClickNode → ...
     ↓
GraphRuntimeExecutor.Execute()
     ↓
ExecuteChain() → ExecuteNode() → NodeRegistry → INodeExecutor → Adapter
```

#### 环路检测
不再用 `HashSet<string> visitedNodes` 阻止节点重复访问，因为 For/While 循环体内节点需要合法重复执行。

当前策略：
- `GraphRuntimeExecutor` 使用最大执行步数保护（`MaxChainSteps = 10000`）。
- For/While 节点自身负责循环次数/退出条件。
- 超过安全步数视为疑似执行环路，记录 `Error` 并停止。

#### 运行结果分级
节点执行统一返回 `NodeExecutionResult`：
- `Success`：成功执行，继续后续节点。
- `WarnButContinue`：业务未命中或参数可退化，写 Warn，继续后续节点。
- `FatalStop`：依赖缺失、脚本崩溃、Win32 异常、超时、执行环路等，写 Error，停止执行。
- `ToDo` 节点通过 `NodeExecutionResult.Jump(...)` 改变执行位置；`ReturnAfterJump` 为 true 时，目标链结束后继续 `ToDo.exec_out`。
- Return-after-target 的目标链由 `ExecuteReturnJump(...)` 调用 `ExecuteFromNode(..., stopBeforeNodeId: sourceToDo.Id)` 执行。目标链如果自然走回源 ToDo，会在执行源 ToDo 前停下并返回，然后继续源 ToDo 的 `exec_out`；这不是编译错误，也不是递归。
- 真正的 ToDo 返回递归由 `ActiveToDoReturnJumps` 和 `MaxNestedToDoReturnJumps` 防护；重复的 source-target return jump 或超过 256 层嵌套会 `FatalStop`。

重要安全规则：
- 输入 pin 未连接：可以使用节点本地属性。
- 输入 pin 已连接但上游没有运行时输出：当前节点 Warn 并跳过，不回退本地属性。
- 典型场景：找图未命中时，下游鼠标点击不会误用旧坐标或 `(0,0)`。
- ToDo 跳转必须同时匹配节点名和编号。空目标、找不到、匹配多项或直接自跳都会 `FatalStop`。

#### Win32 API 调用

Win32 细节现在封装在 `Adapters/`，Runtime 不直接调用 Win32。

键盘输入使用 `Win32KeyboardAdapter`，内部为 `SendInput` 扫描码模式，兼容游戏：

```csharp
// 结构体：x64 上 sizeof(INPUT) = 40, ki 在 offset 8
[StructLayout(LayoutKind.Sequential)]
struct KEYBDINPUT {
    public ushort wVk; public ushort wScan; public uint dwFlags;
    public uint time; public IntPtr dwExtraInfo;
}
[StructLayout(LayoutKind.Explicit, Size = 40)]
struct INPUT64 {
    [FieldOffset(0)] public uint type;
    [FieldOffset(8)] public KEYBDINPUT ki;
}

[DllImport("user32.dll", SetLastError = true)]
static extern uint SendInput(uint nInputs, INPUT64[] pInputs, int cbSize);

[DllImport("user32.dll")]
static extern uint MapVirtualKey(uint uCode, uint uMapType);  // VK→扫描码

// 鼠标封装在 Win32MouseAdapter，当前仍用 SetCursorPos + mouse_event
[DllImport("user32.dll")]
static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")]
static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

// 窗口操作
[DllImport("user32.dll")]
static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")]
static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")]
static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
```

键盘输入常量：`INPUT_KEYBOARD=1`, `KEYEVENTF_KEYUP=0x0002`, `KEYEVENTF_SCANCODE=0x0008`, `KEYEVENTF_EXTENDEDKEY=0x0001`

#### 选中窗口节点
`SelectWindowNodeViewModel` 通过进程名定位窗口：
- 支持手填 `ProcessName`
- 支持前置 string 输入 pin：`process_name`
- 输出 `process_name` 方便后续日志/调试
- 输出 `result` 表示窗口是否成功置前
- 进程名支持 `notepad` 或 `notepad.exe`，运行时会去掉 `.exe`
- 空进程名、找不到窗口：`Warn + continue`
- Win32 异常：`Error + stop`

### 4. Logging 层

#### 日志级别
- **INFO**: 白色，普通信息
- **WARN**: 黄色，警告信息（参数缺失等）
- **ERROR**: 红色，错误信息

#### 日志存储
- 内存：`ObservableCollection<LogEntry>` 用于实时显示
- 文件：`saved/log/Log_yyyy_MM_dd_HH_mm.txt`

#### 日志面板交互
- 主窗口日志显示控件是只读 `RichTextBox`，由 `LogPanelController.Refresh()` 生成带颜色的 `FlowDocument`。
- `LogPanelController` 显式绑定 `ApplicationCommands.Copy` 和 `ApplicationCommands.SelectAll`：`Ctrl+C` 复制当前选中文本，`Ctrl+A` 全选当前过滤后的日志文本。
- `MainWindow.Window_PreviewKeyDown` 对 `TextBoxBase` 焦点直接放行，避免日志 `RichTextBox` 焦点内的 `Ctrl+C` / `Ctrl+A` 被全局节点复制快捷键截获。

## 关键技术决策

### 1. 为什么使用 Python 进行图像识别？

**优点：**
- OpenCV Python 绑定成熟稳定
- 丰富的图像处理生态（PIL, numpy）
- 易于扩展图像预处理等能力

**权衡：**
- 需要管理 Python 环境
- 进程间通信开销

**解决方案：**
- 内置 Python 安装包
- 自动环境检测与安装指引

### 2. 为什么选择 MVVM 架构？

- **可测试性**: 业务逻辑与 UI 分离
- **可维护性**: 职责清晰，代码组织有序
- **可扩展性**: 新增节点类型只需添加 ViewModel

### 3. 节点序列化设计

使用扁平化的 `NodeFileModel` 避免多态序列化复杂性：
```csharp
public class NodeFileModel
{
    public string NodeTypeKey { get; set; }  // 用于反序列化时创建正确类型
    public string Id { get; set; }
    // 各类型共有的字段...
    
    // 类型特有字段（可为 null）
    public string? ImagePath { get; set; }
    public int LoopCount { get; set; }
    public bool ConditionValue { get; set; }
}
```

## 性能优化

### 1. 虚拟化
日志列表使用 `VirtualizingStackPanel.IsVirtualizing="True"`，避免大量日志时的内存问题。

### 2. 异步执行
图谱执行在后台线程进行，避免阻塞 UI：
```csharp
await Task.Run(() => _runtimeExecutor.Execute(plan, baseDirectory, ct), ct);
```

### 3. 延迟加载
属性检查器面板按需显示，减少初始化开销。

## 常见问题排查

### 1. 找图功能无法使用

**检查清单：**
- Python 是否安装：`python --version`
- 依赖库是否安装：`python -c "import cv2; import PIL; import numpy"`
- 图片路径是否正确（支持相对路径和绝对路径）

**解决方案：**
运行安装命令：
```bash
pip install opencv-python pillow numpy -i https://mirrors.aliyun.com/pypi/simple/
```

### 2. 执行时节点无响应

**可能原因：**
- 节点参数缺失（查看黄色警告日志）
- 执行链断裂（检查连线）
- 死循环（执行器会自动检测并终止）

### 3. 日志无法复制

已修复，现在日志使用 `TextBox` 显示，支持选择和复制。

## 扩展指南

### 添加新节点类型

1. **创建 ViewModel**：
```csharp
public sealed class MyNodeViewModel : NodeBaseViewModel
{
    public override NodeKind NodeKind => NodeKind.MyNode;
    public override string NodeTypeKey => "my_node";
    
    // 自定义属性...
}
```

2. **添加到 NodeKind 枚举**（GraphTypes.cs）

3. **实现序列化**（NodeSerializer.cs）

4. **实现执行逻辑**：在对应 `Nodes/<分类>/` 下实现 `INodeExecutor`，不要修改 `GraphRuntimeExecutor` 的节点执行 switch

5. **注册节点**：在 `NodeRegistry.CreateDefault()` 注册 executor，在 `CreateDefaultDefinitions()` 注册显示名、分类、引脚

6. **添加属性面板**（MainWindow.xaml），字段锁定规则优先放到 `InspectorController`

### 添加新的 Python 功能

1. 在 `Python/` 目录添加脚本
2. 在 `PythonAutoInstaller.cs` 添加依赖检查
3. 在 `Adapters/PythonScriptAdapter.cs` 复用 JSON 临时文件调用
4. 在插件节点 executor 中调用 `IPythonScriptAdapter.RunJsonScript`

Python 参数规则：
- C# → Python 不走命令行中文参数。
- 使用 UTF-8 no BOM JSON 临时文件。
- Python 脚本保留旧命令行参数兼容可以，但新调用必须走 JSON 文件。

## 踩坑记录

> 以下记录来自实际开发中的踩坑经验，按时间倒序排列，新记录追加到顶部。

### 2026-06-09：内容浏览器搜索 / 定位 / 调用节点跳转

#### 内容浏览器当前行为
- 顶部 header 只显示“内容浏览器”，不再放新建按钮。
- 新建资产只走右侧空白右键菜单，菜单项为 `脚本 / 文件夹 / 函数库 / 宏库`。没有独立“宏”资产；宏在脚本或宏库编辑面板中新增。
- 右键资产只显示 `重命名 / 删除`。右键空白显示新建菜单。实现上复用一个 `ContextMenu`，由 `ContentBrowserContextMenu_Opened` 根据 `_contentBrowserContextTargetsAsset` 切换 `Visibility`。
- 不要把带事件的 `ContextMenu` 放进 `ListBoxItem.Style Setter`。WPF 会在运行期把模板子元素接到 style connector，可能启动崩溃：`Unable to cast object of type 'TextBox' to type 'Style'`。
- `MainWindow.NavigationFeatures.cs` 动态安装搜索框，不在 XAML 内硬编码。搜索当前目录递归资产/文件夹，支持空格关键字、路径片段、subsequence 模糊匹配和不区分大小写。
- `Ctrl+B` 是已实现定位：内容浏览器有选中资产时定位该资产真实父目录；无选中资产但有当前打开资产时定位当前打开资产。
- 双击画布中的函数/宏调用节点是当前已实现跳转：按 stable `FunctionId` / `MacroId` 找目标图，打开目标所在资产，再加载目标函数/宏图。
- `MainWindow.ContentBrowserMultiSelect.cs` 负责多选、框选、资产 Ctrl+C/Ctrl+V、拖拽预览和多删除。新增内容浏览器交互优先放在该 partial 或独立 controller，不要继续膨胀 `MainWindow.xaml.cs`。

### 2026-06-08: ToDo persistence and log copy fixes

- Compile/save/run entry points must commit inspector edits and snapshot open sessions before reading graph data. This is required for inspector-only edits and multi-window assets, especially ToDo target dropdown selection.
- ToDo static target selection must remain persisted even if `target_title` / `target_number` pins are connected; connected pins are runtime overrides, not a reason to clear static defaults.
- `GraphCompileService.EnsureGraphToDoTargets()` is a migration/repair pass: when old data keeps only `TargetNodeId`, compile fills title/number from the referenced target node before validation.
- Log text copy uses `RichTextBox` command bindings plus `TextBoxBase` shortcut passthrough. Do not special-case only `TextBox`, or `RichTextBox` copy will be intercepted by graph shortcuts again.

#### 文件夹树
- 左侧树绑定 `ContentFolderItems`，右侧瓦片绑定 `ContentVisibleItems`，源数据仍是 `ContentBrowserItems`。
- 单击文件夹行进入文件夹并刷新右侧内容；点击箭头按钮只展开/收起，不进入。
- `HasFolderChildren` 只统计子文件夹，不因脚本/库资产显示箭头。
- 层级缩进由 `ContentAssetViewModel.TreeIndent` 提供，`TreeDisplayName` 只返回原始名称，不再用字符串空格缩进。
- 箭头样式是 `ContentFolderToggleIconStyle`。收起图形朝右，展开图形朝下。默认 `Path.Data` 必须放在 style setter，不能直接写在 `Path Data="..."` 上，否则 `DataTrigger` 无法覆盖。

#### 分栏与瓦片布局
- 内容浏览器内部为三列：`ContentTreeColumn` / `ContentBrowserTreeSplitter` / 右侧瓦片列。
- `ContentTreeColumn` 默认宽度 180，`MinWidth=120`，`MaxWidth=420`；右侧瓦片列 `MinWidth=240`。
- 左侧树禁横向滚动，长名称用 `TextTrimming=CharacterEllipsis`；用户可拖 splitter 加宽。
- 右侧 `ContentBrowserListBox` 保持 `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`，`WrapPanel` 绑定 ListBox 实际宽度，拖动分栏后自动重排瓦片。

#### 画布连线交互
- 从输入或输出引脚拖线到空白画布并抬起，会打开同一个节点菜单；创建节点后由 `PinConnectionController.TryAutoConnectNewNode()` 自动连接第一个兼容的相反方向引脚。
- 普通右键打开节点菜单前必须清掉待自动连接状态；连线落空打开菜单时不能清。
- 引脚释放判定不只依赖 WPF 精确 `InputHitTest`，还会按图空间距离查找最近引脚，当前半径为 24。

#### 参数默认值
- `GraphParameterDefinition.DefaultValue` 是函数/宏/自定义事件参数默认值，必须写入 `GraphParameterFileModel`，并通过 `GraphRuntimeParameter` 进入运行时。
- 函数/宏/自定义事件入口参数：调用节点对应输入未连接时，运行时使用入口参数默认值。
- 函数返回/宏输出参数：返回节点对应输入未连接时，调用节点输出使用返回/输出参数默认值。
- Float/Vector3D/Vector4D/ImageAsset/String 目前仍映射为 `String` pin；默认值按字符串传递，例如 `23.0f`。Boolean 默认值解析为 bool，Vector2D 默认值可写 `x,y`。

#### 验证门禁
- `Tests/CodexSmoke/Program.cs` 负责 UI smoke：检查 header 无新建按钮、右键菜单模式、暗色菜单模板、树缩进、箭头几何、splitter 列宽、单击进入/箭头展开行为、图表隔离、公开到库硬隔离与编译校验。
- Smoke 是轻量启动/回归验收，不是完整测试框架。只放关键交互和数据隔离断言；如果单次执行明显变慢，要优先拆小或删除低价值断言。
- 默认完成前执行：
  - `dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck`
  - `git diff --check`
  - `dotnet run --project .\AutomationStudioWpf.csproj` 做短启动探测，能正常拉起窗口即可
  - `codegraph.cmd sync`
- 只有改到对应高风险交互、需要锁回归，或用户明确要求时才跑 broad smoke / optional reroute repro smoke。

- 启动验证不再固定等待 20 秒；只确认能正常启动。若进程快速退出、输出 `Unhandled exception` 或 WPF 初始化异常，必须收集终端输出/异常栈并先修。
- 不要自动 `git push`。只有用户明确要求推送时才推；推送前确认不包含测试功能相关文件夹。

### 2026-06-11: CodeGraph / docs / skill refresh

- Current project skill source of truth is `AutomationStudio/Agent/skills/automation-studio-wpf/SKILL.md`. The old `.kimi/skills/automation-studio-wpf/SKILL.md` path is deleted and must not be restored.
- CodeGraph sync is part of the final gate. Commit only ignore/config policy; database, wal/shm, cache, dirty markers, and logs stay local.
- README, TECHNICAL, `agentmemory.md`, and project skill must describe the active code path accurately: multi-window sessions are real, each session owns a complete `EditorSurfaceControl`, and detached windows host editable surfaces directly. Do not document inactive detached preview or legacy region moving as active behavior.
- Do not describe git push as allowed unless the user explicitly requests push in the current task.

### 2026-06-08: CodeGraph / docs / skill refresh

- CodeGraph sync is part of the final gate. Commit `.codegraph/.gitignore` so database, wal/shm, cache, and logs stay local.
- Project skill source of truth in this local project is `AutomationStudio/Agent/skills/automation-studio-wpf/SKILL.md`; the old `.kimi/skills/automation-studio-wpf/SKILL.md` tree is gone.
- README, TECHNICAL, `agentmemory.md`, and project skill should mention durable graph-editor rules: `ConnectionPaths` for visuals, `Connections` for persistence/runtime, batched connection edits, command-stack boundaries, and wire/reroute UX.
- Do not describe git push as allowed unless the user explicitly requests push in the current task.

#### Batched graph edits and runtime index
- `GraphEditorService.RunBatchedEdit(...)` defers `ConnectionPaths` rebuild and `GraphChanged` until the outermost batch exits; use it for composed connection mutations.
- `PinConnectionController` uses `RemoveConnections(...)` for selected visual path deletion and wraps reroute insertion in one batch.
- `GraphExecutionPlan` owns an internal lazy `GraphExecutionIndex` for node, execution-edge, and input-edge lookup; public constructor and graph JSON remain unchanged.

### 2026-06-06: editor command and wire UX foundation

#### GraphCommandService
- `Services/GraphCommandService.cs` owns graph edit Undo/Redo. It captures `GraphFileModel` snapshots before/after an edit and restores through `GraphEditorService.LoadFromModel(...)`.
- Commands must use the active `GraphAssetKind`; otherwise function/macro undo can reload as an event graph and auto-create a Start node.
- Clear the command stack when switching content assets or graph/function/macro items. Do not allow undo across graph boundaries.
- Use `Execute(...)` for direct graph mutations and `RecordApplied(...)` for edits already applied by continuous interaction, such as node dragging.

#### Wire selection and reroute editing
- Visible wire selection is stored on `ConnectionPathViewModel.IsSelected`; runtime/persistence still use `ConnectionViewModel` and `GraphEditorService.Connections`.
- `PinConnectionController` maps visual `ConnectionPathViewModel` back to backing `ConnectionViewModel` for double-click, Alt-click, and context-menu reroute insertion by sampling the visible Bezier geometry.
- Delete/Backspace on a selected visible path removes all backing connections in that visual path as one undoable command. Reroute nodes are not deleted automatically.
- Active visible geometry is `ConnectionSplinePlanner.BuildGeometry(...)`. `ConnectionChain` / `ConnectionChainFinder` and `SplineTangentCalculator` are currently not called by XAML-bound paths.
- Tight/backward reroute layouts are treated as no-loop regressions in smoke tests; do not change `ConnectionSplinePlanner` without a new concrete repro.

#### NodeDefinition metadata
- `Runtime/NodeDefinition.cs` now exposes `SearchTags`, `InspectorSchemaKey`, `DefaultValues`, and `ValidationHints`.
- `NodeRegistry.Definition(...)` generates baseline tags from `NodeKind`, type key, category, and pin names/labels.
- `NodePaletteController` search must match display name, category, type key, kind, and generated tags. Keep this path metadata-driven before adding more node families.

### 2026-06-05：事件图 / 函数 / 宏画布隔离修复

#### 问题：切换图表时事件图、函数、宏内容串到一个画布/模型
- **现象**：新增一个事件图和一个函数后，再双击事件图，函数节点也混入同一画布；单个函数/宏时单击不跳转，多个时偶尔能切换。
- **根因**：`GraphListController.Load()` 内部会 `Persist()`。如果加载目标图前 `_activeAssetController` 仍指向旧 controller，`PersistAssetLibrary()` 会调用 `SaveVisibleGraphsToActiveContent()`，再通过旧 controller 把当前编辑器画布快照写回旧图。结果新加载的函数/宏画布被保存进事件图，或反向污染。
- **修复**：
  - 新增统一入口 `ActivateGraphListItem(...)`。
  - 切换顺序固定为：`SnapshotActiveAsset()` -> `_activeAssetController = targetController` -> `targetController.LoadItem(item, snapshotCurrent: false)`。
  - 事件图、函数、宏列表项增加 `PreviewMouseLeftButtonDown`，单击即可切换编辑界面。
  - 右键列表项也先激活目标项，再打开菜单，避免重命名/删除走错 controller。
- **测试**：smoke 用事件图、函数、宏来回切换，断言当前画布节点类型正确，并断言各自 `GraphFileModel.Nodes` 不混入其它图类型。
- **教训**：所有跨 controller 画布切换，必须先快照旧画布，再更新 active controller，再加载新图。不能让 `Load()` 在 active controller 还是旧值时触发持久化。

#### UI 调整：内容浏览器默认尺寸
- 底部行默认高度从 `300` 调整到 `360`，最小高度 `180`。
- 内容浏览器列和日志列改为 `* / *`，默认 50/50 平分底部面板，减少每次手动拉大的成本。

### 2026-06-03：阶段 5 常用节点清理与交互优化

#### 新增节点策略
- **新增范围**：鼠标、键盘、图像、逻辑、系统、调试共 22 个常用节点。
- **保留节点**：`MouseDoubleClick`、`GetMousePosition`、`KeyChord`、`WaitImage`、`WaitImageDisappear`、`Compare`、`BooleanAnd/Or/Not`、`StringConcat`、`WaitWindow`、`CloseWindow`、`WindowExists`、`GetForegroundWindow`、`SaveScreenshot`、`ShowMessage`。
- **已删除节点**：`MouseDrag`、`InputText`、`KeySequence`、`ClickImageCenter`、`SetVariable`、`Comment`。旧图加载时丢弃这些节点并写 Warn，同时过滤坏连线。
- **UI 策略**：保留的小节点继续使用 `CommonNodeViewModel` + 通用属性面板；复杂节点后续再拆专用 ViewModel/Inspector/Executor。
- **Runtime 策略**：每个保留节点仍有独立 `NodeKind`，执行器统一走 `Nodes/Common/CommonNodeExecutors.cs`，菜单定义仍由 `NodeRegistry.Definitions` 生成。
- **交互优化**：`KeyChord` 使用“增加按键 + 组合预览”；窗口类通用节点支持手填、运行窗口下拉、浏览 exe 推导进程名；`WaitImage.image_path` 可输出给后续 `FindImage.image_path`。
- **维护规则**：如果某个通用节点后续参数变复杂，再单独拆成专属 ViewModel/Inspector 面板；不要一开始就把所有小节点拆成几十个重复类。

#### 新增 Adapter 能力
- `IMouseAdapter`：双击、获取鼠标位置。
- `IKeyboardAdapter`：组合键。
- `IWindowAdapter`：等待窗口、关闭窗口、窗口是否存在、获取前台窗口。
- `IScreenshotAdapter`：保存全屏或指定区域截图。

#### 注意事项
- 剪贴板输入必须走 STA 线程，否则 WPF Clipboard 可能抛异常。
- `ShowMessage` 在后台执行线程中触发 UI 弹窗，必须通过 `Application.Current.Dispatcher.Invoke`。
- 图像等待类节点复用 `Python/find_image.py`，不引入 OCR/EasyOCR。
- `WaitImage`、`WaitImageDisappear`、`WaitWindow` 的超时语义统一为 `0=不超时`、负数回退默认值；持续等待时必须打印每轮检查日志，避免 UI 看起来像只执行一次。
- 编辑器空闲/拖拽/选中节点时禁止触发运行时能力：不做 Python 检测、不执行节点、不枚举窗口/进程。窗口列表只允许在用户点击“刷新”按钮时扫描一次。
- `CommonNodeViewModel` 的 `Text/Number` 字段是阶段 5 小节点的通用字段；保存文件字段要保持兼容，避免破坏旧图。前置输入锁定时，属性面板必须显示“前置输入”且不可编辑。

### 2026-06-03：属性面板下沉、找图区块识别、执行前校验增强

#### 变更 0：Runtime 扁平模型减少字段复用
- **修复前**：`StartProgram` 借用 `ImagePath/DelayMs`，`PrintLog` 借用 `ImagePath`，`WhileLoop` 借用 `ScrollSpeed/DelayMs`。
- **修复后**：`GraphRuntimeNode` 增加明确运行时字段：`ProgramPath`、`WaitTimeoutMs`、`PrintLogMessage`、`WhileLoopMode`、`MaxIterations`。
- **维护规则**：新节点可以继续用扁平模型，但字段名必须表达真实语义；不要把某节点字段塞到别的节点字段里复用。

#### 变更 1：InspectorController 不再只做灰态锁定
- **现状**：节点属性面板的加载、自动保存、浏览文件、刷新窗口列表、字段锁定均已下沉到 `Interaction/InspectorController.cs`。
- **MainWindow 职责**：只保留 XAML 事件转发和窗口装配，不再维护属性面板业务规则。
- **维护规则**：新增节点属性 UI 后，同步改 `InspectorController.LoadNode()`、`ApplyChanges()`、`RefreshLocks()`，不要把属性逻辑写回 `MainWindow.xaml.cs`。

#### 变更 2：找图节点支持可选识别区域
- **字段链路**：`FindImageNodeViewModel` → `NodeFileModel` → `GraphRuntimeNode` → `FindImageNodeExecutor` → `Python/find_image.py`。
- **字段**：`UseFindImageRegion`、`FindImageRegionX/Y/Width/Height`。
- **行为**：未启用区域时全屏截图；启用区域时先裁剪指定屏幕区域，再做 OpenCV 模板匹配，输出坐标仍是屏幕绝对坐标。
- **安全规则**：启用区域但宽高无效时记 `Warn` 并继续，不当成致命错误。

#### 变更 3：GraphValidator 增加执行前警告
- **新增检查**：开始执行链不可达节点、找图路径空、找图区宽高无效、鼠标坐标缺省、键盘按键空、延迟值无效、启动程序路径空、选中窗口进程名空。
- **分级**：这些都是 `Warning`，用于执行前提示；只有无开始节点、多开始节点、重复 ID、坏连线、非法类型才是 `Error`。
- **目的**：提前暴露“不会执行/会跳过”的问题，但不阻断可退化流程。

#### 变更 4：GraphValidator 增加连线唯一性校验
- **执行输出**：同一个执行输出引脚出现多条连线是 `Error`。运行时只会取第一条，必须执行前阻止。
- **数据输入**：同一个数据输入引脚出现多条入线是 `Error`。执行输入允许多条入线，用于循环/汇入场景。
- **来源**：UI 创建连线时会自动替换旧线，但旧图/坏 JSON 加载时可能绕过该规则。

### 2026-06-02：EdgePan 边缘自动平移

参考 UE4 `SNodePanel::ComputeEdgePanAmount` 实现。

#### 功能
- 拖动节点或从引脚拉连线到达视口边界 30px 区域时，画布自动向拖动方向滚动
- 非线性加速：`0.15 * distance^0.6`，最大 5px/tick
- 除以缩放系数，高倍率放大时自动降低平移速度以保证精度

#### 实现位置
- `CanvasPanZoomController.EdgePan()` — 核心算法
- `GraphViewport_PreviewMouseMove` — 在节点拖动（`_dragNode is not null`）或连线拖拽（`IsConnecting`）时调用

#### 注意
- `_panTransform` 是屏幕空间（应用在 ScaleTransform 之后），方向与 UE4 的 `ViewOffset`（图空间）相反，所以 EdgePan 使用 `-=` 而非 `+=`

### 2026-06-02：Runtime/Interaction 解耦后的维护规则

#### 2026-06-03 启动崩溃：XAML 初始化事件早于 controller 创建
- **现象**：软件启动时直接崩溃，堆栈为 `NullReferenceException` at `MainWindow.FilterRadio_Checked`，调用链发生在 `InitializeComponent()` 内。
- **根因**：XAML 设置 RadioButton `IsChecked` 会触发 `Checked` 事件；但 `MainWindow` 的 controller 是在 `InitializeComponent()` 之后创建的，`_logPanelController` 当时仍为 `null`。
- **修复**：`FilterRadio_Checked` 顶部增加空保护：
  ```csharp
  if (_logPanelController is null)
      return;
  ```
- **维护规则**：任何由 XAML 直接绑定、且可能在初始化期触发的事件，如果要访问 controller/service，必须先判空或延迟绑定。重点检查 `Checked`、`SelectionChanged`、`TextChanged`、`Loaded`、`LayoutUpdated`。
- **验证**：`dotnet build .\AutomationStudioWpf.csproj` 通过，启动 exe 后进程保持运行。

#### 问题 1：MainWindow 继续膨胀会把 UI、交互、运行时重新耦合
- **现象**：画布平移、连线、图谱列表、执行入口、属性锁定全写在 `MainWindow.xaml.cs`，修改一个交互容易误伤另一个。
- **修复**：拆出 `Interaction/*Controller`：
  - `ExecutionController`
  - `GraphListController`
  - `CanvasPanZoomController`
  - `NodeDragSelectionController`
  - `PinConnectionController`
  - `InspectorController`
  - `NodePaletteController`
  - `LogPanelController`
  - `GraphImportDropController`
#### 当前识字/OCR 状态
- 当前软件不包含识字/OCR 节点。
- 当前软件不依赖 EasyOCR，也不做 EasyOCR 自动安装。
- 后续如果重新做识字功能，需要按独立插件节点接入，并先明确依赖策略。
- **教训**：`MainWindow` 只做事件转发和窗口装配。新交互不要直接塞进 `MainWindow`。

#### 问题 2：新增 controller 后大量 WPF/WinForms 类型歧义
- **现象**：编译报 `Button/TextBox/ListBox/MessageBox/MouseEventArgs/Control/Brushes/Color` 歧义。
- **根因**：项目同时启用 WPF 和 WinForms。
- **修复**：新增 controller 文件优先使用别名：
  - `using WpfTextBox = System.Windows.Controls.TextBox;`
  - `using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;`
  - `System.Windows.MessageBox.Show(...)`
- **教训**：在本项目里不要裸写常见 UI 类型名，除非当前文件没有 WinForms 命名空间污染。

#### 问题 3：工具栏删除按钮后必须同时删 XAML 和 code-behind
- **变更**：顶部工具栏已去掉 `删除所选节点`、`清除待连接引脚`。
- **保留行为**：`Delete` 仍删除选中节点；`Esc` 仍取消连线或取消执行。
- **教训**：WPF XAML 的 `Click="..."` 如果残留，编译期会失败；删按钮时一起删 handler。

#### 当前验证状态
- `dotnet build .\AutomationStudioWpf.csproj`：0 warning / 0 error。
- CodeGraph 已同步，PowerShell 若拦截 `codegraph.ps1`，使用 `codegraph.cmd sync`。

### 2026-05-30: 键盘模拟在游戏窗口无效——从 keybd_event 到 SendInput 扫描码

#### 问题：键盘节点在普通应用正常，游戏窗口完全无效
- **现象**：Space/A 等在浏览器/记事本正常，游戏内毫无作用，但手动按键正常
- **根因（3 层）**：
  1. `keybd_event` 是 Win95 遗留 API，现代 Windows 不可靠 → 改为 `SendInput`
  2. `INPUT` 结构体含 union，C# `LayoutKind.Sequential` 在 x64 上为 `ki` 添加了 +4 对齐填充，导致 `SendInput` 读到错位的内存数据。正确布局：`LayoutKind.Explicit, Size=40`，`type@offset 0`，`ki@offset 8`
  3. 游戏使用 DirectInput/RawInput 读取硬件扫描码，忽略 `SendInput` 的虚拟键码（wVk）。修复：`MapVirtualKey(vkCode, 0)` → `wVk=0, wScan=scanCode, dwFlags=KEYEVENTF_SCANCODE`，扩展键附加 `KEYEVENTF_EXTENDEDKEY`
- **教训**：
  - 永远使用 `SendInput`，不是 `keybd_event`
  - Win32 union 结构体 → `LayoutKind.Explicit` + `FieldOffset`；x64 上 `sizeof(INPUT)=40`，`ki@offset 8`
  - 游戏输入 = 扫描码模式 + 扩展键标志 + 正确的结构体布局

### 2026-05-30: C#↔Python 中文传参编码 + 图谱持久化 + 环路检测

#### 问题 1：C#→Python 命令行传中文全部损坏
- **现象**：`find_image.py` 收到中文路径 `'征神之路.png'` 变成乱码 `'寰佺涔嬭矾.png'`
- **根因**：`ProcessStartInfo.Arguments` 通过 Windows 命令行传参，中文被系统编码破坏
- **修复**：改用 JSON 临时文件通信
- **教训**：**永远不要通过命令行参数传递中文**。C#→Python 通信统一用 JSON 临时文件 + `new UTF8Encoding(false)`（无 BOM）

#### 问题 2：C# 写的 JSON 文件 Python 报 BOM 错误
- **根因**：`Encoding.UTF8` 默认写入 BOM（`EF BB BF`），Python `json.load` 拒绝
- **修复**：`new System.Text.UTF8Encoding(false)` — `false` = 不写 BOM
- **教训**：跨语言 JSON → C# 永远用 `new UTF8Encoding(false)`

#### 问题 3：环路检测阻止循环体内节点重复执行
- **现象**：找图节点放进 For/While 循环报"检测到执行环路"
- **修复**：移除 `visitedNodes` 检查。循环节点（ForLoop/WhileLoop）每次迭代创建新执行上下文，自身有终止条件保证安全
- **教训**：环路检测不应阻止循环体内的合法重复执行

#### 问题 4：每次执行图谱前 Python 检查卡顿数秒
- **根因**：`EnsurePythonAsync` 每次同步 import 6 个库（含 `torch`，import 极慢），且结果不缓存
- **修复**：首次检查后缓存（`_checked` + `_cachedResult`），后续直接返回
- **教训**：环境检测只做一次，启动时检查，执行时复用

#### 问题 5：新增图谱时旧图谱节点全部丢失
- **现象**：编辑图表1→新增图表2→重启→图表1节点全丢
- **根因**：`AddGraphListItem` 先清空编辑器（`NewGraph()`），然后才快照（`SnapshotActiveGraph()`），结果把空画布写回旧图谱
- **修复**：先 `SnapshotActiveGraph()` 存档当前图谱，再创建新图谱
- **教训**：切换/新增图谱前必须先快照当前编辑器状态，顺序反了数据就丢了

### 2026-05-29：图谱列表、右键菜单、选中窗口节点

#### 问题 1：切换图谱时不应提示保存
- **现象**：双击图谱切换时如果当前图谱 dirty，会弹是否保存，编辑体验差
- **修复**：切换图谱时调用 `SnapshotActiveGraph()` 静默把当前编辑器内容写回图谱列表项；只在关闭窗口时统一提示保存
- **教训**：图谱切换是导航行为，不是关闭行为。保存弹窗集中在退出软件和显式保存按钮

#### 问题 2：WPF ContextMenu 默认样式导致白边
- **现象**：图谱右键菜单出现白色边/默认系统 chrome，和暗色 UI 不一致
- **根因**：只设置 `Background/BorderBrush` 不会覆盖 `ContextMenu` / `MenuItem` 的默认 ControlTemplate
- **修复**：同时覆盖 `ContextMenu.Template` 和 `MenuItem.Template`，用暗色 `Border`、自定义 hover、`StackPanel IsItemsHost`
- **教训**：UE 风格暗色菜单不能只改属性，必须全量自绘模板

#### 问题 3：ContextMenu 不能直接使用 Popup 属性
- **现象**：`AllowsTransparency` / `PopupAnimation` 写在 `ContextMenu` 上编译报 `MC3072`
- **根因**：这些属性属于 `Popup`，不是 `ContextMenu`
- **修复**：移除这些属性。需要真正透明/动画时，改用 `Popup` 或 Canvas 浮层

#### 问题 4：选中窗口节点的 Win32 行为边界
- **现象**：`SetForegroundWindow` 可能返回 false
- **根因**：Windows 有前台窗口权限限制，非当前前台进程不一定能强制抢焦点
- **实现**：先 `ShowWindow(SW_RESTORE)`，再 `SetWindowPos(HWND_TOP)`，最后 `SetForegroundWindow`
- **策略**：找不到窗口或进程名为空是 `Warn + continue`；Win32 调用异常才是 `Error + stop`

#### 问题 5：沙箱内 WPF 构建可能报 obj Access denied
- **现象**：`dotnet build` 报 `Access to ... obj\Debug\net8.0-windows\App.g.cs is denied`
- **根因**：WPF MarkupCompile 会删除/重写 `obj` 文件，沙箱权限或 Rider/dotnet 占用会干扰
- **处理**：先查 `AutomationStudioWpf` / `dotnet` / Rider 进程；必要时使用本机权限重新 `dotnet build .\AutomationStudioWpf.csproj`

### 2026-05-29：拓展节点后的运行时与模型边界

#### 问题 1：前置输入已连接但运行时没有值时，鼠标节点会回退到本地坐标
- **现象**：`找图.center` 连到 `鼠标点击.position` 后，找图未命中时只写 `result=false`，不写 `center`；鼠标节点仍继续运行，并回退使用自身 `PositionX/Y`
- **风险**：可能点击 `(0,0)` 或旧坐标。自动化工具里这是高风险行为
- **修复方向**：区分“未连接输入”和“已连接但值缺失”。后者应记录 `Warn`，跳过当前鼠标节点并继续后续节点，不能回退到本地坐标

#### 问题 2：新增节点复用旧 DTO 字段导致模型漂移
- **现象**：`StartProgram` 和 `PrintLog` 为了快速落地，复用了 `NodeFileModel.ImagePath/DelayMs/ScrollSpeed` 等字段
- **风险**：保存文件语义不清，后续新增节点时容易出现字段互相污染
- **修复方向**：给 `NodeFileModel` 增加语义明确的新字段，例如 `ProgramPath`、`WaitTimeoutMs`、`RetryCount`、`PrintLogMessage`；旧字段只做兼容读取，不再作为新写入路径

#### 问题 3：打开旧/坏图谱不保证开始节点存在
- **现象**：`NewGraph()` 会创建开始节点，但 `LoadFromModel()` 不会兜底补开始节点
- **风险**：UI 看起来能编辑，执行时才报“没有开始节点”
- **修复方向**：加载模型后检查 `StartNodeViewModel` 是否存在；不存在则自动补一个默认开始节点，并同步 `NodeFactory` 序号

#### 当前审计状态
- `dotnet build AutomationStudioWpf.csproj` 已验证通过：0 warning / 0 error
- CodeGraph 当前可用；本轮 `codegraph.cmd sync` 已成功。若 PowerShell 策略拦截 `.ps1`，继续优先使用 `codegraph.cmd sync`。
- `.git/index.lock` 存在；提交前应确认没有 Git/Rider 进程占用，再清理锁文件

### 2026-05-29: ComboBox disabled text not gray unlike TextBox

#### Problem: If/While condition ComboBox shows black text instead of gray when input pin is connected
- **Symptom**: MouseClick/MouseMove TextBox correctly turns gray when locked by input pin connection, but If/While ComboBox stays black
- **Root cause**: WPF default template behavior is inconsistent:
  - `TextBox.IsEnabled = false` automatically renders text in gray
  - `ComboBox.IsEnabled = false` only changes border/background; ContentPresenter Foreground is NOT modified
- **Fix**: Manually set `cb.Foreground = Brushes.Gray` when locked, and `cb.ClearValue(Control.ForegroundProperty)` when unlocked
- **Lesson**: Do not assume consistent disabled visual states across WPF controls. ComboBox requires manual Foreground management.

### 2026-05-29: Preview event tunneling blocks Button Click inside popup

#### Problem: Clicking a node in the right-click palette does nothing
- **Symptom**: Node palette opens on right-click, but clicking a node inside it has no effect - no node created, palette not closed
- **Root cause**: `GraphViewport_PreviewMouseLeftButtonDown` is a Preview (tunneling) event, firing before Button Click. `IsGraphBlankSource` walks up the visual tree, hits `GraphViewport`, returns true. Handler sets `e.Handled = true`, swallowing the event and preventing Button Click from firing.
- **Fix**: Early-exit check at top of `GraphViewport_PreviewMouseLeftButtonDown`: if palette is visible and click is inside palette bounds, return immediately.
- **Lesson**: Preview events on parent containers intercept child interactions unless guarded. Always check if event target is inside a floating panel before handling at container level.

### 2026-05-29：中文乱码批量修复

#### 问题：项目文件中出现大量中文乱码
- **现象**：注释、region 标签、MessageBox 标题、SetStatus 文本全部变成乱码
- **根因**：
  1. **历史遗留**：项目早期在 GBK 编码环境下编写，后转为 UTF-8 但未重新编码
  2. **工具边界**：`StrReplaceFile` 替换包含中文的多行字符串时，如果 `old` 字符串跨越了 UTF-8 字节边界，会破坏相邻字符编码
- **修复**：用 Python 脚本扫描含中文的行，建立 `garbled -> correct` 映射表批量替换；单行乱码直接按行号覆写
- **教训**：不在 `StrReplaceFile` 的 `old/new` 中使用中文；新增中文优先用 `WriteFile` 或独立资源文件

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
11. **Runtime 不写具体节点能力**：具体行为写 `INodeExecutor`，底层能力写 `Adapters`
12. **前置输入优先且安全**：已连接输入缺值时 Warn + skip，不能回退本地默认值
13. **节点菜单来自 Registry**：不要在 `MainWindow` 硬编码节点分类/名称
14. **MainWindow 不继续膨胀**：新增交互优先写 `Interaction/*Controller`

## 构建与发布

### 开发构建
```bash
dotnet build
dotnet run
```

### 发布单文件版本
```bash
dotnet publish -c Release -r win-x64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true
```

输出位置：`bin/Release/net8.0-windows/win-x64/publish/`

## 版本历史

### v1.1.0 (2026-05-28)
- 重构：拆分 MainWindow.xaml.cs 到 Services 层
- 新增：Python 环境自动检测
- 优化：日志可复制、警告黄色显示
- 修复：属性命名、旧日志窗口重复创建问题

### v1.0.0
- 初始版本
- 蓝图节点编辑器基础功能
- 找图、鼠标、键盘、延迟等节点
## 2026-06-04：内容浏览器 + 脚本/函数库/宏库资产系统 v1

### 新资产层
- 底部新增“内容浏览器”，资产类型为 `Folder`、`Script`、`FunctionLibrary`、`MacroLibrary`。
- `Script` 等价 UE 蓝图，包含自己的事件图、私有函数、私有宏。
- `FunctionLibrary` / `MacroLibrary` 是全局库；库内函数/宏只有勾选 `公开到库` 后才会出现在其他脚本的节点搜索里。
- `ContentAssetViewModel` 持有 `EventGraphs / Functions / Macros` 三个集合。
- `CallableGraphItem` 是节点菜单和执行器使用的可调用函数/宏 DTO，包含稳定 `Id`、显示名、分组名、`GraphFileModel`。
- 事件图支持 `CustomEvent` / `CustomEventCall`。自定义事件只属于当前脚本事件图，调用节点通过 `CustomEventId` 绑定入口节点。

### UI 行为
- 启动默认隐藏 `EditorSurfaceHostRoot`，显示 `EmptyEditorPanel`，提示从内容浏览器打开资产；打开资产后把该 session 的 `EditorSurfaceControl` 放入 host。
- 底部左侧为内容浏览器，右侧为日志，中间 `GridSplitter` 可调比例。
- 打开脚本：显示事件图、自定义函数、宏、画布和属性面板。
- 打开函数库：只显示函数列表、画布和属性面板。
- 打开宏库：只显示宏列表、画布和属性面板。
- `RunGraph_Click` 只允许脚本里的事件图直接执行。

### 保存与迁移
- `GraphLibraryService.SaveContentLibrary()` 使用新版 `ContentAssets` 字段保存全部内容资产。
- 旧 `graph-library.json` 兼容读取：旧 `Graphs` 迁移到默认脚本事件图，旧 `Functions` 迁移到默认脚本私有函数，旧 `Macros` 迁移到默认脚本私有宏。
- 新保存后以 `ContentAssets` 为准。

### 调用范围
- `CallableGraphResolver` 是函数/宏可调用项的唯一来源，节点菜单、编译同步、运行时都必须走它。
- 脚本内只能调用本脚本私有函数/宏，以及函数库/宏库中已勾选 `公开到库` 的函数/宏。
- 函数库/宏库内部可以调用本库私有项；其他脚本不能搜索、编译同步或运行未公开库项。
- 编译错误路径必须用内容浏览器完整路径：`content/父文件夹/.../资产/图`。函数库、宏库在文件夹内时报错也必须带完整层级。
- 右键节点菜单按 `本脚本函数`、`本脚本宏`、`函数库`、`宏库` 分组。
- 库函数/宏显示为 `库名/函数名` 或 `库名/宏名`，运行时仍用稳定 ID，不靠名字解析。
- 自定义事件显示在节点菜单 `本脚本事件` 分组，只能在当前脚本事件图内调用，不跨脚本/函数库/宏库。

### 重要坑点
- 打开节点菜单前必须 `SnapshotActiveAsset()`，否则函数/宏参数刚改完但未写回 `GraphFileModel`，调用节点会缺 pin。
- `GraphListController.LoadItem(item, snapshotCurrent: false)` 用于上层资产切换；跨事件图/函数/宏切换由 `MainWindow` 统一快照，避免图谱混写。
- `ExecutionController`、`NodePaletteController`、`GraphCallReferenceSyncService` 都读取 `CallableGraphResolver` 产出的 `CallableGraphItem`，不要直接扫全局 `FunctionListItems/MacroListItems`。
- `公开到库` 是硬隔离：旧图如果跨脚本引用未公开库项，编译时报错并保留 dirty，不自动删节点。
- `CustomEventCall` 在当前 `GraphExecutionPlan` 内找 `CustomEventId` 对应入口；运行时用 `custom_event:{id}` 调用栈阻止递归。
- WPF + WinForms 命名冲突仍要用全限定名，尤其 `Brushes`、`Color`、`Cursors`、`HorizontalAlignment`。
## 2026-06-04 恢复记录：编译系统、dirty 规则、左侧折叠栏

- 当前远端基线 `be3b34f Add content browser asset system` 不包含上一轮未提交改动；恢复时在该提交基础上补回，不做 git 回退。
- 编译系统由 `GraphCompileService` + `GraphCallReferenceSyncService` 负责；编译时同步 `function_call` / `macro_call` 的参数引脚和宏出口，并删除失效连线。
- dirty 分级：节点移动/布局变化只调用 `MarkLayoutDirty()`；节点参数、连线、函数/宏签名变化调用 `MarkLogicDirty()` 并设置 `IsCompileDirty`。
- 左侧图谱栏为三块独立区域：事件图表、函数、宏；空列表折叠，新增后展开，删除到空后清空画布。
- 新建脚本/函数库/宏库默认不自动创建图表，用户点击对应 `+` 后才创建。
# AutomationStudio recovery note (2026-06-04)

- Content browser keeps `ContentBrowserItems` as root data, projects folders into `ContentFolderItems`, and projects current-folder tiles into `ContentVisibleItems`.
- Folder tree is left-side only; tile grid is right-side. Empty folders do not show an expander because `HasFolderChildren` only counts child folders.
- Folder tree indentation uses `TreeIndent`; `TreeDisplayName` must stay plain name. The tree/tile split is adjustable through `ContentBrowserTreeSplitter`.
- Asset tiles expose `TileGlyph` / `TileBrush` by `ContentAssetKind`; inline rename still binds `IsEditing`.
- Asset drag/drop to folder supports move/copy/cancel. Copy creates new asset/graph IDs and deep-copies graph DTO data.
- Graph sidebar keeps separate event/function/macro controllers. Empty sections collapse; adding expands; deleting last item clears canvas through `GraphListController`.
- Section collapse state is transient per opened asset. `*SectionHasState` distinguishes user-collapsed non-empty sections from never-toggled default sections.
- Dirty graph sections show orange header badges; dirty graph items show `*` plus orange item highlighting.
- `MarkLogicDirty()` marks compile dirty. `MarkLayoutDirty()` only marks save dirty.
- New graph/function/macro list items start with `IsCompileDirty = true`; compile clears graph dirty flags only after signature/call-node sync and validation succeed.
- Function/macro library rows persist `IsPublicToLibrary`; only public rows appear in node search, compile sync, and runtime lookup from other scripts.
- `CustomEvent` stores `CustomEventId`; call nodes serialize it separately from `FunctionId` / `MacroId`. Do not reuse function/macro ids for events.
- `GraphLibraryService` defaults to `%APPDATA%/AutomationStudioWpf`, but tooling may set `AUTOMATION_STUDIO_LIBRARY_DIR` for isolated smoke tests.
- Compile only clears graph compile flags when validation succeeds, and marks only assets changed by call-reference sync as save dirty.
- `Window_PreviewKeyDown` routes `Delete` / `F2` to focused graph/content list, while text boxes keep normal editing behavior.
- Content tree commands track `_contentFolderSelectionActive` so folder right-click/`Delete`/`F2` cannot act on a stale tile selection.
- Build gate: `dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck` must stay `0 warning / 0 error`.
