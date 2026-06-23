# AutomationStudioWpf 开发 Skill

## 用途

WPF 可视化节点自动化编辑器，类似 UE 蓝图。技术栈：C# 12 / .NET 8.0-windows，零外部 NuGet 包。完整架构和历史细节看 `TECHNICAL.md`；本文件只保留 Agent 高频约束。

## 沟通 / Git

- 默认中文，简洁。中间进度只说：在做什么、发现什么、下一步。
- 保留准确文件名、类名、方法名、命令、错误文本。
- 不自动 `git push`。只有用户明确说推送才推；推送前确认不包含测试功能相关文件夹。
- 不恢复旧 `.kimi/skills/automation-studio-wpf/SKILL.md`。
- `Tests/CodexSmoke` 是本地-only，Git 不跟踪、不提交。只在用户明确要求或触碰高风险交互时本地跑。

## 当前产品边界

- 只保留脚本 (`Script`) + 函数库 (`FunctionLibrary`)。
- 宏库已废弃；不要恢复 UI、资产类型、运行时、节点菜单、文档功能说明。
- 旧 `macro_entry` / `macro_output` / `macro_call` 只允许作为旧文件 skip guard，打开旧文件不崩即可。
- `GraphValidator` 不再把孤立节点写成 warning；只有真正影响执行的校验问题才进 log。
- 不改 graph / node / connection JSON schema，除非用户明确要求。
- 不改 `ConnectionSplinePlanner` 线形，除非有新明确复现。

## 多窗口 / Session 硬规则

- 每个打开资产一个 `EditorSessionViewModel`。重复打开同资产只聚焦已有 session。
- 每个 session 自持 `GraphEditorService`、`NodeFactory`、`GraphCommandService`、`EditorSurfaceControl`、`EditorSurfaceContext`。
- 主窗口标签栏只显示 `DockMode != Detached` 的 session；拖出主窗口创建 `DetachedEditorWindow`，直接 host 自己的 `Surface`。
- Detached 激活只切 toolbar/global-command 目标，不覆盖 `_lastMainEditorSession`，不搬主窗口 surface。
- `EditorSurfaceContext.Configure(...)` 必须幂等；不要因 attach/activate 重建 controller。
- Surface 事件先分类：明确点击/键盘/按钮等用户交互才提升 active session；`PinAnchorLoaded/LayoutUpdated`、无按键 `MouseMove`、初始化 `TextChanged/SelectionChanged` 不切 active。
- 全局窗口事件用 `TryGetActiveEditorSurface()` 或事件来源 surface；无 surface 就 no-op，不抛 `No active editor surface is available.`。
- WPF parent walk 必须用安全 helper；不要裸调 `VisualTreeHelper.GetParent(...)` 处理任意 `DependencyObject`，`FlowDocument` 会崩。

## 图切换 / 函数库保存硬规则

- 当前图 controller 必须通过 `SetSessionActiveGraphController(session, controller)` 写入。
- 该入口同步 `session.SurfaceContext.ActiveAssetController`、session remembered graph，只在当前操作 session 时镜像 `_activeAssetController`。
- 切换事件图/函数顺序固定：先 snapshot 旧图，再设置目标 controller，再 `LoadItem(..., snapshotCurrent: false)`，再 remember。
- 不要在 session controller 仍指向旧图或为空时调用 `GraphListController.Load()`；它内部会 persist，顺序错会把函数图写回错图或重载默认 entry/return。
- 保存、退出、编译、运行、函数调用解析前走 `CommitInspectorAndSnapshotAllSessions()` / `CommitAllSessionsToAssets()`。
- Toolbar 编译是 active-asset scoped：脚本编译该资产内事件图+函数；函数库编译该库全部函数。
- 编译成功后立即同步清对应 session 的 `IsCompileDirty`、黄点、窗口栏、section badge、编译按钮。
- 执行前若存在 `IsCompileDirty` 图，`EnsureCompiledBeforeRun()` 自动编译；成功继续执行并清提示，失败停止。

