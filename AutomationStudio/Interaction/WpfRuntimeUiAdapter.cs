using System.Windows;
using AutomationStudioWpf.Adapters;
using WpfApplication = System.Windows.Application;

namespace AutomationStudioWpf.Interaction;

/// <summary>
/// WPF bridge for runtime notifications. Runtime services only see interfaces.
/// </summary>
internal sealed class WpfRuntimeUiAdapter(Window owner) : IRuntimeUiAdapter, IUserNotificationSink
{
    private Window? ResolveOwner()
    {
        if (owner.Dispatcher.HasShutdownStarted || owner.Dispatcher.HasShutdownFinished)
            return null;

        return WpfApplication.Current?.Windows
                   .OfType<Window>()
                   .FirstOrDefault(window => window.IsActive)
               ?? owner;
    }

    public void ShowMessage(string message, string title)
    {
        try
        {
            owner.Dispatcher.Invoke(() =>
            {
                Window? dialogOwner = ResolveOwner();
                if (dialogOwner is not null)
                    ThemedDialog.Show(dialogOwner, message, title);
            });
        }
        catch (InvalidOperationException)
        {
            // Dispatcher is shutting down. Runtime must not crash because UI is gone.
        }
    }

    public void Notify(string title, string message, NotificationSeverity severity)
    {
        try
        {
            owner.Dispatcher.BeginInvoke(() =>
            {
                Window? dialogOwner = ResolveOwner();
                if (dialogOwner is null)
                    return;

                MessageBoxImage image = severity switch
                {
                    NotificationSeverity.Error => MessageBoxImage.Error,
                    NotificationSeverity.Warning => MessageBoxImage.Warning,
                    _ => MessageBoxImage.Information,
                };
                ThemedDialog.Show(dialogOwner, message, title, MessageBoxButton.OK, image);
            });
        }
        catch (InvalidOperationException)
        {
            // Dispatcher is shutting down. Service notifications are best effort.
        }
    }
}
