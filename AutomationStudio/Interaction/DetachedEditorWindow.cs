using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;

namespace AutomationStudioWpf.Interaction;

public sealed class DetachedEditorWindow : Window
{
    private readonly EditorSessionViewModel _session;
    private readonly Action<EditorSessionViewModel> _activateRequested;
    private readonly Action<EditorSessionViewModel> _dockRequested;
    private readonly Action<EditorSessionViewModel> _closeRequested;
    private readonly Func<EditorSessionViewModel, UIElement> _previewFactory;
    private readonly ContentControl _editorHost = new();
    private readonly TextBlock _titleText = new();
    private bool _closingFromOwner;

    public DetachedEditorWindow(
        EditorSessionViewModel session,
        Window owner,
        Action<EditorSessionViewModel> activateRequested,
        Action<EditorSessionViewModel> dockRequested,
        Action<EditorSessionViewModel> closeRequested,
        Func<EditorSessionViewModel, UIElement> previewFactory)
    {
        _session = session;
        _activateRequested = activateRequested;
        _dockRequested = dockRequested;
        _closeRequested = closeRequested;
        _previewFactory = previewFactory;

        if (owner.IsVisible)
            Owner = owner;
        Title = session.DisplayTitle;
        Width = Math.Max(760, session.Width);
        Height = Math.Max(480, session.Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = owner.Left + 90;
        Top = owner.Top + 90;
        Background = new SolidColorBrush(WpfColor.FromRgb(17, 21, 26));

        Content = CreateContent();
        Activated += (_, _) => _activateRequested(_session);
        Closing += DetachedEditorWindow_Closing;
    }

    public void RefreshChrome()
    {
        Title = _session.DisplayTitle;
        _titleText.Text = _session.DisplayTitle;
    }

    public void SetEditorContent(UIElement editor)
    {
        _editorHost.Content = editor;
        RefreshChrome();
    }

    public void ClearEditorContent(UIElement editor)
    {
        if (ReferenceEquals(_editorHost.Content, editor))
            _editorHost.Content = _previewFactory(_session);
    }

    public void CloseFromOwner()
    {
        _closingFromOwner = true;
        Close();
    }

    private UIElement CreateContent()
    {
        var root = new DockPanel();
        root.Children.Add(CreateToolbar());
        DockPanel.SetDock(root.Children[0], Dock.Top);
        _editorHost.Content = _previewFactory(_session);
        root.Children.Add(_editorHost);
        return root;
    }

    private UIElement CreateToolbar()
    {
        var bar = new DockPanel
        {
            Height = 34,
            Background = new SolidColorBrush(WpfColor.FromRgb(32, 36, 43)),
            LastChildFill = true,
        };

        var closeButton = CreateButton("关闭窗口");
        closeButton.Click += (_, _) => _closeRequested(_session);
        DockPanel.SetDock(closeButton, Dock.Right);
        bar.Children.Add(closeButton);

        var dockButton = CreateButton("停靠回主窗口");
        dockButton.Click += (_, _) => _dockRequested(_session);
        DockPanel.SetDock(dockButton, Dock.Right);
        bar.Children.Add(dockButton);

        _titleText.Foreground = WpfBrushes.White;
        _titleText.FontWeight = FontWeights.SemiBold;
        _titleText.VerticalAlignment = VerticalAlignment.Center;
        _titleText.Margin = new Thickness(10, 0, 0, 0);
        _titleText.Text = _session.DisplayTitle;
        bar.Children.Add(_titleText);
        return bar;
    }

    private static WpfButton CreateButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(4),
        Padding = new Thickness(8, 2, 8, 2),
        MinWidth = 74,
    };

    private void DetachedEditorWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closingFromOwner)
            return;

        e.Cancel = true;
        Dispatcher.BeginInvoke(new Action(() => _closeRequested(_session)));
    }
}
