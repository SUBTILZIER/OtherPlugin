# 技术文档

## 阅读 / 维护规则

- 后续开发先读本文“大纲 / 索引”。只有确认当前改动命中某个主题时，再细读对应章节，避免无意义消耗上下文。
- 重要优化、反复踩坑修复、架构边界变化必须记录到本文件，避免后续重复修改同一问题。
- 本文件是唯一长期技术文档：记录开发规则、踩坑、架构边界、重要知识点。旧 `TECHNICAL.md`、`agentmemory.md`、项目 skill 文档不再维护。

## 大纲 / 索引

- 文档迁移记录：唯一技术文档位置、旧文档删除规则、后续维护入口。
- 架构设计：模块边界、Interaction / Services / Runtime 职责。
- 运行态 / 日志 / 找图：`ExecutionController`、`RuntimeContext`、结构化日志、Python 找图桥接。
- 热键 / 托盘 / 工具栏执行状态：全局热键、脚本属性窗、运行中冻结遮罩、托盘退出。
- 编辑器视觉规则：左侧图表栏、右侧细节面板、工具栏、弹窗、主题资源。
- 资产系统：脚本事件图、辅助事件图、函数库、内容浏览器索引和重命名规则。
- 多窗口 / EditorSurface：session、detached 窗口、active surface 安全规则。
- 编译 / 保存 / 运行：session snapshot、active asset 编译、函数库保存防丢图。
- 连线 / 路由点：`Connections` 持久化、`ConnectionPaths` 视觉路径、命中和批量更新。
- 节点规则：执行节点/纯运算节点、多线程、ToDo、函数调用、参数默认值、Inspector 输入焦点。
- 稳定性 / 数据安全：多线程取消、Python 唯一环境、原子 JSON 保存、关闭顺序、有界 Undo。
- 发布 / 安装：只读安装目录、私有 Python、崩溃报告、单实例升级 IPC、签名与 Inno 安装器。
- 验证 / 文档门禁：构建、启动探针、Git、CodeGraph、本地-only smoke。

## 文档迁移记录

- 2026-07-02 起，长期技术文档只维护本文件：`AutomationStudio/Agent/TECHNICAL.md`。
- 旧文件 `AutomationStudio/TECHNICAL.md`、`AutomationStudio/agentmemory.md`、`AutomationStudio/Agent/skills/automation-studio-wpf/SKILL.md` 已删除，Git 上也应同步删除。
- 后续开发先看上方“大纲 / 索引”。确认当前任务涉及某个主题后，再跳到对应章节细读。
- 新增踩坑、架构约束、重要优化结论时，只写入本文件；README 只保留用户入口和必要指向，不复制技术细节。

### 2026-09-08：执行、Hook、日志和主题稳定性收口

- Python 进程退出后，stdout/stderr drain 必须有明确上限。禁止在正常退出路径直接调用 GetAwaiter().GetResult() 无限等待；超时先终止进程树，再决定是否保留请求文件。
- ScriptHotkeyService.Refresh、鼠标拾取 Hook 安装和 Dispatcher 投递都必须先检查 disposed/shutdown 状态。模块句柄或 Hook 安装失败只能返回结构化错误，不能让 UI 线程未处理异常。
- GraphDependencyIndex 遇到重复 graph/function/main-event ID 必须产生 issue；解析层禁止用 GroupBy(...).First() 静默决定目标。坏索引只能用于报告错误，不能进入执行。
- GraphWorkspaceReadModel 的操作级缓存可能被预览、编译和热键任务并发读取；内部缓存必须使用并发容器或等价同步保护。
- 用户数据目录只能按 %AppData% -> %LocalAppData% -> 用户目录 -> %TEMP% 逐级降级，禁止把安装目录作为写入回退路径。所有候选目录都要先做可写探测。
- 高频日志不能阻塞节点执行线程。Info 日志进入有界后台写入队列；Error 日志在队列压力下保留同步兜底。UI 仍使用现有增量追加和条数上限。
- 主题相关 UI 必须使用 DynamicResource token。选择框、节点编号 chip、ToDo 编号、脏编译图标等不得重新写固定 RGB；新增 token 必须同时加入 App.xaml 默认资源和 AppThemeService 两套 palette。
- 发布验证选择产物时必须确定性排序；不能依赖文件系统枚举顺序。并行 Debug/Release 不得共用同一中间目录，否则 CS2012 可能只是构建竞态。

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
    ├─ CanvasPanZoomController   ← 右键平移、滚轮缩放、键盘重置、坐标转换
    ├─ NodeDragSelectionController ← 节点拖动、框选、多选、复制粘贴、对齐
    ├─ PinConnectionController   ← 拖线、连线、断线、预览线、路由节点插入
    ├─ InspectorController       ← 属性面板加载、自动保存、浏览对话框、窗口列表、字段锁定和灰态
    ├─ NodePaletteController     ← 右键节点菜单，来自 NodeRegistry.Definitions
    ├─ LogPanelController        ← 日志过滤、增量刷新、清空
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

### 2026-07-20：只读图工作区与编译流水线

- `GraphWorkspaceSnapshotFactory` 是编译、运行依赖分析、最终代码预览的只读边界。它使用 `GraphModelCopyMapper` 显式深复制，不允许用 JSON 往返代替长期 copy mapper，也不得保留 live ViewModel 引用。
- `GraphDependencyIndexBuilder` 每个操作周期只构建一次；函数、自定义事件、MainEvent、调用边、Python 可达性必须从同一 index 读取，禁止编译/运行/预览各自再写一套 lookup。
- 自定义事件作用域仅限同一脚本；跨资产不可见。公开函数只允许来自函数库，不能因脚本函数误设公开标志而跨脚本暴露。
- 编译固定顺序：`Prepare live model -> snapshot -> dependency index -> call reference sync -> 必要时重建 snapshot/index -> read-only Validate -> 成功后清 compile dirty`。
- `GraphPreparationService` 是编译期间唯一允许自动修复 live model 的服务：入口结构、节点编号、旧数据兼容、辅助图 Start、ToDo 静态引用。修复必须标 save dirty，并返回 repair message。
- `GraphValidationService` 只读 snapshot；禁止 `Validate*()` 内调用 `Ensure*()`、修改节点、连接或 dirty。
- `GraphCompilePipeline` 不写磁盘。持久化由上层显式决定；编译失败不得清 compile dirty，自动修复内容仍保留 save dirty。
- snapshot 有空/重复资产、图、节点或参数 ID 时，视为基础结构 fatal：跳过调用引用同步，先输出稳定校验错误，禁止在坏图上继续 mutation。
- Git 跟踪的 `Tests/AutomationStudio.CoreTests` 覆盖结构归一化、依赖索引、克隆、兼容迁移和编译流水线；`Tests/CodexSmoke` 继续本地-only、不得提交。
- `EditorWorkspace` 只拥有 `Sessions / MainSessions / ActiveSession / LastMainSession` 状态；它不执行窗口宿主迁移、编译、保存或 UI 刷新。MainWindow 中旧 session 字段名仅是该状态对象的兼容代理。
- `WorkspaceCommitService` 是 session controller 到 `ContentAssetViewModel` 的唯一提交边界；负责应用当前 inspector、snapshot 当前图、记忆 active graph、再写回资产。它不得写磁盘。
- `WorkspacePersistenceService` 是 `GraphLibraryService.SaveContentLibrary(...)` 的唯一真实调用点。保存失败由上层保留 dirty 并提示；成功后才允许刷新热键注册。
- `AssetCompileCoordinator` 固定执行 commit + compile pipeline，但不持久化；编译目标优先取 active session，只有没有 session 时才回退 active content asset。
- `ScriptExecutionCoordinator` 负责手动工具栏执行的编译前置、脚本资格检查和 `ExecutionController` 路由。MainWindow handler 只转发事件和显示主题提示，普通/主题 handler 不得各维护一套执行逻辑。
- `NodeDescriptorCatalog` 是 `NodeKind ↔ TypeKey`、pure/exec/number/delete traits、基础 pin、创建/序列化/runtime/preview 能力的唯一核心数据源。legacy type key 只能作为 alias 读取，写出必须使用 canonical type key。
- `NodePresentationCatalog` 单独保存显示名、菜单分类、Inspector 类型和主题语义 key；核心 descriptor 不携带 WPF 展示文案。
- `NodeRegistry` 不再手写第二套节点定义，只把 core descriptor + presentation descriptor 适配为 `INodeDefinition`，并独立注册 executor。节点菜单只读取 `CanCreate=true` 的定义。
- `NodeTraits`、`NodeFactory`、`NodeSerializer`、编译准备和节点菜单 type-key 解析必须复用 catalog；禁止再新增 `NodeKindFromTypeKey` switch。
- `NodeDescriptorCoverageTests` 强制覆盖全部 `NodeKind`、canonical type key 唯一性、可创建节点 round-trip、executor/pure evaluator 能力和不可删除边界节点。

### 2026-07-21：执行预检、依赖传播与操作级 ReadModel

- 手动执行和全局热键执行必须统一经过 `GraphExecutionPreflightService`。即使 `compile dirty=false`，仍要检查 MainEvent、可达图、未知节点、重复 ID、坏 pin、非法连接和缺失调用目标；坏图不得通过过滤节点或 `GroupBy(...).First()` 降级运行。
- `GraphRuntimePlanBuilder` 是 snapshot 到 runtime plan 的唯一严格入口。它返回 plan + issues，不得静默丢弃未知节点、重复节点或无效连接。
- `GraphDependencyIndex` 同时维护正向调用边和反向 caller 边。函数库变更时，`GraphCompilePipeline` 按反向依赖闭包同步受影响调用方；同步失败时保留相关资产 `compile dirty`，不得全项目无差别清理。
- 自定义事件 catalog 扫描同一脚本全部事件图。MainEvent 可调用辅助图事件，但禁止跨脚本调用；空事件 ID 可生成并同步明确引用，重复事件 ID 不猜目标、不静默重定向，必须阻止编译。
- `GraphSnapshot` 不暴露内部可变 `GraphFileModel`；需要编辑副本时只能调用 `ToMutableModel()`。snapshot 创建后，live ViewModel 后续修改不得污染当前编译、预检或预览。
- `GraphWorkspaceReadModel` 绑定“一份 snapshot + 一份 dependency index + MainEvent reachability cache”，生命周期只覆盖一次编译、运行、预览或菜单打开操作。禁止跨 UI 编辑长期缓存。
- `GraphCompilePipelineResult` 携带同步/校验完成后的最终 read model。手动运行和热键循环直接复用该对象，禁止编译成功后再次全量 snapshot/index。
- 执行目标在工具栏点击时固定为该 session 的 `assetId`，随后把 ID 显式传给 `ExecutionController`；不得在预检前再次读取可能已变化的 active asset。
- 运行库和最终代码必须按可达 graph ID 收集函数。脚本调用公开库函数后，该函数调用的同库私有 helper 仍属于可达执行链，不能再按“脚本菜单可见函数”过滤掉。
- `FinalCodePreviewService` 负责只读 plan/callable 组装；MainWindow 只提交 session、调用 service、显示窗口。生成失败不得清空上一次成功预览。
- 节点菜单在 `Open()` 时只构建一次函数/自定义事件 catalog；搜索过滤复用该 catalog，`Close()` 后释放。禁止每次输入字符都 commit workspace 并重建两次 index。
- MainWindow 不得直接创建 `GraphRuntimePlanBuilder`、`GraphDependencyIndexBuilder` 或 `RuntimeAssetLibrary`。相关职责分别属于 preflight、compile pipeline、preview service 和 workspace read-model service。
- `Tests/AutomationStudio.CoreTests` 当前覆盖严格预检、结构归一化、跨图事件、反向依赖传播、编译同步及 snapshot 隔离；新增执行/编译规则必须先补定向测试。

### 2026-06-22：运行态 / 日志 / 找图补充

- `ExecutionController` 在执行开始时把按钮切到 `执行中...` 并禁用，执行结束、失败、取消或校验失败后统一恢复，避免重复点击触发多次运行。
- `RuntimeContext` 是运行时输入解析唯一入口：按目标 pin 找连接，先求值纯节点，再读取上游 raw 输出；字符串、布尔、坐标、函数参数和函数返回都走该底层 resolver，避免“已连接但误报上游没有输出”。
- 每个执行节点结束后，`GraphRuntimeExecutor` 会把标准执行记录写入 `RuntimeContext` 临时缓冲区：`__executed`、`__status`、`__success`、`__message`、`__next_pin`；有 `result` 输出 pin 但 executor 未写时会兜底写入。纯运算节点按需求值成功后也写入同一缓冲。`__*` 内部键不显示在结构化日志返回结果里。
- 数据 `Reroute` 对 runtime 输入解析是透明节点：raw resolver 会沿数据转接点继续追溯上游真实输出，支持上上游/更远上游通过 reroute 供值。
- 执行日志由 `GraphRuntimeExecutor.ExecuteNode(...)` 统一捕获节点内部细碎日志，并输出“执行节点 / 名称 / 耗时 / 执行结果 / 返回结果 / 详情”块；`Logger.Timestamp` 只保留 `HH:mm:ss`。
- capture 期间的节点内部日志只写日志文件并进入结构化块，不得再单独排入 UI；`Logger.WriteDirect(...)` 仅用于最终结构化块，避免同一节点重复两份可见日志。
- 日志面板和独立日志窗口共用 `LogEntryDocumentRenderer`；UI 视觉上按 `[时间] [LEVEL] ` 前缀宽度做多行对齐，但复制文本保持原始内容，不补缩进空格。
- 日志复制和鼠标拾取复制都走 `ClipboardHelper.TrySetText(...)`，遇到 `CLIPBRD_E_CANT_OPEN` 只重试/提示，不允许崩溃进程。
- 所有只读文本窗口（含“显示最终代码”）也必须拦截 `ApplicationCommands.Copy` 并走 `ClipboardHelper`；禁止直接调用未保护的 `TextBox.Copy()`。
- 查询节点的 `False` 是业务结果，不是执行警告；例如 `WindowExists` 不存在、`FindImage` 未命中时写 `result=False` 且日志级别保持 INFO，只有配置错误、路径无效或执行失败才 WARN/ERROR。
- `FindImageNodeExecutor` 不再把 Python 原始 stderr/stdout 整段刷进日志；`Python/find_image.py` 通过 `np.fromfile(...) + cv2.imdecode(...)` 读取模板/截图，避免 Windows 中文路径失效。


## 2026-06-27：热键系统 / 托盘 / 工具栏执行状态

### 全局热键服务 (`ScriptHotkeyService`)
- `WH_KEYBOARD_LL` / `WH_MOUSE_LL` 全局低层级钩子，不依赖窗口焦点。
- 支持键盘按键、鼠标按钮（左/右/中/侧键X1X2）、鼠标滚轮（WM_MOUSEWHEEL，delta>0→WheelForward，delta<0→WheelBackward）。
- 每个绑定独立 `TriggerWindowMs`（100-10000ms），在时间窗内累计按下次数；达到次数且没有更高次数候选时立即触发。
- `ToMatchKey` 为三元组 (InputKind, Key, PressCount)；时间窗不参与匹配键，独立使用。
- 同一物理按键存在更高按下次数绑定时，低次数动作必须等高次数候选的时间窗结束后再触发；timer 不得在首个 80ms tick 提前触发单击，破坏双击/多击绑定。
- 热键捕获统一走 `HotkeyCaptureCoordinator`。捕获前调用 `ScriptHotkeyService.SuspendTriggers()`；捕获期间保留 hook 但禁止触发绑定，进入/退出均清空按次计数，恢复必须放在 `finally`。手动调试或任意热键脚本运行时禁止进入捕获。
- keyboard/mouse hook 按实际绑定类型分别安装。`SetWindowsHookEx` 返回零时必须保留 `Marshal.GetLastWin32Error()`，写 ERROR、状态栏和主题提示；一个 hook 失败不能卸掉另一个成功 hook，同一错误本次运行只提示一次。
- 启动热键对启用脚本常驻注册；终止热键只在对应资产存在活跃 `ScriptRunManager` 热键任务时注册。`RunningStateChanged` 必须立即刷新绑定，手动调试不能启用终止热键。
- `StopFromHotkey(asset)` 只有首次接受活跃任务的取消请求时返回 `true`；停止蜂鸣必须放在该返回值之后。空闲、已取消或过期回调不得发声。

### 热键属性窗 (`ScriptPropertiesWindow`)
- 热键行固定列布局，避免按钮遮挡文字：`启动热键 | 按键 [keyBadge] | 修改 | 按下次数 [TextBox] | 清空`。
- 按键未设置显示"无"；只显示实际按键名，不显示“键盘/鼠标”前缀；下方单独显示"触发时间阈值"输入框（ms，默认1000）。
- 所有控件带中文 ToolTip 解释。
- `ScriptHotkeyCaptureWindow` 支持键盘、鼠标按钮、鼠标滚轮捕获；`_captured` 防双重 `DialogResult`。

