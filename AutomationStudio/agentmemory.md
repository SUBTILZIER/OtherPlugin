# AutomationStudio Agent Memory

## 当前偏好

- 中文、简洁；保留准确文件名/类名/方法名/命令/错误文本。
- 不自动 `git push`。只有用户明确说推送才推；推送前确认不包含测试功能相关文件夹。
- 项目 skill 源文件：`AutomationStudio/Agent/skills/automation-studio-wpf/SKILL.md`。旧 `.kimi/skills/...` 不恢复。

## 当前产品边界

- 只保留脚本 + 函数库。宏库已废弃；不要恢复 UI、资产类型、运行时、节点菜单或文档功能说明。
- 旧 `macro_entry` / `macro_output` / `macro_call` 仅作为旧文件 skip guard，保证打开旧文件不崩。
- `Tests/CodexSmoke` 是本地-only、untracked；不要提交。只有用户明确要求或触碰高风险交互时本地跑。
- 不改 graph / node / connection JSON schema；不改 `ConnectionSplinePlanner` 线形，除非用户给出新明确复现。

## 多窗口 / 函数库高风险规则

- 每个打开资产一个 `EditorSessionViewModel`，每个 session 自持 `GraphEditorService`、`NodeFactory`、`GraphCommandService`、`EditorSurfaceControl`、`EditorSurfaceContext`。
- Detached 窗口直接 host 自己的 `Surface`；激活 detached 只切 toolbar/global-command 目标，不覆盖 `_lastMainEditorSession`，不搬主窗口 surface。
- Surface 事件必须分类：明确用户交互才提升 active session；layout/binding/mouse move 这类被动事件不能切 active。
- 全局窗口事件用 `TryGetActiveEditorSurface()` 或事件来源 surface；无 surface 就 no-op。WPF parent walk 用安全 helper，避免 `FlowDocument` 崩溃。
- 当前图 controller 必须通过 `SetSessionActiveGraphController(session, controller)` 写入。不要只改 `_activeAssetController`。
- 切换事件图/函数顺序：snapshot 旧图 -> 设置目标 controller -> `LoadItem(..., snapshotCurrent: false)` -> remember。
- `GraphListController.Load()` 会 persist；如果 owning session controller 仍是旧图/空，可能把函数图写错或重载默认 entry/return。
- 保存、退出、编译、运行、函数调用解析前必须 commit/snapshot 所有打开 session。
- Toolbar 编译是 active-asset scoped；成功后要同步清目标 session 的 compile dirty、黄点、窗口栏、section badge、编译按钮。

## ToDo / 连线 / 内容浏览器

- ToDo 静态目标用 `TargetNodeTitle + TargetNodeNumber` 双键；`TargetNodeId` 只用于编辑器维护引用。选择下拉项后立刻写 VM 并 snapshot。
- `target_title` / `target_number` 输入 pin 是运行时 override，不能清静态下拉值。
- Return-after-target 模式目标链自然回到源 ToDo 时停止子链并返回源 `exec_out`，不是递归错误；直接自跳仍非法。
- 可见线走 `ConnectionPaths`，持久化/runtime 走 `Connections`。可见线命中用 Bezier 采样找 backing connection，不退回端点直线。
- 组合连接编辑用 `RunBatchedEdit(...)`；批量内只标脏，最外层 flush。
- 内容浏览器刷新/搜索/定位复用 `ContentBrowserIndex`。资产新增、删除、重命名、移动后重建 index。
- 函数调用节点双击按 stable `FunctionId` 跳转目标资产/函数图，不按显示名。
- 函数库函数在节点菜单里按库资产名分组；画布上的函数调用节点只显示函数名，不显示 `库名/函数名`。默认节点标题不要带冗余 `节点` 后缀，开始节点显示 `开始运行`。

## UI / 主题 / 日志

