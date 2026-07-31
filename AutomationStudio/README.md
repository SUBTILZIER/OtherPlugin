# AutomationStudio

## Release Stability Rules

- Release target is Windows x64, .NET 8 self-contained multi-file, not trimmed and not single-file.
- Bundled Python is the only Python used by a Release package. New Python request JSON files go to `%LocalAppData%\AutomationStudioWpf\Temp\PythonRequests`; the install directory is read-only.
- Internal Python starts suspended, is assigned to the Job Object, and is resumed only after ownership succeeds. Cancellation, timeout, failure, and shutdown kill the process tree and use bounded output draining.
- The `启动程序` node starts a user-owned external process. AutomationStudio does not close that process during normal exit or upgrade.
- Assets/settings use `%AppData%\AutomationStudioWpf` when writable, otherwise one process-wide `%LocalAppData%\AutomationStudioWpf\Data` fallback. Logs, crash reports, screenshots, and Python requests use LocalAppData.
- The installer sends `--shutdown-for-update`, checks exit code `0`, waits for the single-instance mutex and private Python processes to disappear, and aborts on timeout or user cancellation.
- Unsigned local packages must use `-UNSIGNED` in the installer filename and contain `signed=false` in `build-manifest.json`.
- `Packaging\verify-release.ps1` is a Git-tracked release gate. Host verification runs by default; a clean Windows 10/11 x64 VM is still required for Defender and offline-install validation.

Windows 10/11 x64 的可视化桌面自动化编辑器。使用节点、执行线和数据线编排脚本；支持脚本资产、函数库、多窗口编辑、全局热键、并行分支和 OpenCV 找图。

长期架构、踩坑和发布规则只维护在 [Agent/TECHNICAL.md](Agent/TECHNICAL.md)。开发前先读其“大纲 / 索引”，命中相关主题后再细读对应章节。

## 主要功能

- 节点式脚本：鼠标、键盘、组合键、窗口、进程、延迟、循环、条件、截图、找图、字符串/布尔运算。
- 脚本与函数库：脚本含主事件图、辅助事件图和私有函数；主图可调用辅助图自定义事件，函数库可公开函数供脚本调用。
- 多编辑窗口：主窗口标签页与独立窗口并存，每个 session 自持编辑 surface、图状态、Undo 和 dirty 状态。
- 多线程节点：并行执行动态分支；全部成功后执行“全部完成”。业务值 `False` 不等于执行失败。
- ToDo 跳转：在同一图内按“节点名 + 节点编号”定位执行目标。
- 全局热键：启用的脚本可配置启动/终止热键、按下次数和触发窗口；热键启动脚本只能由对应终止热键或顶部停止按钮停止。
- 脚本循环：按次数、运行到终止、按时长运行；支持禁止重复运行。
- 内容浏览器：文件夹、模糊搜索、多选、框选、复制粘贴、拖拽移动、重命名、`Ctrl+B` 定位。
- 执行日志：INFO/WARN/ERROR 过滤、结构化节点执行块、复制、LocalAppData 文件保留与容量清理。
- 暗色/亮色主题：应用级主题和强调色，主窗口、编辑器、弹窗、菜单、托盘同步更新。
- 鼠标拾取：全屏坐标和像素颜色拾取。
- 最终代码：显示当前图的只读伪代码执行逻辑。
- 执行前预检：即使图已编译，运行前仍阻止未知节点、重复 ID、坏连接和缺失函数/事件进入执行器。
- 旧宏数据兼容：旧宏资产/节点会被忽略；项目不再提供宏库功能。

## 基本使用

1. 在底部内容浏览器创建或打开脚本。
2. 在左侧选择事件图/函数，右键画布添加节点。
3. 连接执行引脚和数据引脚，在右侧细节面板编辑参数。
4. 点击“编译”编译当前资产全部图。
5. 点击“执行脚本”；未编译内容会先自动编译。
6. 手动调试可用 `Esc` 或顶部停止按钮取消。
7. 全局热键启动的脚本使用对应终止热键停止；`Esc` 不影响此类运行。

## 常用快捷键

| 快捷键 | 功能 |
|---|---|
| `Delete` | 删除选中节点/连线 |
| `Ctrl+C / Ctrl+V` | 复制/粘贴节点；日志焦点内复制文本 |
| `Ctrl+Z / Ctrl+Y` | Undo / Redo |
| `Ctrl+A` | 日志焦点内全选过滤结果 |
| `Ctrl+B` | 内容浏览器定位真实目录 |
| `F` | 当前图缩放到节点全览 |
| `Esc` | 取消连线、退出拾取、停止手动调试 |
| `Alt+单击连线` | 删除最近 backing connection |
| 双击连线 | 插入路由点 |
| 右键拖动 | 平移画布 |
| 滚轮 | 缩放画布 |

## 最终用户环境

- Windows 10 1809+ / Windows 11，x64。
- 正式安装包为 .NET 8 自包含多文件发布。
- 安装包内置隔离 Python 3.14.6、OpenCV、NumPy、Pillow。
- 用户无需安装 .NET、Python、pip，也无需联网下载找图依赖。
- 私有 Python 损坏时，找图功能会提示修复/重装；不会回退系统 Python。

## 数据目录

安装目录视为只读，不保存日志、缓存或用户资产。

