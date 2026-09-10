using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace AutomationStudioWpf.Controls;

/// <summary>
/// Direct layout splitter. It avoids the preview/adorner path used by the
/// default GridSplitter, which is unreliable with the editor's custom chrome.
/// </summary>
public sealed class LayoutSplitter : GridSplitter
{
    private Grid? _parentGrid;
    private double _startPosition;
    private double _startPreviousSize;
    private double _startNextSize;
    private bool _dragging;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!TryGetAdjacentDefinitions(out var previous, out var next, out var grid) || grid is null)
        {
            e.Handled = true;
            return;
        }

        _parentGrid = grid;
        _startPosition = GetAxisPosition(grid, e.GetPosition(grid));
        _startPreviousSize = GetActualSize(previous);
        _startNextSize = GetActualSize(next);
        _dragging = true;
        CaptureMouse();
        Focusable = false;
        e.Handled = true;
    }

    protected override void OnMouseMove(WpfMouseEventArgs e)
    {
        if (!_dragging || _parentGrid is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        if (!TryGetAdjacentDefinitions(out var previous, out var next, out _))
            return;

        var currentPosition = GetAxisPosition(_parentGrid, e.GetPosition(_parentGrid));
        var rawDelta = currentPosition - _startPosition;
        var minDelta = Math.Max(
            previous.MinWidthOrHeight() - _startPreviousSize,
            _startNextSize - next.MaxWidthOrHeight());
        var maxDelta = Math.Min(
            previous.MaxWidthOrHeight() - _startPreviousSize,
            _startNextSize - next.MinWidthOrHeight());
        var delta = Math.Clamp(rawDelta, minDelta, maxDelta);

        SetSize(previous, _startPreviousSize + delta);
        SetSize(next, _startNextSize - delta);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;

        var position = _parentGrid is null ? 0 : GetAxisPosition(_parentGrid, e.GetPosition(_parentGrid));
        var delta = position - _startPosition;
        _dragging = false;
        ReleaseMouseCapture();
        RaiseEvent(new DragCompletedEventArgs(
            ResizeDirection == GridResizeDirection.Columns ? delta : 0,
            ResizeDirection == GridResizeDirection.Rows ? delta : 0,
            false)
        {
            RoutedEvent = DragCompletedEvent,
        });
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(WpfMouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            RaiseEvent(new DragCompletedEventArgs(0, 0, true)
            {
                RoutedEvent = DragCompletedEvent,
            });
        }

        base.OnLostMouseCapture(e);
    }

    private bool TryGetAdjacentDefinitions(
        out DefinitionBase previous,
        out DefinitionBase next,
        out Grid? grid)
    {
        previous = null!;
        next = null!;
        grid = Parent as Grid;
        if (grid is null)
            return false;

        if (ResizeDirection == GridResizeDirection.Columns)
        {
            var index = Grid.GetColumn(this);
            if (index <= 0 || index >= grid.ColumnDefinitions.Count - 1)
                return false;

            previous = grid.ColumnDefinitions[index - 1];
            next = grid.ColumnDefinitions[index + 1];
            return true;
        }

        if (ResizeDirection == GridResizeDirection.Rows)
        {
            var index = Grid.GetRow(this);
            if (index <= 0 || index >= grid.RowDefinitions.Count - 1)
                return false;

            previous = grid.RowDefinitions[index - 1];
            next = grid.RowDefinitions[index + 1];
            return true;
        }

        return false;
    }

    private double GetAxisPosition(Grid grid, WpfPoint point) =>
        ResizeDirection == GridResizeDirection.Columns ? point.X : point.Y;

    private double GetActualSize(DefinitionBase definition) => definition switch
    {
        ColumnDefinition column => column.ActualWidth,
        RowDefinition row => row.ActualHeight,
        _ => 0,
    };

    private void SetSize(DefinitionBase definition, double size)
    {
        if (definition is ColumnDefinition column)
            column.Width = new GridLength(size);
        else if (definition is RowDefinition row)
            row.Height = new GridLength(size);
    }

}

internal static class LayoutDefinitionExtensions
{
    internal static double MinWidthOrHeight(this DefinitionBase definition) => definition switch
    {
        ColumnDefinition column => column.MinWidth,
        RowDefinition row => row.MinHeight,
        _ => 0,
    };

    internal static double MaxWidthOrHeight(this DefinitionBase definition) => definition switch
    {
        ColumnDefinition column => column.MaxWidth,
        RowDefinition row => row.MaxHeight,
        _ => double.PositiveInfinity,
    };
}