- 共享暗色资源在 `App.xaml`；不要在窗口/surface 重复结构色。
- 节点 header、pin、日志级别、编译按钮、弹窗常用 brush 使用静态冻结 brush。
- 日志面板是只读 `RichTextBox`；全局快捷键必须对 `TextBoxBase` 放行。日志过滤 RadioButton checked dot 必须可见。
- 执行日志由 `GraphRuntimeExecutor` 聚合成节点块，时间戳只到秒；节点内部细碎日志进入块内详情。多行内容按 UI 视觉做续行对齐，但复制文本保持原样。
- Runtime 前置输入解析统一走 `RuntimeContext` raw resolver；字符串、布尔、坐标、函数参数和函数返回不要重复写各自的“上游没有输出”判断。
- 执行按钮在运行中会变成 `执行中...` 并禁用，避免重复点击。
- 鼠标拾取是 editor 工具，使用全局 mouse hook；只在坐标变化时采样/更新，浮窗和复制选择窗 clamp 到当前屏幕工作区；复制坐标/颜色后退出，取消继续；退出、窗口关闭、异常路径必须清 hook、DC、overlay。
- `多线程` 节点有动态 `exec_thread_N` 输出和特殊色 `exec_completed` 输出；运行时并行跑连接分支，全部完成后才走完成输出。鼠标/键盘/窗口类节点在并行分支中必须串行化，避免抢全局设备。
- 找图 Python 桥接用 `np.fromfile(...) + cv2.imdecode(...)`，不要再用 `cv2.imread(...)` 读中文路径。
- XAML 初始化期事件可能早于 controller 创建；事件入口要容忍 null。


## 2026-06-27：热键 / 托盘 / 工具栏

### 热键系统
- `ScriptHotkeyService` 用 WH_KEYBOARD_LL/WH_MOUSE_LL；WM_MOUSEWHEEL→WheelForward/WheelBackward。
- 每个绑定独立 `TriggerWindowMs`（默认1000ms），非全局共享。
- 热键窗体 `ScriptHotkeyCaptureWindow` 必须用 `_captured` bool 防 WPF 重入导致双重 DialogResult。
- 热键行 UI 格式：`按键[Badge] 修改 按下次数[] 清空`；未设置显示"无"。
- 所有属性控件有中文 ToolTip。

### 托盘
- `NotifyIcon` 在 `WindowLifecycle.cs`，图标 Resources/2.png。
- `Window_ClosingThemed` 处理关闭：首次三选后记忆；`_alwaysMinimizeToTray` 绕过后继弹窗。
- 退出必须 `Application.Current.Shutdown()` + `Environment.Exit(0)` 彻底杀进程。
- 右键只弹菜单，不恢复窗口；左键恢复。

### 工具栏执行状态
- `IsExecuting` 是 MainWindow DP；XAML DataTrigger 驱动按钮样式。
- `ExecutionController.ExecutionStateChanged` + `ScriptRunManager.RunningStateChanged` 合并更新。
- StopExecutionButton 调用 `_scriptRunManager.StopAll()` + `_executionController.Cancel()`（=Esc）。
- `SetRunButtonRunning/RestoreRunButton` 只发事件，不直接改按钮（防 Style 冲突）。
- 停止按钮必须终止两种执行路径（热键触发 + 工具栏触发）。

### 鼠标中键
- MouseButton 枚举有 Middle；XAML combo、VM、executor、Win32 adapter 全部支持。

### 提示音
- Console.Beep(800,150) 启动 / (400,300) 停止；只在热键路径播放。

### 日志
- Logger.Write() 先写文件+入队 UI，再进 capture scope；BeginCapture 不吞日志。

### 文件编码
- 所有 .cs/.xaml 必须是 UTF-8 without BOM。PowerShell `Set-Content` 在 PS 5.1 会写 ANSI 损坏中文。
- 用 `[System.IO.File]::WriteAllText(path, content, UTF8Encoding(false))` 写入。

## 验证门禁

```powershell
dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck
git diff --check -- AutomationStudio
codegraph.cmd sync
```

UI/XAML/启动路径改动追加短启动探测：

```powershell
dotnet run --project .\AutomationStudioWpf.csproj --no-build
```

完整历史和细节看 `TECHNICAL.md`；执行规则看项目 skill。
