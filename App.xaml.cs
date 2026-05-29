using System.Windows;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var progress = new Progress<string>(msg => Logger.Info(msg));
        bool hasPython = await PythonAutoInstaller.EnsurePythonAsync(progress);

        if (!hasPython)
        {
            Logger.Warn("Python 环境不可用，找图功能将受限。");
        }

        if (MainWindow == null)
        {
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
    }
}