## 内容浏览器

- 内容浏览器数据源是 `ContentBrowserItems`；左树 `ContentFolderItems`，右瓦片 `ContentVisibleItems`。
- 刷新/搜索/定位走 `ContentBrowserIndex`：`assetById`、children lookup、folder lookup、path cache、search entries。
- 资产新增、删除、重命名、移动后必须走 `RefreshContentBrowserViews()` 或显式重建 index。
- 搜索范围是当前目录递归；支持空格关键字、路径片段、subsequence、不区分大小写。
- `Ctrl+B` 定位：选中搜索结果/资产时进入真实父目录并选中；无选中项时定位当前打开资产。
- 函数调用节点双击按 stable `FunctionId` 找目标资产/函数图，不按显示名解析。
- 内容浏览器新增资产只走空白右键菜单；菜单项只保留 `脚本 / 文件夹 / 函数库`。

## ToDo / 编号

- 非 `Reroute` 节点有图内可复用 `NodeNumber`：事件图 `N###`，函数图 `Fun###`。
- 删除节点释放编号；新增节点拿当前图最小空闲编号。
- `ToDoNodeViewModel` 用 `TargetNodeTitle + TargetNodeNumber` 双键在当前图内跳转；`TargetNodeId` 只作编辑器维护引用。
- ToDo 静态下拉选择必须立即写 VM：`TargetNodeTitle`、`TargetNodeNumber`、`TargetNodeId`，并 snapshot。
- `target_title` / `target_number` 输入 pin 是运行时 override，不能清掉静态下拉值。
- `ReturnAfterTarget=false` 是 Goto；`true` 目标链结束后走源 ToDo 的 `exec_out`。
- Return 模式目标链自然回到源 ToDo 时应停止子链并返回，不是递归错误；直接自跳仍非法。

## 连线 / Reroute

- XAML 可见线绑定 `GraphEditorService.ConnectionPaths`；持久化和 runtime 使用 `Connections`。
- `ConnectionPathViewModel` 聚合线性 reroute 链，顺序来自真实 `Connections` 拓扑，不按点距离重排。
- `ConnectionPathViewModel.FindNearestConnection` 必须用可见 Bezier 采样映射 backing `ConnectionViewModel`；不要退回端点直线命中。
- `GraphEditorService.RunBatchedEdit(...)` 用于组合连接编辑；批量内只标脏，最外层 flush `ConnectionPaths` 和 `GraphChanged`。
- `ConnectionSettings` / `SplineTangentCalculator` 当前不驱动画布可见线；改它们不会改变线形。

## Runtime / 节点扩展

- 新节点至少更新：`GraphTypes.NodeKind`、ViewModel、`NodeFactory`、`NodeSerializer`、executor、`NodeRegistry.CreateDefaultDefinitions()`。
- 默认节点标题是用户可见 UI，不是 schema；新标题不要带冗余 `节点` 后缀。`Start` 显示 `开始运行`。
- 新 runtime 字段要语义明确；不要复用不相关 DTO 字段。
- `NodeRegistry.Definitions` 是节点菜单来源；不要在 `MainWindow` 手写节点列表。
- 前置输入解析统一走 `RuntimeContext` raw resolver：按目标 pin 找连接，先求值纯节点，再取上游 raw 输出；字符串、布尔、坐标、函数参数和函数返回不要各写一套“上游没输出”判断。
- `多线程` 是结构节点：`MultiThreadNodeViewModel` 保存动态 `线程N` 输出数量，`GraphRuntimeExecutor` 并行执行连接分支，全部完成后走 `exec_completed`；鼠标/键盘/窗口类节点在并行分支中走全局设备锁。
- 函数库函数默认不可被其它脚本搜索/调用；只有 `IsPublicToLibrary` 勾选后才公开。
- `CallableGraphResolver` 是 palette、compile sync、runtime lookup 统一来源。
- 函数库可调用项在节点菜单里按库资产名分组；添加到画布后的 `FunctionCallNodeViewModel.Title` 只显示函数名，不显示 `库名/函数名`。跳转/运行仍用 stable `FunctionId`。
- 运行时递归用 call stack key 拦截；合法循环由 For/While 自己控制。

