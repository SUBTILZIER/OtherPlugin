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
- **PinKind**: Execution / Boolean / Vector2D
- 支持动态引脚位置计算

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
```

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