### 脚本运行管理 (`ScriptRunManager`)
- `ScriptRunManager` 只拥有全局热键运行；`ExecutionController` 只拥有工具栏手动调试。使用 `IsAnyHotkeyRunActive` / `IsManualDebugRunning` 区分来源，禁止用一个笼统状态互相取消。
- `StartFromHotkeyAsync` 入口处理重复启动（PreventDuplicateRun 忽略 / 否则取消旧任务重启）；`StopFromHotkey(asset)` 只停止对应资产。
- `ScriptLoopMode.Duration` 的时长必须大于 0；脚本属性窗、主界面脚本属性摘要和运行入口都要兜底阻止 `0:0:0` 静默结束。
- `UntilStopped` 配置了启动热键时必须同时配置终止热键；属性窗阻止保存，运行入口再次兜底拒绝启动。
- 热键脚本运行期间禁止关闭该资产的“脚本启用”开关，否则刷新绑定会卸载终止热键；必须先用终止热键或顶部停止按钮结束运行。
- 运行中的脚本资产禁止删除；单选、多选、右键和快捷键删除入口必须统一走运行态保护，避免终止绑定和运行资产引用失效。
- 热键脚本运行期间禁止修改热键与循环设置；配置刷新不能替换正在使用的终止绑定。旧资产若存在冲突绑定，首个绑定生效，其余冲突项跳过并记录 warning，禁止静默覆盖。
- 热键提示音必须在后台任务播放；不得在 UI Dispatcher 上同步 `Console.Beep`。`Duration` 到时必须取消当前执行，不只是阻止下一轮循环。
- `StopAll(reason)` 只用于顶部停止按钮和应用真正退出。顶部按钮按产品规则同时停止手动调试与全部热键脚本。
- `Esc` 只调用 `ExecutionController.Cancel(EscapeDebug)`；绝不调用 `ScriptRunManager.Stop/StopAll`。没有手动调试且没有其它编辑器取消动作时，不得无条件吞掉 `Esc`。

### 并行执行输入所有权
- `MainWindow` 生命周期内只能有一套共享 `RuntimeAdapters` / `GraphRuntimeExecutor`。切 tab 重建 `ExecutionController` 时只替换 editor service，禁止重建 runtime；否则全局设备锁与同键引用计数会被拆成多份。
- 每次 `GraphRuntimeExecutor.Execute()` 必须通过 `RuntimeAdapters.BeginExecutionInputScope()` 建立唯一 execution scope；多线程 branch 继承同一 scope。
- `Win32KeyboardAdapter` / `Win32MouseAdapter` 按 scope 记录持有的键和鼠标按钮，并对同一输入做引用计数。一个执行结束只能释放自己的 ownership，不能打断并行热键脚本。
- 未知键名必须明确失败，禁止回退为 `A` 或任何其它真实按键；旧资产或手改 JSON 不能产生隐式输入。
- `ExecutionController.Cancel(...)` 只取消手动 token，不直接调用全局 `ReleaseAllInputs()`；scope 在执行退出的 `finally` 路径自动释放本次输入。
- `ReleaseAllInputs()` 是应用退出的紧急清理入口，不得用于普通 `Esc` 或单脚本终止热键。
- 所有 `Process.GetProcesses*()` / `Process.Start()` 返回对象必须 Dispose；窗口轮询和启动程序等待属于高频路径，遗漏会累积 OS 句柄。

### 托盘最小化 / 关闭策略 (`MainWindow.WindowLifecycle` + `ThemedDialogOverrides`)
- `SingleInstanceCoordinator` 在 `App.OnStartup()` 创建主实例 mutex 和激活 event。同一 Windows 登录会话的后续进程只通知主实例恢复/置前，然后无条件退出；不得因通知失败降级为多开。主窗口尚未创建时要保留 pending activation，`App.OnExit()` 统一释放内核句柄。
- `NotifyIcon`（`System.Windows.Forms`）只负责系统托盘图标和鼠标事件。
- 左键单击恢复窗口；右键弹出项目自绘 WPF `TrayMenuWindow`（"打开面板" / "退出程序"），禁止恢复 WinForms 默认 `ContextMenuStrip`。
- `AppSettings.WindowCloseAction` 控制点击主窗口 `×` 的策略，默认 `MinimizeToTray`。默认关闭窗口时不弹选择对话框，直接最小化到托盘；设置里可改为 `ExitApplication`。
- 最小化到托盘不能停止正在运行的脚本、全局热键或释放托盘资源；真正退出软件时才调用 `StopAll()`、释放按键、关闭 detached 窗口、Dispose 热键/托盘/主题事件。
- 真正退出软件时仍要保留未保存资产确认，避免误丢数据；如果用户在保存确认里取消退出，`ExitApplication()` 不能继续 `Shutdown()` / `Environment.Exit(0)`，也不能提前销毁托盘图标。

### 工具栏执行状态 (`MainWindow.xaml` + `ExecutionController`)
- `IsExecuting` 依赖属性绑定到 Window DataContext。
- `IsExecuting` 必须由 `IsManualDebugRunning || IsAnyHotkeyRunActive` 唯一计算；任一来源结束时都重新合并，不能让热键事件直接覆盖手动状态。
- XAML DataTrigger：执行时 RunGraphButton 变 "⏳ 执行中..."、蓝色加粗、禁用；StopExecutionButton 红色显示。
- `ExecutionController.ExecutionStateChanged` 回调 + `ScriptRunManager.RunningStateChanged` 事件合并驱动。
- `SetRunButtonRunning/RestoreRunButton` 只发事件，不直接操作按钮（避免与 Style 冲突）。
- `EditorSurfaceControl.IsExecutionFrozen` 是运行期冻结入口。`MainWindow.UpdateExecutionFreezeState()` 会把 `IsExecuting` 广播到所有 session surface，包含主窗口和 detached 窗口；surface 内部遮罩会拦截命中测试并显示旋转进度/扫描线。不要只靠按钮禁用表达运行态。

### 鼠标中键支持
- `GraphTypes.MouseButton` 枚举新增 `Middle`。
- `EditorSurfaceControl.xaml` MouseButtonComboBox 新增"中键"项。
- `MouseClickNodeViewModel.RefreshDescription` → Middle→"中键"。
- `MouseNodeExecutors.MouseClickNodeExecutor` → Middle→"中键"。
- `Win32MouseAdapter.GetMouseEventFlags` Middle case 使用已有常量 `MOUSEEVENTF_MIDDLEDOWN/UP`。

### 提示音
- `HandleScriptHotkey` 中：Start→`Console.Beep(800, 150)`，Stop→`Console.Beep(400, 300)`。

### 日志捕获修复 (`Logger.cs`)
- `Write()` 方法：日志先写文件 + 入队 UI，再进 capture scope，确保 `BeginCapture()` 期间日志面板仍实时显示。

## 编辑器视觉规则

- 2026-09-11：编辑器面板选中态与分组层级：事件图和函数列表使用两个独立 `ListBox`，活动控制器与两个 `SelectedItem` 必须同步维护；切换到事件图时清除函数控制器的活动项，反之亦然，清空活动图时两边都清除。
- `IsCompileDirty` 只表达编译脏状态，不得通过复用选中背景伪装活动项。选中态、hover、脏标记必须使用独立视觉 token。
- `EditorSectionCardStyle` 不得同时用于侧栏导航、Inspector 摘要和结构化字段分组。外层面板保持扁平，语义分组使用单层背景/边框，输入框和下拉框保留自身边框。
- 参数 Inspector 的 `StackPanel`、`ParameterRowsPanel` 和现有 `x:Name` 是 `InspectorController` 的运行时接口。视觉调整只能增加样式或外围装饰，不能替换控件类型、重建动态行或破坏输入焦点。
- 动态 Inspector 字段的校验提示、帮助说明和编辑事件必须留在对应字段下方；分组边界不能通过重建整个 Inspector 实现。
- 本次踩坑：为减少嵌套边框将共享 section 样式全局设为透明，误伤了结构化 Inspector 和函数参数输入面板。后续必须按“导航分组 / Inspector 分组 / 工作台分组”拆分样式资源后再调整视觉。

- 2026-07-06：应用级主题由 `AppSettingsService` + `AppThemeService` 管理，配置文件在 `%AppData%/AutomationStudioWpf/app-settings.json`。主题设置不属于资产数据，禁止写入脚本/函数库 JSON。
- 主题资源只允许通过 `App.xaml` 中的 `Editor*Brush` / `Accent*Brush` 等全局 brush 进入 UI；这些 brush 必须带 `po:Freeze="False"`，运行时切换主题时更新已有 `SolidColorBrush.Color`，否则旧控件会继续拿着旧颜色。
- 当前内置两套主题：暗色编辑器主题、亮色 Codex 风格主题。强调色必须扩散到选中态、hover、工具栏、输入边框、下拉选中、Tooltip 边框等全局状态，不允许只改设置面板预览色。
- `AppSettings.AccentOpacity` 控制强调色混合强度，范围 `0.18-1.0`，默认 `0.62`。选中态、窗口 tab、内容浏览器资产卡片、hover 等必须使用 `AppThemeService` 按底色混合后的 `EditorListSelectedBrush` / `EditorChromeHighlightBrush`，不要直接把纯 `AccentBrush` 当大面积背景，否则颜色会太实、刺眼。
- 设置窗口里的强调色透明度只保留数值输入，不使用 `Slider`。滑条拖动会产生高频 `ValueChanged`，每帧触发全局主题刷新，主界面会明显卡顿；数值输入按 `Enter` 或失焦后再应用。
- `AccentForegroundBrush` 必须按最终选中底色亮度自动取深/浅字色；亮色主题下不能固定白字，否则资产名和 tab 文本会在柔和强调色上看不清。
- 后续新增控件必须使用资源 brush，不能硬编码浅底浅字或只适配暗色；亮色主题下 hover 字体不能硬编码白色，除非背景是经过对比度处理的深色强调底。
- 顶部工具栏的 `设置` 按钮打开 `SettingsWindow`；该窗口必须保持项目自绘窗口样式，不能使用 Windows 原生设置/消息面板。
- `SettingsWindow` 的主题选择使用可点击预览卡片，不再使用裸 RadioButton 列表；预览卡必须展示主题背景、面板和强调色，让用户点击前就能判断效果。
- `SettingsWindow` 主题/强调色必须实时预览到全局主界面；底部只保留 `应用并关闭` 一个按钮，实时预览会即时写入应用设置，`应用并关闭` 负责确认并关闭。不要恢复 `保存设置` / `取消` 双按钮。
- `ThemedDialog` 需要读取当前主题资源，不能固定暗色；否则亮色主题下会出现视觉割裂。
- `EditorSurfaceControl.xaml` 的左侧图表栏使用扁平 sidebar section（`EditorSidebarSectionStyle`）+ list item：hover、selected、compile dirty 必须分别用 `EditorPanelCardHoverBrush`、`EditorListSelectedBrush`、`EditorDirtyBackgroundBrush`，选中/脏状态用左侧 accent 条辅助识别；不要把整个 section 做成厚重 card。
- 右侧细节面板的 header、基础节点信息、编号 chip 使用卡片层级；禁用/前置输入态使用 disabled chip/input 颜色，避免看起来像普通可编辑输入。
- 2026-07-16 历史记录：主窗口工具栏曾使用更紧凑的圆角分组，窗口 tab 增大点击命中区并保持活动/脏状态层级；底部内容浏览器和日志 header 使用 accent 竖条区分区域；编辑器细节面板的基础字段收进独立 field card，并对输入/下拉/图标按钮提供键盘焦点反馈。工具栏分组已在后续版本改为透明 command strip，当前规则见 2026-09-12 勘误；此类视觉调整只改 XAML 样式和布局参数，不改 x:Name、Binding 或事件。
- 2026-07-17：编辑区和底部工作区的 splitter 统一使用 `EditorVerticalSplitterStyle` / `EditorHorizontalSplitterStyle`：常态只显示 1px 分隔线，hover/拖动才显示 accent，命中宽度保持 7px。亮色主题下节点正文必须保持接近不透明，避免网格穿透影响文字；内容资产选中态同时使用边框、柔和底色和底部 accent 条，不只依赖大面积强调色。
- 左侧 section 的折叠/新增按钮统一用 `EditorSidebarIconButtonStyle`；函数项“公开”开关使用紧凑短文案和 ToolTip，避免挤压函数名。细节面板内 `TextBox` / `ComboBox` 统一最小高度，保持字段节奏一致。
- `MainWindow.xaml` 的顶部工具栏命令按钮统一用 `TopToolbarButtonStyle`；底部内容浏览器 folder/tree item 和 asset tile 分别用 `ContentFolderListItemBaseStyle`、`ContentAssetTileContainerStyle`。不要在每个 `ListBoxItem` 内重复写 hover/selected 模板。
- 主窗口 5 个面板分隔条必须使用 WPF 原生 `GridSplitter`，禁止用 `OnMouseLeftButtonDown/Move/Up` 手写替代 `Thumb` 拖拽。分隔列/行使用独立 7px 命中区，模板根节点绑定 `TemplateBinding Background`；视觉指示线不能作为唯一命中区域。
- 资产或函数双击激活编辑器后，输入路由结束才清理内容框选、资产拖拽、画布平移、节点选择和连线预览捕获；不得在正常 `GridSplitter` 拖拽期间全局释放鼠标。空状态也必须加载、保存底部布局，尺寸只读 `ActualWidth/ActualHeight` 且过滤无效值。
- 底部内容浏览器/日志和 Inspector 只保留主体背景、标题栏及必要的分隔线；脚本工作台的运行设置和热键设置用间距/层级区分，禁止重复完整 Border/card 嵌套。输入框自身边框保留，不能用大面积容器边框代替层级。
- 顶部工具栏使用透明 command strip 和弱分隔线分组；按钮本身继续使用 `TopToolbarButtonStyle` 提供 hover、pressed、keyboard focus、disabled 状态。不要恢复厚重圆角外层容器，也不要退回无状态的透明裸按钮。
- 顶部工具栏不再提供“新建图谱”全局入口；资产创建只能从内容浏览器走，避免绕过脚本主图/辅助图规则。“打开图谱”文案统一为“外部导入”。`鼠标拾取` 固定放在 `另存为` 后面。
- `编译`、`显示最终代码`、`执行脚本` 属于编辑动作组：只有打开脚本或函数库编辑 session 后显示。`执行脚本` 只允许脚本事件图触发；函数库或非事件图下要禁用并给出 ToolTip。
- 全项目禁止直接调用 `System.Windows.MessageBox.Show` / `WpfMessageBox.Show`。确认、错误、信息弹窗必须走 `ThemedDialog` 或项目自定义窗口；运行时 `ShowMessage` 节点也必须使用同风格弹窗。
- 全项目禁止使用 Windows 原生默认面板/默认白底提示，包括 `MessageBox`、WinForms `ContextMenuStrip`、WPF 默认白底 `ToolTip`。所有弹窗、托盘菜单、右键菜单、Tooltip、属性浮层必须走项目暗色高对比样式。
- 所有 UI 文本必须保证“背景/文字/边框”对比度一眼可读：暗底配亮字，禁用态也要能看清功能含义；不要使用浅底浅字、灰字贴近背景、按钮压文字、tooltip 白底灰字这类低对比方案。
- `ThemedDialog` 按钮组居中显示，按钮必须有 hover、pressed、keyboard focus 反馈；空正文对话框只显示标题和按钮，不留大段说明空白。
- 底部内容浏览器/日志 header 标题统一用 `BottomPanelTitleStyle`；日志过滤组用圆角 field card，日志正文保留等宽字体和较大的 padding，优先保证长期阅读清晰度。
- 窗口标签栏使用圆角 tab：active 用 `EditorListSelectedBrush`，dirty 用 `CompileDirtyBorderBrush` 和左侧脏点提示；关闭按钮保持小尺寸但使用共享工具栏按钮风格。
- 空编辑器状态使用中央引导卡片，不要回退成单行文字或整块空黑；节点菜单 `NodePalette` 要有标题、说明、搜索框和结果区层级。
- `ScriptPropertiesWindow` 是代码构建 UI，仍要遵循卡片分组：运行设置和热键分组要留足宽度，热键行固定展示“按键 / 修改 / 按下次数 / 清空 / 触发时间阈值”，按钮不能压住文字。
- `ScriptPropertiesWindow` 根布局保持“顶部标题固定 + 中间 `ScrollViewer` + 底部保存/取消固定”；新增设置项只能放入滚动内容区，禁止被底部按钮遮挡。
- `ScriptPropertiesSummaryControl` 是脚本属性的内嵌可编辑面板：主页面无打开资产时，单击脚本资产会在 `EmptyEditorPanel` 直接显示可编辑属性；脚本编辑界面只有单击画布空白且无节点选中时，才会在右侧细节面板显示脚本属性。选中节点时必须显示节点详情，不能混入脚本属性。`EditorSurfaceContext` 的 node selection callback 也必须调用 MainWindow 的脚本属性隐藏/显示 helper，不能直接只调 `InspectorController.LoadNode(...)`，否则 session 自己选节点会残留脚本属性面板。
- `ScriptPropertiesSummaryControl` 由 C# 构建 UI，但颜色仍必须读取 `App.xaml` 全局 brush，禁止使用冻结硬编码色；热键行必须支持窄面板换行，不能让“修改/清空/阈值”压住文字。
- 内容浏览器里的脚本启用开关必须使用自绘高对比角标，不允许回退为系统 checkbox 外观；启用态显示清晰 `✓`，停用态也必须可读，并通过 Tooltip 说明“是否监听全局热键”。
- 新增编辑器 UI 颜色优先放在 `App.xaml` 的 `Editor*Brush`，不要在 XAML 中散落硬编码颜色；局部样式只负责布局、圆角、间距和状态触发。
- 连线/pin 的执行、完成、布尔、坐标、字符串、默认类型颜色必须使用 `Editor*PinBrush` 语义 token。`PinBrushes` 只能持有可变 brush，并在 `AppThemeService.ThemeChanged` 后同步颜色；禁止缓存冻结的主题 fallback，否则现有图表切换亮/暗主题后会保留旧线色。
- 视觉优化只改样式时不得改控件 `x:Name`、事件处理器、Binding 路径，避免打断 `EditorSurfaceContext` / `InspectorController`。


