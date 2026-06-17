using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;

namespace AutomationStudioWpf.Interaction;

public sealed class DetachedEditorWindow : Window
{
    private static readonly SolidColorBrush WindowBackgroundBrush = FrozenBrush(17, 21, 26);
    private static readonly SolidColorBrush ToolbarBackgroundBrush = FrozenBrush(32, 36, 43);
    private static readonly SolidColorBrush ButtonPrimaryForegroundBrush = FrozenBrush(12, 16, 22);
    private static readonly SolidColorBrush ButtonForegroundBrush = FrozenBrush(232, 237, 245);
    private static readonly SolidColorBrush ButtonBackgroundBrush = FrozenBrush(36, 43, 53);
    private static readonly SolidColorBrush ButtonBorderBrush = FrozenBrush(79, 94, 116);
    private static readonly SolidColorBrush TitleForegroundBrush = FrozenBrush(232, 237, 245);

    private readonly EditorSessionViewModel _session;
    private readonly Action<EditorSessionViewModel> _activateRequested;
    private readonly Action<EditorSessionViewModel> _dockRequested;
    private readonly Action<EditorSessionViewModel> _closeRequested;
    private readonly Action<EditorSessionViewModel, MouseButtonEventArgs> _previewMouseDownRequested;
    private readonly ContentControl _editorHost = new()
    {
        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };
    private readonly TextBlock _titleText = new();
    private bool _closingFromOwner;

    public DetachedEditorWindow(
        EditorSessionViewModel session,
        Window owner,
        Action<EditorSessionViewModel> activateRequested,
        Action<EditorSessionViewModel> dockRequested,
        Action<EditorSessionViewModel> closeRequested,
        Action<EditorSessionViewModel, MouseButtonEventArgs> previewMouseDownRequested)
    {
        _session = session;
        _activateRequested = activateRequested;
        _dockRequested = dockRequested;
        _closeRequested = closeRequested;
        _previewMouseDownRequested = previewMouseDownRequested;

        Title = session.DisplayTitle;
        Width = Math.Max(760, session.Width);
        Height = Math.Max(480, session.Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = owner.Left + 90;
        Top = owner.Top + 90;
        Background = WindowBackgroundBrush;

        Content = CreateContent();
        PreviewMouseDown += DetachedEditorWindow_PreviewMouseDown;
        Closing += DetachedEditorWindow_Closing;
    }

    public void RefreshChrome()
    {
        Title = _session.DisplayTitle;
        _titleText.Text = _session.DisplayTitle;
    }

    public void SetEditorContent(UIElement editor)
    {
        if (ReferenceEquals(_editorHost.Content, editor))
        {
            RefreshChrome();
            return;
        }

        _editorHost.Content = editor;
        RefreshChrome();
    }

    public bool HasEditorContent(UIElement editor) => ReferenceEquals(_editorHost.Content, editor);

    public void ClearEditorContent(UIElement editor)
    {
        if (ReferenceEquals(_editorHost.Content, editor))
            _editorHost.Content = null;
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
        root.Children.Add(_editorHost);
        return root;
    }

    private UIElement CreateToolbar()
    {
        var bar = new DockPanel
        {
            Height = 34,
            Background = ToolbarBackgroundBrush,
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

        var activateButton = CreateButton("编辑此窗口");
        activateButton.Click += (_, _) => _activateRequested(_session);
        DockPanel.SetDock(activateButton, Dock.Right);
        bar.Children.Add(activateButton);

        _titleText.Foreground = TitleForegroundBrush;
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
        Foreground = ButtonForegroundBrush,
        Background = ButtonBackgroundBrush,
        BorderBrush = ButtonBorderBrush,
    };

    private void DetachedEditorWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closingFromOwner)
            return;

        e.Cancel = true;
        Dispatcher.BeginInvoke(new Action(() => _closeRequested(_session)));
    }

    private void DetachedEditorWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _previewMouseDownRequested(_session, e);
    }

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(WpfColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