## UI / 主题 / 日志

- `DarkContextMenuStyle`、`DarkDropdownListBoxStyle`、`DarkDropdownListBoxItemStyle` 和 editor surface 常用 brush 在 `App.xaml`。
- 节点 header、pin、日志级别、编译按钮、弹窗常用 brush 用静态冻结 brush 复用；不要在高频 getter / 日志追加 / dirty 刷新里反复 `new SolidColorBrush(...)`。
- 日志面板是只读 `RichTextBox`。全局快捷键必须对 `TextBoxBase` 放行，避免 `Ctrl+C` 被节点复制截获。
- `LogPanelController` / `LogWindow` 追加日志要增量处理；过滤/清空才全量刷新。
- 日志过滤 `RadioButton` checked dot 必须可见，避免用户不知道当前过滤级别。
- 执行日志由 runtime 聚合成节点块，时间戳只到秒；节点内部细碎 info/warn/error 默认被捕获到块内详情。日志多行内容在 UI 上做对齐显示，但复制文本保持原样。
- `ExecutionController` 运行中会禁用执行按钮并改成 `执行中...`，防重复点击。
- XAML 初始化期事件要容忍 controller/service 为空，尤其 `Checked`、`SelectionChanged`、`TextChanged`、`Loaded`、`LayoutUpdated`。
- 鼠标拾取是 editor 工具，不是 runtime 节点；只在鼠标坐标变化时采样/更新，浮窗和复制选择窗要 clamp 到当前屏幕工作区内；复制坐标/颜色后退出，取消继续；退出、窗口关闭、异常路径必须 unhook、释放 DC、关 overlay。
- 找图节点的 Python 桥接用 `np.fromfile(...) + cv2.imdecode(...)` 处理中文路径；不要再回退到 `cv2.imread(...)`。

## 文件职责

- `MainWindow.xaml.cs` 只保留窗口装配和 Binding 暴露。
- `MainWindow.GraphInputHandlers.cs`：画布、节点、pin、节点菜单输入。
- `MainWindow.GraphListHandlers.cs`：事件图/函数列表、分组展开、公开到库。
- `MainWindow.EditorSurfaceControllers.cs`：surface controller 初始化、typed surface event dispatch。
- `MainWindow.EditorSessionWorkflow.cs`：资产打开/切换/关闭、session 提交、callable 解析入口。
- `MainWindow.AssetCommands.cs`：toolbar 新建/打开/保存/编译/运行。
- `MainWindow.ContentBrowserCommands.cs`：内容浏览器基础 CRUD、目录投影刷新。
- `MainWindow.ContentBrowserMultiSelect.cs`：多选、框选、复制粘贴、拖拽预览、多删除。
- `InspectorController.cs` 只保留 `LoadNode()` / `ApplyChanges()` 主分发；参数、通用节点、系统节点、锁定、ToDo 分别在 partial。

## 验证门禁

默认改动完成后：

```powershell
dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck
git diff --check -- AutomationStudio
codegraph.cmd sync
```

UI/XAML/启动路径改动后追加短启动探测：

```powershell
dotnet run --project .\AutomationStudioWpf.csproj --no-build
```

如果启动快速退出、输出 `Unhandled exception` 或 WPF 初始化异常，先修崩溃。

## 细节来源

- 完整架构、历史踩坑、扩展指南：`TECHNICAL.md`
- 用户入口和功能概览：`README.md`
- 本轮高风险记忆：`agentmemory.md`