| 数据 | 位置 |
|---|---|
| 资产库、应用设置 | `%AppData%\AutomationStudioWpf`；不可写时 `%LocalAppData%\AutomationStudioWpf\Data` |
| 日志 | `%LocalAppData%\AutomationStudioWpf\Logs` |
| 崩溃报告 | `%LocalAppData%\AutomationStudioWpf\CrashReports` |
| 自动截图缓存 | `%LocalAppData%\AutomationStudioWpf\Temp\Screenshots` |
| 私有 Python | 安装目录 `Runtime\Python`（只读） |

LocalAppData 不可写时，运行时诊断目录才回退到 `%TEMP%\AutomationStudioWpf`；安装目录永远不作为写入目录。卸载默认保留用户资产和设置。

## 开发

要求：

- .NET 8 SDK。
- Windows x64。
- 生成安装器时需要 Inno Setup 6；可执行
  `winget install --id JRSoftware.InnoSetup --exact --scope user` 安装当前用户版本。
- 正式公开发布需要代码签名证书。

```powershell
dotnet build .\AutomationStudioWpf.csproj
dotnet test .\Tests\AutomationStudio.CoreTests\AutomationStudio.CoreTests.csproj
dotnet run --project .\AutomationStudioWpf.csproj
```

`Tests/CodexSmoke` 仅本地使用，已被 Git 忽略，不得提交。

## 发布

正式发布：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\Packaging\build-release.ps1 `
  -Version 1.0.0 `
  -CertificateThumbprint <CERT_THUMBPRINT>
```

本地 unsigned staging/安装器测试必须显式声明：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\Packaging\build-release.ps1 `
  -Version 1.0.0 `
  -AllowUnsigned
```

- 工作树默认必须干净；本地临时验证可显式加 `-AllowDirty`。
- `build-release.ps1` 默认执行本机安装门禁；只有显式 `-SkipVerification` 才跳过。跳过后的 manifest 标记 `hostStatus=skipped`，产物不可视为正式可发布包。
- `-SkipInstaller` 只能与 `-SkipVerification` 同用，仅供临时检查 staging。
- 首次构建会按 `Packaging/vendor-manifest.json` 下载并校验固定 SHA256；缓存后可离线重复构建。
- 简体中文 Inno 语言文件已固定版本、SHA256 和许可证并随仓库维护；构建不依赖 Inno 可选语言目录。
- 输出位于 `Packaging/artifacts/release/<version>/`，该目录不提交 Git。
- `stage/` 是自包含多文件目录，`Runtime/Python` 是私有运行时，`symbols/` 单独保存 PDB。
- 正式构建不得使用 `-AllowUnsigned`；安装器和应用 EXE 必须签名。

本机门禁会生成 `verification/release-verification.json` 和 `.md`，真实验证私有 Python/import、正式 `find_image.py`、Python 取消清理、安装目录零写入、5 次单实例启动、安全退出、卸载和用户数据保留。存在已通过门禁的旧版本时，自动选择最高旧版本执行升级测试；没有时明确记录 `NotRun`。

干净 VM 最终门禁：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\Packaging\verify-release.ps1 `
  -ReleaseRoot .\Packaging\artifacts\release\<version> `
  -Mode Vm `
  -ConfirmDisposableVm `
  -RequireOffline `
  -RequireStandardUser `
  -RequireDefender
```

VM 必须是 Windows 10/11 x64 干净快照、标准用户、断网、无系统 Python、无预装 .NET Runtime。VM 报告未通过不得发布。

安装器：

- 当前用户安装到 `%LocalAppData%\Programs\AutomationStudio`，无需管理员权限。
- 安装向导使用内置简体中文语言文件。
- 同一 AppId 原位升级。
- 升级会通过 `--shutdown-for-update` 请求现有实例安全退出。
- 用户取消未保存资产确认或 60 秒内未退出时，安装中止，不覆盖运行文件。
- 不提供联网自动更新。

## 项目结构

```text
AutomationStudio/
├─ Adapters/          Win32、Python、截图等系统能力
├─ Agent/             唯一长期技术文档
├─ Controls/          每 session 的 EditorSurfaceControl
├─ Graph/             节点、pin、connection、文件模型
├─ Interaction/       编辑器与窗口交互 controller
├─ Logging/           UI/文件日志
├─ Nodes/             节点定义与 executor
├─ Packaging/         私有 Python、publish、Inno、供应链清单
├─ Python/            find_image.py
├─ Runtime/           图执行器与临时运行上下文
├─ Services/          编译、保存、环境、单实例、路径、崩溃保护
├─ Tests/             Git 跟踪的 CoreTests；CodexSmoke 保持本地-only
└─ Themes/            共享主题资源
```

## 必要验证

```powershell
dotnet build .\AutomationStudioWpf.csproj -o .\bin\CodexBuildCheck
dotnet build .\AutomationStudioWpf.csproj
dotnet test .\Tests\AutomationStudio.CoreTests\AutomationStudio.CoreTests.csproj
git diff --check -- AutomationStudio
codegraph.cmd sync
```

发布前还需在干净 Windows 10/11 x64 VM 验证：离线标准用户安装、中文用户名、只读安装目录、单实例、找图、热键、升级、卸载、残留进程、Defender、版本、SHA256 和签名状态。