### 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                      Presentation Layer                      │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  │
│  │ MainWindow   │  │ LogWindow    │  │ ThemedDialog     │  │
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
│  │ PythonEnvironment│                                       │
│  │ Service          │                                       │
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

### 0. 资产系统：事件图 / 自定义函数

- `Graphs` 是旧字段，当前语义为事件图；`Functions` 保存脚本私有函数或函数库函数。
- 函数默认包含且必须始终保留唯一的 `FunctionEntry` 和 `FunctionReturn`。两者 `CanDelete=false`，所有用户删除入口和 `GraphEditorService.RemoveNode(...)` 都必须拒绝删除；加载旧图、Undo/Redo 恢复时若缺失会自动补回，若重复会归一化为一个。当前编译/运行语义只支持单入口、单返回，禁止通过节点菜单重新创建结构节点。
- 函数调用同步执行，并把返回节点输入复制到调用节点输出。
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
- 当前图内非 `Reroute` 节点编号分配。编号按图类型使用 `N###` / `Fun###`，删除节点后空出的最小编号可复用
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
- 内容浏览器源数据是 `ContentBrowserItems`；左树投影 `ContentFolderItems`，右侧瓦片投影 `ContentVisibleItems`。两个投影集合使用 `RangeObservableCollection.ReplaceAll(...)` 一次 Reset 刷新，避免大目录 `Clear()+Add` 逐项触发 WPF 重绘和搜索刷新。
- `MainWindow.NavigationFeatures.cs` 在窗口 `Loaded` 后动态给 `ContentBrowserHeaderBar` 安装搜索框。搜索范围是当前 `_currentContentFolderId` 及全部子文件夹；根目录时搜索全部内容资产。
- 内容浏览器刷新会重建 internal `ContentBrowserIndex`；搜索、树刷新、路径定位和展开都复用同一份 `assetById` / `childrenByParent` / `folderChildrenByParent` / path cache，避免对 `ContentBrowserItems` 反复全量扫描。
- 搜索支持空格关键字、路径片段、`DisplayName` / `Kind`、不区分大小写和 subsequence 模糊匹配。搜索结果直接替换 `ContentVisibleItems`，文件夹和资产都会进入结果。
- 搜索结果双击仍走内容浏览器现有打开逻辑：文件夹进入目录，脚本/函数库调用 `OpenContentAsset(asset)`。
- `Ctrl+B` 由 `MainWindow_NavigationPreviewKeyDown` / `ContentBrowserListBox_NavigationPreviewKeyDown` 处理：有选中资产时清空搜索、进入真实父目录、选中并滚动到资产；没有内容浏览器选中项时定位当前打开资产。
- 画布中双击 `FunctionCallNodeViewModel` 会按 stable `FunctionId` 查找目标图，打开目标所在脚本/函数库资产，然后通过函数 `GraphListController` 加载目标 `GraphListItemViewModel`。
- 双击跳转先 `CommitInspectorAndSnapshotAllSessions()`；随后走 `OpenOrActivateAsset(target.Asset, target.Graph, kind)` 聚焦已有 session 或创建新 session。不要靠显示名解析调用目标。
- `MainWindow.ContentBrowserCommands.cs` 承接内容浏览器基础 CRUD、目录进入、树刷新、路径展开、单资产重命名/删除和 move/copy drop 对话框。
- `MainWindow.ContentBrowserMultiSelect.cs` 扩展内容浏览器为 UE 风格多选：Ctrl 多选、Shift 区间、框选、Ctrl+C/Ctrl+V 复制粘贴资产、多删除、拖拽预览和移动/复制到文件夹。

#### Editor sessions and window bar
- 多编辑窗口由 `EditorSessionViewModel` 表示：每个打开资产一个 session，持有自己的 `GraphEditorService`、`NodeFactory`、`GraphCommandService`、事件图/函数集合和当前图记忆。
- 每个 `EditorSessionViewModel` 持有自己的完整 `EditorSurfaceControl`，其中包含图列表、画布、节点菜单和属性面板；`EditorSurfaceContext` 持有该 session 的 graph list/canvas/drag/inspector/pin/palette/import controllers。`MainWindow` 只镜像 active context controllers 以支撑尚未拆完的 handler。
- 全局窗口事件不能直接假定 active surface 存在。启动、无资产、detached 切焦点等路径必须用 `TryGetActiveEditorSurface()` 或事件来源 surface；拿不到 surface 时 no-op。抛异常版 `GetActiveEditorSurface()` 已删除，不要恢复。
- WPF parent walk 不能直接对任意 `DependencyObject` 调 `VisualTreeHelper.GetParent(...)`：`RichTextBox` 内点击可能给出 `FlowDocument`，它不是 `Visual/Visual3D`，会抛异常。MainWindow 用 `GetSafeVisualOrLogicalParent(...)`，Interaction 层用 `VisualTreeUtility.GetParent(...)`。
- `OpenContentAsset(...)` 现在是 `OpenOrActivateAsset(...)` wrapper。重复打开同一资产只聚焦已有 session，不重置到第一个事件图；函数调用节点双击会打开或聚焦目标资产 session，再加载目标 graph id。
- 工具栏下方 `EditorWindowBar` 绑定主窗口内的 `MainEditorSessions`，不显示 `DockMode.Detached` 的独立窗口；窗口栏右键的关闭全部/关闭右侧只作用于主窗口标签页。全量 `EditorSessions` 仍包含 detached，供保存、退出、compile-all 使用。拖出主窗口会创建 `DetachedEditorWindow`；拖到主窗口内部只激活标签，不创建画布子窗口。
- 拖出某个 session 后，主窗口应继续显示最近的 main-tab surface；`EmptyEditorPanel` 只是无主窗口 tab 时的 fallback，不能盖住已经挂载的主 surface。
- 拖动窗口标签时会显示跟随预览卡片，越过主窗口边界后提示释放/继续拖出为独立窗口。
- 主窗口标签页和 `DetachedEditorWindow` 都直接 host 对应 session 的 `Surface`；detached 窗口不再显示只读 preview，也不再要求“激活后在这里编辑”。detached 激活只切 toolbar/command 目标，不应重置主窗口 host。
- `EditorSurfaceContext.Configure(...)` 是幂等的：同一个 session 的 controller 不因 host attach/activate 反复重建；面板布局配置只在 session 首次配置时加载，不能在 `PinAnchorLayoutUpdated` 或其它 surface 事件中覆盖用户拖拽尺寸。surface 事件按类型分类：明确用户交互才提升 active session；`PinAnchorLoaded/LayoutUpdated`、无按键 `MouseMove`、初始化触发的 `TextChanged/SelectionChanged` 只使用所属 context 或直接忽略，避免 tab 闪动、列表折叠或 detached/main 互相污染。
- surface controller 的 dirty/snapshot 回调按所属 `EditorSessionViewModel` 闭包绑定；编辑 detached 或非首个 tab 时不能直接依赖全局 `_activeAssetController`，否则 dirty 黄点和 compile target 会串到其它资产。
- 当前图 controller 统一通过 `SetSessionActiveGraphController(session, controller)` 写入；它同步 `EditorSurfaceContext.ActiveAssetController`、session remembered active graph，以及当前操作 session 的 `_activeAssetController` 镜像。新增图/函数和 `LoadGraphItem(...)` 都必须用这个入口。
- 主窗口 tab 切换是 view activation，不是 graph load。已打开并已加载 graph 的 tab 必须走 `ActivateEditorSessionFromMainTab(...)` 的轻量路径：只切 `_activeEditorSession`、active service/controller、toolbar、content browser selection 和 main host surface；禁止 `LoadFromModel(...)`、禁止清 active graph、禁止写 `graph-library.json`。首次打开资产、双击函数/事件跳转、显式切图表才允许重载目标 graph。
- `GraphListController.LoadItem(..., persistAfterLoad: false)` 用于 `MainWindow.LoadGraphItem(...)` 这类导航加载；纯导航只 snapshot 到内存，不持久化。新增/删除/重命名/导入/保存/编译同步才允许触发 `PersistAssetLibrary()`。
- session dirty/snapshot/compile helper 在 `MainWindow.EditorSessionState.cs`；不要把这些状态路径重新散回 `MainWindow.xaml.cs`。
- `HandleEditorSurfaceEvent(...)` 处理完事件后不能把全局 `_activeAssetController` 回写到 `_activeEditorSession.SurfaceContext`。非 active surface 事件通过 `RunWithSurfaceContext(...)` 临时切 controller，结束后应恢复全局状态而不是污染其它 session。
- detached session 激活时只更新全局工具栏/运行/保存目标，不覆盖 `_lastMainEditorSession`；主窗口继续显示最近的主窗口 tab surface。
- `MainWindow.EditorSurfaceRegions.cs` 和 legacy region reparent hooks 已删除。不要恢复 `AttachLegacyEditorRegionsToSessionSurface()` 的区域搬移逻辑。
- `MainWindow.GraphInputHandlers.cs` 承接画布、节点、pin、节点菜单和快捷键输入；`MainWindow.GraphListHandlers.cs` 承接事件图/函数列表、分组展开和公开到库入口；`MainWindow.EditorSurfaceControllers.cs` 承接 surface controller 初始化、active surface lookup 和 typed surface event dispatch；`MainWindow.EditorSessionWorkflow.cs` 承接资产打开/切换/关闭、session 提交和 callable 解析入口；`MainWindow.AssetCommands.cs` 承接工具栏新建、打开、保存、编译、运行按钮入口；`MainWindow.ContentBrowserCommands.cs` 承接内容浏览器基础命令和目录投影刷新；`MainWindow.InspectorHandlers.cs` 承接属性面板事件转发；`MainWindow.LogAndImportHandlers.cs` 承接日志按钮和拖拽导入入口；`MainWindow.WindowLifecycle.cs` 承接关闭/退出保护；`MainWindow.GraphModelHelpers.cs` 承接 graph/node DTO clone 与入口标题 helper；`MainWindow.VisualTreeHelpers.cs` 承接 WPF visual/focus tree helper。不要把这些 handler 重新堆回 `MainWindow.xaml.cs`。
- `InspectorController.cs` 只保留属性面板 `LoadNode()` / `ApplyChanges()` 主分发和构造注入；参数行在 `InspectorController.Parameters.cs`，通用小节点在 `InspectorController.CommonNodes.cs`，找图/窗口/程序/键盘辅助在 `InspectorController.SystemNodes.cs`，前置输入锁定和灰态在 `InspectorController.Locks.cs`，ToDo 目标选择在 `InspectorController.ToDo.cs`。
- `DarkContextMenuStyle`、`DarkDropdownListBoxStyle`、`DarkDropdownListBoxItemStyle` 和 editor surface 常用 brush 是 `App.xaml` 共享资源；不要在 `MainWindow.xaml` 或 `EditorSurfaceControl.xaml` 复制结构色。
- 节点 header、pin、日志级别和弹窗常用 brush 应复用；不要在高频 getter / 日志追加 / dirty 刷新里反复 `new SolidColorBrush(...)`。编译按钮是主题控件，禁止由 C# 写本地 brush 或暗色 fallback。
- 顶部工具栏的鼠标拾取是编辑器工具，不是运行时节点能力：`MousePickController` 使用 `WH_MOUSE_LL` 全局 mouse hook，`MousePickOverlayWindow` 显示跟随浮窗，`MousePickChoiceWindow` 是非模态复制选择窗，`ScreenPixelSampler` 用 `GetCursorPos` / `GetDC` / `GetPixel` 采样屏幕像素。拾取只在鼠标坐标变化时采样/更新，静止时复用上一帧；浮窗和选择窗必须按当前显示器工作区自适应位置。复制坐标/颜色后退出拾取，取消则继续；拾取结束、窗口关闭或 `Esc` 必须 unhook、释放 DC、关闭 overlay。
- session 关闭只 snapshot 回 `ContentAssetViewModel` 并移除编辑窗口，不删除资产。删除内容浏览器资产时会关闭所有指向该资产的 session，避免悬空编辑窗口。
- session 关闭必须调用 `EditorSessionViewModel.Dispose()`：先释放 `EditorSurfaceContext`，取消节点菜单/连线/Inspector 状态并清空 graph service，再由 `EditorSurfaceControl.Detach()` 清除 host、`Session`、`SurfaceContext` 与 `DataContext`。释放必须幂等；旧 surface/context 不得复用或继续路由 UI 事件。
- 删除事件图/函数必须统一走 owning session 的 `GraphListController`。UI handler 不得自行复制删除逻辑，否则主事件图、函数 Entry/Return 与持久化规则会漂移。
- 保存、退出、编译前使用 `CommitInspectorAndSnapshotAllSessions()` / `CommitAllSessionsToAssets()`，保证多窗口编辑内容参与引用同步和校验。
- 工具栏编译是 active-asset scoped，走 `GraphCompileService.CompileAsset(...)`：脚本会编译该资产内事件图和函数；函数库会编译该库内全部函数。`GraphCompileService.CompileGraph(...)` 仍保留为 current-graph scoped 内部能力，但工具栏不使用它。
- 工具栏编译视觉只由 `IsActiveAssetCompileDirty` 驱动：C# 更新布尔状态；XAML DataTrigger 使用 `DynamicResource` 切换文字、提示图标、背景和边框。禁止重新在 `ApplyAssetCompileButtonState()` 里直接设置颜色，否则主题切换或资源查找失败会出现黑色按钮。
- 执行图谱前如果存在任何 `IsCompileDirty` 图，`EnsureCompiledBeforeRun()` 会自动走 `CompileAllAssets(...)`；编译失败不执行，编译成功后必须同步清掉图列表黄点和工具栏 `编译*`。
- `GraphCompileService` 每个 compile 入口只构建一次 asset id lookup，并传给下游校验。新增校验时复用该索引，不要在每层 `Validate*` 里重复 `ToDictionary(...)`。
- 编译成功后要把 `ContentAssetViewModel` 中清掉的 graph dirty/compile dirty 同步回对应 session 图列表，并刷新窗口栏、section badge 和工具栏编译状态；保存不是清 compile dirty UI 的唯一路径。
- 内容浏览器搜索会缓存扁平 `ContentAssetSearchEntry`（资产、路径、可搜索文本），由 `ContentBrowserIndex` 在 `RefreshContentBrowserViews()` 时重建。新增资产重命名/移动路径时必须走刷新或显式重建索引。

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
- 默认节点标题是用户可见 UI，不是 schema。`Start` 显示 `开始运行`；常规节点默认标题不要带冗余 `节点` 后缀，例如 `延迟`、`找图`、`分支`。

#### NodeRegistry
统一管理节点定义和执行器注册：
- `Definitions`：由 `NodeDescriptorCatalog + NodePresentationCatalog` 生成，提供节点菜单分类、显示名、引脚、搜索标签、属性面板 schema key 和能力声明。
- `TryGetExecutor`：Runtime 对普通能力节点按 `NodeKind` 找到对应 `INodeExecutor`。
- `GraphRuntimeExecutor` 仍直接处理结构节点：`Start`、`Reroute`、`If`、`ForLoop`、`WhileLoop`、`MultiThread`、函数/自定义事件入口与调用节点。
- 右键节点菜单由 `NodePaletteController` 读取 `NodeRegistry.Definitions.Where(CanCreate)` 生成，禁止维护 `HiddenKinds` 或在 `MainWindow` 手写菜单列表。

新增节点时至少更新：
1. `GraphTypes.NodeKind`
2. `NodeDescriptorCatalog` 核心能力和 `NodePresentationCatalog` 展示元数据
3. 对应 `ViewModel` 与 `NodeFactory.CreateNode`
4. `NodeSerializer`
5. 普通执行节点注册 `INodeExecutor`；纯节点注册 evaluator；结构节点声明 built-in runtime 支持
6. 最终代码 emitter 或显式 fallback
7. `NodeDescriptorCoverageTests`

#### PythonEnvironmentService
Python 环境检测与安装指引统一由共享服务负责：
- 检测并缓存唯一的 `ValidatedPythonPath`
- 检查必要依赖库（cv2, PIL, numpy）
- 弹出可复制的安装命令对话框
- 支持阿里云 PyPI 镜像
- 环境检查与实际脚本执行必须使用同一个解释器

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

#### 当前节点定义 (38 个)

