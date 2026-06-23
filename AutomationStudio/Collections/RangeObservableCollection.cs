using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace AutomationStudioWpf.Collections;

public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotifications;

    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _suppressNotifications = true;
        try
        {
            Items.Clear();
            foreach (T item in items)
                Items.Add(item);
        }
        finally
        {
            _suppressNotifications = false;
        }

        NotifyReset();
    }

    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var addedItems = items.ToList();
        if (addedItems.Count == 0)
            return;

        int startIndex = Count;
        _suppressNotifications = true;
        try
        {
            foreach (T item in addedItems)
                Items.Add(item);
        }
        finally
        {
            _suppressNotifications = false;
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, addedItems, startIndex));
    }

    public void RemoveFirst(int count)
    {
        if (count <= 0 || Count == 0)
            return;

        int removeCount = Math.Min(count, Count);
        _suppressNotifications = true;
        try
        {
            for (int i = 0; i < removeCount; i++)
                Items.RemoveAt(0);
        }
        finally
        {
            _suppressNotifications = false;
        }

        NotifyReset();
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotifications)
            base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_suppressNotifications)
            base.OnPropertyChanged(e);
    }

    private void NotifyReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
