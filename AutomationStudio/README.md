# AutomationStudioWpf

UE4 风格的 WPF 蓝图节点编辑器 — 用于桌面自动化脚本编排。

## 功能

- **节点式编程**: 拖拽节点，连线构建自动化流程
- **多种节点类型**: 鼠标点击/移动/双击、键盘/组合键、滚轮、延迟、找图(OpenCV)、条件分支、循环、窗口操作、截图、弹窗、找图等待、布尔/字符串逻辑、比较
- **Python 图像识别**: 通过 Python OpenCV `TM_CCOEFF_NORMED` 模板匹配找图
- **资产系统**: 脚本、函数库、宏库 — 支持公开到库、私有函数/宏、自定义事件
- **内容浏览器**: 文件夹树 + 瓦片视图，支持资产拖拽移动/复制到文件夹
- **多格式兼容**: 支持鼠标左键/右键/侧键、键盘按键、滚轮方向
- **日志系统**: 内嵌日志面板 + 独立日志窗口，分级过滤(INFO/WARN/ERROR)，自动文件持久化，支持复制
- **蓝图编辑器体验**: 框选、组拖动、复制粘贴、对齐、缩放平移、路由节点、边缘自动平移(EdgePan)、快捷键
- **自动环境检测**: 启动时自动检测 Python 环境，提供一键安装指引
- **执行前校验**: 检查节点可达性、参数缺失、连线唯一性、循环/坏图

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
| Ctrl+C | 复制选中节点 |
| Ctrl+V | 粘贴节点(到鼠标位置) |
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
- 资产拖拽到文件夹支持移动/复制

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
│   ├── *NodeViewModel.cs        # 各节点类型实现
│   ├── GraphTypes.cs            # 枚举定义
│   └── GraphFileModel.cs        # 文件模型
├── Runtime/                     # 执行引擎
│   ├── GraphRuntimeExecutor.cs  # 执行器 (Win32 API)
│   └── GraphExecutionModels.cs  # 运行时数据模型
├── Services/                    # 业务服务层
│   ├── GraphEditorService.cs    # 图谱编辑核心逻辑
│   ├── GraphLibraryService.cs   # 图谱/资产库持久化
│   ├── GraphCompileService.cs   # 编译同步与校验
│   ├── NodeSerializer.cs        # 节点序列化/反序列化
│   ├── NodeClipboardService.cs  # 复制粘贴服务
│   ├── NodeFactory.cs           # 节点工厂
│   └── PythonAutoInstaller.cs   # Python 环境检测与安装
├── Interaction/                 # 交互控制器
│   ├── ExecutionController.cs   # 执行、取消、校验、Python 检查
│   ├── GraphListController.cs   # 图谱列表、切换、删除、重命名
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
├── Tests/CodexSmoke/            # UI smoke 门禁
├── MainWindow.xaml(.cs)         # 主窗口
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
```

## 日志位置

日志文件保存在程序目录下的 `saved/log/` 文件夹中，按时间命名：
```
saved/log/Log_2026_05_28_22_11.txt
```

## 最近更新

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
- **修复**: 重复窗口问题

## 许可证

MIT License