`NodeDescriptorCatalog` 覆盖全部 38 个 `NodeKind`。`NodeKind.Comment` 是显式 removed descriptor：存在于能力表，但 `CanCreate=false / HasSerializer=false / RuntimeSupport=Unsupported`，旧 `comment` 文件节点仍被跳过。`MouseDoubleClick` 不再保留 `NodeKind`，只保留旧 `mouse_double_click` alias 读取兼容，保存统一写 `mouse_click`。

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
| Stage-5 Mouse | MouseClick/GetMousePosition | 输入/鼠标 | 鼠标点击支持触发次数/间隔；双击用次数 2 + 80~150ms 间隔；获取鼠标位置输出当前位置 |
| Stage-5 Keyboard | KeyChord | 输入/键盘 | 组合键有专用 ViewModel/Inspector/Executor，支持点击/按下/抬起、触发次数、触发间隔 |
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

#### 多线程节点
- `MultiThreadNodeViewModel` 是结构节点，有 `exec_in`、动态 `exec_thread_N` 输出和特殊色 `exec_completed` 输出；`ThreadOutputCount` 保存到 `NodeFileModel` / `GraphRuntimeNode`。
- `+/-` 按钮走 `NodeBaseViewModel` 通用 dynamic pin API；删除最后一个线程输出前先清掉对应连接，最少保留 2 个线程输出。
- `GraphRuntimeExecutor.ExecuteMultiThreadNode(...)` 为每个已连接 `exec_thread_N` 启动 branch task；未连接线程输出视为立即完成。任一分支失败则节点 `FatalStop`，全部成功后继续 `exec_completed`。
- 每个分支用父 `RuntimeContext` 的输出快照 `Fork(...)` 出独立上下文，分支结束后只把相对快照新增/变化的输出 `Merge` 回父上下文；不同分支写同一个 `nodeId:pin` 且值不同必须 `FatalStop`，日志显示为 `节点名 编号.pin`，禁止 last-writer-wins 静默污染结果。
- 任一分支失败、异常或输出合并冲突时必须取消 sibling branches，避免失败分支被其它长等待/无限触发分支卡住；外部用户取消仍按正常取消路径处理。
- 纯运算节点读取前会重新求值，不能因为上下文里已有旧 output 就跳过；否则循环、多线程或函数调用中会读到旧的 `StringConcat/Boolean/Compare` 结果。
- 鼠标、键盘、窗口类节点使用 runtime 全局设备锁串行执行；Delay、日志、找图、纯运算等仍可并行。
- 键盘、鼠标点击、组合键统一使用 `TriggerCount` / `TriggerIntervalMs`。`TriggerCount=0` 表示无限触发，直到脚本取消；间隔只发生在两次触发之间，运行时最小夹紧到 `1ms`，避免空转。
- 鼠标双击不再是用户可见节点。旧 `mouse_double_click` 读取时由 `NodeSerializer` 迁成 `MouseClickNodeViewModel`，`TriggerCount=2`、`TriggerIntervalMs=80`、左键点击；编译 type key 也映射到 `MouseClick`。
- 键盘节点未设置按键时必须跳过并写 `result=false`，不能回退默认 `A`，避免误触发。
- 鼠标点击/移动用 `HasManualPosition` 区分“未设置坐标”和“明确设置 `(0,0)`”；新建节点默认 `false`，不允许默认点击 `(960,540)`；旧资产没有该字段时，非零坐标推断为已设置，旧 `(0,0)` 推断为未设置。
- `Delay`、等待窗口、启动程序等待、鼠标滚轮和 `WaitImage` / `WaitImageDisappear` 的长等待必须走 `CancellationWait.WaitOrThrow(...)` 或等价可取消等待；禁止用不可取消 `Thread.Sleep(intervalMs)` 卡住停止脚本。鼠标/键盘点击内部几十毫秒按下释放间隔可以保留同步等待。

重要安全规则：
- 输入 pin 未连接：可以使用节点本地属性。
- 输入 pin 已连接但上游没有运行时输出：当前节点 Warn 并跳过，不回退本地属性。
- 典型场景：找图未命中时，下游鼠标点击不会误用旧坐标；只有用户明确设置 `(0,0)` 时才允许点击屏幕左上角。
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
- 文件：`%LocalAppData%/AutomationStudioWpf/Logs/Log_yyyy_MM_dd_HH.txt`，按小时写入；只有 LocalAppData 本身不可创建时才回退 `%TEMP%/AutomationStudioWpf`。
- 启动时后台清理且只匹配 `Log_*.txt`：先删除超过 5 天的文件，再按最旧顺序压缩到目录总量 128 MiB 内；当前小时文件永不删除。清理失败只写调试诊断，不得影响启动或递归调用 Logger。

#### 日志面板交互
- 主窗口日志显示控件是只读 `RichTextBox`；`LogPanelController.HandleEntriesChanged(...)` 对新增日志增量追加段落，切过滤器/Reset 时才由 `Refresh()` 重建带颜色的 `FlowDocument`。
- `LogPanelController` 显式绑定 `ApplicationCommands.Copy` 和 `ApplicationCommands.SelectAll`：`Ctrl+C` 复制当前选中文本，`Ctrl+A` 全选当前过滤后的日志文本。
- `MainWindow.Window_PreviewKeyDown` 对 `TextBoxBase` 焦点直接放行，避免日志 `RichTextBox` 焦点内的 `Ctrl+C` / `Ctrl+A` 被全局节点复制快捷键截获。
- 日志过滤 `RadioButton` 使用 `App.xaml` 全局暗色模板，`IsChecked=true` 时必须显示中心白点，避免过滤生效但用户看不出当前选项。

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
- Release 安装包内置隔离 Python 3.14.6 与固定版本 OpenCV/NumPy/Pillow。
- `PythonEnvironmentService` 只验证私有解释器；Release 禁止扫描系统 Python、WindowsApps alias 或联网安装。

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

### 1. 日志增量刷新
`Logger.Write(...)` 会把 UI 条目放入待刷新队列，并用一次 `DispatcherPriority.Background` flush 合并多条日志；flush 时通过 `RangeObservableCollection.AddRange(...)` 向 `Logger.Entries` 发出一次多项 Add 事件。主日志面板和独立日志窗口使用 `RichTextBox` 显示日志，新增日志只追加新增段落，过滤器变化或清空时才重建文档，避免每条日志都全量刷新、逐条触发 collection change 或塞满 Dispatcher 小任务。

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
- 安装目录 `Runtime/Python/python.exe` 是否存在。
- `Runtime/Python/runtime-manifest.json` 是否与应用要求版本一致。
- 私有解释器能否执行 `-I -c "import cv2; import PIL; import numpy"`。
- 图片路径是否正确（支持相对路径和绝对路径）

**解决方案：**
使用同版本安装器执行“修复”或重新安装。Release 不联网修复，也不回退用户机器上的 Python。

### 2. 执行时节点无响应

**可能原因：**
- 节点参数缺失（查看黄色警告日志）
- 执行链断裂（检查连线）
- 死循环（执行器会自动检测并终止）

### 3. 日志无法复制

已修复，现在日志使用只读 `RichTextBox` 显示，支持自由选择、`Ctrl+A` 和 `Ctrl+C`。

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

4. **实现执行逻辑**：普通能力节点在对应 `Nodes/<分类>/` 下实现 `INodeExecutor`；结构控制流节点才改 `GraphRuntimeExecutor`

5. **注册元数据**：在 `NodeDescriptorCatalog` 声明 type key/traits/pins/runtime/serializer/preview 能力，在 `NodePresentationCatalog` 声明显示名、分类、Inspector 和主题 key

6. **注册执行能力**：普通能力节点在 `NodeRegistry.CreateDefault()` 注册 executor；结构控制流和纯节点不得为了通过覆盖测试而注册空 executor

7. **添加属性面板**，字段锁定规则优先放到 `InspectorController`

8. **补覆盖测试**：descriptor、serializer、runtime 和 final-code 能力必须显式声明

### 添加新的 Python 功能

1. 在 `Python/` 目录添加脚本
2. 在 `Services/PythonEnvironmentService.cs` 添加依赖检查
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
- 新建资产只走右侧空白右键菜单，菜单项为 `脚本 / 文件夹 / 函数库`。
- 右键资产显示 `打开 / 重命名 / 删除`。`打开` 位于 `重命名` 之前，并复用双击的 `OpenContentAsset(...)`/文件夹进入逻辑；右键空白显示新建菜单。实现上复用一个 `ContextMenu`，由 `ContentBrowserContextMenu_Opened` 根据 `_contentBrowserContextTargetsAsset` 切换 `Visibility`。
- 不要把带事件的 `ContextMenu` 放进 `ListBoxItem.Style Setter`。WPF 会在运行期把模板子元素接到 style connector，可能启动崩溃：`Unable to cast object of type 'TextBox' to type 'Style'`。
- `MainWindow.NavigationFeatures.cs` 动态安装搜索框，不在 XAML 内硬编码。搜索当前目录递归资产/文件夹，支持空格关键字、路径片段、subsequence 模糊匹配和不区分大小写。
- `Ctrl+B` 是已实现定位：内容浏览器有选中资产时定位该资产真实父目录；无选中资产但有当前打开资产时定位当前打开资产。
- 双击画布中的函数调用节点是当前已实现跳转：按 stable `FunctionId` 找目标图，打开目标所在资产，再加载目标函数图。
- `MainWindow.ContentBrowserCommands.cs` 负责内容浏览器基础 CRUD / rename / folder projection；`MainWindow.ContentBrowserMultiSelect.cs` 负责多选、框选、资产 Ctrl+C/Ctrl+V、拖拽预览和多删除。新增内容浏览器交互优先放在这些 partial 或独立 controller，不要继续膨胀 `MainWindow.xaml.cs`。
- 脚本资产的启用状态同时出现在瓦片角标和右键菜单。右键菜单文案必须随状态翻转：未启用显示“启用脚本”，已启用显示“关闭脚本”，不要固定显示同一动作名。

### 2026-06-08: ToDo persistence and log copy fixes

- Compile/save/run entry points must commit inspector edits and snapshot open sessions before reading graph data. This is required for inspector-only edits and multi-window assets, especially ToDo target dropdown selection.
- ToDo static target selection must remain persisted even if `target_title` / `target_number` pins are connected; connected pins are runtime overrides, not a reason to clear static defaults.
- `GraphCompileService.EnsureGraphToDoTargets()` is a migration/repair pass: when old data keeps only `TargetNodeId`, compile fills title/number from the referenced target node before validation.
- Log text copy uses `RichTextBox` command bindings plus `TextBoxBase` shortcut passthrough. Do not special-case only `TextBox`, or `RichTextBox` copy will be intercepted by graph shortcuts again.

#### 文件夹树
- 左侧树绑定 `ContentFolderItems`，右侧瓦片绑定 `ContentVisibleItems`，源数据仍是 `ContentBrowserItems`。两个投影集合用 `RangeObservableCollection.ReplaceAll(...)` 批量刷新。
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
- 连线采用双层渲染且共用同一个 `ConnectionPathViewModel.PathGeometry`：`ZIndex=100` 是 14px 透明交互命中层，位于节点下；当前视觉层为 `ZIndex=150`，绘制轮廓、选中高亮和主线且必须 `IsHitTestVisible=false`；节点固定 `ZIndex=200`，拖线预览/框选/节点菜单分别为 `450/500/1000`。禁止把可命中的视觉线直接抬到节点上，否则经过节点的连线会抢走节点与 pin 操作。
- 连接主线普通/选中宽度为 `4.5/6`，轮廓宽度为主线 `+2.5`，选中高亮宽度为主线 `+6`；亮色主题必须使用更深的语义线色和独立轮廓 token，不能依赖暗色主题的白色执行线 fallback。

#### 参数默认值
- `GraphParameterDefinition.DefaultValue` 是函数/自定义事件参数默认值，必须写入 `GraphParameterFileModel`，并通过 `GraphRuntimeParameter` 进入运行时。
- 函数/自定义事件入口参数：调用节点对应输入未连接时，运行时使用入口参数默认值。
- 函数返回参数：返回节点对应输入未连接时，调用节点输出使用返回参数默认值。
- Float/Vector3D/Vector4D/ImageAsset/String 目前仍映射为 `String` pin；默认值按字符串传递，例如 `23.0f`。Boolean 默认值解析为 bool，Vector2D 默认值可写 `x,y`。

### 验证 / 文档门禁

- CodeGraph sync is part of the final gate. Commit `.codegraph/.gitignore` so database, wal/shm, cache, and logs stay local.
- 本文件是唯一长期技术文档；重要开发规则、踩坑和知识点只维护在 `AutomationStudio/Agent/TECHNICAL.md`。
- 旧 `AutomationStudio/TECHNICAL.md`、`AutomationStudio/agentmemory.md`、`AutomationStudio/Agent/skills/automation-studio-wpf/SKILL.md` 已废弃/删除，不再恢复。
- Do not describe git push as allowed unless the user explicitly requests push in the current task.

- `Tests/CodexSmoke` 是本地-only 回归辅助，Git 不跟踪、不提交；只在高风险交互或用户明确要求时本地运行。
- 完成前默认执行：
  - `dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck`
  - `git diff --check`
  - `dotnet run --project .\AutomationStudioWpf.csproj` 做短启动探测
  - `codegraph.cmd sync`
- 启动验证不再固定等待 20 秒；只确认能正常启动。若进程快速退出、输出 `Unhandled exception` 或 WPF 初始化异常，先收集终端输出/异常栈再修。
- 不要自动 `git push`。只有用户明确要求推送时才推。

#### Batched graph edits and runtime index
- `GraphEditorService.RunBatchedEdit(...)` defers `ConnectionPaths` rebuild and `GraphChanged` until the outermost batch exits; use it for composed connection mutations.
- `PinConnectionController` uses `RemoveConnections(...)` for selected visual path deletion and wraps reroute insertion in one batch.
- `GraphExecutionPlan` owns an internal lazy `GraphExecutionIndex` for node, execution-edge, and input-edge lookup; public constructor and graph JSON remain unchanged.

### 2026-06-06: editor command and wire UX foundation

#### GraphCommandService
- `Services/GraphCommandService.cs` owns graph edit Undo/Redo. It captures `GraphFileModel` snapshots before/after an edit and restores through `GraphEditorService.LoadFromModel(...)`.
- Commands must use the active `GraphAssetKind`; otherwise function undo can reload as an event graph and auto-create a Start node.
- Clear the command stack when switching content assets or graph/function items. Do not allow undo across graph boundaries.
- Use `Execute(...)` for direct graph mutations and `RecordApplied(...)` for edits already applied by continuous interaction, such as node dragging.

#### Wire selection and reroute editing
- Visible wire selection is stored on `ConnectionPathViewModel.IsSelected`; runtime/persistence still use `ConnectionViewModel` and `GraphEditorService.Connections`.
- `PinConnectionController` maps visual `ConnectionPathViewModel` back to backing `ConnectionViewModel` for double-click, Alt-click, and context-menu reroute insertion by sampling the visible Bezier geometry.
- Delete/Backspace on a selected visible path removes all backing connections in that visual path as one undoable command. Reroute nodes are not deleted automatically.
- Active visible geometry is `ConnectionSplinePlanner.BuildGeometry(...)`. `ConnectionChain` / `ConnectionChainFinder` and `SplineTangentCalculator` are currently not called by XAML-bound paths.
- 2026-07-14 路由点圆角规则：无 reroute 的两点连线保留原有距离约束出线；多点路径先固定输入/输出端点，用 2-opt 仅重排内部视觉 waypoint，消除拖拽形成的蝴蝶结交叉，再使用相邻方向角平分切线生成 G1 连续圆角。该重排只影响视觉顺序；reroute 仍是 runtime 透明节点，拓扑、JSON 和执行语义不变。
- 控制柄同时受相邻最短线段、转角缩放和当前 span 投影预算约束；完整曲线采样发现自交时按 `1/.75/.5/.25/0` 逐级降低圆角，最终退化为已消交叉的直段。禁止通过放大 Bezier 强行圆角导致绕圈，也禁止交叉检测失败时返回空路径。
- 实现仍保持“每个 backing connection 对应一个 `BezierSegment`”，否则 `ConnectionPathViewModel.FindNearestConnection(...)` 的可见曲线命中数量会错位。视觉 waypoint 被消交叉重排后，segment index 仍只用于映射同一条透明 reroute chain 的 backing connection；插入任一处的运行语义等价。紧凑、反向、蝴蝶结和多 reroute 布局必须通过本地定向回归后才能调整参数。

#### NodeDefinition metadata
- `Runtime/NodeDefinition.cs` 暴露 `SearchTags`、`InspectorSchemaKey`、traits、create/delete、runtime、preview 和 serializer 能力。
- `NodeRegistry` 从 core/presentation descriptor 组合 `INodeDefinition`，并根据 `NodeKind`、type key、分类和 pin 生成搜索标签。
- `NodePaletteController` search must match display name, category, type key, kind, and generated tags. Keep this path metadata-driven before adding more node families.

### 2026-06-05：事件图 / 函数画布隔离修复

