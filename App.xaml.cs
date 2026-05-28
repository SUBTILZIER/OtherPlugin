using System.Windows;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 创建进度报告
        var progress = new Progress<string>(msg =>
        {
            Logger.Info(msg);
        });

        // 检查并确保 Python 环境（阻塞等待）
        var hasPython = await PythonAutoInstaller.EnsurePythonAsync(progress);

        if (!hasPython)
        {
            Logger.Warn("Python 环境不可用，找图功能将受限");
        }

        // 检查是否已经有主窗口，避免重复创建
        if (MainWindow == null)
        {
            // 继续启动主窗口
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
    }
}
