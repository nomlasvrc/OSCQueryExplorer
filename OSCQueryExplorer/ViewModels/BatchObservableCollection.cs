using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace OSCQueryExplorer.ViewModels;

public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    public void AppendRange(IEnumerable<T> items)
    {
        var changed = false;
        foreach (var item in items)
        {
            Items.Add(item);
            changed = true;
        }
        if (changed) NotifyReset();
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        NotifyReset();
    }

    public void RemoveWhile(Func<T, bool> predicate)
    {
        var changed = false;
        while (Items.Count > 0 && predicate(Items[0]))
        {
            Items.RemoveAt(0);
            changed = true;
        }
        if (changed) NotifyReset();
    }

    private void NotifyReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
