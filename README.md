# AutomationStudioWpf

UE4 风格的 WPF 蓝图节点编辑器 — 用于桌面自动化脚本编排。

## 功能

- **节点式编程**: 拖拽节点, 连线构建自动化流程
- **多种节点类型**: 鼠标点击/移动、键盘、滚轮、延迟、找图(OpenCV)、条件分支、循环
- **Python 图像识别**: 通过 Python OpenCV `TM_CCOEFF_NORMED` 模板匹配找图
- **多格式兼容**: 支持鼠标左键/右键/侧键、键盘按键、滚轮方向
- **日志系统**: 内嵌日志面板 + 独立日志窗口, 分级过滤(INFO/WARN/ERROR), 自动文件持久化
- **蓝图编辑器体验**: 框选、组拖动、复制粘贴、对齐、缩放平移、路由节点、快捷键

## 使用

1. 左侧工具箱添加节点
2. 拖拽输出引脚到输入引脚连线
3. 右侧属性面板编辑节点参数
4. 点击"执行图谱"运行
5. 按 Esc 停止执行

## 快捷键

| 快捷键 | 功能 |
|--------|------|
| Delete | 删除选中节点 |
| Ctrl+C | 复制选中节点 |
| Ctrl+V | 粘贴节点(到鼠标位置) |
| Q | 横向对齐(居中对齐Y) |
| Shift+Alt+S | 纵向对齐(居中对齐X) |
| Esc | 取消连线 / 停止执行 |
| Alt+点击连线 | 断开连接 |
| 双击连线 | 生成路由节点 |

## 环境

- .NET 8.0 WPF + Windows Forms
- Python 3.x (可选, 用于找图节点)
- OpenCV (pip install opencv-python)

## 项目结构

```
AutomationStudioWpf/
├── Graph/                       # 节点模型层
│   ├── NodeBaseViewModel.cs     # 抽象节点基类
│   ├── InputNodeBase.cs         # 输入类节点基类
│   ├── PinViewModel.cs          # 引脚模型
│   ├── ConnectionViewModel.cs   # 连线模型
│   ├── *NodeViewModel.cs        # 各节点类型实现
│   └── GraphTypes.cs            # 枚举定义
├── Runtime/                     # 执行引擎
│   ├── GraphRuntimeExecutor.cs  # 执行器 (Win32 API)
│   └── GraphExecutionModels.cs  # 运行时数据模型
├── Logging/                     # 日志模块
│   ├── Logger.cs                # 存储 + 文件写入
│   ├── LogEntry.cs              # 日志条目模型
│   ├── LogLevel.cs              # 级别枚举
│   └── LoggingModule.cs         # 过滤 + 着色
├── Python/                      # Python 脚本
│   └── find_image.py            # OpenCV 找图
├── MainWindow.xaml(.cs)         # 主窗口
├── LogWindow.xaml(.cs)          # 独立日志窗口
└── .claude/skills/              # 开发经验沉淀
```

## 开发

```bash
dotnet build
dotnet run
```
