# AutomationStudioWpf 开发 Skill

## 项目定位
WPF 可视化节点自动化编辑器，类似 UE4 蓝图。技术栈 C# 12 / .NET 8.0-windows，零外部 NuGet 包。

## 踩坑记录（按时间倒序，新记录追加到顶部）

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
  - `InspectorController`：属性字段锁定灰态。
  - `NodePaletteController`：右键节点菜单，条目来自 `NodeRegistry.Definitions`。
  - `LogPanelController`：日志过滤、刷新、清空。
  - `GraphImportDropController`：JSON 图谱拖拽导入。
- **Removed**:
  - `FindText` / `找字` / `EasyOCR` 已从内置节点中删除。
  - 不要恢复 `Python/find_text.py` 或 EasyOCR 自动安装；后续 OCR 只能作为独立插件节点重新接入。

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
- **Root cause**: `EnsurePythonAsync` 每次同步检查 6 个库（包括 `torch`，import 极慢），5s 超时 × 6 = 最多 30s，且结果不缓存
- **Fix**: 首次检查后缓存结果（`_checked` / `_cachedResult` 静态字段），后续调用直接返回
- **Lesson**: 环境检测只做一次。启动时（`App.xaml.cs`）做首次检查，执行时复用缓存结果

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
- CodeGraph 当前阻塞：原索引损坏报 `no such table: unresolved_refs`；删除后用 `codegraph.cmd init` / `codegraph.cmd init -i` 重建均报 `disk I/O error`。不要信任当前 CodeGraph，先解决 CLI/磁盘/权限问题
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
    ├─ GraphEditorService    ← 节点/连接增删、保存加载、执行计划
    ├─ NodeFactory           ← ID 生成 + 节点创建
    ├─ NodeSerializer        ← ViewModel ↔ FileModel ↔ RuntimeModel
    ├─ NodeClipboardService  ← 复制粘贴（JSON 深拷贝）
    └─ PythonAutoInstaller   ← Python 环境检测

Graph (ViewModel)
    ├─ NodeBaseViewModel     ← abstract, virtual GetPinAnchor
    ├─ PinViewModel          ← Direction + Kind
    ├─ ConnectionViewModel   ← IDisposable, 监听 Node.PropertyChanged
    └─ ObservableObject      ← INotifyPropertyChanged 基类

Runtime
    ├─ GraphRuntimeExecutor  ← 顺序执行，HashSet 环路检测
    └─ GraphExecutionModels  ← record，扁平化运行时数据
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
