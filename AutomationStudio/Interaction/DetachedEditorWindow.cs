using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfButton = System.Windows.Controls.Button;

namespace AutomationStudioWpf.Interaction;

public sealed class DetachedEditorWindow : Window
{
    private readonly EditorSessionViewModel _session;
    private readonly Action<EditorSessionViewModel> _dockRequested;
    private readonly Action<EditorSessionViewModel> _closeRequested;
    private readonly Action<EditorSessionViewModel, MouseButtonEventArgs> _previewMouseDownRequested;
    private readonly ContentControl _editorHost = new()
    {
        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };
    private readonly TextBlock _titleText = new();
    private DockPanel _toolbar = null!;
    private WpfButton _dockButton = null!;
    private bool _closingFromOwner;

    public DetachedEditorWindow(
        EditorSessionViewModel session,
        Window owner,
        Action<EditorSessionViewModel> dockRequested,
        Action<EditorSessionViewModel> closeRequested,
        Action<EditorSessionViewModel, MouseButtonEventArgs> previewMouseDownRequested)
    {
        _session = session;
        _dockRequested = dockRequested;
        _closeRequested = closeRequested;
        _previewMouseDownRequested = previewMouseDownRequested;

        Title = session.DisplayTitle;
        Width = Math.Max(760, session.Width);
        Height = Math.Max(480, session.Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = owner.Left + 90;
        Top = owner.Top + 90;
        Icon = WindowIconHelper.AppIcon;

        Content = CreateContent();
        RefreshTheme();
        PreviewMouseDown += DetachedEditorWindow_PreviewMouseDown;
        Closing += DetachedEditorWindow_Closing;
    }

    public void RefreshChrome()
    {
        Title = _session.DisplayTitle;
        _titleText.Text = _session.DisplayTitle;
    }

    public void RefreshTheme()
    {
        ThemeResourceHelper.SetResource(this, BackgroundProperty, "EditorRootBackgroundBrush");
        ThemeResourceHelper.SetResource(_toolbar, DockPanel.BackgroundProperty, "EditorChromeBrush");
        ThemeResourceHelper.SetResource(_titleText, TextBlock.ForegroundProperty, "EditorTextBrightBrush");
        ThemeResourceHelper.SetResource(_dockButton, WpfButton.ForegroundProperty, "EditorTextBrightBrush");
        ThemeResourceHelper.SetResource(_dockButton, WpfButton.BackgroundProperty, "EditorToolbarGroupBrush");
        ThemeResourceHelper.SetResource(_dockButton, WpfButton.BorderBrushProperty, "EditorPanelBorderBrush");
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
        _toolbar = new DockPanel
        {
            Height = 34,
            LastChildFill = true,
        };

        _dockButton = CreateButton("停靠回主窗口");
        _dockButton.Click += (_, _) => _dockRequested(_session);
        DockPanel.SetDock(_dockButton, Dock.Right);
        _toolbar.Children.Add(_dockButton);

        _titleText.FontWeight = FontWeights.SemiBold;
        _titleText.VerticalAlignment = VerticalAlignment.Center;
        _titleText.Margin = new Thickness(10, 0, 0, 0);
        _titleText.Text = _session.DisplayTitle;
        _toolbar.Children.Add(_titleText);
        return _toolbar;
    }

    private static WpfButton CreateButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(4),
        Padding = new Thickness(8, 2, 8, 2),
        MinWidth = 92,
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
}
