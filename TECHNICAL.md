# 技术文档

## 架构设计

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

## 核心模块详解

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
- 图谱的加载和保存
- 执行计划的构建
- 引脚连接状态管理

```csharp
public class GraphEditorService
{
    public ObservableCollection<NodeBaseViewModel> Nodes { get; }
    public ObservableCollection<ConnectionViewModel> Connections { get; }
    
    public void NewGraph()
    public void SaveGraph(string path)
    public void LoadGraph(string path)
    public GraphExecutionPlan BuildExecutionPlan()
}
```

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
    public FindImageNodeViewModel CreateFindImageNode(...)
    public MouseClickNodeViewModel CreateMouseClickNode(...)
    // ...
}
```

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

#### 当前全部节点 (16个)

| 节点 | NodeKind | 分类 | 引脚 |
|------|----------|------|------|
| Start | Start | - | exec_out |
| FindImage | FindImage | 插件节点 | exec, result(bool), center(V2D) |
| FindText | FindText | 插件节点 | exec, text(String in), result(bool), center(V2D) |
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

### 3. Runtime 层（执行引擎）

#### 执行流程
```
StartNode → FindImageNode → MouseClickNode → ...
     ↓
GraphRuntimeExecutor.Execute()
     ↓
ExecuteChain() → ExecuteNode() → Win32 API
```

#### 环路检测
使用 `HashSet<string> visitedNodes` 记录已访问节点，防止无限循环。

#### Win32 API 调用
```csharp
[DllImport("user32.dll")]
static extern bool SetCursorPos(int x, int y);

[DllImport("user32.dll")]
static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

[DllImport("user32.dll")]
static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

[DllImport("user32.dll")]
static extern bool SetForegroundWindow(IntPtr hWnd);

[DllImport("user32.dll")]
static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

[DllImport("user32.dll")]
static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
```

#### 选中窗口节点
`SelectWindowNodeViewModel` 通过进程名定位窗口：
- 支持手填 `ProcessName`
- 支持前置 string 输入 pin：`process_name`
- 输出 `process_name` 方便后续日志/调试
- 输出 `result` 表示窗口是否成功置前
- 进程名支持 `notepad` 或 `notepad.exe`，运行时会去掉 `.exe`
- 空进程名、找不到窗口：`Warn + continue`
- Win32 异常：`Error + stop`

#### 找字节点
`FindTextNodeViewModel` 通过 EasyOCR 进行屏幕文字识别：
- 属性：`Text` (搜索文字，支持前置 String 输入), `SimilarityThresholdPercent` (置信度阈值, 默认 80)
- 输出 `result` bool 和 `center` Vector2D
- Python 脚本：`Python/find_text.py`，使用 EasyOCR 中英文模型
- 首次运行加载模型 ~15s，后续 ~5s
- 文字为空、Python 缺失、EasyOCR 未安装、找字未命中：`Warn + continue`
- 脚本退出码非 0 不阻塞，转为 Warn + 继续执行
- Debug 输出：stderr 打印前 10 个检测文字块及置信度到 log
- EasyOCR 安装：`pip install easyocr torch torchvision`

### 4. Logging 层

#### 日志级别
- **INFO**: 白色，普通信息
- **WARN**: 黄色，警告信息（参数缺失等）
- **ERROR**: 红色，错误信息

#### 日志存储
- 内存：`ObservableCollection<LogEntry>` 用于实时显示
- 文件：`saved/log/Log_yyyy_MM_dd_HH_mm.txt`

## 关键技术决策

### 1. 为什么使用 Python 进行图像识别？

**优点：**
- OpenCV Python 绑定成熟稳定
- 丰富的图像处理生态（PIL, numpy）
- 易于扩展（OCR, 图像预处理等）

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

4. **实现执行逻辑**（GraphRuntimeExecutor.cs）

5. **添加属性面板**（MainWindow.xaml）

### 添加新的 Python 功能

1. 在 `Python/` 目录添加脚本
2. 在 `PythonAutoInstaller.cs` 添加依赖检查
3. 在 `GraphRuntimeExecutor.cs` 调用 Python 脚本

## 踩坑记录

> 以下记录来自实际开发中的踩坑经验，按时间倒序排列，新记录追加到顶部。

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
- CodeGraph 索引异常：原索引报 `no such table: unresolved_refs`；删除损坏索引后，`codegraph.cmd init` / `codegraph.cmd init -i` 均失败为 `disk I/O error`。当前不要依赖 CodeGraph，需先解决 CLI/磁盘/权限问题
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
- 修复：属性命名、重复窗口问题

### v1.0.0
- 初始版本
- 蓝图节点编辑器基础功能
- 找图、鼠标、键盘、延迟等节点
