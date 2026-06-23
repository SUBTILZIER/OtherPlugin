using System.Runtime.InteropServices;
using AutomationStudioWpf.Logging;

namespace AutomationStudioWpf.Interaction;

internal static class ClipboardHelper
{
    public static bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
            catch (ExternalException)
            {
                Thread.Sleep(30);
            }
        }

        Logger.Warn("剪贴板被其它程序占用，复制失败，请稍后重试。");
        return false;
    }
}
