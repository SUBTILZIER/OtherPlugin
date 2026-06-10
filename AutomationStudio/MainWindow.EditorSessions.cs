using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationStudioWpf.Interaction;
using WpfBorder = System.Windows.Controls.Border;
using WpfButton = System.Windows.Controls.Button;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfPopup = System.Windows.Controls.Primitives.Popup;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private UIElement? _emptyEditorPanelDefaultChild;
    private EditorSessionViewModel? _lastMainEditorSession;

    private void EditorSessionTab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<WpfButton>(e.OriginalSource as DependencyObject) is not null)
            return;
        if (sender is not WpfBorder { DataContext: EditorSessionViewModel session })
            return;

        _draggedEditorSession = session;
        _editorSessionDragStart = e.GetPosition(this);
        _isEditorSessionDrag = false;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void EditorSessionTab_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_draggedEditorSession is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _editorSessionDragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(pos.Y - _editorSessionDragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var screenPos = PointToScreen(pos);
            bool willDetach = !IsScreenPointInsideEditorWindowBar(screenPos);
            _isEditorSessionDrag = true;
            StartEditorSessionDragPreview(_draggedEditorSession);
            UpdateEditorSessionDragPreview(pos, willDetach);
            e.Handled = true;
        }
    }

    private void EditorSessionTab_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element && element.IsMouseCaptured)
            element.ReleaseMouseCapture();
        StopEditorSessionDragPreview();

        var session = _draggedEditorSession;
        _draggedEditorSession = null;

        if (session is null)
            return;

        if (_isEditorSessionDrag)
        {
            var screenPos = PointToScreen(e.GetPosition(this));
            if (!IsScreenPointInsideEditorWindowBar(screenPos))
                DetachEditorSession(session, screenPos);
            else
                ActivateEditorSessionFromMainTab(session);
        }
        else
        {
            ActivateEditorSessionFromMainTab(session);
        }

        _isEditorSessionDrag = false;
        e.Handled = true;
    }

    private void EditorSessionCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { DataContext: EditorSessionViewModel session })
            CloseEditorSession(session);
        e.Handled = true;
    }

    private void EditorSessionCloseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetMenuSession(sender) is { } session)
            CloseEditorSession(session);
    }

    private void EditorSessionDetachMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetMenuSession(sender) is { } session)
            DetachEditorSession(session);
    }

    private void EditorSessionCloseAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CloseMainEditorSessions();
    }

    private void EditorSessionCloseRightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetMenuSession(sender) is { } session)
            CloseEditorSessionsToRight(session);
    }

    private void DetachEditorSession(EditorSessionViewModel session, WpfPoint? screenPos = null)
    {
        if (session.DetachedWindow is null)
        {
            session.DetachedWindow = new DetachedEditorWindow(
                session,
                this,
                ActivateEditorSessionFromDetachedWindow,
                DockEditorSessionToTab,
                CloseEditorSession,
                CreateDetachedEditorPreview);
        }

        if (session.DockMode != EditorDockMode.Detached && ReferenceEquals(session, _activeEditorSession))
            _lastMainEditorSession = _mainEditorSessions.LastOrDefault(item => !ReferenceEquals(item, session));

        session.DockMode = EditorDockMode.Detached;
        if (screenPos is { } pos)
        {
            session.DetachedWindow.Left = Math.Max(0, pos.X - session.DetachedWindow.Width / 2);
            session.DetachedWindow.Top = Math.Max(0, pos.Y - 18);
        }
        if (!session.DetachedWindow.IsVisible)
            session.DetachedWindow.Show();
        ActivateEditorSessionFromDetachedWindow(session);
        session.DetachedWindow.Activate();
        SetStatus($"已独立窗口：{session.ContentAsset.Name}");
    }

    private void DockEditorSessionToTab(EditorSessionViewModel session)
    {
        session.DockMode = EditorDockMode.Tab;
        if (ReferenceEquals(session.DetachedWindow, _editorGridOwnerWindow))
            MoveEditorGridHome();
        session.DetachedWindow?.CloseFromOwner();
        session.DetachedWindow = null;
        ActivateEditorSessionFromMainTab(session);
        SetStatus($"已停靠窗口：{session.ContentAsset.Name}");
    }

    private void ActivateEditorSessionFromMainTab(EditorSessionViewModel session)
    {
        _lastMainEditorSession = session;
        RestoreMainEditorDefaultPanel();
        ActivateEditorSession(session);
    }

    private void ActivateEditorSessionFromDetachedWindow(EditorSessionViewModel session)
    {
        ActivateEditorSession(session);
        ShowMainEditorPreviewWhileDetachedSessionIsEditing(session);
    }

    private Rect GetMainWindowScreenRect()
    {
        var topLeft = PointToScreen(new WpfPoint(0, 0));
        var bottomRight = PointToScreen(new WpfPoint(Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)));
        return new Rect(topLeft, bottomRight);
    }

    private bool IsScreenPointInsideEditorWindowBar(WpfPoint screenPos)
    {
        if (EditorWindowBar.ActualWidth <= 0 || EditorWindowBar.ActualHeight <= 0)
            return false;

        var topLeft = EditorWindowBar.PointToScreen(new WpfPoint(0, 0));
        var bottomRight = EditorWindowBar.PointToScreen(new WpfPoint(EditorWindowBar.ActualWidth, EditorWindowBar.ActualHeight));
        return new Rect(topLeft, bottomRight).Contains(screenPos);
    }

    private void StartEditorSessionDragPreview(EditorSessionViewModel session)
    {
        if (_editorSessionDragPreviewPopup is not null)
            return;

        var title = new TextBlock
        {
            Text = session.DisplayTitle,
            Foreground = System.Windows.Media.Brushes.White,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var hint = new TextBlock
        {
            Text = "拖离标签栏创建独立窗口",
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(157, 172, 192)),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(hint);

        _editorSessionDragPreviewPopup = new WpfPopup
        {
            PlacementTarget = this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint,
            AllowsTransparency = true,
            IsHitTestVisible = false,
            Child = new Border
            {
                Width = 220,
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(94, 162, 232)),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 28, 34, 42)),
                Child = stack,
            },
        };
        _editorSessionDragPreviewPopup.IsOpen = true;
    }

    private void UpdateEditorSessionDragPreview(WpfPoint position, bool willDetach)
    {
        if (_editorSessionDragPreviewPopup is null)
            return;

        _editorSessionDragPreviewPopup.HorizontalOffset = position.X + 18;
        _editorSessionDragPreviewPopup.VerticalOffset = position.Y + 18;
        if (_editorSessionDragPreviewPopup.Child is Border border &&
            border.Child is StackPanel stack &&
            stack.Children.Count > 1 &&
            stack.Children[1] is TextBlock hint)
        {
            hint.Text = willDetach ? "释放后成为独立窗口" : "留在标签栏内切换窗口";
            hint.Foreground = willDetach
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(111, 221, 140))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(157, 172, 192));
        }
    }

    private void StopEditorSessionDragPreview()
    {
        if (_editorSessionDragPreviewPopup is null)
            return;

        _editorSessionDragPreviewPopup.IsOpen = false;
        _editorSessionDragPreviewPopup = null;
    }

    private void ShowMainEditorPreviewWhileDetachedSessionIsEditing(EditorSessionViewModel detachedSession)
    {
        EnsureEmptyEditorPanelDefaultChild();

        var previewSession = _lastMainEditorSession;
        if (previewSession is null ||
            previewSession.DockMode == EditorDockMode.Detached ||
            ReferenceEquals(previewSession, detachedSession) ||
            !_editorSessions.Contains(previewSession))
        {
            previewSession = _mainEditorSessions.LastOrDefault(item => !ReferenceEquals(item, detachedSession));
        }

        EmptyEditorPanel.Child = previewSession is null
            ? CreateDetachedMainPreviewPlaceholder(detachedSession)
            : CreateDetachedEditorPreview(previewSession);
        EmptyEditorPanel.Visibility = Visibility.Visible;
    }

    private UIElement CreateDetachedMainPreviewPlaceholder(EditorSessionViewModel detachedSession) => new Border
    {
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 21, 26)),
        Child = new TextBlock
        {
            Text = $"{detachedSession.DisplayTitle} 正在独立窗口编辑。\n主窗口没有其它停靠编辑标签。",
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 170, 184)),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        },
    };

    private void RestoreMainEditorDefaultPanel()
    {
        EnsureEmptyEditorPanelDefaultChild();
        if (!ReferenceEquals(EmptyEditorPanel.Child, _emptyEditorPanelDefaultChild))
            EmptyEditorPanel.Child = _emptyEditorPanelDefaultChild;
        if (_activeEditorSession is not null)
            EmptyEditorPanel.Visibility = Visibility.Collapsed;
    }

    private void EnsureEmptyEditorPanelDefaultChild()
    {
        _emptyEditorPanelDefaultChild ??= EmptyEditorPanel.Child;
    }

    private static EditorSessionViewModel? GetMenuSession(object sender) =>
        sender is WpfMenuItem { DataContext: EditorSessionViewModel session } ? session : null;

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