#### 问题：切换图表时事件图、函数内容串到一个画布/模型
- **现象**：新增一个事件图和一个函数后，再双击事件图，函数节点也混入同一画布。
- **根因**：`GraphListController.Load()` 内部会 `Persist()`。如果加载目标图前 owning session 的 `EditorSurfaceContext.ActiveAssetController` 仍指向旧 controller 或为空，`PersistAssetLibrary()` / snapshot / compile 会用错 controller。结果新加载的函数画布可能写回旧图，或函数库切回后重新加载默认 entry/return。
- **修复**：
  - 新增统一入口 `ActivateGraphListItem(...)`。
  - 切换顺序固定为：`SnapshotActiveAsset()` -> `SetSessionActiveGraphController(session, targetController, remember: false)` -> `targetController.LoadItem(item, snapshotCurrent: false, persistAfterLoad: false)` -> `SetSessionActiveGraphController(session, targetController)`。
  - 事件图、函数列表项增加 `PreviewMouseLeftButtonDown`，单击即可切换编辑界面。
  - 右键列表项也先激活目标项，再打开菜单，避免重命名/删除走错 controller。
- **本地验证建议**：可用事件图、函数来回切换，确认当前画布节点类型正确，并确认各自 `GraphFileModel.Nodes` 不混入其它图类型。
- **教训**：所有跨 controller 画布切换，必须先快照旧画布，再更新 owning session 的 active controller，再加载新图。不能让 `Load()` 在 session active controller 还是旧值/空值时触发持久化。

#### UI 调整：内容浏览器默认尺寸
- 底部行默认高度从 `300` 调整到 `360`，最小高度 `180`。
- 内容浏览器列和日志列改为 `* / *`，默认 50/50 平分底部面板，减少每次手动拉大的成本。

### 2026-06-03：阶段 5 常用节点清理与交互优化

#### 新增节点策略
- **新增范围**：鼠标、键盘、图像、逻辑、系统、调试共 22 个常用节点。
- **保留节点**：`GetMousePosition`、`KeyChord`、`WaitImage`、`WaitImageDisappear`、`Compare`、`BooleanAnd/Or/Not`、`StringConcat`、`WaitWindow`、`CloseWindow`、`WindowExists`、`GetForegroundWindow`、`SaveScreenshot`、`ShowMessage`。`MouseDoubleClick` 已彻底移出可见节点和 `NodeKind`，旧图自动迁移到鼠标点击重复触发。
- **已删除节点**：`MouseDrag`、`InputText`、`KeySequence`、`ClickImageCenter`、`SetVariable`、`Comment`。旧图加载时丢弃这些节点并写 Warn，同时过滤坏连线。
- **UI 策略**：小型纯数据节点继续使用 `CommonNodeViewModel` + 通用属性面板；组合键等交互复杂节点拆专用 ViewModel/Inspector/Executor。
- **Runtime 策略**：每个保留节点仍有独立 `NodeKind`，复杂节点走专用 executor；纯运算节点不注册 executor，只能由 `GraphRuntimeExecutor` 按需求值；其它简单执行节点可走 `Nodes/Common/CommonNodeExecutors.cs`。菜单定义由 `NodeRegistry.Definitions` 生成。
- **交互优化**：`KeyChord` 使用专用面板“增加按键 + 组合预览 + 操作模式 + 触发次数/间隔”；窗口类通用节点支持手填、运行窗口下拉、浏览 exe 推导进程名；`WaitImage.image_path` 可输出给后续 `FindImage.image_path`。
- **维护规则**：如果某个通用节点后续参数变复杂，再单独拆成专属 ViewModel/Inspector 面板；不要一开始就把所有小节点拆成几十个重复类。

#### 新增 Adapter 能力
- `IMouseAdapter`：移动、按键、滚轮、获取鼠标位置；双击由鼠标点击节点重复触发实现。
- `IKeyboardAdapter`：单键按下/抬起/点击、释放全部按键；组合键由 `KeyChordNodeExecutor` 编排按下/反向释放。
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
- **现状**：节点属性面板的加载、自动保存、浏览文件、刷新窗口列表、字段锁定均已下沉到 `Interaction/InspectorController*.cs`；参数面板在 `InspectorController.Parameters.cs`，通用小节点面板在 `InspectorController.CommonNodes.cs`，找图/窗口/程序/键盘辅助在 `InspectorController.SystemNodes.cs`，字段锁定在 `InspectorController.Locks.cs`，ToDo 目标选择入口在 `InspectorController.ToDo.cs`。
- **MainWindow 职责**：只保留 XAML 事件转发和窗口装配，不再维护属性面板业务规则。
- **维护规则**：新增节点属性 UI 后，同步改 `InspectorController.LoadNode()`、`ApplyChanges()`、`RefreshLocks()`，不要把属性逻辑写回 `MainWindow.xaml.cs`。

#### 变更 2：找图节点支持可选识别区域
- **字段链路**：`FindImageNodeViewModel` → `NodeFileModel` → `GraphRuntimeNode` → `FindImageNodeExecutor` → `Python/find_image.py`。
- **字段**：`UseFindImageRegion`、`FindImageRegionX/Y/Width/Height`。
- **行为**：未启用区域时全屏截图；启用区域时先裁剪指定屏幕区域，再做 OpenCV 模板匹配，输出坐标仍是屏幕绝对坐标。
- **安全规则**：启用区域但宽高无效时记 `Warn` 并继续，不当成致命错误。

#### 变更 3：GraphValidator 增加执行前警告
- **检查范围**：找图路径空、找图区宽高无效、鼠标坐标缺省、键盘按键空、延迟值无效、启动程序路径空、选中窗口进程名空。
- **不警告孤立节点**：未接入开始执行链的节点允许作为画布临时备用节点存在，不写入 log warning，避免污染运行日志。
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
- **现象**：画布平移、连线、图谱列表、执行入口、属性锁定曾经全写在 `MainWindow.xaml.cs`，修改一个交互容易误伤另一个。
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
- **修复**：改用 JSON 临时文件通信；Python 侧读取图片必须用 `np.fromfile(path, dtype=np.uint8)` + `cv2.imdecode(...)`，不要用 `cv2.imread(path)`，否则 Windows 中文路径仍可能读图失败
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
- **根因**：`EnsurePythonAsync` 每次同步 import `cv2` / `PIL` / `numpy`，且历史实现没有缓存；超时 probe 进程也可能残留
- **修复**：首次检查在后台线程执行并缓存 `PythonEnvironmentResult`，后续执行复用缓存；并发检查通过 `SemaphoreSlim` 合并；缺环境提示同一进程只弹一次；超时 probe 会 kill 进程树
- **教训**：环境检测只做一次。当前是在首次执行前检查，不是 App 启动时自动检查

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

- 开发构建：`dotnet build .\AutomationStudioWpf.csproj`。
- 正式发布只允许 `Packaging/build-release.ps1`：Windows x64、.NET 8 自包含、多文件、不裁剪、不做单文件。
- 发布脚本固定私有 Python/三方 wheel 版本与 SHA256，分离 PDB，生成 notices、build manifest 和 SHA256 清单。
- 正式公开包必须传 `-CertificateThumbprint`；`-AllowUnsigned` 只用于明确标记的本地测试包。
- 安装器使用 Inno Setup 6，当前用户安装到 `%LocalAppData%/Programs/AutomationStudio`，不请求管理员权限。
- 完整命令和输出目录见 README；技术约束见下方“发布 / 安装稳定性”。

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
## 2026-06-04：内容浏览器 + 脚本/函数库资产系统 v1

### 新资产层
- 底部新增“内容浏览器”，资产类型为 `Folder`、`Script`、`FunctionLibrary`。
- `Script` 等价 UE 蓝图，包含自己的事件图和私有函数。
- `FunctionLibrary` 是全局库；库内函数只有勾选 `公开到库` 后才会出现在其他脚本的节点搜索里。
- `ContentAssetViewModel` 持有 `EventGraphs / Functions` 两个集合。
- `CallableGraphItem` 是节点菜单和执行器使用的可调用函数 DTO，包含稳定 `Id`、显示名、分组名、`GraphFileModel`。
- 事件图支持 `CustomEvent` / `CustomEventCall`。自定义事件作用域是当前脚本资产，可从主事件图调用辅助事件图中的入口；调用节点通过脚本级唯一 `CustomEventId` 绑定入口节点。

### UI 行为
- 启动默认隐藏 `EditorSurfaceHostRoot`，显示 `EmptyEditorPanel`，提示从内容浏览器打开资产；打开资产后把该 session 的 `EditorSurfaceControl` 放入 host。
- 底部左侧为内容浏览器，右侧为日志，中间 `GridSplitter` 可调比例。
- 画布不显示小地图，也不保留右上角缩放/重置/全览工具栏；缩放使用鼠标滚轮，`0` 键重置视图，节点加载时仍可自动适配。
- 打开脚本：显示事件图、自定义函数、画布和属性面板。
- 打开函数库：只显示函数列表、画布和属性面板。
- `RunGraph_Click` 只允许脚本里的事件图直接执行；运行前会自动编译未编译图，编译失败才阻止执行。

### 保存与迁移
- `GraphLibraryService.SaveContentLibrary()` 使用新版 `ContentAssets` 字段保存全部内容资产。
- 旧 `graph-library.json` 兼容读取：旧 `Graphs` 迁移到默认脚本事件图，旧 `Functions` 迁移到默认脚本私有函数；旧宏数据忽略。
- 新保存后以 `ContentAssets` 为准。

### 调用范围
- `CallableGraphResolver` 是函数可调用项的唯一来源，节点菜单、编译同步、运行时都必须走它。
- `CallableGraphItem.Name` 是画布函数调用节点标题；跨资产函数库调用也只放函数名。`CallableGraphItem.GroupName` 负责节点菜单分组，外部函数库用库资产名分组，避免画布标题显示 `函数库/函数名`。
- `显示最终代码` 只读预览基于当前 active graph snapshot 生成 pseudo-code。它只读，不改 runtime / JSON / dirty；可解析的 `FunctionCall` / `CustomEventCall` 默认展开函数或事件体，并静态追踪前置输入、函数入口参数和函数返回输出；带递归、深度和行数保护，无法解析时才保留符号调用注释。
- 脚本内只能调用本脚本私有函数，以及函数库中已勾选 `公开到库` 的函数。
- 函数库内部可以调用本库私有项；其他脚本不能搜索、编译同步或运行未公开库项。
- 编译错误路径必须用内容浏览器完整路径：`content/父文件夹/.../资产/图`。函数库在文件夹内时报错也必须带完整层级。
- 右键节点菜单按 `本脚本函数`、`本函数库` 或具体函数库资产名分组。
- 库函数节点标题只显示函数名；运行时和双击跳转仍用稳定 `FunctionId`，不靠名字解析。
- 自定义事件按所属事件图显示在节点菜单分组，可跨当前脚本的事件图调用；不得跨脚本或函数库调用。

### 重要坑点
- 打开节点菜单前必须 `SnapshotActiveAsset()`，否则函数参数刚改完但未写回 `GraphFileModel`，调用节点会缺 pin。
- `GraphListController.LoadItem(item, snapshotCurrent: false, persistAfterLoad: false)` 用于上层导航加载；跨事件图/函数切换由 `MainWindow` 统一快照，并用 `SetSessionActiveGraphController(...)` 同步 owning session，避免图谱混写或函数库切回后回默认图。主窗口 tab 切换不得调用它，已加载 session 只做轻量激活。
- `ExecutionController`、`NodePaletteController`、`GraphCallReferenceSyncService` 都读取 `CallableGraphResolver` 产出的 `CallableGraphItem`，不要直接扫全局 `FunctionListItems`。
- `公开到库` 是硬隔离：旧图如果跨脚本引用未公开库项，编译时报错并保留 dirty，不自动删节点。
- `CustomEventCall` 在当前 `GraphExecutionPlan` 内找 `CustomEventId` 对应入口；运行时用 `custom_event:{id}` 调用栈阻止递归。
- WPF + WinForms 命名冲突仍要用全限定名，尤其 `Brushes`、`Color`、`Cursors`、`HorizontalAlignment`。
## 2026-06-04 恢复记录：编译系统、dirty 规则、左侧折叠栏

- 当前远端基线 `be3b34f Add content browser asset system` 不包含上一轮未提交改动；恢复时在该提交基础上补回，不做 git 回退。
- 编译系统由 `GraphCompileService` + `GraphCallReferenceSyncService` 负责；编译时同步 `function_call` 的参数引脚，并删除失效连线。
- dirty 分级：节点移动/布局变化只调用 `MarkLayoutDirty()`；节点参数、连线、函数签名变化调用 `MarkLogicDirty()` 并设置 `IsCompileDirty`。
- 左侧图谱栏为两块独立区域：事件图表、函数；空列表折叠，新增后展开，删除到空后清空画布。
- 新建脚本/函数库默认不自动创建图表，用户点击对应 `+` 后才创建。
# AutomationStudio recovery note (2026-06-04)

- Content browser keeps `ContentBrowserItems` as root data, projects folders into `ContentFolderItems`, and projects current-folder tiles into `ContentVisibleItems`; projection refreshes use `RangeObservableCollection.ReplaceAll(...)`.
- Folder tree is left-side only; tile grid is right-side. Empty folders do not show an expander because `HasFolderChildren` only counts child folders.
- Folder tree indentation uses `TreeIndent`; `TreeDisplayName` must stay plain name. The tree/tile split is adjustable through `ContentBrowserTreeSplitter`.
- Asset tiles expose `TileGlyph` / `TileBrush` by `ContentAssetKind`; inline rename still binds `IsEditing`.
- Asset drag/drop to folder supports move/copy/cancel. Copy creates new asset/graph IDs and deep-copies graph DTO data.
- Graph sidebar keeps separate event/function controllers. Empty sections collapse; adding expands; deleting last item clears canvas through `GraphListController`.
- Section collapse state is transient per opened asset. `*SectionHasState` distinguishes user-collapsed non-empty sections from never-toggled default sections.
- Dirty graph sections show orange header badges; dirty graph items show `*` plus orange item highlighting.
- `MarkLogicDirty()` marks compile dirty. `MarkLayoutDirty()` only marks save dirty.
- New graph/function list items start with `IsCompileDirty = true`; compile clears graph dirty flags only after signature/call-node sync and validation succeed.
- Function library rows persist `IsPublicToLibrary`; only public rows appear in node search, compile sync, and runtime lookup from other scripts.
- `CustomEvent` stores `CustomEventId`; call nodes serialize it separately from `FunctionId`. Do not reuse function ids for events.
- `GraphLibraryService` defaults to `%APPDATA%/AutomationStudioWpf`, but tooling may set `AUTOMATION_STUDIO_LIBRARY_DIR` for isolated local validation.
- Compile only clears graph compile flags when validation succeeds, and marks only assets changed by call-reference sync as save dirty.
- `Window_PreviewKeyDown` routes `Delete` / `F2` to focused graph/content list, while text boxes keep normal editing behavior.
- Content tree commands track `_contentFolderSelectionActive` so folder right-click/`Delete`/`F2` cannot act on a stale tile selection.
- Build gate: `dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck` must stay `0 warning / 0 error`.
## 2026-07-02：视觉优化补充

- 顶部工具栏不要再使用 WPF 默认 `ToolBar` 直接承载按钮；默认 gripper/overflow 视觉不符合当前暗色编辑器风格。主窗口顶部命令区使用自绘 `Border + StackPanel` 分组，保留按钮 `x:Name` 和事件处理器。
- 顶部工具栏按钮必须保持轻量 command strip：默认透明、hover 高亮、用分隔线区分基础命令/编辑命令；不要再把整组按钮包成厚重胶囊边框。
- 工具栏分为基础命令组和编辑命令组：基础命令常显，编辑命令组只在脚本/函数库 session 打开后显示。视觉分组用 `EditorToolbarGroupBrush`，不要把所有按钮铺成一排。
- 窗口标签栏使用独立 chrome 背景、圆角 tab、active 底部 accent 线、dirty 小点。后续改 tab 样式时不得破坏拖拽独立窗口事件。
- detached 子窗口只保留“停靠回主窗口”按钮；编辑靠点击窗口激活，关闭靠 OS 窗口 `×`，不要恢复“编辑此窗口/关闭窗口”冗余按钮。
- 底部内容浏览器和日志区按两个独立卡片处理，避免硬边框大黑块。主日志窗口和独立日志窗口要保持同一视觉层级。
- `NodePalette`、执行冻结遮罩、最终代码窗口、脚本属性窗口都属于编辑器浮层；必须使用卡片背景、边框和阴影，不要退回系统默认窗口或朴素白底控件。
- `ScriptPropertiesWindow` 是代码构建 UI；热键行需要足够列宽，窗口默认宽度不应低于 820，内容超出走中间滚动区，底部保存/取消固定。
- 新增视觉资源优先放 `App.xaml` 的 `Editor*Brush`，局部 XAML 只负责布局和状态触发；不要散落硬编码颜色。
## 2026-07-07：全局主题 token 收口

