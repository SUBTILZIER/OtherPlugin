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

## UI / 主题 / 日志

- 共享暗色资源在 `App.xaml`；不要在窗口/surface 重复结构色。
- 节点 header、pin、日志级别、编译按钮、弹窗常用 brush 使用静态冻结 brush。
- 日志面板是只读 `RichTextBox`；全局快捷键必须对 `TextBoxBase` 放行。日志过滤 RadioButton checked dot 必须可见。
- XAML 初始化期事件可能早于 controller 创建；事件入口要容忍 null。

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