- 主题必须是应用级配置，只能由 `AppThemeService.Apply(AppSettings)` 修改；设置保存在 `%AppData%/AutomationStudioWpf/app-settings.json`，不得写入脚本/函数库资产。
- `App.xaml` 是全局主题 token 源。新增 UI 优先使用 `Root/Chrome/Panel/Card/Field/Text/Muted/Border/Hover/Selected/Accent/Warning/Error/Disabled` 语义 brush；不要在页面、控件、C# 构造 UI 里散写 `#RRGGBB`。
- `AppThemeService` 现在支持 `#RRGGBB` 和 `#AARRGGBB`，半透明遮罩类 token（例如 `EditorExecutionOverlayBrush`）也必须走 palette，而不是写死在 XAML。
- C# 动态 UI 必须用 `ThemeResourceHelper.SetResource(...)` 或 `ThemeResourceHelper.Brush(...)`：`ScriptPropertiesWindow`、`ScriptPropertiesSummaryControl`、`SettingsWindow`、`TrayMenuWindow`、`ThemedDialog`、拖拽预览、节点菜单等都不能固定 `Brushes.White` / `new SolidColorBrush(#...)`。
- `SettingsWindow` 的主题/强调色是实时预览：点亮色/暗色、输入合法强调色、点预设色必须立刻刷新主窗口、编辑器、内容浏览器、日志、detached 窗口和已有自绘窗口；当前设置窗只保留 `应用并关闭`，实时预览即写入设置，按钮负责确认保存并关闭窗口。
- 硬编码色白名单只允许：节点类型色、pin/连线语义色、日志 level 语义色、截图/鼠标拾取颜色预览、阴影黑色、透明色、主题 palette 本身。其它 `#[0-9A-Fa-f]{6,8}`、`Brushes.*`、`new SolidColorBrush(...)` 都需要解释或改成 token。
- 亮色主题验收标准：主窗口上方、内容浏览器、日志、编辑器、属性面板、弹窗、菜单、Tooltip 必须同时切到浅色层级；不能出现“上白下黑”、白底白字、浅灰字贴浅底、按钮文字被背景吃掉。
- 2026-07-07 根因补充：旧 `MainWindow.ThemeUnifier` 曾在 `OnContentRendered` 后用冻结暗色 brush 直接写内容浏览器、日志和右键菜单本地属性，导致设置窗口切到亮色后主界面仍黑。该类以后只允许安装内容浏览器交互/重命名校验等 hook，不允许再承担“统一暗色上色器”职责。
- 主题切换事件由 `AppThemeService.ThemeChanged` 广播；主窗口负责刷新日志 FlowDocument、detached 子窗口、最终代码窗口和少量代码生成 UI。XAML 主题 token 使用 `DynamicResource`，避免已创建控件拿着旧资源不刷新。
- 亮色主题按 Codex 风格使用中性灰白层级（灰背景、近白卡片、中性灰边框），禁止偏黄、偏蓝的大面积底色；`ContentAssetTileContainerStyle` 的资产名必须显式绑定 `EditorTextBrush`，选中态使用 `EditorSelectionTextBrush`，不能固定白字；`ThemedDialog` 按钮必须属于同一视觉族，默认按钮只用强调边框/轻微高亮区分。
- 2026-07-07 亮色二次修正：亮色不能大面积纯白刺眼，主背景/画布/日志/内容区优先用低亮度冷灰白；正文文字用灰黑，不用纯黑。执行 pin/执行连线必须走 `EditorExecutionPinBrush`，亮色下为深灰，避免白线在浅色画布上不可见；连接命中高亮和预览线分别走 `EditorConnectionHitBrush`、`EditorPreviewConnectionBrush`。
- 2026-07-14 最终亮色基线：不要用“整体继续压暗”修刺眼问题，那会形成脏蓝灰。应用背景使用中性灰约 `#ECEEED`，主面板约 `#F5F6F5`，卡片约 `#F7F8F7`；节点画布是例外，使用更深的 `#C5CDCA/#D0D7D5`，保证白色节点和深色连线清楚。主窗口用独立 `EditorWindowBorderBrush` 深灰外框。强调色只用于边框、窄 accent 和轻染选中底；选中底最多混入约 17% 强调色，禁止整块高饱和蓝色铺面。
- `SettingsWindow` 默认尺寸不得太小，当前约 `900x680`，最小约 `760x560` 且允许 resize；内容多时滚动，不要把设置项挤没。强调色必须支持项目内自绘色盘（`AccentColorPickerWindow`），禁止调用 Windows 原生颜色面板。
- `SettingsWindow` 底部只保留 `应用并关闭` 一个按钮；实时预览仍即时生效，按钮负责确认保存并关闭。不要再恢复 `保存设置` / `取消` 双按钮和回滚流程，避免交互重复。
- 用户自选强调色必须原样写入 `AccentBrush`，不要为了可读性直接暗化用户选择的颜色。需要深一点的选中底色时，单独派生 `EditorListSelectedBrush` / `DropdownSelectedBrush`；按钮文字色通过亮度在深/浅前景间切换。
- `ScriptPropertiesSummaryControl` 属于 C# 动态 UI，必须用 `SetResourceReference` 绑定主题 token；不要在创建时把 `Brush` 取出来赋给 `Background/Foreground/BorderBrush`，否则亮/暗主题切换后空白画布里的脚本属性摘要会保留旧主题色。

## 2026-07-13：运行稳定性 / 数据安全收口

### 多线程业务值与执行失败
- `False`、`0`、空字符串是正常业务输出，不代表 branch 失败。`WarnButContinue` 也不取消 sibling branches。
- 只有 `FatalStop`、未捕获异常、不同 branch 写同一 `nodeId:pin` 且值不同，才使多线程整体失败并取消其它 branch；用户取消单独记录为取消，不记录成错误。
- 所有 branch 必须从同一父 context baseline `Fork(...)`。branch 正常完成后立即把变化合并回父 context；fatal branch 的部分输出禁止合并。全部 branch 成功后才执行 `exec_completed`。
- runtime 输出是本次执行缓存，不是永久变量。节点重算前必须清除自身上一轮输出；函数调用输出与 CustomEvent 入口参数不得复用旧值。fork/merge 必须同时合并新增、变化和删除，避免父 context 保留已清理的旧输出。
- branch wrapper 捕获 fatal/异常后必须先取消 linked token，再返回结果；禁止等 `Task.WaitAll(...)` 结束后才取消，否则无限触发或长等待 sibling 会卡住整体。

### Python 唯一环境与进程清理
- `PythonEnvironmentService` 是 Python 路径和依赖检测的唯一入口。环境检测与 `PythonScriptAdapter` 实际执行必须使用同一个 `ValidatedPythonPath`，禁止各自扫描解释器。
- 所有 Python 相关进程只能通过 `PythonEnvironmentService.StartOwnedProcess(...)` 启动；包括 `where.exe`、`python --version`、依赖 import probe 和正式脚本。禁止新增裸 `Process.Start()` 后不登记的 Python 路径。
- 首次启动 Python 时创建带 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` 的 Windows Job Object，并把进程加入其中。正常退出会显式 kill + wait；应用崩溃或被强制结束时，Job handle 关闭会兜底终止已登记进程及其后代。
- 拒绝使用 `Microsoft/WindowsApps` 的 Python execution alias。该别名可能经 broker 转发到其它进程，所有权不稳定；只允许直接、已验证的 `python.exe`。
- 进程启动、加入 Job、写入活动表必须走统一临界路径。退出开始后 `_acceptProcesses=false`，任何刚启动但尚未登记的进程必须立即被 kill，禁止漏进程竞态。
- 环境 semaphore、解释器探测、依赖检查、Python 脚本执行都必须接受 `CancellationToken`。
- 只允许缓存验证成功的环境；失败结果下次运行必须重检。解释器启动抛出 `Win32Exception` 时必须 `Invalidate()`，禁止持续复用失效路径。
- Python 超时、用户取消或执行异常时，必须 `Kill(entireProcessTree: true)` 并等待退出；stdout/stderr 收尾后才能删除临时请求 JSON。参数使用 `ProcessStartInfo.ArgumentList`，避免中文和空格路径转义错误。
- 如果进程树无法确认退出，禁止删除仍可能被子进程读取的请求 JSON；保留文件并记录 error，避免用“清理临时文件”掩盖孤儿进程。
- Python adapter 必须向 `PythonEnvironmentService` 登记活动进程；应用真正退出时同步终止全部登记进程。禁止用 `Environment.Exit(...)` 抢在取消清理前强退。
- `MainWindow.Window_Closing` 是正常清理入口，`App.OnExit` 还必须再次 dispose 主窗口服务与共享服务，作为窗口关闭流程被绕过时的兜底；这些入口必须保持幂等。

### 原子 JSON 保存
- `AtomicJsonFileStore` 统一负责资产库、应用设置和外部图谱文件：同目录临时文件、UTF-8 无 BOM、`Flush(true)`、`File.Replace`、保留 `.bak`。
- 主文件损坏/缺失且 `.bak` 有效时，读取结果必须包含“来自备份 / 主文件是否修复 / 修复错误”；恢复后使用不覆盖有效 `.bak` 的原子 replace 立即修主文件。
- 主文件修复失败仍允许使用备份数据，但同一完整路径必须加入本次运行写锁；外部图谱可“另存为”新路径。主文件和备份都损坏时同样阻止原路径保存，禁止用空模型覆盖原数据。
- 显式保存失败不得清除 dirty；自动持久化失败只写日志和状态，不能崩溃进程。
- 脚本 `IsScriptEnabled` 必须随内容资产持久化；旧资产缺字段时按启用处理。任何保存入口都只能在持久化成功后清 dirty。

### 关闭顺序
- 主窗口只保留一个 `Window_Closing` handler。最小化到托盘不能停止脚本、热键或 detached session。
- 默认关闭策略是最小化到托盘：点击标题栏 `X` 后进程仍在运行，运行中的 Python 也会继续，这是“未退出”而不是进程泄漏。只有托盘“退出程序”或设置为“关闭软件”才进入真正退出清理。
- 真正退出时必须先 snapshot 和询问未保存内容；用户取消后保持脚本、热键、窗口和按键状态。只有确认退出后才能停止脚本、取消执行、释放键鼠、关闭 detached 窗口并 dispose hook。
- 鼠标拾取会拦截对话框点击：显示关闭确认前可临时停止，但用户取消关闭或保存失败时必须恢复拾取。
- 确认真正退出后采用立即清理：设置 `_isClosing`，启动 `RuntimeShutdownGate`，取消手动/热键任务，立即向全部登记 Python 进程树发送 Kill，最后释放全部键鼠。gate 只阻止新按下/移动/滚动，不能阻止 key-up/mouse-up 清理。
- 关闭确认取消前禁止启动 shutdown gate，也禁止停止脚本或释放输入。禁止恢复 `Environment.Exit(...)` 强退路径。

### 事件与资源解绑
- 真正退出必须调用 `DisposeWindowSubscriptions()`：解绑 `Logger.Entries`、`AppThemeService.ThemeChanged`、运行状态、active editor service、session、内容浏览器和图表集合事件。
- `CompositionTarget.Rendering` 属于静态事件；退出时无条件调用 `DetachAutoFitRendering()`。最小化到托盘或取消关闭时不得解绑。
- 可重建的 `ExecutionController` 在替换前必须解绑旧 `ExecutionStateChanged`；禁止匿名 handler 订阅 static/长寿命事件，否则无法可靠移除。

### 发布 / 安装稳定性（2026-07-16）

#### 目录所有权
- `ApplicationPaths.InstallRoot` 即 `AppContext.BaseDirectory`，只读；运行时禁止在此创建日志、缓存、临时 JSON、自动截图或设置。
- 资产和设置优先放 `%AppData%/AutomationStudioWpf`；不可写时本进程固定落 `%LocalAppData%/AutomationStudioWpf/Data`，不在单次保存失败后切换根目录。
- 日志、崩溃报告、缓存、自动截图和 Python 请求放 `%LocalAppData%/AutomationStudioWpf`；只有 LocalAppData 根目录不可创建时诊断文件才回退 `%TEMP%/AutomationStudioWpf`，目录失败不得阻止应用启动。
- 手动指定的绝对截图路径保持用户选择；相对截图路径必须解析到 `ApplicationPaths.AutoScreenshotDirectory`，不能依赖进程当前目录。
- 启动后台清理超过 24 小时的 `automation_studio_*.json`；自动截图保留 7 天且总量不超过 512 MiB。

#### 私有 Python 供应链
- Release 只认安装目录 `Runtime/Python/python.exe`，禁止扫描系统 Python、WindowsApps alias、用户 site-packages 或在线安装。
- 当前固定：Python 3.14.6 embeddable x64、opencv-python-headless 4.13.0.92、NumPy 2.4.6、Pillow 12.2.0。URL、版本、SHA256、许可证只维护在 `Packaging/vendor-manifest.json`。
- `Packaging/build-private-python.ps1` 必须校验每个下载 SHA256，配置 `python314._pth`，开放 `Lib/site-packages`，删除缓存/精确测试目录，并执行真实 import probe。
- 运行时必须设置 `PYTHONNOUSERSITE=1`、`PYTHONDONTWRITEBYTECODE=1`、`PYTHONUTF8=1`，移除 `PYTHONPATH/PYTHONHOME`，并使用 `-I -B`。关键坑：`-I` 隐含 `-E`，会忽略 `PYTHONDONTWRITEBYTECODE`；若缺少显式 `-B`，NumPy/OpenCV/Pillow 会把 `__pycache__/*.pyc` 写进安装目录，破坏只读契约并导致卸载残留。
- 所有 Python 进程都经 `PythonEnvironmentService.ConfigureIsolatedPythonStartInfo(...)` 统一注入 `-B` 和隔离环境；禁止调用方各自维护一套参数。`Packaging/build-private-python.ps1` 和 `build-release.ps1` 的 import probe 同样必须显式使用 `-B`。
- 私有运行时缺失、manifest 不匹配或 import 失败时，禁用找图并提示修复/重装；不得联网修复或偷偷回退系统 Python。

#### 发布产物
- 唯一正式入口是 `Packaging/build-release.ps1`。目标固定 `win-x64`、`SelfContained=true`、`PublishSingleFile=false`、`PublishTrimmed=false`。
- 正式构建要求 AutomationStudio 工作树干净；`-AllowDirty` 仅供本地 staging 验证。
- PDB 必须移出安装 stage 到 `symbols/`。stage 必须生成 `THIRD-PARTY-NOTICES.txt`、`vendor-manifest.json`；release 根生成 `build-manifest.json` 和 `SHA256SUMS.txt`。
- 无证书时脚本默认失败。`-AllowUnsigned` 只能生成明确标记的测试包；正式公开发布必须对应用 EXE 和安装器做 Authenticode SHA256 签名与时间戳。
- 托盘/窗口图标从 EXE 内嵌图标读取；禁止恢复 `Resources/AutomationStudio.ico` 输出旁车依赖。`2.png/icon.png/tray_icon.jpg` 已删除，不能重新打包。
- Windows 托盘和任务栏图标槽位由系统固定；视觉尺寸只能在 `WindowIconHelper` 中等比放大 HICON 内容。当前窗口/托盘共用 `1.12x` 缩放，并在生成 WPF `ImageSource` / WinForms `Icon` 后立即释放临时 HICON，禁止通过增大控件或泄漏句柄实现放大。
- `Find-InnoCompiler()` 必须同时查找当前用户 `%LocalAppData%/Programs/Inno Setup 6/ISCC.exe` 和系统 Program Files 安装位置；不能假设 Inno 只能按机器安装。
- 简体中文 Inno 语言文件固定在 `Packaging/InnoLanguages/ChineseSimplified.isl`，来源 commit、SHA256 和 MIT 许可证记录在 `Packaging/vendor-manifest.json`。发布前必须先校验仓库内语言文件；禁止依赖构建机 `compiler:Languages` 下未必安装的可选文件。
- `THIRD-PARTY-NOTICES.txt` 必须包含简体中文 Inno 翻译许可证；语言文件或许可证缺失、哈希不一致时发布立即失败。

#### 单实例与升级
- `SingleInstanceCoordinator` 使用当前 Windows 会话内的命名 Mutex；重复启动只发 Activate 命令并退出，绝不降级多开。
- `--shutdown-for-update` 使用独立命名事件通知主实例真正退出，不走最小化到托盘。无主实例时命令进程直接成功退出，不创建 UI。
- 升级退出仍必须 snapshot 并显示未保存确认。用户取消时主进程保留；Inno `PrepareToInstall` 最多等待 60 秒，超时返回错误并中止，禁止覆盖运行中的文件。
- Inno AppId 固定 `{DA1B9FE1-FE96-460D-8C7E-E5F4E71338AD}`，当前用户安装到 `%LocalAppData%/Programs/AutomationStudio`；卸载默认保留 AppData 用户数据。

#### Git 跟踪的发布门禁（2026-07-30）
- `Packaging/verify-release.ps1` 是 Git 跟踪门禁，不属于本地 smoke。`Tests/CodexSmoke` 仍保持 Git 忽略。
- `build-release.ps1` 默认执行 Host 门禁；仅显式 `-SkipVerification` 可跳过。跳过必须写 `hostStatus=skipped`；门禁异常必须写 `hostStatus=failed`，禁止 manifest 长期停在 `pending`。
- 隐藏入口 `--release-self-test --release-test-root <path> --report <path>` 只允许访问 `%TEMP%/AutomationStudio.ReleaseVerify` 下带 `.automationstudio-release-test` 安全标记的子目录。该模式不创建主窗口、托盘、Hook 或真实 AppData。
- 自检必须真实运行私有 Python 版本/依赖 import、正式 `Python/find_image.py`、长 Python 任务取消，并确认请求 JSON、进程树和可写目录均被隔离清理。
- Host 安装使用验证专用 AppId，不触碰真实 AutomationStudio 安装记录；连续启动 5 次只能留下一个实例，`--shutdown-for-update` 后应用和私有 Python 都必须归零。
- 安装前后比较安装目录完整文件哈希；任何新增、缺失或变化都失败。卸载后安装目录必须消失，隔离用户数据必须保留。
- `Packaging/artifacts/release/1.0.4/verification/release-verification.json` 已在本机通过 fresh install、私有 Python、找图、取消、单实例、退出、零写入和卸载门禁；当时升级项按契约记录 `NotRun`。随后 `1.0.5` 自动选择 `1.0.4`，实际完成运行中升级、安全退出、版本替换和用户数据保留验证并通过。该结果仍属于 Host 门禁，正式发布前必须在干净 Win10/11 VM 补跑。
- VM 模式必须在干净 Windows 10/11 x64 快照中使用正式 AppId，标准用户、断网、无系统 Python、无预装 .NET Runtime，并执行 Defender 扫描。VM 报告和人工“未保存修改时取消升级”未通过前，包不得发布。

#### 崩溃与紧急清理
- `CrashReporter` 捕获 Dispatcher、AppDomain 和未观察 Task 异常，报告写入 LocalAppData，包含版本、OS、架构和完整堆栈。
- Dispatcher 未处理异常视为不可恢复：先启动 shutdown gate、停止脚本、杀登记 Python、释放键鼠/hook，再显示主题错误并退出；禁止捕获后继续运行损坏状态。
- `Window_Closing`、崩溃路径、`App.OnExit` 共用幂等 `CleanupForApplicationExit()`；禁止复制另一套清理顺序。
- Job Object 仍是强制结束/进程崩溃时的 Python 兜底；正常退出必须先显式 kill + wait，不能只依赖操作系统回收。

### Undo 与结构边界
- `GraphCommandService` 快照保存序列化 JSON，before/after 各序列化一次；Undo/Redo 各自最多 100 条、估算总容量最多 64 MiB，超限删除最旧记录。
- Undo/Redo 必须先成功恢复目标快照，再移动历史栈；恢复失败时尝试回滚当前快照并保留历史项，禁止先弹栈造成永久丢失。
- `GraphRuntimeExecutor` 按 `MultiThread`、`Functions`、`Logging` partial 拆分；`FinalCodePreviewGenerator` 按 `ControlFlow`、`Expressions` partial 拆分。拆分只允许移动代码，不得改变运行语义。
- `Themes/EditorSharedStyles.xaml` 保存主窗口与编辑 surface 共用状态样式；`Themes/EditorSurfaceStyles.xaml` 保存 surface 通用控件样式。页面 XAML 继续持有布局、`x:Name`、事件和 Binding，禁止在资源拆分时改交互语义。
- 延迟 auto-fit 必须绑定触发时的 session 和 graph；回调执行前目标已变化则取消，禁止旧图的渲染回调缩放新 tab。
# Release Stability Addendum (2026-07-16)

This section is the source of truth for packaging and process-lifecycle work. Read it before changing release, Python, Hook, shutdown, or persistence code.

## Process Ownership

- Only AutomationStudio's internal Python processes are owned by `PythonEnvironmentService` and its Job Object. User programs launched by the `启动程序` node are intentionally external and are not closed by application exit or upgrade.
- `OwnedProcessLauncher` creates internal processes suspended, assigns them to the Job Object, then resumes them. No code may reintroduce `Process.Start()` followed by delayed Job assignment for Python.
- Cancellation/timeout/failure order is: cancel token, kill the entire process tree, bounded `WaitForExit`, bounded stdout/stderr drain, confirm `HasExited`, then delete the request JSON. If exit cannot be confirmed, keep the request file and log PID/path.
- New Python request files live in `%LocalAppData%\AutomationStudioWpf\Temp\PythonRequests`; old `%TEMP%\automation_studio_*.json` files are cleanup-only compatibility input. The install directory is read-only.

## Data and Upgrade Safety

- `ApplicationPaths.UserDataRoot` is resolved once per process: writable `%AppData%\AutomationStudioWpf`, otherwise `%LocalAppData%\AutomationStudioWpf\Data`. Do not switch roots after an individual save failure.
- `AtomicJsonFileStore` remains the only JSON persistence path: UTF-8 without BOM, flush, same-volume replace, one `.bak`, and no overwrite when both primary and backup recovery are unsafe.
- Inno upgrade must receive `--shutdown-for-update`, require process exit code `0`, wait for the single-instance mutex and private Python processes, and abort after 60 seconds. It must never kill the main process or overwrite files while it is alive.
- Unsigned packages are allowed only for local validation. Their filename must end in `-UNSIGNED.exe` and `build-manifest.json` must contain `signed=false`.

## Hook and Shutdown Rules

- `ScriptHotkeyService` and `MousePickController` set disposed/shutdown state before unhooking. Hook callbacks never throw and never post to a Dispatcher whose shutdown has started or finished.
- Dispatcher callbacks must re-check disposed/session state at execution time. Unhook failures log the Win32 error once; they must not trigger a second UI dialog loop.
- `CleanupForApplicationExit()` is the single normal-exit cleanup order. Minimize-to-tray and cancelled close do not start `RuntimeShutdownGate` and do not stop scripts or release input ownership.
- Crash handling may use `RuntimeEmergencyCleanup` for non-UI runtime resources. Non-UI exception handlers must not call WPF window methods or enqueue work onto a dead Dispatcher.

## Multi-thread and Window Placement Contract

- `MultiThread` exposes dynamic `exec_thread_N` outputs plus fixed `exec_completed`, `exec_failed`, and Boolean `result` outputs. `result` describes scheduler execution, not branch business values.
- `False/0/empty` branch outputs are valid data. Only fatal execution, an uncaught exception, or a conflicting branch write cancels siblings.
- Each branch runs on a forked `RuntimeContext`. Branch changes stay staged until every branch succeeds; merge is deterministic and atomic from the parent context's perspective. Failed branches never leak partial outputs.
- When `exec_failed` is connected, a fatal branch sets `result=false` and routes to that pin as `HandledFailure`. When it is not connected, the same error remains `FatalStop`. User cancellation never routes either completion pin.
- `WM_GETMINMAXINFO` is the only supported maximize-bound calculation for the custom-chrome main window. Use the current monitor's `rcWork`, not `SystemParameters.WorkArea` or a fixed height; this preserves taskbar space on secondary and negative-coordinate monitors.
- The maximize hook is installed after `OnSourceInitialized` and removed by `DisposeWindowSubscriptions()`. It must not survive a closed HWND.

## Release Gate

- `Packaging\build-release.ps1` is the only release staging entry. It checks `git diff --check`, fixed vendor hashes, no `bin/obj/Tests/.cache/PDB/pyc` in stage, and unsigned naming/manifest consistency.
- `Packaging\verify-release.ps1` is local-only and ignored by Git. It validates private Python, repeated startup single-instance behavior, `--shutdown-for-update`, no private Python residue, and uninstall. It must refuse to touch an existing installation unless explicitly extended for a controlled test.
- Current machine Defender is unavailable (`0x800106ba`); this is not a pass. Final release requires a clean Windows 10/11 x64 VM with offline install, read-only install directory, Chinese/space paths, upgrade cancellation, and Defender checks.

## 2026-07-20：图结构与调用完整性

- `GraphStructureNormalizer` 是资产模型结构修复入口：脚本至少一个主事件图；主图恰好一个 `Start`；辅助图没有 `Start`；函数恰好一个 `FunctionEntry` 和一个 `FunctionReturn`。修复必须同步清理被删除边界节点的连线并保留 dirty/compile-dirty。
- 新建脚本本地函数和函数库函数必须统一走 `GraphStructureNormalizer.CreateFunctionGraph()`，初始模型固定包含一个不可删除的 `FunctionEntry`、一个不可删除的 `FunctionReturn` 及默认执行连线；禁止再通过临时清空 live `GraphEditorService` 来生成默认模型。
- `GraphListController.Load()` 必须先把目标写入 `ActiveItem`，再调用 `LoadFromModel()`。加载过程会同步触发 `GraphChanged`，若顺序反转，auto-fit 会记录旧图或空图，使新函数节点实际存在但落在当前视口之外。
- 图表加载属于导航，不得隐式持久化。图表新增/重命名/删除等真实变更需要持久化时，host callback 必须先提交 owning session，再写资产库，避免本地函数只存在于 session 集合而未进入 `ContentAssetViewModel`。
- 自定义事件 ID 在脚本资产内唯一。节点菜单、编译引用同步、运行时和最终代码预览必须统一使用 `CustomEventResolver`，禁止重新退化为只扫描当前图。
- 工具栏“执行脚本”始终执行脚本 `MainEvent`，与用户当前查看主图、辅助图或脚本私有函数无关；当前画布不得被隐式切换。
- Python 依赖检查必须递归遍历主图可达的函数和自定义事件；只检查主图会让函数内找图在运行时才失败。
- `GraphValidator` 对坏图必须是 total function：重复 Start、节点 ID、图表 ID、资产 ID、参数 ID只能生成校验错误，不能由 `Single/ToDictionary` 抛异常。
- 资产复制统一走 `AssetCloneService`：先建立全部图表 ID 映射，再复制模型并重写内部 `FunctionId`。复制脚本继承运行设置但默认禁用，避免热键冲突。
- “外部导入”按 `GraphAssetKind` 路由；事件图不能进入函数库。无 active 资产时按图类型创建脚本/函数库。“另存为”仅导出外部文件，不得清除内部资产 dirty。
- 新建键盘节点没有默认真实按键；空按键只能 warning 并跳过。滚轮 `ScrollDuration=0` 表示无限并必须跨保存保留。`GetCursorPos` 失败不能伪装为成功坐标 `(0,0)`。
- 内容目录索引必须防父级环；For 提前结束时日志和结果使用实际执行次数。

## 2026-07-20：架构收口 Phase 7/8

- Runtime/Nodes 不得直接依赖 WPF。运行时消息通过 `IRuntimeUiAdapter`，Python 环境诊断通过 `IUserNotificationSink`；WPF 层的 `WpfRuntimeUiAdapter` 负责 owner、主题和 Dispatcher，CoreTests 使用 null adapter。
- Inspector 已支持 provider 边界：`InspectorViewModel` + `INodeInspectorProvider` 负责结构化字段；已迁移通用纯节点、输入节点和基础控制流节点。复杂文件选择、窗口枚举、ToDo/参数动态编辑仍走旧面板，迁移时同一 `NodeKind` 只能保留一条生效路径。
- GraphLibrary 持久化边界分为 `GraphLibraryRepository`、`GraphLibraryPersistenceModels`、`GraphLibraryMapper`。Repository 只做 `AtomicJsonFileStore` 读写和恢复保护；Mapper 是 JSON DTO 与编辑 ViewModel 的兼容转换入口；不要在 ViewModel 内直接读写磁盘。
- Mapper 必须保留旧字段名、默认名、脚本启用状态、运行设置、函数公开状态和事件图角色规则。辅助事件图保存前清理 `Start` 及其连线；主图/函数边界归一化仍由 `GraphStructureNormalizer` 负责。
- Phase 8 当前已通过 Debug/Release build、CoreTests `33/33` 和 5 秒 WPF 启动探针。Phase 9 多程序集迁移尚未开始；未通过完整迁移门禁前，不得把项目拆成多个程序集。
- 新增 CoreTests 必须保持无真实 WPF Window、键鼠 Hook、Python 进程和用户目录副作用。`Tests/CodexSmoke` 继续本地-only，不提交。

## 2026-07-21：函数参数改名与 Inspector 输入焦点

- 函数开始、函数返回和自定义事件参数以 `GraphParameterDefinition.Id` 作为连接与调用绑定身份；改名只修改 `Name` 和现有 pin 的 `DisplayName`，不得修改 pin `Name/Id`。
- 参数名连续输入只能做原位预览，禁止在 `TextChanged` 中调用 `SyncPins()`、`RebindConnectionsToCurrentPins()` 或重建 Inspector。否则首字符会触发 `GraphChanged`，销毁仍在输入的 TextBox。
- 参数名采用“预览 + 提交”：有效名称在 Enter、失焦、保存或编译前提交；临时空值不覆盖模型；空值提交恢复原名；Esc 恢复本次编辑前名称。
- `PinViewModel.DisplayName` 必须支持属性通知，使标签可原位刷新；参数改名后 pin 对象、连接对象和连接 JSON 必须保持不变。
- Inspector 内 TextBox/ComboBox 正在交互时，通用 `OnGraphChanged()` 不得全量重载当前面板。参数添加、删除、排序、类型修改等结构操作必须由对应 handler 完成后显式刷新。
- 同一规则适用于节点标题：ToDo 引用同步可更新目标信息，但不得在用户编辑标题时重建当前 Inspector 或移动光标。
- 回归门禁：CoreTests 覆盖 Entry/Return/CustomEvent 改名保持 pin/连接；本地-only WPF smoke 覆盖首字符后 TextBox 实例仍存活并可继续输入。

## 2026-07-27：并发、执行状态与只读图工作区

- `ScriptRunManager` 的 `_running` 和 generation 必须在同一锁下更新。`PreventDuplicateRun=true` 时重复触发直接忽略；允许重启时先取消并等待旧 generation 完成，只有最新 generation 可以发布新任务。`RunningStateChanged` 必须在锁外触发，停止、退出和 Dispose 必须幂等。
- compile dirty 是失败保护契约：任何目标图编译失败后都必须保持 `IsCompileDirty=true`，包括原本 clean、但预检发现结构错误的图。失败不得清调用方 dirty；成功只清实际完成验证的范围。
- `GraphExecutionResult` 以 `GraphExecutionStatus.Completed/FatalStop` 为唯一状态源；`Success` 与 `ContinueExecution` 只能由状态派生。业务输出 `False/0/空字符串` 仍是正常数据，`WarnButContinue` 仍视为完成；只有 fatal、异常或多线程输出冲突取消 sibling branches。
- 连线 geometry 更新统一走 `IRenderUpdateScheduler`。WPF 使用 Dispatcher scheduler，CoreTests 使用同步/可控 scheduler；`ConnectionViewModel` 和 `ConnectionPathViewModel.Dispose()` 必须取消 pending operation，回调执行前后检查 disposed 与 Dispatcher shutdown，禁止关闭 session 后旧回调访问已释放 pin/surface。
- 一次编译、执行或最终代码预览只创建一份 `GraphWorkspaceReadModel`，并复用其中 snapshot、dependency index 和 reachable closure。`GraphDependencyIndex` 直接读取 `GraphSnapshot.Nodes` 的只读摘要；只有明确需要编辑副本时才允许 `ToMutableModel()`。tab 切换、hover 和普通 UI 刷新不得重建 read model。
- CoreTests 当前门禁包含热键并发重启、compile dirty 失败恢复、执行状态一致性、连线 pending callback 释放和 session surface/context 释放。生命周期测试必须在 STA 加载 `App.xaml` 资源，但不得创建或显示真实 Window。

## 2026-07-28：运行退出、Fatal 状态与快照校验收口

- `ScriptRunManager.BeginShutdown()` 是热键运行的唯一退出入口：先在状态锁内置 shutdown/disposed、失效全部 generation，再取消任务。关闭开始后，状态文字和 `RunningStateChanged` 必须通过 callback gate 统一抑制；`Dispose()` 不得再调用会刷新 UI 的 `StopAll()`。
- 热键循环必须保留 `GraphExecutionResult.FatalStop` 的实际失败信息。Count、Duration、UntilStopped 任一模式收到 fatal 后都立即停止后续循环，并显示失败原因；禁止随后用“脚本执行结束”覆盖。业务输出 `False/0/空字符串` 不属于 fatal。
- workspace 编译失败时，参与本轮验证的图全部保持 `IsCompileDirty=true`；保存 dirty 保持原状态。只有 workspace 完整验证成功后才能清 compile dirty。
- 多线程回归必须同时覆盖：`False/True/True` 正常完成、fatal 取消长等待 sibling、用户取消不记 fatal、同 key 同值允许、同 key 异值冲突、`exec_completed` 仅全成功后执行。
- `GraphSnapshot` 同时提供不可变 node/connection 摘要。验证器读取摘要，并仅为 `NodeSerializer` 逐节点生成副本；禁止为了检查自定义事件 ID、连接或调用引用而对整图重复 `ToMutableModel()`。Runtime plan builder 仍允许每次执行创建一次完整可变副本。

## 2026-09-09：界面主题与编辑交互收口

- 亮色主题使用 Codex 风格中性灰白 token：应用/面板/画布/节点正文/输入框均通过 `DynamicResource` 读取；普通文字使用灰黑层级，不得把纯白或纯黑作为大面积背景或正文色。暗色主题保留既有深色主色，只统一控件状态。
- 主题 brush 必须保持可变并由 `AppThemeService.ThemeChanged` 同步。节点 pin、连线、选中 glow 等现有对象不得缓存旧主题颜色；浅色选中 glow 跟随 accent，不使用固定黄色。
- 共享 Button、CheckBox、TextBox、ComboBox 的 hover、pressed、disabled、keyboard focus 状态统一由主题 token 驱动。TextBox 的编辑模板必须保留 `PART_ContentHost`，NumericUpDown 内部编辑框通过应用 TextBox 样式继承统一焦点和校验表现。
- Inspector 编辑期间不得因 `GraphChanged` 全量重建当前面板。普通 TextBox 支持 Enter 提交、失焦提交、Esc 恢复本次编辑值；NumericUpDown、参数名称编辑器保留各自更严格的提交/取消处理，避免双重提交和焦点竞争。
- 连线采用双层结构：`Panel.ZIndex=100` 的透明宽命中层负责选择、右键和路由点；节点位于 200；`Panel.ZIndex=150` 的视觉层只绘制轮廓、主线和选中高亮且 `IsHitTestVisible=false`；拖线预览为 450。视觉层必须低于节点，不得遮挡节点和 pin 操作。
- 连线路径继续复用 `ConnectionPathViewModel.PathGeometry` 和现有圆润规划器；禁止在鼠标移动或主题切换中重复计算同一路径。连线视觉厚度和轮廓由 ViewModel 属性提供，不能在 XAML 中复制路径算法。
- 画布右下角小地图及右上角无效缩放/全览按钮已移除；不得恢复对应 XAML、事件、控制器状态或无效 AutomationProperties。缩放、平移和 `0` 键重置仍由 `CanvasPanZoomController` 保留。
- 设置窗口采用左侧分类导航、右侧滚动内容区。当前分类为 `界面`（主题/强调色）和 `窗口行为`（关闭策略）；新增设置项必须放入对应分类页，不要恢复单列长表单。
- 连线视觉规则参考 UE GraphEditor：直连使用受端点方向和主方向约束的 cubic spline；带路由点的连接使用相邻段长度约束的圆角折线，圆角控制柄不能越过相邻段，检测到自交时移除导致回绕的路由点，不通过反转路由点顺序制造新路径。
- 侧栏、工具栏、Tab、底部内容/日志面板使用紧凑间距，但不能删除现有 splitter、最小宽度或窗口最大化工作区逻辑。Tab 的保存 dirty 使用小圆点，编译 dirty 由编译按钮独立表达，不用整块高饱和背景覆盖活动态。

## 2026-09-09：拾取窗口、函数侧栏与连线表现

- 鼠标拾取结果窗口不得缓存深色静态 brush。窗口标题、正文和复制/取消按钮必须通过 `ThemeResourceHelper.SetResource` 绑定动态主题 token，确保亮色主题下按钮文字与背景始终有足够对比度。
- 函数列表使用独立的函数行样式，不复用事件图的紧凑布局：函数行保留清晰的函数标识、名称截断、公开状态和 compile-dirty 提示；事件图列表的交互和尺寸不可被函数样式覆盖。
- 节点选中状态必须同时显示主题 accent 边框和外环，不能只依赖 ViewModel 的单条细边框。选中外环必须 `IsHitTestVisible=false`，避免影响节点拖动和 pin 操作。
- 直连线参考 UE GraphEditor：当端点横向间距足够时，Bezier 控制点先沿水平 pin 方向离开端点，再向目标端点收束；不能因垂直距离更大而生成陡直回绕。路由线仍使用受相邻段长度限制的圆角段，并复用已计算的 `PathGeometry`。

## 2026-09-09：UE 暗色主题与布局交互

- 暗色主题采用石墨灰分层：应用背景 `#1B1D1F`、面板 `#222426`、卡片 `#292C30`、画布 `#17191C`、节点正文 `#24272B`，正文使用 `#D8DCE3`，次要文字使用 `#9CA3AD`。交互 accent 默认为青蓝 `#3E9BB5`；节点语义色仍独立于通用 accent。
- 所有主题 brush 必须通过可变资源和 `DynamicResource` 更新。设置窗口中的主题预览是静态示意，不作为运行时 token；禁止在高频路径中缓存旧主题 brush。
- 所有布局 `GridSplitter` 使用直接调整模式（`ShowsPreview=False`），并显式声明 `ResizeDirection`、`ResizeBehavior` 和方向光标。命中区保持 7px，视觉指示线只负责 hover/drag 反馈；布局保存读取实际 `ActualWidth/ActualHeight`，不能保存预览值。
- Inspector 采用摘要卡片、字段标签、分组标题和校验提示的层级样式。输入期间禁止因 `GraphChanged` 重建 Inspector；结构变更才显式刷新，普通值只更新当前字段，避免焦点和光标跳动。
- 节点选中态使用 accent 边框和不可命中的外环，不再用固定黄色作为通用选中边框。连线继续采用命中层、节点层、视觉层分离，视觉层不接收鼠标事件。

## 2026-09-09：主窗口层级与脚本工作台

- 主窗口固定为标题栏、工具栏、Tab 栏三层；工具栏按文件、编辑、执行分组，分组只用弱分隔线，不重复套用大圆角容器。日志、侧栏和 Inspector 不再通过无效顶部按钮切换，仍保留面板本身的布局与焦点入口。
- 活动 Tab 使用低饱和选中背景、底部 accent 线和独立 dirty 小圆点；保存 dirty 与 compile dirty 不得通过整块高饱和填充混合表达。
- 选中脚本但未打开图表时，中央显示左对齐、可伸缩的脚本工作台概览；运行设置与热键在宽区域双栏显示，窄区域自动改单栏。该控件只编辑现有 RunSettings，不改变执行和保存语义。
- Inspector 顶部保持节点摘要，字段标签、说明、校验提示使用统一层级资源。输入期间不得因 GraphChanged 重建控件；普通值原位更新，结构变更才显式刷新。
- 面板外层只保留一层弱边界，内部 section 用间距和背景层级区分，禁止无目的的 Border 嵌套。底部内容浏览器和日志默认高度仅影响新布局，已有用户保存尺寸不强制覆盖。

## 2026-09-10：导航与编译状态边界

- 设置分类按钮使用固定图标列和左对齐文本列；选中态必须同步更新子 TextBlock 的前景色，不能只设置 Button.Foreground 覆盖显式子元素资源。
- 已移除无实际作用的顶部“日志/侧栏/属性”按钮及 Alt+D1~D3 面板切换逻辑。日志、侧栏和 Inspector 本身仍由布局拖拽与 Ctrl+D1~D4 焦点快捷键管理。
- 跨资产函数双击跳转后必须刷新主窗口编辑会话栏，再查询目标 surface，保证新打开的函数库会话立即显示 Tab。
- 路由事件临时切换 surface 时，若事件内部打开了新资产，不得在 finally 中恢复旧 session 的 controller；必须保留新 session，并重新同步 Tab 与活动 surface。
- 函数节点双击发生在 PreviewMouseLeftButtonDown；目标 session 的最终激活必须排入 Dispatcher，在本次输入路由结束后执行。只做已加载 session 激活，不得重复加载图或重建画布。
- 原生 `GridSplitter` 调整固定侧时必须保留相邻 `*` 定义。禁止把画布/编辑区的星号列转换成固定像素列，否则窗口右侧会出现空白，Inspector 会视觉左移。
- 编译 dirty 只表示执行结构或依赖发生变化。节点编号、ToDo 目标标题等可持久化显示元数据的自动修复只标保存 dirty，不应单独点亮编译按钮；真实入口/边界/连接结构修复仍必须标 compile dirty。
- Inspector 的提交事件可能在导航、切换资产和 snapshot 前被动触发；`ApplyChanges()` 必须比较应用前后的可持久化节点状态，值未变化时不得调用 `MarkLogicDirty()`，避免双击函数等无修改操作点亮编译提示。结构化字段和 ToDo 目标选择同样遵守该规则。

## 2026-09-11：设置窗口布局与输入反馈

- `SettingsWindow` 固定采用左侧分类导航、右侧滚动内容和底部操作栏。分类只有 `界面`（主题/强调色）与 `窗口行为`（关闭策略）；分类按钮的图标、标题、说明必须使用固定列并左对齐。
- 设置页面禁止重复的窗口外框、页面大卡片和分组卡片嵌套。页面标题、section header、字段间距和单层弱边界负责表达层级；目标尺寸约 `900x680`，最小尺寸约 `760x560`，右侧内容区不得产生横向滚动。
- 主题预览只使用静态示意色；运行时主题、强调色和控件状态必须通过 `DynamicResource` token。强调色输入、预览和常用色按钮保持实时预览，`应用并关闭` 只负责确认并关闭。
- 非法强调色必须显示字段下方的内联提示 `请输入 #RRGGBB 格式的颜色`，禁用应用按钮并保留输入焦点；禁止使用阻塞式错误弹窗。恢复合法输入后立即清除错误态。
- 关闭策略选项使用整行 RadioButton，选中态用低面积 accent 背景和左侧指示条表达；文案必须区分最小化到托盘与真正退出，不能改变 `AppWindowCloseAction` 的读写语义。
- 分类切换只切换现有页面可见性，不重建输入控件，不清空草稿，不移动当前输入焦点。新增设置项必须复用统一标签列、动态资源和内联校验规则。
- 窗口行为页使用扁平的两行 RadioButton 选项，不再包裹大面积卡片。标题和说明必须共享同一内容列并保持左对齐；全局 RadioButton 模板只绘制一次单选圆点，设置页内容不得再模拟第二组圆点。
- 窗口行为选中态只保留低面积选中背景和独立的左侧 accent 指示条。指示条不得改变 RadioButton 的命中区或挤压标题列，两个选项必须保持统一高度、间距和文字起始位置。

## 2026-09-11：脚本启用状态与工作台同步

- `ContentAssetViewModel.IsScriptEnabled` 是脚本启用状态唯一来源。资产角标、右键菜单和脚本工作台开关必须复用 `MainWindow.SetScriptAssetEnabled()`，禁止在工作台直接写属性绕过运行中检查、热键冲突检查、持久化和热键刷新。
- 脚本工作台标题区右上角显示“启用脚本”开关和“已启用/已停用”状态。工作台订阅资产 `PropertyChanged`，外部入口切换状态时必须原位更新 CheckBox、状态徽标和 Tooltip，不得重建工作台或丢失运行设置输入焦点。
- 启用失败、热键冲突或运行中禁止禁用时，工作台开关必须恢复资产真实状态；成功切换继续标记资产 dirty、保存资产库并刷新全局热键。
- 资产角标恢复 `IsChecked` 必须使用 `SetCurrentValue`，不能直接赋值覆盖 `IsScriptEnabled` 的单向绑定。工作台在 Loaded 时订阅并同步状态，Unloaded 时取消订阅；重新加载只刷新启用状态，不覆盖未保存的运行设置草稿。

## 2026-09-11：UE 风格节点连线层级

- 编辑器连线分为命中层与视觉层：命中层保持在节点下方且可交互，视觉线层使用 `IsHitTestVisible=False` 并位于节点层下方；节点内部 Pin、圆点和执行箭头属于节点层，不得移到连线层。
- 当前绘制顺序固定为：连线命中层 `100`、连线视觉层 `150`、节点与 Pin `200`、拖线预览 `450`、选择框及浮层 `500+`。普通 Wire 不得覆盖节点标题、文字或 Pin 箭头。
- 节点正文通过带 Alpha 的 `EditorNodeBackgroundBrush` 透出下方连线；节点整体保持不透明，保证标题、描述、Pin 和箭头清晰。连线轮廓与语义色线使用低面积透明度，不创建新的运行时 Brush。
- 参考 UE `FConnectionDrawingPolicy` 的 WireLayer/ArrowLayer 分离规则；本项目箭头由节点内 Pin 模板绘制。连线层级调整不得修改 `PathGeometry`、路由点、连接模型或 graph/node/connection JSON。

## 2026-09-11：Splitter 直接拖拽与布局约束

- 所有编辑区、Inspector、日志和内容浏览器分隔条统一使用 WPF 原生 `GridSplitter`。原生 `Thumb` 内部负责 `DragStarted/DragDelta/DragCompleted` 的实时调整；项目代码不接管 `DragStarted/DragDelta`，只在 `DragCompleted` 读取实际尺寸并触发布局保存。所有分隔条显式声明 `ResizeDirection`、`ResizeBehavior="PreviousAndNext"`、`ShowsPreview="False"` 和 7px 命中区；禁止再次用 `OnMouseLeftButtonDown/Move/Up` 手写拖拽链替代原生控件。
- 布局保存只在原生拖拽完成后读取有效的 `ActualWidth/ActualHeight`，继续使用现有延迟保存；拖拽过程不得触发 Inspector 重建、节点重绘或其他业务刷新。
- Splitter 视觉指示线只负责 hover/drag 反馈，不能替代原生 `Thumb` 命中区域；不使用额外 `Panel.ZIndex` 覆盖相邻内容，也不把编辑区星号列强制转换为固定像素列。

## 2026-09-12：代码对照勘误与当前基线

- 当前五个分隔条是原生 `GridSplitter`：`GraphSidebarSplitter`、`InspectorSplitter`、`LogPanelSplitter`、`ContentBrowserTreeSplitter`、`ContentLogSplitter`。`LayoutSplitter.cs` 和 `LayoutSplitterMath.cs` 不再是实现入口；不要按旧的自定义鼠标捕获方案排查拖拽问题。
- 原生 `GridSplitter` 的 `Thumb` 内部负责拖拽过程中的相邻行列调整。项目只绑定 `DragCompleted`：编辑器分隔条转发到 `EditorSurfaceContext.NotifyLayoutChanged()`，主窗口分隔条转发到 `MainWindow.Settings.cs`；布局保存读取实际尺寸，经过有限值/最小值/最大值校验后使用现有延迟保存。
- 当前连线层级是命中 `100`、视觉 `150`、节点及 Pin `200`、拖线预览 `450`、选择框/浮层 `500+`。旧记录中的视觉层 `300`、位于节点上方的描述已经失效，不能作为实现依据。
- 当前工具栏是透明 command strip + 弱分隔线；`TopToolbarButtonStyle` 负责按钮自身的背景、边框和交互状态。历史记录中的“厚重圆角分组容器”不代表当前 XAML。
- 当前左侧图表栏使用 `EditorSidebarSectionStyle` 扁平分组；Inspector 使用 `EditorInspectorSectionStyle` / `EditorInspectorPanelSectionStyle` 分层。`EditorSectionCardStyle` 不应重新跨导航、Inspector、工作台复用。
- 设置窗口当前底部按钮文字为 `应用并关闭`；主题和合法强调色输入即时预览，非法强调色显示 `请输入 #RRGGBB 格式的颜色` 并禁用该按钮。分类页只有 `界面` 和 `窗口行为`，窗口默认尺寸为约 `900x680`，最小尺寸为约 `760x560`。
- 事件图与函数列表的活动项由 `SetSessionActiveGraphController` 统一维护；切换控制器必须清理另一侧的 `ActiveItem` / `SelectedItem`。`IsCompileDirty` 只表达逻辑/依赖编译脏，不得复用活动选中背景；Inspector 值未改变时不得点亮编译脏。
- 节点正文使用带 Alpha 的 `EditorNodeBackgroundBrush`，但节点整体保持不透明；不要通过设置节点整体 `Opacity` 来实现透线，否则会同时降低标题、文字、Pin 和箭头的对比度。
- 本节是当前代码基线。更早的日期条目保留为变更历史；若与本节冲突，以当前源码和本节为准，并在下一次结构性变更后继续追加勘误记录。

## 2026-09-12：资产删除、收藏清理与参数字段框

- 文件夹删除必须先归一化为顶层目标，再通过 `ContentAssetDeletionPlanner` 计算删除集合和上移映射。非空文件夹使用“删除文件夹及内容 / 仅删除文件夹 / 取消”三选项；仅删除文件夹时只把直接子项上移一级，更深层级保持不变；取消和运行中校验失败不得产生部分修改。
- 右键、键盘、多选和主题菜单不得各自实现删除逻辑。确认后统一应用计划、关闭受影响编辑会话、清理选择、刷新视图并持久化一次。
- 内容浏览器星号收藏功能已移除：不得恢复 `★`、`is:favorite`、`FavoriteAssetIds` 或 `IsFavorite`。旧设置中的收藏字段由 JSON 忽略，旧过滤条件在 `AppSettings.Normalize()` 中清理，下一次保存时不再写回。
- 普通矩形节点选中态只允许修改最外层节点边框；不得通过内部 Halo、整节点透明度或正文覆盖层伪造选中态。重定向点的点状选中环是无节点外框场景的例外。
- 函数库参数行与函数调用输入必须共享三列网格、固定类型/默认值列宽和字段框规范。调用输入的名称、类型虽为只读，仍必须使用动态主题资源绘制字段边界；禁止用裸 `TextBlock` 作为无边界字段。默认值编辑器继续复用现有控件和提交事件，不能为视觉重排重建输入焦点链。
- 本次问题根因：删除逻辑在普通菜单、多选和主题菜单中重复实现；收藏状态同时存在设置、资产 ViewModel 和搜索 token；调用输入名称/类型使用裸文本；节点内部 Halo 叠加到正文。后续修改优先收敛到共享规划器、单一状态源和可复用 Inspector 字段构造器，先加 CoreTests 再改 WPF 入口。
